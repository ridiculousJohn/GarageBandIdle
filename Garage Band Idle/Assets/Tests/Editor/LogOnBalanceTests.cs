using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // 1 + coefficient x log10(1 + balance) (design doc 12.2): a factor that
    // keeps rising with the balance but by a decade at a time. Observed through
    // the real gather on the standing tree's practice amp, whose other factors
    // are all one at a fresh state, so the cash rate is 0.5 per unit times the
    // factor under test.
    public class LogOnBalanceTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // A permanent tier1 modifier scaling the amp's rate by the log of a
        // tier1 balance, with the tree rebuilt so the plan holds it.
        private static (TestTree tree, CurrencyDefinition hype, LogOnBalance formula) Declare(double coefficient)
        {
            var tree = new TestTree();
            var hype = TestTree.DeclareCurrency(tree.Tier1Def, "hype");
            var formula = new LogOnBalance { currency = hype, coefficient = coefficient };
            var modifier = TestTree.MakeDefinition<ModifierDefinition>("hype_scale");
            modifier.effects.Add(new Effect { target = "practice_amp", stat = Stat.Rate, formula = formula });
            tree.Tier1Def.modifiers.Add(modifier);
            tree.Tier1Def.permanentModifiers.Add(modifier);
            tree.Rebuild();
            tree.Tier1.generatorCounts[tree.PracticeAmp.Id] = 1;
            return (tree, hype, formula);
        }

        [Test]
        public void LogOnBalance_is_exactly_one_at_a_zero_balance()
        {
            var (tree, _, formula) = Declare(0.5);

            Assert.AreEqual(BigNumber.One, formula.Compute(tree.Ctx(tree.Tier1)), "log10(1) is zero");
            AssertClose(0.5, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash), "the amp's rate untouched");
        }

        [TestCase(0.5, 9.0, 1.5)]       // 1 + 0.5 x log10(10)
        [TestCase(2.0, 999.0, 7.0)]     // 1 + 2 x log10(1000)
        public void LogOnBalance_scales_the_rate_by_the_decades_of_the_balance(double coefficient, double balance, double factor)
        {
            var (tree, hype, _) = Declare(coefficient);
            tree.Tier1.balances[hype.Id] = balance;

            AssertClose(0.5 * factor, Producer.GetRate(tree.Ctx(tree.Tier1), tree.Cash));
        }

        // The formula on its own, against the same context the gather hands it.
        [Test]
        public void LogOnBalance_computes_against_the_origins_balance()
        {
            var (tree, hype, formula) = Declare(2);
            tree.Tier1.balances[hype.Id] = 99;

            AssertClose(5, formula.Compute(tree.Ctx(tree.Tier1)), "1 + 2 x log10(100)");
        }

        [Test]
        public void Log10_of_a_thousand_is_three()
        {
            AssertClose(3, BigNumber.Log10(1000));
        }

        // Past double range the log is still a plain number: the exponent is
        // the answer's whole part.
        [Test]
        public void Log10_reads_the_exponent_past_double_range()
        {
            AssertClose(400, BigNumber.Log10(BigNumber.FromMantissaExponent(1, 400)));
        }
    }
}
