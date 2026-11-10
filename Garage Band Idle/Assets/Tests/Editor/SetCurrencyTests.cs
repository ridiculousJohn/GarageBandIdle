using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The overwrite (design doc 12.5): a currency's balance replaced with a
    // value - the formula when one is authored, else the constant - computed
    // in the acting context and written at the currency's home under no
    // multiplier. It records a number rather than paying one, so neither the
    // earned total nor the lifetime total moves.
    public class SetCurrencyTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // ---- the overwrite ----

        // The balance becomes the amount, whatever it held - never the sum.
        [TestCase(50.0, 7.0)]
        [TestCase(3.0, 40.0)]
        [TestCase(50.0, 0.0)]
        public void SetCurrency_replaces_the_balance_rather_than_adding_to_it(double before, double set)
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit(tree.Fans.Id, before);

            new SetCurrency { currency = tree.Fans, amount = set }.Execute(ctx);

            AssertClose(set, tree.Tier1.balances["fans"], $"{before} set to {set}");
        }

        // A record is neither earning nor spending: the totals the deposit
        // moved stay where the deposit left them, downward and upward alike.
        [TestCase(50.0, 7.0)]
        [TestCase(3.0, 40.0)]
        public void SetCurrency_leaves_the_earned_and_lifetime_totals_alone(double before, double set)
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit(tree.Fans.Id, before);

            new SetCurrency { currency = tree.Fans, amount = set }.Execute(ctx);

            AssertClose(set, ctx.GetBalance(tree.Fans.Id), "the balance moved");
            AssertClose(before, ctx.GetEarnedTotal(tree.Fans.Id), "the earned total is the deposit's");
            AssertClose(before, ctx.GetLifetimeTotal(tree.Fans.Id), "the lifetime total is the deposit's");
        }

        // Run in the tier, writing the chapter's currency: the write walks
        // outward to the home, and the tier holds no copy of it.
        [Test]
        public void SetCurrency_writes_at_the_currencys_home()
        {
            var tree = new TestTree();
            tree.Ctx(tree.Ch1).Deposit(tree.Ch1Records.Id, 3);

            new SetCurrency { currency = tree.Ch1Records, amount = 12 }.Execute(tree.Ctx(tree.Tier1));

            AssertClose(12, tree.Ch1.balances["ch1_records"], "the chapter's balance");
            Assert.IsFalse(tree.Tier1.balances.ContainsKey("ch1_records"), "nothing lands in the tier");
        }

        // The formula is computed in the ACTING scope: fans is homed at tier1,
        // below ch1_records' home, so only a read from the tier can see it.
        // floor((7.6 / 1) ^ 1) = 7.
        [Test]
        public void SetCurrency_with_a_formula_writes_the_floored_balance_read_in_the_acting_scope()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 7.6;
            tree.Ch1.balances["ch1_records"] = 50;
            var set = new SetCurrency
            {
                currency = tree.Ch1Records,
                formula = new RootCurveFormula { currency = tree.Fans, reads = PayoutTotal.Balance, divisor = 1, exponent = 1 },
            };

            set.Execute(tree.Ctx(tree.Tier1));

            Assert.AreEqual((BigNumber)7, tree.Ch1.balances["ch1_records"], "floor(7.6)");
            AssertClose(7.6, tree.Tier1.balances["fans"], "the currency read is untouched");
        }

        // An authored formula wins over the constant beside it.
        [Test]
        public void SetCurrency_with_a_formula_ignores_the_constant()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 9;
            var set = new SetCurrency
            {
                currency = tree.Ch1Records,
                amount = 100,
                formula = new RootCurveFormula { currency = tree.Fans, reads = PayoutTotal.Balance, divisor = 1, exponent = 1 },
            };

            set.Execute(tree.Ctx(tree.Tier1));

            Assert.AreEqual((BigNumber)9, tree.Ch1.balances["ch1_records"]);
        }

        // ---- no multiplier applies ----

        // The standing tree, a rung on tier1 holding the actions under test,
        // and a live session over the built tree - PayoutStatTests' shape, so
        // the press runs through the same command pipeline.
        private class Fixture
        {
            public readonly TestTree Tree = new TestTree();
            public GameSession Session { get; private set; }

            public void RungActions(params GameAction[] actions)
            {
                var rung = new Rung { label = "Record", offerCondition = new Always() };
                rung.actions.AddRange(actions);
                Tree.Tier1Def.rung = rung;
            }

            public void Apply(ScopeDefinition site, string id, string target, string stat, double multiplier)
            {
                var modifier = TestTree.MakeDefinition<ModifierDefinition>(id);
                modifier.effects.Add(new Effect { target = target, stat = stat, multiplier = multiplier });
                site.modifiers.Add(modifier);
                site.permanentModifiers.Add(modifier);
            }

            public void Build()
            {
                Tree.Rebuild();
                Session = new GameSession(Tree.Root, ScriptableObject.CreateInstance<GameConfig>());
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public GameContext AtTier => Tree.Ctx(Tree.Tier1);

            public void Press()
            {
                bool? ran = null;
                Session.TryRung(AtTier, r => ran = r);
                Assert.AreEqual(true, ran, "the rung ran");
            }
        }

        // A payout x3 at ch1_records' home triples what an AddCurrency pays
        // into it and leaves what SetCurrency writes alone.
        [Test]
        public void A_payout_effect_leaves_the_written_value_alone_while_it_triples_a_grant()
        {
            var f = new Fixture();
            f.RungActions(new SetCurrency { currency = f.Tree.Ch1Records, amount = 10 });
            f.Apply(f.Tree.Ch1Def, "triple_payout", "ch1_records", Stat.Payout, 3);
            f.Build();
            f.Tree.Ch1.balances["ch1_records"] = 50;

            f.Press();
            AssertClose(10, f.Tree.Ch1.balances["ch1_records"], "the constant, unscaled");

            var grant = new AddCurrency { currencies = { f.Tree.Ch1Records }, amount = 10 };
            AssertClose(30, grant.AmountFor(f.AtTier, f.Tree.Ch1Records), "the effect is live on a grant");
            grant.Execute(f.AtTier);
            AssertClose(40, f.Tree.Ch1.balances["ch1_records"], "10 written, then 10 x 3 paid");
        }

        // An exponent x2 on ch1_records raises floor(100 ^ 1) to
        // floor(100 ^ 2) = 10000 on a grant; the write stays 100.
        [Test]
        public void An_exponent_effect_leaves_a_written_formula_value_alone()
        {
            var f = new Fixture();
            var curve = new RootCurveFormula { currency = f.Tree.Fans, reads = PayoutTotal.Balance, divisor = 1, exponent = 1 };
            f.RungActions(new SetCurrency { currency = f.Tree.Ch1Records, formula = curve });
            f.Apply(f.Tree.Ch1Def, "exponent_boost", "ch1_records", Stat.Exponent, 2);
            f.Build();
            f.Tree.Tier1.balances["fans"] = 100;

            var grant = new AddCurrency { currencies = { f.Tree.Ch1Records }, formula = curve };
            AssertClose(10000, grant.AmountFor(f.AtTier, f.Tree.Ch1Records), "the effect is live on a grant");

            f.Press();

            AssertClose(100, f.Tree.Ch1.balances["ch1_records"], "floor(100 ^ 1), unscaled");
        }

        // ---- inside a list ----

        // The reset clears the tier first and the write lands in the fresh
        // state: fans holds the written 5 and its earned total stays at the
        // reset's zero, while the cash the reset took stays gone.
        [Test]
        public void A_write_after_a_reset_in_one_list_lands_in_the_fresh_state()
        {
            var tree = new TestTree();
            tree.Tier1Trigger.condition = new Not { condition = new Always() };   // never swept; run by hand
            tree.Tier1Trigger.actions.Add(new ResetScope { scope = tree.Tier1Def });
            tree.Tier1Trigger.actions.Add(new SetCurrency { currency = tree.Fans, amount = 5 });
            tree.Rebuild();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit(tree.Fans.Id, 50);
            ctx.Deposit(tree.Cash.Id, 42);

            Assert.IsTrue(ActionList.TryRun(tree.Tier1Trigger.actions, ctx), "the whole list ran");

            AssertClose(5, tree.Tier1.balances["fans"], "written after the reset");
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.earnedTotals["fans"], "the write earned nothing");
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["cash"], "the reset ran");
        }

        // ---- the write's own refusal ----

        [Test]
        public void SetBalance_with_a_negative_amount_throws_and_changes_nothing()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit(tree.Fans.Id, 5);

            Assert.Throws<InvalidOperationException>(() => ctx.SetBalance(tree.Fans.Id, -1));

            AssertClose(5, ctx.GetBalance(tree.Fans.Id), "the balance");
            AssertClose(5, ctx.GetEarnedTotal(tree.Fans.Id), "the earned total");
            AssertClose(5, ctx.GetLifetimeTotal(tree.Fans.Id), "the lifetime total");
        }
    }
}
