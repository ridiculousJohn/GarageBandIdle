namespace RidiculousGaming.GarageBandIdle.Meta
{
    // The one place the code side names the Roadie currency (design doc 8):
    // the store's bundle grant and the allocation screen both read it. No
    // resolve, because Deposit takes the id and already throws on a currency
    // no scope on the chain homes.
    public static class Roadies
    {
        public const string CurrencyId = "roadies";

        // This class's entry on CodeReferences (12.12): the reference is a code
        // fact, so it states what it needs and the pass asks root's own list.
        public static void Validate(ValidationContext ctx)
        {
            if (ctx.RootScope.DeclaresCurrency(CurrencyId))
                return;
            ctx.AddError(ValidationCheck.UnresolvedReference,
                $"Roadies: root declares no currency '{CurrencyId}' - the store's bundle grant names it from code (12.12).");
        }
    }
}
