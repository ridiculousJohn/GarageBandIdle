# Step 6 Plan - Events + the sweep

Design for build-plan step 6. Sections cited are `garage-band-idle-design.md`.

## What step 6 is

Two things land: the event family (6.1, 12.8) and the trigger sweep (12.5). They share one moment -
the sweep latches event goals before it runs any trigger action - so they ship together.

## Existing systems this uses unchanged

Nothing new is built where one of these already answers the question:

- **Host lookup**: declaration is ownership, so the host IS the declaring scope, found by the same
  outward walk generators, upgrades and bar groups already get, asked for an `InteriorScopeState`
  so root is not a candidate. The definition carries no host field (6.1, 12.8).
- **Entry-point shape**: `Purchasing`'s `Can*` / `Do*` / `Try*` triple, fail-closed, content faults
  throwing from either path.
- **Host context**: `GameContext.Rebase(host)` - what `ExecuteRung` already does.
- **Handicap arithmetic**: the per-scope gather. `Producer.GetMultiplier` multiplies what each
  scope returns from `MultiplierFor`, so handicaps are an override on the one class that can hold a
  record rather than a fifth read in the walk.
- **Mid-list reset detection**: `BarSystem`'s reference-identity check (`facts == scope.facts`). A
  reset is a payload swap, so identity is the whole test.
- **Record storage**: `ActiveEvent` and `InteriorFacts` are both in the tree. The record is a single
  field on the payload chapters and tiers hold and root does not, so "at most one" and "never on
  root" are unrepresentable rather than enforced - load cannot deliver two or place one at root,
  and the filter needs no rule for either.
- **Latch storage**: `firedTriggers` and `TriggerDefinition` landed in step 1. The sweep is the
  missing consumer, not new state.
- **Validation**: the per-family branch in the scope loop, `ValidateActionList`, `ValidateEffect`,
  and the existing cycle and reset ledgers. Events add a branch, not machinery.
- **Save**: `FilterToDeclared`'s existing per-family filter, extended the way steps 4 and 5
  extended it.

## Where events are declared

`events` goes on `InteriorDefinition` beside `rung`, because root cannot host one. No special walk
is needed: the three outward walks stopped reading lists off a base-typed loop variable and now ask
each scope for what it has.

```csharp
// ScopeDefinition answers for its own lists; InteriorDefinition adds events.
internal virtual bool Declares(Definition definition)

// Each scope composes its own factor; the interior state folds in handicaps.
internal virtual BigNumber MultiplierFor(GameContext origin, Definition owner,
                                         CurrencyDefinition currency, string stat)
```

`Producer.GetMultiplier` walks and multiplies what `MultiplierFor` returns. The declaration walk is
typed by what it is searching for, so `StartEvent` asks for a scope that can host one:

```csharp
internal static T DeclaringScope<T>(ScopeState from, Definition definition) where T : ScopeState

Producer.DeclaringScope<InteriorScopeState>(from, eventDefinition)
```

Root is not a candidate because it is not an `InteriorScopeState`, not because a check skipped it.
Neither walk names a family, so `events` appears in no signature outside the class that has them,
and root has no member to declare, answer or validate.

`InteriorScopeState` lands with this step - it has nothing to hold until `MultiplierFor` needs
overriding for handicaps.

## New pieces

1. `Events/EventDefinition.cs` - `availableWhen`, `goal`, `timeLimitSeconds`, `handicaps`,
   `onEntry`, `rewards`, `onEnd`. `IsAvailable(ctx)`, `GoalHolds(ctx)` and `BlocksIdle` on the
   definition, mirroring `GeneratorDefinition.IsAvailable`. `BlocksIdle` is derived
   (`timeLimitSeconds > 0`) so the idle path asks one question and never inspects a timer; a bool
   becomes another term in that property if one is ever wanted.
2. `InteriorDefinition.events` - the list 12.3 reserves, on the class that can host one.
3. `Events/EventSystem.cs` - the two operations plus `AdvanceTimers`.
4. `Core/Sweep.cs` - `Sweep.Run(root, foregroundChapter, nowUtc)`.
5. No lifecycle `GameAction` kinds. Start and dismiss are commands (12.11), so no authored list
   can start or end an event and one event cannot spawn another.
6. Two `Condition` kinds: `EventRecordExists(host)`, `EventRewardPending(host)`, each holding a
   `ScopeDefinition` like `ResetScope.scope`, reached self-or-enclosed via `FindInSubtree` at
   runtime and `InActingSubtree` at load. Both are pure fact reads.
7. `ActiveEvent` is `{eventId, remainingSeconds, goalReached}` - with one ending that removes the
   record, no legal state carries a claimed flag. It sits on `InteriorFacts`, which `ChapterFacts`
   and `TierFacts` derive and `RootFacts` does not, so root cannot carry the field at all. A single
   field, never a list. Both shapes are already in the tree.
8. `Always` - a `Condition` kind that always holds, which is how an author opens a gate now that a
   null gate is refused at load.
9. `RestartScope(scope)` - a `GameAction`: fire that scope's rung through its own gate, then clear
   the scope. Reach identical to `ResetScope`. Replaces the `[ExecuteRung(tier), ResetScope(tier)]`
   idiom at every call site so the semantics live in one place rather than in every asset.

## The two operations

Each resolves the host by the outward walk and rebases to it, so `availableWhen`, the goal and all
three action lists evaluate in the HOST's scope (12.4).

- **Start**: refused unless the host holds no record and `availableWhen` holds (a null gate refuses,
  as with a generator). Runs `onEntry`, THEN writes the record through the host's accessor - which
  is what puts it in the fresh payload when `onEntry` reset the host (6.1's banked run).
- **Dismiss**: refused unless the host holds a record FOR THIS EVENT - a sibling's record is an
  ordinary refusal, since which event is running is state the player produced. Reads `goalReached`
  into a local, REMOVES the record, then runs `rewards` if the goal was reached, then runs `onEnd`
  either way.

Removing first is the order rather than a detail: it opens a rung gated on
`Not(EventRewardPending(host))`, so an `onEnd` carrying `[RestartScope(tier)]` banks instead of
no-oping against its own reward. And it costs nothing - no action, payout formula or rung reads a
multiplier, so no list can observe that the handicaps went with the record. No `claimed` field is
needed either way, since nothing can re-enter: dismissal is a command, not an action.

Nothing can start an event into the vacated host, because starting is a command and no action list
can reach it.

Two derived questions, one rule each:

- **Occupied** (Start refuses): any record at the host, running or expired.
- **Running** (the goal may still latch): the record exists and the event is untimed or
  `remainingSeconds > 0`. Handicaps do NOT use this - they ride on the record existing, so expiry
  does not lift them.

There is no third: arming is `goalReached` alone.

## Handicaps

In `InteriorScopeState.MultiplierFor`, after `base.MultiplierFor`: for every event the scope
declares, if the scope's record names it, its matching `handicaps` entries contribute. Existence is the whole test -
no expiry check, because a failed attempt sits one tap from a tier reset and briefly lifting the
handicap there would be a worse state than leaving it. Read through the declaration list, like
upgrades, so a stray record id contributes nothing. No count scaling - there is one record.

## The sweep

`Sweep.Run` over root plus the foreground chapter's subtree (root first, then tree order, triggers
in declaration order). Root always sweeps - it never resets and is on every chain. Dormant sibling
chapters do not; a threshold crossed while away fires on the first live sweep after switch-in.

1. Latch met goals across the swept set. No action has run yet, so the sweep-start snapshot is just
   "now" - no snapshot machinery.
2. Collect eligible triggers - condition holds, id not latched - capturing each scope's facts
   reference. A null condition is closed and never fires; validation refuses it at load, and the
   sweep does not dereference it. Nothing executes during collection, which is what makes a trigger
   armed by an earlier trigger in the same pass wait for the next sweep.
3. Execute in collection order: skip an entry whose facts reference no longer matches, then latch
   the id and run the actions. Latch-first per trigger, so a self-resetting list re-arms itself.

## Timers

`EventSystem.AdvanceTimers(root, foregroundChapter, realSeconds, segmentEndUtc)` - the same set
`Sweep.Run` walks. The timestamp is passed, never read from the clock: latching a goal needs a
`GameContext`, `GameContext` needs `NowUtc`, and `DateTime.UtcNow` inside the phase would make a
segmented tick non-deterministic and untestable - the same reason `BarSystem` takes
`segmentStartUtc` and `settlementUtc` as arguments. For each timed record still running: latch a met
goal, then decrement, floored at zero. Latching before the decrement is what makes 12.8's tie go
to the player - the sweep runs after the last segment, when the record already reads zero. A record
never removes itself; expiry ends nothing but the chance to latch.

## Validation

Completes 12.12. Events join id collection (per-chain uniqueness, tag collision) and `RecordHome`
beside the other nine families, so an event declared by two scopes is a `DuplicateHome` error -
one asset, one home, like everything else a scope declares. Events do not join `Targetables`: an
event owns no number.

- No check for an event on the root. `RootDefinition` has no `events` list to put one in, exactly as
  it has no `rung`, so the rule 6.1 states - an event is scoped to a chapter, and root handicaps
  would gather into every chapter's walk - is carried by the type rather than by a finding.
- Per event: `availableWhen` null is an ERROR - it is a gate, and a gate is required now that
  `Always` exists to say "open". `goal` null warns - dismiss-only, never rewarding.
  `timeLimitSeconds` finite and nonnegative. `handicaps` through `ValidateEffect` from the declaring
  scope.
- The same gate rule extends to the families that already have gates, and that is the one ordering
  constraint inside this step: `Always` lands FIRST, then the check flips. Today only a generator's
  null `availableWhen` reports anything (a warn, `ContentValidator` line 946); an upgrade gate, a
  rung's `offerCondition` and a trigger's condition are silent. All four become errors, so existing
  fixtures need auditing - `ContentValidatorTests` and `GameActionTests` each build rungs with no
  `offerCondition`, and those get `Always` rather than a suppressed finding. The RUNTIME rule is
  unchanged: a null gate still refuses, as the fail-closed backstop behind a dev-only pass. Two
  comments describing today's rule are rewritten with the check - `Rung.cs` ("an unauthored gate is
  closed, not open") and `GeneratorDefinition.cs` ("validation warns rather than errors").
- `onEntry` is its own container through `ValidateActionList`; `rewards` and `onEnd` are ONE
  container in that order, since they always run back to back in a single transaction - a flag set
  in `rewards` and wiped by `onEnd`'s reset is exactly what set-then-wiped exists to catch.
- `RestartScope.Validate` records BOTH ledgers at the caller's action index - `RecordRungInvocation`
  for the rung half and `RecordReset` for the clear - because it is an `ExecuteRung` and a
  `ResetScope` in one action. Registering only the cycle edge would let set-then-wiped,
  reads-zeros, stranded value and stranded reward all step straight over it. The lifecycle ops leave
  the graph entirely, and there is no own-host rule left to write: no list can name one.
- Balance-goal-without-reset warn, and the stranded-reward warn: a `rung:` reset closure containing
  an event host whose `offerCondition` carries no REQUIRED `Not(EventRewardPending(host))` - either
  the whole condition, or a conjunct reached through `All` alone. The requiredness is the test, not
  the mere presence of the leg: a positive leg means the opposite, and one under an `Any` is
  satisfied by its sibling branch.

## Save

No schema version bump. `activeEvents` was a list nothing ever wrote - `EventSystem` does not
exist before this step - so no valid v1 save can hold an event record, and the read drops the
member the payload type no longer has. Discarding that dormant placeholder is the intent, not an
oversight; there is nothing to migrate.

`FilterToDeclared` drops a record whose `eventId` names no event the scope declares, and clamps
`remainingSeconds` to `[0, timeLimitSeconds]`. Nothing else: `claimed` is gone, a second record is
unrepresentable, and a non-finite `remainingSeconds` needs no sanitizing - validation refuses a
non-finite `timeLimitSeconds`, and a hand-edited NaN only makes every comparison false, which reads
as expired.

## Tests

`EventSystemTests`: every Start refusal (gate false, null gate, occupied by a running record, by an
expired one, by another event's record); `onEntry` order and host context; the record landing in the
fresh payload after an entry reset; the entry rung banking a gate-met run and discarding an unmet
one.

Latching: a goal met mid-attempt latches; a goal met and then spent back below stays latched; an
untimed record latches the same way a timed one does; a goal first met AFTER expiry never latches;
the tie - a goal met by the segment that also expires the timer - latches.

Dismiss: refused when the host holds a sibling event's record; `rewards` runs only when
`goalReached` was set and `onEnd` runs either way; the record is gone before either list executes;
an `onEnd` of `[RestartScope(tier)]` banks a gate-met run through a rung guarded by
`Not(EventRewardPending(tier))`, which is the case that fails if the record is removed last; an
empty `onEnd` leaves the run's leavings in place; a reset from above kills the record.

Timers: decrement on real seconds, floor at zero, never removed, untimed records untouched. Idle:
`BlocksIdle` true for a timed event and false for an untimed one.

`GameActionTests` gains `RestartScope`: a gate-met scope banking through its own rung and then
clearing, a gate-unmet scope clearing with nothing banked, a scope with no rung clearing, reach
refused outside the acting subtree, and the root refused. `ContentValidatorTests` asserts it records
both ledgers - a `RestartScope` whose rung payout is unreferenced trips stranded value, and one that
clears a scope holding a flag its own list set trips set-then-wiped.

`SweepTests`: a null trigger condition never firing; one fire per scope-life and re-arm after
reset; a self-resetting trigger;
deterministic order; a trigger armed this pass firing next pass; a scope reset mid-sweep; a dormant
chapter not sweeping; root sweeping with a null foreground chapter; the goal latch landing before
trigger actions.

`ResolutionTests` gains the handicap row: a record zeroes a tagged line, an EXPIRED record still
zeroes it (expiry does not lift handicaps), no record contributes 1x, and a stray id for an event
the scope never declares contributes nothing. `ConditionTests` gains the two kinds, both as pure
fact reads. `ContentValidatorTests` and
`SaveSystemTests` extend per above, including Chapter 1's authored shape producing no findings.
`TestContent` grows one timed and one untimed event on tier1.

## Not in step 6

The tick, dt segmentation and the expiry query it needs, the transaction pipeline that calls
`Sweep.Run` and `AdvanceTimers`, the command boundary, and idle's use of `BlocksIdle` - all step 7.
Step 6 ships the property; step 7 is what reads it. Buffs - step 10. The event UI module - step 9. Chapter 1's event assets - step 8.

## Docs on landing

Only the edits that describe shipped code are left here; 6.1's host field, the lifecycle-op
signatures in 6.1 and 12.5, and 12.4's two condition kinds were decisions, so they are already
applied.

12.3's `ScopeDefinition` paragraph drops its "events join the list when 12.8 lands" note and its
`ActiveEvent` schema line loses `claimed`. 12.6's active-event row names the declaration-list read.
12.8 records that the swept set is root plus the foreground chapter. 12.13 gains `Core/Sweep.cs`.
Build plan step 6's status line and its "full 12.12 only once step 6 lands" caveat.

Everything else settled on 2026-08-24 - two operations, the two ending lists, remove-first,
one latch rule, handicaps on record existence, `blocksIdle`, and `claimed` deleted - is already in
6.1, 12.4, 12.5, 12.8, 12.9 and 12.11.
