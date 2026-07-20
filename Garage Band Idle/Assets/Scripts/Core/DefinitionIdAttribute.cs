using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Marks a string field as a foreign key holding another definition asset's
    // Id. DefinitionIdDrawer (editor-only) renders the field as a dropdown of
    // the ids found on assets of the given type; at runtime the field is still
    // a plain string, so nothing about loading or lookups changes.
    [AttributeUsage(AttributeTargets.Field)]
    public class DefinitionIdAttribute : PropertyAttribute
    {
        public Type DefinitionType { get; }

        public DefinitionIdAttribute(Type definitionType)
        {
            DefinitionType = definitionType;
        }
    }
}
