---
name: ctrl-c-bar-card-controls
description: "Ctrl C's card of levelled bars as John described it - what is built (combined row, Auto window, cycle buy, thresholds, toast, Per Second/Raw, level-0 first bar) and what is still deferred"
metadata:
  node_type: memory
  type: project
  originSessionId: 6de30a77-b186-4a60-a5f3-377cf4424b41
  modified: 2026-11-13T03:06:37.131Z
---

John described these from his own Ctrl C play (2026-11-10 to 2026-11-12). They add to
[[ctrl-c-is-the-reference-game]]. The design doc 12.7/12.11 is the authority for what is built;
this file keeps only what the docs do not.

**Built (2026-11-10 to 2026-11-12, all committed)**
- The combined levelled row, the Book button for the next unstarted bar, Auto (one word; a hold
  opens the Auto window listing every bar as Purchased / Buy / price).
- Buy control chosen per card by content: the "+1 / +max" pair or Ctrl C's cycle (Buy One / Max /
  Next, "Hire: xN" per row). Chapter 2's calendar uses the cycle. John was "not sold" on the pair.
- Count thresholds on a generator (effects on itself or a named row), a toast when one is crossed
  ("Tuesday Open Mic x2 speed"); the row shows nothing about thresholds except the N under Buy Next.
- Per Second / Raw toggle (top left of the card).
- A card with no started bar shows its first bar as a level-0 row with its Hire button; Auto has
  nothing to automate and is hidden until a bar is hired.

**Deferred, not ordered (ask John before building)**
- Later prestige runs: Ctrl C shows EVERY bar from the start, all but the first with a ghosted buy
  button. John is fine without it for now; his idea: a flag set on the first EP release drives it.
- Ctrl C's "Per Second" and "Buy Max" are outlined pill buttons; ours are plain accent text. Raised
  with John 2026-11-12, no answer.
- Small things flagged after the threshold build, no answer: payout toasts name the draw ("House
  Party Draw x2 payout") not the gig; the toast sits over the gig card, not at the screen bottom;
  the cycle mode resets to Buy One after a chapter switch or the idle dialog.
