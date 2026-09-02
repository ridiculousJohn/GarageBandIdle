using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The prefabId-to-asset map (design doc 12.11). Hand-made like GameConfig,
    // never imported, and referenced from the scene, so the UXMLs load with the
    // scene as this asset's own dependency graph - no per-widget async, and a
    // widget can be instantiated synchronously the moment its module turns visible.
    [CreateAssetMenu(menuName = "Garage Band Idle/Module Registry")]
    public class ModuleRegistry : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string prefabId;

            // The UXML is the "prefab" of the UI Toolkit shape, so the authored
            // field keeps the name 12.11 gives it.
            public VisualTreeAsset prefab;
        }

        public List<Entry> entries = new();

        public IEnumerable<string> PrefabIds => entries.Select(e => e.prefabId);

        // Fail-loud in every build (requirement 7): static content cannot
        // legitimately be unresolvable, and a blank widget would hide the fault
        // until someone noticed a missing row on a screen.
        public VisualTreeAsset Resolve(string prefabId)
        {
            foreach (var entry in entries)
            {
                if (entry == null || entry.prefabId != prefabId)
                    continue;
                if (entry.prefab == null)
                    throw new InvalidOperationException(
                        $"ModuleRegistry entry '{prefabId}' carries no VisualTreeAsset (design doc 12.11).");
                return entry.prefab;
            }
            throw new InvalidOperationException(
                $"ModuleRegistry has no entry for prefabId '{prefabId}' (design doc 12.11).");
        }
    }
}
