---
name: ctrl-c-is-the-reference-game
description: "Ctrl C is the game this design descends from - how to use the reference, and what John has reported from his own play"
metadata:
  node_type: memory
  type: reference
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-13T03:32:35.773Z
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

**The look, from the four Play Store screenshots** (seen 2026-11-10 at
`play.google.com/store/apps/details?id=com.amorfatite.pretentious`; stills of an unknown version, so
John's play wins where they differ - they show no top pills, only a "+" at the right)
- Portrait phone. Light and dark themes with the same layout. One accent colour, orange, on the
  big press buttons; everything else is neutral greys.
- Header: "Lines: 1.91e12" large and centred, the rate beneath in parentheses, lots of space
  around it. Scrolled, it collapses to a compact bar with the same two lines small.
- Sections are rounded CARDS on the page background, a gap between cards.
- Generator row: name and count on the first line; a smaller grey second line "cost = yield";
  at the right two bare bold text buttons, "+1" and "+N", no button chrome; a thin divider between
  rows; a row that cannot be bought yet is dimmed.
- Prestige is its own card: "Rewrite (Gain 147580 Knowledge)" with an info icon, "Currently:
  540.31 Knowledge" beneath, and a blue text button "Confirm".
- Idle window: a small dark card, "While You Were Away", "You made:", one line per currency,
  "Double This" with an ad icon, "Dismiss" at the bottom right.
- The typeface looks like Roboto, which is Flutter's Android default (my inference; the reference
  says the app is Flutter).
- NOT visible in stills: how a section reveals itself the first time. John wants that behaviour
  copied (2026-11-10: "how they reveal their new sections on first view"); it has to come from him.

**The current version, from two phone screenshots John sent 2026-11-10** (these beat the store stills)
- At rest, top to bottom: a tall empty band, then "Lines: 12.00" very large and centred with
  "(0/s)" beneath; then the TWO PILLS, below the headline and above the press buttons - left a
  stopwatch icon and "0:00:00", right three icons (story, chapters, settings); then the orange
  Ctrl / C / V buttons; then the generator card.
- Scrolled: a bar pinned at the top holds a bare stopwatch icon at the left, "Lines: 12.00" with
  "(0/s)" small beneath in the centre, and a bare gear at the right. The pills are gone into those
  two icons (no time text, no story or chapters icon). The press buttons and the cards scroll under
  the bar; the buttons "just scroll off the top".
- John: the banner "scrolls, but as it gets close to the top it gets smaller until it anchors to the
  top and is always visible... it smoothly transitions in size and position".
- Rows in this version have no divider lines, more space between them, and a light card with a
  soft shadow. All text in an unaffordable row is grey, the "+1" buttons included.
- "our 'jam' button is akin to their 'Ctrl' buttons".

**Where Garage Band departs from it on purpose** (John, 2026-11-10)
- A gated prestige card always shows what it still needs (2026-11-12: "if there's something
  blocking the confirm then it should show the amount needed"), which Ctrl C's cards do not.
- The two pills stay fixed at the top. "the pills at the top are fine in my opinion, having those
  options move around seems like bad UI design." They do not fold into icons.
- The headline (the chapter's first currency) is big until it scrolls up, then gets smaller and
  stays visible.

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
- Section reveal (2026-11-10): "when a section is first made visible, it fades in. If it becomes
  visible in between two different sections, then the lower sections tween down to make room as
  the revealed section fades in". He wants this copied.
- Upgrade strip (four screenshots, 2026-11-12): chapter-wide buffs sit in their own UNTITLED card
  below the bar card, as square-ish outlined tiles in a row that scrolls sideways, with a thin
  scroll bar under them. A tile: a round icon, the name (ellipsised) with its level "(3)", and
  beneath it the cost "1.62e30 Money" in grey when unaffordable, or "Owned" in orange when maxed.
  Tap buys; long press opens a details window: title (with level), "Current effect: ..." and
  "Next: ..." for a levelled one, "On buy: ..." for one not bought, only "Current effect" when maxed;
  a "Buy Upgrades On Tap" switch ("Long press to show details."); "Close". Upgrades can have
  levels. John prefers this to our titled Office card of full-width rows: "more compact".
- Prestige confirmation (2026-11-12): some prestige buttons open a confirmation box and some do
  not - a "major" reset that clears large swaths of progress asks, a small one that resets one tier
  does not. John: "that's something that should be authored" (per rung, not a code rule).
- Prestige cards (two screenshots, 2026-11-12, chapter with Money/Assets/Shares): each prestige is
  its own card under the cards it resets. Line 1 "Liquidate (Gain 1.95e10 Money)", line 2
  "Currently: 3927.8 Money", line 3 a centred text button "Confirm" - orange when it can be pressed,
  grey when not; a locked card greys its title and still shows "(Gain 0.00 Shares)". No condition
  lines on the card. Only SOME cards carry a black (i) beside the title (Buy Shares does, Liquidate
  does not); Buy Shares' (i) opens a window: title "Buy Shares", "Requires 1.00e4 money", "Close".
  Others may say more. Liquidate converts Assets to Money and resets only the tier above it; Buy
  Shares converts Money to Shares and resets most of the chapter (Discipline and Metrics reset,
  some later buffs do not - details in Docs/ctrl-c).
- Chapter 2's Data (+1% Lines per point): John does not recall it ballooning within one promotion
  (2026-11-12, asked after the sim showed our Buzz, the same shape, reaching x5000 Cash in a 30-minute run).
- Unknown to John: its font, whether idle accrual includes Overclock, and whether tokens moved while
  a chapter was dormant change that chapter's idle payout.
