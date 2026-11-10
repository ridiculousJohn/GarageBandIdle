---
name: ctrl-c-is-the-reference-game
description: "Ctrl C is the game this design descends from - how to use the reference, and what John has reported from his own play"
metadata:
  node_type: memory
  type: reference
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:46:57.938Z
---

The design descends from Ctrl C. Its Android reconstruction (version 1.12.2) is in `Docs/ctrl-c/`:
`chapters-1-8/` and `chapters-9-end/`, each a chapter reference, data appendix, story transcript and
recovered data JSON. Claims there are tagged [D] data, [C] code-derived, [I] inference, [U]
unresolved.

**How to apply**
- On an open design question (pacing, idle, events, progression, UI), read `Docs/ctrl-c/` and ask
  John how Ctrl C does it before proposing a mechanism. His play is the fact where the two differ.
- Every "Ctrl C does X" is quoted from this file, `Docs/ctrl-c/`, a screenshot in the conversation or
  John's words in it. Otherwise it is not said. The design descending from Ctrl C is never evidence
  of a specific behavior.
- A reference feature is "already covered here" only when traced through every path that runs: the
  live tick, the idle claim, the save and an event restart. Name the paths traced.
- When John offers a shape and mine is chosen, record his shape too.
- Record what he reports from play in this file.

**From John's play**
- Numbers: fixed slots - two decimals below 1000, one below 10000, then a two-decimal mantissa and
  exponent - so a counting value churns in place.
- Generator row: name and count "(bought)" or "(bought+granted)", the granted part in the number
  format and printed only when something was granted; then "cost => the next unit's yield" with
  multipliers applied, never the owned total; then "+1" and "+N" where N is the most affordable.
- A generator can pay count into another generator. He knows of none paying two currencies.
- Header: the balance with the rate beneath it "(4.42e13/s)". A chapter whose currency has no rate
  shows no rate line.
- Top bar: two pills on every chapter. Left: Overclock (their Encore) with the remaining time, or
  infinity when permanent; tapping opens its window. Right: story list, chapter select, settings.
- Settings holds "Completion tokens" (their Roadies) and the allocation screen. It is always
  reachable and shows zero tokens and 1.00x on a fresh game.
- Completion tokens boost the press as well as the generators: at 30 tokens (2.50x) the chapter 1
  press pays 30 Lines where the unboosted press pays 12.
- Overclock window: "While active, yields of all generators is 2.0x", time remaining, "Boost for 4
  hours" (an ad, repeatable) and "Boost forever" (the Pro Unlock purchase). It has no higher tier.
- Idle window: what was earned, "Double this" (ad) and "Double All" (Pro Unlock). A Pro owner sees
  the doubled amount and a "Great!" button. It lists one line per thing paid, granted generator
  counts included, and never shows bars or teams.
- Story: beats show when available, some after a goal; a watched beat stays available for rewatch.
- Prestige button: "Liquidate (Gain X Money)" over "Currently: Y Money", with a confirm.
- Chapter 2 teams start tap-to-run; each gets its own automation upgrade; automated teams earn while
  away.
- Untimed events accrue idle earnings.
- Unknown to John: its font, whether idle accrual includes Overclock, and whether tokens moved while
  a chapter was dormant change that chapter's idle payout.
