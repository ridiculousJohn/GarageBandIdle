using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // One balance against another, strictly (design doc 12.4). The kind exists
    // for the catch-up latch: a shared scope resets itself whenever a counter
    // some sibling's prestige raises has moved past the copy the shared scope
    // last recorded, then records the counter again - so every prestige anywhere
    // under the chapter wipes the shared scope exactly once.
    public class CurrencyExceedsTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        // ---- the comparison ----

        [Test]
        public void CurrencyExceeds_is_false_when_the_balances_are_equal()
        {
            var tree = new TestTree();
            var exceeds = new CurrencyExceeds { currency = tree.Cash, other = tree.Fans };
            var ctx = tree.Ctx(tree.Tier1);

            Assert.IsFalse(exceeds.Evaluate(ctx), "both zero");

            tree.Tier1.balances["cash"] = 5;
            tree.Tier1.balances["fans"] = 5;
            Assert.IsFalse(exceeds.Evaluate(ctx), "equal is not ahead");
        }

        [Test]
        public void CurrencyExceeds_is_true_only_while_the_currency_is_ahead()
        {
            var tree = new TestTree();
            var exceeds = new CurrencyExceeds { currency = tree.Cash, other = tree.Fans };
            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.balances["fans"] = 5;

            tree.Tier1.balances["cash"] = 6;
            Assert.IsTrue(exceeds.Evaluate(ctx), "ahead");

            tree.Tier1.balances["cash"] = 4;
            Assert.IsFalse(exceeds.Evaluate(ctx), "behind");
        }

        // Each side resolves outward from the acting scope on its own, so the
        // two may live at different depths of one chain.
        [Test]
        public void CurrencyExceeds_reads_each_side_at_its_own_home()
        {
            var tree = new TestTree();
            var exceeds = new CurrencyExceeds { currency = tree.Ch1Records, other = tree.Cash };
            var ctx = tree.Ctx(tree.Tier1);
            tree.Tier1.balances["cash"] = 2;

            tree.Ch1.balances["ch1_records"] = 2;
            Assert.IsFalse(exceeds.Evaluate(ctx));

            tree.Ch1.balances["ch1_records"] = 3;
            Assert.IsTrue(exceeds.Evaluate(ctx));
        }

        // No threshold the player approaches, so the progress readout is the
        // base default.
        [Test]
        public void CurrencyExceeds_reports_no_progress()
        {
            var tree = new TestTree();
            tree.Tier1.balances["fans"] = 5;
            var exceeds = new CurrencyExceeds { currency = tree.Cash, other = tree.Fans };

            Assert.IsFalse(exceeds.Progress(tree.Ctx(tree.Tier1), out _, out _));
        }

        // ---- the catch-up latch, end to end ----

        // A chapter declaring the counter, two scene tiers whose rungs each add
        // one to it and reset themselves, and a shared tier holding cash and its
        // copy of the counter. The shared trigger resets shared and then records
        // the counter: the reset clears the copy and re-arms the trigger, and the
        // deposit after it is what closes the condition for the new life.
        private class Fixture
        {
            public readonly DateTime Now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

            public readonly RootDefinition RootDef;
            public readonly ChapterDefinition Ch1Def;
            public readonly TierDefinition SceneADef;
            public readonly TierDefinition SceneBDef;
            public readonly TierDefinition SharedDef;
            public readonly CurrencyDefinition Counter;
            public readonly CurrencyDefinition CoinA;
            public readonly CurrencyDefinition CoinB;
            public readonly CurrencyDefinition Cash;
            public readonly CurrencyDefinition Seen;
            public readonly TriggerDefinition CatchUp;
            public readonly ComposedContent Content;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly ScopeState SceneA;
            public readonly ScopeState SceneB;
            public readonly ScopeState Shared;
            public readonly GameSession Session;

            public Fixture()
            {
                RootDef = TestTree.MakeRoot("root");
                TestTree.DeclareCodeReferences(RootDef);

                Ch1Def = TestTree.MakeChapter("ch1");
                Counter = TestTree.DeclareCurrency(Ch1Def, "counter");

                SceneADef = TestTree.MakeTier("scene_a");
                CoinA = TestTree.DeclareCurrency(SceneADef, "a_coin");
                SceneADef.rung = SceneRung("Wrap Scene A", CoinA, Counter, SceneADef);

                SceneBDef = TestTree.MakeTier("scene_b");
                CoinB = TestTree.DeclareCurrency(SceneBDef, "b_coin");
                SceneBDef.rung = SceneRung("Wrap Scene B", CoinB, Counter, SceneBDef);

                SharedDef = TestTree.MakeTier("shared");
                Cash = TestTree.DeclareCurrency(SharedDef, "cash");
                Seen = TestTree.DeclareCurrency(SharedDef, "seen");
                CatchUp = TestTree.MakeDefinition<TriggerDefinition>("catch_up");
                CatchUp.condition = new CurrencyExceeds { currency = Counter, other = Seen };
                CatchUp.actions.Add(new ResetScope { scope = SharedDef });
                CatchUp.actions.Add(new AddCurrency
                {
                    currencies = { Seen },
                    formula = new RootCurveFormula { currency = Counter, reads = PayoutTotal.Balance, divisor = 1, exponent = 1 },
                });
                SharedDef.triggers.Add(CatchUp);

                Ch1Def.children.Add(SceneADef);
                Ch1Def.children.Add(SceneBDef);
                Ch1Def.children.Add(SharedDef);

                Content = ComposedContent.Compose(RootDef, new[] { Ch1Def });
                Root = ScopeState.Build(Content);
                Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Ch1Def);
                SceneA = TestNavigation.Node(Root, SceneADef);
                SceneB = TestNavigation.Node(Root, SceneBDef);
                Shared = TestNavigation.Node(Root, SharedDef);

                Session = new GameSession(Root, ScriptableObject.CreateInstance<GameConfig>());
                Session.SwitchChapter(Ch1, Now);
            }

            // A scene's prestige: one onto the chapter's counter, then the
            // scene clears itself.
            private static Rung SceneRung(string label, CurrencyDefinition coin, CurrencyDefinition counter,
                                          TierDefinition self) => new Rung
            {
                label = label,
                offerCondition = new CurrencyAtLeast { currency = coin, threshold = 1 },
                actions =
                {
                    new AddCurrency { currencies = { counter }, amount = 1 },
                    new ResetScope { scope = self },
                }
            };

            public GameContext Ctx(ScopeState scope, double seconds) => new GameContext(scope, Now.AddSeconds(seconds));

            public BigNumber Balance(ScopeState scope, CurrencyDefinition currency) =>
                new GameContext(scope, Now).GetBalance(currency.Id);

            // One press of a scene's rung through the session, which closes the
            // transaction with the sweep exactly as the button's press does.
            public void Press(ScopeState scene, CurrencyDefinition coin, double seconds)
            {
                Ctx(scene, seconds).Deposit(coin.Id, 1);
                bool? ran = null;
                Session.TryRung(Ctx(scene, seconds), r => ran = r);
                Assert.AreEqual(true, ran, $"the rung at '{scene.ScopeId}' ran");
            }
        }

        [Test]
        public void The_catch_up_content_validates_clean()
        {
            var f = new Fixture();

            var report = ContentValidator.Validate(f.Content);

            Assert.AreEqual(0, report.Findings.Count, string.Join("\n", report.Findings));
        }

        [Test]
        public void Before_any_prestige_the_catch_up_trigger_does_not_fire()
        {
            var f = new Fixture();
            Assert.AreEqual(SessionPhase.Live, f.Session.Phase, "the sweep runs only in a live transaction");
            f.Ctx(f.Shared, 0).Deposit(f.Cash.Id, 50);

            f.Session.Tick(1, f.Now.AddSeconds(1));

            AssertClose(0, f.Balance(f.Ch1, f.Counter), "counter");
            AssertClose(0, f.Balance(f.Shared, f.Seen), "seen");
            AssertClose(50, f.Balance(f.Shared, f.Cash), "0 is not ahead of 0, so shared keeps its cash");
            Assert.IsFalse(f.Shared.firedTriggers.Contains(f.CatchUp.Id), "never latched");
        }

        [Test]
        public void A_scene_prestige_wipes_the_shared_scope_once_and_records_the_counter()
        {
            var f = new Fixture();
            f.Ctx(f.Shared, 0).Deposit(f.Cash.Id, 50);
            f.Ctx(f.SceneB, 0).Deposit(f.CoinB.Id, 7);
            f.Ctx(f.SceneA, 0).Deposit(f.CoinA.Id, 4);

            f.Press(f.SceneA, f.CoinA, 1);

            AssertClose(1, f.Balance(f.Ch1, f.Counter), "scene a's rung paid the counter");
            AssertClose(0, f.Balance(f.Shared, f.Cash), "the shared scope was reset");
            AssertClose(1, f.Balance(f.Shared, f.Seen), "seen records the counter after the reset");
            AssertClose(7, f.Balance(f.SceneB, f.CoinB), "the other scene is untouched");
            AssertClose(0, f.Balance(f.SceneA, f.CoinA), "scene a reset itself");
        }

        [Test]
        public void A_sweep_after_the_catch_up_leaves_the_shared_scope_alone()
        {
            var f = new Fixture();
            f.Press(f.SceneA, f.CoinA, 1);
            f.Ctx(f.Shared, 2).Deposit(f.Cash.Id, 30);

            f.Session.Tick(1, f.Now.AddSeconds(3));
            Sweep.Run(f.Root, f.Ch1, f.Now.AddSeconds(4));

            AssertClose(30, f.Balance(f.Shared, f.Cash), "1 is not ahead of 1, so nothing resets shared again");
            AssertClose(1, f.Balance(f.Shared, f.Seen));
        }

        [Test]
        public void Each_scenes_prestige_wipes_the_shared_scope_again()
        {
            var f = new Fixture();
            f.Press(f.SceneA, f.CoinA, 1);
            f.Ctx(f.Shared, 2).Deposit(f.Cash.Id, 30);
            f.Ctx(f.SceneA, 2).Deposit(f.CoinA.Id, 3);

            f.Press(f.SceneB, f.CoinB, 3);

            AssertClose(2, f.Balance(f.Ch1, f.Counter), "both scenes paid the counter");
            AssertClose(0, f.Balance(f.Shared, f.Cash), "scene b's prestige wiped shared too");
            AssertClose(2, f.Balance(f.Shared, f.Seen), "seen caught up to two");
            AssertClose(3, f.Balance(f.SceneA, f.CoinA), "scene a is untouched by scene b's reset");
        }
    }
}
