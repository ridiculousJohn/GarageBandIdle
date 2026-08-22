# Step 5 Plan - Bars

Implementation plan for build-plan step 5. `build-plan.md` owns order and status; this doc owns
step 5's design decisions and work list. Section references are to `garage-band-idle-design.md`.

## Conceptual model

Bars are the only place in the economy that CONSUMES. Everything through step 4 answers "what does
this pay"; a bar answers "what does this drink, and what happens when it is full". Three numbers per
tick segment, in this order:

1. **Demand** - each active, available, unfilled bar wants `fillRate` units per second. That rate is
   an ordinary produced number, so it goes through `GetMultiplier`.
2. **Draw** - each bar takes `min(rate * dt, balance)` from the currency IT names, in declaration
   order. The balance is read live per bar, so an earlier draw is visible to a later one and an
   empty pool stalls the rest. There are no totals, so nothing is divided (12.7).
3. **Settlement** - progress crossings fire `onComplete`, iteratively for repeating bars, in one
   deterministic order across the whole subtree.

Compute-on-read holds throughout: the only bar facts are `barProgress`, `fillCounts`, and
`activeBars`, all already in `ScopeFacts` since step 1. Completion is derived from progress, never
stored. Step 5 ships stateless functions over an explicit subtree root and a dt, exactly like step
4's `GetRate`; the tick that calls them in phase order is step 7's. Demand and draw are two SEPARATE
calls because the phases straddle the production deposits - see the segment section.

## Shapes

**A group owns its bars.** `ScopeDefinition.barGroups` and `BarGroupDefinition.bars` LANDED with the
2026-08-20 reference pass, along with `BarsCompleted` holding a group reference rather than an id;
what remains for step 5 is the fill and settlement behaviour. A bar's declaring scope IS its
group's, which is what nested scopes are for: the progress fact and the `activeBars` set that
selects it have one lifetime because they have one home. It also makes 12.7's settlement order
("then groups, then bars within a group, in declaration order") literal, and gives `SetActiveBars`
its membership test for free.

**Ruling (2026-08-21, replacing the pipe and the proportional split) - a group holds bars and caps
how many run at once, and nothing else.** `fillCurrency` lives on the BAR: the bar is what drinks, so
it names what it drinks, and a bar with none fills from time alone - which is the whole of what
`BarFillBehavior` used to say, so that family and both its subclasses are deleted along with
`pipeRate`. Throttling a bar is its own `fillRate`; a second cap over the set expressed the same
outcome, and in Chapter 1 the pipe equalled the one active bar's rate and never bound anything.

Owning no number, a group is not an effect target - it leaves `Targetables`. Buffing a set of bars is
a tag they share, which is the mechanism that already fans one effect out to many owners.

**Ruling - a null `availableWhen` on a bar is OPEN**, the opposite of step 4's null purchase gate.
The fail-closed rule binds entry points that create value out of a spend; a bar's availability is a
selection filter, and Chapter 1 authors all three covers without one. Validation therefore says
nothing about a null bar gate.

## Resolution

**Ruling - a bar's rate resolves stage 1 only.** It is read as
`GetMultiplier(declaringScopeCtx, bar, bar.fillCurrency, Stat.Rate)`, and the currency stage is NOT
applied. Stage 2 is "effects on this currency's total production" and a bar consumes rather than
produces; letting a currency-total effect through would mean `records_income` speeds the drain on
Rehearsal as well as its supply, which is not what either buff means. The bar's own currency is
passed as the coordinate anyway so an effect may still narrow to it
(`{target: cover_1, currencyId: rehearsal}`); a bar that fills from time passes an empty coordinate,
which no narrowing effect matches. `maxActive` is an int and stays outside the multiplier system
entirely - nothing addresses it.

**The cascade row of 12.6 joins `GetMultiplier`.** At each scope on the outward walk, after the
upgrade and modifier passes: for every declared group, for every bar in it with
`fillCounts[barId] > 0`, every matching `perFill` entry contributes
`Grown(effect.multiplier, count, entry.growth)`. The read goes through the declaration list, like
upgrades, so a stray fillCount for a bar this scope never declared cannot contribute.
`Producer.Stacked` splits: a shared `Grown(BigNumber, int, GrowthKind)` carries the arithmetic
(`Multiply` = m^n, `Linear` = 1 + (m-1)*n saturating at zero) and `Stacked` becomes the
`StackingKind` wrapper that short-circuits `Replace`. 12.7 states the vocabulary is shared and that
the saturation rule binds both consumers; one helper is what makes that true rather than duplicated.

## The segment - two calls straddling the production phase

**Ruling - demand comes from the start-of-segment snapshot, taken BEFORE the production deposits.**
12.9 fixes the phases and says they resolve "from a start-of-segment snapshot of effects and entry
conditions"; the one intra-segment coupling it carves out is the pool BALANCE ("production before
consumption, so an empty pool fed at +1/sec serves a 1/sec bar demand in the same tick"). Rates and
gates are not carved out, so resolving them after the deposits would let this segment's own
production flip a bar's `availableWhen` and have it draw for the whole dt - the coupling the
snapshot rule exists to forbid. Step 5 therefore ships the seam rather than one call:

- **`BarSystem.ResolveDemand(subtreeRoot, segmentStartUtc)`** - called by the tick BEFORE the
  production phase. Walks the subtree in tree order (parent before child), groups then bars in
  declaration order, and returns a `BarDemand` snapshot: a flat list of the drawing bars, each with
  its effective fill rate, its pre-fill progress, the home of the currency it names, and its scope's
  `ScopeFacts` reference identity. Nothing is mutated.
- **`BarSystem.ConsumeAndSettle(demand, dtSeconds, settlementUtc)`** - called AFTER the production
  phase. Reads pool balances live, draws, then settles completions. Fill and settlement stay one
  call because crossing detection needs the pre-fill progress the snapshot carries.

**Both timestamps are real time and both come from the tick's clock.** `ResolveDemand` takes the
segment-START stamp, which is what its condition and expiry reads are judged against.
`ConsumeAndSettle` takes the segment-END stamp separately, because `dtSeconds` is SCALED production
time and real elapsed is unrecoverable from it whenever `game_speed != 1`. Settlement executes
authored actions, and those stamp real time: `ResetScope` re-stamps `lastActiveUtc` from
`ctx.NowUtc`, and a completion that grants a buff or starts an event writes a real expiry. The end
boundary is the right stamp because 12.9 advances wall clocks to it after the completion phase - "a
timer born mid-segment is never charged for a segment it didn't live through" is exactly what
stamping the boundary produces.

The snapshot governs DEMAND only. Settlement re-reads live state per iteration, per 12.7 - the two
rules are about different phases and the settlement section below states its own.

Fill, using the snapshot's order:

- **Drawing** (the demand-side test, judged once in the snapshot) = the bar id is in
  `activeBars[groupId]` at the group's declaring scope, it belongs to the group, `availableWhen`
  holds in that scope, **`fillAmount` is positive**, and - for a non-repeating bar - progress is
  below `fillAmount`. A repeating bar is otherwise always hungry. The `fillAmount` leg is what keeps
  a malformed bar out of the pool: settlement would refuse to pay it (see the runtime backstop), so
  admitting it to the draw would spend pool currency every segment forever and settle none of it. A
  non-repeating bar fails the progress leg on its own when `fillAmount <= 0`; a repeating one needs
  the explicit test.
- **The draw** is `min(rate * dt, balance)` at the currency's home, taken in the order above, then
  spent. The balance is read live PER BAR, so an earlier bar's draw is visible to a later one and an
  empty pool stalls the rest. There are no totals, so there is nothing to divide and no division to
  guard: the four divide sites the proportional model needed are gone with it. Overfill is allowed
  and readable (12.7), so a bar takes its whole rate rather than its remaining need.
- **Spending, not depositing**: `earnedTotals` is untouched, because a bar's fill is not income.
- **A bar with no `fillCurrency`** advances only if the snapshot recorded it as DRAWING - selected,
  available, positive `fillAmount`, and below it if non-repeating - exactly like any other bar. What
  it skips is only the balance read and the spend: `progress += effectiveRate * dt`, paid by nothing.
  `activeBars` and `maxActive` are its whole throttle, so bypassing the selection test would make it
  unstoppable.

## Settlement

Completions settle after every bar in the subtree has filled, in one deterministic order - scopes in
tree order, then groups, then bars, in declaration order.

**Non-repeating bars fire on the crossing, detected within the pass**: the work list records each
bar's pre-fill progress, and `onComplete` fires iff `before < fillAmount <= after`. Nothing is
stored and nothing re-fires: a bar already full when the segment began was below nothing, and a save
loaded at full progress never fires because no fill crossed it. This is what "no completed-set is
stored" costs, and it is why the fill phase and the settlement phase are one call.

**Repeating bars settle iteratively** (12.7), and **only bars the snapshot recorded as drawing enter
settlement at all**. The live checks then run before every iteration and may only DISQUALIFY: while
progress >= fillAmount, the bar is still selected, still available, and its scope still holds the
payload the snapshot captured, subtract `fillAmount`, increment `fillCounts`, then execute
`onComplete` in the declaring scope. State may take a bar out of the loop; it may never put one in.
Without that asymmetry the seam has a second door: a repeating bar can sit at
`progress >= fillAmount` with `availableWhen` false - its own `onComplete` flipped the gate last
segment and 12.7 retains the residual, and a save can load in that state - and a live-only test
would let this segment's deposits open the gate and pay the whole backlog, which is the coupling
`ResolveDemand` exists to close. Non-repeating bars are safe by construction: their crossing test
needs a fill delta, and nothing fills a bar that was not drawing.

The live re-read is still the doc's own words - "re-reading state each iteration, so a completion
action that resets the host or flips availability stops the loop honestly instead of executing
precomputed fires against a changed world" - it just runs against a work list the snapshot fixed.
Residual progress is retained. Increment before execute, as the doc words it and for the same reason
the trigger latch is written first.

**A reset during settlement invalidates the rest of that scope-life.** The work list captures each
scope's `ScopeFacts` reference; an entry whose scope no longer holds that payload is skipped, and
the repeating loop re-checks it per iteration. Reset is a payload swap (12.3), so reference identity
is the check - no bookkeeping to keep in sync.

**The arithmetic shortcut** 12.7 sanctions applies when a bar's `onComplete` is empty -
`n = Floor(progress / fillAmount)` in one step - because nothing can change between fills. A bar
with actions iterates.

`fillAmount > 0` is a validation error, but the validation pass is dev-only, so the release build
checks it twice. The drawing test above keeps a malformed bar out of the demand snapshot, which is
what protects the pool; settlement keeps its own **runtime backstop** - a bar whose `fillAmount` is
not positive is skipped with a logged error, before either the shortcut's division or the loop's
comparison - as defense against a malformed snapshot reaching it anyway. This is the same doubling
step 4 gave `TryBuy`'s computed cost: the release build executes the check, the validator explains
it.

Completion does not touch `activeBars` - selection is the player's fact, and `SetActiveBars` is its
only writer. A completed bar left selected demands nothing. Auto-advance is not a field any more -
it died with the behavior classes, and how a chapter would grant it was undecided anyway (an Effect
multiplies numbers; it cannot flip a bool).

## Entry point - `SetActiveBars(ctx, group, bars)`

**An entry point takes the assets its caller already holds; an id appears only where a FACT supplies
one.** The caller here is a widget bound to the group, so it has the `BarGroupDefinition` and the
`BarDefinition`s; passing their ids would discard that and make the callee re-derive it, and it
would make this the only entry point in the codebase taking ids (`CanBuy(ctx, generator)`,
`Buy(ctx, upgrade)`, `FireProducer(ctx, producer)`, `GetRate(scope, now, currency)` all take the
asset). The ids in the fact keys below are the other half of the same rule and are CORRECT as ids:
`activeBars[groupId]`, `fillCounts[barId]`, and the `{target: cover_1}` effect coordinate, which is
a string because an effect target is a filter over names, not a reference. (A group is not among
those names - it owns no number, so nothing targets it.)

Fail-closed and all-or-nothing (12.7/12.11): refuses an unknown group, a set larger than
`maxActive` after de-duplication, any bar outside the group, any bar whose `availableWhen` is false
in the group's declaring scope, and any completed non-repeating bar. A refusal changes nothing;
there is no partial application. On success the set is written to `activeBars[groupId]` at the
group's declaring scope. The foreground-subtree guard layers on in step 7.

## Validation extensions

- **Collection LANDED with the 2026-08-20 reference pass.** `CollectDeclared` already walks
  `barGroups` and, in a nested pass, each group's bars; `RecordHome` records both; `DeclaredBy`
  yields the group then its bars, and `Targetables` yields the bars alone. So `DeclaringScope(bar)` already answers,
  `DuplicateHome` already covers a group declared by two scopes and a bar listed by two groups, and
  id/tag collision and tag membership already include bars. What step 5 adds is the per-scope
  VALIDATION branch, which does not exist: the scope loop has producer, generator, upgrade and
  career branches and no group branch.
- **Group**: `maxActive >= 1`, and no null entries in `bars`. That is all a group has.
- **Bar**: `fillAmount > 0` and `fillRate > 0` are `NumericRange` errors (a nonpositive threshold is
  an unbounded settlement loop; a zero rate is a bar no multiplier can ever move). A named
  `fillCurrency` gets `RequireOnChain`, the check every other currency operand already gets; a null
  one is legal and says the bar fills from time. `availableWhen`
  validates in the declaring scope when present. `onComplete` runs through `ValidateActionList` with
  container key `bar:<id>`, joining set-then-wiped, reads-zeros, flag-setter tracking, the modifier
  ledgers, and the cycle graph.
- **Implicit fact write** for a REPEATING bar that carries `perFill` entries: record the fillCount
  write at index -1 (home = declaring scope) before validating `onComplete`, exactly as step 4 does
  for the upgrade latch. That is what catches a cascade whose own completion list resets the scope
  homing the count it reads - the cascade would never accumulate. Non-repeating bars and
  cascade-free repeating bars record nothing, so ordinary "fill then reset the tier" authoring stays
  clean.
- **perFill**: null entries error; the effect goes through `ValidateEffect` with reach measured from
  the declaring scope. **`perFill` on a NON-REPEATING bar is an error**: the cascade count is
  `fillCounts`, which only repeating bars ever acquire - 12.6's row is titled "Repeating bars",
  `ScopeFacts.fillCounts` is commented "repeating bars", and `chapter-01-content.md` states the rule
  from the content side ("Completion is a moment that leaves no derivable effect-fact for a
  non-repeating bar, so the fan-rate reward is an `AddModifier` grant"). The authored effect is
  unreachable rather than merely inert, which is what makes it an error and not a warning. The
  alternative - granting non-repeating bars a count of 1 - is rejected: it would give a one-shot bar
  a second reward channel that duplicates `onComplete`, and 12.6 would have to change.
- **Effect narrowing**: `SatisfiesNarrowing`'s switch falls to `default: return true` for a bar,
  commented as waiting for step 5's production model. A bar has one now - the rate read is
  `GetMultiplier(ctx, bar, bar.fillCurrency, Stat.Rate)` - so the pair it validates against is that
  bar's own currency and `rate`: a currency coordinate must name it by id or tag, and a stat
  coordinate must be `rate`. A bar with no currency has no pair for a currency coordinate to name.
  Groups leave `Targetables` entirely, so an effect aimed at one warns as a kind that is not a
  target.
- **`BarsCompleted`** gains the reach check its step-3 comment promised: the group's declaring scope
  must be the acting scope or an ancestor (`ChainReach`), and its evaluation walks `group.bars`.

## Save extensions

The step 2 incremental contract, applied in `FilterToDeclared` against the scope's own groups:

- `barProgress` keys that name no bar this scope's groups declare are dropped; negative values are
  dropped.
- `fillCounts` likewise, plus nonpositive counts and counts on non-repeating bars (no such fact
  exists).
- `activeBars`: unknown group keys dropped, bar ids outside their group dropped, and a set still
  over `maxActive` after filtering is CLEARED rather than truncated - refusing a tampered selection
  beats picking which bars survive; the player reselects.

## Files and tests

New: `Economy/BarSystem.cs` (the name 12.13's file layout already uses). Deleted:
`Economy/BarFillBehavior.cs` - the family and both subclasses, since a null `fillCurrency` says
everything they said. Modified: `BarDefinition.cs` (gains `fillCurrency`), `BarGroupDefinition.cs`
(loses `fillCurrency`, `pipeRate` and `behavior`), `Producer.cs` (perFill gather, `Grown`/`Stacked`
split, a null currency coordinate), `Condition.cs` (`BarsCompleted` reach - its `Evaluate` already
walks `group.bars`), `Effect.cs` (a group is no longer a target kind), `ContentValidator.cs`,
`SaveSystem.cs`. `ScopeDefinition.barGroups`, `BarGroupDefinition.bars` and
`GrowthKind`/`PerFillEntry` need no edit - they landed with the 2026-08-20 reference pass.

Tests - new `BarSystemTests`: a short pool feeding bars in declaration order until it runs out,
two groups drawing one pool in tree order, pool
exhaustion, spend-not-earn, a currency-free bar ignoring the pool, overfill retained, non-repeating
crossing fires once and not again on a second segment, repeating iterative settlement with residual,
an `onComplete` that flips availability stopping the loop, a reset mid-settlement dropping the rest
of that scope-life, the empty-`onComplete` shortcut matching the iterative result, deterministic
order across scopes and groups, and every `SetActiveBars` refusal. Degenerate numbers: a bar whose
rate resolves to zero through an x0 handicap draws nothing, and a nonpositive
`fillAmount` on a repeating bar leaves the pool balance and the bar's progress BOTH
unchanged after a segment (not merely that nothing threw), with and without an `onComplete` list.
Snapshot tests: a deposit made between
`ResolveDemand` and `ConsumeAndSettle` moves the pool but changes no rate and opens no gate, while
settlement's own re-reads still see live state; and a repeating bar entering the segment gate-closed
with a full backlog, where the deposits open the gate and settlement still pays NOTHING. Currency-free selection: an unselected bar, an unavailable bar,
and a completed non-repeating bar each advance zero while a selected available one advances. A completion-driven `ResetScope` stamping the segment-end boundary rather than anything
derived from scaled dt.
`ResolutionTests` extends with the cascade row (both growth kinds, linear saturation, and a stray
count for an undeclared bar contributing nothing); `BarSystemTests` carries the reach cases, where
the draw is observable - a bar-targeted buff reaching the fill rate, a currency-total effect not. `ContentValidatorTests`
and `SaveSystemTests` extend per the two sections above; `TestTree` grows the Chapter 1 cover group.
No production assets - step 8 authors those.

## Docs on landing

12.7 records the rulings that are not in it today: the group owns its bars and caps how many run,
the bar names what it drinks and takes what is there, stage-1-only rate resolution, a group is not an
effect target, a null bar gate is open, the empty-`onComplete` condition for the arithmetic shortcut,
and the settlement entry gate (snapshot admits, live state may only disqualify). 12.6's fill-count row
names `perFill` gathering through the declaration list and says the cascade is repeating-bars-only.
12.9 needs no change - it already says what the demand snapshot must obey - but its bar-consumption
phase gains the note that demand is resolved before the production deposits, since that is the
constraint step 7's phase code has to honor. 12.12's bullet list gains the bar checks. Build plan
step 5's status line updates when the loop is green.

## Verification

The headless loop (lockfile check first), per the build plan's per-step contract. Commit waits for
John's review.

## Deliberately not in step 5

The tick and dt segmentation (step 7 - bars never advance while a chapter is dormant, so the idle
claim stays a rate-only payout, 13.4). Step 5 owns the seam but not its call order: step 7 must
call `ResolveDemand` at the top of each segment, BEFORE the rate-production deposits, and
`ConsumeAndSettle` after them, passing the segment's real start and end stamps rather than anything
derived from the scaled dt. A step-7 test asserting that order is the guard against the phase code
quietly collapsing the two calls back together. The trigger
sweep and event goal latching that share the transaction (step 6). Bar-completion actions that start
events (they already work through the shared action machinery; the lifecycle ops land in step 6).
The command boundary (step 7). UI, including bar rendering and the group widget (step 9). Additional
fill behaviors (tap-a-chunk, dump-the-pool) - sibling classes when a chapter wants one.
Auto-advance, which died with the behavior classes and waits for the chapter that wants it - and
with it any notion of a shared throughput budget across parallel bars, which is a design decision at
that time rather than a field carried from the start. A `fillCounts`
overflow policy: no authored bar repeats, so the boundary is unreachable and the rule that would
govern it is unwritten on purpose.
