using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.UI;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The rung half of the feedback contract (design doc 12.11) over the
    // standing test tree: pressability is the rung's own IsOffered, the disarmed
    // legs come from GateFeedback over offerCondition, and the "would bank"
    // preview runs the same Compute the execution runs (design doc 5).
    public class RungFeedbackTests
    {
        // Computed amounts within tolerance, never bit-exact: BigDouble's
        // base-10 mantissa is binary-inexact for most values, so an exact
        // compare would pass or fail on the luck of the inputs.
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(),
                Math.Max(1e-9, Math.Abs(expected) * 1e-12), what ?? string.Empty);

        // Chapter 1's album release as the content doc authors it: 50 fans and
        // one cover, paying floor(sqrt(fans / 5)) into root records and the
        // chapter's own counter, then clearing the tier.
        private static Rung Release(TestTree tree) => new Rung
        {
            label = "Cut a Demo",
            offerCondition = new All
            {
                conditions =
                {
                    new CurrencyAtLeast { currency = tree.Fans, threshold = 50, uiText = "50 fans" },
                    new BarsCompleted { group = tree.LearnCovers, count = 1, uiText = "Learn a cover" }
                }
            },
            actions =
            {
                new AddCurrency
                {
                    currencies = { tree.Records, tree.Ch1Records },
                    formula = new RootCurveFormula { currency = tree.Fans, divisor = 5, exponent = 0.5 }
                },
                new ResetScope { scope = tree.Tier1Def }
            }
        };

        [Test]
        public void An_offered_rung_has_no_unmet_legs()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = Release(tree);
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(tree.Tier1Def.rung.IsOffered(ctx));
            Assert.IsEmpty(GateFeedback.UnmetLegs(tree.Tier1Def.rung.offerCondition, ctx));
        }

        [Test]
        public void A_disarmed_rung_names_the_leg_that_refused_and_its_progress()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = Release(tree);
            tree.Tier1.balances["fans"] = 37;
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsFalse(tree.Tier1Def.rung.IsOffered(ctx));

            // The cover is learned, so the fan leg is the whole explanation, and
            // it renders as 37/50 from the same fields IsOffered just read.
            var unmet = GateFeedback.UnmetLegs(tree.Tier1Def.rung.offerCondition, ctx);
            Assert.AreEqual(1, unmet.Count);
            Assert.AreEqual("50 fans", unmet[0].uiText);
            Assert.IsTrue(unmet[0].Progress(ctx, out var current, out var target));
            Assert.AreEqual((BigNumber)37, current);
            Assert.AreEqual((BigNumber)50, target);
        }

        [Test]
        public void The_preview_is_what_the_execution_deposits()
        {
            var tree = new TestTree();
            var rung = Release(tree);
            tree.Tier1Def.rung = rung;
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(RungFeedback.TryPreviewPayout(rung, ctx, out var amount, out var currencies));
            AssertClose(3, amount, "would bank");            // floor(sqrt(60 / 5))
            Assert.AreEqual(2, currencies.Count);
            Assert.AreSame(tree.Records, currencies[0]);
            Assert.AreSame(tree.Ch1Records, currencies[1]);

            rung.Execute(ctx);

            // The reset clears tier1, so the banked amounts are read where they
            // landed: root records and the chapter counter both carry exactly
            // the number the preview showed.
            Assert.AreEqual(amount, tree.Root.balances["records"]);
            Assert.AreEqual(amount, tree.Ch1.balances["ch1_records"]);
            Assert.AreEqual(BigNumber.Zero, tree.Tier1.balances["fans"]);
        }

        [Test]
        public void The_preview_does_not_consult_the_gate()
        {
            var tree = new TestTree();
            var rung = Release(tree);
            tree.Tier1Def.rung = rung;
            tree.Tier1.balances["fans"] = 37;
            var ctx = tree.Ctx(tree.Tier1);

            // Both legs refuse, and the preview still answers - telling a player
            // holding at 37 fans what a release would bank is the point of it
            // (design doc 5).
            Assert.IsFalse(rung.IsOffered(ctx));
            Assert.IsTrue(RungFeedback.TryPreviewPayout(rung, ctx, out var amount, out var currencies));
            AssertClose(2, amount, "would bank while disarmed");   // floor(sqrt(37 / 5))
            Assert.AreEqual(2, currencies.Count);
        }

        [Test]
        public void A_rung_that_does_not_open_with_an_AddCurrency_previews_nothing()
        {
            var tree = new TestTree();
            tree.Tier1Def.rung = Release(tree);
            // The capstone's shape: it fires the tier's own rung first, so the
            // opening action is not a payout and no number is guessed at.
            var capstone = new Rung
            {
                label = "Sign the Deal",
                offerCondition = new Always(),
                actions =
                {
                    new ExecuteRung { tier = tree.Tier1Def },
                    new AddCurrency { currencies = { tree.Roadies }, amount = 1 }
                }
            };
            tree.Ch1Def.rung = capstone;
            var ctx = tree.Ctx(tree.Ch1);

            Assert.IsFalse(RungFeedback.TryPreviewPayout(capstone, ctx, out var amount, out var currencies));
            Assert.AreEqual(BigNumber.Zero, amount);
            Assert.IsEmpty(currencies);

            // An empty list has no first action to run, and answers the same way.
            var silent = new Rung { label = "Nothing", offerCondition = new Always() };
            Assert.IsFalse(RungFeedback.TryPreviewPayout(silent, ctx, out var none, out var noCurrencies));
            Assert.AreEqual(BigNumber.Zero, none);
            Assert.IsEmpty(noCurrencies);
        }

        [Test]
        public void Only_the_first_AddCurrency_is_previewed()
        {
            var tree = new TestTree();
            var rung = new Rung
            {
                label = "Pay Twice",
                offerCondition = new Always(),
                actions =
                {
                    new AddCurrency { currencies = { tree.Fans }, amount = 5 },
                    new AddCurrency
                    {
                        currencies = { tree.Fans },
                        formula = new RootCurveFormula { currency = tree.Fans, divisor = 5, exponent = 0.5 }
                    }
                }
            };
            tree.Tier1Def.rung = rung;
            tree.Tier1.balances["fans"] = 20;
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsTrue(RungFeedback.TryPreviewPayout(rung, ctx, out var amount, out _));
            AssertClose(5, amount, "would bank");

            rung.Execute(ctx);

            // 20 + 5, then floor(sqrt(25 / 5)) = 2 on top of that: the second
            // action reads what the first deposited, which is why the preview
            // stops at the first and never sums the list.
            AssertClose(27, tree.Tier1.balances["fans"], "fans after both actions");
        }
    }
}
