---
name: reveal-is-a-condition
description: "What slice 5.6 established in Garage Band Idle - every reveal is a Condition, the two stale-key refusal shapes, and the fail-open gap the conversion left"
metadata:
  node_type: memory
  type: project
---

Slice 5.6 is committed (`5b8a917`, 2026-08-04): no definition asset or config struct carries a bare reveal-flag id. `BarGroupDefinition.VisibleWhen` and `FansConfig.ActiveWhen` are `[SerializeReference]` Conditions, `FanSystem.Active` evaluates through the condition context (and the factory builds it AFTER that context, the reordering `ProductionSystem` took in 5.4), and `BarListModule` evaluates per group with its `FlagSet` subscription collapsed into `Settled`. Builds on [[economy-context-as-built]].

**Why:** the doc says what 5.6 was asked to build. What it cannot say is what the conversion cost, or which of two refusal shapes a future stale key should follow.

**How to apply:**

- **The conversion traded a fail-closed check for a fail-open one.** The deleted `ValidateFlag` reported an empty flag id, and an empty flag id meant fans NEVER accrued. A null Condition is legal content everywhere - it means "no gate" - so a chapter that omits `activeWhen` accrues 0.2 fans/sec from the first frame with an empty garage, and boot validation says nothing. Chapter 1 authors `flagSet` at both sites and is unaffected. If a non-null check is ever wanted, the fans gate is the site that needs it, not the bar group.
- **The base fan rate never checked for a band.** `FanSystem` pays `baseFansPerSec` (0.2) plus `perBandmateOwnedBonus` x bandmate count; no generator produces fans, and the base term is unconditional once the gate holds. The gate is the ONLY thing tying accrual to owning a band - in Chapter 1 because `play_for_crowd` is gated on `ownedCount drummer >= 1`. Authoring that ownedCount as the fans gate directly would put the constraint in the content instead of in how the flag happens to get set.
- **Two stale-key refusal shapes, and which to copy.** A bar group carrying `revealFlag` is SKIPPED (the currency `earn` precedent) because a group imported gateless shows from the first frame. The fans block is reported but STILL IMPORTS (the `constants.tapBaseValue` precedent) because a chapter's fans config is not skippable content. Pick by whether the content is skippable, not by which refusal was written last.
- **`ValidateFlag` is deleted.** Converting its only two callers to `ConditionEvaluator.Validate` left it with none. A reveal gate now gets exactly the checks every other gate gets - unresolvable id, non-positive threshold - which was the point.
- **Orphaned YAML survives an importer that uses `ApplyIfChanged`.** `rehearsal.asset` carried a dead `_earn` block with `_revealFlagId: covers` for two slices after 5.4 removed the field, because the live values never changed so the asset was never rewritten. Deleting a serialized field does NOT clean the assets; check the YAML directly after any field removal.
- **Next: slice 6 (album prestige).** 5.6 was its precondition and is done. See [[project-layout-and-workflow]] for the slice workflow and [[unity-headless-verify-loop]] for verification.
