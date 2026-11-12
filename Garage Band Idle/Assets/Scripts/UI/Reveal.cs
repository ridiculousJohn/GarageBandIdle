using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // How a section or a module comes and goes (design doc 12.11): hidden at
    // once, shown at once, or revealed - the element fades in while its size
    // along the axis its container stacks on grows from nothing to what it
    // lays out at, so whatever follows it slides along to make room. The host
    // holds one per section and one per built module and says each pass
    // whether the element shows and whether a show may animate.
    //
    // A length of auto cannot be tweened, so a reveal first lays the element
    // out of flow and transparent for one layout pass, reads the size it took,
    // and then grows it in flow from zero - the size, the padding and the
    // margins on that axis, with the opacity - on the element's own scheduler:
    // panel time, presentation and never a game read. The element clips its
    // content while it grows, and its children keep their own size rather
    // than shrinking into the clip. Every inline value a reveal wrote is
    // cleared when it ends, so the element is left as it would be without
    // one, and a size its content took mid-reveal is the size it ends at.
    public sealed class Reveal
    {
        // How long a reveal runs, measure to full size.
        public const float Seconds = 0.3f;

        private enum Phase
        {
            Still,
            Measuring,
            Growing,
        }

        // The axis the element's container stacks on: Vertical grows the
        // height, Horizontal the width, the way DragScrollManipulator reads
        // the same value.
        private readonly ScrollViewMode axis;

        private Phase phase;
        private IVisualElementScheduledItem growth;
        private float elapsed;

        // What the measuring pass laid out, along the axis.
        private float size;
        private float paddingStart;
        private float paddingEnd;
        private float marginStart;
        private float marginEnd;

        public VisualElement Element { get; }

        // Whether the element is displayed: the last answer Show was given.
        public bool Shown { get; private set; }

        // Whether a reveal is under way, measuring or growing; false once it
        // ends, completes or is cancelled by a hide.
        public bool Revealing => phase != Phase.Still;

        // The element starts hidden; the host's first pass says whether it
        // shows.
        public Reveal(VisualElement element, ScrollViewMode axis)
        {
            Element = element;
            this.axis = axis;
            element.style.display = DisplayStyle.None;
        }

        // A hide is at once and cancels a reveal under way; a show is at once
        // unless `animate`, which reveals. A call repeating the shown state
        // changes nothing, so a reveal under way runs on through later passes.
        public void Show(bool visible, bool animate)
        {
            if (visible == Shown)
                return;
            Shown = visible;
            if (!visible)
            {
                End();
                Element.style.display = DisplayStyle.None;
                return;
            }
            Element.style.display = DisplayStyle.Flex;
            if (animate)
                Measure();
        }

        // Ends a reveal under way at the element's natural state, inline
        // values cleared, as the last frame of the growth does.
        public void CompleteReveal() => End();

        private bool Sideways => axis == ScrollViewMode.Horizontal;

        // Out of flow and transparent, so nothing around it moves while the
        // layout pass sizes it. Down the page the element spans its
        // container's content box between the two paddings, which is the
        // width it takes in flow; along a strip it takes its content's width.
        private void Measure()
        {
            phase = Phase.Measuring;
            var style = Element.style;
            style.opacity = 0f;
            style.position = Position.Absolute;
            if (!Sideways && Element.parent != null)
            {
                style.left = Element.parent.resolvedStyle.paddingLeft;
                style.right = Element.parent.resolvedStyle.paddingRight;
            }
            Element.RegisterCallback<GeometryChangedEvent>(OnMeasured);
        }

        private void OnMeasured(GeometryChangedEvent evt)
        {
            Element.UnregisterCallback<GeometryChangedEvent>(OnMeasured);
            var resolved = Element.resolvedStyle;
            size = Sideways ? Element.layout.width : Element.layout.height;
            paddingStart = Sideways ? resolved.paddingLeft : resolved.paddingTop;
            paddingEnd = Sideways ? resolved.paddingRight : resolved.paddingBottom;
            marginStart = Sideways ? resolved.marginLeft : resolved.marginTop;
            marginEnd = Sideways ? resolved.marginRight : resolved.marginBottom;

            var style = Element.style;
            style.position = StyleKeyword.Null;
            style.left = StyleKeyword.Null;
            style.right = StyleKeyword.Null;
            style.overflow = Overflow.Hidden;
            for (var i = 0; i < Element.hierarchy.childCount; i++)
                Element.hierarchy[i].style.flexShrink = 0f;

            phase = Phase.Growing;
            elapsed = 0f;
            ticked = false;
            Apply(0f);
            growth = Element.schedule.Execute(Grow).Every(0);
        }

        // The first tick after the measuring pass reports the layout stall
        // as its delta; counted, it would open the element most of the way in
        // a single frame. So the growth's clock starts at that tick.
        private bool ticked;

        private void Grow(TimerState timer)
        {
            if (ticked)
                elapsed += timer.deltaTime / 1000f;
            ticked = true;
            var progress = elapsed / Seconds;
            if (progress >= 1f)
            {
                End();
                return;
            }
            // Ease-out: fast at first, settling into place.
            var remaining = 1f - progress;
            Apply(1f - remaining * remaining * remaining);
        }

        // The element at `t` of its measured state, 0 to 1.
        private void Apply(float t)
        {
            Element.style.opacity = t;
            SetAxis(t * size, t * paddingStart, t * paddingEnd, t * marginStart, t * marginEnd);
        }

        private void SetAxis(StyleLength length, StyleLength padStart, StyleLength padEnd,
                             StyleLength marStart, StyleLength marEnd)
        {
            var style = Element.style;
            if (Sideways)
            {
                style.width = length;
                style.paddingLeft = padStart;
                style.paddingRight = padEnd;
                style.marginLeft = marStart;
                style.marginRight = marEnd;
            }
            else
            {
                style.height = length;
                style.paddingTop = padStart;
                style.paddingBottom = padEnd;
                style.marginTop = marStart;
                style.marginBottom = marEnd;
            }
        }

        // Back to still, every inline value a reveal writes cleared; the
        // display is the caller's.
        private void End()
        {
            if (phase == Phase.Still)
                return;
            Element.UnregisterCallback<GeometryChangedEvent>(OnMeasured);
            growth?.Pause();
            growth = null;
            if (phase == Phase.Growing)
                for (var i = 0; i < Element.hierarchy.childCount; i++)
                    Element.hierarchy[i].style.flexShrink = StyleKeyword.Null;
            phase = Phase.Still;

            var style = Element.style;
            style.opacity = StyleKeyword.Null;
            style.position = StyleKeyword.Null;
            style.left = StyleKeyword.Null;
            style.right = StyleKeyword.Null;
            style.overflow = StyleKeyword.Null;
            var none = new StyleLength(StyleKeyword.Null);
            SetAxis(none, none, none, none, none);
        }
    }
}
