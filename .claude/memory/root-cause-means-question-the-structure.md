---
name: root-cause-means-question-the-structure
description: "When John says \"root cause\" or \"bandaid\", the answer re-derives from the player's model and questions MY OWN design's structure, not the reviewer's framing; never a menu of edits"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: fe66be03-95f9-4b52-a209-cdb4ac5fc039
  modified: 2026-09-02T00:17:36.587Z
---

2026-09-01, slice C review: a reviewer flagged `elapsed <= 0` clearing banked production on a
same-frame click. I confirmed and proposed the one-character fix. John called it a bandaid. I
then offered a unification, then deleting the flush, each a variation on the structure the bug
lived in. He had to ask "WHY is that comparison in the click path at all?" and "if I'm clicking
I'm not idle" before I asked the first question: why does a player action read the clock at all.
The answer was immediate once asked - my own plan had made "a command IS a clock sample", which
put clock measurement in the path of every player action. The fix was structurally SMALLER than
the original: the frame measures the clock, commands settle the bank, entry sets the reference.

**Why:** A reviewer's finding frames the defect at the level the reviewer saw it. Starting there
defends the surrounding structure by default, and when that structure is mine I defend it hardest.
"Root cause" from John means: derive from what the player is doing (a click is live play, so
nothing idle- or clock-shaped belongs in its path) and ask whether the structure should exist,
including structure I wrote and he approved in a plan. A menu of alternative edits is appeasement,
not analysis, and he reads it as such.

**How to apply:** "Confirm or deny" on a finding is TWO verifications: that the defect exists,
and that its CAUSE is what it actually is, derived by me from the code and the player's action.
The reviewer's stated cause is a claim to check, not the answer - a reviewer sees the symptom's
nearest line, and confirming that line as the cause is what happened here. A bug is code doing the wrong thing; when the thing was wrong to ATTEMPT, the fix is to stop
attempting it, not to make the attempt succeed - deleting the path is a first-class fix. On
"bandaid" or "root cause": stop offering fixes. State in one line what the
player is doing and what the code path does that the player's action does not need. If a design
sentence of mine put it there, say so and name the sentence. Then give ONE fix, and check it is
simpler than the original. Answer "does it cause other issues" by tracing the phase transitions
(boot, entry, dialog, claim) before proposing extra lines - the frame's per-frame sampling usually
already covers them. See [[pushback-means-rederive]] and [[reuse-the-existing-mechanism]].
