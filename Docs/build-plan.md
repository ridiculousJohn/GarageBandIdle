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
| 6 | Events + trigger sweep | not started |
| 7 | Tick + GameSession | not started |
| 8 | Chapter 1 JSON + importer + walkthrough tests | not started |
| 9 | UI layer | not started |
| 10 | Meta & monetization | not started |

## Step detail

1. **Core class families + state** — `Definition`, `Effect`, `Condition` + 9 kinds, `GameAction` +
   6 kinds (`Action` renamed for the `System.Action` collision), `PayoutFormula` + 2 kinds,
   `Rung` (renamed from `Press` in step 3), `TriggerDefinition` (shape only), `ScopeState` (complete §12.3 *state* schema; reset
   swaps the `ScopeFacts` payload, so new fields clear by construction; the root is structurally
   unresettable), `ScopeDefinition` (currencies, flags, triggers, rung — the remaining
   declaration lists land with their families in steps 4–6, since their types don't exist yet),
   `GameContext` (outward chain walks, rebasing), `IDefinitionSource` seam, Economy data shapes
   (`CurrencyDefinition`, `ModifierDefinition` + stacking, `BarDefinition`/`BarGroupDefinition`/
   `BarGroupDefinition`), NumberFormatter display rules. All currency/production values are
   `BigNumber`, authored fields included.
2. **Save system** (§12.10) — serialize the ScopeState tree and nothing else; schemaVersion +
   explicit migrations (missing path or newer version = refused); checksum bound over version AND
   payload; atomic write whose backup only ever receives verified content; load falls back to the
   backup on any read or verification failure. Unknown-id drops cover the families knowable today
   (currencies, flags, trigger latches, roadie scope ids, pending-claim currencies); later
   definition families extend the filter with their steps — the same incremental contract as the
   validation pass. The negative-clock clamp lives in step 7: §12.10 files it under save
   hardening, but it guards elapsed-time computation, which doesn't exist until idle does.
3. **ContentDatabase + validation** (§12.12, §12.14.5–6) — one Addressables load of the root
   scope, which brings the whole directly-referenced graph with it (the label-based discovery and
   `IDefinitionSource` lookup this step originally shipped are deleted; per-chapter Addressables
   entries can be planned later if load time or memory ever calls for them), and the
   validation-pass *framework*
   plus every §12.12 check whose inputs exist by this step (id uniqueness, tag/id collision,
   scope-reference reach, effect reach per target kind, flag setter rules, set-then-wiped,
   stranded value). The pass is incremental by design: each later step is REQUIRED to extend it
   with the checks its own shapes introduce — a "full §12.12" claim is only true once step 6
   lands. Fail loudly at boot in dev builds: validation runs on the production load path itself.
4. **Producers, generators, upgrades + resolution** (§12.2, §12.6) — `ProducerDefinition` +
   produces entries, `GeneratorDefinition` (`availableWhen`, cost curve, ownedCount scaling),
   `UpgradeDefinition` (gate, effects, actions), `GetMultiplier` two-stage gathering (source
   scope-to-root, currency home-to-root), `FireProducer` (atomic pre-fire resolution), `TryBuy`
   (fail-closed). Effects-from-facts covers the rows whose sources exist here (upgrades,
   generator contributions, granted modifier stacks, career facts); later rows join with their
   steps. Extends validation: produces-entry targets, generator/upgrade reference resolution,
   tag membership extending to producers and generators.
5. **Bars** (§12.7) — a bar drinks the currency it names at its own rate, taking what is there in
   declaration order; a group only caps how many run at once. Iterative completion settlement in
   deterministic order, cascades (`perFill` × `fillCount` — the fill-count row of
   effects-from-facts), `SetActiveBars` (fail-closed). Extends validation: bar and group checks.
6. **Events + trigger sweep** (§6.1, §12.8, §12.5) — `EventDefinition` declared on its host scope
   (`ScopeDefinition.events`), with the lifecycle operations holding a direct reference and the
   `ActiveEvent` record's id resolving outward, exactly as modifiers do; the three self-guarding lifecycle operations,
   `EventRewardPending`/`EventRecordExists` condition kinds, handicaps by live-record derivation
   (the active-event row of effects-from-facts), the transaction sweep (latch-first, sweep-start
   snapshot, deterministic order, goal latch before trigger actions). Extends validation to
   complete §12.12: lifecycle-op cycles, host rules, balance-goal-without-reset, stranded-reward
   guard, event kinds' reach.
7. **Tick + GameSession** (§12.9) — dt segmentation at expiry timestamps, fixed economy phases per
   segment, `game_speed` scaled production vs real wall clocks, idle switch-in/pending
   claim/exactly-once settlement (including the §12.10 negative-clock clamp: a backwards device
   clock clamps elapsed to zero, never mints currency), phase machine + command boundary, the
   fail-closed entry points, refresh pipeline hooks.
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

Verification per step: the headless loop (compile grep + edit-mode suite; see the repo memory
`unity-headless-verify-loop`). John reviews and commits per changeset.
