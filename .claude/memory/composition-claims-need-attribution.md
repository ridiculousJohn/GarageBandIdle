---
name: composition-claims-need-attribution
description: "Since 7.4 made modifiers multipliers-only, 'applied once over the sum' is not checkable by a total - only by what each line reads back"
metadata: 
  node_type: memory
  type: project
  originSessionId: 393cd63b-24b4-4eb6-bad0-62e5972e7ae8
  modified: 2026-08-14T19:49:38.709Z
---

Slice 7.4 is committed (commit C `cf1d120`, marked done `05e3254`, 2026-08-14). The build prompts' progress marker carries the architecture summary - one producer per currency, no target enum, every modifiable number carrying an id, modifiers multipliers-only - so this records only what that summary cannot say.

**A number composed as "sum of contributions x product of matching multipliers" cannot have its application POINT verified by its total.** Multiplication distributes over the sum, so `(2+3) x 2` and `2x2 + 3x2` both reach 10: a currency-level multiplier folded into every line looks identical to one applied once over their sum. `Rate_ComposesTheCurrencyLevelModifiersOnceOverTheSum` asserted only the total and had silently stopped being able to fail - it was written when `ModifierOperation.Add` existed, where the two answers genuinely differed.

**Why:** this bites every number a later slice gives a composition. 7.5 composes a generator's COST, and section 6 / section 9 have bar fill rate and a scope's idle rate and cap waiting behind ids with nothing authored against them yet. Each will want a test named "applied once", and each will pass for free.

**How to apply:**

- **Check attribution, not arithmetic.** The discriminating assertion is that the per-line readout stays UNCOMPOSED while the aggregate is composed: with a x2 on `cash_rate`, `producer.Rate` is 10 and `ValueOf` of the amp's line is still 2. A row has to say what its own generator makes, so folding the currency's buffs into it is the actual defect, and it is the only observable difference.
- **The property that makes this safe is disjoint ids, and a new number must preserve it.** A producer's number is `cash_rate` owned by `cash`; a line is `drummer_cash` owned by `drummer`. No single term reaches both, which is why a currency-wide buff cannot apply per line AND again over the sum. Give a new modifiable number an id a contribution could also answer to and that guarantee is gone - silently, since the total still looks right.
- **Mutation-check a claim of this shape rather than trusting a green run.** Folding the composition into `ValueOf` and dropping it from `Compose` is the exact regression; under it the total assertion still passed and only the per-line one failed. Two headless runs, and it is the only evidence that a test can fail for its stated reason. See [[unity-headless-verify-loop]] for the loop, [[test-the-justification-not-just-the-claim]] for the same discipline applied to reasoning.
- `CurrencyProducerTests` is the producer's contract - rate and yield move independently, a gated line is worth the same nothing to the readout and the payout, the list is rebuilt rather than registered. Put a producer-level claim there rather than growing a chapter test. Follows [[fan-accrual-is-production]].
