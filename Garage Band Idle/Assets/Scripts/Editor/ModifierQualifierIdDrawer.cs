using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEditor;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.EditorTools
{
    // Draws a [ModifierQualifierId] string as a dropdown of the ids in whichever
    // definition family the sibling ModifierTarget names, resolved at draw time
    // through ModifierTargetKey.QualifierDefinitionType. Unity applies a field
    // attribute to each element, so a List<string> of qualifiers gets one popup
    // per entry, the same as the [DefinitionId] list it replaced.
    [CustomPropertyDrawer(typeof(ModifierQualifierIdAttribute))]
    public class ModifierQualifierIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var family = ResolveFamily(property);
            if (property.propertyType != SerializedPropertyType.String || family == null)
            {
                // A global target takes no qualifier, and a target that fails to
                // resolve tells us nothing about which family is legal here. Show the
                // raw string either way rather than a confidently wrong dropdown.
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            DefinitionIdDrawer.DrawIdPopup(position, property, label, family);
        }

        private Type ResolveFamily(SerializedProperty property)
        {
            var target = property.serializedObject.FindProperty(
                SiblingPath(property.propertyPath, ((ModifierQualifierIdAttribute)attribute).TargetFieldName));
            if (target == null || target.propertyType != SerializedPropertyType.Enum)
                return null;

            return ModifierTargetKey.QualifierDefinitionType((ModifierTarget)target.intValue);
        }

        // Kept pure so the path arithmetic is testable without an inspector, which
        // is the only part of this drawer that can be wrong in a way compilation
        // would not catch. The sibling lives on the object owning the qualifier
        // field, so drop the array element suffix and then the field name: a
        // SerializeReference element ("references.RefIds.Array.data[0].data
        // ._qualifiers.Array.data[2]") and a bare field both land on their owner.
        internal static string SiblingPath(string qualifierPath, string siblingFieldName)
        {
            if (string.IsNullOrEmpty(qualifierPath) || string.IsNullOrEmpty(siblingFieldName))
                return siblingFieldName;

            var fieldPath = qualifierPath;
            var arrayMarker = fieldPath.LastIndexOf(".Array.data[", StringComparison.Ordinal);
            if (arrayMarker >= 0)
                fieldPath = fieldPath.Substring(0, arrayMarker);

            var lastDot = fieldPath.LastIndexOf('.');
            return lastDot < 0 ? siblingFieldName : $"{fieldPath.Substring(0, lastDot)}.{siblingFieldName}";
        }
    }
}
