---
name: pushback-means-rederive
description: "John disputing my model of HIS design means re-derive from primitives, not defend the reading - doc wording can itself be the defect"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 686db24c-b6c5-412c-aea3-669efcf46f19
  modified: 2026-08-25T23:01:44.695Z
---

2026-08-25, step 7 planning: an hour burned on "reserved target ids". John said from the start
that idle_rate/game_speed are stat-addressed ("have its stat be idle_rate with no target") and
that consumers query per-target inside their own loops. I anchored on the doc's phrase "reserved
effect TARGET ids", invented owner-anchor machinery (code-owned Definitions, null-owner branches),
and defended the label through repeated pushback until he spelled the mechanism out in caps. The
answer was 12.2's own line: "stats (rate, yield) named and extensible" - the open stat vocabulary
existed FOR this.

**Why:** His pushback on my model of his own design is data that the reading is wrong, not
pressure to resist. This is the complement of [[never-cave-to-pressure]]: a verified fact does not
change under pressure, but my READING of a doc is not a fact - and the doc's wording can itself be
the bug (here the doc said both "target" and the stat mechanism; I quoted the wrong half as
authority).

**How to apply:** Pushback means exactly one of two things - he is missing something, or I am -
and his own framing (2026-08-25) is that it is symmetric: he could have been wrong there too. So
the job is to DISCRIMINATE, not to pick a side: produce the specific line of code, doc mechanism,
or concrete walkthrough that would make one of us demonstrably wrong, and walk it against HIS
model, not mine. If I cannot produce a discriminator, what I hold is a reading, not a fact, and I
should say so. Restating my frame louder discriminates nothing and is what circling looks like.
Socratic questions from him ("when is X queried?", "who calls it?") mean he is walking me back to
primitives because I skipped them - answer each one literally and shortly, no extrapolation.
