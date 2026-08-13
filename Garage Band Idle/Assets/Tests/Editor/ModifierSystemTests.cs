using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The modifier registry's own contracts. The load-bearing claims: one
    // composition rule for every system, a store that is REBUILT rather than
    // filtered (which is why grants are kept individually instead of
    // accumulated - a rebuild has to compose the same factors in the same
    // order), a derived modifier untouched by that rebuild because its lifetime
    // is its source's, and a target that addresses nothing refused rather than
    // silently stored.
    public class ModifierSystemTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static readonly ModifierTargetKey TapValue = ModifierTargetKey.Of(ModifierTarget.CurrencyYield, "cash");
        private static readonly ModifierTargetKey FanRate = ModifierTargetKey.Of(ModifierTarget.CurrencyRate, "fans");

        // a derived modifier with a fixed value: enough to assert that a store
        // rebuild leaves derived modifiers standing, without dragging in the
        // currency pool RecordsIncomeModifier reads
        private class FixedDerived : DerivedModifier
        {
            private readonly ModifierTargetKey _target;
            private readonly ModifierOperation _operation;
            private readonly BigNumber _value;

            public FixedDerived(ModifierTargetKey target, ModifierOperation operation, double value)
            {
                _target = target;
                _operation = operation;
                _value = value;
            }

            public override ModifierTargetKey Target => _target;
            public override ModifierOperation Operation => _operation;
            public override BigNumber Value => _value;
        }

        // A serialized enum is an int, so an asset can carry a value no member
        // defines. Both writers come through IsAddressable, which is what keeps such
        // a value out of the store. The operation case is the one that bites: the
        // value guards test for Multiply and Add by name, so an undefined operation
        // would slip past every one of them and then compose as a multiply - a zero
        // there wipes the whole product for the rest of the run.
        [Test]
        public void Grant_RefusesAnEnumValueNoMemberDefines()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant with target kind 99, which no ModifierTarget defines. Ignoring.");
            modifiers.Grant(ModifierTargetKey.All((ModifierTarget)99),
                ModifierOperation.Multiply, ContentScope.Run, 2);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'CurrencyYield:cash' with operation 99, which no ModifierOperation defines. Ignoring.");
            modifiers.Grant(TapValue, (ModifierOperation)99, ContentScope.Run, 0);

            Assert.AreEqual(1.0, modifiers.For(TapValue).Multiply.ToDouble(), 1e-9,
                "the zero never reached the product it would have wiped");
            Assert.AreEqual(0.0, modifiers.For(TapValue).Add.ToDouble(), 1e-9);
        }

        [Test]
        public void UntargetedTarget_ComposesToIdentity()
        {
            var modifiers = new ModifierSystem();

            var composition = modifiers.For(TapValue);

            Assert.AreEqual(0.0, composition.Add.ToDouble(), 1e-9);
            Assert.AreEqual(1.0, composition.Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(7.0, composition.ApplyTo(7).ToDouble(), 1e-9, "identity leaves the base alone");
        }

        // adds sum, multipliers multiply, and the adds land first - the one rule
        // every system applies by calling ApplyTo
        [Test]
        public void Composition_SumsAdds_MultipliesMultipliers_AddsFirst()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, 1);
            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.PermanentInChapter, 2);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);

            var composition = modifiers.For(TapValue);
            Assert.AreEqual(3.0, composition.Add.ToDouble(), 1e-9, "1 + 2");
            Assert.AreEqual(6.0, composition.Multiply.ToDouble(), 1e-9, "2 x 3");
            Assert.AreEqual(48.0, composition.ApplyTo(5).ToDouble(), 1e-9, "(5 + 3) x 6");
        }

        // Rule 11: an absent qualifier means every member of the kind in reach,
        // so an unqualified grant has to compose into each specific target
        // WITHOUT reaching a different kind, and a qualified one must stay put.
        // The double-count guard is the subtle half - an unqualified READ is
        // that bucket, so unioning it with itself would square its multipliers.
        [Test]
        public void UnqualifiedGrant_ReachesEveryMemberOfItsKind_AndOnlyThose()
        {
            var modifiers = new ModifierSystem();
            var everyRate = ModifierTargetKey.All(ModifierTarget.CurrencyRate);
            var merchRate = ModifierTargetKey.Of(ModifierTarget.CurrencyRate, "merch");

            modifiers.Grant(everyRate, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.Run, 3);

            Assert.AreEqual(6.0, modifiers.For(FanRate).Multiply.ToDouble(), 1e-9,
                "the named currency composes its own grant and the reach-all one");
            Assert.AreEqual(2.0, modifiers.For(merchRate).Multiply.ToDouble(), 1e-9,
                "a currency nothing named still gets the reach-all grant");
            Assert.AreEqual(1.0, modifiers.For(TapValue).Multiply.ToDouble(), 1e-9,
                "and a different KIND is untouched - reach-all is not reach-everything");
            Assert.AreEqual(2.0, modifiers.For(everyRate).Multiply.ToDouble(), 1e-9,
                "reading the unqualified key composes it once, not twice");
        }

        // The store is rebuilt, never filtered (design doc section 12, rule 6):
        // ResetGranted empties it whatever the scopes were, and the permanent
        // effects come back because the FACTS behind them come back. Asserting
        // that a permanent grant is cleared here is not a gap - it is the
        // property. A method that dropped run-scoped grants and left permanent
        // ones would be a second mechanism for arriving at a modifier set,
        // beside the projection, able to disagree with it silently.
        //
        // Grants are still kept individually rather than accumulated: what that
        // buys after this change is that a rebuild composes the same factors in
        // the same order, which a collapsed product could not reproduce.
        [Test]
        public void ResetGranted_ClearsEveryGrantWhateverItsScope()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 1.5);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 3);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);
            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, 10);
            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.PermanentInChapter, 4);

            Assert.IsTrue(modifiers.ResetGranted());

            var composition = modifiers.For(TapValue);
            Assert.AreEqual(0.0, composition.Add.ToDouble(), 1e-9, "no grant survives a rebuild, permanent included");
            Assert.AreEqual(1.0, composition.Multiply.ToDouble(), 1e-9, "the store composes to identity until the projection re-runs");
        }

        [Test]
        public void ResetGranted_IsSilentAndFalseWhenThereIsNothingToClear()
        {
            var modifiers = new ModifierSystem();
            modifiers.AddDerived(new FixedDerived(TapValue, ModifierOperation.Multiply, 2));
            var changes = 0;
            modifiers.Changed += _ => changes++;

            Assert.IsFalse(modifiers.ResetGranted(), "nothing granted to clear");
            Assert.AreEqual(0, changes, "and nothing notified");
            Assert.AreEqual(2.0, modifiers.For(TapValue).Multiply.ToDouble(), 1e-9,
                "a derived modifier is untouched: its lifetime is its source's, so there is nothing to rebuild");
        }

        // state, then notify: every target settles before the first
        // notification, so no subscriber observes one target cleared while
        // another still holds its grants
        [Test]
        public void ResetGranted_SettlesEveryTargetBeforeNotifying()
        {
            var modifiers = new ModifierSystem();
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 3);

            var notifications = 0;
            var observedHalfReset = false;
            modifiers.Changed += _ =>
            {
                notifications++;
                if (modifiers.For(TapValue).Multiply != BigNumber.One
                    || modifiers.For(FanRate).Multiply != BigNumber.One)
                    observedHalfReset = true;
            };

            modifiers.ResetGranted();

            Assert.AreEqual(2, notifications, "one notification per target that changed");
            Assert.IsFalse(observedHalfReset, "every subscriber sees all targets settled");
        }

        // What a display filters on. The notification names the target that
        // changed, so a generator row can tell its own output modifier from
        // another generator's, and the grant has settled by the time it fires -
        // a row repainting inside the handler reads the new rate, never the one
        // it was about to replace.
        [Test]
        public void Grant_NotifiesWithTheTargetItNames_AfterTheGrantSettles()
        {
            var modifiers = new ModifierSystem();
            var amp = ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, "practice_amp");
            var drummer = ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, "drummer");

            var notifications = 0;
            var observed = default(ModifierTargetKey);
            var multiplyWhenNotified = 0.0;
            modifiers.Changed += target =>
            {
                notifications++;
                observed = target;
                multiplyWhenNotified = modifiers.For(target).Multiply.ToDouble();
            };

            modifiers.Grant(amp, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(1, notifications, "one notification for the one grant");
            Assert.AreEqual(amp, observed, "named the generator it targets");
            Assert.AreNotEqual(drummer, observed, "another generator's row can tell this is not its own");
            Assert.AreEqual(2.0, multiplyWhenNotified, 1e-9, "the grant was already stored when the signal fired");
        }

        // a target that addresses nothing is a caller mistake, not tuning: it
        // would modify a value no system reads
        [Test]
        public void Grant_RefusesATargetThatAddressesNothing()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant with target kind None (uninitialized). Ignoring.");
            modifiers.Grant(ModifierTargetKey.All(ModifierTarget.None), ModifierOperation.Multiply, ContentScope.Run, 2);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'CurrencyYield:cash' with operation None (uninitialized). Ignoring.");
            modifiers.Grant(TapValue, ModifierOperation.None, ContentScope.Run, 2);

            // an ABSENT qualifier is no longer a mistake - it means every member
            // of the kind in reach (rule 11) - so the only addressing error left
            // is a qualifier on a kind with nothing to resolve it against
            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'IdleRate' carries a qualifier 'practice_amp', which that target has no id family to resolve against. Ignoring.");
            modifiers.Grant(ModifierTargetKey.Of(ModifierTarget.IdleRate, "practice_amp"),
                ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(1.0, modifiers.For(TapValue).Multiply.ToDouble(), 1e-9, "nothing was stored");
        }

        // a modifier with no scope has no lifetime, so no reset could ever treat
        // it correctly
        [Test]
        public void Grant_RefusesAnUnscopedModifier()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'CurrencyYield:cash' with scope None. Ignoring - an unscoped modifier has no lifetime.");
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.None, 2);

            Assert.AreEqual(1.0, modifiers.For(TapValue).Multiply.ToDouble(), 1e-9);
        }

        // a negative add is the additive form of the non-positive multiplier:
        // tuning that subtracts from a value nothing else can restore
        [Test]
        public void Grant_RefusesANegativeAdd()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'CurrencyYield:cash' with a negative Add value '-1'. Ignoring.");
            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, -1);

            Assert.AreEqual(0.0, modifiers.For(TapValue).Add.ToDouble(), 1e-9);
        }

        // a zero add is a legitimate no-op, unlike a zero multiplier
        [Test]
        public void Grant_AcceptsAZeroAdd()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, 0);

            Assert.AreEqual(0.0, modifiers.For(TapValue).Add.ToDouble(), 1e-9);
            Assert.AreEqual(5.0, modifiers.For(TapValue).ApplyTo(5).ToDouble(), 1e-9);
        }

        // the qualifier is what keeps one generator's buff off another's output
        [Test]
        public void QualifiedTargets_AreDistinctPerQualifier()
        {
            var modifiers = new ModifierSystem();
            var amp = ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, "practice_amp");
            var drummer = ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, "drummer");

            modifiers.Grant(amp, ModifierOperation.Multiply, ContentScope.Run, 2);

            Assert.AreEqual(2.0, modifiers.For(amp).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(1.0, modifiers.For(drummer).Multiply.ToDouble(), 1e-9, "another id is another target");
        }
    }
}
