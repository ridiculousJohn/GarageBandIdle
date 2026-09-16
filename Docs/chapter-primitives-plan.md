# Chapter primitives

Plan for the runtime support chapters 2 through 8 will need, 2026-09-15, from a survey of the Ctrl C
reconstruction (`Ctrl-C-Chapter-1-8.zip`: the chapter reference, the data appendix, the story
transcript) against the primitives in `Core` and `Economy`. Sections cited are
`garage-band-idle-design.md`. Suite at the start: 799/799, at commit c3930dc.

The survey's finding, in the reference's own words: Ctrl C is eight different incremental systems
connected by achievement tokens. Chapter 2 there throws out chapter 1's continuous generators and
is built on timed teams, a balance multiplier layer, one prestige and seven timed challenges. That
is the shape John wants for our chapter 2 (sentence 1), and it is the shape the primitives below
make authorable. Nothing here is a chapter design; every item is a mechanism a chapter can be
written on.

## What John said, verbatim

Every line under "The changes" traces to one of these, and a line that does not is the defect.

1. "Ok, all of the 'general' stuff will likely change. if not that will make chapter 2 too much
   like chapter 1. Chapter 1 has a good rhythm."
2. "so we need a way to expand our 'modifier' system to be able to modify cost?"
3. "so the payout on a bar needs to be able to be modified both by a modifier, and by a purchase
   count. that seems simple enough."
4. "Ctrl C also has generator generators, so that the thing saying 'add N to this generator' could
   be another generator, who's output is N."
5. "The secondary generator would somehow say "i modify the primary generator" and name it by name
   there, yes?"
6. "what if we want a generator that, instead of adding, multiplies its parent by our value? could
   the composition formula be data driven?"
7. "yes but that 'repeating' would need to be able to be changed at runtime by a modifier."
8. "don't we though? our 'covers' bars have to be activated to fill start filling."
9. "subtractive yield is self-explanatory, we'd need something if we ever wanted a debt like thing.
   that could be useful on a chapter that is something like a 'multi-state tour', to represent the
   condition of your van or something"
10. "explain 'two rungs on one scope' simply, seems like that could be accomplished with creative
    sibling or child scoping"
11. "Oh; well that's just a bug in what we are doing, IMO. things should almost always default to
    being 'how many earned since the start of this round/prestige', with maybe a way to specify
    the overall total there instead (so we need a way to specify which value to use, overall or
    earned this round)"
12. "tap to add bar time is self-explanatory and just accelerates the bar." and "but yo ucan't tap a
    bar right now"
13. "write up this feature list as a plan doc"

"Modifier" in 2 and 7 means the thing that changes a number or a behavior at runtime from state,
which here is an Effect for a number (12.2) and a Condition for a behavior (12.4). The plan uses
whichever of the two the existing rule for that site already uses.

## What Ctrl C does that is already content here

Recorded so no one builds them again.

- **Layered prestige** (Rewrite inside Project): nested tiers, each rung a `ResetScope` and a
  formula-paid `AddCurrency`, the award declared one scope out so it survives (12.3's placement
  rule).
- **Persistent prestige currencies that multiply production** by a percent per point: a permanent
  chapter modifier carrying `LinearOnBalance`.
- **Challenges** with a restriction, a target, a time limit, a permanent reward, and staged
  families gated on the previous record: `EventDefinition`, `EventRecordExists`. A restriction that
  is a multiplier works today, including x0 on a bar's fill rate (Management Styles).
- **Timed bars** with a cycle, an input consumed per cycle, or time-only fill: `BarDefinition` with
  a fill currency or a null one.
- **Byproducts** (Tech Debt accruing alongside Power): two produces entries on one generator.
- **Exclusive choices** and their later removal: `Not` over `UpgradePurchased`.
- **An opposing counter** forcing a restart (Bicho): a rate producer paying a chapter currency and
  a trigger firing `ResetScope` at the threshold.
- **Two prestige buttons over one production layer** (Rebase and Conference), sentence 10: the
  layer in a tier, wrapped in a second tier that declares nothing of its own. Each rung resets its
  own subtree; the wrapper's subtree is the inner tier, so both clear the same content and pay
  different chapter currencies. Chapter 3's three division prestiges are three sibling tiers the
  same way, with one real gap: the shared Progress they all clear cannot be a single currency
  above them, since a subtree reset never clears a fact declared outside it. Three division
  currencies, or a later mechanism, when chapter 3's analogue is authored.
- **Auto-prestige** (Continuous Integration): a trigger declared one scope above the tier, gated on
  the automation upgrade and the rung's threshold, whose action is `ExecuteRung` on the tier. The
  reset re-arms the trigger, so it fires once per run. `ExecuteRung`'s reach is a rung within the
  acting scope, which is why the trigger sits on the parent.
- **Tokens, Overclock, offline earnings, boundary story**: Records, Encore, the idle claim, the
  story beats.

## Decisions (John, 2026-09-15)

1. **Cost is a multiplier target** (sentence 2). The modifier system reaches a price the way it
   reaches a rate: an Effect names it.
2. **A bar's payout scales by a purchase count and by multipliers** (sentence 3), through the
   number that already does both: a generator's yield entry, fired by the bar.
3. **A generator may pay another generator's owned count** (sentences 4, 5). The paying generator
   names the target in its produces entry, exactly as it names a currency. Nothing on the target
   names its sources; the compiler builds the source list at the target's home at load, as it does
   for a currency.
4. **Add and multiply are the two shapes** (sentence 6). A produces entry adds, an Effect
   multiplies, and the factor inside an Effect is a formula kind. A generator that multiplies
   another by its count is a permanent modifier with an owned-count formula. No expression
   language.
5. **A bar's repeat is a condition** (sentence 7), and the player's activation is the tap
   (sentence 8). A bar whose repeat condition is false pays, returns to zero and drops out of the
   active set; the player activates it again. No new command.
6. **A payout formula reads what was earned this round by default** (sentence 11), with a selector
   for the balance. The balance read today is the defect.
7. **Subtractive yield waits for the chapter that needs it** (sentence 9), the Van chapter (design
   2's row 5).
8. **The owned count splits into purchased and granted** (sentences 4, 5; John: "split the count
   into current and granted"). Granted copies never raise the price: `CostAt` reads purchased,
   production and `OwnedCountAtLeast` read the sum, and the owned-count formula names which it
   reads. Without the split a generator-generator prices its own target out of reach.
9. **A granted count is a BigNumber and keeps its fraction** (John: the numbers get huge, and a huge
   number times another huge number makes the fractional part matter). A granted count is the sum
   of yields that are count times value times gathered multipliers, which is what the BigNumber
   rule covers; the count exemption covers only a purchased count, bounded by player buys.
   Purchased stays `int`, production and the count conditions read purchased plus granted as
   BigNumber, the price reads purchased. No flooring anywhere.
10. **All three totals exist and the payout selector names one** (sentence 11; John: "obviously we
    need all three totals"): the balance, earned this round, and the lifetime total across rounds.
    Earned this round is today's `earnedTotals`, zeroed by the reset that clears the home. The
    lifetime total is a new stored fact that the reset does not clear, kept at the currency's home
    beside the other two, so every read addresses it through the same outward walk. Where at the
    home it sits, given that a reset swaps the whole `ScopeFacts` payload, is section G's question.
11. **Debt is an Effect with a subtractive term, not a system** (sentence 9; John: we already have a
    modifier system and are expanding it to virtually every number, so debt is not another one).
    It is addressed like every multiplier: naming a generator subtracts from that generator's own
    output before the factors, naming a currency subtracts from the total. Both placements, the
    author's choice, one atom. Built when the Van chapter needs it (decision 7).
12. **Autobuy is an effect the granting upgrade carries** (John: a generator should not know
    everything in the system that can make it auto-buy). The upgrade names its targets by generator
    id or tag, as its other effects do; the generator declares nothing. It is a yes or no, not a
    number: the effect existing on a generator's coordinate means buy, and an event handicap on the
    same coordinate means do not while the record exists, which is the design doc's own "automation
    disabled" handicap example.

## Decisions (John, 2026-09-16)

13. A constant x0 on a cost effect is refused at load; the runtime backstop on a nonpositive unit
    cost stays where it is, covering the product.
14. A bar collects what pays it, as a currency does: a rate at its draw beside its own `fillRate`,
    a yield at the write, settling its own crossing there, selected or not (selection governs
    drinking from a pool; a payment is not a drink).
15. Stage 2 belongs to the target under the target's own stat: `rate`/`yield` for a currency,
    `rate`/`yield` for a bar (its fill-rate plan doubles as its rate stage), `count` for a
    generator's granted count - so "querying, rate" keeps meaning Querying's output.
16. The generator row prints the purchased count in parentheses with the granted count added inside
    when there is one: `(3)`, `(3+8.89e11)`; the "x" is gone (Ctrl C's shape; an absent granted
    prints nothing).
17. Idle pays every rate target the tick would, one offer line per target (Ctrl C's idle window
    lists granted counts as lines).
18. Cost is stage 1 only: an effect names the generator or upgrade by id or tag; "everything priced
    in cash" is a tag. Names whose type widens from currency to any target follow the type
    (`TargetContributors`, `StatPlans`, the offer line's `target`); `OwnedCountAtLeast`'s threshold
    is BigNumber.

19. Away pays half of ALL idle income, a grant into a generator's count included (John,
    2026-09-16: "it's about all of it"). The wildcard is every number of a stat at the one coordinate
    that stat has per number, so `count` and `cost` take a wildcard as a currency's total does;
    root's `idle_base` carries `{stat: count, x0.5}` beside its rate line. The tick records a granted
    deposit like a currency's, so the row's count interpolates between ticks.

20. (2026-09-16, D-H) The tap producer is authored on the bar (`tap`), since a row is per bar and
    the one bar-group module renders every bar of its scope; the select button stays the
    activation, the tap pays.
21. (2026-09-16, D-H) The `repeating` bool is gone from the JSON too: `repeatWhen: {type: Always}`
    is how a repeating bar is authored, one spelling and no conflict rule.
22. (2026-09-16, D-H) Autobuy runs once at the tick's end, after every segment, so the bars have
    drunk before anything is spent; then the sweep.

## The changes

Ordered by what a Ctrl C-shaped chapter 2 hits first. A, B and C are its first day of authoring.

### A. Cost as a stat (decision 1; 12.2, 12.5)

- `Stat.Cost = "cost"` joins `game_speed`'s category: an effect address no produces entry may
  name. `IsEffectAddress` answers true for it; `IsProduced` stays rate and yield, so
  `RequireProducedStat` already refuses a produces entry naming it.
- `Purchasing.CostOf(generator, ctx, n)` asks the gather for the factor at the coordinate
  (generator id or tag, cost currency, cost) at the declaring scope and multiplies it onto the unit
  cost before the series. The factor is constant across one purchase, the same assumption the Ctrl
  C reference states for its bulk formula, so `MaxAffordable` is unchanged: it only evaluates
  `CostOf`. The upgrade cost takes the factor at (upgrade id or tag, cost currency, cost).
- The gather compiler compiles a cost plan per generator and per upgrade at its declaring scope,
  the same shape as a source's rate plan.
- Range: a cost factor below zero is a content fault at load (`NumericRange`), and so is a constant
  x0 (decision 13): a free generator is an unbounded rate printer, `CostOf` already throws on a
  nonpositive unit cost, and Ctrl C's "99.99% cheaper" is a small factor, never zero. A wildcard
  cost effect halves every price (decision 19).
- Outside: a change to the growth ratio itself (Literally Unusable) is a different feature, and a
  per-count discount (Ergonomics) is this plus D.

### B. Fire a generator's yield (decision 2; 12.5, 12.7)

- `FireGeneratorYield : GameAction { GeneratorDefinition generator }`. Resolves the generator
  outward from the acting scope (`ChainReach`), runs `Producer.Resolve` over the generator's
  produces entries with `Stat.Yield`, count-scaled and gathered exactly as a rate is, and deposits
  through `FireProducer`'s path. Any action list may hold it; on a bar's `onComplete` it is a team
  paying per cycle, on an upgrade or a rung it is a one-shot grant that scales.
- The bar does not change. Its `onComplete` and `perFill` stay as they are.
- Validation: a yield entry on a generator that no reachable `FireGeneratorYield` names is an
  `InertOperand` warning, since nothing fires it. A generator whose entries are all yield and that
  nothing fires is dead content the same way.

### C. Payment into a count or a bar (decision 3, sentence 12; 12.2, 12.14.8)

- `ProducesEntry` names one target: a currency (today), a generator, or a bar. Exactly one is set;
  two or none is a `NullEntry` fault. The stat rules are unchanged: rate accrues in the tick, yield
  pays on a firing.
- A payment into a generator is a deposit into its `granted` count (decisions 8, 9) at the
  generator's declaring scope, found by the outward walk from the paying source. A payment into a
  bar goes to the bar (decision 14): a rate is collected by the bar at its draw beside its own
  `fillRate`, and a yield moves progress at the write and settles the bar's crossing right there
  through the tick's own settlement, clamped at `fillAmount` for a non-repeating bar, selected or
  not.
- The compiler builds a plan at the target's home listing the sources on the chain that pay it,
  as it does for a currency, so a "how fast is my count growing" readout is the same read a
  currency's rate is.
- Each target collects its own stage-2 modifiers (decision 15): a currency's under `rate`/`yield`,
  a bar's under `rate` (its fill-rate plan, so "10x this bar" speeds its own fill and what is paid
  in alike) and `yield`, a generator's granted count under the stat `count`, so an effect naming a
  generator with `rate` keeps meaning its output.
- Ctrl C's recursive generator is then content: Scoring is a generator whose yield entry pays
  Querying's count, and a repeating time-only bar of 37.4 s fires Scoring's yield on completion
  (B). Three Scoring owned pays three Querying per cycle; "10x generator-generator speed" is a
  multiplier on the bar's fill rate. Direct To Consumer, where the same generators pay Power while
  the challenge runs, is two conditioned entries on Scoring, which entries already support.
- The action "add N to this generator" is the same write with N authored: a generator with a
  single yield entry of N, fired by a trigger or an upgrade. No second action kind.
- `OwnedCountAtLeast`, `UnitRate`, the row's count label and the info screen read the sum;
  `CostAt` reads purchased. Save: `ownedCounts` gains the granted map; missing reads as zero, so no
  migration.
- Validation: the target resolves on the paying source's chain (`ChainReach`). No warning on a rate
  into a currency-fed bar: the bar collects it as fill, which is ordinary authoring (decision 14).

### D. Owned-count multiplier formula (decision 4; 12.2)

- `LinearOnOwnedCount : MultiplierFormula { GeneratorDefinition generator; BigNumber coefficient;
  bool purchasedOnly }`: `1 + coefficient * count`, the count read at the generator's home on the
  origin chain, purchased or the sum by the flag. Markdown, Economies Of Scale, Snowballing
  Productivity and Ergonomics (with A) are permanent chapter modifiers carrying it.
- Validation: the generator resolves on the chain; the coefficient is finite and nonnegative.

### E. A bar's repeat is a condition (decision 5; 12.7)

- `BarDefinition.repeating` (bool) becomes `repeatWhen` (Condition). Absent is today's
  non-repeating bar: fills once, fires on the crossing, stays full for the scope life, which is
  what the covers and `BarsCompleted` want. Present and true is today's repeating bar. Present and
  false is the manual team: on completion it pays, returns to zero and is removed from the active
  set; `SetActiveBars` is how the player runs it again.
- Importer: `repeatWhen` is a condition block and the `repeating` key is gone (decision 21), so a
  document still authoring it is refused as an unknown key. Chapter 1 authors no repeating bar, so
  its assets reimport to the same behavior.
- Validation: `repeatWhen` follows the gate rules for reach; `perFill` on a bar with no `repeatWhen`
  is the error it was on a non-repeating one.

### F. Autobuy (decision 12; 12.2, 12.9)

- `Stat.AutoBuy = "autobuy"` is an effect address like cost and game_speed, and a yes or no: the
  gather's answer for (generator id or tag, autobuy) is whether an effect naming it is live and no
  handicap on it is. An upgrade's effect `{target: line_generators, stat: autobuy}` turns it on for
  every generator carrying the tag; an event handicap on the same coordinate turns it off for the
  record's life. Nothing fires: the tick reads it.
- The tick's one purchase phase, once at the tick's end after every segment (decision 22): for
  every generator in the swept set whose autobuy reads yes, `Purchasing.TryBuy(ctx, generator,
  MaxAffordable(ctx, generator))` when that count is at least one. The same purchase path the
  button uses, so the sweep at the transaction's close sees the buys, and a refused buy is a no-op;
  the bars have drunk before anything is spent. The switch is read off the plan's links' liveness
  (`EffectLink.Live`, `Producer.GetSwitch`): the multiplier on an autobuy effect is not read.
- Ordering within the phase is the sweep's: root, then the foreground subtree in tree order, each
  scope's generators in declaration order, so a chapter that wants Variables bought before
  Functions declares them so. Ctrl C's per-second and five-per-second tiers are not built: every
  tick is the only rate.
- Outside: the payback-time threshold, and Work From Home's fastest-round offline simulation.

### G. Payout selector (decision 6; 12.5)

- `RootCurveFormula` gains `reads`, an enum `{ EarnedThisRound, Balance, Lifetime }`, default
  `EarnedThisRound`. `EarnedThisRound` reads the earned total at the currency's home, the fact
  `EarnedTotalAtLeast` already reads, cleared by the reset that clears the home. Chapter 1's album
  pays the same number, since fans are never spent; the walkthrough tests prove it.
- `Lifetime` reads a per-currency running sum at the currency's home that every deposit adds to
  and no reset clears (decision 10). The reset today swaps the home's `ScopeFacts` payload so new
  fields clear by construction (12.3); the lifetime sum is the first fact that must NOT, so it is
  kept beside `facts` on the scope (`ScopeState.lifetimeTotals`, seeded once at build) that `Clear`
  leaves alone and the save carries as its own block. Missing in an old save reads as the earned
  total at load, the best figure the save holds; the schema version does not move. A condition
  over it, `LifetimeTotalAtLeast`, is one more kind when a gate wants it.

### H. Tapping a bar (sentence 12; 12.11)

- The bar names the producer its row fires (`BarDefinition.tap`, decision 20), resolved outward
  from the bar's scope and validated on its chain. When present the row is a tap target issuing the
  same command the Jam button issues, `FireProducer` on that producer, on a click outside the
  select button. The producer's yield entry pays the bar's progress (C) for a time-fed bar, or the
  fill currency for a currency-fed one, which chapter 1's Jam already does for Rehearsal.
- Nothing on the domain side beyond C.

### I. Debt (decisions 7 and 11)

Not built until the Van chapter. The Effect atom gains a subtractive term beside its multiplier,
so a coordinate resolves as base minus the subtractive terms, clamped at zero, times the product of
factors. Naming a generator subtracts from that generator's output; naming a currency subtracts from
the total. The same walk, the same carriers (upgrade, modifier, handicap), no new site.

### Docs on landing

Design 12.2 (the cost stat, the produces target, the owned-count formula, the purchased and
granted split of decision 8), 12.5 (`FireGeneratorYield`, the payout selector), 12.7 (`repeatWhen`, payments into
progress), 12.9 (the autobuy phase), 12.11 (the bar row's tap), 12.12 (each validation row above),
12.13 (the file list), design 2's chapter table if the Van note lands there. Build plan row and
the after-plan bullet.

### Tests

Each change's tests exercise the runtime code, never a second implementation (memory rule).

- A: a cost factor of 0.01 on a tag reaches every generator carrying it and no other; the n = 1
  identity holds under a factor; `MaxAffordable` under a factor equals the count `CostOf` affords;
  a handicap of 100 lifts when the record goes; a factor below zero is refused at load.
- B: firing a generator's yield at owned 3 deposits three units' worth times the gathered
  multiplier; on a bar's completion the deposit matches `ResolveGeneratorYield`; an unfired yield entry
  warns.
- C: a yield paid into a count lands in granted; `CostAt` ignores granted; `OwnedCountAtLeast`
  and production read the sum; a rate paid into a time-fed bar's progress advances it and clamps
  at the fill; the source list at the target's home matches the declarations; the save round-trips
  granted as BigNumber and reads a missing map as zero; a fractional yield into a count is kept whole
  in the BigNumber, never floored.
- D: `1 + 0.01 * count` at the origin chain, purchased-only and sum.
- E: a bar with no `repeatWhen` stays full after the crossing; `Always` repeats; a false condition
  pays, zeros and leaves the active set, and reactivation runs it again.
- F: a generator whose tag an autobuy effect names is bought to max in the tick, one with no such
  effect is not, a handicap on the coordinate stops it while the record exists, the sweep sees the
  latch once, a closed gate buys nothing, and two generators buy in declaration order against one
  balance.
- G: a payout over a spent currency reads earned, not balance; chapter 1's album is unchanged; the
  lifetime sum survives a tier reset and a chapter reset, and an old save reads it as the earned
  total.
- H: the row's tap issues `FireProducer` on the named producer and nothing else.

## Out of scope

- Growth-ratio changes (Literally Unusable). A different feature.
- Payback-time autobuy, per-second autobuy tiers, fastest-round offline simulation.
- Debt's implementation (decisions 7 and 11); the shape is fixed, the chapter is not.
- A cross-subtree reset for chapter 3's shared Progress.
- The softcap gain formula. One more `PayoutFormula` kind when a chapter wants it.
- Any chapter 2 content. This plan makes it authorable; the chapter is its own document.

## Status

**A, B, C DONE 2026-09-16** - 844/844 green (+45: 47 added, 2 deleted), with decisions 13-18 landed the
same day. Two review corrections on the runtime agent's diff: a payment into a non-repeating bar
already past full takes nothing rather than being clamped down to `fillAmount` (progress is
monotonic, 12.7), and `ResolveGeneratorYield` reads the owned sum through `GameContext.GetOwnedCount`
instead of adding the two facts itself. One fixture fix: a `GatherCompilerTests` upgrade priced in a
currency off its chain, which Build refuses once it compiles a cost plan for it. Same day, on the
landing report's findings: decision 19 (the wildcard reaches count and cost, idle halves grants),
the tick records granted deposits so the row's count interpolates, and the save's dropped-granted
warning names a granted count. External review, same day: a payment landing inside a tick's
settlement fired a non-repeating bar's crossing twice, since `SettleOnce` compared the snapshot with
live progress; it compares the snapshot with its own fill, so each mover settles only its own
crossing (one test).

**D, E, F, G, H DONE 2026-09-16** - 878/878 green (+34: 34 added, 0 deleted), with decisions 20-22
asked and ruled before the contract was written. Three fixture fixes after the first run, no runtime
corrections: the walkthrough's run seeder wrote a fans balance without the earned total the album
reads by default; an importer fixture named a flag `encore`, which collides with root's `encore`
modifier on the chain (`DuplicateHome`); and two expected save warnings were listed against the
dictionary's iteration order. The reimport rewrote the three cover assets (`repeatWhen` null and
`tap` empty where `repeating: 0` was) and regenerated rids everywhere else. I (debt) stays deferred
to the Van chapter.
