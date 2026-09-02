---
name: ctrl-c-is-the-reference-game
description: Ctrl C is the reference game Garage Band Idle is modelled on; check design questions against it instead of reasoning from first principles
metadata: 
  node_type: memory
  type: project
  originSessionId: e6b3ee4e-601a-46e9-8b69-94d55b6fad6a
  modified: 2026-09-02T20:12:30.405Z
---

Ctrl C is John's strong reference for Garage Band Idle. The design doc was built against it across
extensive conversations and two rewrites, so its shapes are inherited, not invented. Stated
2026-08-24 after I failed to recognize the name and claimed it appeared nowhere in the repo - it
does, and I asserted that without checking.

Where it survives in writing (thin, which is why this memory exists):

- Design doc line 234 cites "Ctrl C's Lines to Knowledge" for the push-past-the-gate payout shape:
  bank at a threshold, keep accruing past it at a lower rate, so the offer condition sets the floor
  and a piecewise formula makes press-now-or-push-on emerge from the curve.
- Commit ca6d068 left one decision open "pending a check against Ctrl C" - whether a timed event's
  timer pauses while unfocused.
- 2026-08-24: untimed events accruing idle earnings is "all but required" in Ctrl C, which settled
  the no-idle-earnings rule as applying to TIMED events only.

- 2026-09-02, from John's own play of it: numbers render in FIXED SLOTS (two decimals below 1000,
  one below 10000, then a two-decimal mantissa plus exponent) so a counting value churns in place;
  a generator row reads "cost => per-unit yield" ("6.99e21 Money => 0.38 Prdty"), the yield being
  ONE unit's contribution, not the owned total; most generators offer a buy mode of "+1" and "+N"
  where N is the most the player can afford, with the cost summed over the rising per-unit series
  (not yet built here - `Purchasing.CostOf` is one unit, the sum is the geometric series from it);
  he knows of no Ctrl C generator paying two currencies, so our bandmates (cash + fans) extend it;
  he does not know what font it uses.

- 2026-09-02, the meta layer, from John's play (step 10's references): every chapter has a TOP
  BAR with three buttons - a chapter selector, a "conversation" button listing the story beats
  seen so far from every chapter, and settings. Settings holds many things; one is "Completion
  tokens" (their Roadies), which opens the allocation menu - so our Roadie allocation lives under
  settings, not on the chapter screen. Story beats appear at some cadence: some just show when
  available, some show the goal to reach first; a watched beat's button stays active for a
  rewatch. Their "Overclock" (our Encore) window: "While active, yields of all generators is
  2.0x. Time remaining HH:MM:SS", with "Boost for 4 hours" (an ad, repeatable, cap unknown) and
  "Boost forever" (buy "Pro Unlock"); note it is worded as a generator YIELD multiplier, while our
  Encore is game_speed - John confirmed switching is a one-field content change. Their idle
  window: the yield, then "Double this" (ad) and "Double All" (buy Pro Unlock). With Pro Unlock:
  Overclock shows "Time remaining" as infinity, and the idle window shows the already-doubled
  amount with a "Great!" button. John does NOT know whether Ctrl C's idle accrual includes the
  Overclock buff; Cells to Singularity's does. He also described Cells to Singularity's stacking
  buff: 2x extends in time per ad up to a cap, then further ads add a shorter 4x - and said that
  if we ever do it, it should be ONE buff reporting 2x or 4x from its remaining time, never two
  buffs relying on the clamp. Deferred, not planned.

- 2026-09-02, two screenshots from John (chapter 2 "Money"/"Assets" and chapter 1 "Lines"): the top
  bar is TWO pills above the content, on every chapter. Left pill: the Overclock widget - a
  stopwatch icon plus the remaining time inline ("5:15:03"), or the infinity symbol when the Pro
  Unlock makes it permanent; tapping it opens the Overclock window with the boost buttons. Right
  pill: three icon buttons - story beat selector (phone icon), chapter selector (building icon),
  settings (gear). The header shows the rate under the total ("Lines: 7.79e17" / "(4.42e13/s)");
  the Money chapter shows no rate line. Generator rows: "Mouse (0+5)" / "100.00 lines => 8.65e9
  lines" / "+1" "+76" - the count is "(bought + granted)": the second number is the count given by
  OTHER generators or buffs, since Ctrl C lets a generator generate count for another generator
  and shows the two separately. John will likely want it, and it is authored, not built: a
  currency the feeding generator pays (never spent, its balance IS the granted count) plus a
  permanent modifier `{target: <fed generator id or tag>, stat: rate, formula:
  LinearOnBalance(<currency>, k)}` - the `records_income` shape with a generator as the target.
  Decided 2026-09-02: if that currency is ALSO spendable, the boost shrinking on spend is the
  intended trade-off, so no earned-total formula is needed. The buy buttons are +1 and +max-affordable. The Money chapter has no rate
  line because nothing produces Money per second: its loop is accrue Asset currencies through the
  generators, then "Liquidate" the section (a prestige, our tier release rung) to convert them to
  Money - our fans-to-records shape. The prestige button: "Liquidate (Gain
  1.19e26 Money)" over "Currently: 2.01e19 Money" with a Confirm - the "would bank" preview. Tap
  producers are three big orange keys: "Ctrl", "C", "V".

**Why:** the doc names it once, as a parenthetical about one formula, so nothing tells a fresh
session that the whole design descends from it. Without that, design questions get answered from
first principles and land somewhere Ctrl C already answered differently.

**How to apply:** when an open design question comes up - pacing, idle structure, event shape,
handicaps, progression - ask how Ctrl C does it before proposing a mechanism, and say so when the
answer comes from there. Never claim knowledge of its specifics; ask John, then record what he says
here. Related: [[design-review-revisions]], [[project-layout-and-workflow]].
