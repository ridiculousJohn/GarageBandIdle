using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The modifier registry's own contracts. The load-bearing claims: one
    // composition rule for every system, a run reset that is exact rather than
    // reconstructive (which is why grants are kept individually instead of
    // accumulated), and a target that addresses nothing is refused rather than
    // silently stored.
    public class ModifierSystemTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static readonly ModifierTargetKey TapValue = ModifierTargetKey.Global(ModifierTarget.TapValue);
        private static readonly ModifierTargetKey FanRate = ModifierTargetKey.Global(ModifierTarget.FanRate);

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

        // grants are kept individually, so the reset removes exactly the
        // run-scoped factors. A collapsed product could not do this: dividing
        // 1.5 x 3 out of one number needs the history this keeps.
        [Test]
        public void ResetRunScoped_RemovesExactlyTheRunScopedGrants()
        {
            var modifiers = new ModifierSystem();

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 1.5);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 3);
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);
            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.Run, 10);
            modifiers.Grant(TapValue, ModifierOperation.Add, ContentScope.PermanentInChapter, 4);

            Assert.IsTrue(modifiers.ResetRunScoped());

            var composition = modifiers.For(TapValue);
            Assert.AreEqual(4.0, composition.Add.ToDouble(), 1e-9, "only the permanent add survives");
            Assert.AreEqual(2.0, composition.Multiply.ToDouble(), 1e-9, "only the permanent multiplier survives");
        }

        [Test]
        public void ResetRunScoped_IsSilentAndFalseWhenNothingIsRunScoped()
        {
            var modifiers = new ModifierSystem();
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.PermanentInChapter, 2);
            var changes = 0;
            modifiers.Changed += _ => changes++;

            Assert.IsFalse(modifiers.ResetRunScoped(), "nothing to clear");
            Assert.AreEqual(0, changes, "and nothing notified");
            Assert.AreEqual(2.0, modifiers.For(TapValue).Multiply.ToDouble(), 1e-9);
        }

        // state, then notify: every target settles before the first
        // notification, so no subscriber observes one target cleared while
        // another still holds its run grants
        [Test]
        public void ResetRunScoped_SettlesEveryTargetBeforeNotifying()
        {
            var modifiers = new ModifierSystem();
            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 2);
            modifiers.Grant(FanRate, ModifierOperation.Multiply, ContentScope.Run, 3);

            var notifications = 0;
            var observedHalfReset = false;
            modifiers.Changed += _ =>
            {
                notifications++;
                if (modifiers.For(TapValue).Multiply != BigNumber.One
                    || modifiers.For(FanRate).Multiply != BigNumber.One)
                    observedHalfReset = true;
            };

            modifiers.ResetRunScoped();

            Assert.AreEqual(2, notifications, "one notification per target that changed");
            Assert.IsFalse(observedHalfReset, "every subscriber sees all targets settled");
        }

        // a target that addresses nothing is a caller mistake, not tuning: it
        // would modify a value no system reads
        [Test]
        public void Grant_RefusesATargetThatAddressesNothing()
        {
            var modifiers = new ModifierSystem();

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant with target kind None (uninitialized). Ignoring.");
            modifiers.Grant(ModifierTargetKey.Global(ModifierTarget.None), ModifierOperation.Multiply, ContentScope.Run, 2);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'TapValue' with operation None (uninitialized). Ignoring.");
            modifiers.Grant(TapValue, ModifierOperation.None, ContentScope.Run, 2);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'GeneratorOutput' names no GeneratorOutput id. Ignoring - it would address nothing.");
            modifiers.Grant(ModifierTargetKey.Of(ModifierTarget.GeneratorOutput, ""),
                ModifierOperation.Multiply, ContentScope.Run, 2);

            LogAssert.Expect(LogType.Error,
                "ModifierSystem: Grant on 'TapValue' carries a qualifier 'practice_amp', which that target has no room for. Ignoring.");
            modifiers.Grant(ModifierTargetKey.Of(ModifierTarget.TapValue, "practice_amp"),
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
                "ModifierSystem: Grant on 'TapValue' with scope None. Ignoring - an unscoped modifier has no lifetime.");
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
                "ModifierSystem: Grant on 'TapValue' with a negative Add value '-1'. Ignoring.");
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
