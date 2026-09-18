# Groups, consumption, and idle over bars

Plan for three refactors found while translating chapter 2 to the typed form, 2026-09-18. Sections
cited are `garage-band-idle-design.md`. Suite at the start: 878/878, at commit 1ba8a31.

The finding: chapter 2's whole income is bar completions, and the idle claim pays rate targets
only, so as authored the chapter pays nothing while the player is away. The rule behind that
(section 9, "bar progress never accrues") and two others near it were written from one chapter's
content and generalized without their rationale: consumption is a spend hard-coded off one field on
the bar, and selection lives on a bar-only group. All three are replaced by mechanisms true of every
instance of their kind.

## What John said, verbatim

Every line under "The changes" traces to one of these, and a line that does not is the defect.

1. "it kept what it was pulling from separate from it filling. The consumption of whatever currency
   it uses to fill should be a scriptable action not some hard-coded bullshit that codes us into a
   corner again."
2. "the decision to have 'bar groups' be a formal thing was dumb too."
3. "the entire concept of one active is way too specific. In reality, it should be a generic
   grouping that allows any 'thing' under it to be marked active or not, shouldn't be a 'bar'
   only."
4. "how is it even remotely reasonable that idle is excluded from a bar, when it has everything it
   needs. it has a fill rate over time. The only extra piece is it has a dependency that has to
   resolve first."
5. "How many times have I said we're writing SYSTEMS here that need to be flexible? coding them
   specifically to one task is useless"
6. "there still needs to be a way to consume some other resource as a bar fills, I just don't see
   why that was hard-coded to be the only way"
7. "consumption has to be capped by what it consumes. if there's nothing to consume it can't fill"
8. "if you keep the thing group bars more general to just something that groups 'things', then we
   could have any control in that group be managed and made active/inactive. yes?" and "we have
   producers, generators, and currencies, could those be part of this group?"
9. On consumption being a multiplier target: "that allows for buffs to say that bars consume 90%
   less of currency X."
10. On idle: "nothing in any of this was a contract to change how idle PAYOUT works. you also
    shouldn't have to run the sim over this time. just compute the value."
11. "some bars pay VERY SLOWLY and idle time away should go towards their progress."
12. "just write the plan"

## The changes

Three sections, landing as one plan. A and B change the bar and its group and touch the same files;
C reads the results of both. The content that exists (chapter 1 typed, chapter 2 authored) is
rewritten onto them in the same landing.

### A. A group is a set of members with an active set (sentences 2, 3, 5, 8; 12.3, 12.7, 12.11)

**Definition.** `GroupDefinition : Definition { int maxActive; List<Definition> members; }` replaces
`BarGroupDefinition`. A group is declared on a scope in its own list, `groups`, and its members are
references to definitions declared on THAT scope: a bar, a generator, a producer, a currency, an
upgrade. A member's home is its scope, never the group, and a definition may be listed by more than
one group. Bars stop being owned: `ScopeDefinition.bars` is a declaration list like `generators`,
and `Declares`, `Sources`, `ActionLists`, `CarrierEffects` and every walk that reached bars through
`barGroups` read `bars` directly.

**The fact.** `ScopeFacts.activeMembers : Dictionary<string, HashSet<string>>`, group id to member
ids, replaces `activeBars`. Same home (the group's scope), same lifetime (cleared by the scope's
reset), same save shape under the new key. Dev saves are deleted, the schema version stays 1
(12.10).

**Membership gates.** A member of a group is OFF unless the group's active set holds it, and a
definition listed by several groups is on only when every one of them holds it. That is the whole
meaning of membership, read at one seam per kind, each of them a check that already exists:

- a bar draws only while on (`BarSystem.Drawing`, where the `activeBars` read is today);
- a generator's terms are zero while off (`Producer`'s source-term resolution, beside the
  currency `IsActive` read at line 180), and `FireGeneratorYield` on an off generator pays nothing;
- a producer fires nothing while off (`FireProducer` refuses like an inactive currency's deposit);
- a currency reads inactive while off (`CurrencyDefinition.IsActive` gains the clause);
- an upgrade's effects gather only while on (`CarrierEffects` liveness).

`GatherCompiler` compiles the membership once at Build: per node, definition to the groups on that
node listing it (`ScopeLinks`), so the per-kind read is a link read and never a search.
`GameContext.IsOn(definition)` is the one query behind the five seams.

**The command.** `SetActiveMembers(ctx, group, members)` replaces `SetActiveBars`, fail-closed as
before: every named member is listed by the group, the count is within `maxActive`, and a member
that is a bar with no `repeatWhen` at full progress is refused as today. The select control
TOGGLES: pressing an off member adds it, pressing an on member removes it, and adding into a full
group is refused - the player deselects first. Chapter 1's covers at `maxActive: 1` take a deselect
before switching cover. One behavior for every cap.

**The manual team** (`repeatWhen` false) leaves the active set of every group listing it at
settlement, through the compiled membership, as `SettleManual` removes it from its one group today.

**Conditions.** `BarsCompleted` keeps its group operand and counts the group's bar members at full.
No new condition kind: membership gating is read by the runtime, and nothing in hand needs to ask
it in content.

**UI.** One module, `group`, binds one group by `contentId` as `generator_row` binds one generator,
and renders each member with the row its kind already has - bar, generator, upgrade, currency line,
tap button - plus the select control. `BarGroupUI` and the `bar_group` module (which rendered every
group of its scope, binding nothing) are deleted. `BarRowUI` loses its group field; the select
control lives on the group widget's member wrapper. The pool readout the bar group drew becomes a
readout per distinct currency the group's bar members consume.

**Importer.** `ScopeDto.bars`, `ScopeDto.groups` with `GroupDto { id, displayName, description,
tags, maxActive, members: List<string> }`, member ids resolved to definitions declared on the same
scope. `barGroups` is gone and refused as an unknown key. Chapter 1's typed content moves its three
covers to `bars` and declares `learn_covers` as a group of them; the Rehearsal Space section binds
`{prefabId: group, contentId: learn_covers}`.

**Validation rows.**
- a group with `maxActive < 1` (NumericRange, kept);
- a null member (NullEntry, kept);
- a member not declared on the group's own scope (new: `MemberOffScope`, error) - a group lists
  what its scope declares, so the active set and the member share a home and a lifetime;
- the same member listed twice by one group (DuplicateMember, error);

Events keep their own one-active record; its lifecycle (timer, entry, exit) is not a membership.
Out of scope here.

### B. Consumption is a list on the bar, and a stat (sentences 1, 5, 6, 7, 9; 12.2, 12.7)

**Definition.** `BarDefinition.consumes : List<ConsumesEntry { CurrencyDefinition currency;
BigNumber amount; }>` replaces `fillCurrency`. `amount` is per unit of fill. An empty list is a bar
that fills from time; `fillRate` stays the bar's own speed in units per second, and rate entries
paying the bar still add to it at the draw (12.7).

**The draw.** For each drawing bar, `want = rate x dt`. For each consumes entry, `cover =
balance(currency) / (amount x consumptionFactor)`; the fill is the smallest of `want` and every
entry's cover, and each entry then spends `fill x amount x consumptionFactor` from its currency's
home. No entries: the fill is `want`. Nothing banked in any one entry fills nothing, and the bar
stalls until production refills it - the stall pool-fed covers already show.

**The stat.** `Stat.Consumption = "consumption"` joins cost and autobuy as an effect address no
produces entry may name. The compiler files one plan per consumes entry at the bar, coordinate
(bar id or tag, the consumed currency, consumption), stage 1 only as cost is: a currency-total
buff on Rehearsal means its supply and must not scale its drain. `{target: cover_1, currencyId:
rehearsal, stat: consumption, x0.1}` is "this bar consumes 90% less Rehearsal"; a wildcard on the
stat is efficiency on everything that drinks. Range: a factor below zero is NumericRange as every
factor is; zero is legal and means free.

**The fill-rate plan** loses its currency coordinate: (bar, null, rate), which is what a time-fed
bar compiles today. No authored effect narrows a bar's rate by currency, so nothing changes
meaning.

**`BarPlan`** carries the consumes entries with their currency homes (`PoolHomes`) in place of the
single `PoolHome`, and `BarFill` the same; `TickReport.RecordDraw` is called once per entry.

**Importer.** `BarDto.consumes : List<ConsumesDto { currency, amount }>`; `fillCurrency` is gone
and refused as an unknown key. Chapter 1's covers author `consumes: [{currency: rehearsal, amount:
1}]` with their `fillRate: 2`, the same drain as today. Chapter 2's gigs author no list.

**Validation rows.**
- a consumes entry's currency off the bar's chain (UnresolvedReference, the row `fillCurrency`
  had);
- a null entry (NullEntry);
- `amount <= 0` (NumericRange) - a nonpositive per-unit cost is a bar that drinks nothing or mints;
- the same currency twice in one list (DuplicateMember, the shape A uses).

### C. Idle pays bars by computing the value (sentences 4, 10, 11; section 9, 12.9)

The offer, the dialog, OK, Double It, the stamp and the settle-on-switch rule do not change.
Nothing runs the tick over the window. The offer computation gains bars, computed per segment the
way rate lines are, under the same idle context.

**Per segment, after the rate lines**, for every bar in the chapter's subtree that is on (A),
available, and has room:

1. **Wanted fill** = fill rate (its own plus rate entries paying it, under the idle context, so
   idle_base's rate x0.5 halves it as it halves every rate) x effective seconds.
2. **Cap by consumption** (B): what each consumed currency can give is its balance at the stamp
   plus this segment's rate line for it, minus what earlier bars in the settlement order already
   took in this offer, divided by the per-unit consumption under its factor. The fill is the
   smallest of the want and every entry's cover. A time-fed bar takes the whole want. The
   currency's line is reduced by what the bar takes - a consuming bar never pays; it drinks from
   the line.
3. **Completions** from progress plus fill over `fillAmount`: the floor for a bar whose
   `repeatWhen` holds under the segment context, residual kept; one if crossed for a bar with no
   `repeatWhen`, then full, the fill past the threshold not drawn; one if crossed for the manual
   team, then zero and off.
4. **Payout lines**: every `FireGeneratorYield` in the bar's `onComplete` resolves its yield once
   under the segment context (count-scaled, multiplied, the resolution the live completion uses),
   times completions, into the currency and count lines.
5. **Progress** is always carried, crossed or not (sentence 11): a bar that completes nothing
   over the window still moves.

The order is the tick's settlement order, so two bars on one pool see the same sequence the live
draw gives them.

**The offer** gains `bars : List<IdleBarLine { bar, home, progress, completions }>` beside its
lines. Nothing is written at computation.

**The claim** pays the lines as today, then for each bar entry writes `barProgress`, bumps
`fillCounts` by the completions, removes a manual bar from its active sets, and runs the bar's
`onComplete` once per completion at the claim's context with the `FireGeneratorYield` actions
skipped - those were the lines. A cover finished while away grants its `AddModifier` here.

**Double It** doubles the lines and leaves the bar entries alone: twice the Cash, never twice the
cover. A Pass owner's lines are computed doubled as today, bar payout lines included. A kill under
the dialog loses nothing; the next entry recomputes from the same stamp, as now.

**idle_base is unchanged.** Its rate x0.5 halves a bar's fill, so a booked gig completes half as
often and each lump is whole: half the income, one rule, no new line.

**Rejected shapes**, so they are not rebuilt: running `TickSystem` over the window for real and
presenting the report (changes the payout contract, sentence 10); running it on a copy of the
subtree and again on OK (the same, plus a clone); a design distinction between time-fed and
pool-fed bars for idle (sentence 4 - the pool is a dependency the same computation resolves first,
not a different kind of bar).

**Section 9 and 12.9 rewrite**: "bar progress never accrues" and "a yield never accrues" are
deleted. What accrues is every rate and every bar's fill, capped by what it consumes; a completion
crossed while away pays what it fires. 12.7's "time away fills the pool, presence spends it" goes
with them.

## Content on the result

- **Chapter 1 typed** (`Assets/Content/chapter-01.json`): covers to `bars` with `consumes`,
  `learn_covers` a group of the three, the Rehearsal Space module bound to it. Chapter 1's
  behavior is unchanged except that a cover left running fills while away from the Rehearsal that
  accrues, and completes if it crosses.
- **Chapter 2 authored** (`Docs/chapter-02-open-mic.json`): the calendar is a group of the six
  gig bars, `maxActive: 6`; the standing-booking, idle and "first idle income" claims become true
  as written. Its typed translation is the next slice after this plan lands and is not part of it.
- **Chapter 1 friendly** (`Docs/chapter-01-garage.json`, `chapter-01-content.md`): the bar block
  and its group in the new spelling.

## Tests

Runtime tests convert their call sites (fixtures may build groups and consumes lists; no second
implementation of the draw or the offer). New coverage, one test each unless noted:

- A: a generator listed by a group produces nothing until selected, and its fired yield pays
  nothing; a producer, a currency and an upgrade the same (four); a member of two groups is on only
  when both hold it; the toggle adds, removes, and refuses into a full group; the manual team leaves
  every group listing it; `BarsCompleted` counts bar members only; `MemberOffScope` and
  `DuplicateMember` fire; the save round-trips `activeMembers` and drops an unknown group id with
  the existing warning; the group module renders a mixed group with the select control on every
  row and a pool readout per consumed currency.
- B: a two-entry bar fills the tightest cover and spends both; an empty pool fills nothing; a
  consumption effect x0.1 spends a tenth; a currency-total buff on the pool does not touch the
  drain; a wildcard consumption effect reaches every drinking bar; the fill-rate plan no longer
  matches a currency-narrowed effect; the validator rows.
- C: a time-fed repeating bar over a window pays completions times its yield, halved by idle_base
  through the fill; a consuming bar is capped by the pool's balance plus the window's inflow and
  the pool's line drops by the take; two bars on one pool take in settlement order; a slow bar
  carries progress with no completion; a fill-once bar completes once and stops drinking; the
  manual team completes once and is off after the claim; the claim writes progress and fill counts
  and runs a non-payment completion action once per completion; Double It doubles lines and not
  progress; a Pass owner's bar lines are computed doubled; an unclaimed offer recomputes from the
  stamp with no progress written.
- Chapter 1 content and walkthrough tests updated to the new spelling, plus one walkthrough row: a
  cover left selected across an absence has fill on return.

## Docs

Design 12.2 (the stat list gains consumption), 12.3 (`activeMembers`), 12.7 rewritten around a
bar that fills at the rates paying it and consumes what its list names, 12.9 and section 9 (idle
pays bars; the offer's bar entries and the claim's writes), 12.11 (the `group` module), 12.13
(`GroupDefinition`, `ConsumesEntry`, `IdleBarLine`, the deleted `BarGroupDefinition` and
`BarGroupUI`), 12.14.5 (the JSON blocks). Build plan: a row for this plan and the chapter 2 bullet
pointing at it. Memory: none beyond what is recorded.

## Out of scope

- Consumption on a generator (a converter). Same list, same stat, when a chapter has one.
- Autobuy over the idle window. The claim buys nothing, as today.
- Finer cutting of an idle segment than the buff boundaries. A four-hour segment deposits its
  production before its bars draw, as a long live hitch does.
- Events as group members. The one-active record stays theirs.
- Chapter 2's typed translation. Its own slice, on this result.

## Status

DONE 2026-09-18 - 910/910 green (+32 net), landed as one slice with chapter 1's content and the design
doc sections above. Three sentences of this plan were wrong and the code follows the design rule
instead: (1) "idle_base's rate x0.5 halves a bar's fill" needed the rate wildcard to reach a bar's fill,
which 12.2's "a bar's fill stays out" clause forbade - the clause dated from fill never accruing while
away, a bar's fill is stage 1 only so the wildcard meets it once, and the clause is deleted; (2) "the
currency's line is reduced by what the bar takes" nets a spend into a deposit, and a deposit writes the
earned total (12.3) - the drink is the offer's own list of DRAWS, the claim deposits the lines and spends
the draws, and the dialog prints each balance's net change signed; (3) no "unpresented content" warning
exists in the widget pass, so the validation row citing one is gone and none was built. Decisions made
in the landing: a group bound by a module needs no displayName (John, 2026-09-02: a group draws no
heading); a window that changed no balance settles on entry with no dialog (John, 2026-09-18: "we should never
show an empty dialog"; I had built the opposite without asking, and the dialog's row list moved onto
the offer as `Changes()` so the session and the screen agree on what there is to show); a completed fill-once bar leaves the active set of every group listing it, live and at the claim, as the manual team does (review finding: the toggle otherwise left a Done cover holding the only slot, so no second cover could ever be chosen from the screen); Double It and the Pass double the lines alone; FireProducer on an off producer pays nothing
rather than throwing.
