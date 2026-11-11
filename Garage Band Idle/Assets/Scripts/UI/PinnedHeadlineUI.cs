using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The pinned headline (design doc 12.11): one overlay line, drawn above the
    // scrolling page and below the overlays, showing the same three texts as
    // the page's headline currency line. It sits exactly over that line while
    // the line is on screen, and stops beneath the top bar once the line
    // scrolls up past it, shrinking as it travels - so the chapter's main
    // number is always visible.
    //
    // An overlay and not the page's own line: UI Toolkit draws in hierarchy
    // order with no z-index, so a line inside the page cannot stay above the
    // cards that scroll over it. The page's line keeps its space, so the page
    // lays out the same whether or not it is mirrored, and carries
    // currency-pinned-source, which the stylesheet makes invisible.
    //
    // Placement is read off worldBound every frame: the page's layout and
    // scroll position are the truth, and following them needs no second copy
    // of either. The statics hold the arithmetic, so a headless test reaches
    // it with no layout.
    public sealed class PinnedHeadlineUI
    {
        public const float PinnedScale = 0.6f;

        // 0 while the source sits where it rests with the page unscrolled, 1
        // once it has reached the pin line or passed it, linear between.
        public static float Collapse(float restTop, float pinTop, float sourceTop)
        {
            var distance = restTop - pinTop;
            if (distance <= 0f)
                return 1f;
            return Mathf.Clamp01((restTop - sourceTop) / distance);
        }

        public static float Top(float pinTop, float sourceTop) => Mathf.Max(pinTop, sourceTop);

        public VisualElement Root { get; }

        // The widget root being mirrored, or null.
        public VisualElement Source { get; private set; }

        private readonly VisualElement topBar;
        private readonly ScrollView scroll;
        private readonly VisualElement backdrop;
        private readonly VisualElement line;
        private readonly Label nameLabel;
        private readonly Label valueLabel;
        private readonly Label rateLabel;

        // The source's own labels, resolved when it is followed.
        private Label sourceName;
        private Label sourceValue;
        private Label sourceRate;

        public PinnedHeadlineUI(VisualElement rootElement, VisualElement topBar, ScrollView scroll)
        {
            Root = rootElement;
            this.topBar = topBar;
            this.scroll = scroll;
            backdrop = ScreenHost.Require<VisualElement>(rootElement, "headline-backdrop");
            line = ScreenHost.Require<VisualElement>(rootElement, "headline-line");
            nameLabel = ScreenHost.Require<Label>(rootElement, "headline-name");
            valueLabel = ScreenHost.Require<Label>(rootElement, "headline-value");
            rateLabel = ScreenHost.Require<Label>(rootElement, "headline-rate");
            Root.style.display = DisplayStyle.None;
        }

        // The currency header root to mirror, or null for none. Following
        // another hands the class back, so only the followed line is hidden.
        public void Follow(VisualElement source)
        {
            if (source == Source)
                return;
            Source?.RemoveFromClassList("currency-pinned-source");
            Source = source;
            sourceName = null;
            sourceValue = null;
            sourceRate = null;
            if (source == null)
            {
                Root.style.display = DisplayStyle.None;
                return;
            }
            source.AddToClassList("currency-pinned-source");
            sourceName = SourceLabel(source, "name");
            sourceValue = SourceLabel(source, "value");
            sourceRate = SourceLabel(source, "rate");
        }

        public void Interpolate()
        {
            if (Source == null)
                return;
            nameLabel.text = sourceName.text;
            valueLabel.text = sourceValue.text;
            rateLabel.text = sourceRate.text;
            Root.style.display = DisplayStyle.Flex;

            // A headless test has no panel and no layout, so placement waits
            // for both.
            if (Root.panel == null || Root.parent == null)
                return;
            var sourceTop = Source.worldBound.y;
            var pinTop = topBar.worldBound.yMax;
            var originY = Root.parent.worldBound.y;
            var scrolled = scroll.scrollOffset.y;
            if (float.IsNaN(sourceTop) || float.IsNaN(pinTop) || float.IsNaN(originY) || float.IsNaN(scrolled))
                return;
            // A line the panel has not laid out yet has no height and no
            // position worth following - the frame after it is built or shown.
            // The headline stays down for that frame rather than sitting at the
            // panel's corner over the top bar.
            if (!(Source.worldBound.height > 0f) || !(topBar.worldBound.height > 0f))
            {
                Root.style.display = DisplayStyle.None;
                return;
            }
            var restTop = sourceTop + scrolled;
            var t = Collapse(restTop, pinTop, sourceTop);
            var top = Top(pinTop, sourceTop);

            var s = Mathf.Lerp(1f, PinnedScale, t);
            line.style.translate = new Translate(0, top - originY);
            line.style.scale = new Scale(new Vector3(s, s, 1f));
            // The strip of page colour that hides what scrolls beneath the
            // pinned line; it only matters as the line arrives.
            backdrop.style.top = pinTop - originY;
            backdrop.style.opacity = t * t * t;
        }

        // A followed header missing one of its lines is a content fault, named
        // here rather than left as a null to surface on the next frame.
        private static Label SourceLabel(VisualElement source, string name)
        {
            var label = source.Q<Label>(name);
            if (label == null)
                throw new InvalidOperationException(
                    $"The pinned headline's source has no Label named '{name}' (design doc 12.11).");
            return label;
        }
    }
}
