---
name: currency-values-are-bignumber
description: "Every field that participates in currency/production arithmetic is BigNumber, authored fields included; John called defaulting thresholds to double a huge mistake (2026-08-18)"
metadata:
  node_type: memory
  type: feedback
---

Design doc §12.14 rule 1: break_infinity (BigDouble, wrapped as `BigNumber`) for ALL currency and
production values. That includes AUTHORED fields — condition thresholds, action amounts, formula
constants and divisors, bar fillAmount/fillRate, group pipeRate — not just runtime balances. In
spine step 1 I wrote them as `double` and John called it out: "it's irrelevant if BigNumber
serializes or not, they have to be numbers that we can use. Making those doubles is a huge
mistake."

**Why:** Late-chapter values exceed double's ~1e308 range — a gate of 1e320 cash is unrepresentable
in a double field, and the comparisons happen in BigNumber space anyway. The doc's own §12.7
snippet declares these fields BigDouble; typing them double was drift I introduced, not a decision
anyone made.

**How to apply:** When transcribing doc shapes into code, transcribe the TYPES literally. Any new
field that a balance, rate, yield, threshold, payout, or fill computation touches is `BigNumber`;
the implicit double/int conversions keep authoring and tests ergonomic. The knowable exceptions:
`Effect.multiplier` (double, per the doc — a factor, not a currency value), counts
(`int` — generator/bar counts), and `BigDouble.Pow`'s power parameter (double by the library's
signature). See [[unity-headless-verify-loop]] for the re-verify loop after type changes — and
re-run the import, since serialized assets hold stale field data after a schema change.

Amended 2026-08-19 (step 4): the wrapper now REFUSES NaN and infinity in its
constructors, so the invariant is the type's, not a caller's - never add a
finiteness check at a consumer. Design doc 12.14 requirement 1 states it. The
corollary that bit twice: a mixed expression computing in raw `double` before
converting can overflow on the way in, so convert first.
