using System.Collections.Generic;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The fillable-bar system and rehearsal accrual. The load-bearing claims:
    // bars are independent per-bar progress (never cumulative thresholds on one
    // counter), the continuous drain is clamped and player-directed, completion
    // applies the pool reward exactly once, and barsCompleted conditions read
    // live counts. Rehearsal accrues from tick + taps only after its flag.
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
            out FanSystem fans)
        {
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies);
            fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags);
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
            var group = TestContent.MakeBarGroup("learn_covers", "covers",
                new List<string> { "cover_1", "cover_2", "cover_3" });

            return new BarSystem(new[] { group }, bars, currencies, rewards,
                new RewardContext(currencies, flags, fans));
        }

        // MakeCoversSetup plus a permanent-in-chapter group, for the run-reset
        // scope split
        private static BarSystem MakeTwoScopeSetup(CurrencyManager currencies, FlagSystem flags,
            out FanSystem fans)
        {
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies);
            fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags);
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
            var run = TestContent.MakeBarGroup("learn_covers", "covers",
                new List<string> { "cover_1", "cover_2" });
            var permanent = TestContent.MakeBarGroup("setlist", "covers",
                new List<string> { "song_1" }, scope: ContentScope.PermanentInChapter);

            return new BarSystem(new[] { run, permanent }, bars, currencies, rewards,
                new RewardContext(currencies, flags, fans));
        }

        [Test]
        public void RehearsalAccrual_IsDormantUntilTheFlag()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var rehearsal = new RehearsalSystem(new RehearsalConfig("rehearsal", "covers", 1, 2),
                currencies, flags);

            rehearsal.Tick(10);
            rehearsal.OnJamTap();
            Assert.AreEqual(0.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "no accrual before the flag");

            flags.Set("covers");

            rehearsal.Tick(10);
            Assert.AreEqual(10.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "perSec × seconds");
            rehearsal.OnJamTap();
            Assert.AreEqual(12.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "+perTap on a Jam tap");
        }

        [Test]
        public void UnconfiguredRehearsal_StaysInert()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("covers");
            var rehearsal = new RehearsalSystem(new RehearsalConfig(null, null, 1, 2), currencies, flags);

            Assert.IsFalse(rehearsal.Configured);
            rehearsal.Tick(10);
            rehearsal.OnJamTap();
            Assert.AreEqual(0.0, currencies.Get("rehearsal").ToDouble(), 1e-9);
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
            bars.SetActiveBar("learn_covers", "cover_1");

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
            currencies.Add("rehearsal", 50);
            bars.SetActiveBar("learn_covers", "cover_2");
            currencies.Add("rehearsal", 70);
            bars.SetActiveBar("learn_covers", "cover_3");

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

            bars.SetActiveBar("learn_covers", "cover_1");
            currencies.Add("rehearsal", 120);
            bars.Tick();

            Assert.AreEqual(1, completions);
            Assert.AreEqual(0.2 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9, "fan-rate reward applied on completion");
            Assert.IsNull(bars.GetActiveBar("learn_covers"), "completion clears the target");

            // further ticks and reselection attempts must not re-apply
            currencies.Add("rehearsal", 500);
            bars.Tick();
            bars.SetActiveBar("learn_covers", "cover_1");
            bars.Tick();
            Assert.AreEqual(1, completions, "a completed bar never re-completes");
            Assert.IsNull(bars.GetActiveBar("learn_covers"), "a completed bar cannot be re-selected");
            Assert.AreEqual(0.2 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9);
        }

        [Test]
        public void BarRewards_StackMultiplicativelyOnFanRate()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var bars = MakeCoversSetup(currencies, flags, out var fans);

            currencies.Add("rehearsal", 420);
            bars.SetActiveBar("learn_covers", "cover_1");
            bars.SetActiveBar("learn_covers", "cover_2");

            Assert.AreEqual(2, bars.CompletedCount("learn_covers"));
            Assert.AreEqual(0.2 * 1.15 * 1.15, fans.RatePerSecond.ToDouble(), 1e-9);
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
            bars.SetActiveBar("learn_covers", "cover_1");

            Assert.IsTrue(ConditionEvaluator.IsMet(condition, context), "cover_1 satisfies barsCompleted >= 1");
        }

        // fail closed on broken content: a non-positive fill requirement can
        // never be legitimately filled — the bar is rejected at construction,
        // so a content typo can never satisfy a barsCompleted gate or grant
        // its reward at boot (the importer and boot validation report it)
        [Test]
        public void NonPositiveRequirementBar_IsRejected_AndGrantsNothing()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            flags.Set("fans");
            var generators = new GeneratorSystem(new GeneratorDefinition[0], currencies);
            var fans = new FanSystem(new FansConfig("fans", "fans", 0.2, 0.02), currencies, generators, flags);
            var rewards = new RewardManager(new RewardDefinition[]
            {
                TestContent.MakeFanRateReward("fan_rate_x1_15", 1.15),
            });
            var bars = new[] { TestContent.MakeBar("broken_cover", "rehearsal", 0, "fan_rate_x1_15") };
            var group = TestContent.MakeBarGroup("learn_covers", "covers", new List<string> { "broken_cover" });

            LogAssert.Expect(LogType.Error,
                "BarSystem: bar 'broken_cover' has a non-positive fill requirement (0). Skipping it.");
            var system = new BarSystem(new[] { group }, bars, currencies, rewards,
                new RewardContext(currencies, flags, fans));

            Assert.AreEqual(0, system.GetBars("learn_covers").Count, "the rejected bar has no state");
            Assert.AreEqual(0, system.CompletedCount("learn_covers"), "it never satisfies a barsCompleted gate");
            Assert.AreEqual(0.2, fans.RatePerSecond.ToDouble(), 1e-9, "no reward granted");
        }

        // state-then-notify: the drain's BalanceChanged is a synchronous signal
        // that condition evaluators react to, so the completion must already be
        // latched when it fires — a barsCompleted gate may never observe the
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

            bars.SetActiveBar("learn_covers", "cover_1");

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
            var bars = MakeTwoScopeSetup(currencies, flags, out var fans);

            currencies.Add("rehearsal", 120);
            bars.SetActiveBar("learn_covers", "cover_1"); // completes, applies ×1.15
            currencies.Add("rehearsal", 50);
            bars.SetActiveBar("learn_covers", "cover_2"); // partial, stays selected
            currencies.Add("rehearsal", 100);
            bars.SetActiveBar("setlist", "song_1");       // completes, applies ×1.2
            Assert.AreEqual(1, bars.CompletedCount("learn_covers"));
            Assert.AreEqual(1, bars.CompletedCount("setlist"));
            Assert.IsNotNull(bars.GetActiveBar("learn_covers"));

            bars.ResetRunScopedGroups();

            var covers = bars.GetBars("learn_covers");
            Assert.AreEqual(0.0, covers[0].Progress.ToDouble(), 1e-9);
            Assert.IsFalse(covers[0].Completed);
            Assert.AreEqual(0.0, covers[1].Progress.ToDouble(), 1e-9);
            Assert.AreEqual(0, bars.CompletedCount("learn_covers"), "the run group forgets its completions");
            Assert.IsNull(bars.GetActiveBar("learn_covers"), "the run reset clears the selection");

            Assert.IsTrue(bars.GetBars("setlist")[0].Completed, "permanent-in-chapter survives the run reset");
            Assert.AreEqual(1, bars.CompletedCount("setlist"));
            Assert.AreEqual(0.2 * 1.15 * 1.2, fans.RatePerSecond.ToDouble(), 1e-9, "the reset re-applies no rewards");
        }

        // state-then-notify: by the time any BarProgressChanged subscriber
        // runs, the whole run-scoped reset has settled — no half-reset group
        // is ever observable, and nothing completes
        [Test]
        public void ResetRunScopedGroups_StateSettlesBeforeNotifications()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeTwoScopeSetup(currencies, flags, out _);
            currencies.Add("rehearsal", 50);
            bars.SetActiveBar("learn_covers", "cover_1");
            bars.SetActiveBar("learn_covers", "cover_2");
            currencies.Add("rehearsal", 30);
            bars.Tick();

            var list = bars.GetBars("learn_covers");
            var notifications = 0;
            var observedPartialReset = false;
            bars.BarProgressChanged += _ =>
            {
                notifications++;
                if (list[0].Progress.ToDouble() != 0.0 || list[1].Progress.ToDouble() != 0.0
                    || bars.GetActiveBar("learn_covers") != null)
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

        // save/load: the snapshot re-establishes progress through the same
        // clamp-and-derive rule as accrual — a restored completion is
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
            Assert.AreEqual(0.2, fans.RatePerSecond.ToDouble(), 1e-9, "restore grants no rewards");

            // authoritative in both directions: below the requirement un-completes
            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber> { ["cover_1"] = 10 }));
            Assert.IsFalse(list[0].Completed);
            Assert.AreEqual(1, bars.CompletedCount("learn_covers"));

            // corrupt save data fails closed to an empty bar
            LogAssert.Expect(LogType.Error,
                "BarSystem: RestoreProgress with negative progress for bar 'cover_2'. Restoring an empty bar.");
            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber> { ["cover_2"] = -5 }));
            Assert.AreEqual(0.0, list[1].Progress.ToDouble(), 1e-9);
        }

        // the snapshot is atomic: by the time any subscriber runs, every
        // saved bar holds its final value and a selection left on a
        // now-completed bar is already cleared — Drain must never sit on a
        // completed target, and no subscriber may observe a half-restored
        // system
        [Test]
        public void RestoreProgress_SettlesTheWholeSnapshotBeforeNotifying()
        {
            var currencies = MakeEconomyWithRehearsal();
            var flags = new FlagSystem();
            var bars = MakeCoversSetup(currencies, flags, out _);
            bars.SetActiveBar("learn_covers", "cover_2");
            var list = bars.GetBars("learn_covers");

            var notifications = 0;
            var observedPartialRestore = false;
            bars.BarProgressChanged += _ =>
            {
                notifications++;
                if (list[0].Progress.ToDouble() != 120.0 || list[1].Progress.ToDouble() != 300.0
                    || bars.GetActiveBar("learn_covers") != null)
                    observedPartialRestore = true;
            };
            var activeChanges = 0;
            bars.ActiveBarChanged += _ => activeChanges++;

            bars.RestoreProgress(Snapshot("learn_covers", new Dictionary<string, BigNumber>
            {
                ["cover_1"] = 120,
                ["cover_2"] = 300, // the selected bar restores to complete
            }));

            Assert.AreEqual(2, notifications, "one progress notification per restored bar");
            Assert.IsFalse(observedPartialRestore, "every subscriber sees the whole snapshot settled");
            Assert.IsNull(bars.GetActiveBar("learn_covers"), "a completed bar can never stay the drain target");
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

            bars.SetActiveBar("learn_covers", "cover_2");
            Assert.IsNotNull(bars.GetActiveBar("learn_covers"));

            bars.SetActiveBar("learn_covers", null);
            Assert.IsNull(bars.GetActiveBar("learn_covers"));

            currencies.Add("rehearsal", 60);
            bars.Tick();
            Assert.AreEqual(60.0, currencies.Get("rehearsal").ToDouble(), 1e-9, "deselected = pool accumulates");
        }
    }
}
