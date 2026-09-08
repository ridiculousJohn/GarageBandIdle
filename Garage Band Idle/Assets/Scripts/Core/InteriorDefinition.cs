using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Every scope inside another one - chapters and tiers. Root is the sole
    // exclusion, which is what puts the rung here: a rung is the ladder step out
    // of a scope, and the root is what the ladder climbs toward.
    public abstract class InteriorDefinition : ScopeDefinition
    {
        // The album release (tier) or capstone (chapter). Null for scopes with
        // no rung. SerializeReference so "no rung" stays null instead of an
        // auto-created empty instance.
        [SerializeReference] public Rung rung;

        // The events this scope can host (design doc 12.3, 12.8). On the
        // interior class because root cannot host one: its handicaps would
        // gather into every chapter's walk, so the field does not exist there.
        public List<Events.EventDefinition> events = new();

        // The base answers for the common lists; events exist only here.
        internal override bool Declares(Definition definition) =>
            base.Declares(definition) || Holds(events, definition);

        // The two list kinds only an interior scope has, added to the base's
        // enumeration: the rung, and each event's three lists.
        public override IEnumerable<ActionListSite> ActionLists()
        {
            if (rung != null)
                yield return new ActionListSite(rung.actions, $"scope '{Id}' rung");

            foreach (var entry in base.ActionLists())
                yield return entry;

            foreach (var evt in events)
            {
                if (evt == null)
                    continue;
                yield return new ActionListSite(evt.onEntry, $"event '{evt.Id}' onEntry");
                yield return new ActionListSite(evt.rewards, $"event '{evt.Id}' rewards");
                yield return new ActionListSite(evt.onEnd, $"event '{evt.Id}' onEnd");
            }
        }
    }
}
