using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The factor that shrinks with what is owed (design doc 12.2): one minus a
    // coefficient times the debt, floored at zero. The debt is the sum of the
    // named balances or the largest of them, optionally divided by a relief
    // balance above one. Every number here is observed through the real gather
    // on the standing tree's practice amp, whose other factors are all one at a
    // fresh state - no records, no roadies, no purchased upgrade - so the cash
    // rate is the amp's 0.5 per unit times the factor under test.
    public class DebtFactorTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // The standing tree plus two debt balances and a relief balance, all
        // homed at tier1 beside the amp the factor is applied to.
        private class Fixture
        {
            public readonly TestTree Tree = new TestTree();
            public readonly CurrencyDefinition DebtA;
            public readonly CurrencyDefinition DebtB;
            public readonly CurrencyDefinition Relief;

            public Fixture()
            {
                DebtA = TestTree.DeclareCurrency(Tree.Tier1Def, "debt_a");
                DebtB = TestTree.DeclareCurrency(Tree.Tier1Def, "debt_b");
                Relief = TestTree.DeclareCurrency(Tree.Tier1Def, "relief");
            }

            public DebtFactor Formula(DebtCombine combine, double coefficient, CurrencyDefinition relief = null) =>
                new DebtFactor
                {
                    currencies = new List<CurrencyDefinition> { DebtA, DebtB },
                    combine = combine,
                    coefficient = coefficient,
                    relief = relief,
                };

            // A permanent tier1 modifier carrying the formula on one source's
            // number. The caller rebuilds once every declaration is in.
            public ModifierDefinition Declare(string id, string target, string stat, MultiplierFormula formula,
                                              Condition appliesWhen = null)
            {
                var modifier = TestTree.MakeDefinition<ModifierDefinition>(id);
                modifier.appliesWhen = appliesWhen;
                modifier.effects.Add(new Effect { target = target, stat = stat, formula = formula });
                Tree.Tier1Def.modifiers.Add(modifier);
                Tree.Tier1Def.permanentModifiers.Add(modifier);
                return modifier;
            }

            public void Owe(double a, double b)
            {
                Tree.Tier1.balances[DebtA.Id] = a;
                Tree.Tier1.balances[DebtB.Id] = b;
            }

            // The tier's whole cash rate with one amp owned.
            public BigNumber AmpRate()
            {
                Tree.Tier1.generatorCounts[Tree.PracticeAmp.Id] = 1;
                return Producer.GetRate(Tree.Ctx(Tree.Tier1), Tree.Cash);
            }

            // The stage-1 product on the amp's cash rate, read off the plan the
            // build compiled for its first rate entry.
            public BigNumber AmpFactor() =>
                Producer.GetMultiplier(Tree.Ctx(Tree.Tier1),
                    Tree.Tier1.Link<CoordinatePlan>(Tree.PracticeAmp.produces.First(
                        entry => entry.currency == Tree.Cash && entry.stat == Stat.Rate)));
        }

        // ---- the two combines ----

        [Test]
        public void DebtFactor_computes_one_minus_the_coefficient_times_the_summed_debt()
        {
            var f = new Fixture();
            f.Tree.Rebuild();
            f.Owe(2, 1);

            // 1 - 0.125 x (2 + 1)
            AssertClose(0.625, f.Formula(DebtCombine.Sum, 0.125).Compute(f.Tree.Ctx(f.Tree.Tier1)));
        }

        [Test]
        public void A_summed_debt_factor_scales_the_sources_rate()
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Sum, 0.125));
            f.Tree.Rebuild();

            AssertClose(0.5, f.AmpRate(), "no debt, no reduction");

            f.Owe(2, 1);
            AssertClose(0.5 * 0.625, f.AmpRate(), "1 - 0.125 x (2 + 1)");
        }

        [Test]
        public void A_maximum_debt_factor_reads_only_the_larger_balance()
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Maximum, 0.125));
            f.Tree.Rebuild();
            f.Owe(2, 1);

            AssertClose(0.5 * 0.75, f.AmpRate(), "1 - 0.125 x 2");

            f.Owe(1, 3);
            AssertClose(0.5 * 0.625, f.AmpRate(), "1 - 0.125 x 3, whichever side holds it");
        }

        // An authored DebtFactor with no combine is the sum.
        [Test]
        public void An_unset_combine_is_the_sum()
        {
            Assert.AreEqual(DebtCombine.Sum, new DebtFactor().combine);
        }

        // ---- the floor ----

        [Test]
        public void A_debt_past_the_reciprocal_of_the_coefficient_floors_the_factor_at_zero()
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Sum, 0.5));
            f.Tree.Rebuild();
            f.Owe(2, 1);

            // 0.5 x 3 = 1.5 owed against a factor of one: reduced to nothing,
            // never past it.
            Assert.AreEqual(BigNumber.Zero, f.AmpFactor(), "the factor is exactly zero");
            Assert.AreEqual(BigNumber.Zero, f.AmpRate(), "the rate is exactly zero, not negative");
            var unit = Producer.UnitRate(f.Tree.Ctx(f.Tree.Tier1), f.Tree.PracticeAmp);
            Assert.AreEqual(BigNumber.Zero, unit[0].amount, "one unit's line reads zero too");
        }

        [Test]
        public void A_floored_rate_source_ticks_nothing_and_does_not_throw()
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Sum, 0.5));
            f.Tree.Rebuild();
            f.Owe(2, 1);
            f.Tree.Tier1.generatorCounts[f.Tree.PracticeAmp.Id] = 4;

            Assert.DoesNotThrow(() => TickSystem.Tick(f.Tree.Root, f.Tree.Ch1,
                ScriptableObject.CreateInstance<GameConfig>(), 10, f.Tree.Now.AddSeconds(10)));

            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.balances[f.Tree.Cash.Id], "no cash");
            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.earnedTotals[f.Tree.Cash.Id], "and no earned total moved");
        }

        [Test]
        public void A_floored_yield_source_fires_nothing_and_does_not_throw()
        {
            var f = new Fixture();
            f.Declare("debt", "tap_producer", Stat.Yield, f.Formula(DebtCombine.Sum, 0.5));
            f.Tree.Rebuild();
            f.Owe(2, 1);

            Assert.DoesNotThrow(() => Producer.FireProducer(f.Tree.Ctx(f.Tree.Tier1), f.Tree.TapProducer));

            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.balances[f.Tree.Cash.Id], "no cash");
            Assert.AreEqual(BigNumber.Zero, f.Tree.Tier1.earnedTotals[f.Tree.Cash.Id], "and no earned total moved");
        }

        // ---- relief ----

        // Relief divides only when it is above one: a balance at or under one
        // would multiply the debt or leave it be, and the kind leaves it be.
        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void Relief_at_or_below_one_leaves_the_debt_whole(double relief)
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Sum, 0.125, f.Relief));
            f.Tree.Rebuild();
            f.Owe(2, 1);
            f.Tree.Tier1.balances[f.Relief.Id] = relief;

            AssertClose(0.5 * 0.625, f.AmpRate(), "1 - 0.125 x 3");
        }

        [Test]
        public void Relief_above_one_divides_the_combined_debt()
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Sum, 0.125, f.Relief));
            f.Tree.Rebuild();
            f.Owe(2, 1);
            f.Tree.Tier1.balances[f.Relief.Id] = 4;

            // 1 - 0.125 x (2 + 1) / 4
            AssertClose(0.5 * 0.90625, f.AmpRate());
        }

        [Test]
        public void Relief_divides_the_maximum_and_not_each_balance()
        {
            var f = new Fixture();
            f.Declare("debt", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Maximum, 0.125, f.Relief));
            f.Tree.Rebuild();
            f.Owe(4, 2);
            f.Tree.Tier1.balances[f.Relief.Id] = 2;

            // 1 - 0.125 x max(4, 2) / 2
            AssertClose(0.5 * 0.75, f.AmpRate());
        }

        // ---- the subtraction it stands for ----

        // A per-unit rate of b with a factor of 1 - debt / b is b - debt per
        // unit: the debt comes off the BASE, and every other multiplier scales
        // what is left. Here b = 4, three units, one other constant x2.
        [TestCase(0.0, 24.0)]
        [TestCase(1.0, 18.0)]
        [TestCase(2.0, 12.0)]
        [TestCase(4.0, 0.0)]
        public void A_debt_factor_of_one_over_the_base_rate_subtracts_the_debt_from_the_base(double debt, double expected)
        {
            var f = new Fixture();
            var loan = TestTree.MakeDefinition<GeneratorDefinition>("loan_amp");
            loan.availableWhen = new Always();
            loan.costCurrency = f.Tree.Cash;
            loan.baseCost = 10;
            loan.growth = 1.15;
            loan.produces.Add(TestTree.Entry(f.Tree.Cash, Stat.Rate, 4));
            f.Tree.Tier1Def.generators.Add(loan);

            f.Declare("debt", "loan_amp", Stat.Rate, new DebtFactor
            {
                currencies = new List<CurrencyDefinition> { f.DebtA },
                coefficient = 0.25,
            });
            var doubled = TestTree.MakeDefinition<ModifierDefinition>("doubled");
            doubled.effects.Add(new Effect { target = "loan_amp", stat = Stat.Rate, multiplier = 2 });
            f.Tree.Tier1Def.modifiers.Add(doubled);
            f.Tree.Tier1Def.permanentModifiers.Add(doubled);
            f.Tree.Rebuild();

            f.Tree.Tier1.generatorCounts[loan.Id] = 3;
            f.Tree.Tier1.balances[f.DebtA.Id] = debt;

            // (4 - debt) x 3 x 2
            AssertClose(expected, Producer.GetRate(f.Tree.Ctx(f.Tree.Tier1), f.Tree.Cash));
        }

        // ---- the combine switched by a purchase ----

        // Two modifiers on one number, gated on either side of one upgrade:
        // the sum applies until it is bought and the maximum after, so the
        // switch is authored content rather than a field anything rewrites.
        [Test]
        public void An_upgrade_switches_the_debt_from_the_sum_to_the_maximum()
        {
            var f = new Fixture();
            var consolidate = TestTree.MakeDefinition<UpgradeDefinition>("consolidate");
            consolidate.gate = new Always();
            consolidate.costCurrency = f.Tree.Cash;
            consolidate.cost = 10;
            f.Tree.Tier1Def.upgrades.Add(consolidate);

            f.Declare("debt_sum", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Sum, 0.125),
                new Not { condition = new UpgradePurchased { upgrade = consolidate } });
            f.Declare("debt_max", "practice_amp", Stat.Rate, f.Formula(DebtCombine.Maximum, 0.125),
                new UpgradePurchased { upgrade = consolidate });
            f.Tree.Rebuild();
            f.Owe(2, 1);

            AssertClose(0.5 * 0.625, f.AmpRate(), "before: 1 - 0.125 x (2 + 1)");

            var ctx = f.Tree.Ctx(f.Tree.Tier1);
            ctx.Deposit(f.Tree.Cash.Id, 10);
            Assert.IsTrue(Purchasing.TryBuy(ctx, consolidate), "the upgrade is bought");

            AssertClose(0.5 * 0.75, f.AmpRate(), "after: 1 - 0.125 x max(2, 1)");
        }
    }
}
