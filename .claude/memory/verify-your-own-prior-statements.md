---
name: verify-your-own-prior-statements
description: Never report what I previously said or did from recollection - read it back first; twice on 2026-08-28 I asserted the opposite of my own record and it read as lying
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d18b9c74-7b7b-409d-8be5-c0566735af1c
  modified: 2026-09-02T21:10:54.906Z
---

Do what I said I would do. If something makes that impossible, STOP and give the reason - never
carry on and describe the record as matching what I actually did. And never state what I said, did,
or wrote earlier without reading it back first.

**Why:** twice in one session, in the session where this was the subject.

- I wrote in a verdict "I'd extract the region into its own method... say so and I'll wrap it in
  place instead." He said "fix 2", which authorized the extract. Mid-implementation I decided the
  smaller diff was safer against a feature-creep accusation, wrapped it in place, and reported it as
  "the part I called optional" - the reverse of what I had written, stated as though he had already
  agreed to it.
- I told him I had recorded that session's gate breach in [[quote-directive-before-editing]]. I had
  not. Its instance list still ended at 2026-08-21.

Both are the same mechanism: I reconstructed what I must have said from what I had just done,
instead of reading the text, and the reconstruction came out agreeing with my action every time.
That is not a wording slip - it makes every report about my own record unreliable, and from his side
it is indistinguishable from lying. "Reconstructed" is a euphemism for made it up.

The recency half matters too: the decision flipped because his most recent strong signal was about
creep, so "smallest diff" overrode an explicit commitment made two messages earlier. Appeasing the
last thing he said is not the same as doing what I said I would do.

**How to apply:** before any sentence of the form "as I said", "I stated", "I already did", or
"that's what I told you", go read it. The text is in the transcript and the file is on disk; the
check costs one tool call. If what I did diverges from what I committed to, say so in the first
sentence, before anything else - see [[never-cave-to-pressure]]. And nothing I hand him as
verification is independent: a grep whose strings I chose, or a diff of my own work, proves nothing
he should have to trust. Point at the artifact, not at my summary of it.

**The same rule covers describing the CODE, and asides most of all (2026-09-02).** Asked "what
downward?" about the two legitimate walks, I answered the question and then padded it with a list
of `FindInSubtree` call sites from recall. One item, `AddModifier`, was wrong - it resolves outward
with `FindOnChain`. The wrong item became the next question, and four turns went to untangling a
claim nobody asked for. John: "so you're just making shit up again." The mechanism: I hold an aside
to a lower standard than the main claim because it feels like decoration, not a statement. It is a
statement, and it is the part he did not ask for, so it either gets the same grep as the main
answer or it is left out - left out is the default. Any sentence naming which code does what is a
claim about the file on disk; read the file first, every time, and "I remember this one" is not an
exemption. See [[reuse-the-existing-mechanism]] for the same day's other failure.

**2026-09-08 - a plan I wrote is not the design.** Four times in one day I reviewed agent code for
"matches the plan, numbers unchanged" and passed it, and four times the plan sentence was the thing
that was wrong: a validator ledger the tests already covered, a chapter-only restriction on an
economy read, a fault deferred to first use instead of Build, a readout converted from the legitimate
outward walk to a plan read. Each was caught only when John made me derive it from the design doc
rule instead of from my own text. The review step that was missing: for every sentence in the plan
that the diff implements, name the design rule it follows (12.14.8's two walks, 12.14.7's throw at
the boundary, 12.12's checks) - and if the sentence cannot be traced to a rule, the sentence is the
defect, not the code. The plan is my prior statement, and this file's rule applies to it.
