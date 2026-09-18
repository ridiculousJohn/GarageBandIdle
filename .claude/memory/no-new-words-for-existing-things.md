---
name: no-new-words-for-existing-things
description: Never coin a second word for a thing that already has one; "pool" for the currency a bar drains cost a whole exchange on 2026-09-17
metadata:
  type: feedback
---

A thing is called by its one existing name, in code, docs, plans and chat. A second word for it
is a new concept the reader has to go looking for, and it does not exist.

**Why:** 2026-09-17, groups/consumption/idle contract. The currency a bar drains was "the pool" in
`BarFill.pool`, `BarPlan.PoolHome`, the group widget's "pool readout", and in the plan I wrote
("the pool's balance plus the window's inflow"). I asked John a question about "a negative pool
line". He: "what the fuck are you talking about with a pool? ... bars can be timed or consumption
based (where they drain a currency)", then "what pool though? what is it? as far as I know the
bars just pull from another resource (currency, whatever)", then "making up new fucking words for
something that already exists is just a recipe for confusion." There was never anything but a
currency, so every sentence using "pool" read to him as a mechanism he had not designed.

**How to apply:** before a word goes into a name, a comment, a plan or a question, ask what the
thing IS in the design's own vocabulary and use that word (currency, bar, generator, scope, flag).
If the existing code carries an invented word, do not propagate it into new text; the landing
that touches it renames it to the real thing. A question to John about "X" where X is a word the
design doc does not define is a question he cannot answer, and the confusion is mine to clear
before asking. Related: [[reuse-the-existing-mechanism]] (answer "what is it" in domain words),
[[systems-not-tasks]].
