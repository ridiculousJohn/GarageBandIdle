---
name: test-the-justification-not-just-the-claim
description: "Before defending a deferral or a restriction, check whether the reason is real or inherited - three in one session did not survive checking"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: de3a8a72-b32c-41c3-b489-c1620970bc9b
  modified: 2026-07-31T21:41:32.334Z
---

When I justify NOT doing something - deferring work, keeping a constraint, declining a normalization - check the justification against the code the same way I would check a claim. In one session (2026-07-31) three of mine failed that check:

- Deferring condition invalidation until after slice 5.5 "because 5.5 makes it per-context": `ConditionContext` was already per-instance, so nothing needed teardown.
- Deferring it again because it "gives `ConditionContext` a lifecycle": that cost was already paid by the earlier step I had put before 5.5.
- Refusing to merge the importer's two effect vocabularies because it "would lose a real per-site check": the restriction was a fossil of two deleted class families, not a design. A reward paying a flat tap bonus was always coherent content.

**Why:** a justification is a claim about the code and deserves the same verification. Worse, defending an inherited restriction as if it were a decision is exactly the "this is how it is now" pattern John's normalization work exists to remove - so producing one while doing that work is self-defeating. He notices, and repeating it costs a round trip each time.

**How to apply:** when the reason has the shape "X will change this anyway" or "this constraint protects something," name the specific mechanism and go read it. For a restriction, ask whether anything would actually break if it were lifted, or whether it merely reflects how the code used to be organized. Distinguish a constraint someone chose from one that fell out of a structure that no longer exists. Related: [[bug-reports-are-verify-only]] on evidence before verdict.
