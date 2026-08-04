---
name: pre-5-5-normalization-sequence
description: "Where the Garage Band Idle build stands as of 2026-07-31: audit-driven normalization between slice 5 and 5.5, what is left, and the shape already agreed for it"
metadata: 
  node_type: memory
  type: project
  originSessionId: de3a8a72-b32c-41c3-b489-c1620970bc9b
  modified: 2026-07-31T21:41:18.804Z
---

An audit of the slice 1-5 code (2026-07-31) produced a four-item normalization sequence that sits BETWEEN slice 5 and slice 5.5 and is not written down in `Docs/claude-code-build-prompts.md`. Agreed order: A, then B+C, then D, then 5.5. A, B and C are committed; **D is the only one left.**

- A: `UpgradeSystem.Apply` latches before granting (state-then-notify).
- B: one `GameEffect` family for upgrade payloads and rewards.
- C: scope declared once, by the fact that owns it (bar group, event tier), never by a reward asset.
- D: **not started** - condition invalidation with a drain.

**Why:** the build-prompts doc jumps from slice 5 to 5.5, so a session that reads only the doc will skip D and start 5.5 against a runtime that still polls unlock evaluation every tick. The doc's progress marker is also stale in a smaller way: slice 5 has no completion mark despite being built and hardened.

**How to apply:**
- **D's shape is already settled, don't re-derive it.** The aggregate signal goes on `ConditionContext`, which is already handed out per-instance ([[project-layout-and-workflow]] for where), so 5.5 making one per economy inherits it with no teardown. Mark-dirty plus a drain, where the drain is called at end of tick AND after each player action - NOT tick-only, which would put up to 100ms between buying a drummer and `play_for_crowd` revealing fans, an observable change this pass must not make. `ConditionContext` becomes `IDisposable` once it holds subscriptions. Collapse `ChapterScreen` and `UpgradeListModule` from four subscriptions each to one, keeping `UpgradeApplied`/`UpgradeCleared` (row lifecycle, not condition inputs). Retire the per-tick `EvaluateUnlocks`/`EvaluateContentUnlocks` poll and the manual calls in `BuyUpgrade`/`BuyGenerator`. The re-entrancy regression test committed with A is what makes this safe.
- Watch `ConditionContext`'s null tolerance: `Bars` and `Database` are null in fixtures, and every test constructing one directly gains a live object with a lifecycle.
- Two things were deliberately deferred INTO 5.5 rather than done early: an assertion that a context can enumerate its own resettable systems, and the decision that permanent effects RE-PROJECT from their facts at construction instead of surviving as stored grants (design doc rule 11). Settle the second while designing 5.5's recipe, not after.
- The run-transition coordinator waits for slice 6. It has no caller until a release exists, and its substance is an ordering whose two ends (the Records formula, the permanent pool) slice 6 and 5.5 define.
- Heuristic that held up: 5.5 is load-bearing for OWNERSHIP questions and not for DISCIPLINE questions. Two deferrals justified as "5.5 will change this shape" did not survive checking.

**Open verification John owns:** the `[ModifierQualifierId]` qualifier dropdown restored in `ModifierQualifierIdDrawer`. John said he would check it in the inspector next session (stated 2026-07-31), on a `tight_set` or `amp_strings` payload. It cannot be verified by [[unity-headless-verify-loop]] - a headless run never opens an inspector - and it fails SOFT, degrading to a plain string field, so a regression there is silent. Only its path arithmetic has tests.
