---
name: orchestrated-slice-workflow
description: How John wants a slice built - I orchestrate, Opus agents code, I review and approve; the split and the checks that worked for step 10 slice A
metadata:
  type: feedback
---

John, 2026-09-09, ordering step 10 slice A: "you are the orchestrator. use Opus-5 agents as your
coding agents. You run code reviews. It is your responsibility to approve their work. Look at ALL OF
YOUR MEMORIES AND RULES. use existing systems if they exist, that's why they are there. Report back to
me once you have approved all work with the results and any questions, deviations, concerns,
corrections, or anything else."

**Why:** the coding is delegated; the judgment is not. Every agent line item passes through my
review before it reaches him, and the report is mine, not a relay.

**How to apply (what worked for slice A, 604 -> 631 green in one pass):**
- Read every memory file and the whole plan section FIRST, then the code the slice touches, before
  writing a brief. The brief carries the house rules verbatim (ASCII, // comments, no delta framing,
  no renames, BigNumber, no lookups, command-owns-flush-only) because the agent has none of them.
- Fix the API contract myself - exact names, signatures, semantics - and run the runtime agent and
  the tests agent IN PARALLEL against it, each forbidden from the other's directory. Neither runs
  Unity or git; I do. One compile-plus-import run, one test run, then review.
- Review means reading every diff against the design rule it implements, not against the plan
  sentence ([[verify-your-own-prior-statements]]): the one defect in slice A was a plan sentence
  whose rationale had died in a same-day review, and I had copied it into the contract.
- Filter agent line items before they reach the report ([[problems-not-issues]]): the report lists
  decisions with a second option someone would notice, questions, and findings with evidence.
- The verify script is copied from [[unity-headless-verify-loop]] into the scratchpad with an
  import mode and a tests mode; check for an open editor by path before either.

**2026-09-18 - an agent's "I could not trace this to a design rule" is a stop, not a note.** The
runtime agent's report said of my contract's empty-dialog behavior: "no design sentence says a
dialog may show nothing." I read it, kept the behavior, and listed it in the landing report as a
decision. John: "you even overrode the coding agent's raising the concern, and made the WRONG
decision, instead of saying 'hey, maybe John might want to answer this'." The agents are briefed to
report every sentence they cannot trace precisely so that list reaches him; each item on it is
either traced by me to a design sentence I can quote, or sent to him as a question before the suite
runs. Never resolved by my own judgment and never filed under "decisions made".
