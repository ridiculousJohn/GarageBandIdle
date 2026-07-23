using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RidiculousGaming.GarageBandIdle
{
    // Boot-time content validation (design doc section 12, rule 10): every id
    // referenced by a condition, payload, reward, module, or chapter list must
    // resolve to a loaded asset or a declared flag. Reports loudly and never
    // throws — a broken reference is a content bug to fix, not a crash.
    //
    // Content validates in its OWNING chapter's context: flags are declared
    // per chapter, so a flag reference is only meaningful against the
    // declaring chapter's list — never against whichever chapter happens to
    // be active. Rewards enter a chapter's closure through its bars and event
    // tiers. Definitions no chapter lists (stale imports, unreferenced pool
    // entries) still get every structural check; only the flag-known checks
    // are skipped, because no declaration list governs an orphan.
    public static class ContentValidator
    {
        public static void Validate(ContentDatabase database, ConditionContext context, RewardManager rewards)
        {
            var visited = new Visited();
            foreach (var chapter in database.Chapters.All)
                ValidateChapter(chapter, database, ChapterScoped(context, database, chapter), rewards, visited);

            var orphan = ChapterScoped(context, database, null);
            foreach (var currency in database.Currencies.All)
                if (!visited.Currencies.Contains(currency.Id))
                    ValidateCurrencyEarn(currency, orphan);
            foreach (var section in database.Sections.All)
                if (!visited.Sections.Contains(section.Id))
                    ValidateSection(section, orphan);
            foreach (var generator in database.Generators.All)
                if (!visited.Generators.Contains(generator.Id))
                    ValidateGenerator(generator, orphan);
            foreach (var upgrade in database.Upgrades.All)
                if (!visited.Upgrades.Contains(upgrade.Id))
                    ValidateUpgrade(upgrade, orphan);
            foreach (var group in database.BarGroups.All)
                if (!visited.BarGroups.Contains(group.Id))
                    ValidateBarGroup(group, database, orphan);
            foreach (var bar in database.Bars.All)
                if (!visited.Bars.Contains(bar.Id))
                    ValidateBar(bar, orphan, rewards);
            foreach (var gameEvent in database.Events.All)
                if (!visited.Events.Contains(gameEvent.Id))
                    ValidateEvent(gameEvent, orphan, rewards);
            foreach (var reward in database.Rewards.All)
                if (!visited.Rewards.Contains(reward.Id))
                    ValidateRewardDefinition(reward, orphan);
        }

        private static void ValidateChapter(ChapterDefinition chapter, ContentDatabase database,
            ConditionContext context, RewardManager rewards, Visited visited)
        {
            foreach (var currencyId in chapter.RecordBuff.AffectsCurrencyIds)
                context.Currencies.ValidateReference(currencyId, $"Chapter '{chapter.Id}' (recordBuff affects)");
            context.Currencies.ValidateReference(chapter.Fans.CurrencyId, $"Chapter '{chapter.Id}' (fans currency)");
            ValidateFlag(chapter.Fans.RevealFlagId, context, $"Chapter '{chapter.Id}' (fans revealFlag)");

            // negative tuning drains or dead-ends instead of earning; runtime
            // fails closed on all of it (guarded ticks, zeroed tap), so
            // without these reports the systems would just look mysteriously
            // dead
            if (chapter.Fans.BaseFansPerSec < 0 || chapter.Fans.PerBandmateOwnedBonus < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has negative fan earn values.");
            if (chapter.TapBaseValue < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a negative tapBaseValue ({chapter.TapBaseValue}) — every Jam would drain cash.");
            if (chapter.RecordBuff.PerRecord < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a negative recordBuff perRecord ({chapter.RecordBuff.PerRecord}).");

            ValidateIds(chapter.CurrencyIds, database.Currencies, $"Chapter '{chapter.Id}' (currencies)");
            // the chapter's declared currencies: their earn reveal flags are
            // chapter-scoped like every other flag reference — flag ids may
            // repeat across chapters, so the owning chapter's list is the
            // only one that counts
            foreach (var id in chapter.CurrencyIds)
            {
                if (!database.Currencies.TryGet(id, out var currency))
                    continue;
                visited.Currencies.Add(id);
                ValidateCurrencyEarn(currency, context);
            }

            ValidateIds(chapter.SectionIds, database.Sections, $"Chapter '{chapter.Id}' (sections)");
            ValidateIds(chapter.GeneratorIds, database.Generators, $"Chapter '{chapter.Id}' (generators)");
            ValidateIds(chapter.UpgradeIds, database.Upgrades, $"Chapter '{chapter.Id}' (upgrades)");
            ValidateIds(chapter.BarGroupIds, database.BarGroups, $"Chapter '{chapter.Id}' (barGroups)");
            ValidateIds(chapter.EventIds, database.Events, $"Chapter '{chapter.Id}' (events)");

            foreach (var id in chapter.SectionIds)
            {
                if (!database.Sections.TryGet(id, out var section))
                    continue;
                visited.Sections.Add(id);
                ValidateSection(section, context);
            }

            foreach (var id in chapter.GeneratorIds)
            {
                if (!database.Generators.TryGet(id, out var generator))
                    continue;
                visited.Generators.Add(id);
                ValidateGenerator(generator, context);
            }

            foreach (var id in chapter.UpgradeIds)
            {
                if (!database.Upgrades.TryGet(id, out var upgrade))
                    continue;
                visited.Upgrades.Add(id);
                ValidateUpgrade(upgrade, context);
            }

            // rewards enter the closure through bars and event tiers; collect
            // ids first so a reward two bars share validates once per chapter
            var rewardIds = new HashSet<string>();

            foreach (var id in chapter.BarGroupIds)
            {
                if (!database.BarGroups.TryGet(id, out var group))
                    continue;
                visited.BarGroups.Add(id);
                ValidateBarGroup(group, database, context);

                foreach (var barId in group.BarIds)
                {
                    if (!database.Bars.TryGet(barId, out var bar))
                        continue;
                    visited.Bars.Add(barId);
                    ValidateBar(bar, context, rewards);
                    if (!string.IsNullOrEmpty(bar.RewardId))
                        rewardIds.Add(bar.RewardId);
                }
            }

            foreach (var id in chapter.EventIds)
            {
                if (!database.Events.TryGet(id, out var gameEvent))
                    continue;
                visited.Events.Add(id);
                ValidateEvent(gameEvent, context, rewards);
                foreach (var tier in gameEvent.Tiers)
                {
                    if (!string.IsNullOrEmpty(tier.RewardId))
                        rewardIds.Add(tier.RewardId);
                }
            }

            foreach (var rewardId in rewardIds)
            {
                // an unknown id is reported against the bar/tier that names it
                if (!database.Rewards.TryGet(rewardId, out var reward))
                    continue;
                visited.Rewards.Add(rewardId);
                ValidateRewardDefinition(reward, context);
            }
        }

        // negative earn drains instead of earns, and earn values with no
        // reveal flag can never activate (the importer refuses both; this
        // catches stale assets)
        private static void ValidateCurrencyEarn(CurrencyDefinition currency, ConditionContext context)
        {
            if (!currency.Earn.Configured)
                return;

            if (currency.Earn.PerSec < 0 || currency.Earn.PerTap < 0)
                Debug.LogError($"ContentValidator: Currency '{currency.Id}' has negative earn values.");
            if (string.IsNullOrEmpty(currency.Earn.RevealFlagId))
                Debug.LogError($"ContentValidator: Currency '{currency.Id}' has earn values but no reveal flag — the earn can never activate.");
            else
                ValidateFlag(currency.Earn.RevealFlagId, context, $"Currency '{currency.Id}' (earn revealFlag)");
        }

        private static void ValidateSection(SectionDefinition section, ConditionContext context)
        {
            ConditionEvaluator.Validate(section.VisibleWhen, context, $"Section '{section.Id}' (visibleWhen)");
            foreach (var address in section.ModuleAddresses)
                ValidateModuleAddress(address, $"Section '{section.Id}'");
        }

        private static void ValidateGenerator(GeneratorDefinition generator, ConditionContext context)
        {
            // a zero/negative cost makes a generator free-and-infinite and a
            // non-positive growth breaks the cost curve — content mistakes
            // (including stale assets from before the cost schema) must fail
            // loudly here, not degrade to wrong gameplay. Growth < 1
            // (shrinking costs) is legal.
            context.Currencies.ValidateReference(generator.CostCurrencyId, $"Generator '{generator.Id}' (cost currency)");
            if (generator.BaseCost <= 0)
                Debug.LogError($"ContentValidator: Generator '{generator.Id}' has a non-positive base cost ({generator.BaseCost}) — it would be free to buy.");
            if (generator.CostGrowth <= 0)
                Debug.LogError($"ContentValidator: Generator '{generator.Id}' has a non-positive cost growth ({generator.CostGrowth}).");
            // production must never drain (runtime fails closed on it);
            // zero output stays legal — a pure fan-rate bandmate is coherent
            if (generator.BaseOutput < 0)
                Debug.LogError($"ContentValidator: Generator '{generator.Id}' has a negative base output ({generator.BaseOutput}).");
            ConditionEvaluator.Validate(generator.Unlock, context, $"Generator '{generator.Id}' (unlock)");
        }

        private static void ValidateUpgrade(UpgradeDefinition upgrade, ConditionContext context)
        {
            if (upgrade.Type == UpgradeType.None)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has type None (uninitialized).");
            if (upgrade.Scope == ContentScope.None)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has scope None (uninitialized).");
            // a negative cost would GRANT currency when the buff purchase
            // flow lands; close it before that flow exists
            if (upgrade.CostAmount < 0)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has a negative cost amount ({upgrade.CostAmount}).");
            ConditionEvaluator.Validate(upgrade.Gate, context, $"Upgrade '{upgrade.Id}' (gate)");
            if (upgrade.Payload == null)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has no payload.");
            else
                upgrade.Payload.Validate(context, $"Upgrade '{upgrade.Id}' (payload)");
        }

        private static void ValidateBarGroup(BarGroupDefinition group, ContentDatabase database, ConditionContext context)
        {
            if (group.FillMode == BarFillMode.None)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has fill mode None (uninitialized).");
            if (group.Delivery == BarFillDelivery.None)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has delivery None (uninitialized).");
            if (group.Scope == ContentScope.None)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has scope None (uninitialized).");
            ValidateFlag(group.RevealFlagId, context, $"Bar group '{group.Id}' (revealFlag)");
            ValidateIds(group.BarIds, database.Bars, $"Bar group '{group.Id}' (bars)");
        }

        private static void ValidateBar(BarDefinition bar, ConditionContext context, RewardManager rewards)
        {
            context.Currencies.ValidateReference(bar.FillCurrencyId, $"Bar '{bar.Id}' (fillCurrency)");
            ValidateRewardReference(bar.RewardId, rewards, $"Bar '{bar.Id}'");

            // a non-positive requirement can never be legitimately filled;
            // BarSystem rejects such bars — report the content error here
            // (catches stale assets from before this rule)
            if (bar.FillRequirement <= 0)
                Debug.LogError($"ContentValidator: Bar '{bar.Id}' has a non-positive fill requirement ({bar.FillRequirement}).");
        }

        private static void ValidateEvent(EventDefinition gameEvent, ConditionContext context, RewardManager rewards)
        {
            ConditionEvaluator.Validate(gameEvent.AvailableWhen, context, $"Event '{gameEvent.Id}' (availableWhen)");
            foreach (var tier in gameEvent.Tiers)
            {
                ConditionEvaluator.Validate(tier.Goal, context, $"Event '{gameEvent.Id}' tier {tier.Tier} (goal)");
                ValidateRewardReference(tier.RewardId, rewards, $"Event '{gameEvent.Id}' tier {tier.Tier}");
            }
        }

        // rewards can reveal content too; their flags run through the same registry
        private static void ValidateRewardDefinition(RewardDefinition reward, ConditionContext context)
        {
            if (reward is SetFlagReward setFlag)
                ValidateFlag(setFlag.FlagId, context, $"Reward '{reward.Id}' (setFlag)");
            var scope = reward switch
            {
                FanRateMultiplierReward fanRate => fanRate.Scope,
                TapValueMultiplierReward tapValue => tapValue.Scope,
                _ => (ContentScope?)null,
            };
            if (scope == ContentScope.None)
                Debug.LogError($"ContentValidator: Reward '{reward.Id}' has scope None (uninitialized).");

            // a non-positive multiplier would zero or negate its whole
            // multiplicative stack (runtime fails closed on it)
            var multiplier = reward switch
            {
                FanRateMultiplierReward fanRate => (double?)fanRate.Value,
                TapValueMultiplierReward tapValue => tapValue.Value,
                _ => null,
            };
            if (multiplier <= 0)
                Debug.LogError($"ContentValidator: Reward '{reward.Id}' has a non-positive multiplier ({multiplier}).");
        }

        // conditions and payloads resolve content ids through the database and
        // flag ids through the declaring chapter's list; the orphan pass (null
        // chapter) gets an unrestricted FlagSystem, so flag-known checks pass
        // instead of false-positive against an arbitrary chapter
        private static ConditionContext ChapterScoped(ConditionContext context, ContentDatabase database, ChapterDefinition chapter)
            => new(context.Currencies, context.Generators,
                chapter != null ? new FlagSystem(chapter.FlagIds) : new FlagSystem(),
                context.RecordsCurrencyId, database, context.Bars);

        // which definitions some chapter's closure validated, so the orphan
        // pass covers exactly the rest
        private class Visited
        {
            public readonly HashSet<string> Currencies = new();
            public readonly HashSet<string> Sections = new();
            public readonly HashSet<string> Generators = new();
            public readonly HashSet<string> Upgrades = new();
            public readonly HashSet<string> BarGroups = new();
            public readonly HashSet<string> Bars = new();
            public readonly HashSet<string> Events = new();
            public readonly HashSet<string> Rewards = new();
        }

        private static void ValidateIds<T>(IReadOnlyList<string> ids, ContentDatabase.Registry<T> registry, string source)
            where T : ScriptableObject
        {
            foreach (var id in ids)
            {
                if (!registry.Contains(id))
                    Debug.LogError($"ContentValidator: {source} references unknown {typeof(T).Name} id '{id}'.");
            }
        }

        private static void ValidateFlag(string flagId, ConditionContext context, string source)
        {
            if (string.IsNullOrEmpty(flagId))
                Debug.LogError($"ContentValidator: {source} has an empty flag id.");
            else if (context.Flags != null && !context.Flags.IsKnown(flagId))
                Debug.LogError($"ContentValidator: {source} references flag '{flagId}', which the chapter does not declare.");
        }

        private static void ValidateRewardReference(string rewardId, RewardManager rewards, string source)
        {
            if (string.IsNullOrEmpty(rewardId))
                return; // no reward is legal content

            if (!rewards.Contains(rewardId))
                Debug.LogError($"ContentValidator: {source} references unknown reward id '{rewardId}'.");
        }

        // a module address must resolve to at least one addressable location, or
        // the section will fail to instantiate it at reveal time
        private static void ValidateModuleAddress(string address, string source)
        {
            var locations = Addressables.LoadResourceLocationsAsync(address, typeof(GameObject)).WaitForCompletion();
            if (locations == null || locations.Count == 0)
                Debug.LogError($"ContentValidator: {source} references module address '{address}', which resolves to no addressable prefab.");
        }
    }
}
