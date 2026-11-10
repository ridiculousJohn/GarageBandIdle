using System;
using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // A currency collects what multiplies it (design doc 12.5): an AddCurrency
    // pays each target its own amount, scaled by the payout and exponent
    // multipliers gathered at that currency's home. An effect therefore
    // reaches a payout only from the paid currency's home or further out, and
    // only by naming the paid currency - by id, by tag, or by the wildcard.
    //
    // Every row runs on the standing test tree, with the effect authored as a
    // modifier declared on a scope and listed in that scope's permanent
    // modifiers, and reads balances after the rung is pressed through the
    // session. The tree's own root modifiers name rate and yield only, so a
    // fresh payout is the authored amount.
    public class PayoutStatTests
    {
        // Computed amounts within tolerance, never bit-exact: BigDouble's
        // base-10 mantissa is binary-inexact for most values.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // The standing tree, a rung on tier1 holding the payouts under test, and
        // a live session over the built tree. Authoring happens before Build,
        // since the plans are compiled when the tree is built.
        private class Fixture
        {
            public readonly TestTree Tree = new TestTree();
            public GameSession Session { get; private set; }

            public void PayFromTier(params AddCurrency[] payouts)
            {
                var rung = new Rung { label = "Pay", offerCondition = new Always() };
                rung.actions.AddRange(payouts);
                Tree.Tier1Def.rung = rung;
            }

            // A modifier on one effect, declared at the site and applied there.
            public ModifierDefinition Apply(ScopeDefinition site, string id, string target, string stat,
                                            double multiplier, Condition appliesWhen = null)
            {
                var modifier = TestTree.MakeDefinition<ModifierDefinition>(id);
                modifier.appliesWhen = appliesWhen;
                modifier.effects.Add(new Effect { target = target, stat = stat, multiplier = multiplier });
                site.modifiers.Add(modifier);
                site.permanentModifiers.Add(modifier);
                return modifier;
            }

            // The switch a live player makes: stamped now, so no away window is
            // owed and the phase lands Live.
            public void Build()
            {
                Tree.Rebuild();
                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                Session = new GameSession(Tree.Root, config);
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
            }

            public GameContext AtTier => Tree.Ctx(Tree.Tier1);

            // One press of tier1's rung through the session.
            public void Press()
            {
                bool? ran = null;
                Session.TryRung(AtTier, r => ran = r);
                Assert.AreEqual(true, ran, "the rung ran");
            }
        }

        // RootCurve over fans earned this round, total 1024, exponent 0.2: a
        // payout of 4 before any exponent effect.
        private static AddCurrency CurvePaying(TestTree tree, CurrencyDefinition paid) => new AddCurrency
        {
            currencies = { paid },
            formula = new RootCurveFormula { currency = tree.Fans, divisor = 1, exponent = 0.2 }
        };

        // ---- payout ----

        [Test]
        public void With_no_effect_a_constant_pays_exactly_its_amount()
        {
            var f = new Fixture();
            var pay = new AddCurrency { currencies = { f.Tree.Ch1Records }, amount = 10 };
            f.PayFromTier(pay);
            f.Build();

            Assert.AreEqual((BigNumber)10, pay.AmountFor(f.AtTier, f.Tree.Ch1Records));
            f.Press();

            Assert.AreEqual((BigNumber)10, f.Tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void With_no_effect_a_formula_pays_exactly_its_amount()
        {
            var f = new Fixture();
            var curve = new RootCurveFormula { currency = f.Tree.Fans, divisor = 5, exponent = 0.5 };
            var pay = new AddCurrency { currencies = { f.Tree.Ch1Records }, formula = curve };
            f.PayFromTier(pay);
            f.Build();
            f.Tree.Tier1.earnedTotals["fans"] = 60;

            Assert.AreEqual((BigNumber)3, curve.Compute(f.AtTier), "floor(sqrt(60 / 5))");
            Assert.AreEqual(curve.Compute(f.AtTier), pay.AmountFor(f.AtTier, f.Tree.Ch1Records));
            f.Press();

            Assert.AreEqual((BigNumber)3, f.Tree.Ch1.balances["ch1_records"]);
        }

        [Test]
        public void A_payout_effect_at_the_currencys_home_triples_a_formula_amount()
        {
            var f = new Fixture();
            f.PayFromTier(new AddCurrency
            {
                currencies = { f.Tree.Ch1Records },
                formula = new RootCurveFormula { currency = f.Tree.Fans, divisor = 5, exponent = 0.5 }
            });
            f.Apply(f.Tree.Ch1Def, "triple_payout", "ch1_records", Stat.Payout, 3);
            f.Build();
            f.Tree.Tier1.earnedTotals["fans"] = 60;

            f.Press();

            AssertClose(9, f.Tree.Ch1.balances["ch1_records"], "floor(sqrt(60 / 5)) x 3");
        }

        // The target names the paid currency by its id, by a tag it carries, or
        // not at all (the wildcard) - each one reaches the payout alike.
        [TestCase("tapes")]
        [TestCase("prestige")]
        [TestCase("")]
        public void A_payout_effect_names_the_paid_currency_by_id_tag_or_wildcard(string target)
        {
            var f = new Fixture();
            f.Tree.Ch1Def.declaredTags.Add("prestige");
            var tapes = TestTree.DeclareCurrency(f.Tree.Ch1Def, "tapes", "prestige");
            var pay = new AddCurrency { currencies = { tapes }, amount = 10 };
            f.PayFromTier(pay);
            f.Apply(f.Tree.Ch1Def, "triple_payout", target, Stat.Payout, 3);
            f.Build();

            AssertClose(30, pay.AmountFor(f.AtTier, tapes), "the amount the action computes");
            f.Press();

            AssertClose(30, f.Tree.Ch1.balances["tapes"], "10 x 3");
        }

        // The stat leg is exact: a rate or yield effect on the paid currency is
        // not a payout effect.
        [TestCase(Stat.Yield)]
        [TestCase(Stat.Rate)]
        public void A_rate_or_yield_effect_leaves_what_an_AddCurrency_pays_alone(string stat)
        {
            var f = new Fixture();
            var pay = new AddCurrency { currencies = { f.Tree.Cash }, amount = 10 };
            f.PayFromTier(pay);
            f.Apply(f.Tree.Tier1Def, "triple_other", "cash", stat, 3);
            f.Build();

            AssertClose(10, pay.AmountFor(f.AtTier, f.Tree.Cash));
            f.Press();

            AssertClose(10, f.Tree.Tier1.balances["cash"], "the constant, unscaled");
        }

        // And the other direction: a payout effect is not a rate or a yield
        // effect, so the tap's lump and the amp's rate read as they do fresh.
        [Test]
        public void A_payout_effect_leaves_the_tap_yield_and_the_rate_alone()
        {
            var f = new Fixture();
            var pay = new AddCurrency { currencies = { f.Tree.Cash }, amount = 10 };
            f.PayFromTier(pay);
            f.Apply(f.Tree.Tier1Def, "triple_payout", "cash", Stat.Payout, 3);
            f.Build();

            AssertClose(30, pay.AmountFor(f.AtTier, f.Tree.Cash), "the effect is live on the payout");

            f.Session.FireProducer(f.AtTier, f.Tree.TapProducer);
            AssertClose(1, f.Tree.Tier1.balances["cash"], "one tap pays its authored 1");

            f.Tree.Tier1.generatorCounts[f.Tree.PracticeAmp.Id] = 1;
            AssertClose(0.5, Producer.GetRate(f.AtTier, f.Tree.Cash), "one amp's authored rate");
        }

        // The currency is the chapter's and the action acts in the tier. The
        // multipliers are gathered at the currency's home, so an effect at the
        // home or outward of it counts, and one only in the tier below it does
        // not.
        [TestCase("chapter", 30)]
        [TestCase("root", 30)]
        [TestCase("tier", 10)]
        public void A_payout_effect_counts_at_the_currencys_home_or_outward_and_never_below(string site, double expected)
        {
            var f = new Fixture();
            var pay = new AddCurrency { currencies = { f.Tree.Ch1Records }, amount = 10 };
            f.PayFromTier(pay);
            ScopeDefinition at = site == "root" ? f.Tree.RootDef
                : site == "chapter" ? f.Tree.Ch1Def
                : (ScopeDefinition)f.Tree.Tier1Def;
            f.Apply(at, "triple_payout", "ch1_records", Stat.Payout, 3);
            f.Build();

            AssertClose(expected, pay.AmountFor(f.AtTier, f.Tree.Ch1Records));
            f.Press();

            AssertClose(expected, f.Tree.Ch1.balances["ch1_records"]);
        }

        // appliesWhen gates a payout effect like any other: the latch is
        // written by the real purchase, and only the press after it is tripled.
        [Test]
        public void A_payout_effect_counts_only_while_its_condition_holds()
        {
            var f = new Fixture();
            var deal = TestTree.MakeDefinition<UpgradeDefinition>("payout_deal");
            deal.gate = new Always();
            deal.costCurrency = f.Tree.Cash;
            deal.cost = 10;
            f.Tree.Tier1Def.upgrades.Add(deal);
            f.PayFromTier(new AddCurrency { currencies = { f.Tree.Fans }, amount = 10 });
            f.Apply(f.Tree.Tier1Def, "deal_payout", "fans", Stat.Payout, 3, new UpgradePurchased { upgrade = deal });
            f.Build();

            f.Press();
            AssertClose(10, f.Tree.Tier1.balances["fans"], "unbought, the effect is shut");

            f.AtTier.Deposit(f.Tree.Cash.Id, 10);
            bool? bought = null;
            f.Session.TryBuy(f.AtTier, deal, r => bought = r);
            Assert.AreEqual(true, bought, "the upgrade is bought");

            f.Press();
            AssertClose(40, f.Tree.Tier1.balances["fans"], "10, then 10 x 3");
        }

        // One Execute, two targets, a payout effect on the first alone: each
        // target is paid its own amount.
        [Test]
        public void Two_targets_are_each_paid_their_own_payout()
        {
            var f = new Fixture();
            var pay = new AddCurrency { currencies = { f.Tree.Ch1Records, f.Tree.Records }, amount = 5 };
            f.PayFromTier(pay);
            f.Apply(f.Tree.Ch1Def, "double_payout", "ch1_records", Stat.Payout, 2);
            f.Build();

            AssertClose(10, pay.AmountFor(f.AtTier, f.Tree.Ch1Records));
            AssertClose(5, pay.AmountFor(f.AtTier, f.Tree.Records));
            f.Press();

            AssertClose(10, f.Tree.Ch1.balances["ch1_records"], "5 x 2");
            AssertClose(5, f.Tree.Root.balances["records"], "the plain amount");
        }

        // One amount per target: lists of different lengths are a code bug,
        // refused before anything is written.
        [Test]
        public void DepositAll_with_mismatched_lists_throws_and_writes_nothing()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);

            Assert.Throws<InvalidOperationException>(() => ctx.DepositAll(
                new List<CurrencyDefinition> { tree.Records, tree.Ch1Records },
                new List<BigNumber> { 5 }));

            Assert.AreEqual(BigNumber.Zero, tree.Root.balances["records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Ch1.balances["ch1_records"]);
        }

        // ---- exponent ----

        // The exponent effect addresses the currency PAID: on ch1_records it
        // raises the exponent 0.2 to 0.3, and on fans - the currency the
        // formula reads - it changes nothing. Both are applied at the chapter,
        // which is outward of fans' home as well as ch1_records'.
        [TestCase(null, 4)]                // floor(1024 ^ 0.2)
        [TestCase("ch1_records", 8)]       // floor(1024 ^ 0.3)
        [TestCase("fans", 4)]
        public void An_exponent_effect_scales_the_exponent_of_the_currency_paid(string target, double expected)
        {
            var f = new Fixture();
            var pay = CurvePaying(f.Tree, f.Tree.Ch1Records);
            f.PayFromTier(pay);
            if (target != null)
                f.Apply(f.Tree.Ch1Def, "exponent_boost", target, Stat.Exponent, 1.5);
            f.Build();
            f.Tree.Tier1.earnedTotals["fans"] = 1024;

            AssertClose(expected, pay.AmountFor(f.AtTier, f.Tree.Ch1Records));
            f.Press();

            AssertClose(expected, f.Tree.Ch1.balances["ch1_records"]);
        }

        // A Linear stack granted twice is 1 + 0.5 x 2 = 2, so the exponent is
        // 0.4 and the payout floor(1024 ^ 0.4) = 16.
        [Test]
        public void A_linear_exponent_stack_granted_twice_doubles_the_exponent()
        {
            var f = new Fixture();
            var stack = TestTree.MakeDefinition<ModifierDefinition>("exponent_stack");
            stack.stacking = StackingKind.Linear;
            stack.effects.Add(new Effect { target = "ch1_records", stat = Stat.Exponent, multiplier = 1.5 });
            f.Tree.Ch1Def.modifiers.Add(stack);
            f.PayFromTier(CurvePaying(f.Tree, f.Tree.Ch1Records));
            f.Build();
            f.Tree.Tier1.earnedTotals["fans"] = 1024;

            var grant = new AddModifier { scope = f.Tree.Ch1Def, modifier = stack };
            grant.Execute(f.AtTier);
            grant.Execute(f.AtTier);
            Assert.AreEqual(2, f.Tree.Ch1.modifierStacks["exponent_stack"]);

            f.Press();

            AssertClose(16, f.Tree.Ch1.balances["ch1_records"]);
        }

        // The exponent applies inside the floor and the payout outside it:
        // floor(1024 ^ 0.3) x 3.
        [Test]
        public void An_exponent_and_a_payout_effect_compose()
        {
            var f = new Fixture();
            f.PayFromTier(CurvePaying(f.Tree, f.Tree.Ch1Records));
            f.Apply(f.Tree.Ch1Def, "exponent_boost", "ch1_records", Stat.Exponent, 1.5);
            f.Apply(f.Tree.Ch1Def, "triple_payout", "ch1_records", Stat.Payout, 3);
            f.Build();
            f.Tree.Tier1.earnedTotals["fans"] = 1024;

            f.Press();

            AssertClose(24, f.Tree.Ch1.balances["ch1_records"], "8 x 3");
        }

        // A constant has no exponent to scale, authored as an amount or as a
        // ConstantFormula.
        [Test]
        public void An_exponent_effect_leaves_a_constant_payout_alone()
        {
            var f = new Fixture();
            var amount = new AddCurrency { currencies = { f.Tree.Ch1Records }, amount = 10 };
            var constant = new AddCurrency
            {
                currencies = { f.Tree.Ch1Records },
                formula = new ConstantFormula { value = 7 }
            };
            f.PayFromTier(amount, constant);
            f.Apply(f.Tree.Ch1Def, "exponent_boost", "ch1_records", Stat.Exponent, 1.5);
            f.Build();

            AssertClose(10, amount.AmountFor(f.AtTier, f.Tree.Ch1Records));
            AssertClose(7, constant.AmountFor(f.AtTier, f.Tree.Ch1Records));
            f.Press();

            AssertClose(17, f.Tree.Ch1.balances["ch1_records"], "10 + 7");
        }
    }
}
