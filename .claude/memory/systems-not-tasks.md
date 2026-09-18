---
name: systems-not-tasks
description: "John, many times, furious on 2026-09-18 - we write SYSTEMS that must be flexible; a mechanism justified by what one chapter or one task needed is wrong by construction, and saying \"it was what chapter N needed\" is the tell"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: be48e022-d07a-4fd5-a47d-a68a28a7b926
  modified: 2026-09-17T23:57:26.014Z
---

John: "How many times have I said we're writing SYSTEMS here that need to be flexible? Coding them
specifically to one task is useless." Said after I explained twice that the idle claim excluding
bars and the bar group owning selection were "what chapter 1 needed". He said one more repetition
of that phrase and he gives up.

The instances he named on 2026-09-18:
- The idle claim excludes bars (RatePairs skips them; section 9 says bar progress never accrues)
  because chapter 1's covers were pool-fed and the feel wanted a banked pool. A time-fed bar has a
  rate over time and needs nothing else; the pool-fed one needs only its dependency produced first,
  which the tick's segment order already does. Idle should be the tick over a window.
- Consumption is a spend hard-coded in the bar's draw off one field (fillCurrency). It should be
  authored on the entry that feeds the bar, an action or an entry, not a hard-coded spend.
- "One active" / maxActive lives on BarGroupDefinition and only a bar can be selected. Active
  should be a fact about a member of any grouping, with the cap on the grouping; events, bars and
  future kinds read the same fact.

**Why:** the design doc's own rule is that content is data and mechanisms are generic (12.14
requirement 8, "no code decision per chapter"). A mechanism that fits one chapter's content is a
code decision per chapter wearing a system's clothes, and the next chapter finds the wall.

**How to apply:** when justifying a mechanism, the justification must be a statement true of every
instance of the kind ("a bar is a target that fills at the rates paying it"), never a reference to
what a chapter, a step or a test wanted. If the only justification available names a task, the
mechanism is wrong; say so instead of defending it. Never write or say "it was what chapter N
needed". See [[reference-survey-checks-idle-path]], [[reuse-the-existing-mechanism]],
[[root-cause-means-question-the-structure]].
