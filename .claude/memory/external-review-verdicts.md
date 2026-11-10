---
name: external-review-verdicts
description: "How to answer a review John relays - verify, confirm or deny each defect, accept or reject each fix, then wait for apply"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:47:52.092Z
---

A relayed review (another agent's, a colleague's, his own; any format) is a request for verdicts,
not a go. John: "Confirm or Deny, Approve or Reject. Confirm that this is actually a defect before
inventing anything new. No bandaids." Later rounds arrive as "same acceptance rules".

**Why:** the reviewer's proposed fix is usually heavier than the defect. Applying fixes verbatim
adds mechanisms.

**How to apply**
1. Verify each claim against the code or doc before answering, and say what was checked.
2. Two verdicts per finding: is the defect real (CONFIRM / DENY, with evidence), and is the proposed
   fix the right size (ACCEPT / REJECT).
3. The reviewer's stated cause is a claim to check. Derive the cause from the code and from what the
   player is doing.
4. Ask what content or path can hit it today before sizing a fix ([[problems-not-issues]]).
5. A confirmed defect with a rejected fix gets ONE smaller fix: an existing mechanism used
   differently, or a mechanism removed ([[design-judgment]]). Never a menu.
6. End with the list of what would change and wait for "apply". Then apply exactly the accepted set.
