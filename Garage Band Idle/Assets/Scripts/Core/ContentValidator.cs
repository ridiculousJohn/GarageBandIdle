using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Content;
using RidiculousGaming.GarageBandIdle.Economy;
using RidiculousGaming.GarageBandIdle.Events;
using RidiculousGaming.GarageBandIdle.Loop;
using RidiculousGaming.GarageBandIdle.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
        public static void Validate(ContentDatabase database, string recordsCurrencyId, RewardManager rewards)
        {
            var orphan = ChapterScoped(new ChapterCurrencies(database, null), recordsCurrencyId, database, null);

            ValidateRecordsSurviveRelease(orphan);
            ValidateChapterIndices(database);
            ValidateCurrencyPlacement(database);

            var visited = new Visited();
            foreach (var chapter in database.Chapters.All)
            {
                // the roster is checked per chapter here rather than only when an
                // economy is constructed from it - construction happens for the
                // frontier chapter alone, which left every later chapter's roster
                // unexamined until the player reached it
                var currencies = new ChapterCurrencies(database, chapter);
                currencies.ValidateRoster();
                ValidateChapter(chapter, database,
                    ChapterScoped(currencies, recordsCurrencyId, database, chapter), rewards, visited);
            }

            foreach (var currency in database.Currencies.All)
                if (!visited.Currencies.Contains(currency.Id))
                    ValidateCurrency(currency, orphan);
            foreach (var producer in database.Producers.All)
                if (!visited.Producers.Contains(producer.Id))
                    ValidateProducer(producer, orphan);
            foreach (var section in database.Sections.All)
                if (!visited.Sections.Contains(section.Id))
                    ValidateSection(section, orphan, null, database, null);
            foreach (var generator in database.Generators.All)
                if (!visited.Generators.Contains(generator.Id))
                    ValidateGenerator(generator, orphan);
            foreach (var upgrade in database.Upgrades.All)
                if (!visited.Upgrades.Contains(upgrade.Id))
                    ValidateUpgrade(upgrade, orphan);
            foreach (var group in database.BarGroups.All)
                if (!visited.BarGroups.Contains(group.Id))
                    ValidateBarGroup(group, database, orphan, rewards);
            foreach (var bar in database.Bars.All)
                if (!visited.Bars.Contains(bar.Id))
                    ValidateBar(bar, orphan, rewards);
            foreach (var gameEvent in database.Events.All)
                if (!visited.Events.Contains(gameEvent.Id))
                    ValidateEvent(gameEvent, orphan, rewards);
            foreach (var reward in database.Rewards.All)
                if (!visited.Rewards.Contains(reward.Id))
                    ValidateRewardDefinition(reward, orphan);
            foreach (var beat in database.StoryBeats.All)
                if (!visited.StoryBeats.Contains(beat.Id))
                    ValidateStoryBeat(beat, orphan, null);
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

        // Placement decides which pool holds a group's balances (design doc
        // section 12, rule 12), and this is the ONLY enforcement point for it:
        // currency group assets are hand-authored, never generated from the
        // chapter JSON, so there is no import step to refuse a bad combination
        // at. Both checks below describe assets that already exist rather than
        // JSON someone is about to write.
        //
        // None is the un-migrated field: a group added before placement existed,
        // or created by hand and left at the default. Its currencies would land
        // in no pool at all - not in the chapter's roster, since a global check
        // would not match, and not in the startup pool either - so every balance
        // in the group silently reads zero.
        //
        // Global + resetsOnAlbumRelease has no coherent reading: "resets on whose
        // release?" A global currency is held by a pool no release touches, so
        // one of the two declarations is a mistake and there is no way to tell
        // which. Reported rather than resolved by precedence, because guessing
        // would either wipe permanent progress or silently keep a run currency.
        private static void ValidateCurrencyPlacement(ContentDatabase database)
        {
            foreach (var group in database.CurrencyGroups.All)
            {
                if (group.Placement == CurrencyPlacement.None)
                    Debug.LogError($"ContentValidator: currency group '{group.Id}' has no placement set (None) - its currencies would land in no pool and every balance would read zero. Set it to Chapter or Global.");
                else if (group.Placement == CurrencyPlacement.Global && group.ResetsOnAlbumRelease)
                    Debug.LogError($"ContentValidator: currency group '{group.Id}' is placed Global and also resets on album release - a global currency is held by a pool no release touches, so the two cannot both be true.");
            }

            // Both of a currency's lifetime facts come from its group - which pool
            // holds it, and whether a release resets it - so a group reference
            // that resolves to nothing leaves both unanswered: it lands in no
            // pool by placement and reads as surviving every release. CurrencyManager
            // reports this too, but only for currencies it was constructed with,
            // which is the frontier chapter's pool and the permanent one.
            foreach (var currency in database.Currencies.All)
            {
                if (!string.IsNullOrEmpty(currency.Id) && !database.CurrencyGroups.Contains(currency.GroupId))
                    Debug.LogError($"ContentValidator: currency '{currency.Id}' references unknown group id '{currency.GroupId}' - placement and the album-release reset both come from the group, so it would land in no pool and survive every release.");
            }
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
            {
                context.Currencies.ValidateReference(currencyId, $"Chapter '{chapter.Id}' (recordBuff affects)");
                // Before 5.7 this was impossible rather than checked: fan accrual
                // was its own system and only ever composed FanRate, so no income
                // multiplier could reach it. Fan production is an ordinary config
                // now, so the only thing keeping Records off the fan rate is this
                // list - and Records inflating fans would let time away shortcut
                // the Records payout, the coupling section 11 exists to prevent
                // and the same failure the reset-on-release check above guards
                // from the other side.
                if (!string.IsNullOrEmpty(chapter.Fans.CurrencyId) && currencyId == chapter.Fans.CurrencyId)
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' lists its fans currency '{currencyId}' in recordBuff affects - the Records multiplier must never reach the fan rate, or time away shortcuts the Records payout (design doc section 11).");
            }
            // Fans are the run's performance meter and the Records payout reads
            // fansThisRun, so they have to return to zero on release. Filed in a
            // group that keeps them, fans compound across runs: the payout inflates
            // and every fans gate stays satisfied after the first release, which
            // still plays, just far too easily. Only asked when the id resolves, so
            // a bad reference reports once rather than twice.
            if (context.Currencies.ValidateReference(chapter.Fans.CurrencyId, $"Chapter '{chapter.Id}' (fans currency)")
                && !context.Currencies.ResetsOnAlbumRelease(chapter.Fans.CurrencyId))
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' fans currency '{chapter.Fans.CurrencyId}' is in a currency group that survives an album release - fans would compound across runs and inflate the Records payout.");

            // the release offer's gate, checked like every other authored
            // condition (unresolvable ids, undeclared flags); null is legal -
            // always offered once revealed
            ConditionEvaluator.Validate(chapter.Album.ReleaseWhen, context, $"Chapter '{chapter.Id}' album (unlock)");

            // negative tuning drains or dead-ends instead of earning; runtime
            // fails closed on all of it (guarded ticks, zeroed tap), so
            // without these reports the systems would just look mysteriously
            // dead. The base rate is a production config now and is checked as
            // one, with every other config.
            if (chapter.Fans.PerBandmateOwnedBonus < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a negative fans perBandmateOwnedBonus ({chapter.Fans.PerBandmateOwnedBonus}).");
            if (chapter.RecordBuff.PerRecord < 0)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' has a negative recordBuff perRecord ({chapter.RecordBuff.PerRecord}).");
            // Who sets each declared flag, collected from the loaded ASSETS as
            // they validate - not from the JSON, which the importer lints
            // separately: a stale or hand-edited asset can disagree with the
            // file, and boot is the only pass that sees what the game will
            // actually run. Each setter records its owning FACT's scope, paired
            // here at the call site (rule 11); the flag ids themselves surface
            // through SetFlagEffect's own Validate, via the context's listener,
            // so no code outside the family walks a payload.
            var flagSetters = new Dictionary<string, List<ContentScope>>();

            // the capstone's own grants are chapter-boundary facts: whatever its
            // OnComplete sets is permanent, exactly like the declared completion
            // flag recorded further down
            context.FlagSetterReport = flagId => RecordSetter(flagSetters, flagId, ContentScope.PermanentInChapter);
            ValidateCapstone(chapter, context);
            context.FlagSetterReport = null;

            ValidateFlagDeclarations(chapter, database, rewards);
            ValidateIds(chapter.StoryBeatIds, database.StoryBeats, $"Chapter '{chapter.Id}' (storyBeats)");
            foreach (var id in chapter.StoryBeatIds)
            {
                if (!database.StoryBeats.TryGet(id, out var beat))
                    continue;
                visited.StoryBeats.Add(id);
                ValidateStoryBeat(beat, context, chapter);
            }
            // no ValidateIds over the roster: ChapterCurrencies.ValidateRoster
            // reports an unresolvable entry as part of the roster rules it owns,
            // and says what to do about it (re-run the import) rather than only
            // that the id is unknown
            foreach (var id in chapter.CurrencyIds)
            {
                if (!database.Currencies.TryGet(id, out var currency))
                    continue;
                visited.Currencies.Add(id);
                ValidateCurrency(currency, context);
            }

            // the chapter's producers: their gates are chapter-scoped like every
            // other flag reference - flag ids may repeat across chapters, so the
            // owning chapter's list is the only one that counts
            ValidateIds(chapter.ProducerIds, database.Producers, $"Chapter '{chapter.Id}' (producers)");
            foreach (var id in chapter.ProducerIds)
            {
                if (!database.Producers.TryGet(id, out var producer))
                    continue;
                visited.Producers.Add(id);
                ValidateProducer(producer, context);
            }

            ValidateIds(chapter.SectionIds, database.Sections, $"Chapter '{chapter.Id}' (sections)");
            ValidateIds(chapter.GeneratorIds, database.Generators, $"Chapter '{chapter.Id}' (generators)");
            ValidateIds(chapter.UpgradeIds, database.Upgrades, $"Chapter '{chapter.Id}' (upgrades)");
            ValidateIds(chapter.BarGroupIds, database.BarGroups, $"Chapter '{chapter.Id}' (barGroups)");
            ValidateIds(chapter.EventIds, database.Events, $"Chapter '{chapter.Id}' (events)");

            // Which producers this chapter's sections actually present. This is the
            // check that replaces ProducerDefinition.ModuleAddress (retired in 6.5):
            // "who presents this producer" is derived from the section entries that
            // name it rather than restated on the producer, so the two can no longer
            // disagree - and the thing worth reporting was never the missing string
            // but the consequence, a tap surface the player cannot reach.
            //
            // Filled by the binding check as it walks each section, because that check
            // is the only place the module's FAMILY is known. An id read off any entry
            // regardless of family counts a card as presenting the producer whose id it
            // happens to share, which is precisely the dead-Jam-button case
            // ValidateModuleBinding exists to catch - reported on the entry, then
            // silently forgiven here.
            var presentedProducers = new HashSet<string>();
            foreach (var id in chapter.SectionIds)
            {
                if (!database.Sections.TryGet(id, out var section))
                    continue;
                visited.Sections.Add(id);
                ValidateSection(section, context, chapter, database, presentedProducers);
            }

            foreach (var id in chapter.ProducerIds)
            {
                if (!database.Producers.TryGet(id, out var producer) || !producer.HasTapConfigs)
                    continue; // a passive producer (fan accrual) needs no surface
                if (!presentedProducers.Contains(id))
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' producer '{id}' has tap configs but no section module presents it - a tap fires one named producer, so nothing could ever fire this one.");
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
                context.FlagSetterReport = flagId => RecordSetter(flagSetters, flagId, upgrade.Scope);
                ValidateUpgrade(upgrade, context);
                context.FlagSetterReport = null;
            }

            // Rewards enter the closure through bars and event tiers; ids are
            // collected first so a reward two bars share validates once per
            // chapter. The setter scope is the REFERENCING content's (a bar
            // carries its group's, a tier its own), so the reward's flags are
            // collected once at validation and paired per reference below.
            var rewardIds = new HashSet<string>();
            var rewardScopeRefs = new List<(string rewardId, ContentScope scope)>();

            foreach (var id in chapter.BarGroupIds)
            {
                if (!database.BarGroups.TryGet(id, out var group))
                    continue;
                visited.BarGroups.Add(id);
                ValidateBarGroup(group, database, context, rewards);

                foreach (var barId in group.BarIds)
                {
                    if (!database.Bars.TryGet(barId, out var bar))
                        continue;
                    visited.Bars.Add(barId);
                    ValidateBar(bar, context, rewards);
                    if (!string.IsNullOrEmpty(bar.RewardId))
                    {
                        rewardIds.Add(bar.RewardId);
                        rewardScopeRefs.Add((bar.RewardId, group.Scope));
                    }
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
                    {
                        rewardIds.Add(tier.RewardId);
                        rewardScopeRefs.Add((tier.RewardId, tier.Scope));
                    }
                }
            }

            var rewardFlags = new Dictionary<string, List<string>>();
            foreach (var rewardId in rewardIds)
            {
                // an unknown id is reported against the bar/tier that names it
                if (!database.Rewards.TryGet(rewardId, out var reward))
                    continue;
                visited.Rewards.Add(rewardId);
                context.FlagSetterReport = flagId =>
                {
                    if (!rewardFlags.TryGetValue(rewardId, out var ids))
                        rewardFlags.Add(rewardId, ids = new List<string>());
                    ids.Add(flagId);
                };
                ValidateRewardDefinition(reward, context);
                context.FlagSetterReport = null;
            }

            foreach (var (rewardId, scope) in rewardScopeRefs)
            {
                if (!rewardFlags.TryGetValue(rewardId, out var ids))
                    continue;
                foreach (var flagId in ids)
                    RecordSetter(flagSetters, flagId, scope);
            }

            // The capstone's completion flag is set by the completion OPERATION,
            // from the declaration - recorded as a permanent setter, which is what
            // a chapter boundary is: completing the capstone is not a fact a
            // release takes back.
            if (chapter.Capstone != null && chapter.Capstone.IsAuthored
                && !string.IsNullOrEmpty(chapter.Capstone.CompletionFlagId))
                RecordSetter(flagSetters, chapter.Capstone.CompletionFlagId, ContentScope.PermanentInChapter);

            ValidateFlagLifetimes(chapter, flagSetters);
        }

        private static void RecordSetter(Dictionary<string, List<ContentScope>> setters, string flagId,
            ContentScope scope)
        {
            if (string.IsNullOrEmpty(flagId))
                return;
            if (!setters.TryGetValue(flagId, out var scopes))
                setters.Add(flagId, scopes = new List<ContentScope>());
            scopes.Add(scope);
        }

        // The flag-lifetime checks, over the setters the chapter's assets just
        // disclosed. The importer runs the same two rules over the authored JSON
        // for early feedback; this pass is the one that covers what the game
        // actually loads.
        private static void ValidateFlagLifetimes(ChapterDefinition chapter,
            Dictionary<string, List<ContentScope>> setters)
        {
            foreach (var flag in chapter.Flags)
            {
                if (flag == null || string.IsNullOrEmpty(flag.Id))
                    continue;

                // A flag no content sets is PROBABLY dead - everything gated on
                // it silently never appears, and at runtime an unset flag looks
                // exactly like a not-yet-earned one. A warning rather than an
                // error, because a flag set from code alone is legitimate and
                // invisible to this sweep.
                if (!setters.TryGetValue(flag.Id, out var scopes))
                {
                    Debug.LogWarning($"ContentValidator: Chapter '{chapter.Id}' declares flag '{flag.Id}' but no content sets it - unless code sets it, every flagSet gate on it stays closed and the content behind them can never appear.");
                    continue;
                }

                // a run-scoped flag needs at least one setter whose own fact
                // resets with the run: with only permanent setters, the release
                // clears the flag and the projection immediately re-asserts it
                // from the surviving latch, so the declared scope does nothing
                if (flag.Scope == ContentScope.Run && !scopes.Contains(ContentScope.Run))
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' flag '{flag.Id}' is run-scoped but every setter is permanent - the release clears it and the projection re-asserts it in the same operation, so the scope has no effect.");
            }
        }

        // The capstone (design doc sections 1-2 and 5). Its unlock is now the SOLE
        // authored source of the chapter gate, so what used to be a check on a
        // scalar `capstoneRecordsGate > 0` becomes ordinary condition validation
        // plus one thing ordinary validation cannot cover.
        //
        // Non-positive thresholds need nothing bespoke: every threshold condition
        // calls Condition.ValidateThreshold, which reports one, and ThresholdIsMet
        // fails closed so the gate is never met rather than always met. An empty
        // compound is likewise already refused. What none of that catches is a NULL
        // unlock, because by this codebase's convention a null Condition means "no
        // gate" and is always met - which for a capstone means the chapter ends
        // before it starts.
        private static void ValidateCapstone(ChapterDefinition chapter, ConditionContext context)
        {
            var capstone = chapter.Capstone;
            if (capstone == null || !capstone.IsAuthored)
                return; // a chapter need not declare one

            if (capstone.Unlock == null)
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' capstone '{capstone.Id}' has no unlock condition - a null gate is always met, so the capstone would be offered at boot.");
            else
                ConditionEvaluator.Validate(capstone.Unlock, context, $"Chapter '{chapter.Id}' capstone '{capstone.Id}' (unlock)");

            // The completion flag is the chapter boundary, so it has to outlive a
            // release: anything other than permanent-in-chapter clears at the next
            // demo and re-opens a finished chapter. Compared for EQUALITY rather than
            // against Run alone - None is the un-migrated value a hand-edited
            // declaration can hold, and it is no more a lifetime than Run is.
            if (string.IsNullOrEmpty(capstone.CompletionFlagId))
                Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' capstone '{capstone.Id}' names no completion flag - nothing would record that the chapter finished.");
            else
            {
                var declaration = FindFlag(chapter, capstone.CompletionFlagId);
                if (declaration == null)
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' capstone '{capstone.Id}' names completion flag '{capstone.CompletionFlagId}', which the chapter does not declare.");
                else if (declaration.Scope != ContentScope.PermanentInChapter)
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' capstone completion flag '{capstone.CompletionFlagId}' is declared {declaration.Scope} - a chapter boundary must be permanent-in-chapter, or the next release clears it and re-opens a finished chapter.");
            }

            // An absent onComplete is legal: completing always at least latches the
            // declared completion flag, and the OPERATION sets that flag itself from
            // the declaration above - the payload never carries a copy, so there is
            // no second statement of the fact to keep in agreement (the check that
            // used to walk the payload for it is gone because the mistake it caught
            // is no longer authorable).
            capstone.OnComplete?.Validate(context, $"Chapter '{chapter.Id}' capstone '{capstone.Id}' (onComplete)");

            foreach (var action in capstone.Actions)
            {
                if (action == null)
                    Debug.LogError($"ContentValidator: Chapter '{chapter.Id}' capstone '{capstone.Id}' has a null action entry.");
                else
                    action.Validate(context, $"Chapter '{chapter.Id}' capstone '{capstone.Id}' (actions)");
            }
        }

        // A beat is content that a card presents; its reveal is its SECTION's
        // visibleWhen, so there is no gate here to check. What is left is that the
        // card would have something to show, and that a read latch it records is a
        // flag the chapter actually declares.
        //
        // The flag half is skipped for an orphan (null chapter), the same allowance
        // every other flag check makes - no declaration list governs a beat no chapter
        // lists. The text check is not skipped: an empty beat shows an empty card
        // whoever lists it, which is why the orphan pass reaches this at all.
        private static void ValidateStoryBeat(StoryBeatDefinition beat, ConditionContext context,
            ChapterDefinition chapter)
        {
            if (string.IsNullOrEmpty(beat.Text))
                Debug.LogError($"ContentValidator: Story beat '{beat.Id}' has no text - its card would show nothing.");

            if (string.IsNullOrEmpty(beat.ReadFlagId) || chapter == null)
                return; // recording the read is optional

            var declaration = FindFlag(chapter, beat.ReadFlagId);
            if (declaration == null)
                Debug.LogError($"ContentValidator: Story beat '{beat.Id}' records its read on flag '{beat.ReadFlagId}', which chapter '{chapter.Id}' does not declare.");
        }

        private static FlagDeclaration FindFlag(ChapterDefinition chapter, string flagId)
        {
            foreach (var declaration in chapter.Flags)
            {
                if (declaration != null && declaration.Id == flagId)
                    return declaration;
            }
            return null;
        }

        // The declared flag list is the chapter's whole reveal vocabulary - every
        // flag check anywhere is measured against it - so a blank entry declares
        // nothing and a repeat says the same thing twice, both of which read as
        // an authoring slip in the JSON flags array. The flag-LIFETIME checks run
        // separately (ValidateFlagLifetimes), after the chapter's payloads have
        // validated and disclosed their setters.
        private static void ValidateFlagDeclarations(ChapterDefinition chapter, ContentDatabase database,
            RewardManager rewards)
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

        private static void ValidateCurrency(CurrencyDefinition currency, ConditionContext context)
        {
            // a negative starting value puts the currency in debt at boot and again
            // after every album release, which resets balances back to it
            if (currency.StartingValue < 0)
                Debug.LogError($"ContentValidator: Currency '{currency.Id}' has a negative starting value ({currency.StartingValue}) - it would start in debt at boot and after every album release.");
        }

        // A producer IS its production configs (design doc section 12, rule 13):
        // every field here is trusted per firing, so broken tuning must report
        // at boot rather than degrade to wrong gameplay - runtime fails closed
        // on all of it (skipped configs, zeroed compositions), which without
        // these reports would just look mysteriously dead.
        private static void ValidateProducer(ProducerDefinition producer, ConditionContext context)
        {
            // No module check: a producer names no module any more (6.5). Which
            // module presents it is the SECTION's declaration, and whether a tap
            // producer has one at all is checked in ValidateChapter, where the
            // section list is in hand.
            if (producer.Production.Count == 0)
                Debug.LogError($"ContentValidator: Producer '{producer.Id}' has no production configs - it would produce nothing.");

            foreach (var config in producer.Production)
            {
                var source = $"Producer '{producer.Id}' (config for '{config.CurrencyId}')";
                context.Currencies.ValidateReference(config.CurrencyId, source);
                // production must never drain (runtime fails closed on it)
                if (config.Amount < 0)
                    Debug.LogError($"ContentValidator: {source} has a negative amount ({config.Amount}).");
                if (config.Trigger == ProductionTrigger.None)
                    Debug.LogError($"ContentValidator: {source} has trigger None (uninitialized) - it would never fire.");
                // a config composes through Global(target), so the target has to
                // be one that composes globally - ProductionConfig.IsComposable
                // owns that rule and ProductionSystem asks the same question, so
                // boot validation and the runtime guard cannot disagree
                if (!ProductionConfig.IsComposable(config.Composes))
                    Debug.LogError($"ContentValidator: {source} declares composition '{config.Composes}', which a config cannot compose - it must be a defined target that composes globally (a qualified target like GeneratorOutput would read an empty bucket).");
                ConditionEvaluator.Validate(config.Gate, context, $"{source} (gate)");
            }
        }

        // presentedProducers collects the tap producers this section's entries present,
        // for the chapter-level "nothing presents this tap surface" check. Null for an
        // orphan, which belongs to no chapter that could ask the question.
        private static void ValidateSection(SectionDefinition section, ConditionContext context,
            ChapterDefinition chapter, ContentDatabase database, HashSet<string> presentedProducers)
        {
            ConditionEvaluator.Validate(section.VisibleWhen, context, $"Section '{section.Id}' (visibleWhen)");
            // a section IS its modules: with none, its reveal shows an empty region.
            // The importer reports it too but writes the asset anyway (an empty
            // region is inert, not wrong), so this is the check that sees it in
            // loaded content - freshly imported or hand-edited alike.
            if (section.Modules.Count == 0)
                Debug.LogError($"ContentValidator: Section '{section.Id}' has no modules - its reveal would show an empty region.");

            // The list is instantiated in order with no de-duplication, so a repeat
            // puts two of the same module in the region, each wired to the same
            // systems - a doubled readout rather than an error anyone would trace.
            //
            // Keyed on address AND definition id, because a repeated ADDRESS is now
            // legitimate: two story-beat cards are one prefab presenting two beats.
            // What is still a mistake is the same prefab presenting the same thing
            // twice.
            var seen = new HashSet<string>();
            foreach (var entry in section.Modules)
            {
                if (entry == null)
                {
                    Debug.LogError($"ContentValidator: Section '{section.Id}' has a null module entry.");
                    continue;
                }

                var key = $"{entry.Address}|{entry.DefinitionId}";
                if (!seen.Add(key))
                {
                    Debug.LogError(string.IsNullOrEmpty(entry.DefinitionId)
                        ? $"ContentValidator: Section '{section.Id}' lists module '{entry.Address}' more than once - it would be instantiated twice."
                        : $"ContentValidator: Section '{section.Id}' lists module '{entry.Address}' for '{entry.DefinitionId}' more than once - it would be instantiated twice.");
                }
                var presented = ValidateModuleBinding(entry, section, chapter, database);
                if (presented != null)
                    presentedProducers?.Add(presented);
            }
        }

        // The binding a parameterized module entry declares, checked against what the
        // module actually needs (ModuleDefinitionKind). Membership in ANY of the
        // chapter's content lists is not enough: a tap button naming a story beat and
        // a card naming the jam producer both resolve that way, the producer counts
        // as presented, and the Jam button is dead. So the module is asked what family
        // it requires, and the id is resolved against exactly that.
        //
        // The prefab is the authority because it is the thing with the requirement -
        // a table of addresses here would restate what a prefab already knows and
        // could disagree with it. Loading one asset per entry is the same order of
        // cost as the address check beside it.
        //
        // The whole binding check is skipped for an orphan (null chapter), the same
        // allowance the flag checks make: no declaration list governs a section no
        // chapter lists.
        // Returns the tap producer this entry presents, or null - the family answer the
        // chapter's "nothing presents this tap surface" check needs. It comes back from
        // here rather than being re-derived because this is where the prefab that knows
        // the family is already in hand; asking again outside would mean loading every
        // module a second time to learn what this call just read.
        private static string ValidateModuleBinding(SectionModule entry, SectionDefinition section,
            ChapterDefinition chapter, ContentDatabase database)
        {
            var source = $"Section '{section.Id}' module '{entry.Address}'";

            // No address names no prefab, so nothing below could check anything - and
            // the entry still counts as a module, so the empty-section check passes it
            // too. Left unreported, the first thing to notice would be ChapterScreen
            // failing to instantiate an empty key at reveal time, with only the address
            // it was handed to go on.
            if (string.IsNullOrEmpty(entry.Address))
            {
                Debug.LogError(string.IsNullOrEmpty(entry.DefinitionId)
                    ? $"ContentValidator: Section '{section.Id}' has a module entry with no address - there is no prefab to instantiate at reveal time."
                    : $"ContentValidator: Section '{section.Id}' has a module entry for '{entry.DefinitionId}' with no address - there is no prefab to instantiate at reveal time.");
                return null;
            }

            if (!TryLoadModulePrefab(entry.Address, source, out var module, out var handle))
                return null;

            try
            {
                var required = module.RequiredDefinition;

                if (required == ModuleDefinitionKind.None)
                {
                    // it renders a roster resolved from the chapter, so an id here
                    // would be read by nobody - which looks like a binding and is not
                    if (!string.IsNullOrEmpty(entry.DefinitionId))
                        Debug.LogError($"ContentValidator: {source} names definition '{entry.DefinitionId}', but that module presents a whole roster and reads no definition id.");
                    return null;
                }

                if (string.IsNullOrEmpty(entry.DefinitionId))
                {
                    Debug.LogError($"ContentValidator: {source} presents a {required} but its section entry names none - the module would present nothing.");
                    return null;
                }

                if (chapter == null)
                    return null;

                switch (required)
                {
                    case ModuleDefinitionKind.TapProducer:
                        if (!Names(chapter.ProducerIds, entry.DefinitionId))
                            Debug.LogError($"ContentValidator: {source} presents producer '{entry.DefinitionId}', which chapter '{chapter.Id}' does not declare - the module would present nothing.");
                        else if (database.Producers.TryGet(entry.DefinitionId, out var producer) && !producer.HasTapConfigs)
                            Debug.LogError($"ContentValidator: {source} presents producer '{entry.DefinitionId}', which authors no tap configs - pressing it would pay nothing.");
                        // presented whether or not the id resolved: an undeclared id is
                        // not in the chapter's producer list for the surface check to
                        // look up, so withholding it would only say the same thing twice
                        return entry.DefinitionId;

                    case ModuleDefinitionKind.StoryBeat:
                        if (!Names(chapter.StoryBeatIds, entry.DefinitionId))
                            Debug.LogError($"ContentValidator: {source} presents story beat '{entry.DefinitionId}', which chapter '{chapter.Id}' does not declare - the card would show nothing.");
                        return null;
                }

                return null;
            }
            finally
            {
                Addressables.Release(handle);
            }
        }

        private static bool Names(IReadOnlyList<string> ids, string id)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                    return true;
            }
            return false;
        }

        private static void ValidateGenerator(GeneratorDefinition generator, ConditionContext context)
        {
            // a zero/negative cost makes a generator free-and-infinite and a
            // non-positive growth breaks the cost curve - content mistakes
            // (including stale assets from before the cost schema) must fail
            // loudly here, not degrade to wrong gameplay. Growth < 1
            // (shrinking costs) is legal.
            context.Currencies.ValidateReference(generator.CostCurrencyId, $"Generator '{generator.Id}' (cost currency)");
            // What it pays into, checked here rather than only in GeneratorSystem:
            // that constructor sees one chapter's generators, so an orphan or a
            // later chapter's generator producing into a nonexistent currency had
            // nothing looking at it. Only checkable for every chapter once the
            // resolver is the declaring chapter's rather than the frontier's.
            context.Currencies.ValidateReference(generator.ProducesCurrencyId, $"Generator '{generator.Id}' (produces)");
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
            if (upgrade.Payload == null && upgrade.Actions.Count == 0)
                Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has no payload and no actions - it would grant nothing.");
            upgrade.Payload?.Validate(context, $"Upgrade '{upgrade.Id}' (payload)");

            foreach (var action in upgrade.Actions)
            {
                if (action == null)
                {
                    Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' has a null action entry.");
                    continue;
                }
                action.Validate(context, $"Upgrade '{upgrade.Id}' (actions)");

                // actions run only from TryBuy, and a content unlock is never
                // bought - an award authored on one would silently never pay,
                // which reads as a tuning problem rather than the authoring
                // mistake it is
                if (upgrade.Type == UpgradeType.ContentUnlock)
                    Debug.LogError($"ContentValidator: Upgrade '{upgrade.Id}' is a content unlock carrying actions - actions execute on purchase, and a content unlock is never bought, so its award would never pay. Move it to a bought buff, an event tier, or the capstone.");
            }
        }

        private static void ValidateBarGroup(BarGroupDefinition group, ContentDatabase database,
            ConditionContext context, RewardManager rewards)
        {
            if (group.FillBehavior == null)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has no fill behavior.");
            else
                group.FillBehavior.Validate(context, $"Bar group '{group.Id}' (fillBehavior)");
            if (group.Scope == ContentScope.None)
                Debug.LogError($"ContentValidator: Bar group '{group.Id}' has scope None (uninitialized).");
            ConditionEvaluator.Validate(group.VisibleWhen, context, $"Bar group '{group.Id}' (visibleWhen)");
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

        // Conditions and payloads resolve content ids through the database, and
        // BOTH per-chapter declaration lists through the chapter being validated:
        // flags through its FlagIds, currencies through its CurrencyIds. The
        // orphan pass (null chapter) gets the unrestricted form of each, so
        // declaration-membership checks pass instead of false-positiving against
        // an arbitrary chapter - no declaration list governs an orphan.
        //
        // Nothing here comes from the running economy any more. The currencies a
        // chapter may reference is a content question, and answering it from the
        // frontier's pool made every OTHER chapter's currencies unresolvable -
        // correct only while one chapter exists. Generators and bars were carried
        // in for the same reason and are unused: both conditions resolve those
        // ids through the database already.
        // Generators and bars are deliberately null. Every Validate that resolves
        // one of those ids prefers the database registry (the branch that exists
        // precisely to cover ids outside the running chapter), so the live
        // systems were never read here - they were only subscribed to, by a
        // ConditionContext that nothing then disposed, leaving the running
        // systems holding a reference to every validation context ever built.
        // Not passing what is not read is the fix; disposal would only tidy up
        // after a subscription that had no reason to exist.
        //
        // The records id is all this still needs from the caller - which is
        // what lets boot run validation before any economy exists.
        private static ConditionContext ChapterScoped(ChapterCurrencies currencies, string recordsCurrencyId,
            ContentDatabase database, ChapterDefinition chapter)
            => new(currencies, null,
                chapter != null ? new FlagSystem(chapter.FlagIds) : new FlagSystem(),
                recordsCurrencyId, database, null);

        // which definitions some chapter's closure validated, so the orphan
        // pass covers exactly the rest
        private class Visited
        {
            public readonly HashSet<string> Currencies = new();
            public readonly HashSet<string> Producers = new();
            public readonly HashSet<string> Sections = new();
            public readonly HashSet<string> Generators = new();
            public readonly HashSet<string> Upgrades = new();
            public readonly HashSet<string> BarGroups = new();
            public readonly HashSet<string> Bars = new();
            public readonly HashSet<string> Events = new();
            public readonly HashSet<string> Rewards = new();
            public readonly HashSet<string> StoryBeats = new();
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

        private static void ValidateRewardReference(string rewardId, RewardManager rewards, string source)
        {
            if (string.IsNullOrEmpty(rewardId))
                return; // no reward is legal content

            if (!rewards.Contains(rewardId))
                Debug.LogError($"ContentValidator: {source} references unknown reward id '{rewardId}'.");
        }

        // Loads a module prefab so its declared requirement can be read. Returns the
        // handle so a successful load is released by the caller that borrowed it, and
        // every failing path releases here before returning false.
        // A prefab with no IChapterModule is reported here rather than only at
        // instantiation - ChapterScreen says the same thing, but at boot the section
        // that names it is still in hand.
        //
        // Every way this fails is reported, because this is now the only check the
        // address gets: an address resolving to no prefab throws out of LoadAssetAsync
        // and lands in the catch. The caller refuses an empty address before calling,
        // since there is no key to attempt and the section is the useful thing to name.
        private static bool TryLoadModulePrefab(string address, string source,
            out IChapterModule module, out AsyncOperationHandle<GameObject> handle)
        {
            module = null;
            handle = default;

            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(address);
                var prefab = handle.WaitForCompletion();
                if (prefab == null)
                    Debug.LogError($"ContentValidator: {source} references module address '{address}', which resolves to no prefab - the section would fail to instantiate it at reveal time.");
                else if (prefab.TryGetComponent(out module))
                    return true;
                else
                    Debug.LogError($"ContentValidator: {source} has no IChapterModule component on its root - the section would instantiate it and initialize nothing.");
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"ContentValidator: {source} could not be loaded to check what it presents ({exception.Message}).");
            }

            if (handle.IsValid())
                Addressables.Release(handle);
            handle = default;
            return false;
        }
    }
}
