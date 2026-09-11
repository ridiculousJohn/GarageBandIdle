using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Story;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // One bound beat's row (design doc 10, 12.11): a button that opens the card,
    // and the gate's legs beneath it. Its state is runtime fact, never a widget
    // decision - the button is live while the beat is available or already read,
    // and while it is neither the legs are the goal readout. Nothing here
    // interpolates: a gate has no slope, and a latch is a fact rather than a
    // number.
    public sealed class StoryRowUI : ModuleWidget
    {
        private readonly Button open;
        private readonly VisualElement legs;

        // The host, behind the one method a row needs of it: the card is the
        // host's, and the row hands over the beat and the scope it belongs to.
        private readonly IStoryOpener stories;

        // The legs paired with the conditions they render, as the event row
        // pairs them: the gate's top-level list is fixed, so the labels are
        // built once and matched against the unmet set by identity.
        private readonly List<(Condition condition, Label label)> legViews = new();

        private StoryBeatDefinition beat;

        // The declaring scope, resolved once: a beat is declared on the chapter
        // and declaration is ownership (12.3), so that is where its gate is
        // judged and where its card opens.
        private ScopeState home;

        public StoryRowUI(VisualElement root, IStoryOpener stories) : base(root)
        {
            open = Require<Button>(root, "open", "StoryRow.uxml");
            legs = Require<VisualElement>(root, "legs", "StoryRow.uxml");
            // A row with nowhere to send the click would render a dead button,
            // and the host is the only caller (requirement 7).
            this.stories = stories ?? throw new ArgumentNullException(nameof(stories));
        }

        protected override void OnBound()
        {
            beat = (StoryBeatDefinition)Content;
            home = Producer.DeclaringScope<ScopeState>(Scope, beat);
            open.text = beat.displayName;
            open.clicked += () => stories.OpenStory(beat, home);
            foreach (var leg in GateFeedback.Legs(beat.availableWhen))
            {
                var label = new Label();
                label.AddToClassList("leg");
                legViews.Add((leg, label));
                legs.Add(label);
            }
        }

        public override void Refresh()
        {
            // The gate is authored at the declaring scope, so it is judged
            // there, exactly as an event's is judged at its host (12.4).
            var homeCtx = Context().Rebase(home);
            var seen = homeCtx.IsFlagSet(beat.seenFlag);
            var available = beat.IsAvailable(homeCtx);
            // Enabled is seen OR available (section 10): a reset can close the
            // gate again while the latch stays set further out, and a beat once
            // read stays rereadable.
            open.SetEnabled(seen || available);
            open.EnableInClassList("seen", seen);

            // The legs explain a button the gate has closed, so an open one and
            // a read one show none (12.11). Each unmet leg is judged on its own,
            // so the row names every one and not just the first the All would
            // have stopped at.
            var explains = !seen && !available;
            var unmet = explains ? GateFeedback.UnmetLegs(beat.availableWhen, homeCtx) : null;
            foreach (var (condition, label) in legViews)
            {
                var visible = explains && unmet.Contains(condition);
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                    label.text = GateFeedback.LegText(condition, homeCtx);
            }
        }
    }
}
