# Step 8 Plan - Chapter 1 JSON + importer + walkthrough tests

Design for build-plan step 8. Sections cited are `garage-band-idle-design.md`; content numbers are
`chapter-01-content.md`'s, which is their single home.

## What step 8 is

The game gets its real content and boots on it. Authoring becomes a JSON document materialized
into ScriptableObject assets by an editor importer (12.14.5), the root asset and the labeled
chapter roots become the Addressables content `ContentDatabase` composes into the runtime tree at
boot, and `GameManager` arrives as the
thin driver step 7 deferred: boot (load content, load save, build the session), the tick loop on
real time, the save call sites with the stamp-on-save line, and the auto-enter of the recorded
chapter. The proof is the four walkthroughs from the content doc running as tests against the
imported assets - the first time the suite exercises authored content instead of fixtures.

The old `chapter-01-garage.json` and `ChapterJsonImporter` (at `c446613^`) are PRE-RESTART
salvage: the JSON's schema (sections-first, string-id linking, label addressables, reward
definitions, fillMode pairs) describes the deleted architecture, and the content doc's section 14
records the deltas. What gets mined, not revived: the importer's mechanics - DTO parsing with
strict key handling, the condition pre-pass that aborts the import, stable asset paths keyed by
id, update-in-place re-import - and its lint vocabulary. The content itself is re-authored from
`chapter-01-content.md` against the current schema.

## Existing systems this uses unchanged

- **The definition families**: every authored shape has its class already - scopes, currencies,
  producers, generators, upgrades, bars/groups, modifiers (with `appliesWhen`, formulas,
  `permanentModifiers`), events, rungs, triggers, conditions, actions, payout formulas. The
  importer maps JSON onto them; no family gains a member for step 8.
- **`ContentValidator`**: the real gate on imported content. Import lints only catch what JSON can
  get wrong before assets exist; the loaded tree answers to 12.12 exactly as fixtures do.
- **`GameSession` / `TickSystem`**: complete from step 7; `GameManager` only calls them.
  `SaveSystem` takes ONE reshape: `TryDeserialize`, `LoadFromDisk`, and the backup-loadability
  check behind `WriteAtomic` build state from the composed PAIR, not the bare root - built from
  the root alone, the empty-children contract would read every saved chapter as removed content
  and drop it while the load reports success, and backup rotation would judge loadability against
  a chapterless tree.

## The documents

Two JSON documents, one schema:

- **`root.json`** - the game-wide declarations Chapter 1 happens to consume first (content doc
  section 2): root currencies (`records`, `roadies`), the three permanent modifiers with their
  formula effects, the idle base (`idle_base`, wildcard rate x0.5, `appliesWhen: IdleAccumulation`),
  root flags (`ch1_complete`, the story latches).
- **`chapter-01.json`** - the ch1 subtree: the chapter, tier1, and every declaration the content
  doc files on them (sections 3-10).

Two documents because "re-import overwrites" (12.14.5) is a per-document contract: the JSON is the
source of truth for what IT authored, and chapter 2's arrival must never re-author root. There is
NO cross-document write: the root document owns the `RootDefinition` asset, whose serialized child
list stays EMPTY - the chapter document set is the roster - and a chapter document imports its own
subtree and labels its chapter root `chapter`, which is its whole roster act. Startup composes
the pair - the root asset plus the labeled chapter set - into the tree (12.14.5), so adding or
removing a chapter touches
exactly one document and its label, and no asset ever stores a copy of the roster to go stale.

Both live at `Assets/Content/` (the importer is an editor tool reading project files; `Docs/`
keeps the human-facing content doc and the pre-restart archive).

## The JSON schema

The schema is the definition classes, one block per declaration list, spelled the way the content
doc already spells them:

- **Scopes** nest as authored: the chapter block carries its own declarations plus a `children`
  array of tier blocks, tiers recur. Every scope block's lists mirror `ScopeDefinition`'s
  (`currencies`, `flags`, `producers`, `generators`, `upgrades`, `barGroups`, `modifiers`,
  `permanentModifiers`, `triggers`, and on interiors `rung` and `events`).
- **Polymorphic kinds** are discriminated by a `type` field naming the class (`CurrencyAtLeast`,
  `All`, `SetFlag`, `AddModifier`, `RootCurveFormula`, `LinearOnBalance`, ...). A type with no
  class behind it is an import ERROR that aborts the import (12.14.5) - the old importer's
  condition rule, kept for every family: a kind that silently became null would change meaning per
  site (a null gate is closed, a null bar gate is open), so nothing is written.
- **References are ids in the document, resolved at import per reference FAMILY, the mirror of
  how the runtime reads each.** An authored reference is an object field on the asset (12.14.5).
  Ordinary definitions (currencies, producers, generators, upgrades, modifiers, bars, groups,
  events) resolve by walking the authored tree OUTWARD from the scope the reference sits on -
  through the document's own chain, then the union's CANDIDATE root chain: preflight resolution
  never touches a persisted asset (all-transient, below), and only the write pass wires persisted
  objects - so import-time reach
  equals runtime reach and sibling scopes reusing an id can never cross-wire. SCOPE references
  (`ResetScope`, `RestartScope`, `ExecuteRung`, the event-record conditions, `AddModifier`'s
  grant target) resolve TREE-WIDE, because scope ids are tree-wide unique and the runtime reads
  them downward (`FindInSubtree`) - the capstone's `ExecuteRung(tier1)` points at a child, and an
  outward-only rule would reject the authored content; whether the named scope is legal from that
  site is each class's own 12.12 reach check, not the resolver's. An unresolved id aborts.
  `permanentModifiers` entries resolve as ordinary definitions - usage, not declaration.
- **Strings stay strings** only where the runtime says so: flag ids, tags, stat names, and the
  `Effect` selectors `target` and `currencyId` - id-or-tag MATCH selectors the gather evaluates
  (12.2), never resolved references, even when a value happens to name an id.
- **Numbers**: currency-valued fields parse into `BigNumber` (thresholds, amounts, costs, fill
  rates - the authored-fields rule); `Effect.multiplier`, growth, counts, and `Pow`'s power stay
  double/int.
- **Unknown keys abort** - Newtonsoft `MissingMemberHandling.Error` over strict DTOs gives the old
  importer's hand-rolled guard (`amount` where a condition wants `value`, and every misspelling)
  for free, and an explicit JSON `null` behaves as absent (`NullValueHandling.Ignore`), the single
  source of "absent" semantics.

Deliberately NOT in the schema yet: the UI `sections` block (content doc section 12) waits for
step 9's `SectionDefinition`/`ModuleDefinition`, and story TEXT waits for step 10's beat cards -
only the story flags import now. The schema grows a block when its class family exists, the same
incremental contract as validation.

## The importer

`ChapterJsonImporter` under `Assets/Scripts/Editor/`, rebuilt on the old one's mechanics:

- **Stable paths, update-in-place.** Each definition materializes at a deterministic path,
  SCOPE-QUALIFIED because only scope ids are tree-wide unique (12.3):
  `Assets/ScriptableObjects/<document>/<scope path>/<family>/<id>.asset`
  (`.../ch01/tier1/Generators/practice_amp.asset`). Definition ids are chain-unique, so sibling
  tiers may both author a `cash` and their assets must not collide. Re-import loads the
  existing asset and overwrites its fields rather than recreating it, so GUIDs survive and every
  direct reference (a chapter's own subtree wiring, the Addressables entries, anything hand-wired
  in the editor)
  stays intact. Nested authored objects (conditions, actions, effects, entries, formulas, rungs)
  are serialized data inside the owning asset and rebuild wholesale each import - nothing
  references them from outside.
- **Preflight, then write - and the preflight is always the full UNION.** Every import command
  parses, lints, and resolves EVERY document into transient in-memory instances, composes them
  through the same seam boot uses, and runs `ContentValidator` on that one assembly. All-transient
  is load-bearing: mixing a transient document with persisted neighbors puts two generations of
  one declaration in one tree - the persisted chapter referencing the persisted `records`, the
  candidate root declaring its candidate - and identity-based ownership rightly refuses to call
  those the same asset. The deeper faults (reach,
  reset order, stranded value) only exist on the assembled graph, and finding one after the
  writes would recreate exactly the half-poisoned state preflight exists to prevent. Every
  command then WRITES the union it validated - a selected-document write would land a future root
  beside a past chapter (rename `records` to `albums` across both documents, write only root, and
  the persisted chapter still references the old asset: post-write validation fails after the
  mutation). One contract: preflight the union, write the union, never half, since nothing writes
  before the union validates. Only a clean-of-errors
  assembly earns the persistent writes - a preflight failure leaves yesterday's assets untouched
  and the process failed (entry points below), never a silent no-op; a post-write writer fault
  necessarily cannot preserve state, and its job is only failing the process. The transient union
  is native Unity objects: every instance is destroyed with `DestroyImmediate` in a `finally`,
  success and abort alike, or repeated imports and the importer tests accumulate them until a
  domain reload. Then two passes
  over each written document:
  materialize every asset, then wire every list and reference. The
  duplicate-id refusal is per CHAIN, exactly the uniqueness the runtime owns (12.12) - sibling
  scopes reusing an id is legal authoring, not a collision.
- **Import lints** (abort, fix, re-import - never skip-and-continue for anything load-bearing):
  unknown type, unknown key, a duplicate id on one chain, unresolved reference, an id outside the
  `[a-z0-9_]+` grammar (empty included - ids become path segments, and the grammar is what keeps
  separators, `..`, reserved names, and case-games out of the filesystem), a
  `produces` entry naming no currency, a generator cost block without currency or with
  nonpositive baseCost or growth, an upgrade without a cost currency or with a negative amount -
  the currency is ALWAYS required (`Purchasing` dereferences it and the validator wants it
  on-chain) and only the amount may be zero: `cut_demo` authors `{cash, 0}`. Preflight also
  resolves every target path, verifies each lands inside its managed directory, and refuses
  CASE-INSENSITIVE collisions across the union - `cash` and `Cash` on two chains are legal
  identity but one file on Windows. Everything
  deeper (reach, placement, stranded value, wildcard viability) is 12.12's job on the result.
- **After the writes, validate what boot will load**: the importer reloads the persisted root and
  labeled chapter assets, composes them through the same seam, and runs `ContentValidator` on
  that assembly - validating the bare root asset would inspect nothing, since its serialized
  child list is empty by contract. The preflight
  already gated the writes, so this pass disagreeing with it means the writer itself is broken,
  which is exactly worth failing on.
- **Entry points**: one Import Content menu item and the static
  `ChapterJsonImporter.ImportAll()` for `-executeMethod`, every entry running the same union
  import - the batchmode verify loop's import step
  comes back (the memory note has waited on this since the restart). `ImportAll` THROWS on any
  preflight failure (parse, lint, resolution, the transient-graph validation) and on a post-write
  report with ERRORS - `report.HasErrors`, never warnings - so the batchmode process exits
  nonzero: an import that aborted by logging would leave yesterday's assets testing green, the
  exact false green the loop exists to prevent.
- **Orphans are de-labeled and reported, never deleted**: an asset in the managed folders that
  the documents no longer author gets a warning naming it, and an orphaned CHAPTER root loses its
  `chapter` label - off the runtime roster, still on disk. Deleting content is a human's call.

## chapter-01.json + root.json

Authored from the content doc, block by block: sections 2-3 (scopes, currencies, flags), 4
(producers), 5 (generators), 6 (upgrades), 7 (bars), 8 (modifiers), 9 (rungs - the release and
capstone, with the `EventRewardPending` guards and the `RootCurveFormula` payout), 10 (the three
Garage Jam events with handicaps, `RestartScope` entry, reward swaps), 11 (zero triggers; the
story flags at root). The content doc stays the human home of the numbers; the JSON is their
machine form, and a delta between them is a bug in whichever edited last.

`GameConfig` is NOT content: the real asset (`Assets/ScriptableObjects/GameConfig.asset`,
`maxGameSpeed` 4, `minimumAwaySeconds` 180, `idleCapSeconds` 14400) is created by hand via its
menu entry, referenced by `GameManager`, never imported - settings, not authoring.

## Addressables + startup composition

Two kinds of entries (12.14.5): the `RootDefinition` asset at a fixed address (`"root"`), and
every chapter root under the single `chapter` label - assigned by the importer as a chapter
document's roster act. Nothing else: each loaded chapter's direct references pull its subtree as
dependencies, and the label is consumed exactly once, at the load boundary, like the save's scope
names - no runtime read ever consults it, so requirement 8 stands untouched.

Packing is an enforced contract, not an accident: root-owned assets (`records`, the root
modifiers) are implicit dependencies of BOTH the root entry and every chapter entry, and
Addressables duplicates an implicit dependency into every bundle that references it - two
`records` instances at runtime would break the asset identity the composition leans on. The
importer therefore places the root entry and every chapter entry in one `PackTogether` group, and
an editor test asserts the placement and the group's bundle mode.

`ContentDatabase` splits assembly from loading:

- **The composition is a PAIR, never a clone**: `ComposedContent {root, chapters}`, the chapters
  sorted by id (until ordering becomes an explicit authored fact). Scope operations resolve by
  asset identity, so the tree's root definition must BE the loaded asset - a clone would strand
  every authored reference to root, and a root-granted modifier is legal authoring - and nothing
  is mutated, so nothing writes through to the editor asset. `ScopeState.Build` and
  `ContentValidator.Validate` take the pair and answer "children of root" from the set, every
  deeper child from the serialized lists. A NONEMPTY serialized root child list is refused
  outright at composition - an accidentally wired child would be a second, unvalidated roster
  only the label path cannot see. Boot, the importer's preflight, and the editor tests
  all assemble through this one seam, so what the walkthroughs exercise is what ships.
- **`LoadRoot`** becomes load-and-compose: the root address plus the label load, every handle
  retained, `Root` exposing the composed pair, `Release()` dropping root and chapter handles
  together - there is no clone to destroy. Zero labeled chapters is a boot failure - a game with
  no chapters is broken content -
  and a duplicate chapter id surfaces in the composed tree's validation, where tree-wide scope-id
  uniqueness already lives. The 12.12 pass runs on the composed pair, on the load path, as today;
  a boot smoke test exercises the whole load in the editor.

The pre-restart residue goes first, and it reaches past `Assets/ScriptableObjects/`: the 41 dead
assets there (broken script references and deleted families - Rewards, Sections, StoryBeats,
CurrencyGroups), the 10 prefabs under `Assets/Prefabs/`, each carrying a dead MonoBehaviour
reference (the three row scripts and the six module scripts), `SampleScene.unity` with its
`EditorBuildSettings` entry, the 48 stale labeled entries in the default Addressables group, AND
the 13 legacy label definitions in the settings' label table are deleted outright in slice A,
before the importer writes anything - the 13 legacy labels are replaced by the single `chapter`
label. A directory that existed to hold deleted residue goes with it, `.meta` included: the 12
flat family folders, `Prefabs/` with its `Modules/`, and `Scenes/`. `ScriptableObjects/` itself
stays - it is the importer's managed root, and its flat children are exactly what the
scope-qualified paths replace. Update-in-place cannot load an asset bound to a deleted type, and
the single root entry replaces the label scheme those entries served. Nothing dangles: the row
prefabs were reachable only from their module prefabs, those only from the `module/*` addresses and
the `Sections` assets, and no script or test names any of it. Deleting the scene also retires the
stale `GameManager` component it carried, which slice D would otherwise author a second time.

## GameManager

The thin driver (12.13: bootstrap, save/load, chapter switching), a MonoBehaviour in a `Boot`
scene, holding the `GameConfig` reference and nothing the session already owns:

- **Boot**: `ContentDatabase.LoadRoot` - the root address plus the `chapter` label, composed per
  12.14.5, validation on the load path in dev builds -
  `SaveSystem.LoadFromDisk` on `persistentDataPath/save.json`, a FILE (handing it the directory
  would read every fresh install as `Failed`) - `LoadedPrimary`/`LoadedBackup`
  use the loaded tree, `NoSave` builds fresh, `Failed` is a hard stop with a visible error (never
  a silent new game, per step 2's rule) - then `new GameSession(root, config)`. The manager
  RETAINS the returned `ContentDatabase` and calls `Release()` in `OnDestroy` - the Addressables
  handle needs an owner, and nothing else can release it.
- **Enter**: the recorded `currentChapterId` resolves over root's direct children - the
  load-boundary name resolution, one scan of `root.Children` - and `SwitchChapter` enters it,
  re-offering any unpaid window as the idle dialog phase (nothing renders it until step 9; the
  session state is simply correct). A fresh game has no recorded chapter and step 9 owns the
  chapter select, so until then boot auto-enters the sole authored chapter - an explicit stopgap,
  removed when the select exists.
- **Tick**: `Update` diffs `DateTime.UtcNow` against the `TickBaseline` - a small plain class the
  manager owns, because this is the one behavior-bearing piece of the driver - and calls
  `session.Tick(dt, now)` - real elapsed time, never frame time (requirement 2). The baseline
  RESETS at boot and on both pause transitions, and ADVANCES every `Update` whether or not the
  session accepted the tick: without the resets a resume below the idle minimum replays the whole
  paused interval as live production, and without the unconditional advance the frames refused
  during `AwaitingIdleClaim` pool up and dump into the first live tick through the same door. A
  backwards diff passes through as the nonpositive no-op the session already handles.
- **Save call sites**: `OnApplicationPause(true)` and `OnApplicationQuit` run
  `SwitchChapter(null)` (the backgrounding rule: stamps a live chapter, preserves an unpaid
  window) then `WriteAtomic`; `OnApplicationPause(false)` re-enters the recorded chapter, which is
  where the away window recomputes. The stamp-on-save line lands here for saves taken WITHOUT
  backgrounding: before any other `WriteAtomic`, `if (Phase == Live) ForegroundChapter.StampActive(now)`
  - foreground only and only while Live (12.9); step 8 ships pause/quit saves only, and a periodic
  autosave is one call at this same site whenever it is wanted.

`GameManager` keeps only glue. The two pieces with behavior of their own live beneath it in
testable plain C#: the `TickBaseline` (the walkthroughs cannot see it - they hand `dt` to the
session directly) and a headless boot helper mapping load outcomes to tree-plus-session. The
remaining MonoBehaviour shell - lifecycle forwarding, `Release()` in `OnDestroy` - stays untested
by design.

## Tests

`ChapterImporterTests` (EditMode, editor assembly): a minimal fixture JSON imports clean; unknown
type aborts; unknown key aborts; a duplicate id on one chain aborts; unresolved reference aborts;
re-import preserves the asset GUID and the chapter's label; a chapter document removed from the
set de-labels its root on the next `ImportAll`; a failing document in a multi-document `ImportAll`
leaves every document's assets unwritten (the union preflight); a coordinated cross-document
rename - a root-owned currency and the chapter reference to it - lands atomically in one run; the
root and chapter entries share one `PackTogether` group (the duplication guard); a
second import of identical JSON
changes nothing (idempotence).

`Chapter1ContentTests`: the imported root validates with zero ERRORS and exactly two expected
warnings - the `FlagNoSetter` pair on the story latches, whose setter (`AcknowledgeStory`) is step
10's, so the whitelist retires with it; spot checks against the
content doc - the scope shape (root/ch1/tier1), declaration counts per family, the capstone gate
number, generator costs and gates, the event chain's `availableWhen` laddering, the release
formula's shape. Enough to catch a mis-authored block, not a second copy of the doc.

`Chapter1WalkthroughTests`: the four walkthroughs of content doc section 13, driven through
`GameSession` over a fresh `ScopeState.Build` of the COMPOSED root - the imported assets loaded
by path and assembled through the same `Compose` seam boot uses - computed amounts asserted
through the tolerance helper:

1. **Normal release** (13.1): scripted taps (`FireProducer`) and ticks walk the trace - the reveal
   thresholds arm in order, the buys land, cover_1 fills from banked Rehearsal, and the release at
   50 fans pays exactly `floor((50/5)^0.5)` = 3 records and 3 ch1_records, resetting tier1.
2. **Event entry** (13.2): mid-run `TryStartEvent(garage_jam_1)` banks the gate-met run through the
   release's own rung, the fresh record carries the gear x0 handicap (tap pays, generators do
   not), the goal latches at 150 cash and survives spending below it, and dismissal pays gj_tap_1,
   sets gj1_done, and resets tier1. The expired-timer variant dismisses with no reward.
3. **Replay clear** (13.3): the capstone banks the live run, pays the roadie, sets `ch1_complete`,
   and resets ch1 wholesale; with `roadieAllocation[ch1] = 1` written at root (the command is step
   10's), the income multiplier comes back ~1.87 by AssertClose.
4. **4-hour idle claim** (13.4): the doc's exact state, away 14400s - the offer's three lines match
   the authored arithmetic (cash 84/s x 7200 = 604,800; fans 0.648025/s x 7200 = 4,665.78 - the
   doc's 4,666 is display rounding; rehearsal 3,600), doubled settlement
   pays x2, and the timed-gig variant offers nothing (`BlocksIdle`).

`CompositionTests` + `TickBaselineTests` + boot branching: composing a fixture pair whose chapter
carries an action targeting ROOT resolves it against the built tree - the asset-identity rule the
pair exists to preserve; a nonempty serialized root child list is refused; the baseline resets at boot and on both pause transitions, advances
every update whether or not the session accepted the tick (the pause-replay and dialog-pooling
regressions), and passes a backwards diff through as nonpositive dt; the headless boot helper
maps `LoadedPrimary`/`LoadedBackup`/`NoSave` to loaded tree, loaded backup, and fresh build, and
`Failed` to the hard stop.

`SaveSystemTests` gains the composed round-trip: empty serialized root children, the chapter
supplied through the pair, nondefault chapter facts surviving `TryDeserialize` and `LoadFromDisk`,
and backup loadability judged through the same composed content - the existing save tests build
their trees through serialized children, so none of them proves the reshape.

The walkthroughs load the root and chapter assets by path (`AssetDatabase`); the verify loop
re-imports before
running tests, which is what keeps stale assets from passing as green (the batchmode import step).

## Not in step 8

The UI sections block and its definitions, the chapter select, the idle dialog rendering, refresh
subscribers - step 9. `SetRoadieAllocation`, `AcknowledgeStory`, story beat cards, Encore and the
`timedBuffs` gather row, the ad/store callbacks (including the atomic double-and-settle) - step
10. Chapter JSON beyond Chapter 1 - with their chapters.

## Docs on landing

The build-plan status line, step 3's stale one-root-load description revised to the composition
contract, and 12.13's `ScriptableObjects/` folder sketch gains the per-scope
nesting the importer actually writes. 12.13 already lists `GameManager.cs`; the content doc needs
no edit -
it is the source the JSON is authored FROM, and any delta found while authoring is a finding to
bring back, not a silent doc fix.

## Landing order

Four changesets, each compiling and green on its own.

- **A. The importer + root.json**: the pre-restart purge first (the 41 dead assets, the 10 prefabs,
  `SampleScene` with its build-settings entry, the emptied directories, the 48 stale
  Addressables entries, the 13 label definitions), then `ContentDatabase`'s composition pair (with
  `CompositionTests`, the root-targeting identity row included), DTOs
  and type mapping, the lints, the preflight-then-write
  scope-qualified import, `root.json` authored, the root asset + its fixed-address entry,
  `ChapterImporterTests`.
- **B. chapter-01.json**: the chapter schema blocks, the document authored from the content doc,
  the `chapter` label as its roster act,
  import green with zero validator ERRORS plus the two whitelisted `FlagNoSetter` warnings,
  `Chapter1ContentTests`.
- **C. The walkthroughs**: `Chapter1WalkthroughTests`, all four.
- **D. GameManager**: the Boot scene, boot/enter/tick/save wiring with the stamp-on-save line, the
  `TickBaseline` and headless boot helper with their tests, the
  hand-made `GameConfig` asset, the Addressables boot smoke test, the build-plan status line.
