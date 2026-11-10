---
name: design-restart-2026-11
description: "2026-11-09 John is backing up and starting the game design over; decisions made in that restart (universal Records dropped, chapter resources are free labels)"
metadata:
  node_type: memory
  type: project
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T01:41:06.985Z
---

On 2026-11-09 John said the game is largely written but he wants to "back up and start over", and began
re-examining the design holistically. The existing design doc, chapter JSON and code describe the OLD
design; do not cite them as settled where they conflict with the decisions below.

Decisions John made in that conversation:

- **Chapters are not all Cash.** His words: "Not every chapter will be 'cash'. Some might be accumulating
  fans; some might be trying to get some Billboard ratings; ... Anything that could be accumulated through
  the base level progression generators the engine defines, that could be labeled anything." A chapter's
  base resource, release meter and prestige currency are roles; the labels are per chapter.
- **Universal Records is dropped.** His words: "yes Records never gelled very well, but we can roll Records
  into later chapters (maybe in a chapter trying to get a platinum record or something, I dunno)". Records
  as a root currency paying +2% global income is gone; "Records" may come back as ONE later chapter's own
  local resource. The platinum idea is a maybe, not a decision.

My interpretation (not his words): Roadies are then the only currency that crosses chapters, as Ctrl C's
completion tokens are ([[ctrl-c-is-the-reference-game]]). He did not explicitly confirm that sentence.

**Why:** the old design made every chapter "tap, buy, grow Fans, release an album" and paid every release
twice (root Records plus a chapter counter); John found it thin and found Records never fit.

**How to apply:** in design discussion, describe chapters by role and let each name its own resources; do
not propose a mechanism that assumes a root Records currency or a Cash/Fans pair in every chapter.

Landed the same day on his order (build-plan row "Chapter prestige is local (2026-11-09)", 939/939): root
Records removed from content, docs and tests; chapter 1 pays `demo_tapes`, chapter 2 `eps`; the capstones
no longer run the release; the design doc states roles, and that the income boost and the capstone gate
may read different prestige currencies (his words: "there is absolutely zero reason to limit it").
Chapters 3-8 are still the old sketches.
