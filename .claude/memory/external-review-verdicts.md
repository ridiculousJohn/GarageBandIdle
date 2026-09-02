---
name: external-review-verdicts
description: "How John wants any review he relays handled - findings from another agent, a reviewer, or himself, in whatever format: confirm or deny each finding against the code, approve or reject its proposed fix separately, no bandaids, and no edit until he says apply"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 201953d8-f0bc-43b1-8cd9-02e2c494385a
  modified: 2026-09-02T20:35:12.124Z
---

When John relays a review - findings from another agent, a colleague, or himself, in ANY format:
`::code-comment{...}` blocks with priorities, a numbered list, a paragraph of prose, a single
sentence - his instruction is: "Confirm or Deny, Approve or Reject. Confirm that this is actually
a defect before inventing anything new. No bandaids." The format varies (John, 2026-09-02:
"reviews might not always have that same format"); the shape of the answer does not. The tell is
the content - a claim that something is wrong plus, usually, a suggested fix - not the markup.
Later rounds arrive as "same acceptance rules" or "same approval rules".

**Why:** a review is a request for a verdict, not a go (repo CLAUDE.md). The reviewer's proposed
fix is usually heavier than the defect - in one round (2026-09-02, the step 10 plan) five of five
findings were real and three of five proposed fixes were rejected: an activation-interval ledger
for a bounded present-state inaccuracy, a boot gate on a network restore for one undoubled offer,
a durable transaction ledger for a fake store that never replays. Applying reviewer fixes
verbatim would have added three mechanisms to a plan whose whole point was adding none.

**How to apply:**
1. Verify each claim against the code or the doc before answering - grep, read the test, read the
   passage. State what was verified.
2. Two verdicts per finding, separately: is the defect real (CONFIRM/DENY, with the evidence), and
   is the proposed fix the right size (ACCEPT/REJECT). A confirmed defect with a rejected fix
   gets ONE smaller fix named, usually a sentence in the plan or an existing mechanism used
   differently - never a menu.
3. Ask "do we even care?" about the defect's reach (what content can hit it TODAY) before sizing a
   fix; John asked it himself and the answer closed a P1.
4. End with the list of what would change and wait for "apply" / "make those changes". Then quote
   it (`Acting on:`) and apply exactly the accepted set.
5. A fix that removes a mechanism beats one that adds an ordering rule: the stale-record findings
   were closed by judging a record against `ctx.NowUtc` (which every context carries) instead of
   adding terminal prunes - see [[reuse-the-existing-mechanism]].

Related: [[quote-directive-before-editing]], [[problems-not-issues]], [[pushback-means-rederive]].
