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

**The mirror of this, 2026-08-20: TEST THE FINDING TOO.** An external review handed me a finding
citing design section 12.11; I confirmed it and offered a fix, and John had to ask "why is that
actually an issue?" before I checked. It was not one - both fault classes it guarded were already
load-time validation errors, short-circuiting lost nothing, forcing the check cost a Pow and a chain
walk per UI row per refresh, and the rule it cited was a sentence I had written into the doc two
steps earlier. Circular: my own text validating a finding against my own code. Earlier the same day
I "confirmed" stale-doc findings that existed only because I had broken the roadie formula myself.
Confirming is cheap and looks diligent; disputing costs reasoning and risks being wrong, which is
exactly backwards for a verdict. **Every finding gets three questions before it is reported: what
actually goes wrong in practice, what does the change cost, and does the rule it leans on come from
John's design or from me.** A finding that fails them is reported as not-an-issue WITH the
reasoning - that is a verdict too, and John should never have to ask for it.

**How to apply:** when the reason has the shape "X will change this anyway" or "this constraint protects something," name the specific mechanism and go read it. For a restriction, ask whether anything would actually break if it were lifted, or whether it merely reflects how the code used to be organized. Distinguish a constraint someone chose from one that fell out of a structure that no longer exists. Related: [[quote-directive-before-editing]] on why a finding gets a verdict and nothing else.
