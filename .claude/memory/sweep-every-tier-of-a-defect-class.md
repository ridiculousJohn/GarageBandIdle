---
name: sweep-every-tier-of-a-defect-class
description: "When a defect is a CLASS (stale symbol, wrong rule wording), grep every tier before reporting - three rounds of 'did you miss anything?' each found the next one"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 393cd63b-24b4-4eb6-bad0-62e5972e7ae8
  modified: 2026-08-14T19:49:48.626Z
---

When the thing being fixed is a class of defect rather than one site, sweep every tier that can hold it before saying what is left. On 2026-08-14 I reported "two things" from a grep of `Assets/Scripts` alone, and John asked "did you miss anything?" three times; each round found the next tier down.

- Round 1: `Scripts/` only. Missed four in `Tests/` - a stale symbol in a fixture comment, and a class summary still saying "nothing constructs one yet" from an earlier commit in the same slice.
- Round 2: code only. Missed the design doc, where the same rule the build prompts stated loosely was also stated loosely in two summary lines.
- Round 3: nothing left, and saying so was correct.

**Why:** the tiers are not obvious from the first fix, and each one is where a future session actually reads. A stale comment in a test is read by whoever changes that test; a loose rule in the design doc outranks the same rule in the build prompts, since the doc is the source of truth. Reporting "that's everything" after one tier costs a round trip and, worse, is a false all-clear.

**How to apply:** for a stale symbol, grep the deleted names over `Assets/Scripts`, `Assets/Tests`, `Docs/*.md` and the chapter JSON in one pass, then sort hits into legitimate (importer refusal keys need the exact spelling they detect; a name used as the anti-pattern a rule replaced) versus stale. For a rule that was worded wrong, grep the rule's PHRASING shape, not the topic - "appears nowhere", "returns nothing", "the word" - across both docs, and check the summaries as well as the body, because a summary is what gets read first. Related: [[test-the-justification-not-just-the-claim]], and [[other-machine-lacks-ascii-rule]] for the other sweep this repo needs.
