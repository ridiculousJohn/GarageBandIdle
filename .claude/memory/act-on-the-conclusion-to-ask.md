---
name: act-on-the-conclusion-to-ask
description: "When reasoning reaches 'I should stop and ask John', that IS the next action - send the question; never substitute more work, and never build a parallel path around a blocked sanctioned one"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7994d8cd-29fb-4920-8032-e10d58f56a96
  modified: 2026-08-21T01:10:20.557Z
---

**HARD RULE, stated by John 2026-08-20: Unity batchmode is the ONLY compilation check.** Never
Roslyn/csc, never dotnet, never any other compiler or batch path, for any reason - not as a
cross-check, not as a preview, not while the editor is open, not "just to see". The single
exception is John explicitly asking for it. If Unity batchmode cannot run, compilation is
UNVERIFIED and the answer is to say so, not to find another compiler.

The moment the thinking concludes "I should report this and ask him", that conclusion is the
action. Send the one-line question and stop. Continuing to work after reaching it - even work
that looks like progress on the same task - discards the conclusion and spends his time instead
of mine.

**Why:** 2026-08-20. Verification was blocked because John's editor had the project open, so
batchmode would abort. My own reasoning said, verbatim, to report that implementation was
complete and verification blocked until he closed the editor or ran the tests himself. Instead
of sending that sentence I spent several turns assembling a Roslyn command line - my own
reference list, my own compiler version, my own idea of which files were in the assembly - to
compile the project a second way. His response: "then just fucking stop and ask me to close the
fucking editor". Two failures stacked: ignoring my own conclusion, and building a PARALLEL
VERIFICATION PATH around the blocked sanctioned one, which is [[reuse-the-existing-mechanism]]
applied to tooling. A green light from an unsanctioned build is worthless - if it disagrees with
Unity, mine is the wrong answer, so the work could never have been worth anything.

**How to apply:** Blocked means blocked: name the blocker, name what unblocks it, one sentence,
then wait. "The editor is open on this project - close it and I'll run the loop." Do not
research a workaround, do not verify by another route, do not report completion with the
verification quietly missing. The verification path is the one
[[unity-headless-verify-loop]] describes and nothing else. This is the same underlying habit as
[[quote-directive-before-editing]]: the answer was already known and doing work anyway is what
made it a problem.
