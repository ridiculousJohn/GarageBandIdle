using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    public sealed class SettingsUI
    {
        public VisualElement Root { get; }

        public SettingsUI(VisualElement root, Action openRoadies, Action close)
        {
            Root = root;
            ScreenHost.Require<Button>(root, "roadies-button").clicked += openRoadies;
            ScreenHost.Require<Button>(root, "settings-close").clicked += close;
        }

        public void Show() => Root.style.display = DisplayStyle.Flex;
        public void Hide() => Root.style.display = DisplayStyle.None;
    }
}
