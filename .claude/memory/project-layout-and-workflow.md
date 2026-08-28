---
name: project-layout-and-workflow
description: "Working mechanics of this repo: a rewind does not revert the filesystem, heredocs mangle C# so use a Python script, and pre-2026-08-17 git history is a different architecture"
metadata: 
  node_type: memory
  type: project
  originSessionId: a3758315-7030-4a24-98c6-20cdec0e772e
  modified: 2026-08-28T20:46:09.172Z
---

Garage Band Idle is John's personal Unity 6000.5.4f1 idle game. The Unity project (Assets/,
Packages/, ProjectSettings/) sits one level down at `<repo>/Garage Band Idle/`, with `Docs/` beside
it at the repo root. Authority: the repo `CLAUDE.md` names the design doc and `Docs/build-plan.md`;
this file is only the mechanics.

**A conversation rewind does not revert the filesystem.** When John stops a turn and rewinds, the
messages go but every file edit that turn made stays on disk. On 2026-08-28 that left half of an
interrupted edit in `ChapterJsonImporter.cs`, referencing a variable that no longer existed, and it
surfaced only as a compile error on the next run. After any rewind or interrupt, treat the tree as
dirty in ways the transcript no longer shows: compile before trusting anything, and read the region
the stopped turn was editing.

**Editing mechanics:** Bash heredocs break on embedded quotes and backticks - they have eaten a turn
twice. For any multi-line C# or markdown replacement, write a Python script to the scratchpad with
the Write tool and run it, anchoring on exact strings and asserting the match count is 1. It fails
loudly on a stale anchor instead of silently mangling a file.

**Git history before 2026-08-17 is a DIFFERENT architecture.** John restarted the project that day:
the previous architecture, the docs-driven slice workflow (`claude-code-build-prompts.md`, slices
0-10), and all the code built from them were abandoned, and the dead files were deleted 2026-08-18 on
his call - only the salvage utilities survived. So an old commit is not a pattern to follow, and a
name recovered from one may have been renamed or deleted since. [[design-review-revisions]] holds
what was deleted after the rewrite; the design doc holds what the architecture is.
