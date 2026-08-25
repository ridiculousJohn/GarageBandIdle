# Step 7 Plan - Tick + GameSession

Design for build-plan step 7. Sections cited are `garage-band-idle-design.md`.

## What step 7 is

The runtime loop lands: the segmented tick with its fixed economy phases (12.9), the session that
owns the phase machine, the command boundary and the transaction pipeline (12.9), and idle
switch-in with the exactly-once claim (9, 12.9, 12.10's clock clamp). Two settled design changes
land in front of it: the wildcard-target rule with the three consumer-owned stats (12.2), and
career effects folding into modifiers - which DELETES a family. Everything after those two slices
is orchestration over what steps 1-6 shipped.

## Existing systems this uses unchanged

- **Rates**: `Producer.GetRate(subtreeRoot, nowUtc, currency)` - both stages, subtree explicit,
  built for exactly these two callers (the tick and the idle claim).
- **Bars**: `BarSystem.ResolveDemand` / `ConsumeAndSettle` - already split at 12.9's phase seam,
  demand from the start-of-segment snapshot, balance read live, settlement stamped at the segment's
  real end, scaled and real time as separate arguments.
- **Event timers**: `EventSystem.AdvanceTimers(root, foregroundChapter, realSeconds,
  segmentEndUtc)` - the swept set, latch before decrement, all inside. Timers burn real seconds;
  `game_speed` never touches them.
- **The sweep**: `Sweep.Run(root, foregroundChapter, nowUtc)` - the missing caller arrives.
- **The command surface being wrapped**: `Rung.TryExecute`, `Purchasing.TryBuy` (generator and
  upgrade), `Producer.FireProducer`, `BarSystem.SetActiveBars`, `EventSystem.TryStart` /
  `TryDismiss`. None change; the boundary is orchestration and lives in the session (12.9).
- **State**: `pendingClaim`, `lastActiveUtc` (and its reset re-stamp), `timedBuffs` - all in the
  tree since steps 1-2, persisted and filtered by the save since step 2. `BlocksIdle` - step 6
  shipped the property; this step is what reads it.
- **Deposit at the home**: `GameContext.Deposit` - claim settlement and rate deposits alike.

## Consumer-owned stats + the wildcard

`idle_rate`, `idle_cap` and `game_speed` are STATS - three names joining `rate` and `yield` in
`Stat`, which is the open vocabulary 12.2 built for exactly this. No anchor assets, no reserved
machinery, no new address space: the consumer that owns the number supplies the owner in its own
query, per user, inside a loop it already runs.

- The idle claim iterates the chapter's currencies and asks each one's gather with the currency as
  owner and `stat: idle_rate` (and `idle_cap`) - the same currency-stage read production does for
  `rate`, so per-currency idle tuning is free authoring.
- The tick asks once per segment with `stat: game_speed` and no owner - its dt has no per-target
  breakdown, so only untargeted effects apply, and narrowing is done by PLACEMENT (a
  chapter-declared modifier applies to that chapter's chain).

The one `Matches` change (12.2's rule): **an empty `target` is the wildcard, "every currency"**,
applying at the currency stage - one stage per effect, because root sits on both gather walks and
a stage-less wildcard would be collected twice. So `{stat: rate, x2}` speeds every currency's rate
(and, like every currency-stage effect, never a bar's fill), `{stat: idle_rate, x0.5}` is the idle
base itself, and an owner-less query matches wildcards only. The old "empty target matches
nothing" rule and its validator warning flip.

Bases are authored, not constants: code's base is 1, and a root permanent modifier (below)
carries `{stat: idle_rate, x0.5}` and `{stat: idle_cap, x14400}` - the cap's "multiplier" is its
second count, over base 1. The minimum-away threshold is not a stat - a threshold, not a
multiplier - so it lives in `GameConfig` (below); the authored number is a placeholder until
tuning cares.

## Careers fold into modifiers

`Effect` gains an optional formula: constant `multiplier` when absent, `formula.Compute(origin)`
when present - the evaluation-context rule careers already settled. With that:

- `ScopeDefinition.permanentModifiers` - a second modifier list, read straight from the
  declaration by `MultiplierFor`: no grant, no stack, nothing saved, reset-immune. Root's apply to
  everything; a chapter's apply exactly to its own chain, which is the chapter-unique-buff case.
  `stacking` is meaningless here and ignored.
- A career effect becomes a formula-shaped effect inside a permanent modifier on root - the
  Records curve, the roadie boost. `CareerEffectDefinition`, its declaration list, and its loop in
  `MultiplierFor` are deleted; its validation moves into effect validation.
- Every effect site (upgrades, granted modifiers, handicaps, cascades) gains formula capability -
  embraced as the rule rather than fenced off: "an effect's factor is a constant or a formula."
  Count scaling composes on the computed value.
- Monetization authoring falls out (all step 10): Encore is a root-declared modifier
  `{stat: game_speed, x2}` carried by a `TimedBuff` fact whose `expiresAtUtc` the ad callback
  extends; Backstage Pass the same shape minus expiry, or a plain root grant. Only the facts and
  the reading of `timedBuffs` are step 10's; the authoring vocabulary is complete after this step.

## TickSystem

`TickSystem.Tick(RootScopeState root, ChapterScopeState foregroundChapter, double realSeconds,
DateTime tickEndUtc)` - pure over its arguments, no clock read (the same reason `AdvanceTimers` and
`BarSystem` take timestamps), guarded like `AdvanceTimers`: null chapter or nonpositive dt no-ops.
The session calls it inside a transaction; the tick itself owns only the segments - the sweep and
the refresh are the transaction's.

**Segmentation** (12.9): boundaries are every expiry timestamp strictly inside the tick - for each
running timed record in the foreground subtree, segment start plus `remainingSeconds`; for every
`timedBuff` in the swept set, its `expiresAtUtc`. Sorted and deduplicated. Buffs contribute
boundaries only: nothing reads or removes a buff until step 10 lands the `timedBuffs` gather row.

Per segment [a, b]:

1. `game_speed` read at a: `effDt = (b - a) * GetMultiplier(stat: game_speed)` at the foreground
   chapter's context. A buff live at segment start governs the whole segment (12.9). `effDt` stays
   a double - `ConsumeAndSettle` and the decrement take doubles, and `game_speed` is x1 to x4
   capped (9).
2. Bar demand: `ResolveDemand(foregroundChapter, a)` - BEFORE the deposits, per the snapshot rule.
3. Rate production: enumerate the currencies the subtree's sources pay at `Stat.Rate` (a downward
   walk over `produces` entries, tree order then declaration order - a sibling of `GetRate`, beside
   it in `Producer`), size EVERY amount as `GetRate(foregroundChapter, a, currency) * effDt`
   against pre-deposit state, then deposit - `FireProducer`'s two-pass discipline, which is what
   "definition order never changes production" costs.
4. `ConsumeAndSettle(demand, effDt, b)` - bar fill rides scaled time (9), settlement stamps the
   segment's real end.
5. `AdvanceTimers(root, foregroundChapter, b - a, b)` - wall clocks burn real seconds, never
   scaled.

Why an event expiry is a boundary when handicaps ride on the record existing: the latch. "A goal
first met after expiry never latches" holds at sub-tick precision only if the expiry is a segment
edge - the post-expiry segment's `AdvanceTimers` sees `remainingSeconds` already zero and refuses,
while the pre-expiry segment's latch-before-decrement gives the boundary tie to the player.

## GameSession

Plain C#, no MonoBehaviour, constructed over the root and a `GameConfig` - a small settings
ScriptableObject (`minimumAwaySeconds`, and whatever global knobs join it later) that tests build
inline and step 8's `GameManager` references as the real asset. The session is never serialized
and holds only orchestration: `{foregroundChapter, phase, commandInProgress}` plus one refresh
event (the 12.11 hook; step 9 subscribes).

- **Phases**: `NoChapter | AwaitingIdleClaim | Live`. Launch and backgrounding are `NoChapter`.
  `AwaitingIdleClaim` admits only `ClaimIdle` and `SwitchChapter`, and does not tick - the chapter
  is live only after the claim settles. `Live` admits everything.
- **The transaction pipeline**, fixed for ticks and commands alike (12.9/12.11): guards (phase,
  boundary, `commandInProgress`) - mutation - `Sweep.Run(root, foregroundChapter, nowUtc)` -
  commit - one refresh. Commit is a seam, not machinery: every refusal precedes any mutation, so
  there is nothing to roll back, and commit is simply the point after the sweep where the
  transaction's state is what refresh reads. A refused command runs no pipeline - nothing mutated,
  nothing to sweep or repaint.
- **`commandInProgress`** is the reentrancy guard: a command issued from inside a transaction (a
  trigger action, a refresh handler) is a code bug and throws. The callback queue 12.9 describes is
  this flag's future consumer and lands with the callbacks (step 10).
- **The command surface**: one wrapper per entry point above, taking the same `GameContext` the
  wrapped system takes plus nothing new. The wrapper checks the phase, checks the boundary - the
  acting scope's chain must pass through the foreground chapter - delegates, and runs the pipeline
  when the mutation happened. The wrapped systems stay public and unchanged; tests keep calling
  them directly. Root-owned commands (`SetRoadieAllocation`, `AcknowledgeStory`) are step 10's and
  take the exception path 12.9 names.
- **`Tick(realSeconds, nowUtc)`**: `Live` only; `TickSystem.Tick` inside the same pipeline.
  Nonpositive dt no-ops, which is what a backwards mid-session clock produces when the driver diffs
  DateTimes.

## Idle

**`SwitchChapter(chapter, nowUtc)`** - the session command, legal in every phase, one transaction:

1. When switching INTO another chapter, the outgoing one settles first: an unsettled
   `pendingClaim` deposits at its undoubled value (switching is an exit path, 9/12.9). Either way
   the outgoing chapter stamps `lastActiveUtc = nowUtc`.
2. A null incoming chapter is backgrounding: phase goes `NoChapter` and that is all - an unsettled
   claim STAYS to re-offer on return, so backgrounding and an app kill behave identically.
   Settle-first is a chapter-to-chapter rule only.
3. The incoming chapter: an unsettled claim that survived an app kill re-offers as it stands -
   same `claimId`, nothing recomputed. Otherwise `elapsed = max(0, nowUtc - lastActiveUtc)` (the
   12.10 clamp - a backwards clock claims nothing and mints nothing), and the claim is skipped
   entirely - straight to `Live` - when elapsed is under `GameConfig.minimumAwaySeconds`, when any
   record in the chapter's subtree is for an event whose `BlocksIdle` holds, or when every line
   computes zero. Otherwise: `pendingClaim = {claimId: fresh guid, amounts}` with one line per
   rate-paid currency - `GetRate(chapter, nowUtc, currency) * min(elapsed, cap) * idleRate` at
   current state, so Records earned while away boost it - where `idleRate` and `cap` are that
   currency's `idle_rate` / `idle_cap` gathers; each line stamped with its home's scope id; phase
   goes `AwaitingIdleClaim`.
4. The sweep at the end runs against the NEW foreground, which IS "the first sweep after
   switch-in" where a threshold crossed while away fires (12.8).

**`ClaimIdle(nowUtc)`** - `AwaitingIdleClaim` only. Each line deposits at its named home, doubled
lines at x2 (the `doubled` flag is written by step 10's ad callback; the Backstage Pass pre-double
is an authored `idle_rate` effect). The line's scope id resolves downward through the chapter's
subtree, else outward along its chain - the two sanctioned walks, keyed by a stored fact id exactly
as `modifierStacks` resolves its modifier, kept private to settlement the way the save keeps its
own. Then `settled` flips and the claim object STAYS - a settled survivor is what makes replay
after an app kill idempotent: it never re-offers and never re-deposits. Phase goes `Live`; the
transaction sweeps, so a threshold the deposit crosses fires here.

`lastActiveUtc` on save (foreground only, 12.9) is one line at the save call site, and no save call
site exists until `GameManager` - it lands in step 8 with the caller.

## Validation

- The three names join `Stat.IsConsumed`, so stat narrowing to them already validates.
- The "empty target matches nothing" warning flips: an empty target is the wildcard, and the
  coordinate-pairing check adapts (a wildcard pairs with any currency/stat narrowing that names
  something).
- Career-effect checks move into effect validation with the formula field (non-null formula
  validated where a career's formula was; constant path validated as today).
- Permanent modifiers: effects validated from the declaring scope like upgrade effects; membership
  in `permanentModifiers` counts as usage for the orphan-modifier warn.

## Save

Nothing. No schema change, no new filter: `pendingClaim`, `lastActiveUtc` and `timedBuffs` have
persisted since step 2, careers were stateless, and permanent modifiers are declaration, not
state.

## Tests

Wildcard + stats (`ResolutionTests`, `ContentValidatorTests`): a root wildcard `{stat: rate, x2}`
doubles every currency's rate and is collected once, not once per stage; it never reaches a bar's
fill rate; `{currencyId: cash, stat: idle_rate}` narrows to one currency; an owner-less query
matches wildcards only; the validator rows above.

Careers fold (`ResolutionTests`): the converted Records-curve and roadie rows produce the same
numbers as permanent modifiers; a formula effect inside a granted modifier computes against the
origin; a chapter's permanent modifier applies on its own chain and not a sibling's; reset does
not touch it.

`TickSystemTests`: rate deposits land at their homes scaled by dt, two currencies at two homes;
sizing against pre-deposit state (a rate entry conditioned on a threshold this segment's own
deposit crosses contributes nothing this segment); production before consumption (an empty pool fed
at +1/sec serves a 1/sec bar the same tick); demand before deposits (a bar gate this segment's
deposits open draws nothing until the next tick); `game_speed` scales production and bar fill while
the event timer decrement stays real; segmentation - a goal first met after the expiry timestamp
inside one tick never latches, met before it latches, and the boundary tie latches; a buff expiry
inside the tick is a boundary, asserted through the latch moment it creates; zero and negative dt
no-op.

`GameSessionTests`: the phase table - each command kind in each phase; the boundary - an acting
scope in a dormant chapter refused, the foreground subtree allowed; the pipeline - a command's own
mutation arms a trigger and it fires inside the same transaction, exactly one refresh per completed
transaction, none on a refusal; reentrancy throws.

Idle: switch-away stamps and settles undoubled; switch-in computes `rate x min(elapsed, cap) x
idleRate` with the authored bases; the negative clock claims nothing; below minimum-away skips; a
blocking record skips; an unsettled survivor re-offers by `claimId` and a settled one does not;
`ClaimIdle` deposits (x2 when doubled), flips `settled`, and replay no-ops; claim lines reach a
tier-homed currency downward and a root-homed one outward; a per-currency `idle_rate` effect
scales only its currency's line; backgrounding keeps the claim.

## Not in step 7

The `timedBuffs` gather row, Encore, the ad/store callbacks and the callback queue, the
entitlement facts, `SetRoadieAllocation`, `AcknowledgeStory` - step 10. `GameManager`, the load
and save call sites (with the stamp-on-save line), the tick's driver - step 8. The UI, the refresh
subscribers, the launch-reopen preference - step 9.

## Docs on landing

The stat corrections (9, 12.2, 12.9, the appendix) and the wildcard rule are already in the design
doc - they were decisions. Left for landing, since they describe code: the careers deletion (12.2's
career mentions, 12.6's career row, 8.2's formula home, 12.13's `CareerEffectDefinition.cs` line,
and the chapter-01 doc's career section restated as permanent modifiers), `permanentModifiers` in
12.3's declaration list, and the build-plan status line.

## Landing order

Six changesets, each compiling and green on its own.

- **A. The wildcard + the three stats** - the `Matches` rule with its currency-stage assignment,
  `Stat` gains the names, the validator flip, `ResolutionTests` and `ContentValidatorTests` rows.
- **B. Careers fold** - `Effect.formula`, `permanentModifiers`, `CareerEffectDefinition` deleted,
  the converted content and tests, the career-describing doc edits.
- **C. `TickSystem`** plus `TickSystemTests`, called directly - no session yet.
- **D. `GameSession` core** - phases, the boundary, the pipeline, the command wrappers, `Tick`,
  plus `GameSessionTests`.
- **E. Idle** - `SwitchChapter`, `ClaimIdle`, the clamp, `GameConfig` with minimum-away, the
  `BlocksIdle` read, claim resolution, the authored bases in `TestContent`, plus the idle tests.
- **F. Docs on landing** - the remaining edits above, the build-plan status line. 12.13 already
  lists `GameSession.cs` and `TickSystem.cs`.
