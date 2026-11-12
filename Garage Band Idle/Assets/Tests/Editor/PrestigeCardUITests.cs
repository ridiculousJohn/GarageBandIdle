using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Monetization;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The prestige card and its two windows (design doc 12.11), driven through
    // the shipping Screen.uxml and a real host over the test tree: one section
    // holding the tier's card, whose rung authors neither optional text, and
    // the chapter's card, whose rung authors both. Every assertion reads a
    // shown value, a class or an element's display - never a label's text.
    public class PrestigeCardUITests
    {
        private class Fixture
        {
            public readonly TestTree Tree = new();
            public readonly Rung Release;
            public readonly Rung Party;
            public readonly SectionDefinition Section;
            public readonly GameSession Session;
            public readonly ScreenHost Host;

            // The release pays the chapter counter from the fans the run
            // earned and resets the tier, guarded by the armed-reward leg; the
            // party pays a Roadie and resets the chapter.
            public Fixture()
            {
                // The chrome the host binds: Screen.uxml's pill names timer
                // 'encore_timer', and the host requires root to declare it.
                TestTree.DeclareEncore(Tree.RootDef);
                Release = new Rung
                {
                    label = "Cut a Demo",
                    offerCondition = new All
                    {
                        conditions =
                        {
                            new CurrencyAtLeast { currency = Tree.Fans, threshold = 50, uiText = "Requires 50 Fans" },
                            new Not { condition = new EventRewardPending(), uiText = "Claim your reward first" },
                        },
                    },
                    actions =
                    {
                        new AddCurrency
                        {
                            currencies = { Tree.Ch1Records },
                            formula = new RootCurveFormula { currency = Tree.Fans, divisor = 5, exponent = 0.5 },
                        },
                        new ResetScope { scope = Tree.Tier1Def },
                    },
                };
                Party = new Rung
                {
                    label = "Play the Party",
                    info = "The gig that ends the chapter.",
                    confirmText = "Everything in the garage goes.",
                    offerCondition = new CurrencyAtLeast
                    {
                        currency = Tree.Ch1Records, threshold = 30, uiText = "Requires 30 Records",
                    },
                    actions =
                    {
                        new AddCurrency { currencies = { Tree.Roadies }, amount = 1 },
                        new ResetScope { scope = Tree.Ch1Def },
                    },
                };
                Tree.Tier1Def.rung = Release;
                Tree.Ch1Def.rung = Party;
                Section = new SectionDefinition
                {
                    visibleWhen = new Always(),
                    scope = Tree.Ch1Def,
                    modules =
                    {
                        new ModuleDefinition { prefabId = "rung_button", scope = Tree.Tier1Def },
                        new ModuleDefinition { prefabId = "rung_button", scope = Tree.Ch1Def },
                    },
                };
                Tree.Ch1Def.sections.Add(Section);
                // The link pass runs in Build, so the rungs' resets and the
                // section are wired only once the tree is built again.
                Tree.Rebuild();

                var config = ScriptableObject.CreateInstance<GameConfig>();
                config.maxGameSpeed = 4;
                Session = new GameSession(Tree.Root, config);
                var clock = new GameClock(Tree.Now);
                var screen = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Screen.uxml");
                Assert.IsNotNull(screen, "Assets/UI/Screen.uxml is missing");
                var registry = AssetDatabase.LoadAssetAtPath<ModuleRegistry>("Assets/Settings/ModuleRegistry.asset");
                Assert.IsNotNull(registry, "Assets/Settings/ModuleRegistry.asset is missing.");
                Host = new ScreenHost(screen.Instantiate(), registry, Session, clock,
                    new AdManager(Session, new FakeAdService(), () => { }),
                    new IAPManager(Session, new FakeStoreService(), config, () => { }));

                // The stamp is now, so no idle window exists and the phase lands
                // Live; the switch's own refresh is the first render.
                Tree.Ch1.lastActiveUtc = Tree.Now;
                Session.SwitchChapter(Tree.Ch1, Tree.Now);
                Assert.AreEqual(SessionPhase.Live, Session.Phase);
                Session.Drain();
            }

            private ScreenHost.ModuleView Module(int index) =>
                Host.Sections.Single(view => view.Definition == Section).Modules[index];

            public RungButtonUI ReleaseCard => (RungButtonUI)Module(0).Widget;

            public RungButtonUI PartyCard => (RungButtonUI)Module(1).Widget;

            public GameContext TierCtx => Tree.Ctx(Tree.Tier1);

            public GameContext ChapterCtx => Tree.Ctx(Tree.Ch1);

            // A run standing at this many fans, earned and held: the gate reads
            // the balance and the payout what the round earned (12.5).
            public void Fans(double fans)
            {
                Tree.Tier1.balances[Tree.Fans.Id] = fans;
                Tree.Tier1.earnedTotals[Tree.Fans.Id] = fans;
                Session.Refresh();
            }

            public void Records(double records)
            {
                Tree.Ch1.balances[Tree.Ch1Records.Id] = records;
                Session.Refresh();
            }

            // An armed, unclaimed reward on the tier: the record the release's
            // own leg reads, and the one every reset over the tier refuses on.
            public void ArmReward()
            {
                EventSystem.Start(TierCtx, Tree.TimedGig);
                Tree.Tier1.activeEvent.goalReached = true;
                Session.Refresh();
            }

            public static bool Shown(VisualElement e) => e.style.display.value == DisplayStyle.Flex;

            public static int ShownLines(RungButtonUI card) =>
                card.Root.Q<VisualElement>("legs").Children().Count(Shown);
        }

        [Test]
        public void A_locked_card_shows_its_unmet_legs_and_the_refusal_and_disables_Confirm()
        {
            var fx = new Fixture();
            var card = fx.ReleaseCard;
            var legs = GateFeedback.Legs(fx.Release.offerCondition);

            Assert.IsFalse(card.ConfirmEnabled, "a fresh run has no fans");
            Assert.IsFalse(card.Root.Q<Button>("confirm").enabledSelf);
            Assert.IsTrue(card.Root.Q<Label>("title").ClassListContains("rung-title-locked"));
            CollectionAssert.AreEqual(new[] { legs[0] }, card.ShownUnmetLegs.ToArray(),
                "nothing is pending, so the reward leg holds and stays hidden");
            Assert.IsFalse(card.ShownRefused);
            Assert.AreEqual(1, Fixture.ShownLines(card));

            fx.ArmReward();

            CollectionAssert.AreEqual(new[] { legs[0], legs[1] }, card.ShownUnmetLegs.ToArray(),
                "the armed reward closes the guard leg too");
            Assert.IsTrue(card.ShownRefused, "and the reset over the tier refuses");
            Assert.AreEqual(3, Fixture.ShownLines(card), "two legs, then the refusal line");
            Assert.IsTrue(fx.PartyCard.ShownRefused, "the chapter's reset covers the tier, so it refuses as well");
        }

        [Test]
        public void A_met_card_shows_no_requirement_lines_and_enables_Confirm()
        {
            var fx = new Fixture();
            var card = fx.ReleaseCard;

            fx.Fans(60);

            Assert.IsTrue(card.ConfirmEnabled);
            Assert.IsTrue(card.Root.Q<Button>("confirm").enabledSelf);
            Assert.IsFalse(card.Root.Q<Label>("title").ClassListContains("rung-title-locked"));
            Assert.IsEmpty(card.ShownUnmetLegs);
            Assert.IsFalse(card.ShownRefused);
            Assert.AreEqual(0, Fixture.ShownLines(card), "every requirement line is gone");
        }

        [Test]
        public void The_title_shows_the_gain_while_locked_and_the_balance_line_reads_where_it_pays()
        {
            var fx = new Fixture();
            var release = (AddCurrency)fx.Release.actions[0];
            var party = (AddCurrency)fx.Party.actions[0];
            fx.Tree.Root.balances[fx.Tree.Roadies.Id] = 2;
            fx.Records(7);
            fx.Fans(20);

            var card = fx.ReleaseCard;
            Assert.IsFalse(card.ConfirmEnabled, "20 fans is short of the gate");
            Assert.IsTrue(card.PreviewShown, "the gain shows while locked");
            Assert.IsTrue(Fixture.Shown(card.Root.Q<Label>("balance")));
            CollectionAssert.AreEqual(new[] { fx.Tree.Ch1Records }, card.ShownCurrencies.ToArray());
            CollectionAssert.AreEqual(new[] { release.AmountFor(fx.TierCtx, fx.Tree.Ch1Records) },
                card.ShownAmounts.ToArray(), "the payout's own amount at 20 fans earned");
            CollectionAssert.AreEqual(new[] { (BigNumber)7 }, card.ShownBalances.ToArray(),
                "the chapter counter's balance at its home");

            var capstone = fx.PartyCard;
            CollectionAssert.AreEqual(new[] { fx.Tree.Roadies }, capstone.ShownCurrencies.ToArray());
            CollectionAssert.AreEqual(new[] { party.AmountFor(fx.ChapterCtx, fx.Tree.Roadies) },
                capstone.ShownAmounts.ToArray());
            CollectionAssert.AreEqual(new[] { (BigNumber)2 }, capstone.ShownBalances.ToArray(),
                "the Roadies root holds");
        }

        [Test]
        public void The_info_button_shows_only_with_info_text_and_opens_the_info_window()
        {
            var fx = new Fixture();
            var window = fx.Host.RungInfo;

            Assert.IsFalse(fx.ReleaseCard.InfoShown);
            Assert.IsFalse(Fixture.Shown(fx.ReleaseCard.Root.Q<Button>("info")));
            fx.ReleaseCard.OpenInfo();
            Assert.IsFalse(window.IsOpen, "a rung with no info text has nothing to open");

            Assert.IsTrue(fx.PartyCard.InfoShown);
            Assert.IsTrue(Fixture.Shown(fx.PartyCard.Root.Q<Button>("info")));
            fx.PartyCard.OpenInfo();

            Assert.IsTrue(window.IsOpen);
            Assert.AreSame(fx.Party, window.ShownRung);

            window.Close();

            Assert.IsFalse(window.IsOpen);
            Assert.IsNull(window.ShownRung);
            fx.Session.Refresh();
            Assert.IsFalse(window.IsOpen, "a refresh does not bring a closed window back");
        }

        [Test]
        public void Confirm_with_confirm_text_opens_the_window_and_resets_only_on_its_Confirm()
        {
            var fx = new Fixture();
            fx.Fans(60);
            fx.Records(30);
            var window = fx.Host.RungConfirm;
            var roadies = fx.ChapterCtx.GetBalance(fx.Tree.Roadies.Id);
            Assert.IsTrue(fx.PartyCard.ConfirmEnabled);

            fx.PartyCard.Press();

            Assert.IsTrue(window.IsOpen);
            Assert.AreSame(fx.Party, window.ShownRung);
            Assert.AreSame(fx.Tree.Ch1, window.HeldScope, "the card's own scope");
            Assert.AreEqual((BigNumber)30, fx.ChapterCtx.GetBalance(fx.Tree.Ch1Records.Id), "nothing ran yet");
            Assert.AreEqual(roadies, fx.ChapterCtx.GetBalance(fx.Tree.Roadies.Id));
            fx.Session.Refresh();
            Assert.IsTrue(window.IsOpen, "the window stands across a refresh");

            window.Confirm();

            Assert.IsFalse(window.IsOpen);
            Assert.IsNull(window.ShownRung);
            Assert.IsTrue(fx.ChapterCtx.GetBalance(fx.Tree.Roadies.Id) > roadies, "the Roadie was paid");
            Assert.AreEqual(BigNumber.Zero, fx.ChapterCtx.GetBalance(fx.Tree.Ch1Records.Id), "the chapter reset");
            Assert.AreEqual(BigNumber.Zero, fx.TierCtx.GetBalance(fx.Tree.Fans.Id), "and the tier inside it");
        }

        [Test]
        public void Cancel_closes_the_confirmation_window_without_resetting()
        {
            var fx = new Fixture();
            fx.Fans(60);
            fx.Records(30);
            var window = fx.Host.RungConfirm;
            var roadies = fx.ChapterCtx.GetBalance(fx.Tree.Roadies.Id);

            fx.PartyCard.Press();
            Assert.IsTrue(window.IsOpen);

            window.Cancel();

            Assert.IsFalse(window.IsOpen);
            Assert.IsNull(window.ShownRung);
            Assert.AreEqual(roadies, fx.ChapterCtx.GetBalance(fx.Tree.Roadies.Id));
            Assert.AreEqual((BigNumber)30, fx.ChapterCtx.GetBalance(fx.Tree.Ch1Records.Id));
            Assert.AreEqual((BigNumber)60, fx.TierCtx.GetBalance(fx.Tree.Fans.Id));
        }

        [Test]
        public void Confirm_without_confirm_text_runs_the_rung_at_once()
        {
            var fx = new Fixture();
            fx.Fans(60);
            var paid = ((AddCurrency)fx.Release.actions[0]).AmountFor(fx.TierCtx, fx.Tree.Ch1Records);

            fx.ReleaseCard.Press();

            Assert.IsFalse(fx.Host.RungConfirm.IsOpen, "nothing to ask");
            Assert.AreEqual(paid, fx.ChapterCtx.GetBalance(fx.Tree.Ch1Records.Id), "the release banked");
            Assert.AreEqual(BigNumber.Zero, fx.TierCtx.GetBalance(fx.Tree.Fans.Id), "and reset the tier");
        }

        [Test]
        public void Confirm_on_a_locked_card_does_nothing()
        {
            var fx = new Fixture();
            Assert.IsFalse(fx.PartyCard.ConfirmEnabled);

            fx.PartyCard.Press();
            fx.ReleaseCard.Press();

            Assert.IsFalse(fx.Host.RungConfirm.IsOpen, "the window never asks about a press that would refuse");
            Assert.AreEqual(BigNumber.Zero, fx.ChapterCtx.GetBalance(fx.Tree.Ch1Records.Id));
        }
    }
}
