---
name: design-review-revisions
description: "2026-08-17/18: the rewritten design doc was revised through an accepted external review — all nine findings resolved in-doc; regression guards and deliberately deferred decisions"
metadata:
  node_type: memory
  type: project
  originSessionId: 7f293076-5bbd-4dc3-bc37-166bf599a1eb
  modified: 2026-08-18T03:45:00.000Z
---

Right after the 2026-08-17 rewrite ([[project-layout-and-workflow]]), `Docs/garage-band-idle-design.md` was revised through an external design review John accepted. All nine findings were resolved by discussion and written into the doc the same day. **The doc is the sole authority — read it before advising; do not trust any summary of it that predates these revisions, including build prompts or old chat context.**

**Why:** Several of the corrections deleted mechanisms that looked settled. A fresh session that recalls the pre-review shape will reintroduce exploits and rejected designs.

**How to apply — guards against regressing the review corrections:**
- **No gate bypass exists.** Every press invocation — `TryPress` from UI or `ExecuteRung` from another press's action list — is fail-closed against the target press's OWN gate; unmet gate no-ops; unfinished runs discard, never bank. (The earlier `ExecuteRung`-bypasses-gates design enabled a Records-farming exploit via event entry and was deleted.)
- **Producers are named definitions, never one-per-currency.** `ProducerDefinition {id, tags, produces: [{currencyId, stat, value, condition?}]}`; stats are named strings (`rate`, `yield`, extensible by adding a consumer); generators share the entry shape scaled by ownedCount; Jam is `tap_producer` data, not UI logic. John rejected one-producer-per-currency twice — do not reintroduce it.
- **Capstone gate is per-chapter and flat**: `records_this_chapter ≥ N` — a chapter-declared counter zeroed by the capstone's own reset; the album payout feeds root records + the counter from ONE formula evaluation. The rising replay goal (`base×H^k`) was deleted: replays get faster (banked power), never harder; wall-clock time is the farm throttle. Rewards/goals are `PayoutFormula`s over stored facts.
- **Events are three self-guarding operations** — `StartEvent`/`CompleteEvent`/`AbortEvent`, Action kinds invocable from anywhere, never tied to UI. Behavior is authored `onEntry`/`onComplete` lists on `EventDefinition`; completion is player-CLAIMED (a met goal arms it, fires nothing); the ActiveEvent record dies with any reset reaching its host; expired records are inert by derivation and swept on scope activation; the `resetOnEntry` field was deleted.
- **Encore is a game-speed multiplier, not income**: `{target: game_speed, ×2}`, 4× Overdrive, Pass = permanent Overdrive. `game_speed` is a reserved effect target consumed ONLY by the tick (scales production dt; wall-clock decrements — event timers, buff expiries — never scale; yields never scale).
- **Idle pays via a pending-claim dialog, never silent deposit**: switch-in computes the payout into `pendingClaim` (chapter state), the dialog offers double-via-ad on THAT claim, deposit on dismissal.
- **`ScopeState` is the complete schema and the save IS the tree** — earnedTotals, timedBuffs, songs (tier = run Catalog, root = Discography), roadieAllocation, entitlements, pendingClaim are all fields; there are no side-channel "root facts".

**Deliberately deferred (do not treat as gaps to fill unprompted):**
- Roadie boost double-count: shelved WORKING ASSUMPTION = local × total (spreading still favored by concavity). Not yet in the doc.
- Catalog→Discography selection rule: mechanism recorded in doc §7 (promotion action before the reset); the rule itself is a Ch. 6 authoring decision, candidates parenthesized there.
- Chosen-difficulty replay knob: fits existing machinery but needs a record-the-choice action; deferred until a chapter wants it.

**Next steps as of 2026-08-18:** dead files still on disk pending John's call (build prompts, pre-restart code, chapter-01-garage.json); everything uncommitted pending his review; then Chapter 1 authored data + numeric walkthroughs (release, event entry, replay clear, 4-h idle claim) as the last doc artifact before building the §12.13 spine.
