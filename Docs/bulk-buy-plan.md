# Bulk buy

Plan for the first after-the-plan item, 2026-09-15, from the conceptual pass with John. Sections
cited are `garage-band-idle-design.md`; the reference game is Ctrl C (its generator rows carry
"+1" and "+N", N the largest count the balance affords). Suite at the start: 788/788.

## The finding

A generator is bought one unit at a time. `Purchasing.TryBuy(ctx, generator)` spends
`CostOf(generator, ctx)`, which is `GeneratorDefinition.CostAt(owned)`, and writes `owned + 1`.
The row has one button, "+1". Late in a chapter a player who can afford eighty units taps eighty
times, and every tap is a command with its own flush, sweep and refresh.

## What it is

A purchase is a count. The runtime answers two questions and performs one command, all through
one cost function, so the number on the screen and the number the bank pays are the same number
by construction rather than by agreement between two computations.

**The cost of n.** Buying `n` units at owned count `o` costs the geometric series
`baseCost * growth^o * (growth^n - 1) / (growth - 1)`. When growth is exactly 1 the series is
`n * baseCost * growth^o`; validation refuses only a nonpositive growth, so 1 is authorable and the
branch is required, not defensive. `Purchasing.CostOf(generator, ctx, n)` is the one function, and
today's single cost is its n = 1 case. `GeneratorDefinition.CostAt(owned)` stays as the unit-cost
term the series multiplies onto.

**The largest affordable count.** `Purchasing.MaxAffordable(ctx, generator)` returns M: zero when
the gate is closed or the balance does not cover one unit, otherwise the largest n with
`CanSpend(CostOf(n))` true at the declaring scope. It is found by search over the one function:
double n from 1 until the cost exceeds the balance (the bracket capped so `owned + n` fits the
count field, an `int`), then bisect. About sixty cost evaluations at the worst, each one closed
form, and no logarithm: a log estimate would need a fix-up step against the closed form's last
digit, and the search has none because every probe IS the affordability check.

**The command.** `CanBuy(ctx, generator, count)`, `Buy(ctx, generator, count)` and
`TryBuy(ctx, generator, count)` take the count, required, no default. A buy is one `Spend` of the
series sum and one write of `owned + count`. Never a loop of unit buys: nothing in the effect
vocabulary observes a unit landing, counts scale on read (`Producer.UnitRate` is one unit's term
and n units produce n times it), so n at once is the same state as n in sequence, and the trigger
sweep runs once at the transaction's close as 12.9 wants. `OwnedCountAtLeast(10)` latches once
whether the player crossed it by "+1" or "+76". A count below one throws
`InvalidOperationException` as a caller bug, the ruling `Buy` already gives a false `Can`.

## Decisions (John, 2026-09-15)

1. The button buys the count printed on it, or refuses whole and repaints. The tap captures the
   M it displayed and submits `TryBuy(ctx, generator, M)`. The command flushes banked frames
   first, so the balance it sees is up to `tickIntervalSeconds` newer than the label's: usually a
   little more (production), rarely less (a bar's draw in that tick). If the count is still
   affordable it is bought, not the new larger maximum; if it is not, nothing is written and the
   close's refresh prints the new number. Never a count the player did not see.
2. Both buttons read "+1" when exactly one unit is affordable, and do the same thing. No hiding
   rule for a state that lasts seconds.
3. The count parameter is required. Every call site names its count; the existing sites pass 1.

## Properties the shape guarantees

Stated here so they are tested facts and not caveats.

- **n = 1 is today's cost, bit for bit.** The series factor `(growth^n - 1) / (growth - 1)` is
  computed as its own quotient and then multiplied onto `CostAt(owned)`. At n = 1 the quotient is
  `x / x` for a finite nonzero x, which IEEE evaluates to exactly 1. Computed in the other order
  (`CostAt(owned) * (growth^n - 1)` then the division) the last bit can move, so the order is part
  of the contract and a test asserts equality with `==`, not a tolerance.
- **The power is full double precision.** `BigNumber.Pow` takes a double power, but for an integer
  power BreakInfinity's fast track runs `Math.Pow(mantissa, power)` directly; the library's
  nine-to-eleven-digit caveat is its non-integer path, which no count reaches. The one loss in the
  series is the subtraction `growth^n - 1` when growth is near 1: about `log10(1 / (growth^n - 1))`
  digits of sixteen, under one digit for the authored 1.15 and three for a growth of 1.001.
- **M is never a count the search did not evaluate.** The bisect keeps its lower bound as a count
  whose affordability it checked directly through `CostOf`. A non-monotone last bit in the power
  can therefore hide a larger affordable count, never return an unaffordable one, so "+M" cannot
  print a count the command then refuses on its own arithmetic.
- **One rounding against n.** The balance after one buy of n and after n unit buys differ in the
  last few digits, since the series rounds once and the loop n times. No read compares those two
  histories, and the formatter prints two decimals of a mantissa. The residual is bounded by the
  digit loss above and sits below anything on screen.

## The changes

### `Purchasing`

- `CostOf(GeneratorDefinition generator, GameContext declaringCtx, int count)`: reads `owned`
  from the declaring scope as today, computes `CostAt(owned)`, then the factor as its own
  quotient (`count` when growth is 1, since `BigNumber` compares exactly), then the product. The
  nonpositive backstop stays and reads the unit cost, as today; a count below one throws. The
  two-argument form is deleted.
- `MaxAffordable(GameContext ctx, GeneratorDefinition generator)`: rebases to the declaring
  scope; zero when `IsAvailable` is false or `CanSpend(CostOf(1))` is false; otherwise the
  doubling-then-bisect search described above, with `hi` capped at `int.MaxValue - owned`.
- `CanBuy(ctx, generator, count)`: gate and `CanSpend(CostOf(count))`. `Buy(ctx, generator,
  count)`: the same guard throwing, one `Spend`, one count write. `TryBuy(ctx, generator, count)`
  is the wrapper. The count-free generator overloads are deleted; the upgrade overloads are
  untouched.

### `GameSession`

- `TryBuy(GameContext ctx, GeneratorDefinition generator, int count, Action<bool> completed = null)`
  on `RunCommand` as today. The upgrade wrapper is untouched.

### The row: `GeneratorRow.uxml`, `GeneratorRowUI`

- `GeneratorRow.uxml` gains a second button, `buy_max`, class `row-buy`, after `buy`.
- `OnBound`: `buy` submits `TryBuy(ctx, generator, 1)`; `buy_max` submits
  `TryBuy(ctx, generator, shownMax)`, the count the last `Refresh` printed.
- `Refresh`: one read, `var max = Purchasing.MaxAffordable(ctx, generator)`; `buy.text` "+1",
  `buy_max.text` "+" + `Math.Max(max, 1)`, both enabled when `max >= 1`. At zero the max button
  reads "+1" disabled (John, 2026-09-15). `shownMax` is the printed count kept as an int beside
  the label it was printed into (John, 2026-09-15), since the click needs the number as an int
  and parsing the label back would be a second source.
- `CostAndYieldText` passes 1; the line stays the next unit's cost, as the reference game's does.
- The info screen prints the same line and is otherwise untouched.

### Docs

- Design 12.2 (the generator paragraph), 12.11 (the row and the command set), 12.13 (the
  `Purchasing.cs` line): landed with this plan, 2026-09-15.
- The build plan's after-the-plan bullet points here; on landing the bullet is replaced by a row
  above with the landing line, and this file's status records it.
- Chrome literals added: "+" + count for the max button ("+1" through "+M").

### Tests

`PurchasingTests` unless named. Every existing generator call site passes 1.

- `CostOf(1)` equals `CostAt(owned)` with `==` at owned 0, 1 and 25 on the amp (60, 1.15).
- `CostOf(n)` equals the sum of n successive `CostAt` terms within a relative 1e-12 for n in
  2, 10 and 76, at owned 0 and 25.
- A growth-1 generator: `CostOf(n)` equals `n * baseCost` exactly.
- A count of 0 and of -1 throw from `CostOf`, `CanBuy`, `Buy` and `TryBuy`.
- `MaxAffordable`: zero with the gate closed; zero one unit short; k when the balance is exactly
  `CostOf(k)`; k - 1 when it is one short of that; for every answer M, `CanBuy(M)` is true and
  `CanBuy(M + 1)` is false. A growth-1 generator with base 1 and a balance of 1e9 answers
  1,000,000,000, so the search is exercised over a count the loop shape could not reach.
- `Buy(n)`: the count is `owned + n`, the balance fell by exactly `CostOf(n)`, and
  `generatorCounts` has one entry.
- `TryBuy(n)` one unit short refuses whole: false, count and balance unchanged.
- `GameSessionTests`: a session `TryBuy` of 5 runs as one transaction, `Refreshed` once,
  `completed(true)` once, and a trigger on `OwnedCountAtLeast(3)` whose action deposits has
  deposited once.
- `ScreenHostTests`: on a fresh chapter `buy_max` reads "+" + `MaxAffordable` for the amp and is
  enabled with `buy`; after clicking it the count reads that M, both buttons are disabled and
  both read "+1"; with the balance set to exactly one unit's cost both read "+1" enabled.
- The `Chapter1WalkthroughTests` are unchanged in meaning; their buys pass 1.

## Out of scope

- A "+10" or a slider: any count up to M is buyable through the same call, so those are buttons.
- Refreshing the row on every tick rather than at transaction closes, which would shrink the
  quarter-second gap decision 1 handles: presentation, the polish pass's.
- Ctrl C's "(bought + granted)" count: no content grants count today.
- Placement and weight of the second button: the polish pass.

## Status

Planned 2026-09-15. Not started.
