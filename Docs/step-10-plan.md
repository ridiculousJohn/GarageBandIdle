# Step 10 Plan - Meta and monetization

Design for build-plan step 10. Sections cited are `garage-band-idle-design.md`; Chapter 1's
authored numbers and text are `chapter-01-content.md`, which stays their single home.

## What step 10 is

The game gets its meta layer: everything section 8, 9, and 10 describe that step 9 explicitly
left out. Encore becomes a working buff, the idle claim learns game speed, the Backstage Pass and
the other store products exist behind stubbed store and ad seams, story beats render, Roadies can
be stationed, and the chapter screen gains the chrome Ctrl C puts above every chapter - a top bar
with the chapter selector, the story log, and settings, the last hosting the allocation screen.

The proof is the four walkthrough shapes played by hand again, plus the new ones: an Encore
extended by a stubbed ad and watched to expiry across a backgrounding; a 4-hour claim doubled by
the stubbed ad, and another doubled by buying the Pass from the dialog; the opening card on a
fresh install and the capstone beat after the first clear, both rewatchable from the log; a
replay with one Roadie stationed on ch1 through the allocation screen, ~1.87x as walkthrough 13.3
computes. Both walkthrough tests that today poke facts directly - 13.3's allocation map write and
13.4's `doubled = true` - convert to the commands that now own those writes.

The command surface grows, for the first time since step 7, and only by root-owned commands and
authenticated callbacks (12.11's list): `SetRoadieAllocation`, `AcknowledgeStory`, and the callback
operations - extend Encore, double and settle the offer, write an entitlement, grant Roadies. No
chapter-local mechanic changes. The condition family grows one kind (a live buff record), the tick
one housekeeping prune, the claim one factor; the gather grows nothing. Nothing here invents a vocabulary: every
new number is an ordinary root modifier and every new fact already has a field in `RootFacts` or
`ScopeFacts`.

## Decisions (2026-09-02)

Settled in conversation and landed in the design doc the same day (commit `bb1f630`).

1. **Encore is an ordinary root permanent modifier whose `appliesWhen` reads a timed record.**
   `encore` is `{stat: game_speed, x2}` in root.json's `permanentModifiers` with
   `appliesWhen: Any[FlagSet(backstage_pass), BuffActive(encore)]` - the `idle_base` shape, a
   membership that counts only while a fact holds. The `TimedBuff` record is that fact, resolved
   outward from the acting scope and read by a condition exactly as a flag is; it is NOT a source of effects, and no gather row exists for
   it. A rewarded ad adds four hours to the record, repeatable, to a `GameConfig` cap. John, on
   the gather-row shape this plan first proposed: "can't it be 'Owns the pass' OR 'timer > 0'?"
   Ctrl C's Overclock is the reference: "Boost for 4 hours" (ad) and "Boost forever" (the Pass).
   Ctrl C words its buff as a generator YIELD multiplier; ours is speed, which also reaches bar
   fills. John confirmed the choice is one authored field either way - `stat: rate`, `target:
   income, stat: rate`, and `stat: game_speed` are all shapes root.json already authors - so
   nothing in this step depends on it.
2. **`game_speed` has two consumers, the two places real time becomes production.** The tick,
   as before, and the idle claim: the paid window is the first `min(elapsed, cap)` REAL seconds
   after the stamp, segmented at the root buff expiries inside it, each segment paying its length
   at the rate and speed live in it. The cap bounds real seconds; speed multiplies what they pay.
   One hour of Encore left and four hours away pays one hour at 2x and three at 1x. The
   observation that settled it: for idle, 2x rate and 2x speed are the same number, since only
   rate accrues while away.
3. **The Backstage Pass is every reward the two ads give, without the ads, plus a raised cap**:
   permanent Encore, the claim always doubled, the higher idle cap. The doubling is the offer's
   existing `doubled` flag set from the entitlement at computation - no second idle modifier,
   which is what made the earlier design read as one lever pulled twice. A free player watching
   both ads reaches the same 4x on an offer; the Pass makes it automatic (Ctrl C's Pro Unlock:
   "Double All", the idle window showing the doubled amount with "Great!").
4. **The idle dialog has three actions**: Double It (requests the ad), Backstage Pass (requests the
   purchase), OK (claims). Both request buttons only request; the payout is the callback's own
   transaction. Bought FROM the dialog, the store callback is the ad's callback plus the
   entitlement write, one transaction ending `Live`, so the dialog closes with the phase. An
   entitlement written by any other path repaints only the button set: shown and paid never
   differ, because the dialog renders the stored lines the claim pays.
5. **Overdrive is deferred.** If a sustained ad streak ever earns a higher speed, it is the ONE
   Encore buff reporting 2x or 4x from its own remaining time through a formula-shaped effect -
   never two buffs relying on the tick's clamp. Nothing in this step builds toward it, and
   nothing forecloses it.
6. **Store and ad back ends are stubs with real asynchrony.** The API shape is what a store SDK
   presents - request now, result later - and the fake completes with success by default, with
   scripted paths for a failed purchase and an aborted ad so the tests and the editor can force
   each outcome.
7. **Story beats are buttons, in the event-row shape; popping is opt-in per beat.** Ctrl C: a
   beat's button appears the moment its module is visible, is greyed with a goal readout while
   its gate is unmet, goes live when the gate holds, and stays live after watching for a rewatch.
   So a beat is declared on the chapter, a module binds it as an ordinary row with a `visibleWhen`,
   its `availableWhen` renders as legs through `GateFeedback`, and the button opens the card as
   often as the player likes. A card never opens by itself unless the beat is MARKED to
   (`opensWhenAvailable`), and then the rule is state, never a transition: the card shows while
   the beat is available and its seen flag is unset, once, because closing it sets the flag -
   section 10's own rule, crash-safe by construction. The seen-latch flag stays at root, so the
   capstone reset cannot replay an opener. No action opens a card: a `GameAction` writes state,
   and the moment a beat should light is its `availableWhen`, which reads every fact a trigger
   could observe.
8. **Roadie allocation lives under settings.** Ctrl C's top bar: a chapter selector, a
   "conversation" button listing the story beats seen so far, and settings, where "Completion
   tokens" opens the allocation menu.

## Questions before the slices

Each is a design fact only John can settle. None blocks slice A.

- **The entitlement's spelling as a fact.** `RootFacts.entitlements` (a store-written set) exists
  with no reader. A permanent modifier gated on it needs a condition kind over the set
  (`HasEntitlement`, one new kind). The alternative is the entitlement as a ROOT FLAG the store
  callback sets - `FlagSet` already exists, the modifier needs nothing new, and the set field goes
  unused. Recommended: the flag, since the store's own ids are strings root.json can declare and
  the only reads are boolean; the set is deleted from the schema with it, or kept for receipts if
  John wants one. Either way the session's two code reads (the cap raise, the doubled flag) name
  the Pass id in one constant.
- **"Unlocked chapters only"** (12.11's `SetRoadieAllocation` rule). No chapter unlock condition
  exists: the select renders root's whole roster, and section 2 says a cleared chapter stays
  available. Until a chapter authors an unlock gate, the constraint is "a chapter on root's
  roster", which is also the save filter's rule for the map's keys.
- **Where the Encore window opens from.** Ctrl C's Overclock window shows "While active, yields of
  all generators is 2.0x. Time remaining HH:MM:SS" with the two boost buttons. Whether Ctrl C
  reaches it from the top bar, settings, or a chapter module is unknown - ask before slice D.
- **Settings' contents.** Assumed: the Roadies entry alone this step.
- **The story log's timing.** The "conversation" screen is small once beats are definitions (a
  downward walk over root's roster reading root flags). In step 10 or later.
- **The numbers**, all `GameConfig` placeholders until tuning cares: the Encore ad duration
  (14400 s), the Encore cap, the Pass's idle cap, the Roadie bundle sizes.

## Existing systems this builds on

- **`ScopeFacts.timedBuffs`** (`{buffId, expiresAtUtc}`) on every payload; the doc places Encore's
  at root. **`TickSystem.Boundaries`** already admits every buff expiry in the swept set as a
  segment edge and says so: "nothing reads or removes one until the timedBuffs gather row lands" -
  that comment is corrected with this step, since no gather row lands.
- **`ScopeState.MultiplierFor`**: the permanent-membership loop already applies a modifier only
  while `Applies(modifier, origin)` holds - `idle_base` counts only under the idle context by
  exactly this. Encore adds nothing to the gather: its `appliesWhen` is a compound over two
  facts, and `Any` already exists.
- **`Condition`**: the family a new kind joins (`FlagSet` is the model - a fact keyed by id,
  resolved outward from the acting scope, with `Validate` and `Progress`).
- **`GameSession.EnterChapter`**: computes the offer once over `[stamp, nowUtc]` at current state
  under the idle context; `SettleOffer` pays the stored lines, x2 when `doubled`. `RunCommand` is
  the pipeline every command runs: guards, flush, mutation, conditional sweep, one refresh.
  Root-owned commands take "the exception path 12.9 names and arrive with their step" - this one.
- **`RootFacts`**: `roadieAllocation` (read by `RoadieTotalBoost` / `RoadieActiveBoost`, both
  built), `entitlements`, `currentChapterId`. The save filter already drops allocation keys that
  are not root children and nonpositive counts.
- **`GameContext.NowUtc`**: every gather carries its moment, so a segment context at segment start
  is what the claim's segmentation hands the gather - the tick's own shape.
- **The UI layer**: `ScreenHost` as the one `Refreshed` subscriber with by-phase dispatch;
  `ModuleWidget` / `ModuleWidgetFactory` / `ModuleRegistry` for authored modules; the app-owned
  overlays (`ChapterSelectUI`, `CollectScreenUI`) in `Screen.uxml`; `GateFeedback` for legs;
  `Definition.displayName` on the closed list.
- **`ContentValidator`**: `FlagNoSetter` warns for a declared flag with no content setter - today
  exactly the two story latches (`Chapter1ContentTests` asserts the pair; `ch1_complete` has the
  capstone's `SetFlag`), which code will set.

## Encore

**The modifier.** root.json declares `encore` in `modifiers` - `{stat: game_speed, multiplier: 2}`,
`stacking: Replace`, `appliesWhen: Any[FlagSet(backstage_pass), BuffActive(encore)]` - and lists
it in `permanentModifiers`. The two `game_speed` shape warnings (12.12) already cover it: wildcard,
root-declared. The Pass's permanence and the ad's four hours are the two legs of one condition on
one modifier: a Pass owner applies it through the first leg, a free player with a live record
through the second, and either way the membership is applied once, so no case exists in which the
two could stack - the merge rule, the clamp, and a refusal are all beside the point.

**The record.** One `TimedBuff` per buff id on the scope that holds it, `expiresAtUtc` absolute.
Encore's lives at root because the ad callback writes it there; a chapter-scoped buff is a record
on the chapter, which dies with the chapter's reset - lifetime is placement, as for every fact.
The record is a FACT, like a flag: it is a source of nothing, and only a condition reads it. Its id is the id the
condition names; by convention it is the modifier's, and the save filter drops a record whose id
no `BuffActive` in the composed content names, with a warning, the same rule as an unknown flag.

**`BuffActive(buffId)`**, the one new condition kind: walks OUTWARD from the acting scope, like
`FlagSet`, and is true at the first scope holding a record with that id (12.14 requirement 8 - no
scope is named, root least of all) whose `expiresAtUtc` is later than `ctx.NowUtc` - the one
comparison. Every condition evaluates against a context stamped at the moment being judged (12.4:
every read runs at the clock's time), the tick's segment context is already stamped at the
segment's start, and the claim's segments are stamped the same way, so the 12:00 segment sees a
13:00 record as live, the 13:00 segment sees it as dead, the closing sweep at 16:00 sees it as
dead, and a trigger or a module gate reading `BuffActive` sees the truth at the clock's time. Truth
is the timestamp, never the record's presence, so nothing depends on when a prune ran: a record
past the cap, one expiring exactly at a tick's end, or one expiring under the dialog is dead the
moment it is dead. (The first draft read presence and pruned at segment starts, which left each of
those three cases answering true through a closing sweep - a review caught it.) `Validate`: the id must be named by some `BuffActive`...
which is itself, so the check is the placement one every kind gets; the declaration home for a buff
id is a question the orphan sweep (step 11) answers, since nothing declares one today. `Progress`:
none - a timer is not a threshold the player approaches. Refused inside a currency's `activeWhen`
for the same reason `IdleAccumulation` is: it would gate a currency's existence on a fact the
claim's own pruning moves.

**The prune is housekeeping.** Since `Boundaries` already cuts a segment at each expiry inside the
tick and `BuffActive` judges by the segment's time, a buff live at segment start governs the whole
segment and dies at its edge - 12.9's sentence - with no removal involved. Expired records are
removed only to keep the list short: at the tick's end, from the swept set, every record with
`expiresAtUtc <= tickEndUtc`. A missed prune costs nothing, since no read trusts presence. The claim
removes nothing during its walk (a record that expired inside the window must survive to cut its
boundary and to be judged dead in the segments after it) and lets the next tick's end collect it.
Expiry is tick-owned time: no command touches a record's timestamp except `ExtendBuff`, which only
ever moves one later.

**What the claim reads is present state.** A dormant chapter's claim reads the records standing at
switch-in and judges them segment by segment across its window; it does not reconstruct what
happened to Encore while another chapter was live. A buff that began after the stamp reads as live
over the whole window (generous by the gap); one that expired under another chapter's tick was
pruned there and reads as absent (short by the overlap). Both are bounded by the cap, both need a
second authored chapter and a present player, and the only complete fix is a ledger of activation
intervals - a second time model. The rule Records already follow (earned elsewhere while away,
they boost the whole window) and 8.2's open retroactivity question are the same stance, so the
claim takes it for Encore too. John, 2026-09-02: "do we even care?" - no. The case that occurs -
one chapter, app closed, the buff expiring inside the window - is exact.

**The extension**, `ExtendBuff(scope, buffId, seconds, nowUtc)`: a session command taking the
record's home scope, as `AddModifier` takes its target - root for Encore, so the ad callback's call
is root-owned. Finds the record by id on that scope; absent, creates it at `nowUtc + seconds`;
present, sets
`max(expiresAtUtc, nowUtc) + seconds`; then clamps remaining time to the config cap. **Legal in
every phase**: it is an authenticated callback, and 12.9 already says those are always
phase-eligible - the dialog's refusal of ordinary commands exists so a sweep cannot reset away an
unpaid window, and a root record write under the dialog sweeps nothing, since the sweep is
conditional on the resulting phase. A refusal here would discard a watched ad whenever the app
resumed into the dialog before the callback landed. Under the Pass the window shows no ad button
(Ctrl C: "Time remaining" reads infinity), so the command is never asked; if it were, the write is
harmless, since the membership applies once either way. Runs the ordinary pipeline through a
root-context variant of `RunCommand` that skips the foreground test and the phase test (root-owned
commands are 12.9's exception) but keeps the flush - the memory rule: a command owns its mutation
and the flush, and nothing the tick owns; under the dialog the flush is a no-op, since the session
banks time only while Live. It is the ad callback's whole Encore job; the fake ad completes into it.

**The window** (UI, slice D): "Time remaining HH:MM:SS" computed from the record against the
clock per frame (display, not truth), "Boost for 4 hours" requesting the ad, "Boost forever"
requesting the Pass; a Pass owner sees the remaining time as infinity and no ad button. Where it
opens from is the open question above.

**Config**: `encoreAdSeconds` (14400), `encoreCapSeconds`; `Require` rejects zero, negative, or
non-finite, and a cap below the ad duration. The other knobs this step adds get their rows in the
same place with their slices: `backstagePassIdleCapSeconds` finite and at least `idleCapSeconds`
(a malformed Pass cap must fail at boot, never shrink a paid claim), and every Roadie bundle count
positive (a store that has already reported success must never meet a grant that throws).

## The claim learns game speed

`EnterChapter` today: `Producer.GetRate(idleCtx, currency) * seconds` per currency. It becomes a
loop over the paid window's segments, the tick's own shape:

- The paid window is `[stamp, stamp + min(elapsed, cap)]` - accrual runs for the first cap seconds
  after the stamp and stops. The offer's `windowEndUtc` stays `nowUtc`: the stamp advances to the
  window actually paid, and time past the cap is a lost window either way (12.9's settled-window
  rule), so nothing about settlement changes.
- The edges first: every buff expiry strictly inside the paid window, over root and the chapter's
  subtree, sorted - the same `Boundaries` logic the tick runs (a dormant chapter's subtree
  contributes no event timers, since a blocking record skips the claim entirely, but its own buff
  records, if any, cut edges like root's).
- Per segment, a `GameContext(chapter, segmentStartUtc, idleAccumulation: true)`: `game_speed`
  gathered once, clamped to `[1, maxGameSpeed]` like the tick, and each currency's rate times
  speed times the segment's real length accumulates into its line. `BuffActive` judges each
  segment by its own start, so the record that expires at 13:00 is live in the 12:00 segment, cuts
  the 13:00 edge, and is dead in the 13:00 segment - with the record untouched throughout. Nothing
  is removed during the walk; removing before it would delete the boundary and pay the whole
  window at 1x.

The `idle_base` x0.5 rides the rate gather as before; a live-only speed buff excuses itself by
`appliesWhen: Not(IdleAccumulation)` with the vocabulary that exists. The Pass's cap raise is the
one entitlement read in the window computation: `idleCapSeconds` becomes the Pass's larger knob
when the entitlement holds. The Pass's doubling is the other: `offer.doubled = true` at
computation when the entitlement holds, so `SettleOffer` changes nothing and the dialog shows what
OK pays.

`TickSystem.Boundaries` and the segment walk get factored so the claim calls the same code the tick
calls, rather than a second implementation of "segments between expiries" - the tests-exercise-
runtime-code rule.

## Entitlements and the Pass

The three benefits, each on an existing mechanism once the spelling question is answered:

- **Permanent Encore**: the first leg of `encore`'s own `appliesWhen` (`FlagSet(backstage_pass)`
  under the recommended spelling). No second modifier, no refusal.
- **The claim always doubled**: `EnterChapter` sets `doubled` from the entitlement.
- **The raised cap**: the window computation reads `backstagePassIdleCapSeconds` instead of
  `idleCapSeconds` while it holds.

**The write**, `GrantEntitlement(id, nowUtc)`: a root-owned session command the store callback
completes into - sets the fact and runs the pipeline. From the idle dialog it is
`PurchasePassFromDialog(nowUtc)`: the entitlement write, `doubled = true`, and the claim in one
transaction ending `Live` (decision 4). A kill between the store's confirmation and the claim
leaves the stamp unmoved and the entitlement to be restored from the store. Restoration is the
store seam's `RestoreEntitlements` completing into the same write for each id, asynchronously, on
a frame AFTER boot: the entry switch in `GameManager.Awake` is synchronous and computes the first
offer before any restore can land, so that offer is computed undoubled at the base cap, and the
restored entitlement applies from the next offer computed after it. No boot gate: an entitlement
arriving behind the scenes is the case John does not care about, and a launch gated on a network
restore is a worse product than one undoubled offer. The entitlement is a saved root fact, so this
window exists only on a new device or after the kill above; every ordinary launch has it from the
save.

The 12.10 filter: an entitlement id no content declares (a store product dropped from root.json) is
dropped with a warning, like any unknown flag.

## Store and ad seams

Two interfaces in `Monetization/`, each with a fake the shipping scenes reference until a real SDK
arrives. The seams carry the protocol a real store needs; what they do NOT carry is stated below,
so the plug-in claim is honest rather than absolute:

- `IAdService.ShowRewarded(placement)` returning a completion (a `Task<AdResult>`: Rewarded,
  Aborted, Failed). Placements are a code enum - a closed set - `EncoreExtension` and `IdleDouble`.
- `IStoreService.Purchase(productId)` returning `PurchaseResult` (Succeeded, Failed, Cancelled,
  plus a `transactionId` on success), `Acknowledge(transactionId)`, and `RestoreEntitlements()`
  returning the ids owned. Product ids are a code enum too: `BackstagePass`, the Roadie bundles,
  the Tip Jar tiers.

**Consumable delivery order**: grant, save, acknowledge. A real store re-delivers any transaction
the app never acknowledged, so `IAPManager` runs the grant command, calls the driver's one save
site (`GameManager.Save` - "a periodic autosave is one call here whenever it is wanted"), and only
then acknowledges. A kill before the save leaves the transaction unacknowledged and the store
re-delivers it; a kill after the save and before the acknowledge re-delivers a transaction already
granted. Two pieces of that replay are deferred to the real SDK's arrival, together, and the plan
records the gap rather than claiming the seam complete: the ROUTE - a `Task` from `Purchase`
serves only the process that asked, and a store's launch-time redelivery of an unacknowledged
consumable arrives as a pending-purchase callback at initialization, an operation the seam does
not carry because the fake would never fire it - and the IDEMPOTENCY, a durable record of
processed transaction ids, a new root fact only a real SDK can exercise. Until both land, a kill
between a real store's confirmation and the save would lose the grant; with the fake nothing is
paid, so nothing is lost. The Pass is an entitlement, not a
consumable: restoration is idempotent by construction (the flag is set or it is not), so it needs
no ledger.

`AdManager` and `IAPManager` are plain C# over those seams, owned by `GameManager`, and are the only
callers of the callback commands: `ExtendBuff` and `DoubleAndClaimIdle(nowUtc)` from the ads;
`GrantEntitlement` / `PurchasePassFromDialog`, `GrantRoadies(count, nowUtc)` (a root-context
deposit into `roadies`, the currency's `activeWhen` honored), and a no-op transaction for a Tip Jar
purchase from the store. **The drop rule is `IdleDouble`'s alone.** An `IdleDouble` request
records the chapter it was made for; its result is dropped only when the foreground chapter is no
longer that one (the player switched under the dialog, which settled that offer undoubled on the
way out) - for the same chapter, a live offer is doubled and settled, and with none live the
callback recomputes from the stamp first, 12.9's mid-ad kill rule. An `EncoreExtension` result has
no offer to lose and its command is legal in every phase, so a Rewarded result ALWAYS reaches
`ExtendBuff` and the save, after a backgrounding or a chapter change alike; a watched ad is never
discarded. **A Rewarded result
saves after its grant**, through the same one save site the store path uses: the save runs only on
pause and quit today, an ad network never replays a watched ad, and a crash after `ExtendBuff` or
`DoubleAndClaimIdle` would otherwise take the reward off disk. Aborted and Failed save nothing.

`DoubleAndClaimIdle` is `ClaimIdle` with `doubled = true` set first, one transaction. When no offer
is live (the mid-ad kill), it recomputes from the stamp first - 12.9's sentence, now code.

The fakes complete on the main thread on the next frame, through the driver's `Update`, so the
asynchrony is real enough to catch a callback landing under a changed phase but never interleaves
with a running command - which is why no callback queue is built: 12.9's "queued behind
`commandInProgress`" is satisfied by Unity's main thread until a real SDK proves otherwise. Each
fake has a scripted next outcome so a test or the editor forces failure or abort.

## Story beats

**The definition.** `StoryBeatDefinition : Definition` - `displayName` (the row's label, the
card's title; it joins the closed list since a widget renders it), `text`, `availableWhen`
(a `Condition`, required, `Always` for the opener), `seenFlag` (a flag id resolved outward from
the beat's home, exactly as `SetFlag` resolves), and `opensWhenAvailable` (bool, default false -
the pop mark, decision 7). Declared on `ChapterDefinition.storyBeats` - beats are chapter-boundary
content and nothing outside the chapter references one - with the flag declared at ROOT in
root.json, as the two ch1 story latches already are. The beat is content on the chapter and the
module is a REFERENCE to it, never the other way round: Ctrl C's story log lists every revealed
beat across chapters in one dialog, and that list is a downward walk over root's roster reading
each chapter's `storyBeats` and the root flags. A beat defined inline in a module would put the
log's source inside the UI layout, a find-all-X search through sections and modules - the one
walk this project forbids. Imported like every family; `chapter-01.json` gains a `storyBeats`
block with ch1's two beats and content doc section 11's text.

**Validation** (12.12): `seenFlag` must resolve on the beat's chain (a home outside it is the
error the setter rule already names, since the write could never reach it); the beat counts as
that flag's setter, which retires the two `FlagNoSetter` warnings the content test asserts today;
`availableWhen` gets the kind placement checks every gate gets. The Pass flag, under the recommended spelling, needs the same
allowance and needs it first (slice B): a code-set flag declared at root is legitimate, and the
validator needs to know which.
The smallest spelling is a `setBy: code` marker on the flag declaration... which is a new field for
a two-flag problem. Alternative: `FlagNoSetter` stays a warning and the two are accepted as known
warnings - but the validator's report is printed at boot, so a known warning is noise John reads
every launch. Recommended: the marker, one optional field on the flag entry, refused on any flag
content also sets.

**`AcknowledgeStory(ctx, beat)`**: a root-owned session command on the root-command pipeline -
reentrancy guard, `Live` required (a card opens only over a live chapter), the flush, refused when
the beat's `availableWhen` does not hold at that context (a card cannot be acknowledged before it
could be shown), else the flag written at its home through the same outward walk `SetFlag` uses,
then `CloseTransaction`: the sweep, and `Refreshed`.

**The module**, `story_row`: a `ModuleDefinition` binding one beat as `content`, the event-row
shape - a registry entry, a UXML with a button and a legs container, `StoryRowUI` in the factory
switch. `OnBound` sets the button text from `displayName`, builds one label per top-level leg of
`availableWhen` through `GateFeedback.Legs` exactly as `EventUI` does, and wires the click.
`Refresh` reads two facts in the module's context, `availableWhen.Evaluate` and the seen flag
(read outward as the `FlagSet` condition reads it), and renders one of three: unseen and
unavailable - button disabled, the unmet legs' text (the goal readout); available - button enabled,
legs hidden; seen - button enabled with a `seen` class, whatever the gate says now. **Enabled is
seen OR available**: the tier reset clears purchased upgrades, so a gate like
`UpgradePurchased(last_buff)` goes false again on a replay while the root latch stays set, and a
watched beat stays rewatchable (Ctrl C). No widget decision anywhere.

**The click.** The row cannot see the host today, so `ModuleWidgetFactory.Create` gains a
parameter - the host behind a one-method interface, open a story for a beat and a scope - and only
`StoryRowUI`'s constructor takes it. The base `Bind` stays four parameters; no other widget changes.

**The card.** One app-owned overlay in `Screen.uxml` (`story`), the host owning it as it owns the
select and the collect dialog: title, text, one button, driven by a `StoryBeatUI` the host
constructs over it. The host's open method stores the requested beat and scope and calls `Render`,
which shows the overlay while a request is held and the phase is `Live`. The sections stay UP
beneath it: the chapter is live and ticking, so the interpolation reason that keeps them down under
the idle dialog does not apply. The card's button: the host clears the held request first; then, if
the beat's flag is unset, `AcknowledgeStory` with a context at the beat's scope; if set, nothing
else, and `Render` hides the overlay. `Refreshed` from the command calls `Render` - the request is
already cleared, so the overlay hides, and the row's `Refresh` in the same pass reads the flag as
set. One transaction, one redraw, the order every command follows.

**Auto-open, for marked beats only.** On each `Render` while `Live` with no request held, the host
walks the foreground chapter's `storyBeats` - the declaration list of a scope it already holds -
and opens the card for the first beat that is marked `opensWhenAvailable`, available at the
chapter's context, and unseen. State, not a transition (section 10): the pair "available and
unseen" holds from the transaction that made it available until the close sets the flag, so a
crash with the card up shows it again next launch, and a beat whose gate is `UpgradePurchased(x)`
pops once after the purchase and never again, though the gate stays true forever. Unmarked beats
never pop; their button is the only way in. The walk is over the chapter's list, not its sections,
so a marked beat pops whether or not any row for it is on screen at that moment.

**One ch1 authoring consequence, settled: both rows live in `garage_floor`.** The capstone
transaction sets `ch1_complete` and resets the chapter in one go, so at the refresh that follows,
`album` is cleared and `backyard_party` is hidden. A story row authored there would vanish at the
moment its beat became available. `garage_floor` is always visible and already exists, so no
`story` section is authored; content doc section 12 gains the two rows there, the capstone row's
`visibleWhen` being `FlagSet(ch1_complete)` so a fresh chapter shows only the opener's button.
Neither ch1 beat is marked to pop; the content doc says so.

**The log** (if in step 10): a `StoryLogUI` overlay opened from the top bar, listing every beat
whose flag is set, across root's roster - a downward walk from root through each chapter's
`storyBeats`, reading root flags: the legitimate walk (12.14.8). A tap is a second caller of the
host's open method, handing it the beat and its chapter's state node. The log reads no section,
which is what the declaration-on-the-chapter rule buys.

## Roadie allocation

**`SetRoadieAllocation(map, nowUtc)`**: a root-owned session command over the whole map, replace
semantics. Refused when any count is negative, any key is not a root child's id, or the sum
exceeds the `roadies` balance (a `BigNumber` compare; the pool is a currency). Writes
`Root.roadieAllocation` wholesale, dropping zero entries so the map holds only stationed chapters
(the save filter's rule, applied at the write). Runs the root pipeline: flush, write, sweep,
refresh - so the next tick resolves the new boosts and the header re-renders. Legal in `Live` only
this step, since the chrome that reaches it is chapter chrome; a dormant chapter's next claim is
computed at current state and so sees the new allocation, which answers 8.2's "deliberately
open" retroactivity question by construction - recorded on landing.

**`RoadieAllocationUI`**: an app-owned overlay - one row per root child (`displayName`, stationed
count, minus and plus), a line for the unallocated remainder, Done. Plus and minus edit a local
copy; Done calls the command once. The local copy is the only UI-held state in the app and dies
with the overlay.

## The chrome

A top bar above the sections in `Screen.uxml`, visible while `Live`: the chapter selector (opens
the existing select as an overlay - `SwitchChapter` from a live chapter is already legal and
settles the outgoing offer), the story log (or a placeholder until the log lands), and settings.
`SettingsUI` is an overlay with one row this step, Roadies, opening the allocation screen. The
Encore window hangs wherever the open question lands.

The host's `Render` grows from three phases to a small overlay stack: the phase screens as today,
plus at most one requested overlay (settings, allocation, the story card, the Encore window) on
top while `Live`. Overlays are host-owned like the select and the dialog; none is a module.

## Validation additions

The 12.12 pass grows: story beats (flag reach, setter accounting, gate kind placement), the
code-set flag marker, `BuffActive`'s placement (refused in `activeWhen`), and the entitlement
flag's declaration. The save filter grows: a buff record no `BuffActive` names; unknown
entitlement flags (if a flag, the existing flag drop covers it).

## Tests

- **The condition and the prune**: a root record of `encore` with a future expiry makes
  `BuffActive` true at the clock's time and false at a context stamped past its expiry; a record
  past the cap, one expiring exactly at a tick's end, and one expiring under the dialog all read
  false in the closing sweep and refresh, prune or no prune; the tick's end removes expired
  records and a missed prune changes no answer; a live record makes the
  membership doubles the tick's effective dt; the Pass flag alone does the same; both together are
  still 2x (one membership, one application); the record contributes nothing to yields or to a
  bar's fill rate directly (only through dt); a tick crossing the expiry pays the pre-expiry
  segment at 2x and the post-expiry at 1x (the boundary already exists - the test asserts the prune
  makes it matter); an expired record is gone after the tick; a record no condition names is
  dropped on load with a warning; `BuffActive` inside an `activeWhen` is a validation error; a
  record written on a chapter is found from its tier's context, not from a sibling chapter's, and
  is gone after the chapter's reset.
- **`ExtendBuff`**: absent creates at now plus duration; present extends from the later of expiry
  and now; the cap clamps; legal in `AwaitingIdleClaim` and `NoChapter`, and under the dialog the
  transaction sweeps nothing and leaves the offer standing; the flush precedes the write (the
  same-frame regression row from step 9, for a root command).
- **The claim's walk**: a record expiring inside the window cuts its boundary and reads dead in
  the segments after it while staying in the list - the 12:00 / 13:00 / 16:00 case pays one hour
  at 2x and three at 1x, which removing the record before the walk would pay entirely at 1x; a
  record that began after the stamp reads as live over the whole window (present state, asserted
  so the rule is recorded).
- **Store delivery order**: the fake's `Acknowledge` is called only after the save site ran; a fake
  scripted to kill between grant and acknowledge leaves the transaction unacknowledged.
- **The claim**: 13.4's numbers unchanged with no buff; with one hour of Encore left and four away,
  cash = 84 x (3600 x 2 + 10800 x 1) x 0.5; a buff expired before the stamp changes nothing; away
  time past the cap pays the cap; a Pass owner's offer is `doubled` and computed over the larger
  cap; the segment code is the tick's (a shared method, asserted by the call, not by a copy).
- **Callbacks**: an `EncoreExtension` Rewarded result reaches `ExtendBuff` after a backgrounding
  and after a chapter change, and only an `IdleDouble` result for a chapter no longer in the
  foreground is dropped; `DoubleAndClaimIdle` pays x2 and advances the stamp in one transaction, and
  recomputes from the stamp when no offer is live; `PurchasePassFromDialog` writes the entitlement,
  pays x2, ends `Live`; a fake ad scripted to abort leaves the offer undoubled and the dialog up; a
  fake purchase scripted to fail writes nothing; `GrantRoadies` deposits into root's `roadies`. 13.4's `doubled = true` converts to the
  fake ad's callback.
- **Story**: the opener is available on a fresh chapter and unseen; `AcknowledgeStory` sets the
  root flag and the row reads seen; the capstone beat is unavailable before `ch1_complete` and its
  legs name the gate; acknowledging an unavailable beat is refused; the reset leaves both flags
  set and a seen row stays enabled with its gate false again; a marked beat opens the card by
  itself at the transaction that makes it available and not again after the close, an unmarked one
  never; a fresh chapter renders the opener's row alone; validation flags a `seenFlag` with no home
  on the chain, and the content test's two `FlagNoSetter` rows go to zero.
- **Allocation**: 13.3 through `SetRoadieAllocation({ch1: 1})` - the ~1.87x multiplier as
  authored; refused on a negative count, a non-chapter key, and a sum past the balance; zero
  entries are dropped; the allocation affects the next tick's rate (the flush row again).
- **The host**: the overlay stack shows at most one overlay; a story row's click opens the card
  over the still-rendered sections and the card's close hides it in the command's own refresh; the
  dialog renders three buttons for a free player and OK alone for a Pass owner; the registry
  cross-check gains `story_row`; `Require` rejects a Pass cap below the base cap and a nonpositive
  bundle count.
- **Content**: `Chapter1ContentTests` gains the two beats, their flags, and the rows' sections;
  `root.json`'s `encore` with its two-leg `appliesWhen`.

## Not in step 10

A real ad or store SDK - the seams and fakes are the deliverable. Overdrive (decision 5). The
`bought <= earned` cap held in reserve. The late-game Cash to Roadie sink. The Ch. 6 song
operations. The story log, if John places it later. Chapter unlock gating - no chapter authors one.

## Docs on landing

The build-plan status line. 12.13's file list (`Meta/RoadieAllocation.cs`, `Monetization/*`, the
story family files, `StoryBeatUI` / `StoryLogUI` / `RoadieAllocationUI` / `SettingsUI` / the top
bar, the Encore window). The design doc: `TickSystem`'s "gather row" comment retired; 8.2's
retroactivity question closed; 12.11's entry-point list gaining
the callback operations by name; 12.3's `entitlements` line following the spelling decision; 12.9
naming the shared segment walk. The content doc: section 11's beats as authored definitions,
section 12's story rows, walkthrough 13.4's Encore variant. `root.json` and `chapter-01.json`
carry the content.

## Landing order

Five changesets, each compiling and green on its own. A is unblocked; B waits on the entitlement
spelling; D on the Encore window's placement.

- **A. Encore in the runtime**: root.json's `encore` with the TIMED leg alone
  (`appliesWhen: BuffActive(encore)` - the Pass leg joins in B, so A imports, validates, and stays
  green without the spelling decision), `BuffActive`, the prune, `ExtendBuff` with the
  root-command pipeline variant and the config knobs, the claim's segmented window over the shared
  boundary code, the save filter's buff row, the tests.
- **B. Entitlements, the Pass, and the seams**: the entitlement spelling, its validation, and the
  code-set flag marker (needed here first, for `backstage_pass`; the story flags reuse it in C),
  `encore`'s `appliesWhen` becoming the two-leg `Any`, the two interfaces and their fakes,
  `AdManager` / `IAPManager` with the grant-save-acknowledge order, the callback commands, the
  Pass's three benefits, the dialog's Double It and Pass buttons, 13.4 converted.
- **C. Story beats**: the family with the pop mark, the importer and validator rows,
  `AcknowledgeStory`, `story_row` with the factory's opener parameter, the card and the marked-beat
  walk in the host, ch1's two beats and their `garage_floor` rows (placement settled above, so C
  waits on nothing), the content doc.
- **D. Allocation and the chrome**: `SetRoadieAllocation`, the top bar, settings, the allocation
  screen, the Encore window, 13.3 converted.
- **E. The story log**, if in step, and the hand playthrough of every shape above.
