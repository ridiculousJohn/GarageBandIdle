---
name: docs-and-sweeps
description: "A rule lives in code, tests, docs and comments - size a change by all of them, sweep all of them, and update the docs as part of the change"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:48:07.060Z
---

**Why:** the design doc is what a fresh session reads and reasons from, so a stale line there or in
a comment recreates the old design. John calls stale records "bullshit that's hanging around that I
don't want to find later", and a label smaller than the work reads to him as gaslighting.

**Size the change first**
- Grep the words the old rule uses across `Assets/Scripts`, `Assets/Tests`, `Assets/Content`, the
  live docs and `.claude/memory`. Say the count ("one line of code, two tests, one doc sentence,
  four comments"). If it is larger than the ask deserves, say which part is the sweep and let him cut
  it. Do it in one batch. If the work outgrows the label, correct the label before continuing.

**Docs are part of the change, under the same order**
- A decision changes the design doc in that change. Never parked in a "docs on landing" list; only
  text describing code that does not exist yet waits for the code.
- Every landing updates the build-plan row (date, test count, what changed) and walks
  `find Assets/Scripts -name "*.cs"` against design 12.13's file list, before the report. John
  ordered this once and does not repeat it.
- A grep finds renames. A changed mechanism usually leaves its intent sentence true, so also open
  the doc section that owns each thing the change touched and read it against the code.
- Restate the same claim wherever else it appears: `build-plan.md`, `chapter-01-content.md`, the
  chapter JSON.

**Sweeps**
- For a retired symbol or a reworded rule, grep the retired names (not the topic) across Scripts,
  Tests, Content, the live docs and memory in one pass. Then say what was grepped.
- "Did you miss anything?" is a command to grep, never to recall.
- Anything a sweep finds is fixed with the change. Exception: finished plans are history and are
  never swept or edited ([[project-layout-and-workflow]]).
