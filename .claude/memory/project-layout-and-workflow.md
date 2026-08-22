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

**Why:** On 2026-08-17 John restarted the project. The previous architecture, the docs-driven slice workflow (Docs/claude-code-build-prompts.md, slices 0-10), and the code built from them are ABANDONED. Docs/garage-band-idle-design.md was rewritten the same day and is the sole source of truth: §1-11 game design, §12 the new architecture — state stored in ScopeState containers, everything else computed on read; Condition / GameAction / PayoutFormula class families; lifetime is placement; a scope's rung is `{offerCondition, List<GameAction>}` (`Press` was renamed `Rung` in step 3, and `Action` became `GameAction` for the `System.Action` collision). It was then revised the same day through an accepted design review — see [[design-review-revisions]] for the corrections a stale summary would regress. The old architecture survives only in git history.

**Editing mechanics:** Bash heredocs break on embedded quotes and backticks - they have eaten a turn twice. For any multi-line C# or markdown replacement, write a Python script to the scratchpad with the Write tool and run it, anchoring on exact strings and asserting the match count is 1. It fails loudly on a stale anchor instead of silently mangling a file.

**How to apply:** Read the design doc before advising on or writing anything. Chapter 1's tuning numbers live in `Docs/chapter-01-content.md` (2026-08-18) — data only, in §12 shapes; the design doc stays numbers-free. Do not consult claude-code-build-prompts.md — it describes the dead build. Any pre-restart code under `Garage Band Idle/Assets/Scripts/` is not a pattern to follow. If a design decision changes during a build, update the design doc so it and the code don't drift. Content stays data-driven: ONE Addressables load of the root scope, which brings the whole directly-referenced graph with it (label-based discovery and the content database were deleted - see doc §12.14 requirement 8 and the repo CLAUDE.md).

**Dead-file cleanup EXECUTED 2026-08-18 on John's call** ("delete unnecessary scripts and build prompts, keep the chapter json"): `Docs/claude-code-build-prompts.md`, all of `Assets/Tests/`, and the dead architecture under `Assets/Scripts/` were git-rm'd (252 paths incl. .meta files). Everything survives in git history.
- **Kept:** `Docs/chapter-01-garage.json` (reference for Chapter 1 numbers — old generator names, costs, curves) and the salvage utilities: `Core/BigNumber.cs` (break_infinity wrapper), `Core/SubclassPickerAttribute.cs` + `Scripts/Editor/SubclassPickerDrawer.cs`, `UI/NumberFormatter.cs`, `Utilities/SingletonManager/**`. (`DefinitionIdAttribute.cs` and its drawer were salvaged then DELETED in `0e6a363` with the id-index machinery; ids are authored through `EditorInit` and the importer now.) No .asmdef files exist — plain Assembly-CSharp.
- `NumberFormatter.cs`'s `Format(BigNumber, CurrencyDefinition)` overload was trimmed on John's call (2026-08-18) since it referenced the deleted type; only the standalone `Format(BigNumber)` remains. A symbol-prefix variant can be re-added when the new architecture needs one.
