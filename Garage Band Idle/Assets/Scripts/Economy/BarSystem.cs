using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One drawing bar as the segment found it. The scope is the bar's declaring
    // one and the facts reference is that payload's identity, which is how a
    // reset mid-settlement invalidates the rest of that scope-life (12.3).
    // `filled` is the only field the draw writes.
    public class BarFill
    {
        public ScopeState scope;
        public ScopeFacts facts;
        public BarDefinition bar;

        // What the bar consumes and what one unit of fill costs in each, both
        // resolved in the start-of-segment snapshot the rate is (12.9): a
        // consumption factor read after the deposits would be this segment's
        // own production deciding the price it pays.
        public IReadOnlyList<(ConsumesPlan plan, BigNumber perUnit)> consumes =
            Array.Empty<(ConsumesPlan, BigNumber)>();

        public BigNumber rate = BigNumber.Zero;             // effective fill speed, units/sec
        public BigNumber progressBefore = BigNumber.Zero;   // what the crossing test compares against
        public BigNumber filled = BigNumber.Zero;

        // What one completion would pay each currency, sized in the same
        // start-of-segment snapshot as the rate (12.9): read after the deposits
        // or after the bar's own completions, it would be this segment's own
        // production sizing the income the report attributes to it. Empty on a
        // payment, which the report never reads.
        public IReadOnlyList<(CurrencyDefinition currency, ScopeState home, BigNumber amount)> payout =
            Array.Empty<(CurrencyDefinition, ScopeState, BigNumber)>();

        // A payment into the bar rather than the time-based draw (12.7). A paid
        // fill takes the bar's consumption for the progress it adds (Deposit),
        // but selection and availability gate the draw and not a paid fill, so
        // the settlement's entry gate does not ask them of one.
        public bool payment;
    }

    // A subtree's drawing bars for one segment, in settlement order: scopes
    // parent before child, then bars in declaration order (12.7).
    public class BarDemand
    {
        public readonly List<BarFill> bars = new();
    }

    // Bars are the only place in the economy that CONSUMES (design doc 12.7).
    // A bar fills at its own rate and pays a per-unit price in each currency its
    // consumes list names: it wants rate * dt, takes what the tightest entry
    // covers, and spends every entry for what it took. No totals, no shared
    // throughput, no proportional split - when a currency cannot cover its bars
    // they simply stall, which is correct feedback rather than unfairness, and
    // the amount delivered per second is the inflow either way. A yield paid
    // into a bar is that much of the bar running, so it pays the same price for
    // the progress it adds, through the same cover and spend (Deposit).
    //
    // The one thing that IS two calls: 12.9's phases straddle the draw. Rates,
    // prices and gates come from the start-of-segment snapshot taken BEFORE the
    // production deposits, while the balance is read live ("production before
    // consumption, so an empty currency fed at +1/sec serves a 1/sec bar demand
    // in the same tick"). Resolving rates after the deposits would let a
    // segment's own production open a bar's gate and draw for the whole dt,
    // which is the coupling the snapshot rule exists to forbid.
    //
    // Everything here is stateless over an explicit subtree root, exactly like
    // GetRate - and, like GetRate, it reads that node's compiled contributor
    // plan rather than rediscovering the bars every segment (12.2/12.14.8); the
    // tick that calls the two halves in phase order is step 7's.
    public static class BarSystem
    {
        // The segment's drawing bars, in the settlement order the subtree root's
        // contributor plan fixed when the tree was built - scopes parent before
        // child, then bars in declaration order. Mutates nothing.
        // `segmentStartUtc` is real time - it is what the condition reads are
        // judged against.
        public static BarDemand ResolveDemand(ScopeState subtreeRoot, DateTime segmentStartUtc)
        {
            var demand = new BarDemand();
            if (subtreeRoot == null)
                return demand;

            var subtreeCtx = new GameContext(subtreeRoot, segmentStartUtc);
            var planned = ContributorPlan.At(subtreeRoot).Bars;
            for (var i = 0; i < planned.Count; i++)
            {
                var entry = planned[i];
                var node = entry.Node;
                var ctx = new GameContext(node, segmentStartUtc);
                var progress = node.barProgress.TryGetValue(entry.Bar.Id, out var stored) ? stored : BigNumber.Zero;
                if (!Drawing(ctx, entry.Bar, progress))
                    continue;
                demand.bars.Add(new BarFill
                {
                    scope = node,
                    facts = node.facts,
                    bar = entry.Bar,
                    consumes = PerUnitCosts(ctx, entry.Consumes),
                    rate = FillRate(subtreeCtx, entry),
                    progressBefore = progress,
                    payout = CompletionPayout(ctx, entry.Bar),
                });
            }
            return demand;
        }

        // What one unit of fill costs in each consumed currency, judged in the
        // snapshot: the authored amount times the entry's own consumption
        // factor, stage 1 only (12.7). Zero is legal and means free. The draw
        // and a payment into the bar (Deposit) both price through this.
        private static List<(ConsumesPlan plan, BigNumber perUnit)> PerUnitCosts(
            GameContext barCtx, IReadOnlyList<ConsumesPlan> planned)
        {
            var costs = new List<(ConsumesPlan, BigNumber)>(planned.Count);
            for (var i = 0; i < planned.Count; i++)
            {
                var consumes = planned[i];
                costs.Add((consumes, consumes.Entry.amount * Producer.GetMultiplier(barCtx, consumes.Factor)));
            }
            return costs;
        }

        // A fill rate is an ordinary produced number, so it goes through the
        // multiplier gather - but STAGE 1 ONLY, which is what the bar's own
        // compiled plan is: owner the bar, no currency coordinate, stat rate,
        // with no currency stage. Stage 2 is "effects on this currency's total
        // production", and a bar consumes rather than produces: letting a
        // currency-total buff through would mean demo_tapes_income speeds the drain
        // on Rehearsal as well as its supply, which is not what either buff
        // means. Its own fill plus the rate entries paying it - those already
        // carry their own stage 1 and this bar's stage 2, and the bar is the one
        // asking, so they arrive at the draw rather than through a deposit.
        //
        // Clamped at zero. Every factor the gather can apply is validated
        // nonnegative and linear growth saturates, but that pass is dev-only,
        // and a negative rate here would turn a draw into a mint. The draw and
        // the idle offer share this, so the number a window computes is the
        // number the tick would have moved (section 9).
        public static BigNumber FillRate(GameContext subtreeCtx, BarPlan plan) =>
            FillRate(subtreeCtx, plan.Node, plan.Bar, plan.Rate);

        // The same resolution for one bar a caller holds rather than a planned
        // entry: the bar's home found outward from the acting scope, the plan
        // its home compiled for it, and the subtree the tick draws it in - the
        // chapter it resolves on, the foreground chapter whenever it draws at
        // all. A bar declared at root draws in no chapter, so its own home is
        // the subtree. Answers whether or not the bar is drawing.
        public static BigNumber FillRate(GameContext ctx, BarDefinition bar)
        {
            var home = Producer.DeclaringScope<ScopeState>(ctx.Scope, bar);
            var subtree = (ScopeState)home.Chapter() ?? home;
            return FillRate(ctx.Rebase(subtree), home, bar, home.Link<StatPlans>(bar).Rate);
        }

        private static BigNumber FillRate(GameContext subtreeCtx, ScopeState home, BarDefinition bar,
                                          CoordinatePlan ratePlan) =>
            BigNumber.Max(BigNumber.Zero,
                bar.fillRate * Producer.GetMultiplier(subtreeCtx.Rebase(home), ratePlan)
                + Producer.GetRate(subtreeCtx, bar));

        // What one completion of the bar would deposit to each currency if its
        // onComplete ran now, depositing nothing (12.7): a FireGeneratorYield
        // entry through the same resolution the firing commits
        // (Producer.ResolveGeneratorYield - the owned count and every
        // multiplier), an AddCurrency entry through the same AmountFor its
        // Execute deposits. Only currency targets are income, so a yield entry
        // naming a generator is left out (12.12 refuses a generator's yield
        // entry naming a bar); other kinds pay no currency.
        // Amounts to one (currency, home) are summed, in first-seen order, and
        // zeros are kept, so a level nobody bought still names what it pays.
        //
        // Every entry is sized against the one state the call sees. A list
        // whose later action reads what an earlier one deposited - a formula on
        // a balance the first entry paid - is where this and the execution
        // part; none of the authored completion lists is that shape.
        public static List<(CurrencyDefinition currency, ScopeState home, BigNumber amount)> CompletionPayout(
            GameContext ctx, BarDefinition bar)
        {
            var home = Producer.DeclaringScope<ScopeState>(ctx.Scope, bar);
            var homeCtx = ctx.Rebase(home);
            var payout = new List<(CurrencyDefinition currency, ScopeState home, BigNumber amount)>();
            foreach (var action in bar.onComplete)
            {
                switch (action)
                {
                    case FireGeneratorYield fire when fire.generator != null:
                    {
                        // The firing pays from the generator's declaring scope,
                        // so each currency's home is found outward from there.
                        var firing = Producer.DeclaringScope<ScopeState>(home, fire.generator);
                        foreach (var (target, amount) in Producer.ResolveGeneratorYield(homeCtx, fire.generator))
                            if (target is CurrencyDefinition currency)
                                Accumulate(payout, currency, Producer.DeclaringScope<ScopeState>(firing, currency), amount);
                        break;
                    }
                    case AddCurrency add:
                        foreach (var currency in add.currencies)
                            if (currency != null)
                                Accumulate(payout, currency, Producer.DeclaringScope<ScopeState>(home, currency),
                                           add.AmountFor(homeCtx, currency));
                        break;
                }
            }
            return payout;
        }

        // What the bar earns per REAL second while it runs: one completion's
        // payout times completions per real second, which is the fill speed in
        // units per game second, times game_speed, over fillAmount. Answers
        // whether or not the bar is drawing - "what this earns while it runs" -
        // and a bar that consumes answers from its nominal speed, since what
        // its consumed currency covers is the draw's question, not this one.
        // `gameSpeed` is TickSystem.GameSpeed over the chapter, the read the
        // tick scales the same fill by.
        public static List<(CurrencyDefinition currency, ScopeState home, BigNumber perSecond)> CompletionRate(
            GameContext ctx, BarDefinition bar, double gameSpeed)
        {
            var payout = CompletionPayout(ctx, bar);
            var rates = new List<(CurrencyDefinition currency, ScopeState home, BigNumber perSecond)>(payout.Count);
            // A nonpositive threshold is refused at load and never draws; here
            // it would be a division by it.
            var perSecond = bar.fillAmount > BigNumber.Zero
                ? FillRate(ctx, bar) * gameSpeed / bar.fillAmount
                : BigNumber.Zero;
            foreach (var (currency, home, amount) in payout)
                rates.Add((currency, home, amount * perSecond));
            return rates;
        }

        private static void Accumulate(List<(CurrencyDefinition currency, ScopeState home, BigNumber amount)> into,
                                       CurrencyDefinition currency, ScopeState home, BigNumber amount)
        {
            for (var i = 0; i < into.Count; i++)
            {
                if (into[i].currency != currency || into[i].home != home)
                    continue;
                into[i] = (currency, home, into[i].amount + amount);
                return;
            }
            into.Add((currency, home, amount));
        }

        // The demand-side test, judged once in the snapshot: the bar is on, it
        // is available, and it has somewhere to fill to. The fillAmount leg is
        // what keeps a malformed bar from drinking - settlement refuses to pay a
        // nonpositive threshold, so admitting it to the draw would spend
        // currency every segment forever and settle none of it. A non-repeating
        // bar fails the progress leg on its own when fillAmount is nonpositive;
        // a repeating one needs the explicit test. Public because the idle offer
        // admits a bar by the same answer the tick does (section 9).
        public static bool Drawing(GameContext ctx, BarDefinition bar, BigNumber progress)
        {
            if (!ctx.IsOn(bar))
                return false;
            if (bar.fillAmount <= BigNumber.Zero)
                return false;
            if (bar.repeatWhen == null && progress >= bar.fillAmount)
                return false;
            // A refused completion list excludes the bar from the WHOLE segment
            // (12.5): it draws nothing, its progress does not move, and it
            // resumes on the first segment after the refusal lifts. Whether it
            // would complete is not decidable before the fill math, which is why
            // the exclusion is decided before it. The ctx is the bar's declaring
            // node, which is where a completion runs.
            if (ActionList.Refuses(bar.onComplete, ctx) != null)
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
        //
        // The report takes the DRAW's realized numbers (12.11) and not the
        // settlement's: a completion is one-shot, and a slope measured with it
        // in would extrapolate the payout as if it repeated every second.
        public static void ConsumeAndSettle(BarDemand demand, double dtSeconds, DateTime settlementUtc,
                                            TickReport report)
        {
            if (demand == null || dtSeconds <= 0)
                return;
            Draw(demand, dtSeconds, settlementUtc, report);
            Settle(demand, settlementUtc);
        }

        // Each bar advances by its rate times dt, in the deterministic order the
        // snapshot fixed, through the one advance a payment also takes.
        private static void Draw(BarDemand demand, double dtSeconds, DateTime settlementUtc, TickReport report)
        {
            foreach (var entry in demand.bars)
            {
                if (entry.rate <= BigNumber.Zero || !Alive(entry))
                    continue;
                var fill = Advance(entry, entry.rate * dtSeconds, settlementUtc, report);
                if (fill <= BigNumber.Zero)
                    continue;

                entry.filled = fill;
                // Progress lands from the snapshot's pre-fill value, which is the
                // same number the crossing test compares against.
                entry.scope.barProgress[entry.bar.Id] = entry.progressBefore + fill;
                report.RecordFill(entry.scope, entry.bar.Id, fill);

                // The completions this fill is worth, as the income it earns
                // spread over the time it took (12.9): the snapshot's payout
                // times the fraction of a cycle filled. Drawing admitted only a
                // positive fillAmount, so the quotient is defined.
                foreach (var (currency, home, amount) in entry.payout)
                    if (amount != BigNumber.Zero)
                        report.RecordCompletionIncome(home, currency.Id, amount * fill / entry.bar.fillAmount);
            }
        }

        // The one way a bar moves forward (12.7), for the tick's time and for a
        // yield paid into it alike: the bar wants `want` units of fill and takes
        // what its tightest consumed entry covers, then spends every entry for
        // what it took. Returns the fill realized; nothing moves the progress
        // here, since where it lands is the caller's. A bar that consumes
        // nothing takes the whole want and spends nothing; a pool that is low
        // takes what it can; an empty one takes nothing. The balance is read
        // live, so an earlier bar's take is visible to a later one and an empty
        // currency stalls the rest - no totals to compute and nothing to divide.
        // The move is a SPEND: earnedTotals is untouched, because a bar's fill is
        // not income. `report` is the tick's, and null on a payment, which the
        // report does not read.
        private static BigNumber Advance(BarFill entry, BigNumber want, DateTime nowUtc, TickReport report)
        {
            var fill = want;
            foreach (var (plan, perUnit) in entry.consumes)
            {
                // A free entry covers unboundedly, and dividing by it would
                // be a division by zero rather than an unlimited cover.
                if (perUnit <= BigNumber.Zero)
                    continue;
                fill = BigNumber.Min(fill, Balance(plan, nowUtc) / perUnit);
            }
            if (fill <= BigNumber.Zero)
                return BigNumber.Zero;

            foreach (var (plan, perUnit) in entry.consumes)
            {
                if (perUnit <= BigNumber.Zero)
                    continue;
                // Clamped at the balance the cover came from: the quotient's
                // last digits can put the product a hair above it, and Spend
                // throws on a balance that does not cover the amount.
                var balance = Balance(plan, nowUtc);
                var take = BigNumber.Min(fill * perUnit, balance);
                new GameContext(plan.Home, nowUtc).Spend(plan.Entry.currency.Id, take);
                report?.RecordDraw(plan.Home, plan.Entry.currency.Id, take);
            }
            return fill;
        }

        // The consumed currency's balance at its own home, read live - the one
        // carve-out in the snapshot rule (12.9).
        private static BigNumber Balance(ConsumesPlan plan, DateTime nowUtc) =>
            new GameContext(plan.Home, nowUtc).GetBalance(plan.Entry.currency.Id);

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
                // settlement loop. The drawing test already kept this bar from
                // drinking; this is the second half of the same doubling TryBuy's
                // computed cost gets.
                if (bar.fillAmount <= BigNumber.Zero)
                {
                    Debug.LogError($"BarSystem: bar '{bar.Id}' has fillAmount {bar.fillAmount} - not settled.");
                    continue;
                }
                // Which of the three settlements this bar takes, asked at its
                // home the moment it completes (12.7): no condition is the bar
                // that fills once, a condition holding is the repeating one,
                // and a condition refusing is the manual team.
                if (bar.repeatWhen == null)
                    SettleOnce(entry, settlementUtc);
                else if (bar.repeatWhen.Evaluate(new GameContext(entry.scope, settlementUtc)))
                    SettleRepeating(entry, settlementUtc);
                else
                    SettleManual(entry, settlementUtc);
            }
        }

        // A non-repeating bar fires on the CROSSING its own fill made, detected
        // within the pass: the snapshot holds the pre-fill progress and the entry
        // holds what its fill added, so a bar already full when the segment
        // began was below nothing, and a save loaded at full progress never fires
        // because no fill crossed it - live progress is never the test. That
        // is what "no completed-set is stored" costs, and it is why filling and
        // settling are one call. A completed bar has nothing left to run, so it
        // leaves the active set of every group listing it as the manual team
        // does, freeing the slot for the next choice (12.7); before Execute, the
        // latch-first order the other settlements keep.
        private static void SettleOnce(BarFill entry, DateTime settlementUtc)
        {
            var bar = entry.bar;
            if (entry.progressBefore >= bar.fillAmount)
                return;
            if (entry.progressBefore + entry.filled < bar.fillAmount)
                return;
            Deselect(entry.scope, bar);
            Execute(entry, bar, settlementUtc);
        }

        // A repeating bar settles in segments (12.7): each pass re-reads state,
        // asks how many completions can run before anything the gates read
        // changes, settles that many at once, and goes again from the new
        // state - so a completion action that resets the host or flips
        // availability stops the run honestly at its boundary instead of
        // executing precomputed fires against a changed world. A gate that
        // would close after 10 fills clamps a run of 100 at 10, because the
        // condition is the segment boundary. Residual progress is retained;
        // the progress and the count are written before the actions run, for
        // the same reason the trigger latch is written first.
        //
        // `remaining` is the run's BOUND as well as its expected count. Nothing
        // adds progress during settlement, so no run can exceed it.
        private static void SettleRepeating(BarFill entry, DateTime settlementUtc)
        {
            var bar = entry.bar;
            var remaining = Crossings(bar, Progress(entry, bar));
            while (remaining > BigNumber.Zero)
            {
                if (!Alive(entry) || !Eligible(entry, bar, settlementUtc))
                    return;
                var ctx = new GameContext(entry.scope, settlementUtc);
                if (!bar.repeatWhen.Evaluate(ctx))
                    return;
                var progress = Progress(entry, bar);
                if (progress < bar.fillAmount)
                    return;

                var batch = Batch(entry, bar, ctx, remaining);
                // A run that settles one at a time and is counted past the
                // range a subtraction moves would never end; past that range
                // it is a count no loop of single completions can finish.
                if (remaining - batch == remaining)
                    throw new InvalidOperationException(
                        $"Bar '{bar.Id}' crossed {remaining} thresholds and settles one completion at a time - "
                        + "a run past the range one completion moves cannot be settled (12.7).");

                entry.scope.barProgress[bar.Id] = progress - bar.fillAmount * batch;
                Bump(entry, bar, batch);
                ActionList.RunTimes(bar.onComplete, ctx, batch);
                remaining -= batch;
            }
        }

        // How many of the remaining completions settle as one (12.7). A bar
        // with perFill entries goes one at a time, since each fill changes
        // what the next one pays, and so does a list some action of which does
        // not repeat as one (a reset, a rung, a recorded write). Otherwise the
        // batch runs to the nearest boundary its own gates report: what one
        // completion changes, asked of repeatWhen and of availableWhen, each
        // answering how many completions until its answer differs. A payment
        // does not ask availableWhen, as Eligible does not. The payout of the
        // batch is resolved once, from the state the batch starts in - the
        // start-of-segment snapshot rule of 12.9 applied to completions.
        private static BigNumber Batch(BarFill entry, BarDefinition bar, GameContext ctx, BigNumber remaining)
        {
            if (bar.perFill.Count > 0 || !ActionList.RepeatsAsOne(bar.onComplete))
                return BigNumber.One;
            var delta = new CompletionDelta();
            ActionList.Describe(bar.onComplete, ctx, delta);
            var batch = Bounded(remaining, bar.repeatWhen, ctx, delta);
            if (!entry.payment && bar.availableWhen != null)
                batch = Bounded(batch, bar.availableWhen, ctx, delta);
            return batch;
        }

        private static BigNumber Bounded(BigNumber batch, Condition gate, GameContext ctx, CompletionDelta delta) =>
            gate.StepsUntilChange(ctx, delta, out var steps)
                ? BigNumber.Min(batch, BigNumber.Max(steps, BigNumber.One))
                : batch;

        // The manual team: one cycle per selection (12.7). The crossing is the
        // one SettleOnce tests, so a bar already full when the segment began
        // fires nothing; then the progress returns to zero, the bar leaves the
        // active set of EVERY group listing it - the compiled membership is what
        // names them - and selecting it again is how the player runs it again.
        // Excess past the threshold is discarded - the team ran one cycle, not
        // a fraction of a second one. The fill count is bumped like a repeating
        // bar's: a present repeatWhen counts fills whichever way it evaluated,
        // since the cascade counts completions of a bar that can complete more
        // than once. Writes before Execute, the same latch-first discipline the
        // other two keep.
        private static void SettleManual(BarFill entry, DateTime settlementUtc)
        {
            var bar = entry.bar;
            if (entry.progressBefore >= bar.fillAmount)
                return;
            if (entry.progressBefore + entry.filled < bar.fillAmount)
                return;

            entry.scope.barProgress[bar.Id] = BigNumber.Zero;
            Deselect(entry.scope, bar);
            Bump(entry, bar, 1);
            Execute(entry, bar, settlementUtc);
        }

        // Out of every group listing the bar at its own declaring scope, which
        // is where both the groups and the active sets live (12.7).
        public static void Deselect(ScopeState node, BarDefinition bar)
        {
            var listing = node.Link<MembershipPlan>(GatherCompiler.Memberships).Listing(bar);
            for (var i = 0; i < listing.Count; i++)
                if (node.activeMembers.TryGetValue(listing[i].Id, out var active))
                    active.Remove(bar.Id);
        }

        // The settlement entry gate is asymmetric: the snapshot ADMITS a bar and
        // live state may only DISQUALIFY it. A repeating bar can sit at full
        // progress with its gate closed - its own onComplete flipped it last
        // segment and 12.7 retains the residual, and a save can load in that
        // state - so a live-only test would let this segment's deposits open the
        // gate and pay the whole backlog, the very coupling ResolveDemand closes.
        private static bool Eligible(BarFill entry, BarDefinition bar, DateTime nowUtc)
        {
            // Membership and availability gate the time-based draw, not a paid
            // fill (12.7): a bar paid directly settles its crossing whether or
            // not it was selected or available.
            if (entry.payment)
                return true;
            var ctx = new GameContext(entry.scope, nowUtc);
            if (!ctx.IsOn(bar))
                return false;
            return bar.availableWhen == null || bar.availableWhen.Evaluate(ctx);
        }

        // A yield paid into this bar (12.7) - a tap's: 12.12 refuses a
        // generator's yield entry naming a bar - is that much of the bar running: it
        // advances through the same Advance the draw takes, so it takes the
        // bar's consumption for the progress it adds, as far as the balances
        // cover, and a bar that consumes nothing takes the whole payment. The
        // bar then settles its own crossing at the moment it is paid, through
        // the settlement the tick runs, since the tick only ever settles the
        // bars its draw admitted and would read a bar filled between ticks as
        // one that fired earlier. Clamped at fillAmount for a non-repeating bar
        // (a tap cannot overshoot full) before the advance, so consumption is
        // charged only for the progress that fits; a repeating bar keeps the
        // excess and settles every threshold it crossed. Membership and
        // availability gate the time-based draw and do not gate a paid fill. A
        // refused completion list (12.5) leaves the payment undelivered and
        // spends nothing, exactly as the draw excludes that bar for the segment.
        public static void Deposit(GameContext ctx, BarDefinition bar, BigNumber amount)
        {
            if (amount < BigNumber.Zero)
                throw new InvalidOperationException(
                    $"Deposit of {amount} into bar '{bar.Id}': a payment is never negative.");

            var home = Producer.DeclaringScope<ScopeState>(ctx.Scope, bar);
            var homeCtx = new GameContext(home, ctx.NowUtc);
            if (ActionList.Refuses(bar.onComplete, homeCtx) != null)
                return;

            // Progress is monotonic until reset (12.7): a non-repeating bar at or
            // past full takes nothing, and one below full stops at the threshold.
            var progress = home.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;
            var moved = progress + amount;
            if (bar.repeatWhen == null && moved > bar.fillAmount)
                moved = BigNumber.Max(progress, bar.fillAmount);

            // One entry through the one advance and the one settlement, so a
            // payment's fill and crossing and a draw's are the same code (12.7).
            // Priced now, in the state the payment lands in: a payment has no
            // segment, so its snapshot is this moment.
            var entry = new BarFill
            {
                scope = home,
                facts = home.facts,
                bar = bar,
                consumes = PerUnitCosts(homeCtx, PlanAt(home, bar).Consumes),
                progressBefore = progress,
                payment = true,
            };
            var want = moved - progress;
            var fill = Advance(entry, want, ctx.NowUtc, null);
            if (fill <= BigNumber.Zero)
                return;

            // The whole want realized lands exactly where the clamp put it, so a
            // tap that fills a bar lands ON fillAmount and the crossing holds; a
            // want the balances cut short lands where the fill reached.
            if (fill < want)
                moved = progress + fill;
            home.barProgress[bar.Id] = moved;
            entry.filled = moved - progress;

            var demand = new BarDemand();
            demand.bars.Add(entry);
            Settle(demand, ctx.NowUtc);
        }

        // The bar's compiled plan, read off its home's own contributor plan:
        // a scope's plan lists the bars it declares, so the bar is found in the
        // one subtree the caller already holds, never searched for (12.14.8).
        private static BarPlan PlanAt(ScopeState home, BarDefinition bar)
        {
            var planned = ContributorPlan.At(home).Bars;
            for (var i = 0; i < planned.Count; i++)
                if (planned[i].Node == home && planned[i].Bar == bar)
                    return planned[i];
            throw new InvalidOperationException(
                $"Bar '{bar.Id}' has no compiled plan at its home '{home.ScopeId}' (12.7).");
        }

        // Fail-closed and all-or-nothing (12.7/12.11): a refusal changes nothing,
        // and there is no partial application. The caller is a widget bound to the
        // group, so it holds the assets; ids appear only where a FACT supplies
        // one. A group off this chain THROWS rather than answering no - that is a
        // content or caller fault, not a state the player's own choices produced.
        // The bar legs are the only per-kind ones: a bar that is unavailable or
        // done has nothing left to run, while every other kind is a thing the
        // player may switch on whenever the group has room.
        public static bool SetActiveMembers(GameContext ctx, GroupDefinition group,
                                            IReadOnlyList<Definition> members)
        {
            var declaring = Producer.DeclaringScope<ScopeState>(ctx.Scope, group);
            var declaringCtx = ctx.Rebase(declaring);
            if (members == null)
                return false;

            var chosen = new HashSet<string>();
            foreach (var member in members)
            {
                if (member == null || !group.members.Contains(member))
                    return false;
                if (member is BarDefinition bar)
                {
                    if (bar.availableWhen != null && !bar.availableWhen.Evaluate(declaringCtx))
                        return false;
                    var progress = declaring.barProgress.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;
                    if (bar.repeatWhen == null && progress >= bar.fillAmount)
                        return false;
                }
                chosen.Add(member.Id);
            }
            if (chosen.Count > group.maxActive)
                return false;

            declaring.activeMembers[group.Id] = chosen;
            return true;
        }

        // ---- fact reads and writes, all at the bar's declaring scope ----

        private static bool Alive(BarFill entry) => entry.facts == entry.scope.facts;

        private static BigNumber Progress(BarFill entry, BarDefinition bar) =>
            entry.scope.barProgress.TryGetValue(bar.Id, out var value) ? value : BigNumber.Zero;

        // The fill count is a BigNumber like every other count a run can grow
        // (12.3), so a run of any length is a number it holds.
        private static void Bump(BarFill entry, BarDefinition bar, BigNumber fires)
        {
            var count = entry.scope.fillCounts.TryGetValue(bar.Id, out var stored) ? stored : BigNumber.Zero;
            entry.scope.fillCounts[bar.Id] = count + fires;
        }

        // How many thresholds the progress crosses, the one arithmetic the
        // idle offer's completions and the tick's fires share (section 9).
        public static BigNumber Crossings(BarDefinition bar, BigNumber progress)
        {
            var fires = BigNumber.Floor(progress / bar.fillAmount);
            return fires <= BigNumber.Zero ? BigNumber.Zero : fires;
        }

        private static void Execute(BarFill entry, BarDefinition bar, DateTime settlementUtc) =>
            ActionList.Run(bar.onComplete, new GameContext(entry.scope, settlementUtc));
    }
}
