---
name: currency-values-are-bignumber
description: "Every field that participates in currency/production arithmetic is BigNumber, authored fields included; John called defaulting thresholds to double a huge mistake (2026-08-18)"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 85a135f4-95e0-47c5-8f52-584b811e84fb
  modified: 2026-08-28T17:46:17.653Z
---

Design doc §12.14 rule 1: break_infinity (BigDouble, wrapped as `BigNumber`) for ALL currency and
production values. That includes AUTHORED fields — condition thresholds, action amounts, formula
constants and divisors, bar fillAmount/fillRate — not just runtime balances. In
spine step 1 I wrote them as `double` and John called it out: "it's irrelevant if BigNumber
serializes or not, they have to be numbers that we can use. Making those doubles is a huge
mistake."

**Why:** Late-chapter values exceed double's ~1e308 range — a gate of 1e320 cash is unrepresentable
in a double field, and the comparisons happen in BigNumber space anyway. The doc's own §12.7
snippet declares these fields BigDouble; typing them double was drift I introduced, not a decision
anyone made.

**How to apply:** When transcribing doc shapes into code, transcribe the TYPES literally. Any new
field that a balance, rate, yield, threshold, payout, or fill computation touches is `BigNumber`;
the implicit double/int conversions keep authoring and tests ergonomic. See
[[unity-headless-verify-loop]] for the re-verify loop after type changes - and re-run the import,
since serialized assets hold stale field data after a schema change.

Amended 2026-08-28 (step 8 slice A review), John's rule: **"I should be able to author any number in
any field that the game could eventually calculate on its own."** That retired the
`Effect.multiplier` exception and took the RATIOS with it - `multiplier`, generator `growth`,
`LinearOnBalance.coefficient`, both `perRoadie`s are `BigNumber` now, because the gather's product
and every formula factor already were. The test for an exception is no longer "is it a currency
value" but "can the runtime compute past a double here". Only two survive: counts (`int` in state,
so the game cannot either) and `BigDouble.Pow`'s power (a `double` by the library's signature);
wall clocks are seconds and unrelated. The JSON boundary is part of this: a plain number token
carries up to ~1.8e308, so anything past it is authored QUOTED (`"1e400"`) and split into mantissa
and exponent - a `double` DTO field silently capped the authorable range, which is the same defect
one layer out.

Amended 2026-08-19 (step 4): the wrapper now REFUSES NaN and infinity in its
constructors, so the invariant is the type's, not a caller's - never add a
finiteness check at a consumer. Design doc 12.14 requirement 1 states it. The
corollary that bit twice: a mixed expression computing in raw `double` before
converting can overflow on the way in, so convert first.
