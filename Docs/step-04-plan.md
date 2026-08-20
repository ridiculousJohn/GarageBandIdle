# Step 4 Plan - Producers, Generators, Upgrades + Resolution

Implementation plan for build-plan step 4, approved 2026-08-19 after two external review
rounds were absorbed. `build-plan.md` owns order and status; this doc owns step 4's design
decisions and work list. Section references are to `garage-band-idle-design.md`.

**Parts of this plan are SUPERSEDED (2026-08-20); the design doc is authoritative.** What the body
below still describes and shouldn't:

- **Roadies.** `RoadieVenueDefinition`, its `chapterScopeId`, per-venue `perRoadie`/`cap`, the
  read-side clamp, and the venue validation pass are deleted. The replacement: two root-declared
  career effects, each carrying its own `perRoadie` and reading the root's `roadieAllocation` map
  and nothing else. `RoadieTotalBoost` is the PRODUCT over the map's entries -
  `Pi (1 + perRoadie x stationed there)`, additive within a chapter and multiplicative across them,
  so spreading is concave and rewarding (8.2). `RoadieActiveBoost` reads the entry for the
  chapter it resolves on, counting the played chapter's stationing a second time. No stationing cap
  exists anywhere, which also makes the "`SetRoadieAllocation` + write-time cap enforcement" line
  in the deferred-work section wrong: the command keeps the nonnegative and sum-of-pool invariants
  only.
- **Purchasing.** The `TryBuy`-only API described below is now `CanBuy` / `Buy` / `TryBuy`: a query
  for the mutable-state question, a command that performs the purchase and refuses when the query
  says no, and the wrapper over both. Content-derived faults - an id resolving to neither kind, no
  declaring scope on the acting chain, a nonpositive computed cost - THROW instead of logging and
  returning false. The same shape applies to `GameContext.CanSpend` / `Spend` / `TrySpend` and to
  `Rung.IsOffered` / `Execute` / `TryExecute`.
- **Lookups.** `Producer.FindCurrencyHome` and `Producer.DeclaringScope` walk outward from the
  acting scope and throw when the chain does not hold the target; the root-down searches this plan
  specified are gone.
- **Currencies.** `declaredCurrencyIds` is gone - a scope declares `List<CurrencyDefinition>` by
  direct reference, and the ids are derived.

## Conceptual model

Step 4 is the compute-on-read half of the economy. Steps 1-3 built the facts (balances,
counts, latches, modifier stacks) and the machinery that guards them; step 4 builds the
arithmetic that turns those facts into numbers: every produced value is
`sum of matching contributions whose conditions hold * product of matching multipliers`,
gathered in two explicit stages (source scope to root, then currency home to root), and the
two mutating entry points that create the new fact kinds (`FireProducer` deposits yields,
`TryBuy` creates counts and latches). Nothing here stores a derived value - `GetMultiplier`
is a pure read, and the only writes are deposits, spends, count increments, latch adds, and
upgrade payload actions.

## New shapes (Economy/)

**`ProducesEntry`** - `{currencyId, stat, BigNumber value, [SerializeReference] Condition
condition}`. A null condition means the entry is active - the condition is optional, an
entry is not a gate. Value is BigNumber per the authored-fields rule; zero legal, negative
refused at validation.

**Stats stay strings** with code constants (`Stat.Rate = "rate"`, `Stat.Yield = "yield"`).
Section 12.2 rules it explicitly - "stats are named, not enumerated" - and `Effect.stat`
committed to string in step 1. The validator warns on a stat outside the consumed set,
which recovers the enum's typo protection.

**`ProducerDefinition : Definition`** - `List<ProducesEntry> produces`. No availableWhen;
entries carry their own conditions.

**`GeneratorDefinition : Definition`** - `{[SerializeReference] Condition availableWhen,
costCurrencyId, BigNumber baseCost, double growth, List<ProducesEntry> produces}`.
`CostAt(owned) = baseCost * Pow(growth, owned)`. Growth is a double - a curve ratio, same
species as Effect.multiplier and Pow's power.

**`UpgradeDefinition : Definition`** - `{[SerializeReference] Condition gate,
costCurrencyId, BigNumber cost, List<Effect> effects, [SerializeReference]
List<GameAction> actions}`. The purchase latch is the existing `purchasedUpgrades` set at
the declaring scope; effects apply while the latch exists.

**Null purchase gates fail closed**: a null `availableWhen` or `gate` refuses the buy,
matching Rung's "an unauthored gate is closed, not open". Validation flags a null purchase
gate as a warning (permanently inert content, the same species as flag-no-setter), not an
error - the validator already rules a null rung offer legal authoring, and the two stay
consistent.

**`CareerEffectDefinition : Definition`** - `{target, currencyId, stat,
[SerializeReference] MultiplierFormula formula}`, plus the `MultiplierFormula` class family
(`Compute(GameContext) -> BigNumber`). Three kinds now: `LinearOnBalance`
(1 + k * balance; records_income), `RoadieTotalBoost` (product over venues of
1 + perRoadie * stationed), `RoadieActiveBoost` (1 + perRoadie * stationed at the chapter
on the resolution chain). Root declares all three Ch. 1 career effects; 8.2's "what Roadies
help with is authored data" holds because the target tag is authored.

**`RoadieVenueDefinition : Definition`** - `{chapterScopeId, double perRoadie, int cap}`.
One per venue; both roadie formulas read it, so step 10 owns only the
`SetRoadieAllocation` command, write-time validation, and UI - never the multiplier
arithmetic. Formulas read `stationed = clamp(saved, 0, venue.cap)`, so a tampered save or
a cap retuned downward never over-pays. Validation: `chapterScopeId` resolves to a root
child (chapters are structurally root's children), at most one venue definition per
chapter.

**Ruling - formula context.** `MultiplierFormula.Compute` receives the gather-origin
context - the source scope in stage 1, the currency home in stage 2 - never a context
rebased to the effect's declaring scope. `RoadieActiveBoost` is unimplementable from a
root-rebased context (no chapter on root's chain). This is a deliberate asymmetry with
12.4: conditions and action lists evaluate in their declaring scope, but a multiplier
formula is addressed to a number, and the number's identity includes the chain it resolves
on. Reads stay chain-only, so no sibling reach opens up. Gets its own comment in the
family base class.

**Scope attachment**: `ScopeDefinition` gains `producers`, `generators`, `upgrades`,
`careerEffects` lists - direct-reference lists exactly like `triggers` (declaration is
ownership; `[DefinitionId]` indirection is for cross-references). Duplicate-home checks
mirror the trigger pattern. `RoadieVenueDefinition` is not scope-attached - it names its
chapter by id.

## Resolution (Economy/Producer.cs, per the 12.13 layout)

Stateless static class:

- **Match rule**: an effect matches owner + coordinates when `target` equals the owner's
  id or any of its tags, and each optional coordinate set on the effect agrees
  (`currencyId` empty or equal, `stat` empty or equal).
- **`GetMultiplier(defs, originScope, owner, currencyId, stat)`** - walk originScope to
  root; at each scope gather effects from that scope's facts - `purchasedUpgrades`
  (upgrade effects), `activeModifiers` (modifier effects scaled by stacking: Replace = m,
  Linear = 1+(m-1)n, Multiply = m^n), and career effects declared on the scope (formulas
  computed against the origin context) - keeping matches. The product is BigNumber:
  authored multipliers are doubles, but the records term is unbounded.
- **Two-stage composition** for one number: `(base entries whose conditions hold, summed,
  * source-stage product from the source's declaring scope) * currency-stage product from
  the currency's home`. A currency-stage effect with no stat coordinate hits rate and
  yield both - what makes `records_income` reach the tap yield (walkthrough 13.2's
  1.4/press) while `tight_set`'s `stat: rate` narrowing leaves it alone.
- **`GetRate(subtreeRoot, defs, now, currencyId)`** - enumerate the subtree's scopes; for
  each declared producer (base entries) and generator (entries * ownedCount), sum matching
  `rate` entries with conditions judged in the declaring scope, apply both stages. This is
  what the step 7 tick and idle claim will consume; step 4 ships it as a pure function
  over an explicit subtree root, since "foreground chapter" does not exist yet.
- **`FireProducer(ctx, producerId)`** - rebase to the producer's declaring scope, resolve
  every `yield` entry against pre-fire state (conditions and amounts judged together,
  multipliers included), then deposit the resolved list. Atomicity test: an entry
  conditioned on `EarnedTotalAtLeast(cash, X)` where the firing itself crosses X - the
  sibling entry must not see it.

## Purchasing (Economy/Purchasing.cs - minor addition to the 12.13 layout)

One entry point, matching 12.11's `TryBuy(generator | upgrade)`:
`bool TryBuy(GameContext ctx, string definitionId)` - resolves the id (unique tree-wide)
and dispatches to private typed paths; an id resolving to neither type refuses with a log.

- Generator path: rebase to the declaring scope; fail-closed on `availableWhen`, on
  affordability at `CostAt(owned)`, and on a computed cost <= 0 (runtime backstop - the
  validation pass is dev-only, this check is what release builds execute); spend,
  increment the count at the declaring scope.
- Upgrade path: fail-closed on the gate, on the latch not already existing, and on
  affordability; spend, add the latch, run `actions` in the declaring scope.
- **`GameContext.TrySpend(currencyId, amount)`** - decrements the balance at the
  currency's home iff sufficient, never touches `earnedTotals` (spending is not earning;
  section 2's strobe-proofing depends on this).

## Validation extensions

- Id collection, tree-wide uniqueness, and duplicate-home checks extend to the four new
  scope lists (reuse `DuplicateId`/`DuplicateHome`); `RoadieVenueDefinition` ids join the
  global id space.
- Per-definition walk (new containers in tree order): produces entries -
  `RequireChainCurrency` from the declaring scope, entry conditions validated there, warn
  on a stat outside {rate, yield} (new check member `UnconsumedStat`); generator cost
  currency on chain, `availableWhen` validated when present; upgrade gate validated when
  present, cost currency on chain, `actions` through `ValidateActionList` (container key
  `upgrade:<id>`).
- **Implicit latch write**: before validating an upgrade's action list, record the
  purchase latch as a fact write at index -1 (home = declaring scope, same container key).
  The existing set-then-wiped finalizer then catches a payload that resets the latch's own
  scope - otherwise `purchase -> latch -> actions[0] ResetScope(declaring scope)` passes
  validation and yields a repeatably-purchasable upgrade. Index -1 collides with nothing:
  only actions record fact writes.
- **`NumericRange`** (new check member): NaN/infinity refused on every authored double,
  existing shapes included (`Effect.multiplier`, `growth`, `perRoadie`,
  `LinearOnBalance`'s coefficient, `RootCurveFormula.exponent`); negative produces values,
  costs, and effect multipliers are errors; zero multipliers legal (event handicaps are
  x0); `baseCost <= 0` on a generator is an error (generator purchases repeat - a free
  generator is an unbounded rate printer); zero upgrade cost legal (`cut_demo` is authored
  at 0); nonpositive growth an error; `perRoadie` and `LinearOnBalance` coefficients
  nonnegative; `cap` nonnegative.
- Upgrade action lists participate in set-then-wiped, reads-zeros, flag-setter tracking,
  modifier grant/remove ledgers, and cycle edges through the shared ledgers.
  **StrandedValue stays rung-only** - the existing finalizer filters to rung containers
  deliberately, per the doc bullet.
- `OwnedCountAtLeast` and `UpgradePurchased` get their promised Validate overrides:
  reference resolves, and the target's declaring scope sits on the acting chain (same
  shape as the FlagSet read check).
- Effect reach: `ValidateEffectReferences`/`ValidateEffectReach` generalize beyond
  modifiers to upgrade and career effect lists - reach measured from the declaring scope
  (statically, unlike modifiers' per-grant-site reach). Producer/generator ids become
  valid exact-source targets with the 12.12 rule: the effect's scope must be the target's
  declaring scope or an ancestor - error, not warning.
- `TagHasMemberInSubtree` extends membership to producers and generators (bars stay
  step 5).

## Save extensions (the step 2 incremental contract)

- `FilterToDeclared` drops unknown `generatorCounts` keys and `purchasedUpgrades` entries
  against the declaring scope's new lists (same pattern as trigger latches).
- `activeModifiers` entries filter against the definition source; entries with nonpositive
  stack counts are dropped. `IDefinitionSource` threads through the tree-application path
  where `FilterToDeclared` runs - not just the `LoadFromDisk` signature - and tests that
  deserialize directly exercise the same filter.
- The root allocation filter also drops nonpositive stationed values (alongside the
  existing not-a-chapter drop), making the formula-side clamp defense in depth rather than
  the only line.

## Files and tests

New: `Economy/ProducerDefinition.cs`, `GeneratorDefinition.cs`, `UpgradeDefinition.cs`,
`CareerEffectDefinition.cs` (+ MultiplierFormula family), `RoadieVenueDefinition.cs`,
`Producer.cs`, `Purchasing.cs`. Modified: `ScopeDefinition.cs` (four lists),
`GameContext.cs` (TrySpend), `Condition.cs` (two Validate overrides),
`ContentValidator.cs`, `SaveSystem.cs`.

Tests: `ResolutionTests` (match rule, coordinate narrowing, two-stage sibling isolation,
stacking math, ownedCount scaling, career formulas including active-chapter derivation and
allocation clamping, subtree rate summation), `FireProducerTests` (pre-fire atomicity,
conditioned entries, multiplier application), `PurchasingTests` (fail-closed gates
including null gates, dispatch including unknown-id refusal, cost curve, computed-cost
backstop, spend-not-earn, latch and payload execution, re-buy refusal), plus
ContentValidator extensions (new checks including zero/negative base cost, latch-reset
set-then-wiped, NaN/infinity on old and new doubles) and SaveSystem extensions. `TestTree`
grows Chapter-1-shaped producers/generators/upgrades. No production Chapter 1 assets in
step 4 - tests use fixtures; step 8's importer creates the authoritative assets.

## Docs on landing

12.6's career row names the authored shape (`CareerEffectDefinition` +
`MultiplierFormula`); 12.13's file list gains the new Economy files including
`Purchasing.cs`; 8.2 names `RoadieVenueDefinition` as the venue-scaling data home; build
plan step 10 rewords to allocation command, write-time validation, and UI (arithmetic
owned by step 4); step 4's status line updates when the loop is green.

## Verification

The headless loop (lockfile check first), per the build plan's per-step contract. Commit
waits for John's review.

## Deliberately not in step 4

Bars' participation in resolution beyond what exists (step 5), event handicaps and the
timed-buff row of 12.6 (steps 6-7, no BuffDefinition exists yet), reserved-target base
values and their consumers (`game_speed`/`idle_rate`/`idle_cap` resolve through the same
GetMultiplier, but their consumers are the step 7 tick), `SetRoadieAllocation` +
write-time cap enforcement (step 10), and the command boundary (step 7 - `FireProducer`/
`TryBuy` are fail-closed against their own gates now, foreground-subtree rejection layers
on in GameSession).
