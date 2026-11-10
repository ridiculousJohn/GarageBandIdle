using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // One over a balance, and zero while the balance is zero (design doc
    // 12.2): a value that has not been recorded pays nothing rather than
    // dividing by zero. Observed through the real gather on the standing
    // tree's practice amp, whose other factors are all one at a fresh state -
    // no records, no roadies, no purchased upgrade - so the cash rate is the
    // amp's 0.5 per unit times the factor under test.
    public class InverseOfBalanceTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // The standing tree plus one balance homed at tier1 beside the amp
        // the factor is applied to.
        private class Fixture
        {
            public readonly TestTree Tree = new TestTree();
            public readonly CurrencyDefinition Pace;

            public Fixture()
            {
                Pace = TestTree.DeclareCurrency(Tree.Tier1Def, "pace");
            }

            public InverseOfBalance Formula() => new InverseOfBalance { currency = Pace };

            // A permanent tier1 modifier carrying the formula on the amp's
            // rate. The caller rebuilds once every declaration is in.
            public void Declare()
            {
                var modifier = TestTree.MakeDefinition<ModifierDefinition>("inverse_pace");
                modifier.effects.Add(new Effect { target = "practice_amp", stat = Stat.Rate, formula = Formula() });
                Tree.Tier1Def.modifiers.Add(modifier);
                Tree.Tier1Def.permanentModifiers.Add(modifier);
            }

            public void Record(double pace) => Tree.Tier1.balances[Pace.Id] = pace;

            // The tier's whole cash rate with one amp owned.
            public BigNumber AmpRate()
            {
                Tree.Tier1.generatorCounts[Tree.PracticeAmp.Id] = 1;
                return Producer.GetRate(Tree.Ctx(Tree.Tier1), Tree.Cash);
            }
        }

        // ---- the factor ----

        [TestCase(4.0, 0.25)]
        [TestCase(0.5, 2.0)]
        [TestCase(0.0, 0.0)]
        public void InverseOfBalance_computes_one_over_the_balance(double balance, double expected)
        {
            var f = new Fixture();
            f.Tree.Rebuild();
            f.Record(balance);

            AssertClose(expected, f.Formula().Compute(f.Tree.Ctx(f.Tree.Tier1)));
        }

        // Zero by construction rather than by rounding: the kind answers zero
        // for an unrecorded balance and divides nothing.
        [Test]
        public void At_a_zero_balance_the_factor_is_exactly_zero()
        {
            var f = new Fixture();
            f.Tree.Rebuild();

            Assert.AreEqual(BigNumber.Zero, f.Formula().Compute(f.Tree.Ctx(f.Tree.Tier1)));
        }

        // ---- observed through resolution ----

        [TestCase(4.0, 0.5 * 0.25)]
        [TestCase(0.5, 0.5 * 2.0)]
        public void An_inverse_factor_scales_the_sources_rate(double balance, double expected)
        {
            var f = new Fixture();
            f.Declare();
            f.Tree.Rebuild();
            f.Record(balance);

            AssertClose(expected, f.AmpRate(), "the amp's 0.5 over the balance");
        }

        [Test]
        public void At_a_zero_balance_the_rate_is_exactly_zero()
        {
            var f = new Fixture();
            f.Declare();
            f.Tree.Rebuild();

            Assert.AreEqual(BigNumber.Zero, f.AmpRate(), "nothing recorded, nothing paid");
            var unit = Producer.UnitRate(f.Tree.Ctx(f.Tree.Tier1), f.Tree.PracticeAmp);
            Assert.AreEqual(BigNumber.Zero, unit[0].amount, "one unit's line reads zero too");
        }

        [Test]
        public void At_a_zero_balance_a_tick_deposits_nothing_and_does_not_throw()
        {
            var f = new Fixture();
            f.Declare();
            f.Tree.Rebuild();
            f.Tree.Tier1.generatorCounts[f.Tree.PracticeAmp.Id] = 4;

            Assert.DoesNotThrow(() => TickSystem.Tick(f.Tree.Root, f.Tree.Ch1,
                ScriptableObject.CreateInstance<GameConfig>(), 10, f.Tree.Now.AddSeconds(10)));

            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.balances[f.Tree.Cash.Id], "no cash");
            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.earnedTotals[f.Tree.Cash.Id], "and no earned total moved");
        }
    }
}
