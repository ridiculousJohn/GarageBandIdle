using System;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Marks a [SerializeReference] field for inspector authoring: the editor-only
    // SubclassPickerDrawer renders a concrete-type popup over the field's declared
    // base type (Condition, GameAction, PayoutFormula, ...). Lives on the field,
    // not the type, so a new polymorphic family needs no editor-tool change. At
    // runtime the field is untouched.
    [AttributeUsage(AttributeTargets.Field)]
    public class SubclassPickerAttribute : PropertyAttribute
    {
    }
}
