using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Marks a qualifier field holding another definition asset's Id, where WHICH
    // definition family depends on a sibling ModifierTarget field rather than
    // being fixed when the field is declared. That is exactly what a plain
    // [DefinitionId(typeof(X))] cannot express, and why generalizing the payload
    // classes into GrantModifierEffect would otherwise have cost the inspector
    // dropdown those classes each had.
    //
    // The drawer resolves the named sibling through
    // ModifierTargetKey.QualifierDefinitionType - the same mapping validation
    // uses - so the ids offered and the ids accepted are one decision. At runtime
    // the field is still a plain string.
    [AttributeUsage(AttributeTargets.Field)]
    public class ModifierQualifierIdAttribute : PropertyAttribute
    {
        // name of the ModifierTarget field on the same object, e.g. nameof(_target)
        public string TargetFieldName { get; }

        public ModifierQualifierIdAttribute(string targetFieldName)
        {
            TargetFieldName = targetFieldName;
        }
    }
}
