using System;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The address a modifier applies to: a kind, plus the open designer id it
    // acts on for the kinds that need one. Split this way so the vocabulary
    // stays closed where it is code (ModifierTarget) and open where it is
    // content (a generator id, a currency id) - the same division every other
    // system here follows.
    public readonly struct ModifierTargetKey : IEquatable<ModifierTargetKey>
    {
        public ModifierTarget Kind { get; }

        // empty for the global kinds; the generator or currency id otherwise
        public string Qualifier { get; }

        private ModifierTargetKey(ModifierTarget kind, string qualifier)
        {
            Kind = kind;
            Qualifier = qualifier ?? "";
        }

        public static ModifierTargetKey Global(ModifierTarget kind) => new(kind, "");

        public static ModifierTargetKey Of(ModifierTarget kind, string qualifier) => new(kind, qualifier);

        // The definition family a qualifier names, for the kinds that take one.
        // GeneratorOutput and CurrencyProduction name the thing they act on;
        // TapValue and FanRate ARE the whole system and get null.
        //
        // One mapping, two consumers: validation resolves the id against this
        // family's registry, and the inspector builds its dropdown from the same
        // assets, so an authoring UI can never offer ids from a family boot
        // validation would then reject.
        public static Type QualifierDefinitionType(ModifierTarget kind)
            => kind switch
            {
                ModifierTarget.GeneratorOutput => typeof(GeneratorDefinition),
                ModifierTarget.CurrencyProduction => typeof(CurrencyDefinition),
                _ => null,
            };

        // Derived from the mapping above rather than restated, so a qualified kind
        // added later cannot be known to one and not the other. A missing qualifier
        // addresses nothing and a qualifier on a global kind addresses nothing;
        // ModifierSystem reports either mistake rather than storing it.
        public static bool RequiresQualifier(ModifierTarget kind)
            => QualifierDefinitionType(kind) != null;

        public bool Equals(ModifierTargetKey other) => Kind == other.Kind && Qualifier == other.Qualifier;

        public override bool Equals(object obj) => obj is ModifierTargetKey other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Qualifier.GetHashCode();

        // the form every modifier error message names a target by
        public override string ToString() => Qualifier.Length == 0 ? Kind.ToString() : $"{Kind}:{Qualifier}";
    }
}
