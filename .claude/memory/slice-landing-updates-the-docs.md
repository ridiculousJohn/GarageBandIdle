---
name: slice-landing-updates-the-docs
description: "Every slice that lands updates the build-plan status line and design doc 12.13's file list in the same changeset - a standing order John gave once, not a per-slice request"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 3215b57f-b4ac-47f3-a94d-d30a07004e33
  modified: 2026-09-02T03:08:30.130Z
---

2026-09-01, slice B: John ordered "update the status line, update the doc to reflect the missing
files under UI, then commit". I did it for B and treated the order as spent. Slices C and D then
landed and were committed with the status line still saying "slices A and B" and 12.13 missing
GameClock, TickReport, ModuleWidget, ScreenHost, UIRoot and six widgets. 2026-09-02 he asked why
the status line and the doc were stale: "I said fix the status line a long time ago and the doc
change a long time ago."

**Why:** The plan's own "Docs on landing" section names the status line for every landing, and
12.13 claims to list every file. An order phrased for one slice about a recurring artifact is the
rule for the artifact, and John does not expect to repeat it.

**How to apply:** Before reporting a slice as done (and before any commit of it), update the
build-plan step's status line with the slice, date, test count and the review corrections, and
walk `Assets/Scripts` against 12.13's file list. Both are part of the slice, under the slice's
order. See [[doc-decisions-land-when-made]].
