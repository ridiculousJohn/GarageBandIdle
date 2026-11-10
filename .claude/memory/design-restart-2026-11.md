---
name: design-restart-2026-11
description: "The design decisions John made when he restarted the design on 2026-11-09, and what is still the old sketch"
metadata:
  node_type: memory
  type: project
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:46:25.566Z
---

On 2026-11-09 John backed up to re-examine the design as a whole. These decisions are landed in the
design doc, content and tests (build-plan row "Chapter prestige is local (2026-11-09)").

- **A chapter names its own resources.** Not every chapter is Cash and Fans; anything the base
  generators can accumulate may be the chapter's subject (fans, chart ratings, any band thing). The
  design doc states roles only: base resource, release meter, prestige currency.
- **No universal Records.** Roadies are the only currency that crosses chapters. Chapter 1's prestige
  currency is `demo_tapes`, chapter 2's is `eps`. Records may return as ONE later chapter's own
  resource (a platinum-record chapter was floated, not decided; it is in the backlog).
- **The income boost and the capstone gate are separate authored references.** One currency may do
  both (chapters 1 and 2) or two may. "There is absolutely zero reason to limit it."
- **A capstone runs no release.** It pays the Roadie, sets the completion flag and resets the chapter.
- **Kept on purpose as engine capabilities no content uses:** the multi-target `AddCurrency` and
  `ExecuteRung`.
- **Replay pacing is untuned.** A replay starts from zero and is faster only through Roadies; the
  Roadie rate is revisited in the tuning pass.
- **Chapters 3-8 are still the old sketches.** Treat them as high-level concepts, not designs.

**How to apply:** describe chapters by role and let each name its own resources. Never propose a
mechanism that assumes a root Records currency or a Cash/Fans pair in every chapter.
