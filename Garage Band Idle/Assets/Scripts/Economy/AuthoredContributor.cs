using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The runtime form of a ProducerDefinition's lines (design doc section 12,
    // rule 13): flat amounts, scaled by nothing. A generator multiplies its lines
    // by an owned count and an upgrade gates its on a purchase latch; this
    // contributor has no state at all, which is why it exists as a thin wrapper
    // rather than as a branch inside production - the assembler asks every
    // contributor the same question and never learns what kind it is.
    public class AuthoredContributor : IProductionContributor
    {
        private readonly ProducerDefinition _definition;
        private readonly IModifierResolver _modifiers;
        private readonly Dictionary<ProductionContribution, ModifierSubject> _lineSubjects = new();

        public AuthoredContributor(ProducerDefinition definition, IModifierResolver modifiers)
        {
            _definition = definition;
            _modifiers = modifiers;

            foreach (var contribution in definition.Contributions)
            {
                if (contribution != null)
                    _lineSubjects[contribution] = contribution.SubjectUnder(definition.Id, definition.Tags);
            }
        }

        public ProducerDefinition Definition => _definition;

        public string ContributorId => _definition.Id;

        public IReadOnlyList<ProductionContribution> Contributions => _definition.Contributions;

        // The authored amount, composed with the modifiers reaching this line.
        // A negative amount is invalid data (boot validation reports it) and fails
        // closed to zero, so a broken asset can never drain the currency it names.
        public BigNumber ValueOf(ProductionContribution contribution)
        {
            if (contribution == null || contribution.Amount < 0)
                return BigNumber.Zero;

            return _modifiers.For(SubjectOf(contribution)).ApplyTo((BigNumber)contribution.Amount);
        }

        public ModifierSubject SubjectOf(ProductionContribution contribution)
            => contribution != null && _lineSubjects.TryGetValue(contribution, out var subject)
                ? subject
                : new ModifierSubject(_definition.Id, _definition.Tags);
    }
}
