namespace RidiculousGaming.GarageBandIdle.Meta
{
    // The one place the code side names the Pass entitlement (design doc 9):
    // the session, the dialog and the store manager all read it, so the id and
    // its existence check live together rather than at each site.
    public static class BackstagePass
    {
        public const string EntitlementId = "backstage_pass";

        // Root's set is the whole answer (12.3): root is the top of every
        // chain, and the entitlements are root's alone.
        public static bool Owned(RootScopeState root) => root.entitlements.Contains(EntitlementId);

        // This class's entry on CodeReferences (12.12): the reference is a code
        // fact, so it states what it needs and the pass asks root's own list.
        public static void Validate(ValidationContext ctx)
        {
            if (ctx.RootScope is RootDefinition root && root.DeclaresEntitlement(EntitlementId))
                return;
            ctx.AddError(ValidationCheck.UnresolvedReference,
                $"BackstagePass: root declares no entitlement '{EntitlementId}' - the session and the store name it from code (12.12).");
        }
    }
}
