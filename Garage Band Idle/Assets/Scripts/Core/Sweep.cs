using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Events;

namespace RidiculousGaming.GarageBandIdle
{
    // The trigger sweep (design doc 12.5), run inside every transaction, tick
    // and command alike. One pass over the swept set: latch met event goals,
    // collect the eligible triggers, then execute them - a fixed order that
    // makes "when did this fire" answerable from the authored tree alone.
    public static class Sweep
    {
        public static void Run(RootScopeState root, ChapterScopeState foregroundChapter, DateTime nowUtc)
        {
            // The swept set: root first - it never resets and is on every chain
            // - then the foreground chapter's subtree in tree order (parent
            // before child). Dormant siblings wait; a threshold crossed while
            // away fires on the first live sweep after switch-in (12.8).
            var swept = new List<ScopeState> { root };
            AddSubtree(foregroundChapter);

            // 1. Latch met event goals across the set before anything executes.
            // No action has run yet, so the sweep-start snapshot is just "now"
            // - and latching before collection is what lets a trigger gated on
            // the armed reward fire in the same pass the goal lands.
            foreach (var scope in swept)
                if (scope is InteriorScopeState host)
                    EventSystem.LatchGoal(host, nowUtc);

            // 2. Collect eligible triggers - condition holds, id not latched -
            // capturing each scope's payload identity. Nothing executes during
            // collection, which is what makes a trigger armed by an earlier
            // trigger in this pass wait for the next sweep. A null condition is
            // closed and never dereferenced; validation refuses it at load.
            var eligible = new List<(ScopeState scope, ScopeFacts facts, TriggerDefinition trigger)>();
            foreach (var scope in swept)
            {
                var ctx = new GameContext(scope, nowUtc);
                foreach (var trigger in scope.Definition.triggers)
                {
                    if (trigger == null || trigger.condition == null)
                        continue;
                    if (scope.firedTriggers.Contains(trigger.Id))
                        continue;
                    if (trigger.condition.Evaluate(ctx))
                        eligible.Add((scope, scope.facts, trigger));
                }
            }

            // 3. Execute in collection order. A swapped payload means a reset
            // invalidated the rest of that scope-life (12.5) - the same
            // identity test BarSystem settles by. Latch FIRST, so a
            // self-resetting list re-arms its own trigger for the new life.
            foreach (var (scope, facts, trigger) in eligible)
            {
                if (facts != scope.facts)
                    continue;
                scope.firedTriggers.Add(trigger.Id);
                var ctx = new GameContext(scope, nowUtc);
                foreach (var action in trigger.actions)
                    action?.Execute(ctx);
            }

            void AddSubtree(ScopeState node)
            {
                if (node == null)
                    return;
                swept.Add(node);
                foreach (var child in node.Children)
                    AddSubtree(child);
            }
        }
    }
}
