---
name: project-layout-and-workflow
description: "Where things live in this repo, which documents are authority, and the editing mechanics that bite"
metadata:
  node_type: memory
  type: reference
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:46:19.324Z
---

- Unity 6000.5.10f1 project at `<repo>/Garage Band Idle/` (Assets, Packages, ProjectSettings). `Docs/`
  sits beside it at the repo root.
- Authority: the repo `CLAUDE.md`, then `Docs/garage-band-idle-design.md` (section 12 is the
  architecture spec) and `Docs/build-plan.md` (order, status, expected test count).
  `Docs/chapter-01-content.md` owns chapter 1's numbers.
- Content is authored as JSON in `Assets/Content/` (`root.json`, `chapter-NN.json`). The importer
  writes assets under `Assets/ScriptableObjects/`. `Docs/chapter-0N-*.json` are hand-authored
  friendly files and are not imported.
- The Ctrl C reference is `Docs/ctrl-c/` ([[ctrl-c-is-the-reference-game]]).
- Finished plans (`Docs/step-*-plan.md`, other `*-plan.md`) are history: never edited, never sweep
  targets.
- Git history before 2026-08-17 is a different, abandoned architecture. Never a pattern to follow.
- A conversation rewind does not revert the filesystem. After a rewind or an interrupt, compile
  before trusting the tree and read the region the stopped turn was editing.
- Shell heredocs mangle C#, JSON and markdown quotes. Use Edit with exact anchors, or a Python
  script in the scratchpad that asserts each anchor matches exactly once.
- Verification is [[unity-headless-verify-loop]].
- Open, deferred by John: a digit font. In the default runtime theme only "1" is narrower, so a
  right-anchored number jitters. The fix is a font asset with tabular digits plus a
  `-unity-font-definition` rule on numeric labels; alignment and formatting do not fix it.
