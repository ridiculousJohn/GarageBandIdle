---
name: sweep-every-tier-of-a-defect-class
description: "When a defect is a CLASS (stale symbol, wrong rule wording), grep every tier before reporting - three rounds of 'did you miss anything?' each found the next one"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 393cd63b-24b4-4eb6-bad0-62e5972e7ae8
  modified: 2026-08-26T18:39:07.034Z
---

When the thing being fixed is a class of defect rather than one site, sweep every tier that can hold it before saying what is left. On 2026-08-14 I reported "two things" from a grep of `Assets/Scripts` alone, and John asked "did you miss anything?" three times; each round found the next tier down.

- Round 1: `Scripts/` only. Missed four in `Tests/` - a stale symbol in a fixture comment, and a class summary still saying "nothing constructs one yet" from an earlier commit in the same slice.
- Round 2: code only. Missed the design doc, where the same rule the build prompts stated loosely was also stated loosely in two summary lines.
- Round 3: nothing left, and saying so was correct.

**Why:** the tiers are not obvious from the first fix, and each one is where a future session actually reads. A stale comment in a test is read by whoever changes that test; a loose rule in the design doc outranks the same rule in the build prompts, since the doc is the source of truth. Reporting "that's everything" after one tier costs a round trip and, worse, is a false all-clear.

**How to apply:** for a stale symbol, grep the deleted names over `Assets/Scripts`, `Assets/Tests`, `Docs/*.md` and the chapter JSON in one pass, then sort hits into legitimate (importer refusal keys need the exact spelling they detect; a name used as the anti-pattern a rule replaced) versus stale. For a rule that was worded wrong, grep the rule's PHRASING shape, not the topic - "appears nowhere", "returns nothing", "the word" - across both docs, and check the summaries as well as the body, because a summary is what gets read first. Related: [[problems-not-issues]], and [[other-machine-lacks-ascii-rule]] for the other sweep this repo needs.

**"Did you miss anything?" is a command to grep, not to remember (2026-08-24).** John asked four
times in one session; the first three answers came from recall and were wrong, and the fourth ran an
actual sweep and turned up three more stale doc lines. Recall of what you edited is the least
reliable source available after a long session, and answering from it converts his check into
another round trip.

**How to run it:** list the identifiers the change RETIRED - every renamed type, deleted member and
changed signature - and grep that list across Scripts, Tests, all of Docs, and .claude/memory in one
pass. Grep the retired names, not the topic. Then say what you grepped, so the claim's coverage is
inspectable instead of being a promise. Anything the sweep finds is a blocker: John's rule is that
stale records are "bullshit that's hanging around that I don't want to find later", so they are not
a separate lower-priority category to report and leave.

**Standing exception (John, 2026-08-27):** the archived step plans for LANDED steps
(`Docs/step-04-plan.md` and successors) keep their stale lines - specifically step-04's
`idle_rate`/`idle_cap` forward reference, which predates the idle respell. He declined both the
one-line fix and deleting the archived plans, and closed it with "just leave it". A sweep that
hits an archived step plan reports nothing and relitigates nothing; only the live plan, the design
doc, the content doc, code, and tests are sweep targets.
