using System;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Widget kinds are a closed, code-defined set, so the controller for a
    // prefabId is a switch: the registry SO stays the home of the assets, code
    // stays the home of the behavior, and a new widget type is a UXML, a line
    // here, and a registry entry (design doc 12.11).
    public static class ModuleWidgetFactory
    {
        public static bool Answers(string prefabId) =>
            prefabId == "currency_line"
            || prefabId == "jam_button"
            || prefabId == "generator_list"
            || prefabId == "upgrade_list";

        // Unknown ids throw for the registry's reason (requirement 7): the id is
        // authored content, and the editor cross-check catches a miss before a
        // first render ever does.
        public static ModuleWidget Create(string prefabId, VisualElement root)
        {
            switch (prefabId)
            {
                case "currency_line":
                    return new CurrencyHeaderUI(root);
                case "jam_button":
                    return new JamButtonUI(root);
                case "generator_list":
                    return new GeneratorListUI(root);
                case "upgrade_list":
                    return new UpgradeListUI(root);
                default:
                    throw new InvalidOperationException(
                        $"No widget controller answers prefabId '{prefabId}' (design doc 12.11).");
            }
        }
    }
}
