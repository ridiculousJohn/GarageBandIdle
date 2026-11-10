using System;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // A chapter's income lift and its capstone gate are two authored
    // references, each resolved outward from where it is read, so they may name
    // one chapter currency (as ch1 and ch2 do) or two. This is the two-currency
    // shape at chapter level: ch1 declares tapes and tours, the release pays
    // each its own constant, a permanent ch1 modifier lifts income on tapes, and
    // the capstone gates on tours.
    public class SplitPrestigeTests
    {
        private static void AssertClose(double expected, BigNumber actual, string what = null) =>
            Assert.AreEqual(expected, actual.ToDouble(), 1e-9, what ?? string.Empty);

        private class Fixture
        {
            public readonly DateTime Now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

            public readonly RootDefinition RootDef;
            public readonly ChapterDefinition Ch1Def;
            public readonly TierDefinition Tier1Def;
            public readonly CurrencyDefinition Roadies;
            public readonly CurrencyDefinition Cash;
            public readonly CurrencyDefinition Tapes;
            public readonly CurrencyDefinition Tours;
            public readonly GeneratorDefinition Amp;
            public readonly ComposedContent Content;

            public readonly RootScopeState Root;
            public readonly ChapterScopeState Ch1;
            public readonly TierScopeState Tier1;
            public readonly GameSession Session;

            public Fixture()
            {
                // What the code asks of root (12.12), with the roadies asset
                // kept so the capstone pays the instance root declares.
                RootDef = TestTree.MakeRoot("root");
                Roadies = TestTree.DeclareCurrency(RootDef, "roadies");
                RootDef.entitlements.Add("backstage_pass");
                RootDef.declaredTimers.Add("encore_timer");
                RootDef.encoreAdReward.Add(new ExtendTimer { timer = "encore_timer", seconds = 14400, capSeconds = 86400 });
                RootDef.declaredFlags.Add("ch1_complete");
                RootDef.declaredTags.Add("income");

                Ch1Def = TestTree.MakeChapter("ch1");
                Tier1Def = TestTree.MakeTier("tier1");
                Ch1Def.children.Add(Tier1Def);

                // The two chapter currencies: tapes is what income reads, tours
                // is what the capstone reads.
                Tapes = TestTree.DeclareCurrency(Ch1Def, "tapes");
                Tours = TestTree.DeclareCurrency(Ch1Def, "tours");
                Cash = TestTree.DeclareCurrency(Tier1Def, "cash", "income");

                Amp = TestTree.MakeDefinition<GeneratorDefinition>("amp");
                Amp.availableWhen = new Always();
                Amp.costCurrency = Cash;
                Amp.baseCost = 60;
                Amp.growth = 1.15;
                Amp.produces.Add(TestTree.Entry(Cash, Stat.Rate, 0.5));
                Tier1Def.generators.Add(Amp);

                // The release pays each chapter currency its own constant.
                Tier1Def.rung = new Rung
                {
                    label = "Cut a Demo",
                    offerCondition = new CurrencyAtLeast { currency = Cash, threshold = 10 },
                    actions =
                    {
                        new AddCurrency { currencies = { Tapes }, amount = 3 },
                        new AddCurrency { currencies = { Tours }, amount = 7 },
                        new ResetScope { scope = Tier1Def },
                    }
                };

                // 1 + 0.1 * tapes on the income tag, applied at the chapter.
                var tapesIncome = TestTree.MakeDefinition<ModifierDefinition>("tapes_income");
                tapesIncome.effects.Add(new Effect { target = "income", stat = Stat.Rate,
                    formula = new LinearOnBalance { currency = Tapes, coefficient = 0.1 } });
                Ch1Def.modifiers.Add(tapesIncome);
                Ch1Def.permanentModifiers.Add(tapesIncome);

                // The capstone gates on tours alone.
                Ch1Def.rung = new Rung
                {
                    label = "Play the Show",
                    offerCondition = new CurrencyAtLeast { currency = Tours, threshold = 20 },
                    actions =
                    {
                        new AddCurrency { currencies = { Roadies }, amount = 1 },
                        new SetFlag { flagId = "ch1_complete" },
                        new ResetScope { scope = Ch1Def },
                    }
                };

                Content = ComposedContent.Compose(RootDef, new[] { Ch1Def });
                Root = ScopeState.Build(Content);
                Ch1 = (ChapterScopeState)TestNavigation.Node(Root, Ch1Def);
                Tier1 = (TierScopeState)TestNavigation.Node(Root, Tier1Def);

                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                Session = new GameSession(Root, config);
                Session.SwitchChapter(Ch1, Now);
            }

            public GameContext Ctx(ScopeState scope) => new GameContext(scope, Now);

            // One release through the session, from a run that meets its gate.
            public void Release()
            {
                Ctx(Tier1).Deposit(Cash.Id, 10);
                bool? ran = null;
                Session.TryRung(Ctx(Tier1), r => ran = r);
                Assert.AreEqual(true, ran, "the release ran");
            }

            public BigNumber CashRate()
            {
                Tier1.generatorCounts[Amp.Id] = 1;
                return Producer.GetRate(Ctx(Tier1), Cash);
            }
        }

        [Test]
        public void Two_chapter_currencies_split_between_income_and_the_capstone_validate_clean()
        {
            var f = new Fixture();

            var report = ContentValidator.Validate(f.Content);

            Assert.AreEqual(0, report.Findings.Count, string.Join("\n", report.Findings));
            Assert.AreEqual(BigNumber.Zero, f.Ch1.balances[f.Tapes.Id], "the built chapter holds tapes");
            Assert.AreEqual(BigNumber.Zero, f.Ch1.balances[f.Tours.Id], "and tours beside it");
        }

        [Test]
        public void The_release_pays_each_currency_and_income_reads_only_its_own()
        {
            var f = new Fixture();
            AssertClose(0.5, f.CashRate(), "no tapes, no lift");

            f.Release();

            AssertClose(3, f.Ch1.balances[f.Tapes.Id], "tapes");
            AssertClose(7, f.Ch1.balances[f.Tours.Id], "tours");
            AssertClose(0.5 * 1.3, f.CashRate(), "1 + 0.1 x 3 tapes");

            f.Ch1.balances[f.Tours.Id] = 1000;
            AssertClose(0.5 * 1.3, f.CashRate(), "tours never reach income");

            f.Ch1.balances[f.Tapes.Id] = 10;
            AssertClose(0.5 * 2, f.CashRate(), "1 + 0.1 x 10 tapes");
        }

        [Test]
        public void The_capstone_opens_on_its_own_currency_and_the_chapter_reset_zeroes_both()
        {
            var f = new Fixture();
            var atCh1 = f.Ctx(f.Ch1);

            f.Release();
            Assert.IsFalse(f.Ch1Def.rung.IsOffered(atCh1), "7 tours is short of 20");

            f.Ch1.balances[f.Tapes.Id] = 1000;
            Assert.IsFalse(f.Ch1Def.rung.IsOffered(atCh1), "tapes never open the capstone");

            f.Ch1.balances[f.Tapes.Id] = 0;
            f.Ch1.balances[f.Tours.Id] = 20;
            Assert.IsTrue(f.Ch1Def.rung.IsOffered(atCh1), "20 tours opens it with no tapes at all");

            f.Ch1.balances[f.Tapes.Id] = 3;
            bool? ran = null;
            f.Session.TryRung(atCh1, r => ran = r);

            Assert.AreEqual(true, ran, "the capstone ran");
            AssertClose(1, f.Root.balances["roadies"], "the roadie");
            Assert.IsTrue(f.Root.flags.Contains("ch1_complete"));
            AssertClose(0, f.Ch1.balances[f.Tapes.Id], "the income currency zeroes");
            AssertClose(0, f.Ch1.balances[f.Tours.Id], "the gate currency zeroes");
            AssertClose(0.5, f.CashRate(), "the replay's income starts unlifted");
        }
    }
}
