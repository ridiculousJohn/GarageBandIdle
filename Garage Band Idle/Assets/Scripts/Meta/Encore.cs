using System;

namespace RidiculousGaming.GarageBandIdle.Meta
{
    // The one place the code side names the Encore modifier (design doc 9):
    // three classes read it and none owns it, so the id, its resolve, and its
    // existence check live together rather than being spelled out at each site.
    public static class Encore
    {
        public const string ModifierId = "encore";

        // The asset off root's OWN modifier list (12.14.8: one scope's
        // declaration list, no search). A miss throws: the validation pass
        // already refused content without it, so reaching null here is a code
        // or content fault (requirement 7).
        public static Economy.ModifierDefinition Modifier(RootScopeState root)
        {
            foreach (var modifier in root.Definition.modifiers)
                if (modifier != null && modifier.Id == ModifierId)
                    return modifier;
            throw new InvalidOperationException(
                $"Encore: root declares no modifier '{ModifierId}' - the validation pass refuses content without it (12.12).");
        }

        // This class's entry on CodeReferences (12.12): the reference is a code
        // fact, so it states what it needs and the pass asks root's own list.
        public static void Validate(ValidationContext ctx)
        {
            foreach (var modifier in ctx.RootScope.modifiers)
                if (modifier != null && modifier.Id == ModifierId)
                    return;
            ctx.AddError(ValidationCheck.UnresolvedReference,
                $"Encore: root declares no modifier '{ModifierId}' - the ad callback and the Encore pill name it from code (12.12).");
        }
    }
}
