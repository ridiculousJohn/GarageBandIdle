using System;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Grants one stat modifier, composed on read by ModifierSystem. This is every
    // numeric effect in the game: one generator's output multiplier, the fan-rate
    // multiplier a cover bar pays, an income multiplier over named currencies. The
    // address is a ModifierSelector, so the operation stays code and the open half
    // (which numbers) stays content - one class rather than one per stat.
    //
    // A FLAT BONUS IS NOT ONE OF THESE. "+1 cash per press" is a contribution to
    // cash's yield authored by the upgrade that pays it (rule 11), not an effect
    // granted against the number, which is why nothing here adds.
    //
    // It used to carry a ModifierTarget as well: a closed KIND plus a list of ids
    // within that kind's family. That could not name one of a generator's two
    // output lines, because the kind named the family and the id named a member of
    // it, and the number itself was never named at all (design doc rule 11). A
    // selector names the numbers directly, so what it reaches is decided by what
    // the numbers say they are.
    //
    // What it reaches is declared data, never implied: a generator producing fans
    // or merch must not inherit a cash income buff just because the buff exists
    // (design doc section 3, the rule the Records buff also follows).
    [Serializable]
    public class GrantModifierEffect : GameEffect
    {
        [SerializeField]
        [Tooltip("Ids or tags this reaches. EMPTY reaches every number in scope.")]
        private ModifierSelector _selector;

        [SerializeField]
        private ModifierOperation _operation;

        [SerializeField]
        [Tooltip("The multiplier: 1.5 for +50%.")]
        private double _value;

        public ModifierSelector Selector => _selector;
        public ModifierOperation Operation => _operation;
        public double Value => _value;

        public GrantModifierEffect() { }

        public GrantModifierEffect(ModifierSelector selector, ModifierOperation operation, double value)
        {
            _selector = selector;
            _operation = operation;
            _value = value;
        }

        // ONE grant, whatever the selector reaches - not one per named id. A
        // selector reaching several numbers is one modifier that several numbers
        // ask about, which is the only shape that survives the set growing: a
        // grant per named id would silently miss whatever is added later.
        //
        // This is the effect the rebuild boundaries exist FOR: the store is
        // cleared before every projection (ModifierSystem.ResetGranted), so
        // re-granting rebuilds rather than compounds. Grants are deliberately not
        // idempotent on their own - clearing first is what makes replaying them
        // exact.
        public override void Apply(EffectContext context, ContentScope scope)
            => context.Modifiers.Grant(_selector, _operation, scope, _value);

        public override void Validate(ConditionContext context, string source)
        {
            // A serialized enum is an int, so a hand-edited or un-migrated asset can
            // hold a value no member defines. The specialized payload classes this
            // replaced could not express that - each hardcoded its operation - so
            // generalizing made two new broken states representable and they have to
            // be named here rather than only failing closed later.
            if (!Enum.IsDefined(typeof(ModifierOperation), _operation))
                Debug.LogError($"GameEffect: {source} has modifier operation {(int)_operation}, which no ModifierOperation defines.");
            else if (_operation == ModifierOperation.None)
                Debug.LogError($"GameEffect: {source} names no modifier operation (uninitialized).");

            // a non-positive multiplier zeroes or negates the whole product it
            // lands in. The registry refuses it at runtime; this catches the asset.
            if (_operation == ModifierOperation.Multiply && _value <= 0)
                Debug.LogError($"GameEffect: {source} has a non-positive multiplier ({_value}).");

            ValidateTerms(context, source);
        }

        // A term naming nothing is the failure the closed target enum used to
        // catch by refusing to compile: the modifier is stored, matches no number,
        // and looks authored rather than broken. So every term must resolve to
        // something in the content set - a definition id, a tag some definition
        // declares, or a produced number's feed name.
        //
        // An EMPTY selector is legal and reaches everything (rule 11), so there is
        // nothing to check for one. What guards a forgotten key is the importer,
        // which refuses an effect that declares no targets at all - absent and
        // deliberately-empty are different things in the JSON.
        private void ValidateTerms(ConditionContext context, string source)
        {
            // No database means a unit fixture rather than a boot: there is no
            // content set to resolve against, and reporting every term as unknown
            // would drown the assertion each test is actually making.
            if (context.Database == null)
                return;

            for (var i = 0; i < _selector.TermCount; i++)
            {
                var term = _selector.Term(i);
                if (!context.Database.ResolvesModifierTerm(term))
                    Debug.LogError($"GameEffect: {source} targets '{term}', which no definition id, tag or produced number answers to.");
            }
        }
    }
}
