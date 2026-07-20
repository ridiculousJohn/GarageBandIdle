using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.EditorTools
{
    // Draws a [DefinitionId(typeof(X))] string field as a dropdown of the ids
    // found on X assets in the project. Authoring UX only — the serialized
    // value stays a plain string. A value that matches no asset is kept and
    // shown as a "missing" entry rather than silently rewritten.
    [CustomPropertyDrawer(typeof(DefinitionIdAttribute))]
    public class DefinitionIdDrawer : PropertyDrawer
    {
        private const string NoneLabel = "(none)";

        // asset scans are cached per definition type and invalidated whenever
        // project assets change
        private static readonly Dictionary<Type, string[]> IdCache = new();

        static DefinitionIdDrawer()
        {
            EditorApplication.projectChanged += IdCache.Clear;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var definitionType = ((DefinitionIdAttribute)attribute).DefinitionType;
            var ids = GetIds(definitionType);

            var options = new List<string> { NoneLabel };
            options.AddRange(ids);

            var current = property.stringValue;
            int index;
            var missingIndex = -1;
            if (string.IsNullOrEmpty(current))
            {
                index = 0;
            }
            else
            {
                index = options.IndexOf(current);
                if (index < 0)
                {
                    options.Add($"<missing: {current}>");
                    missingIndex = options.Count - 1;
                    index = missingIndex;
                }
            }

            var contents = new GUIContent[options.Count];
            for (var i = 0; i < options.Count; i++)
                contents[i] = new GUIContent(options[i]);

            label = EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            var selected = EditorGUI.Popup(position, label, index, contents);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck() && selected != missingIndex)
                property.stringValue = selected == 0 ? string.Empty : options[selected];
            EditorGUI.EndProperty();
        }

        private static string[] GetIds(Type definitionType)
        {
            if (IdCache.TryGetValue(definitionType, out var cached))
                return cached;

            var ids = new List<string>();
            var idProperty = definitionType.GetProperty("Id", typeof(string));
            if (idProperty == null)
            {
                Debug.LogError($"DefinitionIdDrawer: {definitionType.Name} has no public string Id property.");
            }
            else
            {
                foreach (var guid in AssetDatabase.FindAssets($"t:{definitionType.Name}"))
                {
                    var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), definitionType);
                    if (asset == null)
                        continue;

                    var id = (string)idProperty.GetValue(asset);
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                        ids.Add(id);
                }

                ids.Sort(StringComparer.Ordinal);
            }

            var result = ids.ToArray();
            IdCache[definitionType] = result;
            return result;
        }
    }
}
