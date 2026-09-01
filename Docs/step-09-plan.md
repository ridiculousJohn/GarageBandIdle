# Step 9 Plan - UI layer

Design for build-plan step 9. Sections cited are `garage-band-idle-design.md`; Chapter 1's
authored sections are `chapter-01-content.md` section 12, which is their single home.

## What step 9 is

The game gets its screen. All of 12.11 lands: the authored layout
(`SectionDefinition`/`ModuleDefinition` on `ChapterDefinition`, the `ModuleRegistry`), the coarse
two-trigger refresh, the rung feedback contract (uiText legs, progress rendering, payout preview),
and interpolation. Three deferrals from earlier steps land with it: the `sections` JSON block step
8 explicitly held back, the chapter select (retiring `GameBoot.EntryChapter`'s first-chapter
stopgap), and the idle dialog rendering of the offer the session has held correctly since step 7.

The proof is Chapter 1 playable by hand: fresh install, the select, the reveal ladder, covers,
the release at 50 fans, the Garage Jam chain, the capstone - every interaction through the 12.11
entry points that already exist. The command surface gains nothing; the UI is a consumer. The
runtime grows two things behind that surface: the session takes over tick PACING (the
accumulator, and the flush every command owes it - below), and the tick pipeline records the
report interpolation feeds on.

## Questions before the slices

1. **Framework: how does Ctrl C build its screens?** The design's own vocabulary is prefab-shaped
   - `ModuleRegistry` maps prefab ids via Addressables, "a new widget type is a prefab plus an
   entry" (12.11) - which reads as uGUI (in the project as ugui 2.0.0), not UI Toolkit. The
   reference game decides; slices C-E are blocked on the answer, A and B are not.
2. **Tick cadence.** The driver currently ticks every `Update`, so `Refreshed` fires per frame and
   the "coarse" refresh (12.11) degenerates into a per-frame repaint - and interpolation would
   have nothing to interpolate. 12.13 already calls `TickSystem` "fixed-interval". The mechanism
   is below (the screen host section); the open part is the number: what interval for
   `tickIntervalSeconds`, and what does Ctrl C use for its tick and repaint pacing?
3. **Content doc section 12 underspecifies the modules** - findings to bring back to the content
   doc, not silent fixes:
   - What the `garage_floor` currency header actually lists, and how lines reveal - a module
     `visibleWhen` per line is the existing mechanism, no new visibility rule.
   - The event module's shape. The ladder's `availableWhen` gates are NOT exclusive: after
     `gj1_done` with 15 Records, `garage_jam_1` and `_2` both pass their gates, and after
     `gj2_done` with 30 all three do - no authored fact picks one offered event. Exclusion legs
     (`Not(FlagSet(gjN_done))`), a module listing every available event (which would make
     replays authorable), or a selection rule - the content doc decides which is the design.

## Existing systems this builds on

- **`GameSession`**: the complete command surface (`TryRung`, both `TryBuy`s, `FireProducer`,
  `SetActiveBars`, the event operations, `SwitchChapter`, `ClaimIdle`) and the `Refreshed` event -
  one per completed transaction, none on a refusal, unconditional where the sweep is not. Widgets
  call the commands; only the screen host subscribes to the event (below). The command surface
  gains nothing; the session's growth is orchestration - the tick pacing it takes over from the
  per-frame driver (the cadence section) and holding the tick report out for display.
- **The queries the widgets read**: `Rung.IsOffered`, `Purchasing.CanBuy`,
  `PayoutFormula.Compute`, and `Producer`'s yield resolution for the Jam preview - pure, so
  previews call the same code the execution runs (12.5).
- **`TickSystem`**: the tick pipeline records its realized per-currency and per-bar deltas at
  their mutation sites into a transient tick report (interpolation, below). The session exposes
  the report beside `CurrentOffer`; presentation data, never serialized.
- **`NumberFormatter`**: step 1's display rules, already at `UI/NumberFormatter.cs`.
- **`ScopeState.FindInSubtree`**: a section's evaluation scope resolves definition-to-state
  downward through the one named subtree - the legitimate walk (12.14.8), from the chapter node
  the screen already holds.
- **The importer**: the schema grows the `sections` block, the same incremental contract as every
  family before it.

## The definitions

`SectionDefinition {title, visibleWhen, scope, modules}` and `ModuleDefinition {prefabId,
content?, visibleWhen?, scope?}` are plain `[Serializable]` classes - the `Rung` precedent, not
the `Definition` family. Nothing references a section from outside, so they are inline serialized
data on the owning `ChapterDefinition` (`sections`, ordered), rebuilt wholesale on re-import,
with no id joining the chain namespace. The content doc table's names (`garage_floor`, ...) are
row labels for humans, not authored ids.

**Display text is authored, because nothing else can produce it.** `Definition` holds id + tags,
and "Three-Chord Anthem" is not derivable from `cover_3` - so `Definition` gains a `displayName`
(one field, the schema and importer carrying it), and a section's on-screen header is its
`title`. Required on a CLOSED LIST: the families the step-9 widgets put on screen by
construction - currencies, generators, upgrades, bars, events, and chapters (the select) - plus
`title` on every section; optional everywhere else, because requiring a name on a trigger or a
modifier no widget renders authors junk that reads as design intent. When a later step's widget
renders a new family, that family joins the list in the same changeset - the incremental
contract validation already follows.

Two name sources sit outside the family lists. **Content a module BINDS must carry a
`displayName`** - a per-binding-site check the sections make statically answerable, which is
what names `tap_producer` (the Jam button's label) without putting a family-wide requirement on
producers and forcing junk onto `band`, which nothing renders; any family a future module binds
is covered by the same rule. And **`Rung` gains a required `label`** - the button text ("Play
the Backyard Party") is the rung's own content, the same placement logic as `displayName`, and
neither the section `title` nor any bound content was ever going to reach that button. Both of
ch1's rungs render; if a chapter ever authors a programmatic-only rung, relaxing to bound-only
is the producer rule again. The content doc gains a naming pass before the sections are
authored - the names and labels are content, and the doc is their home.

Reference shapes follow 12.14.5, not 12.11's literal field spellings - the `scopeId`/`contentId`
spellings are the JSON's, and the doc gets reconciled on landing:

- **`scope`** (section required, module optional): a direct `ScopeDefinition` field. The JSON id
  resolves TREE-WIDE at import like `ResetScope` - the runtime reads it downward from a node it
  holds - and 12.12 owns the reach rule: the chapter itself or one of its descendants (12.11).
- **`content`** (module, optional): a direct `Definition` field, base-typed because modules bind
  different families (a producer for the Jam button, a currency for a header line). List modules
  (generators, upgrades, bars, events) bind nothing: their content is the evaluation scope's own
  declaration lists.
- **`prefabId`** stays a string: it names a widget through the `ModuleRegistry`, and the registry
  entry - not a content asset - is its authored home. "A new widget type is a prefab plus an
  entry" is the point of the indirection.

**Resolution order breaks the default's circularity** - the content id wants a scope to resolve
from, and 12.11's default scope comes from the content's home. The order: an authored module
scope wins, and the content resolves outward from it; with no authored scope, the content
resolves outward from the SECTION's scope, and its home then supplies the default (within the
chapter's subtree, else the chapter); a module with no content defaults to its section's scope.
Import NORMALIZES the result: every module asset carries a concrete direct scope reference, so
the runtime computes no defaults and a hand-authored module states its scope explicitly - the
default is a JSON authoring convenience, not a runtime rule.

One ch1 consequence: `the_release`'s section evaluates at ch1 (the `album` flag's home) but the
release rung is TIER1's, so that module authors `scope: tier1` explicitly - the rung button
presses its evaluation scope's own rung, exactly as `TryRung` reads it.

## ModuleRegistry

A ScriptableObject mapping `prefabId` to an Addressables `AssetReference` per widget prefab.
Hand-made like `GameConfig`, never imported, referenced by the screen host. An authored
`prefabId` the registry cannot answer throws at bind time in every build (requirement 7 - static
content cannot legitimately be unresolvable), and an editor test crosses every authored
`prefabId` in the composed content against the registry so the fault is caught at test time, not
first render.

The mapping alone is not a contract - the host must hand a prefab its session, scope, content,
and refresh wiring without switching on widget types. Every widget prefab's root carries a
`ModuleWidget`: the abstract MonoBehaviour base with `Bind(session, scope, content)`,
`Refresh()`, and `Interpolate(realDt)`. The host instantiates, binds, and refreshes through the
base and knows no concrete widget; the registry cross-check test also asserts each entry's
prefab root carries one.

## The screen host and refresh

One `UIRoot` MonoBehaviour, bound by `GameManager` after boot with the session and the registry -
an explicit `Bind(session, registry)` call, no singleton, no serialized session reference. It is
the ONE `Refreshed` subscriber, full stop: widgets subscribe to nothing, and on each event the
host evaluates visibility, then calls `Refresh` on every visible widget through the
`ModuleWidget` base - one subscriber, one dispatch order, and a widget instantiated mid-refresh
is refreshed by the same pass that created it.

**`Bind` renders once, unconditionally.** Boot's transactions run in `Awake` before any
subscriber exists, a fresh game runs no transaction at all, and a boot into `AwaitingIdleClaim`
refuses every tick without a refresh - so waiting for the first `Refreshed` leaves the select or
the dialog permanently blank. The same rule one level down: a widget instantiated during a
refresh cannot receive the event mid-dispatch, so the host pushes current state into every
widget it creates - instantiate, `Bind`, `Refresh`, in that order, at creation.

On each refresh, by phase:

- **`Live`**: render the foreground chapter's `sections` in authored order. Each section's
  `visibleWhen` evaluates in a `GameContext` at its evaluation scope's STATE node
  (`FindInSubtree` from the chapter, cached at chapter entry) - visible exactly while the
  condition holds, no latch (section 2). Each MODULE's `visibleWhen` then evaluates the same way
  at the module's own scope - the currency-line reveals stand on this - so a section shows
  exactly its visible modules. Instantiation is lazy: created the first time visible, toggled
  thereafter; the host refreshes every visible widget.
- **`AwaitingIdleClaim`**: the collect dialog over the chapter screen, rendering
  `session.CurrentOffer`.
- **`NoChapter`**: the chapter select (below); post-boot this exists only on a fresh game.

Nothing polls per frame (requirement 3): between refreshes only interpolation advances, on the
tick report's slopes.

**The cadence lives in the SESSION - the clock sample included**, because pacing and the
command surface cannot have two owners: widgets call `GameSession` directly, and a mutating
command must settle the pending window before it changes the state that window accrued against
(the flush, below). Splitting the roles - a driver-held baseline handing dt to a session-held
accumulator - reopens the same defect at the seam: a command's timestamp lands after the
driver's last sample, so flushing "through the command" stamps the pending window forward onto
time it did not cover, the next driver dt re-includes the pre-command gap against post-command
state, and the stamped windows overlap. So the session holds `lastSampleUtc` and computes
elapsed itself: the driver calls `session.Accumulate(nowUtc)` every `Update` and holds no clock
state at all. The sample advances UNCONDITIONALLY on every `Accumulate` and every command -
only PENDING is conditional - which is `TickBaseline`'s advance rule absorbed whole: the
baseline class retires, since keeping it would be a second copy of the same rule. The session
ticks ONCE with the WHOLE accumulation ending at the sample when pending crosses
`tickIntervalSeconds` (a `GameConfig` knob beside the idle thresholds, joining `Require`'s
fail-loud checks as finite and positive - zero restores the per-frame ticking the knob exists
to remove). Whole-accumulation ticking is what keeps the simulated windows contiguous and their
timestamps exact - a fixed-size tick against a drifting frame clock leaves unsimulated gaps
between windows and hands `TickSystem` boundary timestamps that never happened, and
`TickSystem` already segments internally, so a 5-second hitch is one correct call, no catch-up
loop.

**Every mutating command FLUSHES pending live time first**: inside the pipeline, before the
mutation, the command advances the sample to its own timestamp - the command IS a clock
sample - and ticks everything pending through it as a preceding tick transaction, then runs the
mutation against settled state. The flush window is the real pre-command window, correctly
stamped and contiguous with both neighbors, because one owner holds the clock. Without the
flush the pending window is simulated AFTER the mutation, and the segment snapshots read
post-mutation state for time that elapsed before it: a generator bought 0.9s into the window
earns for 0.9s it did not exist, an event entry's `RestartScope` lets pre-reset time produce
into the fresh run, and a chapter switch credits the outgoing chapter's accumulated foreground
time to the incoming subtree. Flush, never clear - `FireProducer` is a command, so clearing per
Jam tap would starve rate production during exactly the play the rates exist for.

Only LIVE time accumulates: `Accumulate` under any other phase clears pending, below the
threshold included - draining only at the crossing would let sub-interval dialog time ride
into the first live tick, the pooling regression the unconditional sample advance exists to
prevent, at smaller scale. A nonpositive elapsed also CLEARS pending - the sample follows a
rolled-back clock exactly as the baseline did, and pending dies with the discontinuity:
`Tick(dt, now)` promises `[now - dt, now]` is one contiguous clock interval, and a dt spanning
the rollback would compute absolute-stamped boundaries (buff expiries) against wall positions
that never held. Cross-TICK overlap after a rollback is a different thing - the next windows
land on rolled-back timestamps preceding everything already simulated, which today's per-frame
driver already produces and the session tolerates by design (monotonic stamps, relative event
timers); the clear costs at most one sub-interval of real production, the same order the
per-frame path discards around a rollback.

## The rung feedback contract

A plain static helper (`RungFeedback`), because the behavior is testable logic, not rendering:

- **Pressability** is `IsOffered` - the same object `TryRung` enforces.
- **Unmet legs**: the gate's TOP-LEVEL legs are the `All`'s list when the gate is an `All`, else
  the single condition (12.11). Each leg evaluates individually; unmet legs report their
  `uiText`.
- **Progress**: threshold kinds additionally expose current/target. A new virtual on `Condition`
  - `bool Progress(GameContext ctx, out BigNumber current, out BigNumber target)`, default false
  - overridden by `CurrencyAtLeast`, `EarnedTotalAtLeast`, `OwnedCountAtLeast`, and
  `BarsCompleted`, computing from the same fields `Evaluate` reads: one implementation, no drift.
  The capstone's `ch1_records`/30 readout IS this contract on its gate leg - no bespoke readout
  widget.
- **Payout preview**: "would bank: N" computes the rung's FIRST action through the same
  `Compute` the execution runs (12.5, section 5), and only when that action is an `AddCurrency`.
  First-only is what makes parity hold by construction: nothing has mutated when the first
  action evaluates, while even a second `AddCurrency` may read what the first deposited (a
  `LinearOnBalance` on the paid currency is authorable today). Any other opening kind previews
  nothing rather than a wrong number. The release (payout first) previews; the capstone
  (`ExecuteRung` first) shows none, which is what its authored module wants - the readout, no
  preview.

## Widgets (Chapter 1's set)

Thin MonoBehaviours under `UI/Widgets/`; whatever has behavior of its own (the feedback helper,
the preview, offer-line text assembly) lives in plain testable C#, and the prefab binding stays
untested by design, like the driver shell. The set the ch1 sections table needs:

- **`CurrencyHeaderUI`** - one line per bound currency: balance via `NumberFormatter`,
  interpolated between refreshes.
- **`JamButtonUI`** - `FireProducer` on the bound producer; yield preview from the same
  resolution `FireProducer` uses.
- **`GeneratorRowUI`** + list module - rows from the scope's `generators` gated by
  `availableWhen`; cost, owned count, `CanBuy` pressability, `TryBuy` on press.
- **`UpgradeRowUI`** + list module - same shape over `upgrades`, and the list additionally
  filters out purchased ones on the purchased fact (`IsUpgradePurchased`, the same fact
  `Purchasing.CanBuy` enforces): ch1's authored gates are progression conditions that never
  exclude their own purchase, so without the filter a bought row sits disabled forever.
- **`BarGroupUI`** - the group's bars, fill progress interpolated on the tick report's realized
  bar slopes, selection through `SetActiveBars`, the Rehearsal readout beside it.
- **`RungButtonUI`** - the release and the capstone: the rung's `label` as the button text,
  `RungFeedback` legs + progress + preview, `TryRung` on press.
- **`EventUI`** - the Garage Jam module: the offered event or events (question 3 decides the
  shape, since the authored gates alone do not pick one), start/dismiss through the event
  operations, the timer from the record's `remainingSeconds`, goal progress, reward-pending
  state.
- **`CollectScreenUI`** - the idle dialog: the offer's lines (currency, amount - all references,
  formatted), OK settles via `ClaimIdle`. "Double it" ARRIVES with step 10's AdManager - the
  button only requests the ad, and doubling-and-settling is the callback's transaction (12.9), so
  the dialog ships OK-only until then.
- **`ChapterSelectUI`** - the fresh-game select over root's children.

## Interpolation

Presentation only (12.11), and the slopes are the tick's REALIZED deltas, not gross rates. The
gross gathers lie exactly where display matters most: ch1's covers demand rehearsal at 2/s
against 0.5/s of production, and the pool-limited draw means the balance holds near zero while
the bar fills at 0.5/s - a `GetRate` slope would show the balance climbing and a
`ResolveDemand` slope would fill the bar 4x too fast, both snapping backward every refresh. And
a display-side projection of the pool draw would be a second implementation of
`ConsumeAndSettle`'s limiting math - the drift the one-implementation rule exists to prevent.

So the tick itself records what actually happened, AT the mutation sites and never as a balance
delta: rate deposits where `TickSystem` lands them, pool draws in `Draw`, progress in the fill -
per currency the net of deposits minus draws, per bar the progress added, over the tick's dt -
the transient tick report the session holds out. Site recording is what keeps one-shot
mutations out of the slope by construction: a bar completion's `onComplete` can author an
`AddCurrency`, and the transaction's closing sweep runs trigger payouts inside the same
boundary - measured as a state delta both would extrapolate as if they repeated every second.
A widget's slope is realized delta over dt; `game_speed` rides in for free because the realized
numbers already contain it. Between refreshes widgets advance displayed values by slope
times real elapsed and snap to truth on the next refresh. A NON-TICK transaction CLEARS the
report: a command can invalidate every measured slope - an event start lands the gear x0
handicap, a chapter switch changes whose subtree the slopes even describe - so widgets sit at
truth until the next tick measures the new state, one frozen interval at most. And extrapolation
never leaves the legal range: currency displays clamp at zero (a draining pool's negative slope
is honest motion, a negative balance is not) and bar displays clamp to [0, fillAmount]. No
gather ever runs per frame.

## Chapter select and the boot stopgap

Only a save with no recorded chapter - a fresh game - shows a select, and such a save owes no
idle to conflict with (12.9). So: `GameBoot.EntryChapter` keeps the recorded-id resolution and
loses the first-chapter fallback; boot with no record stays `NoChapter`, `UIRoot` renders the
roster (`Root.Children`, already sorted by composition), and the pick calls `SwitchChapter`.
`OnApplicationPause(false)` needs a guard for the same state - a player who backgrounds on the
select screen still has no recorded chapter to re-enter. The `GameBootTests` rows that encoded
the stopgap retire with it.

## JSON schema + importer

The `sections` block on the chapter block only - the key is real, so a root or tier authoring it
is an import ERROR naming the scope, the `rung`-on-root rule's shape, not an unknown key. DTOs
per the schema's conventions: `visibleWhen` a condition kind (required on sections - `Always` is
authorable and `garage_floor` authors it; optional on modules, absent means always), `scopeId`
and `contentId` strings resolved per the reference families above (the module default normalized
at import), `prefabId` a string kept as one, unknown keys abort. `displayName` joins every
definition block (required on the closed list, per 12.12 below), `title` every section block,
and `label` the rung block. Sections rebuild wholesale on re-import like
every nested authored object. `chapter-01.json` gains the seven sections of content doc section
12, authored after question 3's answers and the naming pass land in the content doc.

## Validation growth (12.12)

- A section or module scope reference outside the chapter-or-descendant set: error (12.11's load
  rule, on the composed tree so hand-authored assets answer too).
- A null section `visibleWhen`: error ("gates may not be null"); a null entry in `sections` or
  `modules`: `NullEntry`.
- Section and module `visibleWhen` conditions run `Condition.Validate` in a context at their
  resolved evaluation scope - the same site validation every other authored condition gets, so a
  hand-authored asset's unreachable reference or illegal operand is caught at boot, not only by
  the importer that happened not to write it.
- A module's bound content whose home is not on the module's evaluation chain: error - the
  runtime reads it outward from there, so unreachable content is a dark widget.
- A module scope reference left empty: error - import normalizes every module to a concrete
  scope, so an empty one is hand-authoring that skipped the field.
- An empty or non-grammar `prefabId`: error. Registry membership stays the editor test's job -
  the registry is settings, not content, and the pass's signature does not grow for it.
- An empty `displayName` on the closed list's families (currencies, generators, upgrades, bars,
  events, chapters) and an empty section `title`: error. One check on the composed tree serves
  both paths, since the importer's preflight runs the same pass; families off the list go
  unjudged.
- An empty `displayName` on content a module binds: error - the binding is the render site, so
  the requirement follows the binding, not the family. An empty `label` on any rung: error.

## Tests

- **Importer**: a sections fixture imports and wires the direct references, and the
  normalization covers all three default branches - a module bound to descendant content lands
  on the content's home, one bound to ROOT-OWNED content lands on the chapter (the Records
  header line's case), and a contentless module lands on its section's scope; aborts - unknown
  key in a section block, unresolved scope or content id, sections on a non-chapter scope, empty
  `prefabId`; re-import idempotence over the sections block.
- **Validator**: each new check firing and its legal twin (a tier-scoped section on the owning
  chapter passes; a sibling chapter's scope fails).
- **`Chapter1ContentTests`**: the seven sections spot-checked - count, order, gate kinds and
  numbers against the content doc table.
- **`RungFeedback`**: met gate answers pressable and no legs; unmet `All` lists exactly the unmet
  legs' uiText; `Progress` numbers for each threshold kind; preview equals what execution
  deposits when the first action is an `AddCurrency` (walkthrough-1's release numbers); a rung
  opening with any other kind previews nothing (the capstone's shape), and a second `AddCurrency`
  behind the first is not previewed.
- **`GameBoot`**: no recorded chapter boots to `NoChapter` (the stopgap rows retire); recorded
  path unchanged.
- **Session pacing**: a sub-interval `Accumulate` ticks nothing; crossing the interval ticks
  once with the WHOLE accumulation, so consecutive windows are contiguous; a non-Live
  `Accumulate` clears pending below the threshold (0.4s under the dialog, claim, 0.6s live - the
  tick carries 0.6, never 1.0); a nonpositive elapsed clears pending and stalls nothing (+0.8
  then a 5s rollback leaves 0, and later samples tick normally on the rolled-back clock). The
  flush: a buy 0.9s into the window settles 0.9s of pre-purchase production before the
  generator exists; a command whose timestamp lands AFTER the last `Accumulate` flushes through
  its own timestamp - pending plus the gap, and the next window starts where the flush ended;
  a switch flushes the outgoing chapter, so the incoming subtree inherits no pending time; a Jam
  tap flushes rather than clears, so tapping through a whole interval loses no rate production.
  The four `TickBaselineTests` retire into these rows with the class they tested.
- **`GameConfig.Require`**: a zero, negative, or non-finite `tickIntervalSeconds` throws,
  alongside the existing knob rows.
- **The tick report**: the pool-limited cover case - rehearsal produced at 0.5/s against a 2/s
  bar - reports a near-zero rehearsal net and a 0.5/s bar slope, the numbers the gross gathers
  get wrong; a plain production tick reports rate times dt; a non-tick transaction clears the
  report; a bar completion payout moves truth and leaves the report untouched (site recording,
  not a balance delta). The display clamps get their regressions: a depleting pool never
  extrapolates below zero, a completed bar never past `fillAmount`.
- **Registry cross-check** (editor): every authored `prefabId` in the composed content has a
  registry entry, and each entry's prefab root carries a `ModuleWidget`.
- **Screen host**: an EditMode test builds the section view over the imported content headlessly
  and asserts the fresh-state visible set (`garage_floor` alone) and the post-reveal set - the
  host's structure logic in plain C# so this needs no play mode. The regression: a module whose
  `visibleWhen` crosses into visibility during a transaction is created and receives its first
  refresh in that same refresh pass. Prefab binding itself stays untested by design.

## Not in step 9

`SetRoadieAllocation` and its UI, `AcknowledgeStory` and story beat cards, Encore and the buff
UI, AdManager and the Double It wiring, IAPManager and entitlement display - step 10. The song
operations' UI - chapter 6's. Fine-grained refresh - a mechanical optimization profiling has not
asked for (12.11).

## Docs on landing

The build-plan status line. 12.11's reference spellings reconciled to the object-field shapes
(the `scopeId`/`contentId` naming predates 12.14.5's direct-reference rule - a doc finding this
plan surfaces, landed only if John agrees). Content doc section 12 gains question 3's answers
and the doc gains the naming pass before the sections are authored. 12.13 already lists every
file this step creates.

## Landing order

Five changesets, each compiling and green on its own. A and B are headless and unblocked; C-E
wait on questions 1 and 2.

- **A. The definitions + schema + validation + ch1 sections**: `SectionDefinition` /
  `ModuleDefinition` on `ChapterDefinition`, `Definition.displayName`, the section `title` and
  the rung `label`, the DTOs, the resolution order with its import-time normalization, the 12.12
  checks, the
  sections block in `chapter-01.json` (after question 3 and the naming pass), importer +
  validator + `Chapter1ContentTests` growth.
- **B. The feedback contract**: `Condition.Progress` on the four threshold kinds, `RungFeedback`,
  the payout preview, their tests.
- **C. The host + first widgets**: `ModuleRegistry` + its asset, the `ModuleWidget` base,
  `UIRoot` bound from `GameManager` with the unconditional first render, the session's pacing
  (`Accumulate(nowUtc)` with the session-held sample, the command flush,
  `GameConfig.tickIntervalSeconds`) with the driver rewired onto it and `TickBaseline` retired,
  the canvas in the Boot scene, header/Jam/generator/upgrade widgets - the pre-reveal game
  playable.
- **D. The remaining modules**: bars, the two rung buttons over `RungFeedback`, the event module,
  the tick report and interpolation over it - the full ch1 loop playable.
- **E. The select and the dialog**: `ChapterSelectUI`, `CollectScreenUI`, the `EntryChapter`
  stopgap removal with its test rows, and the hand playthrough of all four walkthrough shapes.
