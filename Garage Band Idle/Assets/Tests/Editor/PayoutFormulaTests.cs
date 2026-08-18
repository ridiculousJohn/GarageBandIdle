using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class PayoutFormulaTests
    {
        // The Chapter 1 album payout: floor((fans/5)^0.5). Values from the
        // content doc's walkthroughs.
        [TestCase(50, 3)]
        [TestCase(60, 3)]
        [TestCase(125, 5)]
        [TestCase(500, 10)]
        [TestCase(2000, 20)]
        [TestCase(0, 0)]
        public void RootCurve_matches_the_chapter1_payout_table(double fans, double expected)
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = fans;
            var formula = new RootCurveFormula { currencyId = "fans", divisor = 5, exponent = 0.5 };

            Assert.AreEqual((BigNumber)expected, formula.Compute(tree.Ctx(tree.Tier1)));
        }

        [Test]
        public void Constant_is_constant()
        {
            var tree = new TestTree();

            Assert.AreEqual(BigNumber.One, new ConstantFormula { value = 1 }.Compute(tree.Ctx(tree.Root)));
        }
    }
}
