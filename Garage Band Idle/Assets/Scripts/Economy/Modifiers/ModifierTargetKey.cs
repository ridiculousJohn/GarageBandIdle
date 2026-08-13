using System;
using RidiculousGaming.GarageBandIdle.Content;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The address a modifier applies to: a kind, plus the open designer id it
    // acts on. Split this way so the vocabulary stays closed where it is code
    // (ModifierTarget) and open where it is content (a generator id, a currency
    // id) - the same division every other system here follows.
    //
    // The qualifier is OPTIONAL, and an absent one means every member of the
    // kind's family that is in reach (design doc section 12, rule 11). That is
    // what lets "-99% cost for this tier" or "double all idle payouts" be pure
    // placement rather than an authored id list that a new currency or generator
    // would silently fall out of. It is not a second addressing mode: an
    // unqualified grant is stored under its own key and Covers decides which
    // specific targets it reaches, so there is exactly one rule and both the
    // composition and the change notification ask it.
    public readonly struct ModifierTargetKey : IEquatable<ModifierTargetKey>
    {
        public ModifierTarget Kind { get; }

        // empty means "every member of this kind in reach"
        public string Qualifier { get; }

        private ModifierTargetKey(ModifierTarget kind, string qualifier)
        {
            Kind = kind;
            Qualifier = qualifier ?? "";
        }

        // every member of the kind in reach
        public static ModifierTargetKey All(ModifierTarget kind) => new(kind, "");

        public static ModifierTargetKey Of(ModifierTarget kind, string qualifier) => new(kind, qualifier);

        public bool IsQualified => Qualifier.Length > 0;

        // Whether a modifier filed under THIS key reaches the given specific
        // target. An unqualified key covers every target of its kind; a
        // qualified one covers only itself. One implementation, because a
        // composition that unioned differently from a change notification would
        // let a row show a number the economy does not agree with.
        public bool Covers(ModifierTargetKey specific)
            => Kind == specific.Kind && (!IsQualified || Qualifier == specific.Qualifier);

        // The definition family a qualifier names, for the kinds whose qualifier
        // resolves against a content registry. Null means the kind takes no
        // resolvable id - IdleRate and IdleCap are per scope, and scopes are not
        // addressable content until the scope tree lands, so today they are
        // authorable only unqualified.
        //
        // One mapping, two consumers: validation resolves the id against this
        // family's registry, and the inspector builds its dropdown from the same
        // assets, so an authoring UI can never offer ids from a family boot
        // validation would then reject.
        public static Type QualifierDefinitionType(ModifierTarget kind)
            => kind switch
            {
                ModifierTarget.GeneratorOutput => typeof(GeneratorDefinition),
                ModifierTarget.CurrencyRate => typeof(CurrencyDefinition),
                ModifierTarget.CurrencyYield => typeof(CurrencyDefinition),
                ModifierTarget.BarFillRate => typeof(BarGroupDefinition),
                _ => null,
            };

        // Derived from the mapping above rather than restated, so a kind added
        // later cannot be known to one and not the other. A qualifier on a kind
        // with no family addresses nothing resolvable; ModifierSystem reports
        // that rather than storing it.
        public static bool AcceptsQualifier(ModifierTarget kind)
            => QualifierDefinitionType(kind) != null;

        public bool Equals(ModifierTargetKey other) => Kind == other.Kind && Qualifier == other.Qualifier;

        public override bool Equals(object obj) => obj is ModifierTargetKey other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Qualifier.GetHashCode();

        // the form every modifier error message names a target by
        public override string ToString() => Qualifier.Length == 0 ? $"{Kind}:*" : $"{Kind}:{Qualifier}";
    }
}
