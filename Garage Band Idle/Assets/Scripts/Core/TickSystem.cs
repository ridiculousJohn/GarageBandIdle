using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle
{
    // The segmented tick with its fixed economy phases (design doc 12.9). Pure
    // over its arguments, no clock read - the same reason AdvanceTimers and
    // BarSystem take timestamps. The session calls it inside a transaction; the
    // tick owns only the segments, and the sweep and the refresh are the
    // transaction's.
    public static class TickSystem
    {
        // Guarded like AdvanceTimers: a null chapter or nonpositive dt no-ops.
        // The config is fail-loud instead (requirement 7) - a bad ceiling is a
        // content or caller fault, not a state the tick can answer quietly.
        public static TickReport Tick(RootScopeState root, ChapterScopeState foregroundChapter,
                                      GameConfig config, double realSeconds, DateTime tickEndUtc)
        {
            GameConfig.Require(config);
            if (foregroundChapter == null || realSeconds <= 0)
                return new TickReport(0);

            // What the tick actually moved, for interpolation to read (12.11).
            // Every segment records into the ONE report, so a segmented tick
            // reports its whole dt's movement over Seconds = realSeconds.
            var report = new TickReport(realSeconds);
            var tickStartUtc = tickEndUtc.AddSeconds(-realSeconds);
            foreach (var (start, end) in Segments(root, foregroundChapter, tickStartUtc, tickEndUtc))
                RunSegment(root, foregroundChapter, config, start, end, report);
            PruneExpiredBuffs(root, foregroundChapter, tickEndUtc);
            return report;
        }

        // The consecutive segments of [startUtc, endUtc], cut at every boundary
        // inside it. The idle claim walks the paid window through this same
        // method, so neither consumer can divide a window the other way.
        public static IEnumerable<(DateTime start, DateTime end)> Segments(
            RootScopeState root, ChapterScopeState chapter, DateTime startUtc, DateTime endUtc)
        {
            if (startUtc >= endUtc)
                yield break;
            var segmentStartUtc = startUtc;
            foreach (var edge in Boundaries(root, chapter, startUtc, endUtc))
            {
                yield return (segmentStartUtc, edge);
                segmentStartUtc = edge;
            }
            yield return (segmentStartUtc, endUtc);
        }

        // game_speed, read off the chapter's own compiled plan - the owner-less,
        // currency-less query 12.2 describes, compiled at every chapter node
        // because the tick's origin is fixed in code - and CLAMPED here, at the
        // ONE place the stat is read: section 9 describes the caps but nothing
        // else enforces one - unclamped authoring could stall time (a x0
        // wildcard) or stack carriers past the ceiling. The floor of 1 also
        // forbids an authored slow-time mechanic; nothing designs one, and it is
        // one constant if that ever changes.
        public static double GameSpeed(GameContext ctx, ChapterScopeState chapter, GameConfig config) =>
            Math.Clamp(
                Producer.GetMultiplier(ctx, chapter.Link<CoordinatePlan>(GatherCompiler.GameSpeed)).ToDouble(),
                1, config.maxGameSpeed);

        // Housekeeping, and only that: BuffActive judges a record against the
        // asking context's time, so a missed prune changes no answer. Removal
        // waits for the tick's end because the record has to survive the tick
        // that crosses its expiry - it cuts that tick's own boundary (design
        // doc 9).
        private static void PruneExpiredBuffs(RootScopeState root, ChapterScopeState foregroundChapter,
                                              DateTime tickEndUtc)
        {
            Prune(root);
            Walk(foregroundChapter);

            void Walk(ScopeState node)
            {
                Prune(node);
                foreach (var child in node.Children)
                    Walk(child);
            }

            void Prune(ScopeState node) =>
                node.timedBuffs.RemoveAll(buff => buff == null || buff.expiresAtUtc <= tickEndUtc);
        }

        // Every expiry timestamp strictly inside the tick, sorted and
        // deduplicated (12.9): each running timed record in the foreground
        // subtree expires at tick start plus its remaining seconds, and every
        // timed buff in the swept set - root plus the subtree - at its own
        // stamp. Buffs contribute boundaries, the BuffActive condition reads
        // them, and the tick's end prunes the expired. An expiry AT an edge of
        // the tick is not a boundary - it would cut an empty segment.
        //
        // Why an event expiry is a boundary when handicaps ride on the record
        // existing: the latch. "A goal first met after expiry never latches"
        // holds at sub-tick precision only if the expiry is a segment edge -
        // the post-expiry segment's AdvanceTimers sees remainingSeconds already
        // zero and refuses, while the pre-expiry segment's latch-before-
        // decrement gives the boundary tie to the player.
        private static SortedSet<DateTime> Boundaries(RootScopeState root, ChapterScopeState foregroundChapter,
                                                      DateTime tickStartUtc, DateTime tickEndUtc)
        {
            var edges = new SortedSet<DateTime>();
            AdmitBuffs(root);
            Walk(foregroundChapter);
            return edges;

            void Walk(ScopeState node)
            {
                if (node is InteriorScopeState host && host.activeEvent != null
                    && host.activeEvent.remainingSeconds > 0)
                    Admit(tickStartUtc.AddSeconds(host.activeEvent.remainingSeconds));
                AdmitBuffs(node);
                foreach (var child in node.Children)
                    Walk(child);
            }

            void AdmitBuffs(ScopeState node)
            {
                foreach (var buff in node.timedBuffs)
                    if (buff != null)
                        Admit(buff.expiresAtUtc);
            }

            void Admit(DateTime edge)
            {
                if (edge > tickStartUtc && edge < tickEndUtc)
                    edges.Add(edge);
            }
        }

        // One segment's fixed phases (12.9), every read against the start-of-
        // segment snapshot: a multiplier or condition live at segment start
        // governs the whole segment, expiring only at its edge.
        private static void RunSegment(RootScopeState root, ChapterScopeState foregroundChapter,
                                       GameConfig config, DateTime segmentStartUtc, DateTime segmentEndUtc,
                                       TickReport report)
        {
            var realDt = (segmentEndUtc - segmentStartUtc).TotalSeconds;
            var liveCtx = new GameContext(foregroundChapter, segmentStartUtc);

            // effDt stays a double - ConsumeAndSettle and the timer decrement
            // take doubles, and the clamp inside GameSpeed bounds it.
            var effDt = realDt * GameSpeed(liveCtx, foregroundChapter, config);

            // Bar demand BEFORE the deposits, per the snapshot rule: resolving
            // it after would let this segment's own production open a bar's
            // gate and draw for the whole dt.
            var demand = BarSystem.ResolveDemand(foregroundChapter, segmentStartUtc);

            // Rate production, two-pass like FireProducer: EVERY amount sized
            // against pre-deposit state, then deposited at its pair's home -
            // which is what "definition order never changes production" costs.
            var pairs = Producer.RatePairs(foregroundChapter);
            var amounts = new List<BigNumber>(pairs.Count);
            foreach (var pair in pairs)
                amounts.Add(Producer.GetRate(liveCtx, pair.currency) * effDt);
            for (var i = 0; i < pairs.Count; i++)
                if (amounts[i] != BigNumber.Zero)
                {
                    liveCtx.Rebase(pairs[i].home).DepositResolved(pairs[i].currency.Id, amounts[i]);
                    report.RecordDeposit(pairs[i].home, pairs[i].currency.Id, amounts[i]);
                }

            // Consumption on scaled time, settlement stamped at the segment's
            // real end; then wall clocks burn real seconds - game_speed never
            // touches a timer.
            BarSystem.ConsumeAndSettle(demand, effDt, segmentEndUtc, report);
            EventSystem.AdvanceTimers(root, foregroundChapter, realDt, segmentEndUtc);
        }
    }
}
