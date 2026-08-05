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

- **The conversion traded a fail-closed check for a fail-open one, and an OMITTED gate is still the open case.** The deleted `ValidateFlag` reported an empty flag id, and an empty flag id meant fans NEVER accrued. A null Condition is legal content everywhere - it means "no gate" - so a fans source authored with no gate accrues from the first frame with an empty garage, and boot validation says nothing. The site moved twice: `FansConfig.ActiveWhen` (this slice) was deleted by 5.7, so it is now the band producer's production config `gate` ([[fan-accrual-is-production]]). Chapter 1 authors one and is unaffected. Note what this does NOT cover: since 2026-08-05 the importer aborts on a MALFORMED condition (unknown type, empty compound), but an omitted gate is legal by design and always will be - if a non-null check is ever wanted, it belongs on the fans production config, not on the bar group.
- **The base fan rate never checked for a band.** `FanSystem` pays `baseFansPerSec` (0.2) plus `perBandmateOwnedBonus` x bandmate count; no generator produces fans, and the base term is unconditional once the gate holds. The gate is the ONLY thing tying accrual to owning a band - in Chapter 1 because `play_for_crowd` is gated on `ownedCount drummer >= 1`. Authoring that ownedCount as the fans gate directly would put the constraint in the content instead of in how the flag happens to get set.
- **Two stale-key refusal shapes, and which to copy.** A bar group carrying `revealFlag` is SKIPPED (the currency `earn` precedent) because a group imported gateless shows from the first frame. The fans block is reported but STILL IMPORTS (the `constants.tapBaseValue` precedent) because a chapter's fans config is not skippable content. Pick by whether the content is skippable, not by which refusal was written last.
- **`ValidateFlag` is deleted.** Converting its only two callers to `ConditionEvaluator.Validate` left it with none. A reveal gate now gets exactly the checks every other gate gets - unresolvable id, non-positive threshold - which was the point.
- **Orphaned YAML survives an importer that uses `ApplyIfChanged`.** `rehearsal.asset` carried a dead `_earn` block with `_revealFlagId: covers` for two slices after 5.4 removed the field, because the live values never changed so the asset was never rewritten. Deleting a serialized field does NOT clean the assets; check the YAML directly after any field removal.
- **Next: slice 6.** 5.7 landed (`1227bab`) and retired `FanSystem` - see [[fan-accrual-is-production]]. It deleted `FansConfig.ActiveWhen`, which 5.6 had just added: that is a relocation onto the production config's `gate`, not churn, and it was only a relocation BECAUSE 5.6 had already made it a Condition. The bar group's `VisibleWhen` from this slice is untouched and still live. See [[project-layout-and-workflow]] for the slice workflow and [[unity-headless-verify-loop]] for verification.
