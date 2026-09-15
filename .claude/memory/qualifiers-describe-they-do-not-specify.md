---
name: qualifiers-describe-they-do-not-specify
description: "a qualifier in John's description of a situation (\"if there is only one chapter unlocked, like now\") describes the state he is in; it is not a predicate to implement"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 83b4ac2e-fbf6-4de3-87e4-8fd45a20088b
  modified: 2026-09-15T19:18:05.075Z
---

2026-09-15, fresh-game entry. John: "if there is only one chapter unlocked (like now) and no
previous chapter, it should just pick the chapter. On a new game here, it should just go to the
garage." I built the qualifier: no record, scan the roster for the single UNLOCKED chapter, which
needs the unlock condition evaluated, which needs a clock, which changes EntryChapter's signature
and two call sites. The rule he meant was one line: no record means the first chapter.

**Why:** "like now" marked the phrase as a description of the state he was looking at, not a
condition to test. The mechanism I added guarded a state nothing reaches (a fresh save with two
unlocked chapters), which [[problems-not-issues]] and [[reuse-the-existing-mechanism]] already
forbid. He then had to argue me down from it across three turns, and my agreeing at the end read
as caving rather than as the finding it was (the guarded state does not exist).

**How to apply:** when a report or a rule carries a qualifier, ask what state the qualifier is
naming and whether any OTHER state is reachable. If none is, the qualifier is scenery: implement
the rule without it. If one is, name that state and ask whether he wants it handled, before
building a check for it. Never let a phrase grow a predicate that grows a parameter.
