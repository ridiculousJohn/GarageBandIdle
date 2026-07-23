using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RidiculousGaming.GarageBandIdle
{
    // Boot-time content validation (design doc section 12, rule 10): every id
    // referenced by a condition, payload, reward, module, or chapter list must
    // resolve to a loaded asset or a declared flag. Reports loudly and never
    // throws — a broken reference is a content bug to fix, not a crash.
    public static class ContentValidator
    {
        public static void Validate(ContentDatabase database, ConditionContext context, RewardManager rewards)
        {
            foreach (var chapter in database.Chapters.All)
            {
                foreach (var currencyId in chapter.RecordBuff.AffectsCurrencyIds)
                    context.Currencies.ValidateReference(currencyId, $"Chapter '{chapter.Id}' (recordBuff affects)");
                context.Currencies.ValidateReference(chapter.Fans.CurrencyId, $"Chapter '{chapter.Id}' (fans currency)");
                ValidateFlag(chapter.Fans.RevealFlagId, context, $"Chapter '{chapter.Id}' (fans revealFlag)");

                // a chapter without a fill currency is legal; one that declares it
                // must resolve both the currency and its activation flag
                if (!string.IsNullOrEmpty(chapter.Rehearsal.CurrencyId))
                {
                    context.Currencies.ValidateReference(chapter.Rehearsal.CurrencyId, $"Chapter '{chapter.Id}' (rehearsal currency)");
                    ValidateFlag(chapter.Rehearsal.RevealFlagId, context, $"Chapter '{chapter.Id}' (rehearsal revealFlag)");
                }
                ValidateIds(chapter.SectionIds, database.Sections, $"Chapter '{chapter.Id}' (sections)");
                ValidateIds(chapter.GeneratorIds, database.Generators, $"Chapter '{chapter.Id}' (generators)");
                ValidateIds(chapter.UpgradeIds, database.Upgrades, $"Chapter '{chapter.Id}' (upgrades)");
                ValidateIds(chapter.BarGroupIds, database.BarGroups, $"Chapter '{chapter.Id}' (barGroups)");
                ValidateIds(chapter.EventIds, database.Events, $"Chapter '{chapter.Id}' (events)");
            }

            foreach (var section in database.Sections.All)
            {
                ConditionEvaluator.Validate(section.VisibleWhen, context, $"Section '{section.Id}' (visibleWhen)");
                foreach (var address in section.ModuleAddresses)
                    ValidateModuleAddress(address, $"Section '{section.Id}'");
            }

            foreach (var generator in database.Generators.All)
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
                ConditionEvaluator.Validate(generator.Unlock, context, $"Generator '{generator.Id}' (unlock)");
            }

            foreach (var upgrade in database.Upgrades.All)
            {
                if (upgrade.Type == UpgradeType.None)
                    Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has type None (uninitialized).");
                if (upgrade.Scope == ContentScope.None)
                    Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has scope None (uninitialized).");
                ConditionEvaluator.Validate(upgrade.Gate, context, $"Upgrade '{upgrade.Id}' (gate)");
                if (upgrade.Payload == null)
                    Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has no payload.");
                else
                    upgrade.Payload.Validate(context, $"Upgrade '{upgrade.Id}' (payload)");
            }

            foreach (var group in database.BarGroups.All)
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

            foreach (var bar in database.Bars.All)
            {
                context.Currencies.ValidateReference(bar.FillCurrencyId, $"Bar '{bar.Id}' (fillCurrency)");
                ValidateReward(bar.RewardId, rewards, $"Bar '{bar.Id}'");
            }

            foreach (var gameEvent in database.Events.All)
            {
                ConditionEvaluator.Validate(gameEvent.AvailableWhen, context, $"Event '{gameEvent.Id}' (availableWhen)");
                foreach (var tier in gameEvent.Tiers)
                {
                    ConditionEvaluator.Validate(tier.Goal, context, $"Event '{gameEvent.Id}' tier {tier.Tier} (goal)");
                    ValidateReward(tier.RewardId, rewards, $"Event '{gameEvent.Id}' tier {tier.Tier}");
                }
            }

            // rewards can reveal content too; their flags run through the same registry
            foreach (var reward in database.Rewards.All)
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
            }
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
                Debug.LogError($"ContentValidator: {source} references flag '{flagId}', which no chapter declares.");
        }

        private static void ValidateReward(string rewardId, RewardManager rewards, string source)
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
