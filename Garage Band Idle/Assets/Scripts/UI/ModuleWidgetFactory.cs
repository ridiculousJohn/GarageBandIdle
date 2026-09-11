using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Widget kinds are a closed, code-defined set, so one table maps prefabId
    // to controller construction: the registry SO stays the home of the assets,
    // code stays the home of the behavior, and a new widget type is a UXML, a
    // line here, and a registry entry (design doc 12.11).
    public static class ModuleWidgetFactory
    {
        private static readonly Dictionary<string, Func<VisualElement, IStoryOpener, ModuleWidget>> Creators =
            new()
            {
                ["currency_line"] = (root, _) => new CurrencyHeaderUI(root),
                ["jam_button"] = (root, _) => new JamButtonUI(root),
                ["generator_list"] = (root, _) => new GeneratorListUI(root),
                ["upgrade_list"] = (root, _) => new UpgradeListUI(root),
                ["bar_group"] = (root, _) => new BarGroupUI(root),
                ["rung_button"] = (root, _) => new RungButtonUI(root),
                ["event_row"] = (root, _) => new EventUI(root),
                ["story_row"] = (root, stories) => new StoryRowUI(root, stories),
            };

        // The registry cross-check enumerates the same closed set Create uses,
        // so adding a controller cannot leave its UXML entry unchecked.
        public static IEnumerable<string> PrefabIds => Creators.Keys;

        public static bool Answers(string prefabId) =>
            prefabId != null && Creators.ContainsKey(prefabId);

        // Import and boot call this directly beside content validation. Only
        // this check needs the registry; ContentValidator and CodeReferences
        // remain concerned with composed content alone.
        public static ValidationReport Validate(ComposedContent content, ModuleRegistry registry)
        {
            var report = new ValidationReport();
            void Error(ValidationCheck check, string message) =>
                report.Add(ValidationSeverity.Error, check, message);

            if (registry == null)
            {
                Error(ValidationCheck.NullEntry, "ModuleRegistry is missing.");
                return report;
            }

            var layouts = new Dictionary<string, VisualTreeAsset>(StringComparer.Ordinal);
            if (registry.entries == null)
                Error(ValidationCheck.NullEntry, "ModuleRegistry entries are missing.");
            else foreach (var entry in registry.entries)
            {
                if (entry == null)
                {
                    Error(ValidationCheck.NullEntry, "ModuleRegistry contains a null entry.");
                    continue;
                }
                if (string.IsNullOrEmpty(entry.prefabId)
                    || !ModuleDefinition.PrefabIdGrammar.IsMatch(entry.prefabId))
                {
                    Error(ValidationCheck.UnresolvedReference, "ModuleRegistry contains an invalid prefabId.");
                    continue;
                }
                if (layouts.ContainsKey(entry.prefabId))
                    Error(ValidationCheck.DuplicateId, $"ModuleRegistry repeats prefabId '{entry.prefabId}'.");
                else
                    layouts.Add(entry.prefabId, entry.prefab);
                if (entry.prefab == null)
                    Error(ValidationCheck.NullEntry, $"ModuleRegistry entry '{entry.prefabId}' carries no VisualTreeAsset.");
                if (!Answers(entry.prefabId))
                    Error(ValidationCheck.UnresolvedReference, $"ModuleRegistry prefabId '{entry.prefabId}' has no widget controller in ModuleWidgetFactory.");
            }

            foreach (var id in PrefabIds)
                if (!layouts.TryGetValue(id, out var layout) || layout == null)
                    Error(ValidationCheck.UnresolvedReference, $"Widget controller '{id}' has no layout in ModuleRegistry.");

            foreach (var chapter in content.Chapters)
            {
                for (var i = 0; i < chapter.sections.Count; i++)
                {
                    var section = chapter.sections[i];
                    if (section == null)
                        continue;
                    for (var j = 0; j < section.modules.Count; j++)
                    {
                        var module = section.modules[j];
                        if (module == null || string.IsNullOrEmpty(module.prefabId)
                            || !ModuleDefinition.PrefabIdGrammar.IsMatch(module.prefabId))
                            continue;

                        var site = $"chapter '{chapter.Id}' sections[{i}] modules[{j}]";
                        if (!Answers(module.prefabId))
                            Error(ValidationCheck.UnresolvedReference,
                                $"{site}: prefabId '{module.prefabId}' has no widget controller in ModuleWidgetFactory.");
                        if (!layouts.TryGetValue(module.prefabId, out var prefab) || prefab == null)
                            Error(ValidationCheck.UnresolvedReference,
                                $"{site}: prefabId '{module.prefabId}' has no layout in ModuleRegistry.");
                    }
                }
            }
            return report;
        }

        // Unknown ids throw for the registry's reason (requirement 7): the id is
        // authored content, and the import/boot cross-check catches a miss before a
        // first render ever does.
        //
        // `stories` is the card's owner, which one widget kind needs and the
        // rest ignore: the host is the one caller and hands itself in (12.11).
        public static ModuleWidget Create(string prefabId, VisualElement root, IStoryOpener stories)
        {
            if (prefabId != null && Creators.TryGetValue(prefabId, out var create))
                return create(root, stories);
            throw new InvalidOperationException(
                $"No widget controller answers prefabId '{prefabId}' (design doc 12.11).");
        }
    }
}
