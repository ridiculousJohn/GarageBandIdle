using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The buff payload handlers (design doc section 4). The load-bearing claims:
    // each payload grants a modifier and holds no state of its own, the effect
    // reaches exactly the target the payload names, and the lifetime comes from
    // the owning upgrade's scope rather than a second declaration.
    public class UpgradePayloadTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static readonly ModifierTargetKey TapValue = ModifierTargetKey.Global(ModifierTarget.TapValue);

        private static UpgradePayloadContext Context(ModifierSystem modifiers)
            => new(new FlagSystem(), modifiers);

        // a flat tap add lands before the multipliers, which is what makes
        // stage_presence worth more later in a run than at the start
        [Test]
        public void TapValueAdd_GrantsAnAddOnTapValue()
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(1, modifiers);

            new TapValueAddPayload(1).Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(2.0, tap.Value.ToDouble(), 1e-9, "base 1 + 1");

            modifiers.Grant(TapValue, ModifierOperation.Multiply, ContentScope.Run, 3);
            Assert.AreEqual(6.0, tap.Value.ToDouble(), 1e-9, "(1 + 1) x 3");
        }

        [Test]
        public void GeneratorOutputMultiplier_ReachesOnlyTheNamedGenerator()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var system = new GeneratorSystem(new[]
            {
                TestContent.MakeGenerator("practice_amp", "cash", 60, 1.15, 0.4),
                TestContent.MakeGenerator("drummer", "cash", 500, 1.15, 3),
            }, currencies, modifiers);
            TestContent.BuyTimes(system.Get("practice_amp"), currencies, 1);
            TestContent.BuyTimes(system.Get("drummer"), currencies, 1);

            new GeneratorOutputMultiplierPayload("practice_amp", 2).Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(0.8, system.Get("practice_amp").ProductionPerSecond.ToDouble(), 1e-9, "0.4 x 2");
            Assert.AreEqual(3.0, system.Get("drummer").ProductionPerSecond.ToDouble(), 1e-9,
                "the drummer produces the same currency and is untouched");
        }

        // the payload names the currencies it multiplies, so a generator
        // producing anything else never inherits the income buff
        [Test]
        public void CurrencyPerSecMultiplier_GrantsOnEveryDeclaredCurrency_AndNoOther()
        {
            var currencies = TestContent.MakeEconomy();
            var modifiers = new ModifierSystem();
            var cashGen = TestContent.MakeGenerator("cash_gen", "cash", 10, 1.15, 3);
            var fansGen = TestContent.MakeGenerator("fans_gen", "fans", 10, 1.15, 5);
            var system = new GeneratorSystem(new[] { cashGen, fansGen }, currencies, modifiers);
            TestContent.BuyTimes(system.Get("cash_gen"), currencies, 1);
            TestContent.BuyTimes(system.Get("fans_gen"), currencies, 1);
            var cashBefore = currencies.Get("cash");
            var fansBefore = currencies.Get("fans");

            new CurrencyPerSecMultiplierPayload(new List<string> { "cash" }, 1.5)
                .Apply(Context(modifiers), ContentScope.Run);
            system.Tick(10);

            Assert.AreEqual(45.0, (currencies.Get("cash") - cashBefore).ToDouble(), 1e-9, "3 x 1.5 x 10s");
            Assert.AreEqual(50.0, (currencies.Get("fans") - fansBefore).ToDouble(), 1e-9,
                "an undeclared currency takes no multiplier");
        }

        [Test]
        public void CurrencyPerSecMultiplier_GrantsOncePerCurrencyItNames()
        {
            var modifiers = new ModifierSystem();

            new CurrencyPerSecMultiplierPayload(new List<string> { "cash", "fans" }, 2)
                .Apply(Context(modifiers), ContentScope.Run);

            Assert.AreEqual(2.0,
                modifiers.For(ModifierTargetKey.Of(ModifierTarget.CurrencyProduction, "cash")).Multiply.ToDouble(), 1e-9);
            Assert.AreEqual(2.0,
                modifiers.For(ModifierTargetKey.Of(ModifierTarget.CurrencyProduction, "fans")).Multiply.ToDouble(), 1e-9);
        }

        // the scope travels with the grant, so a run-scoped buff clears on the
        // album release and a permanent-in-chapter one survives it - the
        // upgrade's declaration is the only place that lifetime is stated
        [TestCase(ContentScope.Run, 1.0)]
        [TestCase(ContentScope.PermanentInChapter, 2.0)]
        public void PayloadScope_DecidesWhatARunResetKeeps(ContentScope scope, double afterReset)
        {
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(1, modifiers);

            new TapValueAddPayload(1).Apply(Context(modifiers), scope);
            Assert.AreEqual(2.0, tap.Value.ToDouble(), 1e-9);

            modifiers.ResetRunScoped();

            Assert.AreEqual(afterReset, tap.Value.ToDouble(), 1e-9);
        }

        // UpgradeSystem hands the owning definition's scope to the payload. The
        // buff purchase flow will be the real caller; a content unlock is the
        // only path that applies a payload today, so it is what proves the wiring.
        [Test]
        public void UpgradeSystem_PassesTheDefinitionsScopeToThePayload()
        {
            var currencies = TestContent.MakeEconomy();
            var flags = new FlagSystem();
            var modifiers = new ModifierSystem();
            var tap = new TapSystem(1, modifiers);
            var upgrades = new UpgradeSystem(new[]
            {
                // no gate = met from the start, so it applies on the first pass
                TestContent.MakeUpgrade("permanent_tap", UpgradeType.ContentUnlock,
                    ContentScope.PermanentInChapter, null, new TapValueAddPayload(4)),
            }, currencies, flags, modifiers);

            upgrades.EvaluateContentUnlocks(TestContent.MakeContext(currencies, flags: flags));
            Assert.AreEqual(5.0, tap.Value.ToDouble(), 1e-9, "base 1 + 4");

            modifiers.ResetRunScoped();
            Assert.AreEqual(5.0, tap.Value.ToDouble(), 1e-9,
                "the definition's permanent-in-chapter scope reached the grant");
        }

        // tuning that the registry refuses at runtime is reported against the
        // asset too, so a content mistake surfaces at boot instead of as an
        // effect that silently never applied
        [Test]
        public void Validate_ReportsTuningTheRegistryWouldRefuse()
        {
            var context = TestContent.MakeContext(TestContent.MakeEconomy());

            LogAssert.Expect(LogType.Error,
                "UpgradePayload: Upgrade 'drain_tap' (payload) adds a negative amount (-1) to the tap value.");
            new TapValueAddPayload(-1).Validate(context, "Upgrade 'drain_tap' (payload)");

            LogAssert.Expect(LogType.Error,
                "UpgradePayload: Upgrade 'zero_amp' (payload) has a non-positive multiplier (0).");
            LogAssert.Expect(LogType.Error,
                "UpgradePayload: Upgrade 'zero_amp' (payload) targets unknown generator id 'practice_amp'.");
            new GeneratorOutputMultiplierPayload("practice_amp", 0).Validate(context, "Upgrade 'zero_amp' (payload)");
        }
    }
}
