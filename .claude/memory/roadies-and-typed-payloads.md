---
name: roadies-and-typed-payloads
description: "2026-08-20 corrections John directed: roadie venues deleted, no stationing cap, scope payloads typed by tree position, currencies declared by direct reference"
metadata: 
  node_type: memory
  type: project
  originSessionId: 725a3991-fe12-4b4f-bdb9-02cb627048e5
  modified: 2026-08-20T20:03:27.332Z
---

Four corrections landed 2026-08-20, before step 5 began, each reversing something a build step had
invented. Suite is 205/205 after them.

- **`RoadieVenueDefinition` is DELETED.** Roadies attach to the band and are stationed per chapter;
  "venue" was flavor prose in section 8.2 that step 4 turned into an asset with a `chapterScopeId`
  back-pointer. The two boosts are `CareerEffectDefinition`s on root whose formulas carry their own
  `perRoadie`: `RoadieTotalBoost` sums the whole `roadieAllocation` map, `RoadieActiveBoost` reads
  the chapter it resolves on. Nothing walks the tree for numbers, nothing points back at a chapter.
- **No stationing cap.** The pre-build doc only ever said per-venue rates and caps were *planned*;
  step 4 shipped them as authored fact with clamping in both formulas. The global boost is linear in
  the pool, so where Roadies sit changes the sprint, not the total, and price is the throttle. If a
  cap is ever wanted it belongs at the WRITE (`SetRoadieAllocation`), never in the read.
- **Scope payloads are typed by position**: `ScopeFacts` for every scope, `RootFacts` adding
  `roadieAllocation` + `entitlements`, `ChapterFacts` adding `pendingClaim`; `RootScopeState` /
  `ChapterScopeState` with `lastActiveUtc` on the chapter one. `ScopeState.Build` allocates each
  node's payload by depth; `Root()` / `Chapter()` extensions replace the base-class property (a base
  class never names its derived types). `SaveSystem` reads each node's facts against the type the
  tree position dictates, so a save cannot name its own type, and the three placement filters are
  gone - unrepresentable beats policed.
- **Currencies are declared by direct reference** (`ScopeDefinition.declaredCurrencies`), like
  producers/generators/upgrades/triggers; `declaredCurrencyIds` and its dropdown are gone. Ids are
  derived through the `currencyIds` accessor, and the declaration now gets the same
  "resolves from the database to THIS asset" check as every other family.

**Why:** each was a second way to say something the architecture already said - see
[[reuse-the-existing-mechanism]] - and John found all four by reading, not by being told.

**How to apply:** do not reintroduce venue assets, stationing caps, per-scope roadie fields, or
currency id lists. Section 8.2, section 9's throttle sentence, 12.3's state schema, 12.11's
`SetRoadieAllocation` invariants, and `chapter-01-content.md` were all corrected to match.
