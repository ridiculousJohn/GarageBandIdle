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
    // throws - a broken reference is a content bug to fix, not a crash.
    //
    // Content validates in its OWNING chapter's context: flags are declared
    // per chapter, so a flag reference is only meaningful against the
    // declaring chapter's list - never against whichever chapter happens to
    // be active. Rewards enter a chapter's closure through its bars and event
    // tiers. Definitions no chapter lists (stale imports, unreferenced pool
    // entries) still get every structural check; only the flag-known checks
    // are skipped, because no declaration list governs an orphan.
    public static class ContentValidator
    {
        public static void Validate(ContentDatabase database, ConditionContext context, RewardManager rewards)
        {
            ValidateRecordsSurviveRelease(context);
            ValidateChapterIndices(database);

            var visited = new Visited();
            foreach (var chapter in database.Chapters.All)
                ValidateChapter(chapter, database, ChapterScoped(context, database, chapter), rewards, visited);

            var orphan = ChapterScoped(context, database, null);
            foreach (var currency in database.Currencies.All)
                if (!visited.Currencies.Contains(currency.Id))
                    ValidateCurrency(currency, orphan);
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

        // Records are the one permanent progression currency: the income buff and
        // every capstone gate read their cumulative total, and the balance is what
        // the player sees as permanent progress. Filing Records in a group that
        // resets on release makes those two disagree - the readout returns to zero
        // every album while the progression it stands for carries on. The whole
        // "derived modifiers carry no scope" argument (rule 11) rests on this group
        // flag, and nothing else checks that the asset was filed correctly.
        private static void ValidateRecordsSurviveRelease(ConditionContext context)
        {
            if (context.Currencies.ResetsOnAlbumRelease(context.RecordsCurrencyId))
                Debug.LogError($"ContentValidator: Records currency '{context.RecordsCurrencyId}' is in a currency group that resets on album release - permanent progress would return to zero every release.");
        }

        // The starting chapter is the lowest index (GameManager) and advancement
        // walks them in order, so an index is an ordinal, not a label: two chapters
        // sharing one make which of them starts arbitrary.
        private static void ValidateChapterIndices(ContentDatabase database)
        {
            var byIndex = new Dictionary<int, string>();
            foreach (var chapter in database.Chapters.All)
            {
                if (chapter.Index <= 0)
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a non-positive index ({chapter.Index}) - chapter order is 1-based.");
                else if (byIndex.TryGetValue(chapter.Index, out var existing))
                    Debug.LogError($"ContentValidator: Chapters '{existing}' and '{chapter.Id}' share index {chapter.Index} - which one starts would be arbitrary.");
                else
                    byIndex.Add(chapter.Index, chapter.Id);
            }
        }

        private static void ValidateChapter(ChapterDefinition chapter, ContentDatabase database,
            ConditionContext context, RewardManager rewards, Visited visited)
        {
            foreach (var currencyId in chapter.RecordBuff.AffectsCurrencyIds)
                context.Currencies.ValidateReference(currencyId, $"Chapter '{chapter.Id}' (recordBuff affects)");
            // Fans are the run's performance meter and the Records payout reads
            // fansThisRun, so they have to return to zero on release. Filed in a
            // group that keeps them, fans compound across runs: the payout inflates
            // and every fans gate stays satisfied after the first release, which
            // still plays, just far too easily. Only asked when the id resolves, so
            // a bad reference reports once rather than twice.
            if (context.Currencies.ValidateReference(chapter.Fans.CurrencyId, $"Chapter '{chapter.Id}' (fans currency)")
                && !context.Currencies.ResetsOnAlbumRelease(chapter.Fans.CurrencyId))
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' fans currency '{chapter.Fans.CurrencyId}' is in a currency group that survives an album release - fans would compound across runs and inflate the Records payout.");
            ValidateFlag(chapter.Fans.RevealFlagId, context, $"Chapter '{chapter.Id}' (fans revealFlag)");

            // negative tuning drains or dead-ends instead of earning; runtime
            // fails closed on all of it (guarded ticks, zeroed tap), so
            // without these reports the systems would just look mysteriously
            // dead
            if (chapter.Fans.BaseFansPerSec < 0 || chapter.Fans.PerBandmateOwnedBonus < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has negative fan earn values.");
            if (chapter.TapBaseValue < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a negative tapBaseValue ({chapter.TapBaseValue}) - every Jam would drain cash.");
            if (chapter.RecordBuff.PerRecord < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a negative recordBuff perRecord ({chapter.RecordBuff.PerRecord}).");
            // the primary pacing knob (design doc section 11): at zero the
            // capstone is reachable before the player has released anything, so
            // the chapter has no length at all
            if (chapter.CapstoneRecordsGate <= 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a non-positive capstoneRecordsGate ({chapter.CapstoneRecordsGate}) - the capstone would unlock before play starts.");

            ValidateFlagDeclarations(chapter);
            ValidateIds(chapter.CurrencyIds, database.Currencies, $"Chapter '{chapter.Id}' (currencies)");
            // the chapter's declared currencies: their earn reveal flags are
            // chapter-scoped like every other flag reference - flag ids may
            // repeat across chapters, so the owning chapter's list is the
            // only one that counts
            foreach (var id in chapter.CurrencyIds)
            {
                if (!database.Currencies.TryGet(id, out var currency))
                    continue;
                visited.Currencies.Add(id);
                ValidateCurrency(currency, context);
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

        // The declared flag list is the chapter's whole reveal vocabulary - every
        // flag check anywhere is measured against it - so a blank entry declares
        // nothing and a repeat says the same thing twice, both of which read as
        // an authoring slip in the JSON flags array.
        private static void ValidateFlagDeclarations(ChapterDefinition chapter)
        {
            var declared = new HashSet<string>();
            foreach (var flagId in chapter.FlagIds)
            {
                if (string.IsNullOrEmpty(flagId))
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' declares an empty flag id.");
                else if (!declared.Add(flagId))
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' declares flag '{flagId}' more than once.");
            }
        }

        // Every currency gets the checks that do not depend on an earn config; an
        // earn-less currency (Cash, Fans, Records) previously got none at all.
        private static void ValidateCurrency(CurrencyDefinition currency, ConditionContext context)
        {
            // a negative starting value puts the currency in debt at boot and again
            // after every album release, which resets balances back to it
            if (currency.StartingValue < 0)
                Debug.LogError($"ContentValidator: Currency '{currency.Id}' has a negative starting value ({currency.StartingValue}) - it would start in debt at boot and after every album release.");

            // negative earn drains instead of earns, and earn values with no
            // reveal flag can never activate (the importer refuses both; this
            // catches stale assets)
            if (!currency.Earn.Configured)
                return;

            if (currency.Earn.PerSec < 0 || currency.Earn.PerTap < 0)
                Debug.LogError($"ContentValidator: Currency '{currency.Id}' has negative earn values.");
            if (string.IsNullOrEmpty(currency.Earn.RevealFlagId))
                Debug.LogError($"ContentValidator: Currency '{currency.Id}' has earn values but no reveal flag - the earn can never activate.");
            else
                ValidateFlag(currency.Earn.RevealFlagId, context, $"Currency '{currency.Id}' (earn revealFlag)");
        }

        private static void ValidateSection(SectionDefinition section, ConditionContext context)
        {
            ConditionEvaluator.Validate(section.VisibleWhen, context, $"Section '{section.Id}' (visibleWhen)");
            // a section IS its modules: with none, its reveal shows an empty region.
            // The importer reports it too but writes the asset anyway (an empty
            // region is inert, not wrong), so this is the check that sees it in
            // loaded content - freshly imported or hand-edited alike.
            if (section.ModuleAddresses.Count == 0)
                Debug.LogError($"ContentValidator: Section '{section.Id}' has no modules - its reveal would show an empty region.");

            // the list is instantiated in order with no de-duplication, so a repeat
            // puts two of the same module in the region, each wired to the same
            // systems - a doubled readout rather than an error anyone would trace
            var seen = new HashSet<string>();
            foreach (var address in section.ModuleAddresses)
            {
                if (!seen.Add(address))
                    Debug.LogError($"ContentValidator: Section '{section.Id}' lists module '{address}' more than once - it would be instantiated twice.");
                ValidateModuleAddress(address, $"Section '{section.Id}'");
            }
        }

        private static void ValidateGenerator(GeneratorDefinition generator, ConditionContext context)
        {
            // a zero/negative cost makes a generator free-and-infinite and a
            // non-positive growth breaks the cost curve - content mistakes
            // (including stale assets from before the cost schema) must fail
            // loudly here, not degrade to wrong gameplay. Growth < 1
            // (shrinking costs) is legal.
            context.Currencies.ValidateReference(generator.CostCurrencyId, $"Generator '{generator.Id}' (cost currency)");
            if (generator.BaseCost <= 0)
                Debug.LogError($"ContentValidator: Generator '{generator.Id}' has a non-positive base cost ({generator.BaseCost}) - it would be free to buy.");
            if (generator.CostGrowth <= 0)
                Debug.LogError($"ContentValidator: Generator '{generator.Id}' has a non-positive cost growth ({generator.CostGrowth}).");
            // production must never drain (runtime fails closed on it);
            // zero output stays legal - a pure fan-rate bandmate is coherent
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
            // a negative cost would GRANT currency when the buff purchase flow
            // lands, and a buff costing nothing would be an endless free
            // purchase - the same failure class as a non-positive generator
            // cost. A content unlock legitimately costs nothing: its gate is
            // the price. Both close before that flow exists.
            if (upgrade.CostAmount < 0)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has a negative cost amount ({upgrade.CostAmount}).");
            else if (upgrade.Type == UpgradeType.Buff && upgrade.CostAmount == 0)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' is a buff with no cost - it would be free to buy.");

            // an amount with no currency to charge is free in practice, which is
            // the same hole from the other side; a currency it does name resolves
            // through the check every other currency id goes through. A content
            // unlock charges nothing, so it needs no currency at all.
            if (upgrade.CostAmount > 0 && string.IsNullOrEmpty(upgrade.CostCurrencyId))
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' costs {upgrade.CostAmount} but names no cost currency - the purchase would charge nothing.");
            else if (!string.IsNullOrEmpty(upgrade.CostCurrencyId))
                context.Currencies.ValidateReference(upgrade.CostCurrencyId, $"Upgrade '{upgrade.Id}' (cost currency)");
            ConditionEvaluator.Validate(upgrade.Gate, context, $"Upgrade '{upgrade.Id}' (gate)");
            if (upgrade.Payload == null)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has no payload.");
            else
                upgrade.Payload.Validate(context, $"Upgrade '{upgrade.Id}' (payload)");
        }

        private static void ValidateBarGroup(BarGroupDefinition group, ContentDatabase database, ConditionContext context)
        {
            if (group.FillBehavior == null)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has no fill behavior.");
            else
                group.FillBehavior.Validate(context, $"Bar group '{group.Id}' (fillBehavior)");
            if (group.Scope == ContentScope.None)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has scope None (uninitialized).");
            ValidateFlag(group.RevealFlagId, context, $"Bar group '{group.Id}' (revealFlag)");
            // a group with no bars reveals an empty region and can never satisfy
            // a barsCompleted gate, so anything waiting on it waits forever
            if (group.BarIds.Count == 0)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has no bars - it can never complete one.");
            ValidateIds(group.BarIds, database.Bars, $"Bar group '{group.Id}' (bars)");
        }

        private static void ValidateBar(BarDefinition bar, ConditionContext context, RewardManager rewards)
        {
            context.Currencies.ValidateReference(bar.FillCurrencyId, $"Bar '{bar.Id}' (fillCurrency)");
            ValidateRewardReference(bar.RewardId, rewards, $"Bar '{bar.Id}'");

            // a non-positive requirement can never be legitimately filled;
            // BarSystem rejects such bars - report the content error here
            // (catches stale assets from before this rule)
            if (bar.FillRequirement <= 0)
                Debug.LogError($"ContentValidator: Bar '{bar.Id}' has a non-positive fill requirement ({bar.FillRequirement}).");
        }

        // An event's tier ladder is where the design's rules about events live as
        // data (design doc section 6.1), and slice 8's runtime will trust every
        // field here: a tier that cannot be failed, cannot be won, or pays nothing
        // is a content mistake that reads as working content.
        private static void ValidateEvent(EventDefinition gameEvent, ConditionContext context, RewardManager rewards)
        {
            ConditionEvaluator.Validate(gameEvent.AvailableWhen, context, $"Event '{gameEvent.Id}' (availableWhen)");

            if (gameEvent.Tiers.Count == 0)
                Debug.LogError($"ContentValidator: Event '{gameEvent.Id}' has no tiers - there would be nothing to enter.");

            // tier numbers are the ladder, so they ascend with list order and
            // start at 1; a repeat or a step backwards makes one number name two
            // rungs, which no save or reward record could tell apart
            var previous = 0;
            foreach (var tier in gameEvent.Tiers)
            {
                var source = $"Event '{gameEvent.Id}' tier {tier.Tier}";
                ConditionEvaluator.Validate(tier.Goal, context, $"{source} (goal)");
                ValidateRewardReference(tier.RewardId, rewards, source);

                // a null Condition means "no gate", which for a goal means the
                // tier is won the moment it is entered
                if (tier.Goal == null)
                    Debug.LogError($"ContentValidator: {source} has no goal - the tier would be won on entry.");

                // an event's reward magnitude is the dial that sets how essential
                // it is; no reward sets that dial to nothing, which is never what
                // authoring a challenge means (a bar may legitimately have none)
                if (string.IsNullOrEmpty(tier.RewardId))
                    Debug.LogError($"ContentValidator: {source} has no reward - clearing it would grant nothing.");

                // the tier's scope is how long its own clear state lasts; whatever it
                // pays projects from that clear and inherits the durability (design
                // doc rule 11), and an unscoped grant has no reset path at all, so
                // ModifierSystem would refuse it at runtime
                if (tier.Scope == ContentScope.None)
                    Debug.LogError($"ContentValidator: {source} has scope None (uninitialized) - a tier clear needs a declared lifetime for anything to project from.");

                // only timed tiers can fail, which cuts both ways: a failable tier
                // with no timer has no way to fail, and a timer on a tier that
                // cannot fail runs a clock with nothing riding on it
                if (tier.Failable && tier.TimerSeconds <= 0)
                    Debug.LogError($"ContentValidator: {source} is failable but has no timer ({tier.TimerSeconds}s) - only timed tiers can fail.");
                else if (!tier.Failable && tier.TimerSeconds > 0)
                    Debug.LogError($"ContentValidator: {source} has a {tier.TimerSeconds}s timer but is not failable - the timer could never end the tier.");

                if (tier.Tier <= previous)
                    Debug.LogError($"ContentValidator: Event '{gameEvent.Id}' has tier number {tier.Tier} following {previous} - tier numbers ascend with list order, starting at 1.");
                else
                    previous = tier.Tier;
            }
        }

        // A reward's effect validates itself - the same call an upgrade's payload
        // gets, so a new effect kind is one class and never an edit here. This used
        // to be a downcast chain over reward subclasses, which meant a kind nobody
        // added a case for was silently unvalidated.
        //
        // No scope check: a reward carries no lifetime of its own. Whatever applies
        // it declares one (a bar group, an event tier), and those are checked where
        // they are declared.
        private static void ValidateRewardDefinition(RewardDefinition reward, ConditionContext context)
        {
            if (reward.Effect == null)
                Debug.LogError($"ContentValidator: Reward '{reward.Id}' has no effect - applying it would grant nothing.");
            else
                reward.Effect.Validate(context, $"Reward '{reward.Id}' (effect)");
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
