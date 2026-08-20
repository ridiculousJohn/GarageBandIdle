using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class ScopeChainTests
    {
        [Test]
        public void Deposit_lands_at_the_currency_home_and_bumps_earned_total()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);

            ctx.Deposit("cash", 100);
            ctx.Deposit("records", 3);

            Assert.AreEqual((BigNumber)100, tree.Tier1.balances["cash"]);
            Assert.AreEqual((BigNumber)100, tree.Tier1.earnedTotals["cash"]);
            Assert.AreEqual((BigNumber)3, tree.Root.balances["records"]);
            Assert.IsFalse(tree.Tier1.balances.ContainsKey("records"));
        }

        [Test]
        public void Balance_read_walks_the_chain_to_the_holder()
        {
            var tree = new TestTree();
            tree.Root.balances["records"] = 42;

            Assert.AreEqual((BigNumber)42, tree.Ctx(tree.Tier1).GetBalance("records"));
        }

        [Test]
        public void Balance_absent_everywhere_reads_zero()
        {
            var tree = new TestTree();

            Assert.AreEqual(BigNumber.Zero, tree.Ctx(tree.Tier1).GetBalance("no_such_currency"));
        }

        [Test]
        public void Spending_does_not_reduce_the_earned_total()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit("cash", 300);

            tree.Tier1.balances["cash"] -= 250;   // a purchase spends balance only

            Assert.AreEqual((BigNumber)50, ctx.GetBalance("cash"));
            Assert.AreEqual((BigNumber)300, ctx.GetEarnedTotal("cash"));
        }

        [Test]
        public void Flag_set_anywhere_on_the_chain_reads_set()
        {
            var tree = new TestTree();
            tree.Ch1.flags.Add("album");

            Assert.IsTrue(tree.Ctx(tree.Tier1).IsFlagSet("album"));
            Assert.IsTrue(tree.Ctx(tree.Ch1).IsFlagSet("album"));
            Assert.IsFalse(tree.Ctx(tree.Root).IsFlagSet("album"));   // root's chain is root alone
        }

        [Test]
        public void SetFlag_writes_to_the_declared_home_not_the_acting_scope()
        {
            var tree = new TestTree();

            tree.Ctx(tree.Tier1).SetFlag("album");   // declared by ch1

            Assert.IsFalse(tree.Tier1.flags.Contains("album"));
            Assert.IsTrue(tree.Ch1.flags.Contains("album"));
        }

        [Test]
        public void SetFlag_with_no_declaring_scope_throws()
        {
            var tree = new TestTree();

            Assert.Throws<System.InvalidOperationException>(
                () => tree.Ctx(tree.Tier1).SetFlag("undeclared_flag"));
            Assert.IsFalse(tree.Ctx(tree.Tier1).IsFlagSet("undeclared_flag"));
        }

        [Test]
        public void Owned_count_and_purchased_latch_walk_the_chain()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["drummer"] = 3;
            tree.Ch1.purchasedUpgrades.Add("some_chapter_upgrade");

            Assert.AreEqual(3, tree.Ctx(tree.Tier1).GetOwnedCount("drummer"));
            Assert.AreEqual(0, tree.Ctx(tree.Ch1).GetOwnedCount("drummer"));   // counts never leak upward
            Assert.IsTrue(tree.Ctx(tree.Tier1).IsUpgradePurchased("some_chapter_upgrade"));
        }
    }
}
