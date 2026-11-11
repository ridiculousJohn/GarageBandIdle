---
name: session-handoff-2026-11-10
description: "Where the 2026-11-10 session stopped - uncommitted work, what John was waiting to rule on, and the UI work still open. Transient: delete once it is acted on."
metadata:
  node_type: memory
  type: project
  originSessionId: 32094228-1f1c-4432-ae44-5d5aa66836b3
  modified: 2026-11-11T03:19:50.436Z
---

John asked for this at the end of the 2026-11-10 session: "record anything that isn't durable for
next session". It is a snapshot, not a rule. Check `git status` and `git log` before trusting any
line of it, and delete this file once its contents are stale.

**Commits made that day, oldest first** (all on `master`, none pushed)
- `b8b0d3c` build plan: runtime work to do before chapters 3-8
- `aa42043` the seven pre-chapter runtime changes (1092/1092)
- `e1c849c` design: songs as the loop of chapters 5-8
- `05721c4` docs: stale statements brought up to the map and song design
- `04f3797` the screen's look: Ctrl C's layout, pinned headline, drag-scroll (1120/1120)

**Uncommitted when the session stopped** (John had not been asked to commit, nor said to)
- The "no test reads a label" rework: typed shown-value members on about 15 widget files under
  `Assets/Scripts/UI`, and about 120 test assertions rewritten across `ScreenHostTests`,
  `WidgetInterpolationTests`, `ChapterLockUITests`, `HeadlineAndScrollTests`,
  `Chapter1ContentTests`. Three tests renamed. No wording a player sees changed.
- Docs for it: a paragraph in design 12.11 ("A widget exposes what it shows as data, and no test
  reads a label") and a build-plan row "No test reads a label (2026-11-10)".
- Memory edits: this file, `code-conventions.md` (the no-label-literal rule),
  `ctrl-c-is-the-reference-game.md`.
- Verified headless at 03:19Z on 2026-11-11: no compile errors, 1120/1120. That run was AFTER the
  last code edit.

**A mistake made in that work, fixed, worth re-checking**
- While normalizing a non-ASCII character an agent had written, I broke `EncoreTime.Format` in
  `TopBarUI.cs`: the Pass owner's timer text became `"221E"` instead of the infinity sign. The
  suite still passed, because no test asserts that text any more. Fixed to `"∞"` and
  re-verified. Nothing covers that glyph now; whether a format-function test should is part of
  the ruling below.

**John was about to rule on this** (I asked; he had not answered)
- Seven assertions compare a text-building static against a literal. They are not on-screen
  labels, so the rework left them: `RungFeedback.RefusalText` in `ActionListTests.cs:273`,
  `Chapter1WalkthroughTests.cs:503`, `GameActionTests.cs:585`, `RungFeedbackTests.cs:265`;
  `GeneratorRowUI.CostAndYieldText` in `Chapter2WalkthroughTests.cs:470`;
  `GeneratorInfoUI.ProductionText` in `Chapter2WalkthroughTests.cs:472` and `:477`.
- Also his to rule on: one reworked test compares the info screen's production label against
  `GeneratorInfoUI.ProductionText(...)`, the screen's own formatter, because that line has no
  shown-value member.

**Gaps the rework left, reported to him**
- The host has no accessor for the top bar or the collect dialog, so those tests read the
  session's offer and `EncoreTime` instead of the widget.
- A group's bar rows are not reachable from outside `GroupUI`; one progress test builds a
  `BarRowUI` over the same bar. The printed fill amounts are no longer asserted anywhere.
- On-screen things with no shown-value member yet: unlock-progress legs, an event's time left,
  refusal legs, the info screen's production line.

**UI work still open** (John: the look comes before anything else)
- The prestige card's wording as Ctrl C has it: "Gain X" over "Currently: Y", with a confirm.
- A section fading in when first revealed, the sections below sliding down. Agreed to apply the
  same fade to a row appearing inside a section; a section already visible at load does not
  animate.
- Real icons for the top bar. Needs image files: his own, or his go-ahead to download a set.
- The story card, chapter select and Discography were restyled unseen. John on checking them:
  "looks ok for now".

**After the UI**
- Design chapter 3 (Local Venues). Everything it needs from the engine exists.
- Song build steps 1-3 (design 7.5); nothing uses them until chapters 5-8 exist.

**State of his game**
- His save was in chapter 2 with an idle offer waiting. Every test run in the editor claimed it
  and bought things to look at the screen, then stopped without saving, so the save is as he left
  it.
