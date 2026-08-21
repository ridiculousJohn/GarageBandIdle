---
name: problems-not-issues
description: "A true review finding is not automatically work; triage for whether it breaks something in code that runs today, and supply the stopping rule the review loop does not have"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7994d8cd-29fb-4920-8032-e10d58f56a96
  modified: 2026-08-21T18:55:16.149Z
---

**Before fixing a finding, ask what breaks today** - not whether the finding is true. A finding is a
PROBLEM when it produces a wrong number, a wrong save, or a crash in code that actually runs. It is
an ISSUE when it is a true statement about a path nothing reaches: unauthored content, a system a
later build step has not written yet, or a release build of content that validation would have
rejected. Issues get a verdict and a note, never a mechanism.

**The review loop has no stopping rule of its own.** An external reviewer enumerates a class
exhaustively, which is its job. Treating each enumeration as a work order is how the validator grew
checks for content that does not exist while step 5 sat unbuilt. Supplying the stopping rule is my
job, and John should not have to interrupt to impose it.

**Why:** 2026-08-21. John stopped a round mid-flight: "I'm sending severe feature creep here just to
fix issues instead of fix problems", and earlier, "You keep just fixing the direct issue instead of
the root cause." The worst case that session: two review rounds reported that direct references
"discard identity" at runtime, and I implemented a typed `GameContext` layer - nine methods, fifteen
call sites, modifier grant checks, a test - before asking why aliasing was a problem at all. It was
not (see [[fact-addressing-is-id-plus-outward-walk]]); the whole thing was backed out by hand,
because none of it was committed. The reviewer withdrew both findings. Real problems that same
session, for contrast: false validation errors on legal sibling content, and a cycle that killed the
editor with no report.

**How to apply:**
- Sort findings before starting: what runs today, what runs never, what a later step will write.
- Documentation triage that worked: a stale description of code that EXISTS gets fixed; an
  instruction the NEXT step will follow gets fixed, because it would be built wrong; a section
  describing a system a later step builds gets SKIPPED, because that step writes it anyway.
- When a fix is patching a boundary rather than removing it, say so and name the layer underneath
  before writing anything ([[reuse-the-existing-mechanism]]).
- Reporting an issue as an issue is the deliverable. It is not a lesser answer than fixing it.
