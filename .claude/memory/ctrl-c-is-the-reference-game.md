---
name: ctrl-c-is-the-reference-game
description: Ctrl C is the reference game Garage Band Idle is modelled on; check design questions against it instead of reasoning from first principles
metadata: 
  node_type: memory
  type: project
  originSessionId: e6b3ee4e-601a-46e9-8b69-94d55b6fad6a
  modified: 2026-08-24T18:13:53.524Z
---

Ctrl C is John's strong reference for Garage Band Idle. The design doc was built against it across
extensive conversations and two rewrites, so its shapes are inherited, not invented. Stated
2026-08-24 after I failed to recognize the name and claimed it appeared nowhere in the repo - it
does, and I asserted that without checking.

Where it survives in writing (thin, which is why this memory exists):

- Design doc line 234 cites "Ctrl C's Lines to Knowledge" for the push-past-the-gate payout shape:
  bank at a threshold, keep accruing past it at a lower rate, so the offer condition sets the floor
  and a piecewise formula makes press-now-or-push-on emerge from the curve.
- Commit ca6d068 left one decision open "pending a check against Ctrl C" - whether a timed event's
  timer pauses while unfocused.
- 2026-08-24: untimed events accruing idle earnings is "all but required" in Ctrl C, which settled
  the no-idle-earnings rule as applying to TIMED events only.

**Why:** the doc names it once, as a parenthetical about one formula, so nothing tells a fresh
session that the whole design descends from it. Without that, design questions get answered from
first principles and land somewhere Ctrl C already answered differently.

**How to apply:** when an open design question comes up - pacing, idle structure, event shape,
handicaps, progression - ask how Ctrl C does it before proposing a mechanism, and say so when the
answer comes from there. Never claim knowledge of its specifics; ask John, then record what he says
here. Related: [[design-review-revisions]], [[project-layout-and-workflow]].
