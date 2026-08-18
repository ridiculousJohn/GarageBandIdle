---
name: project-layout-and-workflow
description: "Garage Band Idle repo layout and where truth lives; restarted 2026-08-17 — rewritten design doc is the sole source of truth"
metadata: 
  node_type: memory
  type: project
  originSessionId: a3758315-7030-4a24-98c6-20cdec0e772e
  modified: 2026-08-17T23:30:00.000Z
---

Garage Band Idle is John's personal Unity 6000.5.4f1 idle game. The Unity project (Assets/, Packages/, ProjectSettings/) sits one level down at `<repo>/Garage Band Idle/`, with `Docs/` beside it at the repo root. There is a stray empty `Garage Band Idle/Garage Band Idle/Logs/` directory - it is not the project.

**Why:** On 2026-08-17 John restarted the project. The previous architecture, the docs-driven slice workflow (Docs/claude-code-build-prompts.md, slices 0-10), and the code built from them are ABANDONED. Docs/garage-band-idle-design.md was rewritten the same day and is the sole source of truth: §1-11 game design, §12 the new architecture — state stored in ScopeState containers, everything else computed on read; Condition / Action / PayoutFormula / BarFillBehavior class families; lifetime is placement; presses are `{offerCondition, List<Action>}`. It was then revised the same day through an accepted design review — see [[design-review-revisions]] for the corrections a stale summary would regress. The old architecture survives only in git history.

**How to apply:** Read the design doc before advising on or writing anything. Chapter 1's tuning numbers live in `Docs/chapter-01-content.md` (2026-08-18) — data only, in §12 shapes; the design doc stays numbers-free. Do not consult claude-code-build-prompts.md — it describes the dead build. Any pre-restart code under `Garage Band Idle/Assets/Scripts/` is not a pattern to follow. If a design decision changes during a build, update the design doc so it and the code don't drift. Content stays data-driven (ScriptableObjects discovered via Addressables; see doc §12.14 on authoring format).

**Dead-file cleanup EXECUTED 2026-08-18 on John's call** ("delete unnecessary scripts and build prompts, keep the chapter json"): `Docs/claude-code-build-prompts.md`, all of `Assets/Tests/`, and the dead architecture under `Assets/Scripts/` were git-rm'd (252 paths incl. .meta files). Everything survives in git history.
- **Kept:** `Docs/chapter-01-garage.json` (reference for Chapter 1 numbers — old generator names, costs, curves) and the salvage utilities: `Core/BigNumber.cs` (break_infinity wrapper), `Core/DefinitionIdAttribute.cs` + `Core/SubclassPickerAttribute.cs`, `UI/NumberFormatter.cs`, `Editor/DefinitionIdDrawer.cs` + `Editor/SubclassPickerDrawer.cs`, `Utilities/SingletonManager/**`. No .asmdef files exist — plain Assembly-CSharp.
- `NumberFormatter.cs`'s `Format(BigNumber, CurrencyDefinition)` overload was trimmed on John's call (2026-08-18) since it referenced the deleted type; only the standalone `Format(BigNumber)` remains. A symbol-prefix variant can be re-added when the new architecture needs one.
