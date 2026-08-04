---
name: pre-5-5-normalization-sequence
description: "Where the Garage Band Idle build stands as of 2026-07-31: audit-driven normalization between slice 5 and 5.5, what is left, and the shape already agreed for it"
metadata: 
  node_type: memory
  type: project
  originSessionId: de3a8a72-b32c-41c3-b489-c1620970bc9b
  modified: 2026-07-31T21:41:18.804Z
---

An audit of the slice 1-5 code (2026-07-31) produced a four-item normalization sequence that sits BETWEEN slice 5 and slice 5.5. Agreed order: A, then B+C, then D, then 5.5. **All four are committed** (D on 2026-08-04, `dfded84`).

- A: `UpgradeSystem.Apply` latches before granting (state-then-notify).
- B: one `GameEffect` family for upgrade payloads and rewards.
- C: scope declared once, by the fact that owns it (bar group, event tier), never by a reward asset.
- D: condition invalidation with a drain - `ConditionContext` holds the aggregate dirty signal over its four inputs and publishes `Settled`; `GameManager.Settle()` is the single post-mutation seam (drain, then `RefreshTapValue`), called from end of tick, end of `Jam`, and each purchase.

**Next: slice 5.5, then 5.6** (the `revealFlag`-to-Condition collapse, written up 2026-08-04 in the build-prompts doc and gated behind 5.5).

**Why:** originally, because the doc jumped 5.4 to 5.5 with no sign these passes existed. That gap is closed - commit `8dbe02b` records A-D in the doc's progress marker and corrects 5.5 foundation 3, which had told its reader to BUILD the post-mutation seam that D already built. What this file is still for is the reasoning that never went into the doc: what was deliberately deferred, and what is still unverified.

**How to apply:**
- **D's outcome, for anyone touching 5.5:** the seam is `GameManager.Settle()` and 5.5 MOVES it into `EconomyContext` rather than building one. `ConditionContext` is `IDisposable` and holds live subscriptions, so a discarded context must be disposed - invisible with one economy, a leak with two. The drain is test-and-clear (clear the flag BEFORE evaluating) and deliberately does not loop to a fixpoint, so a second-order chain resolves at the next seam exactly as the old per-tick poll resolved it.
- One behavior shift D did make, on purpose: unlock evaluation moved from mid-tick to end-of-tick, which retired the old "content unlocks before fan accrual" ordering. No currency value changes in Chapter 1 (traced), but a gate a tick satisfies now reveals up to 100ms earlier. If a future content unlock sets the fans activation flag from a mid-tick balance crossing, fans accrue from the following tick.
- **SETTLED 2026-08-04, was the last open decision here:** effects re-project from facts at EVERY boundary, not only at load. A release resets the facts it owns and re-runs the projection; nothing strips run-scoped grants out of the modifier store in place. `ModifierSystem.ResetRunScoped()` is deleted in 5.5 (`UpgradeSystem.ResetRunScoped()` stays - latches are facts). The two mechanisms would have been written by different slices - slice 6 the release, slice 9 the load - and could disagree silently, which is the compounding failure design rule 11 describes. Recorded in design doc rule 6, slice 6's prompt (which previously instructed the `ResetRunScoped()` call), and slice 5.5 foundation 3. The other half of the same decision, an assertion that a context can enumerate every modifier-producing fact class, is the obligation this takes on and is now part of 5.5's job rather than a deferral.
- Nothing calls `ModifierSystem.ResetRunScoped()` in production code today - the release does not exist until slice 6 - so this cost a doc change and no refactor. What made it urgent was that slice 6's prompt already instructed the wrong mechanism, two slices out.
- The run-transition coordinator waits for slice 6. It has no caller until a release exists, and its substance is an ordering whose two ends (the Records formula, the permanent pool) slice 6 and 5.5 define.
- Heuristic that held up: 5.5 is load-bearing for OWNERSHIP questions and not for DISCIPLINE questions. Two deferrals justified as "5.5 will change this shape" did not survive checking.

**Open verification John owns - STILL UNCONFIRMED as of 2026-08-04:** the `[ModifierQualifierId]` qualifier dropdown restored in `ModifierQualifierIdDrawer`. John said he would check it in the inspector next session (stated 2026-07-31), on a `tight_set` or `amp_strings` payload; the D session came and went without it being raised, so it has now been carried across two sessions. It cannot be verified by [[unity-headless-verify-loop]] - a headless run never opens an inspector - and it fails SOFT, degrading to a plain string field, so a regression there is silent. Only its path arithmetic has tests. Worth asking him directly rather than waiting for it to come up.
