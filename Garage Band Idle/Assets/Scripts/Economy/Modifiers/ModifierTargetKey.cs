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

        // GeneratorOutput and CurrencyProduction name the thing they act on, so
        // a missing qualifier addresses nothing; TapValue and FanRate ARE the
        // whole system, so a qualifier on them would silently address nothing.
        // ModifierSystem reports either mistake rather than storing it.
        public static bool RequiresQualifier(ModifierTarget kind)
            => kind is ModifierTarget.GeneratorOutput or ModifierTarget.CurrencyProduction;

        public bool Equals(ModifierTargetKey other) => Kind == other.Kind && Qualifier == other.Qualifier;

        public override bool Equals(object obj) => obj is ModifierTargetKey other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Qualifier.GetHashCode();

        // the form every modifier error message names a target by
        public override string ToString() => Qualifier.Length == 0 ? Kind.ToString() : $"{Kind}:{Qualifier}";
    }
}
