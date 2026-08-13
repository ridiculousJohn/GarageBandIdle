using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The fillable-bar system and fill-currency production. The load-bearing
    // claims: bars are independent per-bar progress (never cumulative
    // thresholds on one counter), the continuous drain is clamped and
    // player-directed, completion applies the pool reward exactly once,
    // barsCompleted conditions read live counts, and fill-currency accrual is
    // producer-held production configs (design doc section 12, rule 13) -
    // firing from tick + taps only while each config's own gate holds.
    public class BarsAndRehearsalTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        private static CurrencyManager MakeEconomyWithRehearsal()
        {
            var groups = new[] { TestContent.MakeGroup("run", true) };
            var currencies = new[]
            {
                TestContent.MakeCurrency("cash", "run"),
                TestContent.MakeCurrency("fans", "run"),
                TestContent.MakeCurrency("rehearsal", "run"),
            };
            return new CurrencyManager(groups, currencies);
        }

        private static BarSystem MakeCoversSetup(CurrencyManager currencies, FlagSystem flags,
            out ProductionSystem fans)
        {
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var rewards = new RewardManager(new RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
                TestContent.MakeFanRateReward("fan_rate_x1_20", 1.2),
            });

            var bars = new[]
            {
                TestContent.MakeBar("cover_1", "rehearsal", 120, "fan_rate_x1_15"),
                TestContent.MakeBar("cover_2", "rehearsal", 300, "fan_rate_x1_15"),
                TestContent.MakeBar("cover_3", "rehearsal", 600, "fan_rate_x1_20"),
            };
            var group = TestContent.MakeBarGroup("learn_covers", new FlagSetCondition("covers"),
                new List<string> { "cover_1", "cover_2", "cover_3" });

            return new BarSystem(new[] { group }, bars, currencies, rewards,
                new EffectContext(currencies, flags, modifiers));
        }

        // MakeCoversSetup plus a permanent-in-chapter group, for the run-reset
        // scope split
        private static BarSystem MakeTwoScopeSetup(CurrencyManager currencies, FlagSystem flags,
            out ProductionSystem fans, out ModifierSystem modifiers)
        {
            modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var rewards = new RewardManager(new RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
                TestContent.MakeFanRateReward("fan_rate_x1_20", 1.2),
            });

            var bars = new[]
            {
                TestContent.MakeBar("cover_1", "rehearsal", 120, "fan_rate_x1_15"),
                TestContent.MakeBar("cover_2", "rehearsal", 300),
                TestContent.MakeBar("song_1", "rehearsal", 100, "fan_rate_x1_20"),
            };
            var run = TestContent.MakeBarGroup("learn_covers", new FlagSetCondition("covers"),
                new List<string> { "cover_1", "cover_2" });
            var permanent = TestContent.MakeBarGroup("setlist", new FlagSetCondition("covers"),
                new List<string> { "song_1" }, scope: ContentScope.PermanentInChapter);

            return new BarSystem(new[] { run, permanent }, bars, currencies, rewards,
                new EffectContext(currencies, flags, modifiers));
        }

        [Test]
        public void Production_IsDormantUntilTheConfigsGate()
        {
            var rehearsal = TestContent.MakeCurrency("rehearsal", "run");
            var currencies = new CurrencyManager(new[] { TestContent.MakeGroup("run", true) }, new[] { rehearsal });
            var flags = new FlagSystem();
            var producer = TestContent.MakeProducer("jam",
                ("rehearsal", 1, ProductionFeed.Rate, new FlagSetCondition("covers")),
                ("rehearsal", 2, ProductionFeed.Yield, new FlagSetCondition("covers")));
            var production = new ProductionSystem(new[] { producer }, null, null, currencies, new ModifierSystem(),
                TestContent.MakeContext(currencies, flags: flags));

            production.Accrue(10);
            production.Fire("jam");
            Assert.AreEqual(0.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "no accrual before the gate");
            Assert.AreEqual(0.0, production.RateOf("rehearsal").ToDouble(), 1e-9);

            flags.Set("covers");

            production.Accrue(10);
            Assert.AreEqual(10.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "per-sec amount x seconds");
            production.Fire("jam");
            Assert.AreEqual(12.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "+per-tap amount on a Jam tap");
        }

        // The readout answers the same question the payout does. A tap config
        // whose gate is unmet pays nothing when FireTap runs, so advertising
        // "+2/tap" beside the balance would promise a yield tapping does not
        // deliver - and unlike a stale number, an authored-looking one gives the
        // player no reason to doubt it.
        [Test]
        public void ProductionReadout_HonoursGates_TheSameWayThePayoutDoes()
        {
            var rehearsal = TestContent.MakeCurrency("rehearsal", "run");
            var currencies = new CurrencyManager(new[] { TestContent.MakeGroup("run", true) }, new[] { rehearsal });
            var flags = new FlagSystem();
            var producer = TestContent.MakeProducer("jam",
                ("rehearsal", 1, ProductionFeed.Rate, new FlagSetCondition("covers")),
                ("rehearsal", 2, ProductionFeed.Yield, new FlagSetCondition("covers")));
            var production = new ProductionSystem(new[] { producer }, null, null, currencies, new ModifierSystem(),
                TestContent.MakeContext(currencies, flags: flags));

            Assert.IsFalse(production.HasProduction("rehearsal"), "nothing can fill it while the gate is shut");
            Assert.AreEqual(0.0, production.YieldOf("rehearsal").ToDouble(), 1e-9, "a dormant tap config advertises nothing");
            Assert.AreEqual(0.0, production.RateOf("rehearsal").ToDouble(), 1e-9);

            // what a tap actually pays while the gate is shut - the number the
            // readout has to agree with
            production.Fire("jam");
            Assert.AreEqual(0.0, currencies.Get("rehearsal").ToDouble(), 1e-9);

            flags.Set("covers");

            Assert.IsTrue(production.HasProduction("rehearsal"));
            Assert.AreEqual(2.0, production.YieldOf("rehearsal").ToDouble(), 1e-9);
            Assert.AreEqual(1.0, production.RateOf("rehearsal").ToDouble(), 1e-9);

            production.Fire("jam");
            Assert.AreEqual(2.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "the tap pays what the readout advertised");
        }

        // a chapter can hold several independently produced fill currencies -
        // each config carries its own gate (an ordinary Condition, design doc
        // section 12 rule 13), and a currency no config names never accrues
        // from engagement
        [Test]
        public void FillCurrencies_ProduceIndependently_EachGatedByItsOwnCondition()
        {
            var rehearsal = TestContent.MakeCurrency("rehearsal", "run");
            var stagecraft = TestContent.MakeCurrency("stagecraft", "run");
            var cash = TestContent.MakeCurrency("cash", "run");
            var currencies = new CurrencyManager(new[] { TestContent.MakeGroup("run", true) },
                new[] { rehearsal, stagecraft, cash });
            var flags = new FlagSystem();
            var producer = TestContent.MakeProducer("jam",
                ("rehearsal", 1, ProductionFeed.Rate, new FlagSetCondition("covers")),
                ("rehearsal", 2, ProductionFeed.Yield, new FlagSetCondition("covers")),
                ("stagecraft", 3, ProductionFeed.Rate, new FlagSetCondition("openmic")));
            var production = new ProductionSystem(new[] { producer }, null, null, currencies, new ModifierSystem(),
                TestContent.MakeContext(currencies, flags: flags));

            flags.Set("covers");
            production.Accrue(10);
            production.Fire("jam");

            Assert.AreEqual(12.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "the gated-open currency earns tick + tap");
            Assert.AreEqual(0.0, currencies.Get("stagecraft").ToDouble(), 1e-9, "its own gate governs it, not another's");
            Assert.AreEqual(0.0, currencies.Get("cash").ToDouble(), 1e-9, "no config = no engagement production");
            Assert.IsFalse(production.HasProduction("cash"));

            flags.Set("openmic");
            production.Accrue(10);
            Assert.AreEqual(30.0, currencies.Get("stagecraft").ToDouble(), 1e-9, "amount x seconds once its gate holds");
            Assert.AreEqual(22.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "both produce once both gates hold");
        }

        [Test]
        public void Pool_AccumulatesUntilABarIsSelected_ThenDrainsClamped()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            currencies.Add("rehearsal", 200);
            bars.Tick();
            Assert.AreEqual(200.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "no target = pool holds");
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"));

            // selecting pours the built-up pool in immediately, clamped to the
            // bar's requirement; the excess stays in the pool
            var covers = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            covers.SetActiveBar("cover_1");

            var cover1 = bars.GetBars("learn_covers")[0];
            Assert.IsTrue(cover1.Completed, "120 requirement filled from a 200 pool");
            Assert.AreEqual(120.0, cover1.Progress.ToDouble(), 1e-9);
            Assert.AreEqual(80.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "overfill never spends");
        }

        [Test]
        public void Bars_TrackTheirOwnProgress_Independently()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            // pour 50 into cover_2, then redirect to cover_3 and pour 70
            var covers = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            currencies.Add("rehearsal", 50);
            covers.SetActiveBar("cover_2");
            currencies.Add("rehearsal", 70);
            covers.SetActiveBar("cover_3");

            var list = bars.GetBars("learn_covers");
            Assert.AreEqual(0.0, list[0].Progress.ToDouble(), 1e-9, "unselected bar untouched");
            Assert.AreEqual(50.0, list[1].Progress.ToDouble(), 1e-9, "progress stays when deselected");
            Assert.AreEqual(70.0, list[2].Progress.ToDouble(), 1e-9, "independent accumulation");
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"), "independent bars, not cumulative thresholds");
        }

        [Test]
        public void Completion_AppliesThePoolRewardOnce_AndClearsSelection()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeCoversSetup(currencies, flags, out var fans);
            var completions = 0;
            bars.BarCompleted += _ => completions++;

            var covers = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            covers.SetActiveBar("cover_1");
            currencies.Add("rehearsal", 120);
            bars.Tick();

            Assert.AreEqual(1, completions);
            Assert.AreEqual(0.2 * 1.15, fans.RateOf("fans").ToDouble(), 1e-9, "fan-rate reward applied on completion");
            Assert.IsNull(covers.ActiveBar, "completion clears the target");

            // further ticks and reselection attempts must not re-apply
            currencies.Add("rehearsal", 500);
            bars.Tick();
            covers.SetActiveBar("cover_1");
            bars.Tick();
            Assert.AreEqual(1, completions, "a completed bar never re-completes");
            Assert.IsNull(covers.ActiveBar, "a completed bar cannot be re-selected");
            Assert.AreEqual(0.2 * 1.15, fans.RateOf("fans").ToDouble(), 1e-9);
        }

        [Test]
        public void BarRewards_StackMultiplicativelyOnFanRate()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeCoversSetup(currencies, flags, out var fans);

            var covers = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            currencies.Add("rehearsal", 420);
            covers.SetActiveBar("cover_1");
            covers.SetActiveBar("cover_2");

            Assert.AreEqual(2, bars.CompletedCount("learn_covers"));
            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RateOf("fans").ToDouble(), 1e-9);
        }

        [Test]
        public void BarsCompletedCondition_ReadsLiveCounts()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);
            var context = new ConditionContext(currencies, null, flags, "records", null, bars);
            var condition = new BarsCompletedCondition("learn_covers", 1);

            Assert.IsFalse(ConditionEvaluator.IsMet(condition, context));

            currencies.Add("rehearsal", 120);
            ((PerBarContinuousRuntime)bars.GetRuntime("learn_covers")).SetActiveBar("cover_1");

            Assert.IsTrue(ConditionEvaluator.IsMet(condition, context), "cover_1 satisfies barsCompleted >= 1");
        }

        // the fill behavior is the mode: a group authored without one (an
        // unimplemented fillMode/delivery pair imports null) fails loudly at
        // construction instead of silently running some other mode's rules
        [Test]
        public void GroupWithNoFillBehavior_IsSkippedLoudly()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var rewards = new RewardManager(new RewardDefinition[0]);
            var bar = TestContent.MakeBar("cover_1", "rehearsal", 120);

            var group = ScriptableObject.CreateInstance<BarGroupDefinition>();
            group.hideFlags = HideFlags.HideAndDontSave;
            group.EditorInitialize("broken", "broken", new FlagSetCondition("covers"), null,
                ContentScope.Run, new List<string> { "cover_1" });

            LogAssert.Expect(LogType.Error, "BarSystem: bar group 'broken' has no fill behavior. Skipping it.");
            var bars = new BarSystem(new[] { group }, new[] { bar }, currencies, rewards,
                new EffectContext(currencies, flags, new ModifierSystem()));

            Assert.AreEqual(0, bars.Groups.Count, "a behaviorless group never reaches the runtime");
            bars.Tick();

            Object.DestroyImmediate(group);
        }

        // fail closed on broken content: a non-positive fill requirement can
        // never be legitimately filled - the bar is rejected at construction,
        // so a content typo can never satisfy a barsCompleted gate or grant
        // its reward at boot (the importer and boot validation report it)
        [Test]
        public void NonPositiveRequirementBar_IsRejected_AndGrantsNothing()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var rewards = new RewardManager(new RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
            });
            var bars = new[] { TestContent.MakeBar("broken_cover", "rehearsal", 0, "fan_rate_x1_15") };
            var group = TestContent.MakeBarGroup("learn_covers", new FlagSetCondition("covers"), new List<string> { "broken_cover" });

            LogAssert.Expect(LogType.Error,
                "BarSystem: bar 'broken_cover' has a non-positive fill requirement (0). Skipping it.");
            var system = new BarSystem(new[] { group }, bars, currencies, rewards,
                new EffectContext(currencies, flags, modifiers));

            Assert.AreEqual(0, system.GetBars("learn_covers").Count, "the rejected bar has no state");
            Assert.AreEqual(0, system.CompletedCount("learn_covers"), "it never satisfies a barsCompleted gate");
            Assert.AreEqual(0.2, fans.RateOf("fans").ToDouble(), 1e-9, "no reward granted");
        }

        // state-then-notify: the drain's BalanceChanged is a synchronous signal
        // that condition evaluators react to, so the completion must already be
        // latched when it fires - a barsCompleted gate may never observe the
        // pool drained with the bar not yet counted as done
        [Test]
        public void Completion_IsLatchedBeforeTheSpendNotifies()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);
            currencies.Add("rehearsal", 120);

            var completedDuringSpend = -1;
            currencies.BalanceChanged += (id, _) =>
            {
                if (id == "rehearsal")
                    completedDuringSpend = bars.CompletedCount("learn_covers");
            };

            ((PerBarContinuousRuntime)bars.GetRuntime("learn_covers")).SetActiveBar("cover_1");

            Assert.AreEqual(1, completedDuringSpend);
        }

        // the run reset (album release, event baseline) honors each group's
        // declared scope: run groups forget everything, permanent-in-chapter
        // groups keep it, and no reward ever re-applies
        [Test]
        public void ResetRunScopedGroups_ClearsRunGroups_KeepsPermanentInChapter()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeTwoScopeSetup(currencies, flags, out var fans, out _);
            var coversRuntime = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            var setlistRuntime = (PerBarContinuousRuntime)bars.GetRuntime("setlist");

            currencies.Add("rehearsal", 120);
            coversRuntime.SetActiveBar("cover_1"); // completes, applies x1.15
            currencies.Add("rehearsal", 50);
            coversRuntime.SetActiveBar("cover_2"); // partial, stays selected
            currencies.Add("rehearsal", 100);
            setlistRuntime.SetActiveBar("song_1"); // completes, applies x1.2
            Assert.AreEqual(1, bars.CompletedCount("learn_covers"));
            Assert.AreEqual(1, bars.CompletedCount("setlist"));
            Assert.IsNotNull(coversRuntime.ActiveBar);

            bars.ResetRunScopedGroups();

            var covers = bars.GetBars("learn_covers");
            Assert.AreEqual(0.0, covers[0].Progress.ToDouble(), 1e-9);
            Assert.IsFalse(covers[0].Completed);
            Assert.AreEqual(0.0, covers[1].Progress.ToDouble(), 1e-9);
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"), "the run group forgets its completions");
            Assert.IsNull(coversRuntime.ActiveBar, "the run reset clears the selection");

            Assert.IsTrue(bars.GetBars("setlist")[0].Completed, "permanent-in-chapter survives the run reset");
            Assert.AreEqual(1, bars.CompletedCount("setlist"));
            Assert.AreEqual(0.2 * 1.15 * 1.2, fans.RateOf("fans").ToDouble(), 1e-9, "the reset re-applies no rewards");
        }

        // One reward ASSET, two lifetimes - the property a scope field on the reward
        // could not express at all, rather than merely stated twice. The same id is
        // paid by a run-scoped group and a permanent one, each grant takes the
        // durability of the completion it projects from, and the run reset keeps
        // exactly one of the two.
        [Test]
        public void OneRewardAsset_TakesADifferentLifetimeFromEachSourceApplyingIt()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var modifiers = new ModifierSystem();
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies, modifiers);
            var fans = TestContent.MakeFanProduction(modifiers, generators, currencies,
                new ConditionContext(currencies, generators, flags), new FlagSetCondition("fans"));
            var rewards = new RewardManager(new[] { TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15) });

            var bars = new[]
            {
                TestContent.MakeBar("cover_1", "rehearsal", 100, "fan_rate_x1_15"),
                TestContent.MakeBar("song_1", "rehearsal", 100, "fan_rate_x1_15"),
            };
            var run = TestContent.MakeBarGroup("learn_covers", new FlagSetCondition("covers"), new List<string> { "cover_1" });
            var permanent = TestContent.MakeBarGroup("setlist", new FlagSetCondition("covers"), new List<string> { "song_1" },
                scope: ContentScope.PermanentInChapter);
            var system = new BarSystem(new[] { run, permanent }, bars, currencies, rewards,
                new EffectContext(currencies, flags, modifiers));

            currencies.Add("rehearsal", 100);
            ((PerBarContinuousRuntime)system.GetRuntime("learn_covers")).SetActiveBar("cover_1");
            currencies.Add("rehearsal", 100);
            ((PerBarContinuousRuntime)system.GetRuntime("setlist")).SetActiveBar("song_1");
            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RateOf("fans").ToDouble(), 1e-9,
                "the one asset granted once per completion");

            TestContent.RunReset(modifiers, bars: system);

            Assert.AreEqual(0.2 * 1.15, fans.RateOf("fans").ToDouble(), 1e-9,
                "the run group's completion is gone so its grant did not come back; the permanent group's completion survived and re-granted the same asset");
        }

        // The group's scope is how long bar completion lasts, and the reward's grant
        // projects from that completion, so it inherits the same durability (design
        // doc rule 11) rather than declaring one. Both groups here pay the same kind
        // of reward and differ only in their own scope, so the effects half of a
        // release has to split them: a cover's boost
        // goes with the bars it came from, a setlist song's stays. The release does
        // that by resetting the run group's completions and re-projecting, so the
        // cover's boost is absent because its FACT is absent.
        //
        // While the scope lived on the shared reward asset it could disagree with the
        // group paying it, and the disagreement was invisible: a run-scoped group
        // whose reward claimed permanence re-granted that multiplier every run and
        // compounded without limit.
        [Test]
        public void BarRewards_TakeTheirGroupsScope_SoARunResetSplitsThem()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeTwoScopeSetup(currencies, flags, out var fans, out var modifiers);
            var coversRuntime = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            var setlistRuntime = (PerBarContinuousRuntime)bars.GetRuntime("setlist");

            currencies.Add("rehearsal", 120);
            coversRuntime.SetActiveBar("cover_1"); // run group, grants x1.15
            currencies.Add("rehearsal", 100);
            setlistRuntime.SetActiveBar("song_1"); // permanent-in-chapter group, grants x1.2
            Assert.AreEqual(0.2 * 1.15 * 1.2, fans.RateOf("fans").ToDouble(), 1e-9,
                "both grants stack while the run lives");

            TestContent.RunReset(modifiers, bars: bars);

            Assert.AreEqual(0.2 * 1.2, fans.RateOf("fans").ToDouble(), 1e-9,
                "the run group's completion reset so its grant is not re-projected; the permanent group's survived and is");
        }

        // state-then-notify: by the time any BarProgressChanged subscriber
        // runs, the whole run-scoped reset has settled - no half-reset group
        // is ever observable, and nothing completes
        [Test]
        public void ResetRunScopedGroups_StateSettlesBeforeNotifications()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeTwoScopeSetup(currencies, flags, out _, out _);
            var coversRuntime = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            currencies.Add("rehearsal", 50);
            coversRuntime.SetActiveBar("cover_1");
            coversRuntime.SetActiveBar("cover_2");
            currencies.Add("rehearsal", 30);
            bars.Tick();

            var list = bars.GetBars("learn_covers");
            var notifications = 0;
            var observedPartialReset = false;
            bars.BarProgressChanged += _ =>
            {
                notifications++;
                if (list[0].Progress.ToDouble() != 0.0 || list[1].Progress.ToDouble() != 0.0
                    || coversRuntime.ActiveBar != null)
                    observedPartialReset = true;
            };
            var completions = 0;
            bars.BarCompleted += _ => completions++;

            bars.ResetRunScopedGroups();

            Assert.AreEqual(2, notifications, "one progress notification per changed bar");
            Assert.IsFalse(observedPartialReset, "every subscriber sees fully settled state");
            Assert.AreEqual(0, completions, "a reset never completes anything");
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, BigNumber>> Snapshot(
            string groupId, Dictionary<string, BigNumber> progressByBarId)
            => new Dictionary<string, IReadOnlyDictionary<string, BigNumber>> { [groupId] = progressByBarId };

        // A selection is player INTENT, not progress, so a restore has to say what it
        // is - and before 6.5 it said nothing: RestoreProgress cleared a selection
        // only when its bar had become complete, so an unrelated pre-restore target
        // survived and the next drain poured the restored fill currency into it. Every
        // balance and every progress value came from the snapshot; where the currency
        // went came from a decision made before it.
        [Test]
        public void RestoreProgress_DropsAnyStandingSelection_AndRestoreActiveBarsPutsBackTheSnapshots()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("covers");
            var bars = MakeCoversSetup(currencies, flags, out _);
            var runtime = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");

            runtime.SetActiveBar("cover_3");
            Assert.AreEqual("cover_3", runtime.ActiveBar.Definition.Id);

            // a snapshot about cover_1 only - it says nothing about cover_3, so
            // nothing may still be pouring into cover_3
            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber> { ["cover_1"] = 10 }));
            Assert.IsNull(runtime.ActiveBar, "the standing selection went with the state it belonged to");

            bars.RestoreActiveBars(new Dictionary<string, string> { ["learn_covers"] = "cover_1" });
            Assert.AreEqual("cover_1", runtime.ActiveBar.Definition.Id, "the snapshot's own choice is back");

            // and it round-trips
            CollectionAssert.AreEquivalent(new[] { "learn_covers" }, bars.CaptureActiveBars().Keys);
            Assert.AreEqual("cover_1", bars.CaptureActiveBars()["learn_covers"]);
        }

        // Drain must never hold a completed target, so a snapshot naming one - stale
        // save data, or a requirement retuned downwards - leaves the group unselected
        // rather than stuck on a bar that can take nothing.
        [Test]
        public void RestoreActiveBars_RefusesACompletedTarget()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("covers");
            var bars = MakeCoversSetup(currencies, flags, out _);
            var runtime = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");

            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber> { ["cover_1"] = 120 }));
            Assert.IsTrue(bars.GetBars("learn_covers")[0].Completed);

            bars.RestoreActiveBars(new Dictionary<string, string> { ["learn_covers"] = "cover_1" });

            Assert.IsNull(runtime.ActiveBar, "a completed bar is never the drain's target");
        }

        // save/load: the snapshot re-establishes progress through the same
        // clamp-and-derive rule as accrual - a restored completion is
        // recorded fact (no reward, no BarCompleted), and the derivation
        // holds in both directions
        [Test]
        public void RestoreProgress_DerivesCompletion_WithoutRewardOrCompletionEvent()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeCoversSetup(currencies, flags, out var fans);
            var completions = 0;
            bars.BarCompleted += _ => completions++;
            var list = bars.GetBars("learn_covers");

            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber>
            {
                ["cover_1"] = 120, // exactly the requirement
                ["cover_2"] = 50,  // partial
                ["cover_3"] = 900, // over the 600 requirement
            }));

            Assert.IsTrue(list[0].Completed, "restored-full derives completed");
            Assert.AreEqual(50.0, list[1].Progress.ToDouble(), 1e-9);
            Assert.IsFalse(list[1].Completed);
            Assert.AreEqual(600.0, list[2].Progress.ToDouble(), 1e-9, "restore clamps to the requirement");
            Assert.IsTrue(list[2].Completed);
            Assert.AreEqual(2, bars.CompletedCount("learn_covers"));
            Assert.AreEqual(0, completions, "a restored completion is fact, not an occurrence");
            Assert.AreEqual(0.2, fans.RateOf("fans").ToDouble(), 1e-9, "restore grants no rewards");

            // Authoritative in both directions, and REPLACEMENT rather than a merge
            // (6.5): cover_1 falls below its requirement and un-completes, AND the
            // bars this snapshot does not name return to zero instead of keeping
            // what they held - so cover_3's completion goes with them. A merge would
            // leave a previous restore's progress standing under a different
            // snapshot, which is two routes to one state.
            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber> { ["cover_1"] = 10 }));
            Assert.IsFalse(list[0].Completed);
            Assert.AreEqual(0.0, list[2].Progress.ToDouble(), 1e-9,
                "a bar the snapshot omits is cleared, not left alone");
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"));

            // corrupt save data fails closed to an empty bar
            LogAssert.Expect(LogType.Error,
                "BarSystem: RestoreProgress with negative progress for bar 'cover_2'. Restoring an empty bar.");
            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber> { ["cover_2"] = -5 }));
            Assert.AreEqual(0.0, list[1].Progress.ToDouble(), 1e-9);
        }

        // the snapshot is atomic: by the time any subscriber runs, every
        // saved bar holds its final value and a selection left on a
        // now-completed bar is already cleared - Drain must never sit on a
        // completed target, and no subscriber may observe a half-restored
        // system
        [Test]
        public void RestoreProgress_SettlesTheWholeSnapshotBeforeNotifying()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);
            var coversRuntime = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            coversRuntime.SetActiveBar("cover_2");
            var list = bars.GetBars("learn_covers");

            var notifications = 0;
            var observedPartialRestore = false;
            bars.BarProgressChanged += _ =>
            {
                notifications++;
                if (list[0].Progress.ToDouble() != 120.0 || list[1].Progress.ToDouble() != 300.0
                    || coversRuntime.ActiveBar != null)
                    observedPartialRestore = true;
            };
            var activeChanges = 0;
            coversRuntime.ActiveBarChanged += () => activeChanges++;

            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber>
            {
                ["cover_1"] = 120,
                ["cover_2"] = 300, // the selected bar restores to complete
            }));

            Assert.AreEqual(2, notifications, "one progress notification per restored bar");
            Assert.IsFalse(observedPartialRestore, "every subscriber sees the whole snapshot settled");
            Assert.IsNull(coversRuntime.ActiveBar, "a completed bar can never stay the drain target");
            Assert.AreEqual(1, activeChanges, "the cleared selection notifies");
        }

        // stale save data fails closed: unknown group and bar ids are
        // reported and skipped, and nothing else in the snapshot is lost
        [Test]
        public void RestoreProgress_SkipsUnknownIdsLoudly()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            LogAssert.Expect(LogType.Error,
                "BarSystem: RestoreProgress with unknown bar group id 'ghost_group'. Skipping it.");
            bars.RestoreProgress(Snapshot("ghost_group", new Dictionary<string, BigNumber> { ["cover_1"] = 50 }));

            LogAssert.Expect(LogType.Error,
                "BarSystem: RestoreProgress with unknown bar id 'ghost' in group 'learn_covers'. Skipping it.");
            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber>
            {
                ["ghost"] = 50,
                ["cover_1"] = 70, // the valid entry still restores
            }));

            Assert.AreEqual(70.0, bars.GetBars("learn_covers")[0].Progress.ToDouble(), 1e-9);
        }

        [Test]
        public void TogglingTheActiveBar_Deselects()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);

            var covers = (PerBarContinuousRuntime)bars.GetRuntime("learn_covers");
            covers.SetActiveBar("cover_2");
            Assert.IsNotNull(covers.ActiveBar);

            covers.SetActiveBar(null);
            Assert.IsNull(covers.ActiveBar);

            currencies.Add("rehearsal", 60);
            bars.Tick();
            Assert.AreEqual(60.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "deselected = pool accumulates");
        }
    }
}
