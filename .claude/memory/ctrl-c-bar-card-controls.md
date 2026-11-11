---
name: ctrl-c-bar-card-controls
description: "How Ctrl C lays out a card of levelled bars (combined row, Auto, Hire xN, Buy One/Max/Next, Per Second/Raw, count thresholds) as John described it from play on 2026-11-10, what was ordered for chapter 2 and what is deferred"
metadata:
  node_type: memory
  type: project
  originSessionId: edbbbf61-8b4e-4ea3-b5a3-e83d43fa518d
  modified: 2026-11-11T04:47:25.980Z
---

John described these from his own play of Ctrl C on 2026-11-10, with two phone screenshots. They
add to [[ctrl-c-is-the-reference-game]]. The game must support BOTH layouts: the split one (bars in
one list, their level generators in another) and the combined one below. A section picks by which
widget it names.

**The combined row (ordered 2026-11-10 for chapter 2's Calendar: "start this")**
- Left: name with the level in parentheses, the bar under it, the bar's earnings printed inside it.
- Right: the buy control with the next level's cost.
- Bottom of the card: the next bar not yet started, as one button with its price
  ("Hire (5.50e4 lines)"), and beside it an **Auto** button.
- Auto buys auto-activation for the next bar that does not activate itself. It is dimmed when
  unaffordable, absent when every visible bar is already automatic, and returns when a new bar is
  bought. John applies this to "any bar group that you have to tap to activate".
- The Auto button's face reads only "Auto", which is how Ctrl C keeps the footer from crowding.
  A long press on it opens a window showing which bars already have auto and what the next one
  costs. (John, 2026-11-10, after seeing our "Auto (4.00e5 Cash)" push the Book button onto three
  lines. Reported as what Ctrl C does; not yet ordered for our screen.)
  The window, from two screenshots he sent: title "Auto"; one line of text ("Progress bars with
  automation will automatically start."); one row per bar on the card, the name at the left and at
  the right one of three things - "Purchased" in grey, "Buy" in the accent colour when it is
  affordable (pressing it buys that bar's auto from inside the window), or the price in grey when
  it is not; "Dismiss" at the bottom right. Every bar is listed, not only the next one.
- A bar that is not automatic is activated by tapping it.
- Rulings I made on my recommendation when he said start, not individually confirmed: The
  Office's standing-booking rows are removed; the "own 3 levels" gate on a standing booking is
  dropped so price alone gates Auto; the button prints its price, which Ctrl C does not.
- Header rate: the "(X/s)" beside a balance is the continuous income plus every running bar's
  per-second figure. Ctrl C prints no rate at the top; John saw the mockup with totals and said
  "that's fine". He wants to see overall Cash and Fans rates somewhere.

**The buy control: two variants, John undecided**
- Built first for chapter 2: a "+1 / +max" pair. John: "I'm not sold on the +1 +max look, but go
  ahead and do it that way for now and I might change it." Keep the row built so the control can
  be swapped.
- Ctrl C's own: one button per row reading "Hire: xN" over the cost, and one card-level button
  that cycles **Buy One / Buy Max / Buy Next**. In Ctrl C the cycle button exists once a Bulk Buy
  upgrade is owned. John's view: this variant is for a chapter with a bulk upgrade; chapter 2 has
  none, so the pair suits it.

**Deferred, not ordered (do not build without his word)**
- **Per Second / Raw toggle.** A card-level button. Raw is what one completion pays ("30.87 PC");
  Per Second divides by the cycle time ("25.51 PC/s"). The runtime gives both numbers
  (completion payout and completion rate), so this is a button and a remembered choice.
- **Count thresholds.** A row levels up as more of it is bought: at a count, an effect switches
  on, for itself or for another row (John's example: Components at level 5 gives Motherboards
  x100 speed). Authorable today as one modifier per threshold (`appliesWhen` OwnedCountAtLeast,
  effect on a bar's rate or a generator's yield). The gap: nothing lists a row's thresholds, so
  "Buy Next" has nothing to read and the row cannot show the next one. My proposal to him: author
  the thresholds on the levelled thing as a list of "at this count, this effect", the shape a
  bar's perFill entries already have. Order of work: thresholds, then the cycle button.
- **The "Hire: xN" button and the Buy One/Max/Next cycle button.**

**Why:** John reported these as he remembered them mid-session and asked that they be recorded
so they are not forgotten.

**How to apply:** when a chapter's bars are levelled, start from the combined row. Ask John
before building any deferred item. When the docs are next touched for this work, these belong in
`Docs/build-plan.md` under the UI polish entry and this file can shrink to a pointer.
