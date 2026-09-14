# Rate display and the generator info screen

Plan for the step 10 follow-up, 2026-09-14, from John's review of the chapter screen against the
Ctrl C reference. Sections cited are `garage-band-idle-design.md`; Chapter 1's authored text is
`chapter-01-content.md`. Suite at the start: 754/754.

## The finding

Nothing on the screen shows a rate. The currency header shows each balance and no rate for it,
and no row shows what a generator produces. The Ctrl C reference shows both: the total with the
rate under it ("Lines: 1.54e19" / "(6.38e13/s)"), and on every generator row a line reading the
cost of the next unit and what that one unit adds per second ("100.00 lines -> 10.00 lines" on a
fresh game; multipliers applied, never the generator's total, never the bulk count), with the buy
buttons carrying "+1" and "+N".

What the code does today, checked 2026-09-14:

- `CurrencyHeaderUI` prints the name and the balance. The rate is already in hand: every refresh
  `CurrencyReadout.Snap` takes the tick report's `CurrencySlope` for the currency's home and uses
  it only to interpolate the balance between ticks. It is never printed.
- `GeneratorRowUI` prints Ctrl C's line, cost and the next unit's yield through `Producer.UnitRate`,
  but as the TEXT OF THE BUY BUTTON ("250.00 Cash => 3.00 Cash, 0.02 Fans"), so the row reads as one
  long button and the yield is easy to miss. The button is the wrong place for it (John).
- The generator row also prints the authored `description` beneath the name on every row, all the
  time. That is noise (John): text a player reads once occupies a line per row forever.
- Nothing shows what the owned units of a generator produce now.

## Decisions (John, 2026-09-14)

1. The header gets Ctrl C's rate line: the realized rate per second beside the balance.
2. The generator row takes Ctrl C's layout: cost and yield on their own line under the name, the
   buy button reading "+1". No new read; the text moves.
3. The always-on description line leaves the generator row. A long press on the row opens an
   info screen, Ctrl C's "long click" (Ctrl C offers it on buffs and events, not on generators;
   John wants it on ours): the flavor text, the cost and yield line, the owned count, and the
   generator's current production, the one figure nothing shows today.
4. Upgrade rows, bar rows and section bands keep their description line. Only the generator row
   changes, since only it gains a place for the text to go.

## What each number is

**The header rate** is the tick report's slope for the currency at its home, the number the
readout already snaps: what the last tick deposited net of what it drew, divided by the tick's
real seconds (`TickReport.CurrencySlope`, 12.11). It is the realized figure, so it is exactly the
rate the interpolated balance climbs by between ticks, and it is zero until the first tick runs
and zero for a currency nothing pays. For a pool currency a cover is drawing from, it is the net,
which is the honest number: the bar group's pool readout already extrapolates by it.

**The row line** is unchanged in meaning: `Purchasing.CostOf` and `Producer.UnitRate` at the
declaring scope, the per-unit resolution the tick sums with both stages applied (12.5). A currency
the unit pays nothing is not a line, and a unit paying nothing has no arrow, as today.

**The info screen's production** is owned count times the per-unit rate, per currency, at the
declaring scope. That product is what the owned units produce because nothing in the effect
vocabulary reads the owned count: one unit's term is also what the next unit adds (the
`Producer.UnitRate` comment), so N units produce N times it. It is computed from what the row
already resolves and needs nothing from the tick report, which is keyed by currency and knows no
source. The row line and the production line are per game second at the declaring scope; the
header slope is per real second and carries game speed, because it is measured from what landed.
Both are correct for what they describe and neither is converted into the other.

## The changes

### Header: `CurrencyLine.uxml`, `CurrencyReadout`, `CurrencyHeaderUI`

- `CurrencyLine.uxml` gains a third label, `rate`, class `currency-rate`, after `value`. The
  line stays a row: name, balance, then "(X/s)". Placement and weight are the polish pass's; this
  slice makes the number exist on screen.
- `CurrencyReadout` exposes the slope it snapped as a read-only `Slope` property. The readout
  keeps its two-label-free shape, since the bar group's pool readouts share it and print no rate.
- `CurrencyHeaderUI.Refresh` prints `"(" + NumberFormatter.Format(readout.Slope) + "/s)"` into
  `rate` after the snap. Every header shows the line; a currency nothing pays reads "(0.00/s)".
- `Screen.uss`: one `.currency-rate` rule (the small, light voice `.row-description` uses).

### Generator row: `GeneratorRow.uxml`, `GeneratorRowUI`

- `GeneratorRow.uxml`: the `description` label becomes `yield`, class `row-yield`, still the
  second line of `row-text`. The `buy` button stays.
- `GeneratorRowUI.Refresh`: `yield.text` is the line the button carried, cost then " => " then the
  per-unit yields; `buy.text` is "+1". The yield line is always shown, since a generator always
  has a cost. `UnitRateText` becomes a static `CostAndYieldText(ctx, generator)` so the info
  screen prints the identical line. The comment on the class says where the description went.
- The long press. UI Toolkit has no long-press event, so the row installs a small
  `PointerManipulator` on its `row-text` element: `PointerDownEvent` starts a scheduled callback
  through the element's `schedule` at the hold threshold; `PointerUpEvent`, `PointerLeaveEvent`
  and `PointerCancelEvent` before it fires cancel it; the fire calls the row's public `OpenInfo()`.
  The target is the text, NOT the button: the "+1" button is an ordinary click and never a hold,
  so no click has to be suppressed after a fire and the two gestures never share an element.
  The threshold is `GameConfig.longPressSeconds`, default 0.5 (John, 2026-09-14: the duration is
  fine, and it is configurable), refused by `Require` unless finite and positive like
  `tickIntervalSeconds`; `GameSession` exposes its config as `Config` so a widget reads the knob
  off the session it already holds, and the row reads it once at bind. The scheduler is panel
  time, which is presentation and not a game read; EditMode tests have no panel, so they exercise
  `OpenInfo` directly, exactly as the Encore window's `RequestAd` and the select's `Select` are
  public for.
- `OpenInfo()` calls `infos.OpenGeneratorInfo(generator, home)`: `home` is the declaring scope
  the row resolved once at bind, so the screen reads the same scope the row reads.

### The opener seam: `IGeneratorInfoOpener`, `ModuleWidgetFactory`

- `IGeneratorInfoOpener { void OpenGeneratorInfo(GeneratorDefinition generator, ScopeState scope); }`
  in `UI/`, the one method a generator row asks of the host, the shape `IStoryOpener` has: the host
  owns the overlay and the row never sees it.
- `ModuleWidgetFactory.Create` takes the opener as a fourth parameter beside `IStoryOpener`, the
  `generator_row` creator passes it, every other creator ignores it. The host passes `this` for
  both, as it does for stories today.

### The info screen: `GeneratorInfoUI`, `Screen.uxml`, `ScreenHost`

- `Screen.uxml` gains `generator-info` (`overlay modal`, hidden) with a `modal-panel`: labels
  `info-name` (`modal-title`), `info-description`, `info-cost` (the row line), `info-owned`,
  `info-production`, and `info-close` ("Back", `overlay-close`).
- `GeneratorInfoUI` is a plain C# class over that element, host-owned like `EncoreWindowUI`:
  `Show(generator, scope, session, clock)` holds the pair and refreshes; `Refresh()` prints the
  name, the description (hidden when empty, as the row hid it), `CostAndYieldText`, "Owned: N" and
  "Producing: X Cash/s, Y Fans/s" from count times `UnitRate` (zero-amount currencies dropped as the
  row drops them; a generator producing nothing reads "Producing: nothing"); `Hide()`. No command,
  no state of its own beyond the held pair.
- `ScreenHost`: `LiveOverlay` gains `GeneratorInfo`; the host holds the requested generator and
  its scope beside the enum (a `LiveOverlay` value cannot carry them, and the story card already
  holds its beat the same way); `OpenGeneratorInfo` implements the interface through `OpenOverlay`;
  `HideRequestedOverlay` hides it; `ShowRequestedOverlay` calls `Show`, so every refresh while it
  stands re-reads the count and the production (a purchase beneath it, a tick, a Roadie change).
  `ClearLiveRequests` and `CloseOverlay` drop the pair with the request. `OpenOverlay` already
  refuses outside `Live` and already replaces the standing overlay and the card, so the info
  screen is replaced by the next requested overlay, replaces an open card, and defers the
  marked-beat walk while up (12.11's modal rule), with no new rule.
- `Interpolate` does nothing for it. Production is a per-tick number; it moves at the refresh.

### Docs

- Design 12.11: the description sentence says the generator row shows its description on the
  info screen and the other rows and bands beneath the name or title; the header shows the tick
  report's realized rate beside the balance; the generator row is name, count, the cost and yield
  line, and a "+1" button, with a long press opening the info screen; the modal list gains the
  generator info screen. 12.13's UI list gains `IGeneratorInfoOpener.cs` and `GeneratorInfoUI.cs`.
- Content doc section 12's description paragraph: a generator's description is read on its info
  screen; the rest beneath the name or title. The twenty texts stay authored and unchanged.
- The build plan's step 10 row gains the landing line; this file's status line records it.
- Chrome literals added: "+1", "/s", "Owned: ", "Producing: ", "Producing: nothing", "Back".

### Tests (`ScreenHostTests` unless named)

- Header: after `Enter` and before any tick, every header's `rate` reads "(0.00/s)"; after one
  tick with an amp owned, Cash's reads the report's slope formatted, and equals what
  `Session.LastTick.CurrencySlope(home, "cash")` formats to; Fans', behind its reveal, reads zero.
- Row: the two existing button assertions ("60.00 Cash => 0.50 Cash", "250.00 Cash => 3.00 Cash")
  move to the `yield` label, and `buy.text` is "+1" on both rows.
- Row: `RowsAndSectionsShowTheirAuthoredDescriptions` loses its amp half (the row has no
  description label); the section half stays, and an upgrade row's description is asserted in its
  place so the "other rows keep theirs" decision is a tested fact.
- Info: `OpenInfo` on the amp's row shows `generator-info` with the amp's name, its authored
  description, the same cost and yield line the row shows, "Owned: 0" and "Producing: nothing";
  after a `TryBuy` beneath it the same pass reads "Owned: 1" and "Producing: 0.50 Cash/s".
- Info: the screen stands across a tick's refresh (the widget-detachment shape slice E fixed:
  the labels are the document's, so nothing is rebuilt); `OpenEncore` replaces it; opening it
  replaces an open card; a phase leaving `Live` drops it.
- Info: the sixth modal-deferral case, a marked beat becoming available while the info screen is
  up waits, and pops on the first refresh after Back.
- Factory: the registry cross-check row passes the two openers.
- `Chapter1ContentTests.Every_row_and_section_chapter_one_shows_carries_a_description` is
  unchanged: the texts are still authored and still rendered, on the info screen.

## Out of scope

- Bulk buy and the "+N" button: the after-the-plan item. The row's "+1" is the single-unit buy.
- Ctrl C's "(bought + granted)" count: no content grants count today, so the row keeps "xN".
- Placement, weight and color of the new labels: the polish pass. This slice puts the numbers on
  screen in the existing voice.
- An info screen for upgrades, covers or events: the same seam would serve them; not asked for.

## Status

DONE 2026-09-14 - 761/761 green (+7: 6 tests and one deferral row added, 0 deleted). Landed as
planned, with `GameConfig.longPressSeconds` the hold threshold. Two fixture facts recorded from the
tests agent: `Producer.DeclaringScope` is internal, so the tests key the header's slope by the
authored home (`cash` is declared on tier1) and take the unit rate from the public `UnitRate`,
which rebases itself; and the Fans header's zero rate is not asserted, since the Fans line is
hidden on a fresh chapter and its widget does not exist to read. Review found no defects. The
row's second constructor parameter is exercised only through the factory, as every row is.

**Correction the same day, 762/762 (+1).** The rate line surfaced a tick defect the balance had
hidden: `TickSystem.Tick` built the tick's start with `DateTime.AddSeconds`, which Mono rounds to
a whole millisecond, so the window the segments simulated differed from the report's `Seconds` by
up to half a millisecond and every slope was the rate times a ratio that wandered tick to tick
(0.2% on a quarter-second tick, 3% on a one-frame flush before a command, which is why a purchase
dipped the rate for one tick). The start is now built with `AddTicks` at the clock's 100ns
precision. `TickReportTests` gains a fractional-millisecond tick asserting the slope is the rate;
that row fails on the old line and nothing else does.
