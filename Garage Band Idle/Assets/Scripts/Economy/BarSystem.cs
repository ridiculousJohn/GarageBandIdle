using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One drawing bar as the segment found it. The scope is its group's - a bar's
    // home is where its group is declared - and the facts reference is that
    // payload's identity, which is how a reset mid-settlement invalidates the
    // rest of that scope-life (12.3). `filled` is the only field the draw writes.
    public class BarFill
    {
        public ScopeState scope;
        public ScopeFacts facts;
        public BarGroupDefinition group;    // the selection re-read needs its id
        public BarDefinition bar;
        public CurrencyDefinition pool;     // null means it fills from time alone
        public ScopeState poolHome;
        public BigNumber rate = BigNumber.Zero;             // effective fill speed, units/sec
        public BigNumber progressBefore = BigNumber.Zero;   // what the crossing test compares against
        public BigNumber filled = BigNumber.Zero;
    }

    // A subtree's drawing bars for one segment, in settlement order: scopes
    // parent before child, then groups, then bars, in declaration order (12.7).
    public class BarDemand
    {
        public readonly List<BarFill> bars = new();
    }

    // Bars are the only place in the economy that CONSUMES (design doc 12.7).
    // A bar drinks a currency at its own rate: it wants rate * dt and takes what
    // is there. No totals, no shared throughput, no proportional split - when a
    // pool cannot cover its bars they simply stall, which is correct feedback
    // rather than unfairness, and the amount delivered per second is the inflow
    // either way.
    //
    // The one thing that IS two calls: 12.9's phases straddle the draw. Rates and
    // gates come from the start-of-segment snapshot taken BEFORE the production
    // deposits, while the pool balance is read live ("production before
    // consumption, so an empty pool fed at +1/sec serves a 1/sec bar demand in
    // the same tick"). Resolving rates after the deposits would let a segment's
    // own production open a bar's gate and draw for the whole dt, which is the
    // coupling the snapshot rule exists to forbid.
    //
    // Everything here is stateless over an explicit subtree root, exactly like
    // GetRate; the tick that calls the two halves in phase order is step 7's.
    public static class BarSystem
    {
        // The segment's drawing bars. Walks the subtree in settlement order and
        // mutates nothing. `segmentStartUtc` is real time - it is what the
        // condition reads are judged against.
        public static BarDemand ResolveDemand(ScopeState subtreeRoot, DateTime segmentStartUtc)
        {
            var demand = new BarDemand();
            if (subtreeRoot != null)
                Walk(subtreeRoot);
            return demand;

            void Walk(ScopeState node)
            {
                var ctx = new GameContext(node, segmentStartUtc);
                foreach (var group in node.Definition.barGroups)
                {
                    if (group == null)
                        continue;
                    foreach (var bar in group.bars)
                    {
                        if (bar == null)
                            continue;
                        var progress = node.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;
                        if (!Drawing(node, ctx, group, bar, progress))
                            continue;
                        demand.bars.Add(new BarFill
                        {
                            scope = node,
                            facts = node.facts,
                            group = group,
                            bar = bar,
                            pool = bar.fillCurrency,
                            poolHome = bar.fillCurrency == null
                                ? null
                                : Producer.FindCurrencyHome(node, bar.fillCurrency),
                            rate = Rate(ctx, bar),
                            progressBefore = progress,
                        });
                    }
                }
                foreach (var child in node.Children)
                    Walk(child);
            }
        }

        // A fill rate is an ordinary produced number, so it goes through the
        // multiplier gather - but STAGE 1 ONLY. Stage 2 is "effects on this
        // currency's total production", and a bar consumes rather than produces:
        // letting a currency-total buff through would mean records_income speeds
        // the drain on Rehearsal as well as its supply, which is not what either
        // buff means. The bar's own currency is passed as the coordinate so an
        // effect may narrow to it; a bar that fills from time passes null, which
        // no narrowing effect matches.
        //
        // Clamped at zero. Every factor the gather can apply is validated
        // nonnegative and linear growth saturates, but that pass is dev-only,
        // and a negative rate here would turn a draw into a mint.
        private static BigNumber Rate(GameContext ctx, BarDefinition bar) =>
            BigNumber.Max(BigNumber.Zero,
                bar.fillRate * Producer.GetMultiplier(ctx, bar, bar.fillCurrency, Stat.Rate));

        // The demand-side test, judged once in the snapshot: the player selected
        // it, it is available, and it has somewhere to fill to. The fillAmount
        // leg is what keeps a malformed bar out of the pool - settlement refuses
        // to pay a nonpositive threshold, so admitting it to the draw would spend
        // currency every segment forever and settle none of it. A non-repeating
        // bar fails the progress leg on its own when fillAmount is nonpositive;
        // a repeating one needs the explicit test.
        private static bool Drawing(ScopeState node, GameContext ctx, BarGroupDefinition group,
                                    BarDefinition bar, BigNumber progress)
        {
            if (!node.activeBars.TryGetValue(group.Id, out var selected) || !selected.Contains(bar.Id))
                return false;
            if (bar.fillAmount <= BigNumber.Zero)
                return false;
            if (!bar.repeating && progress >= bar.fillAmount)
                return false;
            // A null gate on a bar is OPEN, the opposite of a purchase gate:
            // fail-closed binds entry points that create value out of a spend,
            // and a bar's availability is a selection filter (12.7).
            return bar.availableWhen == null || bar.availableWhen.Evaluate(ctx);
        }

        // The draw and the settlement, called AFTER the production phase.
        // `dtSeconds` is SCALED production time; `settlementUtc` is the segment's
        // real END boundary, which is what authored actions stamp - a completion
        // that resets the host re-stamps lastActiveUtc, and one that starts an
        // event writes a real expiry. Real elapsed is unrecoverable from a scaled
        // dt whenever game_speed is not 1, so the two arrive separately.
        public static void ConsumeAndSettle(BarDemand demand, double dtSeconds, DateTime settlementUtc)
        {
            if (demand == null || dtSeconds <= 0)
                return;
            Draw(demand, dtSeconds, settlementUtc);
            Settle(demand, settlementUtc);
        }

        // Each bar takes what it wants or what is left, in the deterministic
        // order the snapshot fixed. The balance is read live per bar, so an
        // earlier bar's draw is visible to a later one and an empty pool stalls
        // the rest - no totals to compute and nothing to divide. The move is a
        // SPEND: earnedTotals is untouched, because a bar's fill is not income.
        private static void Draw(BarDemand demand, double dtSeconds, DateTime settlementUtc)
        {
            foreach (var entry in demand.bars)
            {
                if (entry.rate <= BigNumber.Zero || !Alive(entry))
                    continue;
                var want = entry.rate * dtSeconds;
                if (entry.pool != null)
                {
                    var poolCtx = new GameContext(entry.poolHome, settlementUtc);
                    want = BigNumber.Min(want, poolCtx.GetBalance(entry.pool.Id));
                    if (want <= BigNumber.Zero)
                        continue;
                    poolCtx.Spend(entry.pool.Id, want);
                }
                entry.filled = want;
                // Progress lands from the snapshot's pre-fill value, which is the
                // same number the crossing test compares against.
                entry.scope.barProgress[entry.bar.Id] = entry.progressBefore + want;
            }
        }

        // Completions, after every bar in the subtree has filled, in one
        // deterministic order across the whole subtree (12.7). The snapshot
        // decides WHO settles; live state may only disqualify.
        private static void Settle(BarDemand demand, DateTime settlementUtc)
        {
            foreach (var entry in demand.bars)
            {
                // A reset during settlement invalidates the rest of that
                // scope-life (12.7/12.5), and reset is a payload swap - so
                // reference identity is the check, with no bookkeeping to keep in
                // sync. Bars homed elsewhere are unaffected.
                if (!Alive(entry))
                    continue;
                var bar = entry.bar;

                // Runtime backstop. Validation refuses a nonpositive fillAmount,
                // but that pass is dev-only, and here it is an unbounded
                // settlement loop. The drawing test already kept this bar out of
                // the pool; this is the second half of the same doubling TryBuy's
                // computed cost gets.
                if (bar.fillAmount <= BigNumber.Zero)
                {
                    Debug.LogError($"BarSystem: bar '{bar.Id}' has fillAmount {bar.fillAmount} - not settled.");
                    continue;
                }
                if (bar.repeating)
                    SettleRepeating(entry, settlementUtc);
                else
                    SettleOnce(entry, settlementUtc);
            }
        }

        // A non-repeating bar fires on the CROSSING, detected within the pass:
        // the snapshot holds the pre-fill progress, so a bar already full when the
        // segment began was below nothing, and a save loaded at full progress
        // never fires because no fill crossed it. That is what "no completed-set
        // is stored" costs, and it is why filling and settling are one call.
        private static void SettleOnce(BarFill entry, DateTime settlementUtc)
        {
            var bar = entry.bar;
            if (entry.progressBefore >= bar.fillAmount)
                return;
            if (Progress(entry, bar) < bar.fillAmount)
                return;
            Execute(entry, bar, settlementUtc);
        }

        // A repeating bar settles iteratively, re-reading state each iteration so
        // a completion action that resets the host or flips availability stops the
        // loop honestly instead of executing precomputed fires against a changed
        // world. Residual progress is retained; increment before execute, for the
        // same reason the trigger latch is written first.
        private static void SettleRepeating(BarFill entry, DateTime settlementUtc)
        {
            var bar = entry.bar;
            var progress = Progress(entry, bar);
            var fires = FireCount(entry, bar, progress);
            if (fires <= 0)
                return;

            // The arithmetic shortcut 12.7 sanctions: with no completion actions
            // nothing can change between fills, so the whole run is one
            // subtraction and one live check.
            if (!HasActions(bar))
            {
                if (!Eligible(entry, bar, settlementUtc))
                    return;
                entry.scope.barProgress[bar.Id] = progress - bar.fillAmount * fires;
                Bump(entry, bar, fires);
                return;
            }

            // `fires` is the loop's BOUND as well as its expected count. Nothing
            // adds progress during settlement, so no run can exceed it, and
            // bounding the loop is what keeps the subtraction out of the range
            // where it stops moving the value.
            for (var i = 0; i < fires; i++)
            {
                if (!Alive(entry) || !Eligible(entry, bar, settlementUtc))
                    return;
                progress = Progress(entry, bar);
                if (progress < bar.fillAmount)
                    return;
                entry.scope.barProgress[bar.Id] = progress - bar.fillAmount;
                Bump(entry, bar, 1);
                Execute(entry, bar, settlementUtc);
            }
        }

        // The settlement entry gate is asymmetric: the snapshot ADMITS a bar and
        // live state may only DISQUALIFY it. A repeating bar can sit at full
        // progress with its gate closed - its own onComplete flipped it last
        // segment and 12.7 retains the residual, and a save can load in that
        // state - so a live-only test would let this segment's deposits open the
        // gate and pay the whole backlog, the very coupling ResolveDemand closes.
        private static bool Eligible(BarFill entry, BarDefinition bar, DateTime nowUtc)
        {
            if (!entry.scope.activeBars.TryGetValue(entry.group.Id, out var selected) || !selected.Contains(bar.Id))
                return false;
            return bar.availableWhen == null
                || bar.availableWhen.Evaluate(new GameContext(entry.scope, nowUtc));
        }

        // Fail-closed and all-or-nothing (12.7/12.11): a refusal changes nothing,
        // and there is no partial application. The caller is a widget bound to the
        // group, so it holds the assets; ids appear only where a FACT supplies
        // one. A group off this chain THROWS rather than answering no - that is a
        // content or caller fault, not a state the player's own choices produced.
        // The foreground-subtree guard layers on in step 7.
        public static bool SetActiveBars(GameContext ctx, BarGroupDefinition group, IReadOnlyList<BarDefinition> bars)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, group);
            var declaringCtx = ctx.Rebase(declaring);
            if (bars == null)
                return false;

            var chosen = new HashSet<string>();
            foreach (var bar in bars)
            {
                if (bar == null || !group.bars.Contains(bar))
                    return false;
                if (bar.availableWhen != null && !bar.availableWhen.Evaluate(declaringCtx))
                    return false;
                var progress = declaring.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;
                if (!bar.repeating && progress >= bar.fillAmount)
                    return false;
                chosen.Add(bar.Id);
            }
            if (chosen.Count > group.maxActive)
                return false;

            declaring.activeBars[group.Id] = chosen;
            return true;
        }

        // ---- fact reads and writes, all at the group's declaring scope ----

        private static bool Alive(BarFill entry) => entry.facts == entry.scope.facts;

        private static BigNumber Progress(BarFill entry, BarDefinition bar) =>
            entry.scope.barProgress.TryGetValue(bar.Id, out var value) ? value : BigNumber.Zero;

        private static void Bump(BarFill entry, BarDefinition bar, int fires)
        {
            entry.scope.fillCounts.TryGetValue(bar.Id, out var count);
            entry.scope.fillCounts[bar.Id] = count + fires;
        }

        // How many thresholds the progress crosses. fillCounts is an int and 12.7
        // writes no overflow policy - no authored bar repeats at all yet - so a
        // run past the range throws, which is the answer this codebase gives
        // everywhere content reaches a state validation cannot describe.
        private static int FireCount(BarFill entry, BarDefinition bar, BigNumber progress)
        {
            var fires = BigNumber.Floor(progress / bar.fillAmount);
            if (fires <= BigNumber.Zero)
                return 0;
            entry.scope.fillCounts.TryGetValue(bar.Id, out var count);
            if (fires > int.MaxValue - count)
                throw new InvalidOperationException(
                    $"Bar '{bar.Id}' crossed {fires} thresholds in one segment with {count} already recorded - fillCounts cannot hold the result.");
            return (int)fires.ToDouble();
        }

        // An all-null completion list is an authoring fault the validator
        // reports, but it cannot affect anything, so the shortcut still applies.
        private static bool HasActions(BarDefinition bar)
        {
            foreach (var action in bar.onComplete)
                if (action != null)
                    return true;
            return false;
        }

        private static void Execute(BarFill entry, BarDefinition bar, DateTime settlementUtc)
        {
            var ctx = new GameContext(entry.scope, settlementUtc);
            foreach (var action in bar.onComplete)
                action?.Execute(ctx);
        }
    }
}
