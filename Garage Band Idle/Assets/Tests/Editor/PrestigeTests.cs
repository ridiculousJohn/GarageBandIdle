using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The prestige press (design doc rule 14): rungs as DATA, and one
    // parameterized operation over them - the generalization the album release
    // and the capstone completion were the two hardcoded cases of. What is
    // pinned here is the operation's contract, in the order the press applies
    // it:
    //
    // - the refusals, in strictness order: silent for the states a double-tap
    //   or a stale row reaches, loud for content that cannot execute - and
    //   every one of them BEFORE anything irreversible
    // - the execution order: deepest scope first, same depth in the tree's
    //   authored order, the initiating rung last, each rung exactly once
    // - who participates: every LATCHLESS rung of a selected scope, whatever
    //   its offer says, and no latch-bearing one
    // - what is cleared: the SELECTED set only, downward-closed, in place
    // - and what comes back: onComplete from the latch at projection, never
    //   from the press
    //
    // Every fixture is a definition-built tree hanging under no outer pool, so
    // the payout has to land somewhere a clear does not take: these currency
    // groups are a 'run' group a press clears and a 'bank' group it never does,
    // which is the Global permanent pool's role played by a roster entry.
    public class PrestigeTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private const string RecordsId = GameManager.RecordsCurrencyId;
        private const string RoadiesId = GameManager.RoadiesCurrencyId;

        private static CurrencyGroupDefinition[] Groups()
            => new[] { TestContent.MakeGroup("run", true), TestContent.MakeGroup("bank", false) };

        // four run currencies, because the geometry fixtures need one balance
        // per scope to tell "cleared" from "untouched", and two bank currencies
        // for the payouts to survive in
        private static CurrencyDefinition[] Currencies()
            => new[]
            {
                TestContent.MakeCurrency("cash", "run"),
                TestContent.MakeCurrency("fans", "run"),
                TestContent.MakeCurrency("rehearsal", "run"),
                TestContent.MakeCurrency("merch", "run"),
                TestContent.MakeCurrency(RecordsId, "bank"),
                TestContent.MakeCurrency(RoadiesId, "bank"),
            };

        private static ContentDatabase Database(params ScopeDefinition[] scopes)
            => TestContent.MakeDatabase(currencies: Currencies(), currencyGroups: Groups(), scopes: scopes);

        // The capstone shape over a ladder: a parent filing the rung that is
        // pressed, and one tier scope holding the run's fans. The parent owns
        // the bank currencies, so a payout it or its tier banks outlives the
        // clear - which is what makes "nothing moved" and "this much landed"
        // both assertable on the same fixture.
        private static Scope BuildCapstoneOverATier(PrestigeTierDefinition capstone,
            PrestigeTierDefinition tierRung = null,
            List<FlagDeclaration> parentFlags = null, List<FlagDeclaration> tierFlags = null)
        {
            var tier = TestContent.MakeScope("tier_1",
                currencyIds: new List<string> { "fans" },
                flags: tierFlags,
                rung: tierRung);
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash", RecordsId, RoadiesId },
                childScopeIds: new List<string> { "tier_1" },
                flags: parentFlags ?? new List<FlagDeclaration> { new("chapter_2") },
                rung: capstone);

            return ScopeFactory.Build(garage, "frontier", Database(garage, tier));
        }

        // the capstone rung the fixtures above press: latch-bearing, awarding a
        // flat two Records, clearing the tier it names and nothing else
        private static PrestigeTierDefinition MakeCapstoneRung(List<GameAction> actions = null,
            Condition operationGate = null, GameEffect onComplete = null,
            string latchFlagId = "chapter_2")
            => TestContent.MakeRung("backyard_party",
                new NamedScopesSelector(new List<string> { "tier_1" }),
                actions ?? new List<GameAction> { new GrantCurrencyAction(RecordsId, 2) },
                latchFlagId: latchFlagId, operationGate: operationGate, onComplete: onComplete);

        // the album rung: latchless, so it is repeatable and rides along on any
        // press that selects its scope, paying the chapter's authored curve -
        // floor((fans / 5) ^ 0.5)
        private static PrestigeTierDefinition MakeAlbumRung(ResetTargetSelector resetTargets = null,
            Condition offer = null)
            => TestContent.MakeRung("cut_demo", resetTargets,
                new List<GameAction>
                {
                    new GrantComputedCurrencyAction(RecordsId, new RootOfBalanceFormula("fans", 5)),
                },
                offer: offer);

        // ---- the refusals ----------------------------------------------------

        // A finished rung does not complete twice, and the refusal is SILENT:
        // the UI calls this on a button press, and a double-tap on a row that
        // has not redrawn yet is not an error. What makes it a real claim is
        // the state, not the return - a second press that ran the actions again
        // would pay the award twice and clear a run the player has started over.
        [Test]
        public void ASecondPressOfALatchedRung_IsRefusedSilently_AndMovesNothing()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung());
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            Assert.IsTrue(scope.CompleteRung("backyard_party"), "the first press is the real one");
            Assert.AreEqual(2.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9, "the award paid once");

            // a fresh run under the finished rung, so a second press would have
            // something visible to take
            tier.Currencies.Add("fans", 45);

            Assert.IsFalse(scope.CompleteRung("backyard_party"),
                "the latch is set, so the press refuses - and says nothing, because a double-tap is not an error");

            Assert.AreEqual(2.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "the award did not pay twice");
            Assert.AreEqual(45.0, tier.Currencies.Get("fans").ToDouble(), 1e-9,
                "and the refused press cleared nothing");
            Assert.IsTrue(scope.Flags.IsSet("chapter_2"), "the latch stands, once");
        }

        // The operation gate is fail-closed and asked by the OPERATION, not
        // only by the button that offered it: a press that latches a permanent
        // flag must not be reachable through a row the player is merely still
        // looking at. Silent for the same reason a TryBuy under an unmet gate
        // is. The second half is the control: with the gate met and nothing
        // else changed, the same press goes through, so the refusal above is
        // the gate's doing and not the fixture's.
        [Test]
        public void AnUnmetOperationGate_RefusesThePressSilently_BeforeAnythingMoves()
        {
            var scope = BuildCapstoneOverATier(
                MakeCapstoneRung(operationGate: new FlagSetCondition("gig_booked")),
                parentFlags: new List<FlagDeclaration> { new("chapter_2"), new("gig_booked") });
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            Assert.IsFalse(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(0.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "a refused press awards nothing");
            Assert.AreEqual(45.0, tier.Currencies.Get("fans").ToDouble(), 1e-9,
                "and clears nothing");
            Assert.IsFalse(scope.Flags.IsSet("chapter_2"), "and latches nothing");

            scope.Flags.Set("gig_booked");

            Assert.IsTrue(scope.CompleteRung("backyard_party"),
                "with the gate met the same press runs: the refusal above was the gate, not the fixture");
        }

        // The preflight is the whole press's, not each rung's: one action that
        // cannot execute refuses everything, LOUDLY, before any award lands or
        // any run is cleared. The good participant is authored FIRST on purpose
        // - a preflight that checked and executed rung by rung would already
        // have paid its five Roadies by the time it reached the broken one, and
        // those Roadies are the assertion below.
        [Test]
        public void OneUnexecutableParticipantAction_RefusesTheWholePressLoudly_BeforeAnyPayout()
        {
            var good = TestContent.MakeScope("a_good",
                currencyIds: new List<string> { "fans" },
                rung: TestContent.MakeRung("good_rung",
                    actions: new List<GameAction> { new GrantCurrencyAction(RoadiesId, 5) }));
            // 'unheld' resolves to no pool anywhere in the tree, which is what
            // an award naming a currency its scope cannot reach looks like
            var bad = TestContent.MakeScope("b_bad",
                currencyIds: new List<string> { "rehearsal" },
                rung: TestContent.MakeRung("bad_rung",
                    actions: new List<GameAction> { new GrantCurrencyAction("unheld", 1) }));
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash", RecordsId, RoadiesId },
                childScopeIds: new List<string> { "a_good", "b_bad" },
                flags: new List<FlagDeclaration> { new("chapter_2") },
                rung: TestContent.MakeRung("backyard_party", new SelfAndContainedSelector(),
                    new List<GameAction> { new GrantCurrencyAction(RecordsId, 2) },
                    latchFlagId: "chapter_2"));

            var scope = ScopeFactory.Build(garage, "frontier", Database(garage, good, bad));
            scope.Children[0].Currencies.Add("fans", 45);

            LogAssert.Expect(LogType.Error, new Regex(
                "rung 'bad_rung' on instance 'frontier/b_bad' has an action that cannot execute"));

            Assert.IsFalse(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(0.0, scope.Currencies.Get(RoadiesId).ToDouble(), 1e-9,
                "the good participant never ran: one unexecutable action refuses the WHOLE press");
            Assert.AreEqual(0.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "the initiating rung's own award did not land either");
            Assert.IsFalse(scope.Flags.IsSet("chapter_2"), "and nothing latched");
            Assert.AreEqual(45.0, scope.Children[0].Currencies.Get("fans").ToDouble(), 1e-9,
                "the run was not cleared for nothing");
        }

        // The latch is in the preflight for exactly this reason: it sits outside
        // Actions and runs LAST, so a latch that cannot execute would fail after
        // every payout had landed and the run had been cleared - the stranding
        // the whole refusal order exists to prevent. Here the parent declares no
        // flags at all, so its own latch has nowhere to write.
        [Test]
        public void AnInitiatorLatchThatCannotExecute_RefusesTheWholePressLoudly_BeforeAnyPayout()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung(),
                tierRung: MakeAlbumRung(),
                parentFlags: new List<FlagDeclaration>());
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            LogAssert.Expect(LogType.Error, new Regex(
                "rung 'backyard_party' on instance 'frontier' has an action that cannot execute"));

            Assert.IsFalse(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(0.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "the participating album payout never ran: the initiator's latch failed the preflight first");
            Assert.AreEqual(45.0, tier.Currencies.Get("fans").ToDouble(), 1e-9,
                "and the run stands");
            Assert.IsFalse(scope.Flags.IsSet("chapter_2"));
        }

        // ---- the execution order ---------------------------------------------

        // Reads go outward, so an outer rung running first would write state an
        // inner rung's formula then measures. The deep rung banks 45 fans into
        // the currency the ROOT owns, and the rung one level up computes over
        // that same balance: with the order reversed its formula would read zero
        // and the press would bank nothing.
        [Test]
        public void ActionsRunDeepestScopeFirst_SoAShallowerFormulaMeasuresWhatADeeperRungBanked()
        {
            // a participant's own selector is never consulted - only the
            // initiating rung's - so these two file none
            var deep = TestContent.MakeScope("tier_2",
                rung: TestContent.MakeRung("deep_rung",
                    actions: new List<GameAction> { new GrantCurrencyAction("fans", 45) }));
            var shallow = TestContent.MakeScope("tier_1",
                childScopeIds: new List<string> { "tier_2" },
                rung: TestContent.MakeRung("shallow_rung",
                    actions: new List<GameAction>
                    {
                        new GrantComputedCurrencyAction(RecordsId, new RootOfBalanceFormula("fans", 5)),
                    }));
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash", "fans", RecordsId },
                childScopeIds: new List<string> { "tier_1" },
                rung: TestContent.MakeRung("press", new SelfAndContainedSelector()));

            var scope = ScopeFactory.Build(garage, "frontier", Database(garage, shallow, deep));

            Assert.IsTrue(scope.CompleteRung("press"));

            Assert.AreEqual(3.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "floor((45 / 5) ^ 0.5): the shallower formula measured the deeper rung's grant");
            Assert.AreEqual(0.0, scope.Currencies.Get("fans").ToDouble(), 1e-9,
                "and the run the press selected is cleared behind it");
        }

        // Depth is a partial order, so same-depth scopes need an ordering that
        // is neither a selector's list order nor an incidental enumeration's:
        // it is the tree's authored traversal. The ids are reverse alphabetical
        // on purpose - an incidental sort anywhere in the plan would run the
        // formula before the grant it measures, and bank nothing.
        [Test]
        public void SameDepthActionsRunInTheParentsAuthoredChildOrder()
        {
            var first = TestContent.MakeScope("b_first",
                rung: TestContent.MakeRung("granting_rung",
                    actions: new List<GameAction> { new GrantCurrencyAction("fans", 45) }));
            var second = TestContent.MakeScope("a_second",
                rung: TestContent.MakeRung("measuring_rung",
                    actions: new List<GameAction>
                    {
                        new GrantComputedCurrencyAction(RecordsId, new RootOfBalanceFormula("fans", 5)),
                    }));
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash", "fans", RecordsId },
                childScopeIds: new List<string> { "b_first", "a_second" },
                rung: TestContent.MakeRung("press", new SelfAndContainedSelector()));

            var scope = ScopeFactory.Build(garage, "frontier", Database(garage, first, second));

            Assert.IsTrue(scope.CompleteRung("press"));

            Assert.AreEqual(3.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "the first-authored sibling ran first: list order, not alphabetical order");
        }

        // The implicit cut (design doc sections 1-2): a capstone-shaped press
        // banks the run through the album rung filed below it, and only then
        // awards its own payout - so a formula reading cumulative Records
        // measures the demo the same press just cut. Running the initiator
        // first would award off a zero balance.
        [Test]
        public void TheInitiatingRungRunsLast_SoItsFormulaMeasuresWhatTheParticipantsBanked()
        {
            var scope = BuildCapstoneOverATier(
                MakeCapstoneRung(new List<GameAction>
                {
                    new GrantComputedCurrencyAction(RoadiesId, new RootOfBalanceFormula(RecordsId, 1)),
                }),
                tierRung: MakeAlbumRung());
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 500);

            Assert.IsTrue(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(10.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "floor((500 / 5) ^ 0.5): the tier's album banked first");
            Assert.AreEqual(3.0, scope.Currencies.Get(RoadiesId).ToDouble(), 1e-9,
                "floor(10 ^ 0.5): the initiating rung measured what the participant had already banked");
        }

        // The preview walks the press's plan WITH its state transitions: an
        // earlier planned grant shifts the balances a later formula reads,
        // through a read-only overlay - so a formula that measures what the
        // press will have banked previews the number the press will pay, not
        // the number the original balances imply (which here is zero).
        [Test]
        public void ThePreviewAppliesEarlierPlannedGrants_SoALaterFormulaPreviewsWhatItWillMeasure()
        {
            var scope = BuildCapstoneOverATier(
                MakeCapstoneRung(new List<GameAction>
                {
                    new GrantComputedCurrencyAction(RoadiesId, new RootOfBalanceFormula(RecordsId, 1)),
                }),
                tierRung: MakeAlbumRung());
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 500);

            Assert.AreEqual(3.0, scope.PendingRungGrant("backyard_party", RoadiesId).ToDouble(), 1e-9,
                "the preview saw the 10 records the participating album will have banked - not the standing zero");

            Assert.IsTrue(scope.CompleteRung("backyard_party"));
            Assert.AreEqual(3.0, scope.Currencies.Get(RoadiesId).ToDouble(), 1e-9,
                "and the press paid exactly what the preview promised");
        }

        // The whole plan's grants, per currency, in plan order - what a generic
        // button renders. Zeros are real entries: a rung at zero fans still
        // advertises "+0" of what it pays rather than advertising nothing.
        [Test]
        public void PendingRungGrants_ListEveryPlannedCurrency_WithPressMatchingTotals()
        {
            var scope = BuildCapstoneOverATier(
                MakeCapstoneRung(new List<GameAction>
                {
                    new GrantCurrencyAction(RoadiesId, 1),
                }),
                tierRung: MakeAlbumRung());
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            var grants = scope.PendingRungGrants("backyard_party");

            Assert.AreEqual(2, grants.Count, "two currencies in the plan: the album's records, the capstone's roadie");
            Assert.AreEqual(RecordsId, grants[0].CurrencyId, "plan order: the participating album's grant first");
            Assert.AreEqual(3.0, grants[0].Amount.ToDouble(), 1e-9, "floor((45 / 5) ^ 0.5)");
            Assert.AreEqual(RoadiesId, grants[1].CurrencyId);
            Assert.AreEqual(1.0, grants[1].Amount.ToDouble(), 1e-9);

            Assert.IsTrue(scope.CompleteRung("backyard_party"));
            Assert.AreEqual(3.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9, "the press paid the listed totals");
            Assert.AreEqual(1.0, scope.Currencies.Get(RoadiesId).ToDouble(), 1e-9);
        }

        // A self-and-contained selector selects the scope the rung is filed on,
        // so the initiating rung is a candidate participant on its own scope -
        // and is excluded, exactly once. A press that enumerated it twice would
        // pay the album curve twice off one run of fans. Latchless, so it stays
        // offerable forever: the second half is the same press again over a
        // re-earned run.
        [Test]
        public void ASelfSelectingPress_PaysExactlyOnce_AndIsRepeatable()
        {
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "fans", RecordsId },
                rung: MakeAlbumRung(new SelfAndContainedSelector()));
            var scope = ScopeFactory.Build(garage, "frontier", Database(garage));

            scope.Currencies.Add("fans", 45);
            Assert.IsTrue(scope.CompleteRung("cut_demo"));

            Assert.AreEqual(3.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "floor((45 / 5) ^ 0.5), once - not twice for a rung that selected its own scope");
            Assert.AreEqual(0.0, scope.Currencies.Get("fans").ToDouble(), 1e-9, "and the run reset");

            scope.Currencies.Add("fans", 45);
            Assert.IsTrue(scope.CompleteRung("cut_demo"), "a latchless rung never reads as completed");

            Assert.AreEqual(6.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "so the second run banks its own payout on top");
        }

        // A latch-bearing rung never rides along on another rung's press: a
        // completion's awards are inseparable from its latch, only the initiator
        // latches, and a one-shot paid as a participant would pay again on every
        // press. The tier below files a completion of its own, and the parent's
        // press selects it - it is cleared, and it neither pays nor latches.
        [Test]
        public void ALatchBearingRung_NeverRidesAlongOnAnotherRungsPress()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung(),
                tierRung: TestContent.MakeRung("tier_capstone",
                    actions: new List<GameAction> { new GrantCurrencyAction(RoadiesId, 5) },
                    latchFlagId: "tier_done"),
                tierFlags: new List<FlagDeclaration> { new("tier_done") });
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            Assert.IsTrue(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(2.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "the pressed rung's own award paid");
            Assert.AreEqual(0.0, scope.Currencies.Get(RoadiesId).ToDouble(), 1e-9,
                "the selected scope's latch-bearing rung did not: only the initiator completes");
            Assert.IsFalse(tier.Flags.IsSet("tier_done"), "and nothing latched on its behalf");
            Assert.AreEqual(0.0, tier.Currencies.Get("fans").ToDouble(), 1e-9,
                "though the scope was still cleared, which is what the selector asked for");
        }

        // `offer` governs whether a rung is PRESENTED and is never asked by the
        // press: a participant is a rung filed on a selected scope, not a row
        // the player could have pressed. If the press asked, a chapter whose
        // album is only offered past some threshold would silently strand the
        // run it clears.
        [Test]
        public void AParticipantWhoseOfferIsUnmet_StillRuns()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung(),
                tierRung: MakeAlbumRung(offer: new FlagSetCondition("encore_called")),
                tierFlags: new List<FlagDeclaration> { new("encore_called") });
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            Assert.IsFalse(tier.Conditions.IsFlagSet("encore_called"),
                "the album rung would not be offered right now");

            Assert.IsTrue(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(5.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "3 from the album that was never on offer, plus the capstone's flat 2");
        }

        // ---- what is cleared ---------------------------------------------------

        // The press clears the SELECTED set, not the scope that initiated it -
        // which is what keeps a capstone-shaped rung from wiping the completion
        // it just latched. The parent's own run balance is the observable: it
        // survives because the parent's selector named only the tier.
        [Test]
        public void TheInitiatingScopeIsNotCleared_WhenItsSelectorDoesNotSelectIt()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung());
            var tier = scope.Children[0];
            scope.Currencies.Add("cash", 50);
            tier.Currencies.Add("fans", 45);

            Assert.IsTrue(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(0.0, tier.Currencies.Get("fans").ToDouble(), 1e-9,
                "the selected tier's run facts cleared");
            Assert.AreEqual(50.0, scope.Pool.Get("cash").ToDouble(), 1e-9,
                "the initiating scope's own run balance survived - it was never selected");
            Assert.IsTrue(scope.Flags.IsSet("chapter_2"),
                "and the permanent completion flag stands");
        }

        // Clearing is IN PLACE: the instance and every subscription on it
        // survive, because three things rest on it - stable save identity, live
        // UI bindings, and the surviving dirty flag. A press that rebuilt the
        // scope would leave a bound row listening to an object nothing writes
        // to again, which is silent at the moment it happens.
        [Test]
        public void ClearingIsInPlace_SoSubscriptionsOnTheClearedScopeSurviveThePress()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung());
            var tier = scope.Children[0];
            var pool = tier.Pool;
            tier.Currencies.Add("fans", 45);

            var changes = 0;
            pool.BalanceChanged += (id, balance) => changes++;

            Assert.IsTrue(scope.CompleteRung("backyard_party"));
            Assert.AreNotEqual(0, changes, "the clear itself published through the standing subscription");

            changes = 0;
            tier.Currencies.Add("fans", 5);

            Assert.AreEqual(1, changes,
                "and a mutation AFTER the press still reaches it: the pool was cleared, not replaced");
            Assert.AreSame(pool, tier.Pool, "same instance either way");
        }

        // ---- what comes back ---------------------------------------------------

        // onComplete is re-applicable state, so the press never executes it: the
        // projection re-applies it FROM the latched flag, which is the only door
        // it enters through. That is what lets it survive a clear and a load
        // unchanged - the flag is the fact, and the buff is derived from it every
        // time the store is rebuilt.
        [Test]
        public void OnCompleteIsProjectedFromTheLatch_SoARebuildKeepsIt()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung(
                onComplete: new GrantModifierEffect(TestContent.Sel("cash_yield"),
                    ModifierOperation.Multiply, 4)));
            var cashYield = TestContent.YieldOf("cash");

            Assert.AreEqual(1.0, scope.Modifiers.For(cashYield).Multiply.ToDouble(), 1e-9,
                "nothing is latched, so there is nothing to project from");

            Assert.IsTrue(scope.CompleteRung("backyard_party"));
            Assert.AreEqual(4.0, scope.Modifiers.For(cashYield).Multiply.ToDouble(), 1e-9,
                "the press latched, and the projection that followed applied the effect");

            // the rebuild, twice over: the store is emptied and re-derived, and
            // then the whole scope is restored from its own capture
            scope.ClearForReset();
            scope.ProjectModifiers();
            Assert.AreEqual(4.0, scope.Modifiers.For(cashYield).Multiply.ToDouble(), 1e-9,
                "a clear and a re-projection put it back: it was read off the flag, not off the press");

            scope.Restore(scope.CaptureLocalState());
            Assert.AreEqual(4.0, scope.Modifiers.For(cashYield).Multiply.ToDouble(), 1e-9,
                "and a load round-trip does the same, with no second copy of the grant anywhere");
        }

        // Zero is a legal payout and the press is still a press: a computed
        // grant's CanExecute passes at zero (unlike a flat one, which is broken
        // content at zero), Execute banks nothing, and the reset still runs.
        // A release at zero fans that refused would leave the player unable to
        // start over.
        [Test]
        public void AZeroPayoutIsARealPress_TheRunFactsStillClear()
        {
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash", "fans", RecordsId },
                rung: MakeAlbumRung(new SelfAndContainedSelector()));
            var scope = ScopeFactory.Build(garage, "frontier", Database(garage));

            scope.Currencies.Add("cash", 50);

            Assert.IsTrue(scope.CompleteRung("cut_demo"), "no fans, and the press is still legal");

            Assert.AreEqual(0.0, scope.Currencies.Get(RecordsId).ToDouble(), 1e-9, "nothing banked");
            Assert.AreEqual(0.0, scope.Currencies.Get("cash").ToDouble(), 1e-9,
                "and the run facts cleared anyway");
        }

        // ---- the preview -------------------------------------------------------

        // The preview runs the SAME plan the press does, so a capstone-shaped
        // button promises the participating album payout too - not merely its
        // own actions. Two numbers that came from two resolutions could disagree
        // the moment a participant is added; here they cannot.
        [Test]
        public void ThePreviewIncludesTheParticipatingPayouts_AndEqualsWhatThePressBanks()
        {
            var scope = BuildCapstoneOverATier(MakeCapstoneRung(), tierRung: MakeAlbumRung());
            var tier = scope.Children[0];
            tier.Currencies.Add("fans", 45);

            var previewed = scope.PendingRungGrant("backyard_party", RecordsId);

            Assert.AreEqual(5.0, previewed.ToDouble(), 1e-9,
                "the tier's album payout (3) plus the capstone's own flat award (2)");

            Assert.IsTrue(scope.CompleteRung("backyard_party"));

            Assert.AreEqual(previewed.ToDouble(), scope.Currencies.Get(RecordsId).ToDouble(), 1e-9,
                "what the press banked is what the preview promised");
        }

        // The other end of the same read: an empty run previews nothing, rather
        // than the curve's value at some remembered balance.
        [Test]
        public void ThePreviewAtZero_ReadsZero()
        {
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "fans", RecordsId },
                rung: MakeAlbumRung(new SelfAndContainedSelector()));
            var scope = ScopeFactory.Build(garage, "frontier", Database(garage));

            Assert.AreEqual(0.0, scope.PendingRungGrant("cut_demo", RecordsId).ToDouble(), 1e-9);
        }

        // ---- selector geometry -------------------------------------------------

        // Output closes downward, in the selector base class so no member can
        // forget: a scope's contents include its ladder, and clearing a scope
        // while a child kept its facts would leave the child's formulas reading
        // state the reset claims is gone. The grandchild is the assertion - no
        // selector named it.
        [Test]
        public void SelfAndContainedSelection_ClosesDownward()
        {
            var grandchild = TestContent.MakeScope("tier_2",
                currencyIds: new List<string> { "rehearsal" });
            var child = TestContent.MakeScope("tier_1",
                currencyIds: new List<string> { "fans" },
                childScopeIds: new List<string> { "tier_2" });
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "tier_1" },
                rung: TestContent.MakeRung("press", new SelfAndContainedSelector()));

            var scope = ScopeFactory.Build(garage, "frontier", Database(garage, child, grandchild));
            scope.Currencies.Add("cash", 10);
            scope.Children[0].Currencies.Add("fans", 10);
            scope.Children[0].Children[0].Currencies.Add("rehearsal", 10);

            Assert.IsTrue(scope.CompleteRung("press"));

            Assert.AreEqual(0.0, scope.Pool.Get("cash").ToDouble(), 1e-9, "the owner cleared");
            Assert.AreEqual(0.0, scope.Children[0].Pool.Get("fans").ToDouble(), 1e-9, "and its child");
            Assert.AreEqual(0.0, scope.Children[0].Children[0].Pool.Get("rehearsal").ToDouble(), 1e-9,
                "and the child's child, which nothing named: the selection closes downward");
        }

        // The deep-rung shape, as rule 14's table states it: THIS scope plus
        // the siblings before it in the parent's authored child order - a late
        // rung resets itself and the rungs climbed to reach it, or its own
        // facts would survive its own press. Ordering is the PARENT's, because
        // only the parent owns it - so pressing the middle sibling's rung takes
        // the first and itself, and leaves the third.
        [Test]
        public void PrecedingSiblingsSelection_TakesTheOwnerAndTheEarlierSiblings()
        {
            var first = TestContent.MakeScope("first", currencyIds: new List<string> { "fans" });
            var second = TestContent.MakeScope("second",
                currencyIds: new List<string> { "rehearsal" },
                rung: TestContent.MakeRung("climb", new PrecedingSiblingsSelector()));
            var third = TestContent.MakeScope("third", currencyIds: new List<string> { "merch" });
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "first", "second", "third" });

            var scope = ScopeFactory.Build(garage, "frontier", Database(garage, first, second, third));
            scope.Children[0].Currencies.Add("fans", 10);
            scope.Children[1].Currencies.Add("rehearsal", 10);
            scope.Children[2].Currencies.Add("merch", 10);

            Assert.IsTrue(scope.Children[1].CompleteRung("climb"));

            Assert.AreEqual(0.0, scope.Children[0].Pool.Get("fans").ToDouble(), 1e-9,
                "the sibling before it cleared");
            Assert.AreEqual(0.0, scope.Children[1].Pool.Get("rehearsal").ToDouble(), 1e-9,
                "the presser's own scope cleared too - preceding siblings is 'this scope plus'");
            Assert.AreEqual(10.0, scope.Children[2].Pool.Get("merch").ToDouble(), 1e-9,
                "the one after it is untouched: preceding, in the parent's authored order");
        }

        // A selector only reaches downward: a named scope that is not among the
        // owner's descendants would clear truth the rung cannot even read. The
        // id is reported and SKIPPED rather than resolved anywhere else in the
        // tree, and the rest of the selection still happens - a broken name in
        // a list must not silently cancel the press it appears in.
        [Test]
        public void ANamedScopeThatIsNotADescendant_IsReportedAndSkipped_WhileTheRestStillClear()
        {
            // authored, resolvable, and simply not under the owner
            var elsewhere = TestContent.MakeScope("elsewhere");
            var tier = TestContent.MakeScope("tier_1", currencyIds: new List<string> { "fans" });
            var garage = TestContent.MakeScope("garage",
                currencyIds: new List<string> { "cash" },
                childScopeIds: new List<string> { "tier_1" },
                rung: TestContent.MakeRung("press",
                    new NamedScopesSelector(new List<string> { "tier_1", "elsewhere" })));

            var scope = ScopeFactory.Build(garage, "frontier", Database(garage, tier, elsewhere));
            scope.Children[0].Currencies.Add("fans", 10);

            LogAssert.Expect(LogType.Error, new Regex(
                "names scope 'elsewhere', which is not among its descendants"));

            Assert.IsTrue(scope.CompleteRung("press"));

            Assert.AreEqual(0.0, scope.Children[0].Pool.Get("fans").ToDouble(), 1e-9,
                "the resolvable id still cleared its scope");
        }

        // ---- the generator forward ---------------------------------------------

        // A discarded system must stop re-broadcasting a generator that outlives
        // it. The forward is per generator and kept so it can be removed: an
        // anonymous handler with no reference held is one nothing can ever
        // unhook - harmless while system and generators die together, a leak the
        // moment a press rebuilds one without the other.
        [Test]
        public void DisposingTheGeneratorSystem_SeversTheOwnedChangedForward()
        {
            var currencies = TestContent.MakeEconomy();
            var generators = new GeneratorSystem(
                new[]
                {
                    TestContent.MakeGenerator("drummer", "cash",
                        baseCost: 10, costGrowth: 1.15, baseOutput: 3),
                },
                currencies, new ModifierSystem());
            var drummer = generators.Get("drummer");

            var signals = 0;
            generators.GeneratorOwnedChanged += _ => signals++;

            // the control: while the forward is hooked, a purchase reaches the
            // subscriber - or "it did not fire" below proves nothing
            TestContent.BuyTimes(drummer, currencies, 1);
            Assert.AreEqual(1, signals);

            generators.Dispose();
            TestContent.BuyTimes(drummer, currencies, 1);

            Assert.AreEqual(2, drummer.Owned, "the purchase itself still went through");
            Assert.AreEqual(1, signals,
                "but no callback: the disposed system unhooked its per-generator forward");
        }
    }
}
