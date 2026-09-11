using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The story card (design doc 10, 12.11): the beat's title, its body, and one
    // button. It writes nothing and reads no state - which beat is showing and
    // when its latch is written are the host's answers, so there is no second
    // reader to keep in step. No interpolation either: a card is authored text,
    // fixed for as long as it is up.
    public sealed class StoryBeatUI
    {
        public VisualElement Root { get; }

        private readonly Label title;
        private readonly Label text;

        public StoryBeatUI(VisualElement root, Action onClose)
        {
            Root = root;
            title = ScreenHost.Require<Label>(root, "title");
            text = ScreenHost.Require<Label>(root, "text");
            var close = ScreenHost.Require<Button>(root, "close");
            close.clicked += onClose;
        }

        public void Show(Story.StoryBeatDefinition beat)
        {
            title.text = beat.displayName;
            text.text = beat.text;
            Root.style.display = DisplayStyle.Flex;
        }

        public void Hide() => Root.style.display = DisplayStyle.None;
    }
}
