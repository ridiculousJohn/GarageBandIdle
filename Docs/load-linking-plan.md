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

Two changesets, in order. Changeset 0 introduces the pass and resolves scope references. Changeset 1
compiles the gather over the pass changeset 0 built; every number the tick produces must come out
the same.

This document is the whole specification. Where it names a class, a method, or a place, that is
where the thing lives; where it states an order or a rule, that is the rule, and "as the code does
today" is never the answer to anything below.

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
- **Enumeration.** A scope definition enumerates its own action lists ONCE, in one method on
  `ScopeDefinition` (`InteriorDefinition` adds its own): the rung's actions, each event's `onEntry`,
  `rewards`, and `onEnd`, each trigger's actions, each upgrade's actions, each bar's completion
  actions, each paired with its site string. The link pass iterates that enumeration; so does the
  validator's `ValidateActionList` walk. Neither spells the sites out by hand, and no executor does
  either. A new kind of list is added in that one method and nowhere else. Every action gets
  `Link(LinkContext)`, a virtual on `GameAction` that defaults to nothing; the three
  scope-referencing kinds override it. A chapter node additionally links its sections and modules.
  Lists are not nested - `ExecuteRung` names a rung, it does not contain one - so the visit is one
  pass with no recursion.
- **Checks.** Existence, reach, and kind, exactly the rules 12.12 states for each holder, checked
  before the link is stored. The validator's `Validate` methods keep producing the dev-time report.
  The reach predicate exists ONCE: one static function over "a node and how to reach its parent",
  called by `ValidationContext.InSubtree` (parent = the validator's parent map) and by
  `LinkContext.ResolveEnclosed` (parent = `ScopeState.Parent`). Two hand-written chain walks, one
  over definitions and one over nodes, is the second copy this forbids.
- **Entry.** `ScopeLinker.Link(root, nodes)` in `Core/ScopeLinks.cs` is the one pass `Build` calls.
  It links scope references first, then (changeset 1) calls `GatherCompiler.Compile(root)` in
  `Core/GatherCompiler.cs`, so the plans are compiled over a tree whose links already exist and
  `Build` has exactly one post-construction step.
- **Save loading.** `SaveSystem.TryDeserialize` today calls `ScopeState.Build` inside its broad
  `catch (Exception)`, which turns any failure into "try the backup". `Build` moves out of that
  try. A content fault propagates as a content fault; a corrupt payload keeps its primary/backup
  behavior.

## Changeset 0 - scope references

### The three actions

`ResetScope`, `RestartScope`, and `ExecuteRung` implement `Link`: resolve the referenced definition,
check reach (self or enclosed; never root for the two resets; a rung present for `ExecuteRung`),
store the node. `Execute` reads `ctx.Scope.Link(this)` and acts on that node: `ResetScope` calls
`ClearSubtree`; `RestartScope` runs the node's rung through `TryExecute` (its own gate, refusals
included) then `ClearSubtree`; `ExecuteRung` runs the rung rebased to the node through `TryExecute`.

The two subtree operations a reset performs - clear every node below, and ask every node below
whether it refuses - are SCOPE operations and live on `ScopeState`, beside `Clear` and
`RefusesClear`: `ClearSubtree(DateTime nowUtc)` and `RefusalInSubtree(ScopeState ignoring = null)`,
each a recursion over `Children`, self first. `ignoring` is the one node whose own record is not
asked (its children still are); it exists for dismissal, below, and nothing else passes it. That is
the parent informing its subtree and the subtree answering, not resolution. An action calls them;
no action carries the walk.

`Refusal` is a sealed class in `Core/ScopeState.cs` beside `ActiveEvent`: the refusing
`InteriorScopeState`, its `ActiveEvent` record, and the `EventDefinition` the record names, read
from the host's own `events` list (null only for a record the save filter should have dropped).
`InteriorScopeState.RefusesClear` is the only constructor call.

### The reset's own refusal

Design 12.5: a scope holding an armed, unclaimed reward (a record with `goalReached`) refuses to be
cleared, judged from its own facts, and the refusal comes back up the walk the clear goes down.

The rule is about RUNNING AN ACTION LIST, so it lives in the one place that runs action lists, and
nowhere else. There is exactly one such place.

- **The runner.** `ActionList` in `Core/ActionList.cs`, static, three operations over
  `(IReadOnlyList<GameAction> list, GameContext ctx, ScopeState ignoring = null)`:
  - `Refusal Refuses(list, ctx, ignoring)` - the first refusal any action in the list reports, in
    list order, or null. Null entries are skipped. This is the ONLY code that asks actions whether
    they are refused.
  - `bool TryRun(list, ctx, ignoring)` - false without executing anything when `Refuses` answers,
    otherwise executes every action in order and returns true. A list runs whole or not at all.
  - `void Run(list, ctx, ignoring)` - throws when `Refuses` answers (requirement 7), otherwise
    executes. For a caller that has already asked.
  `ignoring` is passed straight through to each action; only dismissal ever sets it.
- **The answer.** `GameAction.Refuses(GameContext ctx, ScopeState ignoring)`, virtual, default
  null. `ResetScope` and `RestartScope` answer `ctx.Scope.Link(this).RefusalInSubtree(ignoring)`.
  `InteriorScopeState.RefusesClear` answers for its own record: `activeEvent != null &&
  activeEvent.goalReached` refuses. No other class computes a refusal. `Execute` on the two resets
  asks `RefusalInSubtree()` itself before clearing and throws on an answer, so a reset run outside
  any list is as fail-closed as one inside.
- **The six sites.** Every place that runs an action list calls the runner and NOTHING else - no
  site keeps a `foreach (action) action.Execute(ctx)` of its own, and no site asks `Refuses` on an
  action directly. Each site's existing guard gets the runner's answer folded in:
  1. `Rung.IsOffered` = `offerCondition != null && offerCondition.Evaluate(ctx) &&
     ActionList.Refuses(actions, ctx) == null`. `Rung.Execute` = `ActionList.Run`. `Rung.Refusals`
     (for feedback) is deleted; feedback reads `ActionList.Refuses(rung.actions, ctx)` itself.
  2. `EventSystem.CanStart` additionally requires `Refuses(evt.onEntry, hostCtx) == null`; `Start`
     runs `onEntry` through the runner.
  3. `EventSystem.CanDismiss` additionally requires both ending lists unrefused: `Refuses(rewards,
     hostCtx, ignoring: host)` when the record's `goalReached` is set, and `Refuses(onEnd, hostCtx,
     ignoring: host)` always. Dismissal removes the host's own record before either list runs, so
     the question is asked as if that record were already gone - `ignoring: host` is that, and it
     is the only place the argument is ever non-null. `Dismiss` removes the record, then runs
     `rewards` (when `goalReached`) and `onEnd` through `Run` with no exclusion, since the record is
     gone. A refused dismissal changes nothing.
  4. `Sweep.Run` collects a trigger as eligible only when its condition holds AND
     `Refuses(trigger.actions, ctx) == null`, judged at collection time like the condition; a
     refused trigger is neither latched nor run and fires on a later pass once the refusal lifts.
     Execution goes through `Run`.
  5. `Purchasing.CanBuy(upgrade)` additionally requires `Refuses(upgrade.actions, declaringCtx) ==
     null`; `Buy` latches, then runs the list through `Run`.
  6. `BarSystem` settlement: before any fill math for a segment, every SELECTED bar whose
     `onComplete` is refused (`Refuses(bar.onComplete, ctx at the bar's declaring node)`) is
     excluded from the segment - it draws nothing from the pool and its progress does not move -
     and it resumes on the first segment after the refusal lifts. Whether a bar would complete is
     not decidable before the fill math, which is why the exclusion is decided before it. A
     completion runs through `Run`.
  Adding a seventh list kind means adding it to the definition's list enumeration (above) and
  calling the runner. There is no other step, so there is no other step to forget.
- **Forced execution throws.** `ActionList.Run` past a refusal throws, and so does a reset executed
  directly past one (requirement 7).
- **Feedback** (12.5, 12.11): `RungFeedback.RefusalText` renders a refusal as a leg naming the event
  by its `displayName`, formatted as `Claim your {0} reward first`. `GateFeedback` stays the pure
  condition half; the rung widget adds the refusal legs after the condition legs, reading
  `ActionList.Refuses`. The refusal labels are created and removed per refresh, after the condition
  labels, so a rung with nothing refused shows exactly its condition legs. `EventUI` renders a
  refused start or dismissal the same way.

The defect this section exists to forbid: a rule about action lists attached to one caller of
action lists. The rung asking on its own while five other loops never ask is that defect.

### The two event conditions

`EventRecordExists` and `EventRewardPending` lose `host`, as 12.4 already spells them. The outward
read is ONE method, `GameContext.EventRecord()`, beside `IsFlagSet`: walk from the acting scope
outward to the first `InteriorScopeState` whose `activeEvent` is non-null and return it; null when
none. `EventRecordExists.Evaluate` is `EventRecord() != null`; `EventRewardPending.Evaluate` is
`EventRecord()?.goalReached == true`. Neither condition walks anything itself. `Validate` on both
refuses an acting scope that is root (`ScopeReach`), where the condition can never hold, through
one shared static on `EventRecordExists`. Nothing else to check: there is no operand.

Deleted with the operand: `FinalizeStrandedReward`, `CollectRequiredGuards`, the `StrandedReward`
check (12.12 already says no load-time check stands in for the reset's refusal); the `host` field on
the two importer DTOs and their `ResolveScope` calls; the importer comment at `ResolveScope` that
describes the runtime reading scope references downward.

### Content

`chapter-01.json`: the chapter rung's `Not(EventRewardPending(host: tier1))` leg is removed - a
parent cannot read a child's record, and the refusal is the reset's own. With one leg left, the
capstone's `offerCondition` becomes the bare `CurrencyAtLeast(ch1_records, 30)`, not an `All` of
one. Tier1's rung keeps its `Not(EventRewardPending)` leg, now hostless, with its `uiText`.
Re-import `ScriptableObjects/ch1` headlessly (`unity-headless-verify-loop` in the repo memory
store); the regenerated `rid` values touch every asset carrying a condition or action and are not
content change.
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
  test that authors a scope-referencing action after building rebuilds before exercising it:
  `TestTree.Rebuild()` rebuilds `Root`/`Ch1`/`Tier1` from the definitions as they now stand. A
  LOOSE action - `new ResetScope { ... }.Execute(ctx)` - cannot run at all, because nothing linked
  it; `TestTree.Author(actingScope, action)` files it in a closed trigger (`Not(Always)`) declared
  at the acting scope, rebuilds, and returns it, so a reach fault surfaces from `Build` exactly as
  for authored content. Subtree navigation in tests is `TestNavigation.Node(top, definition)`, a
  fixture over `Children`.
- `GameActionTests`: the three actions act on the linked node; a reset over an armed reward is
  refused and a forced execution throws; the refusal reports the event.
- **One test per site, six in all**, each with an armed reward under the list's reset target: the
  site's guard answers no (`IsOffered`, `CanStart`, `CanDismiss` with a chapter-hosted event over an
  armed tier, trigger not latched and not run, `CanBuy`, bar not completed), no fact moved, and the
  guard answers yes after the record is dismissed. A seventh: a list whose FIRST action pays and
  whose second is a refused reset pays nothing - all or nothing at the runner.
- `ScopeDefinition` enumerates its action lists: a test authors one of each kind and asserts the
  enumeration names all of them with their sites; the validator and the linker are exercised
  through it by every other test.
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

### The one question

Every multiplier the game computes is one question asked at one node: *at this origin, for this
owner, this currency, and this stat, which effects can ever apply, and in what order?* Today
`GetMultiplier` answers it by walking the chain outward and, at each node, string-comparing every
effect on every carrier against the coordinate. Which effects CAN match is static. Only whether each
one is LIVE is a fact.

So changeset 1 compiles two things, and only two:

1. **A coordinate plan** - the compiled answer to that question for one (origin node, owner,
   currency, stat). Every multiplier read in the game is a coordinate plan read. There is no
   "stage-1 plan", "currency-stage plan", "bar plan", or "game_speed plan": those are the same plan
   asked with a different owner, and naming them separately is naming instances instead of the
   thing.
2. **A chapter's contributor plan** - which sources in the chapter's subtree pay which currency at
   `Stat.Rate`, in what order, and where each currency is homed; and the chapter's bars in
   settlement order with their groups and pool homes. This is the aggregation `RatePairs` and
   `BarSystem.ResolveDemand` rediscover every segment by walking the subtree.

Nothing else is compiled. What is dynamic stays dynamic: entry conditions, `appliesWhen`,
`activeWhen`, purchased latches, stack counts, fill counts, event records, formula factors. A plan
fixes the candidates; each candidate's liveness is read every time, against the segment-start
snapshot.

### Enumeration - what a scope declares, stated once

The compiler never spells out the kinds of thing a scope declares. The definition does, once, and
the compiler and the validator both iterate that; the gather loops this changeset deletes were the
hand-spelled copies:

- `ScopeDefinition.Sources()` - its producers and generators, each with the count fact that scales
  it (a producer is 1; a generator is `generatorCounts[id]`).
- `ScopeDefinition.EffectCarriers()` - every (carrier, effect) pair this scope can apply, each with
  its LIVENESS KIND: an upgrade (live when `purchasedUpgrades` holds its id at this node), a
  permanent modifier (live always, subject to `appliesWhen`, merged with this node's stack for the
  same modifier through its stacking kind), a granted modifier (live when `modifierStacks` holds a
  count at this node, subject to `appliesWhen`, scaled by the stacking kind; the candidates are the
  modifiers declared on this node's chain, since a grant can only stack one of those), a repeating
  bar's per-fill entry (live when `fillCounts` holds a count, scaled by the growth kind).
  `InteriorDefinition` adds an event handicap (live when the node's record names that event).
- The order of `EffectCarriers()` IS the multiplication order at a node: upgrades, permanent
  modifiers, granted modifiers, bar cascades, then handicaps, each kind in declaration order,
  effects in declaration order within a carrier. That is a rule stated here, not "today's order".
  The one behavior change: granted stacks are visited today in dictionary insertion order and
  afterward in declaration order. The tests decide whether any number notices.

Adding a sixth carrier kind or a third source kind is an entry in one of these two methods. There is
no other step.

### The effect link

One class, `EffectLink`, per (carrier, effect, node) that a coordinate plan holds: the effect, the
node whose fact decides it, and its liveness kind. It has one operation, `Factor(GameContext
origin)`: read my fact at my node, return `BigNumber.One` when I am not live, otherwise the effect's
factor (constant or formula, formula evaluated at the ORIGIN context, 12.6) scaled by my count
through my stacking or growth kind. `appliesWhen` is judged at the link's node via
`origin.Rebase(node)`, as changeset 0 already does. The liveness kind is the link's own; no reader
switches on carrier kind, and nothing but a link reads a carrier's fact.

### Compiling a coordinate plan

For a query (origin node, owner, currency, stat): walk outward from the origin; at each node take
`Definition.EffectCarriers()` in order; keep every effect for which `Producer.Matches(effect.target,
effect.currencyId, effect.stat, owner, currency, stat)` holds - the selector rule of 12.2, by id or
tag, unchanged. The kept links, chain order then carrier order, are the plan. `Matches` runs at
compile time only.

Which queries exist is decided by the content, and the compiler builds exactly those:

- For each source at its declaring node, for each of its entries: owner = the source, currency and
  stat = the entry's. (Stage 1.)
- For each currency at its home, for `Stat.Rate` and `Stat.Yield`: owner = the currency, so its own
  tags match (8.2). (Stage 2.)
- For each bar at its declaring node: owner = the bar, currency = its fill currency, stat =
  `Stat.Rate`. (The bar's fill rate; stage 1 only - a bar's fill has no currency stage, 12.7.)
- For each chapter node: owner null, currency null, stat = `Stat.GameSpeed`. (The tick's read.)

Plans are stored in the same per-node `links` store changeset 0 introduced, keyed by ONE static
holder object at the node the query is asked from, so `node.Link(holder)` is the read and a miss
throws as for every other link:
- the `ProducesEntry` instance, at the source's declaring node (stage 1);
- the `CurrencyDefinition`, at its home, holding a `CurrencyPlans` object with the `Rate` and
  `Yield` plans (stage 2) - one key, both stats;
- the `BarDefinition`, at its declaring node (its fill rate);
- `GatherCompiler.GameSpeed`, one static readonly object, at every chapter node.
The store's value type widens from `ScopeState` to `object` for this; `Link<T>(holder)` casts and
throws on a wrong kind, which is a code bug like a miss.

### Compiling a chapter's contributor plan

At each chapter node, stored under the chapter's own definition: for each currency any source in
the subtree pays at `Stat.Rate`, the contributors as (node, source, its rate-entry plans) in tree
order (parent before child) then `Sources()` order, and the currency's home node found outward from
the paying node. And the bars in the subtree in settlement order - scopes parent before child, then
`barGroups` in declaration order, then bars in declaration order - each with its node, group, pool
home, and its coordinate plan.

### What reads what

Every consumer of the gather, named. A consumer holds a node and reads a plan off it; none walks a
chain, iterates a subtree, or compares a string.

- `Producer.SourceTerm` - the entry's stage-1 plan at the declaring node.
- `Producer.CurrencyStage` - the currency's stage-2 plan at its home.
- `Producer.GetRate` - the chapter's contributor plan for the currency: sum each contributor's term,
  times the currency stage.
- `Producer.RatePairs` - the contributor plan's currency list; it no longer walks.
- `Producer.ResolveUnit` (`ResolveYield`, `UnitRate`, `FireProducer`) - the source's entry plans
  and the currency stage plans.
- `BarSystem.ResolveDemand` - the chapter's bar order from the contributor plan.
- `BarSystem.Rate` - the bar's coordinate plan.
- `TickSystem` - the chapter's `GameSpeed` plan, `RatePairs`, `ResolveDemand`.
- `GameSession`'s idle offer - `RatePairs` and `GetRate` under the idle context; the circumstance
  rides the context into every `Factor` call.
- `CurrencyReadout` (UI) - binds to a currency's home; it takes the home from the contributor plan
  of the chapter it renders, so the bind is a plan read and not a walk.
- `SourceTerm`'s `activeWhen` check - the home node is on the entry's plan.

### Runtime deletions

`ScopeState.MultiplierFor` and its interior override (the per-node gather `GetMultiplier` walked
the chain to call), `ScopeState.SourceTermsFor`, `Producer.GetMultiplier`'s chain walk (it becomes
"read the plan, multiply each link's `Factor`"), `Producer.Matches` at runtime (compile-time only),
`Producer.FindModifier` (the granted link holds its modifier), `Producer.FindCurrencyHome` (every
home is on a plan). `DeclaringScope` stays: it is the outward walk from an acting scope to the node
whose list holds a definition, and every command starts there.

### Not converted

The sweep, event timer and edge collection, `BlockedByEvent`, the subtree clear and refusal on
`ScopeState`, and the save's `Apply` iterate a subtree to inform or ask every node. They resolve
nothing and stay.

### Tests

164 test lines author producers, modifiers, upgrades, or entries onto definitions, many after the
fixture has built its tree. Those tests rebuild after authoring. The proof of the changeset is the
existing tick, resolution, bar, idle, and walkthrough suites producing the same numbers over the
compiled plans, plus:

- `Sources()` and `EffectCarriers()`: a scope authored with one of every kind enumerates all of
  them, in the stated order; an interior scope adds its handicaps and a root does not.
- Every `EffectLink` liveness kind: not live reads `One`; live reads the effect's factor scaled as
  its kind says; the permanent-plus-granted merge is one application under `Replace`.
- A coordinate plan for a chapter with two tiers each applying the same modifier holds a link per
  tier, and each reads its own tier's stack and gate.
- Every plan a consumer reads exists after `Build`: a query the content never authors has no plan,
  and a consumer asking for one is a thrown miss, never a silent `One` or zero.
- Two trees from one content set compile separate plans and read separate facts.

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
link" and gains the link pass beside the facts; **12.5's "a rung asks that question of every reset
in its list as part of `IsOffered`" becomes "the action-list runner asks it before any list runs -
rung, event entry and ending, trigger, upgrade, bar completion alike - and a list runs whole or not
at all"**; 12.11 says layout scopes are linked at the chapter; 12.12 loses the stranded-reward guard
text where it survives; 12.13 gains `ScopeLinks.cs` and `ActionList.cs` under Core; 12.14.8 states
that explicit scope references are resolved by the link pass at load and read by reference
afterward; the build-plan row records the landing. On changeset 1 landing: 12.6 states the
compile-once rule the way 12.14.8 states the link rule - the candidates for every multiplier are
fixed when the tree is built and only liveness is read at gather time; 12.2 describes the
coordinate plan and the contributor plan; 12.13 gains `GatherCompiler.cs` and `EffectLink.cs` under
Core and loses `MultiplierFor`/`SourceTermsFor` from `ScopeState`'s description; `Producer.cs`'s
12.13 comment follows.
