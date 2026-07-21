using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.EditorTools
{
    // Draws a [SubclassPicker] [SerializeReference] field as a concrete-type
    // popup plus the chosen instance's fields, so polymorphic families
    // (Condition, UpgradePayload, Debuff, ...) can be authored in the inspector —
    // Unity's default inspector can edit an existing instance but offers no way
    // to create one. The family comes from the field's declared base type and
    // its subclasses from TypeCache, so this drawer is family-agnostic: new
    // types and new families appear with no changes here. The instance's own
    // fields are drawn by iterating child properties (never PropertyField on
    // the reference itself, which would recurse back into this drawer).
    [CustomPropertyDrawer(typeof(SubclassPickerAttribute))]
    public class SubclassPickerDrawer : PropertyDrawer
    {
        private const string NoneLabel = "(none)";

        // subclass scans are cached per base type; the type set only changes on
        // domain reload, which resets statics anyway
        private static readonly Dictionary<Type, Type[]> ChoiceCache = new();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                // [SubclassPicker] on a non-reference field is an authoring
                // mistake; fall back to the default field rather than hide it
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            label = EditorGUI.BeginProperty(line, label, property);

            var choices = GetChoices(GetDeclaredBaseType(property));
            var options = new GUIContent[choices.Length + 1];
            options[0] = new GUIContent(NoneLabel);
            for (var i = 0; i < choices.Length; i++)
                options[i + 1] = new GUIContent(choices[i].Name);

            var currentType = property.managedReferenceValue?.GetType();
            var index = currentType == null ? 0 : Array.IndexOf(choices, currentType) + 1;

            var popupRect = EditorGUI.PrefixLabel(line, label);
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            var selected = EditorGUI.Popup(popupRect, index, options);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck() && selected != index)
            {
                // switching type discards the old instance's field values; the
                // inspector's undo covers accidents
                property.managedReferenceValue = selected == 0 ? null : Activator.CreateInstance(choices[selected - 1]);
                property.serializedObject.ApplyModifiedProperties();
            }
            EditorGUI.EndProperty();

            if (property.managedReferenceValue == null || property.hasMultipleDifferentValues)
                return;

            // the instance's own fields, indented under the popup
            var y = line.yMax + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.indentLevel++;
            foreach (var child in Children(property))
            {
                var height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), child, true);
                y += height + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.indentLevel--;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
                return EditorGUI.GetPropertyHeight(property, label, true);

            var height = EditorGUIUtility.singleLineHeight;
            if (property.managedReferenceValue == null || property.hasMultipleDifferentValues)
                return height;

            foreach (var child in Children(property))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);
            return height;
        }

        // direct children of the reference; NextVisible(false) skips nested
        // grandchildren, which their own PropertyField calls handle
        private static IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var iterator = property.Copy();
            var end = iterator.GetEndProperty();
            if (!iterator.NextVisible(true))
                yield break;

            while (!SerializedProperty.EqualContents(iterator, end))
            {
                yield return iterator;
                if (!iterator.NextVisible(false))
                    yield break;
            }
        }

        // the field's declared base type, e.g. Condition for a [SerializeReference]
        // Condition field — the popup offers that family's concrete subclasses.
        // Read from the property (not fieldInfo) so list elements resolve too.
        private static Type GetDeclaredBaseType(SerializedProperty property)
        {
            // managedReferenceFieldTypename is "<assembly> <full type name>"
            var typename = property.managedReferenceFieldTypename;
            var space = typename.IndexOf(' ');
            if (space < 0)
                return null;
            return Type.GetType($"{typename.Substring(space + 1)}, {typename.Substring(0, space)}");
        }

        private static Type[] GetChoices(Type baseType)
        {
            if (baseType == null)
                return Array.Empty<Type>();
            if (ChoiceCache.TryGetValue(baseType, out var cached))
                return cached;

            var types = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                types.Add(type);
            }
            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var result = types.ToArray();
            ChoiceCache[baseType] = result;
            return result;
        }
    }
}
