using System.Collections.Generic;
using System.Globalization;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The threshold toast (design doc 12.11): a pill near the bottom of the
    // screen naming one effect a count threshold switched on - "<target> x<m>
    // <stat word>", "Tuesday Open Mic x2 speed" - for Seconds, fading in and
    // out. Crossings queue and show one after another. Host-owned like every
    // overlay; it reads no state and ignores picking, so it is never modal and
    // never in the way of a tap. Its clock is the element's own scheduler:
    // panel time, presentation and never a game read.
    public sealed class ToastUI
    {
        // How long one toast stands, fade in to fade out.
        public const float Seconds = 2f;

        // The fade at each end, inside Seconds.
        public const float FadeSeconds = 0.25f;

        public VisualElement Root { get; }

        // The effect on screen now, or null while nothing is.
        public ThresholdEffect Shown { get; private set; }

        // The effects waiting behind it, in the order they arrived.
        public IReadOnlyList<ThresholdEffect> Queued => queue;

        private readonly Label text;
        private readonly List<ThresholdEffect> queue = new();
        private IVisualElementScheduledItem timer;
        private float elapsed;

        // The first tick after a show reports the frame's stall as its delta,
        // as Reveal's first growth tick does; the toast's clock starts there.
        private bool ticked;

        public ToastUI(VisualElement root)
        {
            Root = root;
            text = ScreenHost.Require<Label>(root, "toast-text");
            Root.pickingMode = PickingMode.Ignore;
            text.pickingMode = PickingMode.Ignore;
            Root.style.display = DisplayStyle.None;
        }

        // A crossing's effect, shown at once when nothing is up and queued
        // behind the shown one otherwise.
        public void Enqueue(ThresholdEffect effect)
        {
            if (effect == null)
                return;
            queue.Add(effect);
            if (Shown == null)
                Next();
        }

        // Ends the shown toast at once and shows the next, as its last frame
        // does - for a headless test, which has no panel to run the clock.
        public void Advance() => Next();

        // Takes the toast down and drops the queue: a toast belongs to the
        // chapter it was raised in, so leaving it takes them with it.
        public void Clear()
        {
            queue.Clear();
            Stop();
            Shown = null;
            Root.style.display = DisplayStyle.None;
        }

        // "<target> x<multiplier> <stat word>", generated from the effect.
        public static string Text(ThresholdEffect effect) =>
            effect.target.displayName + " x" + Factor(effect.multiplier) + " " + StatWord(effect.stat);

        // The one mapping from a stat to the word the toast names it by: a
        // rate is speed (a bar's fill, a source's output), a yield is payout,
        // a cost is cost, and any other stat is called by its own name.
        public static string StatWord(string stat) => stat switch
        {
            Stat.Rate => "speed",
            Stat.Yield => "payout",
            Stat.Cost => "cost",
            _ => stat,
        };

        // A multiplier as an author writes one: "2", "1.5", "0.5", with the
        // number formatter's notation once it is too long to read that way.
        private static string Factor(BigNumber multiplier) =>
            multiplier < 1000000 && multiplier >= BigNumber.Zero
                ? multiplier.ToDouble().ToString("0.##", CultureInfo.InvariantCulture)
                : NumberFormatter.Format(multiplier);

        private void Next()
        {
            Stop();
            if (queue.Count == 0)
            {
                Shown = null;
                Root.style.display = DisplayStyle.None;
                return;
            }
            Shown = queue[0];
            queue.RemoveAt(0);
            text.text = Text(Shown);
            Root.style.display = DisplayStyle.Flex;
            Root.style.opacity = 0f;
            elapsed = 0f;
            ticked = false;
            timer = Root.schedule.Execute(Step).Every(0);
        }

        private void Step(TimerState state)
        {
            if (ticked)
                elapsed += state.deltaTime / 1000f;
            ticked = true;
            if (elapsed >= Seconds)
            {
                Next();
                return;
            }
            var fadeIn = elapsed / FadeSeconds;
            var fadeOut = (Seconds - elapsed) / FadeSeconds;
            Root.style.opacity = System.Math.Min(1f, System.Math.Min(fadeIn, fadeOut));
        }

        private void Stop()
        {
            timer?.Pause();
            timer = null;
            Root.style.opacity = StyleKeyword.Null;
        }
    }
}
