# Step 7 Plan - Tick + GameSession

Design for build-plan step 7. Sections cited are `garage-band-idle-design.md`.

## What step 7 is

The runtime loop lands: the segmented tick with its fixed economy phases (12.9), the session that
owns the phase machine, the command boundary and the transaction pipeline (12.9), and idle
switch-in with the exactly-once claim (9, 12.9, 12.10's clock clamp). Two settled design changes
land in front of it: the wildcard-target rule with the one consumer-owned stat, `game_speed`
(12.2) - the idle numbers are deliberately not stats (fraction: an idle-only rate modifier; cap: a
`GameConfig` threshold) - and career effects folding into modifiers, which DELETES a family and
brings `appliesWhen` (conditional modifier application) with it. Everything after those two
slices is orchestration over what steps 1-6 shipped.

## Existing systems this uses unchanged

- **Rates**: `Producer.GetRate` - both stages, subtree explicit, built for exactly these two
  callers (the tick and the idle claim). One reshape in slice B: it takes a `GameContext`
  (carrying the walk root, the timestamp, and the circumstance) instead of loose scope + time.
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
- **State**: `pendingClaim`, `lastActiveUtc`, `timedBuffs` - all in the tree since steps 1-2,
  persisted and filtered by the save since step 2 (the reset re-stamp turns monotonic in slice E,
  the one touch). `BlocksIdle` - step 6 shipped the property; this step is what reads it.
- **Deposit at the home**: `GameContext.Deposit` - claim settlement and rate deposits alike.

## game_speed + the wildcard

`game_speed` is a STAT - one name joining `rate` and `yield` in `Stat`, the open vocabulary 12.2
built for exactly this. The vocabulary SPLITS by consumer, though: `rate` and `yield` are PRODUCED
stats, legal in `produces` entries; `game_speed` is an EFFECT-ADDRESS stat, legal only as an
effect's stat coordinate - one shared list would let `{cash, game_speed, 10}` validate as a
contribution nothing ever sums. It earns stat-hood because it scales a different quantity: rate
effects scale a number's units-per-second, `game_speed` scales the seconds, which is why it alone
reaches bar fills and why it alone carries a sale cap. The tick asks once per segment with
`stat: game_speed` and no owner - its dt has no per-target breakdown, so only untargeted effects
apply, and narrowing is done by PLACEMENT (a chapter-declared modifier applies to that chapter's
chain).

The two `Matches` changes (12.2's rules): **an empty `target` is the wildcard, "every currency"**,
applying at the currency stage - one stage per effect, because root sits on both gather walks and
a stage-less wildcard would be collected twice. And **the stat leg is exact and required** -
`effectStat == stat`, an empty effect-stat matches nothing, the fail-closed backstop behind the
load-time error. A stat-less effect claimed to answer questions of different kinds with one
factor; "rate and yield alike" authors as two entries, which 12.2 already prefers. So
`{stat: rate, x2}` speeds every currency's rate (and, like every currency-stage effect, never a
bar's fill), and an owner-less query matches wildcards only - by name. The old "empty target
matches nothing" rule and its validator warning flip; no flag and no stat classification exists
anywhere in the match path.

The idle numbers are NOT stats. The fraction is an ordinary rate effect: a root permanent modifier
(below) carrying `{stat: rate, x0.5}` with `appliesWhen: idle accumulation` (next section),
gathered by the same GetRate the tick uses, run under an idle-accumulation context - "rate but
idle" is a circumstance of one gather, never a second vocabulary. The cap and the minimum-away
threshold are thresholds in seconds, not multipliers, so both live in `GameConfig`; the authored
numbers (0.5, 14400s, 180s) are placeholders until tuning cares. The same mechanism gives
live-only buffs (a modifier that excuses itself from idle) and chapter-local idle tuning (an
idle-only modifier declared at one chapter) as ordinary authoring. game_speed's base is code's 1;
Encore and Overdrive carry it as wildcard effects (step 10).

## Careers fold into modifiers

`Effect` gains an optional formula: constant `multiplier` when absent, `formula.Compute(origin)`
when present - the evaluation-context rule careers already settled. With that:

- `ScopeDefinition.permanentModifiers` - a USAGE list, the parallel of an `AddModifier` grant
  minus the moment: each entry references a modifier declared in a `modifiers` list on the
  reachable chain (ownership stays where it is), and `MultiplierFor` reads it directly - nothing
  granted, nothing saved, reset-immune. Root's apply to everything; a chapter's apply exactly to
  its own chain, which is the chapter-unique-buff case. Permanent membership contributes an
  implicit application count of 1, MERGED with the scope's stored stacks for the same modifier
  and resolved through the modifier's own stacking kind - `Replace` means permanent-plus-granted
  is still one application, so the two paths can never double-apply outside the vocabulary.
- A career effect becomes a formula-shaped effect inside a permanent modifier on root - the
  Records curve, the roadie boost. `CareerEffectDefinition`, its declaration list, and its loop in
  `MultiplierFor` are deleted; its validation moves into effect validation.
- Every effect site (upgrades, granted modifiers, handicaps, cascades) gains formula capability -
  embraced as the rule rather than fenced off: "an effect's factor is a constant or a formula."
  Count scaling composes on the computed value.
- Monetization authoring falls out (all step 10): Encore is a root-declared modifier
  `{stat: game_speed, x2}` carried by a `TimedBuff` fact whose `expiresAtUtc` the ad callback
  extends; Backstage Pass the same shape minus expiry, or a plain root grant - plus its idle half,
  an idle-only modifier `{stat: rate, x2}` (`appliesWhen` below) and entitlement plumbing on the
  cap's config read. Only the facts and the reading of `timedBuffs` are step 10's; the authoring
  vocabulary is complete after this step.

## Conditional application (appliesWhen)

`ModifierDefinition` gains an optional `appliesWhen` condition, judged at gather time in
`MultiplierFor` against the ORIGIN context - the same evaluation-context ruling formulas follow.
Absent means always; a false condition skips the whole modifier (permanent membership and granted
stacks alike). Effects stay unconditional four-field atoms; the timing lives on the carrier.

The circumstance it exists to read: `GameContext` gains an `idleAccumulation` flag,
construction-scoped - the claim builds its contexts with it, everything else builds without, and
nothing mutates. A new condition kind (`IdleAccumulation`) returns it; composed with `Not`, a
live-only buff is ordinary authoring. `Producer.GetRate` reshapes from `(subtreeRoot, nowUtc,
currency)` to `(ctx, currency)` so the circumstance rides the context it already implies - no
function flags, and the tick, the claim, and tests all hand it the same object they hand
everything else.

The idle fraction is this mechanism's first content: a root permanent modifier
`{stat: rate, x0.5}` with `appliesWhen: IdleAccumulation`. The same two primitives - placement for
where, `appliesWhen` for when - make live-only buffs and chapter-local idle tuning ordinary
authoring.

Validation: `appliesWhen` validates as a condition wherever the modifier's addresses are judged -
per grant site, or the declaring scope when nothing grants it. An `IdleAccumulation` condition in
a site never evaluated under a claim's context (a gate, a trigger, a rung offer, an event goal) is
dead content and warns; the one entry site evaluated under both circumstances is a RATE entry on a
source some chapter's subtree contains, so one there is legal ("this line pays only while idle" is
coherent authoring). A yield condition is read only by live FireProducer calls, and a
root-declared source sits outside every chapter's idle walk, so those warn too.

## TickSystem

`TickSystem.Tick(RootScopeState root, ChapterScopeState foregroundChapter, GameConfig config,
double realSeconds, DateTime tickEndUtc)` - pure over its arguments, no clock read (the same reason `AdvanceTimers` and
`BarSystem` take timestamps), guarded like `AdvanceTimers`: null chapter or nonpositive dt no-ops.
The session calls it inside a transaction; the tick itself owns only the segments - the sweep and
the refresh are the transaction's.

**Segmentation** (12.9): boundaries are every expiry timestamp strictly inside the tick - for each
running timed record in the foreground subtree, segment start plus `remainingSeconds`; for every
`timedBuff` in the swept set, its `expiresAtUtc`. Sorted and deduplicated. Buffs contribute
boundaries only: nothing reads or removes a buff until step 10 lands the `timedBuffs` gather row.

Per segment [a, b]:

1. `game_speed` read at a, CLAMPED at the consumer: `effDt = (b - a) * clamp(GetMultiplier(stat:
   game_speed), 1, GameConfig.maxGameSpeed)` at the foreground chapter's context. Section 9
   describes the caps but nothing else enforces one - unclamped authoring could stall time (a x0
   wildcard) or stack carriers past Overdrive - so the sole consumer clamps, ceiling in the config
   (4 today). The floor of 1 also forbids an authored slow-time mechanic; nothing designs one, and
   it is one constant if that ever changes. A buff live at segment start governs the whole segment
   (12.9). `effDt` stays a double - `ConsumeAndSettle` and the decrement take doubles, and the
   clamp bounds it.
2. Bar demand: `ResolveDemand(foregroundChapter, a)` - BEFORE the deposits, per the snapshot rule.
3. Rate production: enumerate the unique `(currency, home)` pairs the subtree's sources pay at
   `Stat.Rate` (a downward walk over `produces` entries, tree order then declaration order, the
   home resolved from a contributing scope's chain exactly as `GetRate` resolves it - a sibling of
   `GetRate`, beside it in `Producer`), size EVERY amount as `GetRate(liveCtx, currency) * effDt`
   against pre-deposit state (the context: foreground chapter, segment start, no idle
   circumstance), then deposit at the pair's home -
   `FireProducer`'s two-pass discipline, which is what "definition order never changes production"
   costs. The pair is the point: the claim path retains the same home reference, so neither
   consumer looks anything up twice.
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
ScriptableObject (`minimumAwaySeconds`, `idleCapSeconds`, `maxGameSpeed`, and whatever global knobs join it later) that tests build
inline and step 8's `GameManager` references as the real asset. The session is never serialized
and holds only orchestration: `{foregroundChapter, phase, commandInProgress}` plus one refresh
event (the 12.11 hook; step 9 subscribes).

- **Phases**: `NoChapter | AwaitingIdleClaim | Live`. Launch and backgrounding are `NoChapter`.
  `AwaitingIdleClaim` admits only `ClaimIdle` and `SwitchChapter`, and does not tick - the chapter
  is live only after the claim settles. `Live` admits everything.
- **The current chapter is a durable root fact**, not session state: switching INTO a chapter
  writes its id at root, and backgrounding leaves it - the fact names where play left off, which
  is where boot returns. Step 8's `GameManager` reads it at launch and auto-enters that chapter,
  offering no selection; only a save with no recorded chapter (a fresh game) shows a chapter
  select, and such a save holds no pending claim to conflict with. This is what makes an unsettled
  claim unstrandable: every load that carries one lands on the chapter that owns it, where it
  re-offers.
- **The transaction pipeline**, fixed for ticks and commands alike (12.9/12.11): guards (phase,
  boundary, `commandInProgress`) - mutation - `Sweep.Run(root, foregroundChapter, nowUtc)` -
  commit - one refresh. The sweep is CONDITIONAL on the resulting phase: only a transaction
  ending in `Live` sweeps. Ending in `AwaitingIdleClaim` or `NoChapter` commits and refreshes
  without sweeping - a callback answered mid-dialog (12.9's phase-eligible contract) or a
  backgrounding that keeps a claim must not open the chapter to a trigger's reset - and the
  transaction that enters `Live` performs the deferred sweep. The refresh IS unconditional, which
  is what repaints the dialog when a callback marks the claim doubled. Commit is a seam, not
  machinery: every refusal precedes any mutation, so
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

Every `lastActiveUtc` write is MONOTONIC - `max(lastActiveUtc, nowUtc)` - at all three stamp
sites: switch-away, backgrounding, and the reset re-stamp in `ChapterScopeState.Clear` (step 8's
save-stamp inherits the same rule). The 12.10 clamp alone is not enough: clamping the read but
stamping a rolled-back clock into state mints the difference the moment the clock recovers. A
monotonic stamp may under-pay across a rollback; it can never pay for time that did not pass.

**`SwitchChapter(chapter, nowUtc)`** - the session command, legal in every phase, one transaction:

1. Switching to the CURRENT chapter (or to null while already `NoChapter`) is a no-op success -
   nothing stamps, nothing recomputes. The stamp is old during a live session (written at the last
   switch-away), so recomputing here would mint a claim covering time the player spent playing.
2. When switching INTO another chapter, the outgoing one settles first: an unsettled
   `pendingClaim` deposits at its undoubled value (switching is an exit path, 9/12.9). Either way
   the outgoing chapter stamps.
3. A null incoming chapter is backgrounding: the outgoing chapter STAMPS (12.9: "stamped on
   switch-away" - skipping it would pay idle for the live session on return), the unsettled claim
   STAYS to re-offer so backgrounding and an app kill behave identically, and phase goes
   `NoChapter`. Settle-first is a chapter-to-chapter rule only.
4. The incoming chapter: an unsettled claim that survived an app kill re-offers as it stands -
   same `claimId`, nothing recomputed. Otherwise `elapsed = max(0, nowUtc - lastActiveUtc)` (the
   12.10 clamp - a backwards clock claims nothing), and the claim is skipped entirely - straight
   to `Live` - when elapsed is under `GameConfig.minimumAwaySeconds`, when any record in the
   chapter's subtree is for an event whose `BlocksIdle` holds, or when every line computes zero.
   Otherwise: `pendingClaim = {claimId: fresh guid, amounts}` with one line per rate-paid currency
   - `GetRate(idleCtx, currency) * min(elapsed, GameConfig.idleCapSeconds)` at current state, so
   Records earned while away boost it - where `idleCtx` is the chapter's context constructed with
   the idle-accumulation circumstance, so the x0.5 root base joins the gather and live-only
   modifiers excuse themselves. Each line serializes its home's scope id and RETAINS the home reference
   from the enumeration's `(currency, home)` pair, transient beside the id - no second lookup;
   phase goes `AwaitingIdleClaim`.
5. The closing sweep follows the pipeline's GENERAL rule - only a transaction ending in `Live`
   sweeps. Entering `Live` directly: root plus the new foreground, the "first live sweep after
   switch-in" (12.8). Entering `AwaitingIdleClaim`: no sweep - a root trigger can legally carry
   `ResetScope` against a descendant chapter (`FindInSubtree` reach), so even a root-only sweep
   could swap `ChapterFacts` and destroy the claim before it is presented, the exact write
   `AwaitingIdleClaim` exists to forbid (12.9). The rule is phase-derived, not command-paired:
   a callback marking the claim `doubled` is a legal transaction while the dialog is up and it
   does not sweep either.

**`ClaimIdle(nowUtc)`** - `AwaitingIdleClaim` only. Each line deposits at its RETAINED home
reference, doubled lines at x2 (the `doubled` flag is written by step 10's ad callback; the
Backstage Pass pre-double is an authored idle-only modifier). Nothing resolves a name at
settlement - requirement 8's downward walk is aggregation, not resolution - so references attach
at the two boundaries instead: creation retains them, and the save's load path, which already owns
claim-id resolution privately (12.3), reattaches them to the claim it rehydrates. Then `settled`
flips and the claim object STAYS - a settled survivor is what makes replay after an app kill
idempotent: it never re-offers and never re-deposits. Phase goes `Live`, and this transaction's
sweep - root plus the now-live foreground - is the deferred one: a threshold crossed while away,
by the switch's own settle-out, or by this deposit fires here, root triggers included.

`lastActiveUtc` on save (foreground only, 12.9, monotonic like every stamp) is one line at the
save call site, and no save call site exists until `GameManager` - it lands in step 8 with the
caller.

## Validation

- The stat vocabulary splits in `Stat`: produced stats (`rate`, `yield`) are what
  `ValidateProducesEntries` accepts; `game_speed` is legal in an effect's stat coordinate only.
  One shared `IsConsumed` would legalize inert contributions.
- The consumer's query shape is validated per stat: a `game_speed` effect is matched by an
  ownerless, currencyless query, so a target or currency narrowing on one is dead content (warn),
  and its usage scope must be a chapter or the root - the tick gathers from the foreground
  chapter outward, so a placement below chapter level is never visited (warn).
- The "empty target matches nothing" warning flips: an empty target is the wildcard, and what is
  checked instead is currency-stage reach - a wildcard is collected on home-to-root walks only,
  so its usage scope's subtree must home at least one currency its narrowing selects (any
  currency when unnarrowed) that some source pays with the authored stat, since the currency
  stage never runs for an unpaid pair; the ancestor-homed currencies `MatchNarrowingCurrency`
  accepts belong to source-targeted effects, which a wildcard is not (warn).
- An effect's empty STAT is an error - the coordinate is required now that matching is exact.
  Existing fixtures and authored content that leaned on stat-less effects gain explicit stats
  (one entry per stat they meant).
- Career-effect checks move into effect validation with the formula field (non-null formula
  validated where a career's formula was; constant path validated as today).
- `permanentModifiers` validates as USAGE, never ownership: each entry must reference a modifier
  declared on the chain reachable from the usage scope - the same reach a grant gets - and appear
  once per list, since membership is one implicit application and a duplicate entry would
  double-apply outside the stacking vocabulary (error); its effects are validated from the usage
  scope. It joins no id collection and no `RecordHome`: a
  modifier both declared and permanently applied at one scope is the normal case, not a
  `DuplicateHome`. Neither list has, or gains, a declared-but-unused warn; `RemoveWithoutGrant`
  stays the only modifier-shaped finding.
- `appliesWhen` validates as a condition at the same sites the modifier's addresses are judged
  (per grant site, declaring scope when ungranted); an `IdleAccumulation` condition in a site
  never evaluated under a claim's context (gate, trigger, rung offer, event goal) is dead content
  (warn); it is legal on a rate entry some chapter's subtree contains, and dead on a yield entry
  or a root-declared source's entries (warn).
- `GameConfig` is fail-loud at its consumers (requirement 7): a null config, a non-finite or
  sub-1 `maxGameSpeed`, or a non-finite or negative `minimumAwaySeconds` or `idleCapSeconds`
  throws - at `TickSystem.Tick` for direct use and at session construction - never a silent clamp
  of the clamp.

## Save

No version bump - pre-ship formats revise in place (12.10). One new field: the current-chapter id
at root, written by `SwitchChapter`; the unknown-id filter clears it when content no longer
authors that chapter. Everything else already persists: `pendingClaim`, `lastActiveUtc` and
`timedBuffs` since step 2, careers were stateless, and permanent modifiers are declaration, not
state. One load addition: the claim resolution the save already owns privately now also attaches
each surviving line's transient home reference, so settlement never resolves a name
(requirement 8).

## Tests

Wildcard + stats (`ResolutionTests`, `ContentValidatorTests`): a root wildcard `{stat: rate, x2}`
doubles every currency's rate and is collected once, not once per stage; it never reaches a bar's
fill rate; `{currencyId: cash, stat: rate}` narrows to one currency; an owner-less query
matches wildcards only; the stat is exact - a stat-less effect is a load error and matches
nothing at runtime, so a `{target: cash, x2}`-shaped effect can never answer a question of a kind
its author never chose; the validator rows above.

Careers fold (`ResolutionTests`, `ContentValidatorTests`): the converted Records-curve and roadie
rows produce the same numbers as permanent modifiers; a formula effect inside a granted modifier
computes against the origin; a chapter's permanent modifier applies on its own chain and not a
sibling's; reset does not touch it; a modifier both permanent and granted at one scope resolves
through its own stacking kind - `Replace` stays one application, `Linear`/`Multiply` count the
implicit 1 plus the stacks; a permanent entry referencing a modifier declared nowhere on the
chain is a reach error, and one both declared and permanently applied at the same scope produces
no finding.

appliesWhen (`ResolutionTests`, `ContentValidatorTests`): a modifier with
`appliesWhen: IdleAccumulation` contributes under an idle-accumulation context and not under a
live one, permanent membership and granted stacks alike; a live-only modifier
(`Not(IdleAccumulation)`) inverts that; absent applies always; the validator rows above (dead
sites warn, chapter-reachable rate entries legal, yield and root-source entries warn).

`TickSystemTests`: rate deposits land at their homes scaled by dt, two currencies at two homes;
sizing against pre-deposit state (a rate entry conditioned on a threshold this segment's own
deposit crosses contributes nothing this segment); production before consumption (an empty pool fed
at +1/sec serves a 1/sec bar the same tick); demand before deposits (a bar gate this segment's
deposits open draws nothing until the next tick); `game_speed` scales production and bar fill while
the event timer decrement stays real, and the clamp holds at both bounds (a x0 wildcard still runs
at x1, stacked carriers cap at `maxGameSpeed`); segmentation - a goal first met after the expiry timestamp
inside one tick never latches, met before it latches, and the boundary tie latches; a buff expiry
inside the tick is a boundary, asserted through the latch moment it creates; zero and negative dt
no-op; a null or invalid `GameConfig` throws.

`GameSessionTests`: the phase table - each command kind in each phase; the boundary - an acting
scope in a dormant chapter refused, the foreground subtree allowed; the pipeline - a command's own
mutation arms a trigger and it fires inside the same transaction, exactly one refresh per completed
transaction, none on a refusal; a transaction ending in `AwaitingIdleClaim` or `NoChapter`
commits and refreshes without sweeping, and the one entering `Live` performs the deferred sweep;
reentrancy throws; an invalid `GameConfig` refuses construction.

Idle: switch-away stamps and settles undoubled; backgrounding stamps and keeps the claim; a
same-chapter switch is a no-op that neither stamps nor recomputes; rollback recovery - a stamp
attempted at a rolled-back clock preserves the newer timestamp at all three sites (switch-away,
backgrounding, reset re-stamp), and the recovered clock pays no phantom idle; switch-in computes
`GetRate(idleCtx) x min(elapsed, idleCapSeconds)` with the authored root base joining the gather
and a live-only modifier contributing nothing to it; the negative clock claims nothing;
below minimum-away skips; a blocking record skips; an unsettled survivor re-offers by `claimId`
and a settled one does not; neither a chapter trigger nor a ROOT trigger carrying a reset fires
during the switch that creates a claim - no sweep runs, the claim survives to present, and both
fire on the claim's own sweep; `ClaimIdle` deposits through the retained references (x2 when
doubled), a
LOADED claim settles the same way through the references the save attached, `settled` flips, and
replay no-ops; claim lines reach a tier-homed currency and a root-homed one; a currency-narrowed
idle-only effect scales only its currency's line, and a chapter-declared idle-only modifier only
its own chapter's claim; switching into a chapter records it as the
root current-chapter fact and backgrounding leaves the record; a recorded chapter no content
authors is dropped at load.

## Not in step 7

The `timedBuffs` gather row, Encore, the ad/store callbacks and the callback queue, the
entitlement facts, `SetRoadieAllocation`, `AcknowledgeStory` - step 10. `GameManager`, the load
and save call sites (with the stamp-on-save line), the boot auto-enter of the recorded chapter,
the tick's driver - step 8. The UI and the refresh subscribers - step 9.

## Docs on landing

The stat corrections (9, 12.2, 12.9, the appendix), the wildcard rule, and the idle respell
(fraction as an idle-only modifier via `appliesWhen`, cap and threshold in `GameConfig`) are
already in the design doc - they were decisions. Left for landing, since they describe code: the
careers deletion (12.2's career mentions, 12.6's career row, 8.2's formula home, 12.13's
`CareerEffectDefinition.cs` line, and the chapter-01 doc's career section restated as permanent
modifiers), `permanentModifiers` in 12.3's declaration list, and the build-plan status line.

## Landing order

Six changesets, each compiling and green on its own.

- **A. The wildcard + `game_speed`** - **LANDED 2026-08-26, 339/339, then revised 2026-08-27**:
  the two `Matches` rules (wildcard target, exact required stat), `Stat` gains `game_speed` and
  the produced/address split, the validator flips (empty target legal, empty stat an error), the
  audit of existing fixtures for stat-less effects, `ResolutionTests` and `ContentValidatorTests`
  rows. (The idle stats it briefly carried were removed when the idle respell was decided.)
- **B. Careers fold + appliesWhen** - **LANDED 2026-08-26, 356/356**: `Effect.formula`,
  `permanentModifiers`, `CareerEffectDefinition` deleted (the `MultiplierFormula` family survives
  in its own file), the converted content and tests, the career-describing doc
  edits; `ModifierDefinition.appliesWhen`, the `GameContext` idle-accumulation circumstance, the
  `IdleAccumulation` condition kind, and the `GetRate(ctx, currency)` reshape, plus their tests -
  the mechanism lands here beside the other modifier-shape work, so E only consumes it.
- **C. `TickSystem`** - **LANDED 2026-08-26, 368/368**: the segments and phases, plus `GameConfig`
  (born here for `maxGameSpeed`; `minimumAwaySeconds` and `idleCapSeconds` wait for their
  consumer), plus `TickSystemTests` - called directly, no session yet. The `(currency, home)` pair
  enumeration landed as `Producer.RatePairs`, GetRate's sibling.
- **D. `GameSession` core** - phases, the boundary, the pipeline, the command wrappers, `Tick`,
  plus `GameSessionTests`.
- **E. Idle** - `SwitchChapter`, `ClaimIdle`, the monotonic stamps (the reset re-stamp included),
  the current-chapter root fact and its save filter, minimum-away and cap read from `GameConfig`,
  the `BlocksIdle` read, the retained claim references (creation and load), the authored idle-only
  root base in `TestContent`, plus the idle tests.
- **F. Docs on landing** - the remaining edits above, the build-plan status line. 12.13 already
  lists `GameSession.cs` and `TickSystem.cs`.
