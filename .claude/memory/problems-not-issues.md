---
name: problems-not-issues
description: "Verdicts on findings and on non-actions: a true finding is not automatically work (what breaks TODAY), and a reason for NOT doing something is a claim about the code that has to be checked too"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7994d8cd-29fb-4920-8032-e10d58f56a96
  modified: 2026-08-28T20:49:39.690Z
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

**Why:** 2026-08-21. John stopped a round mid-flight: "I'm sensing severe feature creep here just to
fix issues instead of fix problems", and earlier, "You keep just fixing the direct issue instead of
the root cause." The worst case, a typed `GameContext` layer built off two withdrawn findings, is in
[[fact-addressing-is-id-plus-outward-walk]]. For calibration, the real problems that same session
were false validation errors on legal sibling content and a cycle that killed the editor silently.

**Every finding gets three questions before it is reported:** what actually goes wrong in practice,
what does the change cost, and does the rule it leans on come from John's design or from me. That
last one is not hypothetical - on 2026-08-20 I confirmed a finding citing design section 12.11 and
offered a fix before John asked "why is that actually an issue?" It was not one, and the rule it
cited was a sentence I had written into the doc two steps earlier: my own text validating a finding
against my own code. Confirming is cheap and looks diligent; disputing costs reasoning and risks
being wrong, which is exactly backwards for a verdict. A finding that fails the three questions is
reported as not-an-issue WITH the reasoning - that is a verdict too, and John should never have to
ask for it.

**A justification for NOT doing something is a claim about the code and gets the same check.** Both
shapes have failed it - "X will change this anyway" and "this constraint protects something" (three
of mine in one session, 2026-07-31). Name the specific mechanism and go read it. For a restriction,
ask whether anything would actually break if it were lifted, or whether it merely reflects how the
code used to be organized: defending an inherited restriction as if it were a decision is the "this
is how it is now" pattern John's normalization work exists to remove.

**How to apply:**
- Sort findings before starting: what runs today, what runs never, what a later step will write.
- Documentation triage that worked: a stale description of code that EXISTS gets fixed; an
  instruction the NEXT step will follow gets fixed, because it would be built wrong; a section
  describing a system a later step builds gets SKIPPED, because that step writes it anyway.
- When a fix is patching a boundary rather than removing it, say so and name the layer underneath
  before writing anything ([[reuse-the-existing-mechanism]]).
- Reporting an issue as an issue is the deliverable. It is not a lesser answer than fixing it, and
  it is all a review authorizes - [[quote-directive-before-editing]] owns that gate.
