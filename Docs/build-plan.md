# Build Plan

Working order for building the §12.13 spine and getting Chapter 1 playable. The design doc owns
*what* (architecture, §12); `chapter-01-content.md` owns Chapter 1's *numbers*; this doc owns the
*order and current position* — update the status line when a step lands. Steps are ordered so each
step's tests need only what came before it.

**Standing constraint on every step (§12.14 requirement 8).** No step introduces a registry, an id
index, or a tree-wide search to make its own content findable. Everything is declared on a scope and
reached by walking outward from the acting scope, or downward through one named subtree. If a step's
content seems to need a lookup, that is a conversation, not a design decision - the answer is almost
always that the content belongs on a scope.

| # | Step | Status |
|---|---|---|
| 1 | Core class families + state | **DONE 2026-08-18** — 51/51 green (50 project-authored + Addressables' TestStub) |
| 2 | Save system | **DONE 2026-08-18** — 66/66 green (65 project-authored + Addressables' TestStub) |
| 3 | ContentDatabase + validation | **DONE 2026-08-18** — 123/123 green (122 project-authored + Addressables' TestStub) |
| 4 | Producers, generators, upgrades + resolution | **DONE 2026-08-19** - 208/208 green at the time |
| - | Correction pass (2026-08-20) | **DONE** - 194/194 green: authored content references assets rather than ids, the content database and `IDefinitionSource` deleted, modifiers and bar groups declared, `ResetScope` self-or-enclosed, ids unique per chain, content faults throw |
| 5 | Bars | **DONE 2026-08-21** - 266/266 green |
| - | Scope kind refactor (2026-08-24) | **DONE** - 271/271 green: a scope is authored as `RootDefinition` / `ChapterDefinition` / `TierDefinition` under the abstract `InteriorDefinition`, each building its own state node, so no depth test infers a kind; `rung` moved off the base and `RungOnRoot` was deleted with it; the save populates the payload the node holds; the three outward walks ask `Declares` / `MultiplierFor` / `SourceTermsFor` instead of reading lists off a base-typed node; placement is validated root -> chapters -> tiers |
| 6 | Events + trigger sweep | **DONE 2026-08-25** - 327/327 green |
| 7 | Tick + GameSession | **DONE 2026-08-26** - 397/397 green (six slices A-F; two design revisions absorbed mid-step: the idle respell, then the claim respell - the stamp IS the pending claim, offers are transient and session-held) |
| - | Tag declarations + effect-selector deletion | **DONE 2026-08-27** - 379/379 green: `declaredTags` on the scope joining the per-chain name space, carried tags resolved outward from the carrier, and the downward effect-selector searches deleted, leaving the stat coordinate and the two game_speed shape checks (slice 0 of `step-08-plan.md`) |
| 8 | Chapter 1 JSON + importer + walkthrough tests | **slice A landed 2026-08-27** - 404/404 green: `ComposedContent` and the load-and-compose `ContentDatabase`, the save reshaped onto the pair, the document-scoped importer with its lints and preflight-the-union contract, `root.json`, and the root entry in one PackTogether group; slices B-D remain |
| 9 | UI layer | not started |
| 10 | Meta & monetization | not started |
| 11 | Orphan sweep | not started |

## Step detail

1. **Core class families + state** — `Definition`, `Effect`, `Condition` + 9 kinds, `GameAction` +
   6 kinds (`Action` renamed for the `System.Action` collision), `PayoutFormula` + 2 kinds,
   `Rung` (renamed from `Press` in step 3), `TriggerDefinition` (shape only), `ScopeState` (complete §12.3 *state* schema; reset
   swaps the `ScopeFacts` payload, so new fields clear by construction; the root is structurally
   unresettable), `ScopeDefinition` (currencies, flags, triggers, and the rung that moved to
   `InteriorDefinition` in the 2026-08-24 refactor — the remaining
   declaration lists land with their families in steps 4–6, since their types don't exist yet),
   `GameContext` (outward chain walks, rebasing), `IDefinitionSource` seam, Economy data shapes
   (`CurrencyDefinition`, `ModifierDefinition` + stacking, `BarDefinition`/`BarGroupDefinition`/
   `BarGroupDefinition`), NumberFormatter display rules. All currency/production values are
   `BigNumber`, authored fields included.
2. **Save system** (§12.10) — serialize the ScopeState tree and nothing else; schemaVersion +
   explicit migrations (missing path or newer version = refused); checksum bound over version AND
   payload; atomic write whose backup only ever receives verified content; load falls back to the
   backup on any read or verification failure. Unknown-id drops cover the families knowable today
   (currencies, flags, trigger latches, roadie scope ids - the recorded current chapter joins in
   step 7); later
   definition families extend the filter with their steps — the same incremental contract as the
   validation pass. The negative-clock clamp lives in step 7: §12.10 files it under save
   hardening, but it guards elapsed-time computation, which doesn't exist until idle does.
3. **ContentDatabase + validation** (§12.12, §12.14.5–6) — the Addressables load of the content
   set, each entry bringing its own directly-referenced graph with it (the label-based discovery and
   `IDefinitionSource` lookup this step originally shipped are deleted). The one-root-load this
   step described became the composition PAIR in step 8 slice A: the root asset at a fixed address
   plus the chapter roots under one label, composed into `ComposedContent`, since root's serialized
   child list is empty by contract and the chapter documents are the roster. And the
   validation-pass *framework*
   plus every §12.12 check whose inputs exist by this step (id uniqueness, tag/id collision,
   scope-reference reach, effect reach per target kind, flag setter rules, set-then-wiped,
   stranded value; the effect-reach and effect-target checks this step shipped are deleted - they
   judged an effect by enumerating candidates BELOW it, and §12.12 now leaves effect placement
   unjudged). The pass is incremental by design: each later step is REQUIRED to extend it
   with the checks its own shapes introduce - step 6 completed the set, and the full §12.12 pass
   runs today. Fail loudly at boot in dev builds: validation runs on the production load path itself.
4. **Producers, generators, upgrades + resolution** (§12.2, §12.6) — `ProducerDefinition` +
   produces entries, `GeneratorDefinition` (`availableWhen`, cost curve, ownedCount scaling),
   `UpgradeDefinition` (gate, effects, actions), `GetMultiplier` two-stage gathering (source
   scope-to-root, currency home-to-root), `FireProducer` (atomic pre-fire resolution), `TryBuy`
   (fail-closed). Effects-from-facts covers the rows whose sources exist here (upgrades,
   generator contributions, granted modifier stacks, and the career facts step 7 later folded
   into permanent modifiers); later rows join with their
   steps. Extends validation: produces-entry targets, generator/upgrade reference resolution,
   tag membership extending to producers and generators - that last one deleted with step 3's
   effect-target checks. Producers and generators still carry tags, and each carried tag is now
   checked the other way round: outward from the carrier to the scope declaring it (§12.2).
5. **Bars** (§12.7) — a bar drinks the currency it names at its own rate, taking what is there in
   declaration order; a group only caps how many run at once. Iterative completion settlement in
   deterministic order, cascades (`perFill` × `fillCount` — the fill-count row of
   effects-from-facts), `SetActiveBars` (fail-closed). Extends validation: bar and group checks.
6. **Events + trigger sweep** (§6.1, §12.8, §12.5) — `EventDefinition` declared on its host scope
   (`InteriorDefinition.events` - root cannot host one), the host found by the outward walk asked for
   an `InteriorScopeState`, and the `ActiveEvent` record's id
   resolving the same way, exactly as modifiers do; `StartEvent` / `DismissEvent` as COMMANDS rather
   than Action kinds (so no authored list can start or end an event), the two ending lists
   (`rewards` on success, `onEnd` always), `EventRewardPending`/`EventRecordExists` condition kinds,
   handicaps derived from a record existing (the active-event row of effects-from-facts), the
   `Always` condition and `RestartScope` action, the transaction sweep (latch-first, sweep-start
   snapshot, deterministic order, goal latch before trigger actions). Extends validation to
   complete §12.12: `RestartScope` in both ledgers, gates may not be null,
   balance-goal-without-reset, stranded-reward
   guard, event kinds' reach.
7. **Tick + GameSession** (§12.9) — dt segmentation at expiry timestamps, fixed economy phases per
   segment, `game_speed` scaled production vs real wall clocks, idle switch-in with the transient
   offer and exactly-once settlement via the stamp (including the §12.10 negative-clock clamp: a
   backwards device clock clamps elapsed to zero, never mints currency), phase machine + command
   boundary, the fail-closed entry points, refresh pipeline hooks.
8. **Chapter 1 JSON + importer + walkthrough tests** (§12.14.5) — the chapter JSON schema, the
   editor importer (materializes SO assets and WIRES THEM INTO the scope tree's declaration lists,
   since a declaration is a direct reference and there are no labels to assign; re-import overwrites; mine the old
   `ChapterJsonImporter` at `c446613^` for type-field mapping and import lints), author
   `chapter-01.json` from the content doc, then the four walkthroughs as tests: normal release,
   event entry, replay clear, 4-hour idle claim.
9. **UI layer** (§12.11) — `SectionDefinition`/`ModuleDefinition`/`ModuleRegistry`, widgets, the
   rung feedback contract (`uiText` legs, progress rendering), two-trigger refresh, interpolation.
10. **Meta & monetization** (§8, §9) — `SetRoadieAllocation` + the allocation UI
    (the roadie-boost arithmetic is step 4's), Encore/game-speed buffs, AdManager (rewarded: Encore
    top-up, Double It), IAPManager (Backstage Pass, Roadie bundles, Tip Jar), story beat cards +
    `AcknowledgeStory`.
11. **Orphan sweep** (§12.12) - the one whole-tree validation pass, and validation's only legitimate
    use of the tree-wide exception: what nothing references. A modifier nothing grants and no scope
    lists - which also has no site, so its formula and `appliesWhen` were never validated, another
    reason the sweep is what surfaces it - a declared tag nothing carries, a declared flag nothing
    sets (`FlagNoSetter` is already an
    instance of this shape), and an `Effect` selector no definition's outward walk ever answers to -
    the last being where a misspelled `target` or `currencyId` is caught, since the per-site checks
    deliberately cannot claim it (§12.2). ONE pass that walks every definition outward and records
    what it met, then reports what went unmet - never a per-effect search, which is the whole reason
    the coverage waited for a pass of this shape. Everything it finds is dead weight rather than
    broken behavior, so nothing depends on it; it lands when authored content is voluminous enough
    that hand-checking stops being reliable.

Verification per step: the headless loop (compile grep + edit-mode suite; see the repo memory
`unity-headless-verify-loop`). John reviews and commits per changeset.
