# Load-time linking - scope references and the compiled gather

Plan for the build-plan step between 9 and 10, 2026-09-08. Sections cited are
`garage-band-idle-design.md`; Chapter 1's authored text is `chapter-01-content.md`.

## What this step is

One principle: **static wiring is resolved once, when a tree is built, and runtime reads facts
through it.** Definitions, conditions, actions, modifiers, and effects stay static and shared.
What is dynamic is only the facts a scope holds. Today the runtime re-derives the static part on
every read, in two places:

1. **Scope references.** `ResetScope(tier1)` holds the tier1 definition and, at every execution,
   searches the tree from the acting node (`FindInSubtree`) to find tier1's runtime node. The
   reference already names exactly one node in this tree, known the moment the tree is built.
   Five call sites do this: the two event conditions, `ResetScope`, `RestartScope`, `ExecuteRung`.
   `ScreenHost` does it once per chapter build for each section and module scope.
2. **The gather.** `GetRate` walks the foreground subtree to discover which sources pay a
   currency, and `GetMultiplier` walks every chain node and string-matches every effect's
   `target`, `currencyId`, and `stat` against the coordinate to find out whether it applies.
   Which sources exist, where they sit, and which effects can ever match a coordinate are all
   authored and validated. Only whether an effect is LIVE (purchased, stacked, `appliesWhen`
   holding) is a fact.

The fix is a **link pass** at the end of `ScopeState.Build`: it walks the new tree's definitions
once and writes, onto the runtime nodes, what each static reference means in this tree. Nothing is
written into an asset; two games built from the same assets hold separate links. The pass runs in
every build, release included, because it is wiring, not diagnosis. Its failures are content faults
and throw (12.14.7).

Two changesets, in order. Changeset 0 is small and introduces the pass. Changeset 1 is large,
changes every number the tick produces, and uses the pass changeset 0 built.

## The link pass

- **When.** Inside `ScopeState.Build`, after every node exists and its declared facts are seeded,
  before `Build` returns. Save hydration comes after `Build` and never touches a link, because a
  link holds a node and never a payload. Reset swaps a payload and never a node.
- **Resolver.** `Build` keeps a construction-only map from scope definition to the node it just
  created and drops it when it returns. The pass resolves every scope reference through that map.
  No search, and no map survives construction.
- **Storage.** Each `ScopeState` holds a private `links` store. A link is keyed by the static object
  that holds the reference (a `ResetScope` instance, a `SectionDefinition`) and stored at the node
  where that object executes or is evaluated. A read is `node.Link(holder)`, a dictionary read at
  a node the caller already holds. A missing link throws: construction omitted a site, which is a
  code bug, and nothing falls back to a search.
- **Enumeration.** Per node, the pass visits the lists its own definition declares: the rung's
  actions, each event's `onEntry`, `rewards`, and `onEnd`, each trigger's actions, each upgrade's
  actions, each bar's completion actions. Every action gets `Link(LinkContext)`, a virtual on
  `GameAction` that defaults to nothing; the three scope-referencing kinds override it. A chapter
  node additionally links its sections and modules. Lists are not nested - `ExecuteRung` names a
  rung, it does not contain one - so the visit is one pass with no recursion.
- **Checks.** Existence, reach, and kind, exactly the rules 12.12 states for each holder, checked
  before the link is stored. The validator's `Validate` methods keep producing the dev-time report;
  the pass shares their reach predicates and does not keep a second copy of the rules.
- **Save loading.** `SaveSystem.TryDeserialize` today calls `ScopeState.Build` inside its broad
  `catch (Exception)`, which turns any failure into "try the backup". `Build` moves out of that
  try. A content fault propagates as a content fault; a corrupt payload keeps its primary/backup
  behavior.

## Changeset 0 - scope references

### The three actions

`ResetScope`, `RestartScope`, and `ExecuteRung` implement `Link`: resolve the referenced definition,
check reach (self or enclosed; never root for the two resets; a rung present for `ExecuteRung`),
store the node. `Execute` reads `ctx.Scope.Link(this)` and does what it does today on that node:
the recursive clear, the rung through its own gate then the clear, the rung rebased to its node.
The recursive clear stays a recursion over `Children`: that is the parent informing its subtree,
not resolution.

### The reset's own refusal

Design 12.5: a scope holding an armed, unclaimed reward (a record with `goalReached`) refuses to be
cleared, judged from its own facts, and the refusal comes back up the walk the clear goes down.

- `GameAction.Refuses(GameContext ctx)`, virtual, default false. `ResetScope` and `RestartScope`
  answer by asking the target subtree: any `InteriorScopeState` in it with `activeEvent != null &&
  activeEvent.goalReached` refuses. The refusing node's active event is reported with the refusal.
- `Rung.IsOffered` is the offer condition AND no action in the list refuses. The button closes
  before any action runs, so a list never half-executes.
- `Rung.Execute` past a refusal throws, and so does a reset executed directly past one
  (requirement 7).
- `DismissEvent` removes the record before `onEnd` runs, so an `onEnd` carrying
  `RestartScope(tier)` is never refused by its own event. Already the case; unchanged.
- **Feedback** (12.5, 12.11): `RungFeedback` renders a refusal as a leg naming the event by its
  `displayName`, formatted as `Claim your {0} reward first`, which is the text the content authors
  today for the leg being removed. `GateFeedback` stays the pure condition half; the rung layer adds
  the refusal legs after the condition legs.

### The two event conditions

`EventRecordExists` and `EventRewardPending` lose `host`, as 12.4 already spells them. `Evaluate`
walks outward from the acting scope to the first `InteriorScopeState` holding a record and reads
it; no record on the chain is false. `Validate` refuses an acting scope that is root, where the
condition can never hold. Nothing else to check: there is no operand.

Deleted with the operand: `FinalizeStrandedReward`, `CollectRequiredGuards`, the `StrandedReward`
check (12.12 already says no load-time check stands in for the reset's refusal); the `host` field on
the two importer DTOs and their `ResolveScope` calls; the importer comment at `ResolveScope` that
describes the runtime reading scope references downward.

### Content

`chapter-01.json`: the chapter rung's `Not(EventRewardPending(host: tier1))` leg is removed - a
parent cannot read a child's record, and the refusal is the reset's own. Tier1's rung keeps its
`Not(EventRewardPending)` leg, now hostless, with its `uiText`. Re-import `ScriptableObjects/ch1`.
`chapter-01-content.md` lines 171, 177, 187, 221, and 320 spell the old legs and the stranded-reward
guard; they change to the reset refusal in the same changeset.

### Section and module scopes

`ScreenHost.Resolve` searches the chapter subtree for each section's and module's scope at every
chapter build. The pass links them at the chapter node instead, keyed by the `SectionDefinition` or
`ModuleDefinition`, with the 12.11 reach check (the chapter or a descendant) moving from the
runtime throw to the link. `ScreenHost.Build` reads `chapter.Link(section)`.

### appliesWhen

`ScopeState.Applies` evaluates `modifier.appliesWhen` at `origin.Rebase(this)`: the node the
modifier is applied to, which holds the stack or the permanent membership. The idle circumstance
and the clock ride the rebase. Validation already judges `appliesWhen` from each application site
(`FinalizeModifierChecks`), so this makes execution agree with it. Effect formulas keep the origin
context. One behavior change and no shipping content depends on it: a chapter stack's gate reads
chapter facts, not the gathering tier's.

### Deletions and what stays

- `ScopeState.FindInSubtree` is deleted. No production code navigates a tree that way afterward.
  The 54 test call sites move to a navigation helper in the test assembly, which is a fixture over
  `Children` and not a copy of anything the runtime does.
- `ScopeState.FindOnChain` stays: `AddModifier` and `RemoveModifier` target self or an ancestor,
  and `GameSession.InForeground` compares session state. Both are the outward walk.
- Every other outward walk (balances, flags, upgrades, `FindModifier`, `DeclaringScope`) and every
  subtree iteration (the rate gather, bar demand, the sweep, event timers, `BlockedByEvent`, the
  save's child match by id at the load boundary) is untouched in this changeset.

### Tests

- The tree fixture and every test that builds through `ScopeState.Build` gets links for free. A
  test that authors a scope-referencing action after building rebuilds before exercising it; the
  fixture already documents rebuild-from-content for added chapters.
- `GameActionTests`: the three actions act on the linked node; a reset over an armed reward is
  refused and a forced execution throws; the refusal reports the event.
- `ConditionTests` / `EventSystemTests`: the two conditions read outward with no operand; false at
  root's chain; a tier's record visible to the tier and not to its chapter.
- `Chapter1WalkthroughTests`: the capstone closes while tier1 holds an armed reward, with the
  refusal text, and opens after dismissal; every payout unchanged.
- `ContentValidatorTests`: the stranded-reward cases are deleted; link-time reach failures are
  content faults from `Build`.
- `SaveSystemTests`: a save loads onto a linked tree; a content fault in `Build` is not answered by
  the backup.
- `ScreenHostTests`: sections and modules over linked scopes; a module scope outside the chapter
  fails at build.
- A second tree from the same definitions, alive beside the first: every action and condition in
  each reads and mutates only its own nodes.

## Changeset 1 - the compiled gather

### What the pass compiles

Per node, from static content only, in the order today's walks visit it:

- **Effect links.** One per (effect, carrier, node) that can ever apply at this node. A link
  holds the effect, the node whose fact decides it, and how it goes live: an upgrade (purchased
  at that node), a permanent modifier (always, subject to `appliesWhen`), a granted modifier (the
  stack count at that node, subject to `appliesWhen`; every modifier declared on the chain is a
  candidate, since a grant can only add a stack for one of those), a repeating bar's per-fill entry
  (the fill count and growth kind), an event handicap (the node's record naming that event). The
  stacking kind and the permanent-plus-granted merge rule ride the link.
- **Source plans.** Per source declared at the node (producer or generator), per (currency, stat)
  its entries pay: the entries, and the stage-1 effect links gathered from the declaring node
  outward. Matching is decided once, by today's `Matches`, at link time.
- **Currency stage plans.** At each currency's home, per stat: the stage-2 links gathered from the
  home outward, the currency as owner so its tags match (8.2).
- **Chapter rate plan.** At each chapter node, per currency any source in its subtree pays at
  `Stat.Rate`: the contributors (node, source, plan) in tree order then declaration order, and the
  home node. This is what `RatePairs` rebuilds every segment.
- **Bar plans.** At each chapter node, the bars in its subtree in settlement order, each with its
  node, group, pool home, and the stage-1 links for its fill rate (stage 1 only, as today).
- **game_speed plan.** At each chapter node, the wildcard links for the tick's owner-less,
  currency-less `Stat.GameSpeed` query.

### What stays dynamic

Entry conditions, `appliesWhen`, `activeWhen`, purchased latches, stack counts, fill counts, event
records, formula factors. A tick reads each of them at the moment of the query, against the
segment-start snapshot, exactly as now. A candidate list is fixed; whether each candidate is live
is read every time.

### Consumers

`GetRate`, `RatePairs`, `SourceTerm`, `CurrencyStage`, `GetMultiplier`, both `MultiplierFor`
overrides, `ResolveUnit` (`ResolveYield`, `UnitRate`, `FireProducer`), `BarSystem.ResolveDemand` and
`Rate`, the tick's `game_speed` read, and the idle offer in `GameSession` read the plans. A caller
that holds a source and a context reaches the source's plan through the declaring node, found
outward as today. The order of multiplication and the order of deposits are the order the plans
were compiled in, which is today's order, with one exception: granted stacks are visited today in
dictionary insertion order and afterward in declaration order. The tests decide whether any number
notices.

### Runtime deletions

`Matches`, `FindCurrencyHome`, and `FindModifier` become link-time functions or go. No tick-time
subtree walk, chain walk, or string compare remains in the gather. `DeclaringScope` stays: it is
the outward walk from an acting scope to the node whose list holds a definition.

### Not converted

The sweep, event timer and edge collection, `BlockedByEvent`, `ClearRecursive`, and the save's
`Apply` iterate a subtree to inform or ask every node. They resolve nothing and stay.

### Tests

164 test lines author producers, modifiers, upgrades, or entries onto definitions, many after the
fixture has built its tree. Those tests rebuild after authoring. The proof of the changeset is the
existing tick, resolution, bar, idle, and walkthrough suites producing the same numbers over the
compiled plans, plus: a plan for a chapter with two tiers each applying the same modifier reads
each tier's stack and gate; a granted stack absent from the plan's candidates is a content fault
at load, not a silent zero.

## Acceptance checks

Planned checks, run only when John instructs a test run.

- Every production `FindInSubtree` call is gone and the method with it. `grep` over
  `Assets/Scripts` finds no runtime subtree search for a named node.
- Chapter 1 by hand and through `GameSession`: event start, the capstone closed over an armed
  reward with the refusal text, dismissal, the capstone opening, the release, every payout as the
  content doc states it.
- Two games from one content set, with a save round trip on each, never touch each other's nodes.
- A content fault in `Build` on a saved game propagates; the backup is not tried.
- After changeset 1: every tick and walkthrough number unchanged; a segment performs no `Matches`
  call and no chain walk.

## Landing

Two changesets, each compiling and green on its own. Changeset 0 first; step 10's slice A starts
after changeset 1 lands, or after changeset 0 if John orders it so.

On changeset 0 landing: design 12.3 loses "neither points at the other, so the walk is the only
link" and gains the link pass beside the facts; 12.11 says layout scopes are linked at the chapter;
12.12 loses the stranded-reward guard text where it survives; 12.13 gains `ScopeLinks.cs` under
Core; 12.14.8 states that explicit scope references are resolved by the link pass at load and read
by reference afterward; the build-plan row records the landing. On changeset 1 landing: 12.6 and
12.2 describe the compiled gather; `Producer.cs`'s 12.13 comment follows.
