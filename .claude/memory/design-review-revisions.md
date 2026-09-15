---
name: design-review-revisions
description: "Designs the twelve review passes REJECTED that a fresh session would reinvent from genre instinct, plus the deliberately deferred questions - the doc cannot show an absence"
metadata:
  node_type: memory
  type: project
  originSessionId: 7f293076-5bbd-4dc3-bc37-166bf599a1eb
  modified: 2026-09-15T00:49:51.415Z
---

`Docs/garage-band-idle-design.md` went through twelve external design review passes (2026-08-17/18),
each accepted by John, every finding resolved IN-DOC; pass 12 found zero architectural defects and
declared the architecture implementation-ready. John has directed that further reviews stick to
actual defects, not ordering nits.

**The doc says what the architecture IS - read it, do not recall it.** This file keeps only what the
doc cannot: designs that were rejected AND that instinct would rebuild anyway. Anything the doc
prohibits in its own words, or that its schemas make unwritable, is deliberately not repeated here.

**Rejected - and each is what a session would otherwise invent:**
- **A persisted pending claim, or a silent idle deposit.** Idle pays through an offer dialog, and THE
  STAMP IS THE PENDING CLAIM (respelled 2026-08-26, John's call): switch-in computes a TRANSIENT
  session-held offer over [lastActiveUtc, B], nothing about an offer is ever saved, the dialog offers
  double-via-ad on THAT offer, and settlement deposits the lines and advances the stamp to B in one
  transaction. A kill with the dialog up recomputes from the unmoved stamp. The persisted
  `pendingClaim`/`ClaimEntry` shape was my invention inside an approved doc, never John's decision.
- **A rising replay goal (`base * H^k`).** Replays get faster from banked power, never harder;
  wall-clock time is the farm throttle. The capstone gate is flat and per-chapter
  (`records_this_chapter >= N`), a chapter-declared counter zeroed by the capstone's own reset.
- **An automatic expiry sweep, and `resetOnEntry`.** Expiry ends nothing: an expired, goal-unreached
  record persists inert until the player dismisses it (`AbortEvent`) or a reset reaches its host, and
  it occupies that host, so `StartEvent` fails until dismissed. Completion is player-CLAIMED - a met
  goal arms it and fires nothing.
- **Event level/tier machinery.** A harder rerun is just another EventDefinition whose `availableWhen`
  gates on the prior level's completion flag. Level rewards stack as authored increments or swap via
  `RemoveModifier` + `AddModifier`.
- **Encore as income.** It is a game-speed multiplier: `{target: game_speed, x2}`; the Pass makes
  THAT 2x permanent. CORRECTED 2026-09-14 (John): there is no designed 4x tier. "Overdrive" in the
  design doc and the build plan's after-the-plan list is a conflation I wrote: the 4x idea is Cells to
  Singularity's (2x per ad to a 24h bank, the excess 4x to ~36h), mentioned once; Ctrl C's Overclock
  is a flat ad-extended 2x made permanent by its IAP and has no 4x; we discussed how a 4x would work
  mechanically and decided NOTHING about implementing it, since it cuts against the Backstage Pass.
  DECIDED later 2026-09-14 (John): the Encore tier - 2x to a 24h threshold, 4x beyond it to a 36h
  cap (config placeholders), the Pass = permanent 4x so the two never compete. TWO buffs, not one
  formula: John's argument, which I first weighed wrong - the segment walk cuts only at record
  expiries, so a second record written by the same grant at Encore's expiry minus the threshold
  makes the tier edge free, while a tiered formula would teach the walk to derive an edge from
  content. Design section 9 holds the write-up; not yet built.
  `game_speed` is consumed ONLY by the tick and scales production dt;
  wall-clock decrements (event timers, buff expiries) never scale, and yields never scale.
- **Banking or auto-claiming a reward on reset (2026-08-18).** Resets only clear. Refuse the
  destroyer, never rescue the value - mirroring the idle-claim guard. A rung whose reset closure
  contains an event host without an `EventRewardPending` / `EventRecordExists` guard warns at load.
- **Roadie venue assets and stationing caps.** Deleted 2026-08-20 - see [[roadies-and-typed-payloads]].
  Price and wall-clock are the throttle; allocation concavity does not cap the total. The fan rate
  must NEVER carry a roadie-targeted tag, because the wall-clock farm throttle stands on it.
- **One producer per currency.** John rejected it twice; producers are named definitions with a
  `produces` entry list.

**Decoder:** any "press" in older memory or in doc history means today's Rung - renamed during step 3
(2026-08-18, John's call: it collided with UI button vocabulary in a codebase where the UI owns
nothing), giving class `Rung`, field `InteriorDefinition.rung`, and UI entry point `TryRung`.

**Deliberately deferred - do not treat as gaps to fill unprompted:**
- Roadie reallocation retroactivity on idle claims. Shelved by John repeatedly and explicitly: "it's
  a targeted fix later."
- Roadie boost double-count: working assumption is local x total (spreading still favored by
  concavity). Not in the doc.
- Catalog to Discography selection rule: the mechanism is in doc section 7 (a promotion action before
  the reset); the rule itself is a Chapter 6 authoring decision.
- Chosen-difficulty replay knob: fits existing machinery but needs a record-the-choice action;
  deferred until a chapter wants it.
- A JSON export round-trip tool for authored content.
- Chapters 2-8 remain thematic sketches by design.
