using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Runtime state for one upgrade, wrapping its UpgradeDefinition asset.
    // Applied means the payload has been granted, which is the whole of an
    // upgrade's state: a content unlock applies when its gate is met, a buff
    // when the player buys it. There is no second "purchased" flag because it
    // would say the same thing for buffs and nothing for content unlocks -
    // Definition.Scope already carries how long it lasts, so the album release
    // clears exactly the run-scoped ones and the save partitions on the same
    // field.
    //
    // An upgrade that authors a flat bonus is a production contributor (design doc
    // section 12, rule 13): "+1 Cash per press" is a line feeding cash's yield for
    // as long as this latch holds, not an Add modifier composed over it. The latch
    // IS the lifetime - a release clears it and the line goes with it - so nothing
    // has to remember to withdraw the bonus.
    public class Upgrade : IProductionContributor
    {
        public UpgradeDefinition Definition { get; }

        public bool Applied { get; private set; }

        private readonly Dictionary<ProductionContribution, ModifierSubject> _lineSubjects = new();
        private readonly ModifierSystem _modifiers;

        public Upgrade(UpgradeDefinition definition, ModifierSystem modifiers)
        {
            Definition = definition;
            _modifiers = modifiers;

            foreach (var contribution in definition.Contributions)
            {
                if (contribution != null)
                    _lineSubjects[contribution] = contribution.SubjectUnder(definition.Id, definition.Tags);
            }
        }

        public string ContributorId => Definition.Id;

        public IReadOnlyList<ProductionContribution> Contributions => Definition.Contributions;

        // The flat amount, composed with the modifiers reaching this line. Nothing
        // scales it by a count the way a generator's does - an upgrade is bought
        // once and the bonus is the bonus.
        //
        // Zero while the latch is absent, which is belt-and-braces: the assembler
        // only offers an applied upgrade's lines to a producer, so this is the
        // second of two answers to "a bonus you no longer own", and they agree.
        public BigNumber ValueOf(ProductionContribution contribution)
        {
            if (contribution == null || !Applied || contribution.Amount < 0)
                return BigNumber.Zero;

            return _modifiers.For(SubjectOf(contribution)).ApplyTo((BigNumber)contribution.Amount);
        }

        public ModifierSubject SubjectOf(ProductionContribution contribution)
            => contribution != null && _lineSubjects.TryGetValue(contribution, out var subject)
                ? subject
                : new ModifierSubject(Definition.Id, Definition.Tags);

        internal void MarkApplied() => Applied = true;

        // The album release drops a run-scoped buff's latch so the player re-buys
        // it (design doc section 5); whether this upgrade is one of those is the
        // caller's question, since Definition.Scope is where that is declared.
        // Returns whether anything changed, so an untouched upgrade stays silent.
        internal bool ClearApplied()
        {
            if (!Applied)
                return false;

            Applied = false;
            return true;
        }
    }
}
