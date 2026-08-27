---
name: roadies-and-typed-payloads
description: "2026-08-20 corrections John directed: roadie venues deleted, no stationing cap, scope payloads typed by tree position, currencies declared by direct reference"
metadata: 
  node_type: memory
  type: project
  originSessionId: 725a3991-fe12-4b4f-bdb9-02cb627048e5
  modified: 2026-08-26T22:59:13.353Z
---

Four corrections landed 2026-08-20, before step 5 began, each reversing something a build step had
invented. Suite is 205/205 after them.

- **`RoadieVenueDefinition` is DELETED.** Roadies attach to the band and are stationed per chapter;
  "venue" was flavor prose in section 8.2 that step 4 turned into an asset with a `chapterScopeId`
  back-pointer. The two boosts are formula-shaped effects in permanent modifiers on root (the
  `CareerEffectDefinition` family they started as folded into modifiers in step 7 slice B,
  2026-08-26) whose formulas carry their own
  `perRoadie`: `RoadieTotalBoost` sums the whole `roadieAllocation` map, `RoadieActiveBoost` reads
  the chapter it resolves on. Nothing walks the tree for numbers, nothing points back at a chapter.
- **No stationing cap.** The pre-build doc only ever said per-venue rates and caps were *planned*;
  step 4 shipped them as authored fact with clamping in both formulas. The global boost is linear in
  the pool, so where Roadies sit changes the sprint, not the total, and price is the throttle. If a
  cap is ever wanted it belongs at the WRITE (`SetRoadieAllocation`), never in the read.
- **Scope payloads are typed by the scope's authored KIND** (2026-08-24: this originally read
  "typed by position", and the position half is now wrong - a definition is a `RootDefinition`,
  `ChapterDefinition` or `TierDefinition` and builds its own state node, so nothing infers a kind
  from depth): `ScopeFacts` for every scope, `RootFacts` adding
  `roadieAllocation` + `entitlements` + `currentChapterId`, `ChapterFacts` adding nothing (its
  `pendingClaim` was deleted 2026-08-26 - the stamp is the pending claim, offers are transient);
  `RootScopeState` /
  `ChapterScopeState` with `lastActiveUtc` on the chapter one, plus `TierFacts`/`TierScopeState` and
  the abstract `InteriorFacts`/`InteriorDefinition` middles. Each definition allocates its own
  node's payload; `Root()` / `Chapter()` extensions replace the base-class property (a base
  class never names its derived types). `SaveSystem` reads each node's facts against the type the
  tree position dictates, so a save cannot name its own type, and the three placement filters are
  gone - unrepresentable beats policed.
- **Currencies are declared by direct reference** (`ScopeDefinition.declaredCurrencies`), like
  producers/generators/upgrades/triggers; `declaredCurrencyIds` and its dropdown are gone. Ids are
  derived through the `currencyIds` accessor, and the declaration now gets the same
  "resolves from the database to THIS asset" check as every other family.

**Also 2026-08-20, the same session, on John's call:** the CONTENT DATABASE IS GONE.
`IDefinitionSource` deleted; `ContentDatabase` loads the root scope (chapters stream as their own
Addressables entries) and runs validation. Every authored field that named content by id now holds
the asset itself - condition and action operands, cost currencies, produces entries, scope targets -
so `DefinitionIdAttribute` and its drawer are deleted too; only flags, tags, and stat names stay
strings, since none of them is an asset. Ids survive exactly where a FACT needs one (the save is
ids), and such an id resolves by walking its scope OUTWARD, never by lookup. Consequences: modifiers
are declared content (`ScopeDefinition.modifiers`) with stacks as `Dictionary<string, int>`; bar
groups are declared and own their bars; ids are unique per CHAIN rather than tree-wide (scope ids
stay tree-wide); `ResetScope` reaches self-or-enclosed only, never a peer; and the
"declared but undiscoverable" check died with the catalogue. Suite: 194 tests.

**Why:** each was a second way to say something the architecture already said - see
[[reuse-the-existing-mechanism]] - and John found all four by reading, not by being told.

**How to apply:** do not reintroduce venue assets, stationing caps, per-scope roadie fields, or
currency id lists. Section 8.2, section 9's throttle sentence, 12.3's state schema, 12.11's
`SetRoadieAllocation` invariants, and `chapter-01-content.md` were all corrected to match.
