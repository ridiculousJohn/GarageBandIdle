using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class PayoutFormulaTests
    {
        // The Chapter 1 album payout: floor((fans/5)^0.5). Values from the
        // content doc's walkthroughs. Fans are never spent and both totals
        // clear with the tier, so the balance and the earned total carry the
        // same number here and the default selector reads either one.
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
            tree.Tier1.earnedTotals["fans"] = fans;
            var formula = new RootCurveFormula { currency = tree.Fans, divisor = 5, exponent = 0.5 };

            Assert.AreEqual((BigNumber)expected, formula.Compute(tree.Ctx(tree.Tier1)));
        }

        // A prestige payout is about the run just played (12.5), so the default
        // is what the round earned and a spend never lowers it.
        [Test]
        public void The_default_selector_is_what_this_round_earned()
        {
            var tree = new TestTree();
            tree.Tier1.earnedTotals["fans"] = 500;
            tree.Tier1.balances["fans"] = 100;
            var formula = new RootCurveFormula { currency = tree.Fans, divisor = 5, exponent = 0.5 };

            Assert.AreEqual(PayoutTotal.EarnedThisRound, formula.reads, "the unauthored default");
            Assert.AreEqual((BigNumber)10, formula.Compute(tree.Ctx(tree.Tier1)), "the 500 earned, not the 100 left");
        }

        [Test]
        public void The_balance_selector_reads_what_a_spend_left_standing()
        {
            var tree = new TestTree();
            tree.Tier1.earnedTotals["fans"] = 500;
            tree.Tier1.balances["fans"] = 100;
            var formula = new RootCurveFormula
            {
                currency = tree.Fans, reads = PayoutTotal.Balance, divisor = 5, exponent = 0.5,
            };

            Assert.AreEqual((BigNumber)4, formula.Compute(tree.Ctx(tree.Tier1)), "floor(sqrt(20))");
        }

        // The lifetime total is the one economy fact a reset does not clear
        // (12.3), so it reads across rounds while the earned total starts over.
        [Test]
        public void The_lifetime_selector_reads_across_a_reset()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.DepositResolved("fans", 400);
            tree.Tier1.ClearSubtree(tree.Now);
            ctx.DepositResolved("fans", 100);

            var lifetime = new RootCurveFormula
            {
                currency = tree.Fans, reads = PayoutTotal.Lifetime, divisor = 5, exponent = 0.5,
            };
            var thisRound = new RootCurveFormula
            {
                currency = tree.Fans, reads = PayoutTotal.EarnedThisRound, divisor = 5, exponent = 0.5,
            };

            Assert.AreEqual((BigNumber)500, ctx.GetLifetimeTotal("fans"), "every deposit ever made");
            Assert.AreEqual((BigNumber)10, lifetime.Compute(ctx), "floor(sqrt(100))");
            Assert.AreEqual((BigNumber)4, thisRound.Compute(ctx), "the second deposit alone, floor(sqrt(20))");
        }

        [Test]
        public void Constant_is_constant()
        {
            var tree = new TestTree();

            Assert.AreEqual(BigNumber.One, new ConstantFormula { value = 1 }.Compute(tree.Ctx(tree.Root)));
        }
    }
}
