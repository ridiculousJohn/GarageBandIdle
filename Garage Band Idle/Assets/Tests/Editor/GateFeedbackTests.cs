using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.UI;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The generic half of the feedback contract (design doc 12.11) over the
    // standing test tree: what a gate's legs ARE, which of them a context leaves
    // unmet, the numbers a threshold leg renders its progress from, and the
    // text a leg renders.
    public class GateFeedbackTests
    {
        [Test]
        public void Legs_of_an_All_are_its_own_conditions_in_authored_order()
        {
            var tree = new TestTree();
            var fans = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 };
            var cover = new BarsCompleted { group = tree.LearnCovers, count = 1 };
            var gate = new All { conditions = { fans, cover } };

            var legs = GateFeedback.Legs(gate);

            // The All's own list, not a copy: the legs the screen explains are
            // the same objects the gate evaluates.
            Assert.AreSame(gate.conditions, legs);
            Assert.AreEqual(2, legs.Count);
            Assert.AreSame(fans, legs[0]);
            Assert.AreSame(cover, legs[1]);
        }

        [Test]
        public void A_non_All_gate_is_one_leg_and_is_never_decomposed()
        {
            var tree = new TestTree();
            var threshold = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 };
            var not = new Not { condition = threshold };
            var any = new Any { conditions = { threshold, new Always() } };

            // A Not or an Any carries its own uiText and answers as one leg -
            // naming its operands would name requirements the gate never made.
            Assert.AreEqual(1, GateFeedback.Legs(threshold).Count);
            Assert.AreSame(threshold, GateFeedback.Legs(threshold)[0]);
            Assert.AreEqual(1, GateFeedback.Legs(not).Count);
            Assert.AreSame(not, GateFeedback.Legs(not)[0]);
            Assert.AreEqual(1, GateFeedback.Legs(any).Count);
            Assert.AreSame(any, GateFeedback.Legs(any)[0]);
        }

        [Test]
        public void A_null_gate_has_no_legs()
        {
            // The load pass refuses a null gate; this is the fail-closed
            // backstop behind it, so the rendering has nothing to say.
            Assert.AreEqual(0, GateFeedback.Legs(null).Count);
        }

        [Test]
        public void A_met_All_has_no_unmet_legs()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.generatorCounts["practice_amp"] = 4;
            var ctx = tree.Ctx(tree.Tier1);
            var gate = new All
            {
                conditions =
                {
                    new CurrencyAtLeast { currency = tree.Fans, threshold = 50, uiText = "50 fans" },
                    new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 4, uiText = "4 amps" }
                }
            };

            Assert.IsTrue(gate.Evaluate(ctx));
            Assert.IsEmpty(GateFeedback.UnmetLegs(gate, ctx));
        }

        [Test]
        public void An_unmet_All_names_every_unmet_leg_in_authored_order()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 37;
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            tree.Ch1.flags.Add("album");
            var ctx = tree.Ctx(tree.Tier1);
            var gate = new All
            {
                conditions =
                {
                    new CurrencyAtLeast { currency = tree.Fans, threshold = 50, uiText = "50 fans" },
                    new FlagSet { flagId = "album", uiText = "Cut a demo" },
                    new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 4, uiText = "4 amps" }
                }
            };

            var unmet = GateFeedback.UnmetLegs(gate, ctx);

            // Each leg is judged on its own, so the met middle one drops out and
            // BOTH refusals are named - the All itself stops at the first.
            Assert.AreEqual(2, unmet.Count);
            Assert.AreEqual("50 fans", unmet[0].uiText);
            Assert.AreEqual("4 amps", unmet[1].uiText);
        }

        [Test]
        public void A_single_unmet_gate_is_its_own_unmet_leg()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 37;
            var ctx = tree.Ctx(tree.Tier1);
            var gate = new CurrencyAtLeast { currency = tree.Fans, threshold = 50, uiText = "50 fans" };

            var unmet = GateFeedback.UnmetLegs(gate, ctx);

            Assert.AreEqual(1, unmet.Count);
            Assert.AreSame(gate, unmet[0]);
        }

        [Test]
        public void CurrencyAtLeast_reports_the_balance_against_the_threshold()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 37;
            var ctx = tree.Ctx(tree.Tier1);
            var leg = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 };

            Assert.IsTrue(leg.Progress(ctx, out var current, out var target));
            Assert.AreEqual((BigNumber)37, current);
            Assert.AreEqual((BigNumber)50, target);

            // Raw numbers, never clamped: a met leg reads 60/50, not 50/50.
            tree.Tier1.balances["fans"] = 60;
            Assert.IsTrue(leg.Progress(ctx, out var met, out var metTarget));
            Assert.AreEqual((BigNumber)60, met);
            Assert.AreEqual((BigNumber)50, metTarget);
        }

        [Test]
        public void EarnedTotalAtLeast_reports_the_earned_total_not_the_balance()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            ctx.Deposit("cash", 300);
            tree.Tier1.balances["cash"] = 10;   // spent down
            var leg = new EarnedTotalAtLeast { currency = tree.Cash, threshold = 250 };

            Assert.IsTrue(leg.Progress(ctx, out var current, out var target));
            Assert.AreEqual((BigNumber)300, current);
            Assert.AreEqual((BigNumber)250, target);
        }

        [Test]
        public void OwnedCountAtLeast_reports_the_generator_count()
        {
            var tree = new TestTree();
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            var ctx = tree.Ctx(tree.Tier1);
            var leg = new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 4 };

            Assert.IsTrue(leg.Progress(ctx, out var current, out var target));
            Assert.AreEqual((BigNumber)3, current);
            Assert.AreEqual((BigNumber)4, target);
        }

        [Test]
        public void BarsCompleted_reports_the_completed_count()
        {
            var tree = new TestTree();
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;   // exactly full
            tree.Tier1.barProgress[tree.Cover2.Id] = 299;   // just short
            var ctx = tree.Ctx(tree.Tier1);
            var leg = new BarsCompleted { group = tree.LearnCovers, count = 2 };

            Assert.IsTrue(leg.Progress(ctx, out var current, out var target));
            Assert.AreEqual((BigNumber)1, current);
            Assert.AreEqual((BigNumber)2, target);
        }

        [Test]
        public void The_kinds_with_no_number_report_no_progress()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 37;
            var ctx = tree.Ctx(tree.Tier1);
            var threshold = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 };

            AssertNoProgress(new FlagSet { flagId = "album" }, ctx, "FlagSet");
            AssertNoProgress(new Always(), ctx, "Always");
            // A compound holds no number of its own even when an operand does:
            // the rendering walks to the legs for that, and a Not's number would
            // read backwards anyway.
            AssertNoProgress(new Not { condition = threshold }, ctx, "Not");
            AssertNoProgress(new All { conditions = { threshold } }, ctx, "All");
        }

        [Test]
        public void Progress_agrees_with_Evaluate_on_both_sides_of_the_threshold()
        {
            var tree = new TestTree();
            var ctx = tree.Ctx(tree.Tier1);
            var balance = new CurrencyAtLeast { currency = tree.Fans, threshold = 50 };
            var earned = new EarnedTotalAtLeast { currency = tree.Cash, threshold = 250 };
            var owned = new OwnedCountAtLeast { generator = tree.PracticeAmp, count = 4 };
            var bars = new BarsCompleted { group = tree.LearnCovers, count = 2 };

            // Short of all four thresholds.
            tree.Tier1.balances["fans"] = 37;
            ctx.Deposit("cash", 100);
            tree.Tier1.generatorCounts["practice_amp"] = 3;
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;
            AssertAgrees(balance, ctx, "CurrencyAtLeast short");
            AssertAgrees(earned, ctx, "EarnedTotalAtLeast short");
            AssertAgrees(owned, ctx, "OwnedCountAtLeast short");
            AssertAgrees(bars, ctx, "BarsCompleted short");
            Assert.IsFalse(balance.Evaluate(ctx));
            Assert.IsFalse(earned.Evaluate(ctx));
            Assert.IsFalse(owned.Evaluate(ctx));
            Assert.IsFalse(bars.Evaluate(ctx));

            // Past all four.
            tree.Tier1.balances["fans"] = 60;
            ctx.Deposit("cash", 200);
            tree.Tier1.generatorCounts["practice_amp"] = 4;
            tree.Tier1.barProgress[tree.Cover2.Id] = 300;
            AssertAgrees(balance, ctx, "CurrencyAtLeast met");
            AssertAgrees(earned, ctx, "EarnedTotalAtLeast met");
            AssertAgrees(owned, ctx, "OwnedCountAtLeast met");
            AssertAgrees(bars, ctx, "BarsCompleted met");
            Assert.IsTrue(balance.Evaluate(ctx));
            Assert.IsTrue(earned.Evaluate(ctx));
            Assert.IsTrue(owned.Evaluate(ctx));
            Assert.IsTrue(bars.Evaluate(ctx));
        }

        [Test]
        public void A_leafs_text_is_its_uiText_raw()
        {
            var tree = new TestTree();
            var named = new CurrencyAtLeast { currency = tree.Fans, threshold = 50, uiText = "50 fans" };
            var unnamed = new BarsCompleted { group = tree.LearnCovers, count = 1 };

            Assert.AreEqual("50 fans", named.Text);
            // Nothing composes at a leaf, so an unauthored one renders as
            // nothing at all - the capstone's threshold leg reads as its
            // progress alone.
            Assert.IsTrue(string.IsNullOrEmpty(unnamed.Text), "an unauthored leaf renders no text");
        }

        [Test]
        public void An_All_with_no_pattern_joins_its_parts_with_and()
        {
            var tree = new TestTree();

            Assert.AreEqual("50 fans", new All { conditions = { Fans(tree) } }.Text);
            Assert.AreEqual("50 fans and a cover", new All { conditions = { Fans(tree), Cover(tree) } }.Text);
            Assert.AreEqual("50 fans, a cover, and a demo",
                new All { conditions = { Fans(tree), Cover(tree), Demo() } }.Text);
        }

        [Test]
        public void An_Any_with_no_pattern_joins_its_parts_with_or()
        {
            var tree = new TestTree();

            Assert.AreEqual("50 fans or a cover", new Any { conditions = { Fans(tree), Cover(tree) } }.Text);
            Assert.AreEqual("50 fans, a cover, or a demo",
                new Any { conditions = { Fans(tree), Cover(tree), Demo() } }.Text);
        }

        [Test]
        public void A_pattern_formats_the_parts_by_index()
        {
            var tree = new TestTree();
            var sentence = new Any { uiText = "Reach {0} or learn {1}", conditions = { Fans(tree), Cover(tree) } };

            Assert.AreEqual("Reach 50 fans or learn a cover", sentence.Text);

            // The pattern is the whole line, so what it does with its parts -
            // reordering them, dropping one - is between it and string.Format.
            var reordered = new Any { uiText = "{1} first", conditions = { Fans(tree), Cover(tree) } };
            Assert.AreEqual("a cover first", reordered.Text);
        }

        [Test]
        public void A_pattern_with_no_placeholders_is_a_whole_override()
        {
            var tree = new TestTree();
            var whole = new All { uiText = "Do the thing", conditions = { Fans(tree), Cover(tree) } };

            Assert.AreEqual("Do the thing", whole.Text);

            // An override escapes a literal brace the way string.Format spells
            // it, doubled - one rule, not a second escaping convention.
            var braced = new All { uiText = "{{literal}}", conditions = { Fans(tree) } };
            Assert.AreEqual("{literal}", braced.Text);
        }

        [Test]
        public void Not_has_no_default_and_renders_the_line_its_author_wrote()
        {
            var tree = new TestTree();
            var pending = new EventRewardPending { host = tree.Tier1Def, uiText = "a reward is waiting" };

            // Prose does not negate mechanically (12.4), so an unauthored Not
            // renders empty and the author writes the line instead.
            Assert.IsTrue(string.IsNullOrEmpty(new Not { condition = pending }.Text),
                "an unauthored Not renders no text");
            Assert.AreEqual("Not while a reward is waiting",
                new Not { condition = pending, uiText = "Not while {0}" }.Text);
            Assert.AreEqual("Claim your reward first",
                new Not { condition = pending, uiText = "Claim your reward first" }.Text);
        }

        [Test]
        public void Composition_recurses_through_nested_compounds()
        {
            var tree = new TestTree();
            var joined = new All
            {
                conditions = { Fans(tree), new Any { conditions = { Cover(tree), Demo() } } }
            };

            Assert.AreEqual("50 fans and a cover or a demo", joined.Text);

            // The inner pattern is the inner line, and the outer default join
            // reads it as one part like any other.
            var patterned = new All
            {
                conditions =
                {
                    Fans(tree),
                    new Any { uiText = "either {0} or {1}", conditions = { Cover(tree), Demo() } }
                }
            };
            Assert.AreEqual("50 fans and either a cover or a demo", patterned.Text);
        }

        [Test]
        public void A_placeholder_past_the_parts_throws()
        {
            var tree = new TestTree();
            var gate = new Any { uiText = "{2}", conditions = { Fans(tree), Cover(tree) } };

            // Left to propagate on purpose: the load pass formats every rendered
            // gate once, and that is where the throw becomes a finding (12.12).
            Assert.Throws<System.FormatException>(() => { _ = gate.Text; });
        }

        [Test]
        public void The_text_reads_the_same_whether_the_legs_are_met_or_unmet()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 60;
            tree.Tier1.barProgress[tree.Cover1.Id] = 100;
            var ctx = tree.Ctx(tree.Tier1);
            var gate = new All { conditions = { Fans(tree), Cover(tree) } };

            Assert.IsEmpty(GateFeedback.UnmetLegs(gate, ctx));
            var whenMet = gate.Text;

            // The text describes the requirement and UnmetLegs judges it, which
            // is what lets the load pass compute every gate's text once (12.12).
            tree.Tier1.balances["fans"] = 37;
            Assert.AreEqual(1, GateFeedback.UnmetLegs(gate, ctx).Count);
            Assert.AreEqual(whenMet, gate.Text);
            Assert.AreEqual("50 fans and a cover", gate.Text);
        }

        // Progress and Evaluate read the same fields, so the readout can never
        // disagree with the answer the gate gave (12.11).
        private static void AssertAgrees(Condition leg, GameContext ctx, string what)
        {
            Assert.IsTrue(leg.Progress(ctx, out var current, out var target), what + " reports progress");
            Assert.AreEqual(current >= target, leg.Evaluate(ctx), what + " progress against Evaluate");
        }

        // A kind with no number keeps the default: no progress, and both outs
        // zero rather than a stale or invented figure.
        private static void AssertNoProgress(Condition leg, GameContext ctx, string what)
        {
            Assert.IsFalse(leg.Progress(ctx, out var current, out var target), what + " reports no progress");
            Assert.AreEqual(BigNumber.Zero, current, what + " current");
            Assert.AreEqual(BigNumber.Zero, target, what + " target");
        }

        // The three named legs the text rows compose over, so each row reads as
        // the line a player would see rather than as fixture wiring.
        private static Condition Fans(TestTree tree) =>
            new CurrencyAtLeast { currency = tree.Fans, threshold = 50, uiText = "50 fans" };

        private static Condition Cover(TestTree tree) =>
            new BarsCompleted { group = tree.LearnCovers, count = 1, uiText = "a cover" };

        private static Condition Demo() => new FlagSet { flagId = "album", uiText = "a demo" };
    }
}
