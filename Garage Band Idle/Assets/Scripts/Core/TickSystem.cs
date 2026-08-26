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
        public static void Tick(RootScopeState root, ChapterScopeState foregroundChapter,
                                GameConfig config, double realSeconds, DateTime tickEndUtc)
        {
            GameConfig.Require(config);
            if (foregroundChapter == null || realSeconds <= 0)
                return;

            var tickStartUtc = tickEndUtc.AddSeconds(-realSeconds);
            var segmentStartUtc = tickStartUtc;
            foreach (var edge in Boundaries(root, foregroundChapter, tickStartUtc, tickEndUtc))
            {
                RunSegment(root, foregroundChapter, config, segmentStartUtc, edge);
                segmentStartUtc = edge;
            }
            RunSegment(root, foregroundChapter, config, segmentStartUtc, tickEndUtc);
        }

        // Every expiry timestamp strictly inside the tick, sorted and
        // deduplicated (12.9): each running timed record in the foreground
        // subtree expires at tick start plus its remaining seconds, and every
        // timed buff in the swept set - root plus the subtree - at its own
        // stamp. Buffs contribute boundaries only; nothing reads or removes one
        // until the timedBuffs gather row lands. An expiry AT an edge of the
        // tick is not a boundary - it would cut an empty segment.
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
                                       GameConfig config, DateTime segmentStartUtc, DateTime segmentEndUtc)
        {
            var realDt = (segmentEndUtc - segmentStartUtc).TotalSeconds;
            var liveCtx = new GameContext(foregroundChapter, segmentStartUtc);

            // game_speed, gathered once and CLAMPED at the consumer: section 9
            // describes the caps but nothing else enforces one - unclamped
            // authoring could stall time (a x0 wildcard) or stack carriers past
            // the ceiling. The floor of 1 also forbids an authored slow-time
            // mechanic; nothing designs one, and it is one constant if that
            // ever changes. effDt stays a double - ConsumeAndSettle and the
            // timer decrement take doubles, and the clamp bounds it.
            var speed = Producer.GetMultiplier(liveCtx, null, null, Stat.GameSpeed).ToDouble();
            var effDt = realDt * Math.Clamp(speed, 1, config.maxGameSpeed);

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
                    liveCtx.Rebase(pairs[i].home).Deposit(pairs[i].currency.Id, amounts[i]);

            // Consumption on scaled time, settlement stamped at the segment's
            // real end; then wall clocks burn real seconds - game_speed never
            // touches a timer.
            BarSystem.ConsumeAndSettle(demand, effDt, segmentEndUtc);
            EventSystem.AdvanceTimers(root, foregroundChapter, realDt, segmentEndUtc);
        }
    }
}
