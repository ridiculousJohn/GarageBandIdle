# Garage Band Idle — Design & Build Spec

An idle game about a band rising from a garage to arenas. Play progresses through eight chapters, each
a bigger venue with a new mechanic. All numbers below are starting values for tuning.

> **Doc state.** Rewritten 2026-08-17. The previous architecture (scope-tree machinery, stored modifier
> registry, re-projection, settle boundaries) was rejected and replaced wholesale; it survives only in
> this file's git history. The game design (§1–§11) is carried forward; the architecture (§12) is
> rebuilt around one principle: **state is stored, everything else is computed on read.**

---

## 1. Core loop

The game has two kinds of loop — a fast one inside a chapter, a slow one across chapters. A chapter
declares one or more **prestige tiers**; Chapter 1 declares one (the album). A later chapter may
declare several, either independent of each other or laddered so that pressing a deeper tier resets
the shallower ones with it (§12.3).

**The album loop (inner).** Within the current chapter the player taps for Cash, buys gear and
bandmates, grows Fans, and then releases an album. Releasing an album resets the run — Cash, gear,
Fans, and the working Catalog — and awards **Records**. Each Record permanently increases global
income, so the next run is faster. The player repeats this loop several times within a chapter.
A chapter with more than one tier may bank a different currency at each: a shallow tier's payout can
be an intermediate currency spent inside the chapter, while only the deepest banks Records. Which tier
carries the income multiplier, which carries the advancement gate, and which is purely archival is a
per-chapter authoring decision.

**The chapter loop (outer).** Records earned within the chapter unlock its capstone gig. Playing
the capstone **cuts the album as part of the show if the run has earned it** — the run's Fans bank
as Records only when the album's own gate is met; an unfinished run is discarded — and then
completes the chapter. Completion facts (the completion flag; optionally, counters a chapter's
reward curves read, §8.1) live at the **root** and survive everything; the capstone's reset clears
the entire chapter, which is what makes the
chapter immediately replayable (§8.1). Advancement to the next chapter is forward-only and reacts to
the completion flag.

Records are the link between the loops: they raise income, and the Records a chapter's albums earn
gate its capstone. Chapter advancement therefore depends on releasing albums, not on a single large
Cash total.

```
   INNER (minutes):  tap → Cash → gear → Fans → release album ─┐
                      ▲  reset run, +Records, repeat faster     │
                      └─────────────────────────────────────────┘
                                    │ this chapter's Records reach the gate
                                    ▼
   OUTER (hours/days): capstone gig → next chapter (forward only)
```

---

## 2. Chapters

Eight chapters. Each has its own gear, currencies, mechanic, and capstone gig, and is gated by
Records earned within the chapter.

| # | Chapter | New mechanic | Capstone gig |
|---|---|---|---|
| 1 | Garage / Basement | Tap, gear/bandmate buffs, "learn covers" bars | First backyard / house party |
| 2 | Open Mic / Talent Show | Fans scoring, rehearsal, bigger song list | Win an open-mic / talent-show slot |
| 3 | House Parties | Merch (second income stream) | Headline the house-show circuit |
| 4 | Local Venues / Small Gigs | Booking agent (automation) | First booked venue gig |
| 5 | Regional Tour | The Van: routing across towns | Complete a regional tour / festival slot |
| 6 | Record Deal / Studio | Songwriting & Catalog (§7) | Sign the deal & cut the record |
| 7 | Radio / Streaming | Royalty catalog scaling; large idle income | First charting single / radio play |
| 8 | Arenas / Stadiums | World tours, endgame scaling | Sell out a stadium (Hall of Fame) |

Every chapter uses the same rhythm — tap, buy, grow Fans, release an album — with new gear, a new
mechanic, and a higher Records gate.

**Chapter anatomy.** A chapter is a **scope** (§12.3): a state container holding the currencies,
flags, producers, generators, upgrades, bars, and triggers declared to it, plus its tier scopes. The chapter level owns
whatever the whole chapter shares; each tier owns what that tier's release destroys. **Lifetime is
placement**: a fact survives a reset by being declared further out, and anything two tiers share
lives in their nearest common ancestor. Moving a declaration up a level is a pure data edit, because
authored content references content DIRECTLY (the asset, not its id), and the ids that remain - in saved facts - are unique along a chain.

**Progressive reveal.** A chapter does not present all its mechanics at once. Content-unlock upgrades
(§4) introduce new generators, currencies, and mechanics as the player buys them. Each such upgrade
should introduce a change in play — a new mechanic, sub-loop, or automation step — rather than only
increasing a number.

Reveal runs through **one mechanism**: revealed content gates its visibility on a `Condition`. A
reveal tied to a *moment* gates on a flag that moment's actions set (an upgrade payload, a bar
completion); a reveal tied to a *threshold* gates **directly on the monotonic fact**
(`EarnedTotalAtLeast`) — pure visibility needs no flag and no moment. A threshold moment that
carries a **payload** (a grant, a story hint) is a **Trigger** (§12.5): the one sanctioned
condition-observer, one-shot per scope-life, swept at defined points. Division of labor: direct
gate for pure reveals, trigger for threshold moments with payloads, moment-set flags for reveals
hanging off purchases and completions.
A section is visible exactly *while* its condition holds — evaluated live, no latch. "Stays once
earned" is authored by gating on a fact with that lifetime: a flag, or a monotonic value like total
Cash ever earned. Gating a region on a spendable balance is an authoring smell — it strobes with
every purchase. (Ch. 1's gear region gates on `EarnedTotalAtLeast(cash, 250)` for exactly this
reason — earned total, not balance.)

A flag's lifetime is the scope that declares it (§12.3). Chapter 1 declares `fans` and `covers`
(and the upgrades that set them) in its tier, so an album release clears them — and the tier's Cash
earned total resets with the run too, re-hiding the threshold-gated gear region — so the second run
re-walks the progression — band → fans → covers → gear — instead of opening with every system on
screen. The `album` flag is declared at the chapter level, so the release *region* stays on screen
across runs; only the button's pressability tracks the live offer condition (§5).

Boot validation: a setter that lives OUTSIDE the flag's home is an error, not a warning — the write
walks outward and can never reach the flag, and the same scope could not read it either, so the
gated sub-system would go dark for good (§12.12).

Once a chapter is cleared it remains available for replay (§8.1).

---

## 3. Currencies

A currency's durability is **which scope declares it**. There is no lifetime flag or placement enum:
a currency declared in a tier resets when that tier resets, one declared in the chapter survives every
tier reset, and one declared at the root survives everything. The headings below name Chapter 1's
*filing*, not a property of the currencies.

Currency definitions are global content; **ids are unique across the whole tree**, and an id declared
in two scopes is refused at load. Resetting a scope clears the balances it holds and never reaches a
balance held further out.

**Run-scoped (Chapter 1 tier):**
- **Cash** — earned by tapping and generators; spent on gear and upgrades.
- **Gear & bandmates** — generators bought with Cash, each contributing to one or more currencies'
  rates (§12.2). A bandmate is simply a generator with two outputs — cash and fans — plus a
  `bandmate` tag; there is no flag any system branches on.
- **Rehearsal** — Chapter 1's fill currency, earned from engagement (a passive tick plus taps) and
  consumed by learn-songs bars (§6). An ordinary currency; later chapters may define their own fill
  currencies.
- **Learn-songs bars** — generic fillable bars that pace a chapter (§6, §12.7).
- **Fans** — the run's performance meter; determines the album's Records payout on release. Accrue
  passively once revealed; the fans rate composes a base contribution plus one from each bandmate,
  so it is a function of band size and time — never of Cash or income.
- **Catalog (Ch. 6+)** — songs written during the run; a global income multiplier that converts to
  Records on album release (§7).

**Permanent (root):**
- **Records** — each Record increases global income; the Records earned within a chapter (mirrored
  into a chapter-declared counter, §5) gate its capstone. Accumulated, never spent.
- **Roadies** — crew; a global multiplier allocated across cleared chapters (§8).
- **Discography** — a list of the player's best named songs (§7). Display only.

**Income.** Every produced number is computed on read as *the sum of its contributions times the
product of the multipliers targeting it* (§12.2). The effective multiplier stack on Cash:

```
income = Σ(generator base × owned × per-generator effects)
         × catalogBoost      (run-scoped fact, §7)
         × recordsMultiplier (root fact, §5)
         × roadieTotalBoost  (root fact, §8)
         × roadieChapterBoost (the played chapter's own stationing, §8.2)
```

Each of those is an **Effect** (§12.2) that declares which target it multiplies; a currency never
opts into a multiplier — the dependency points from the effect to its target. All of these effects
exist from the start of the game and simply contribute nothing until the facts they derive from
exist. Encore is deliberately absent from the stack: it multiplies **time**, not income (§9).

---

## 4. Upgrades

Upgrades are the primary way a chapter's content is delivered. The player buys them with chapter
currencies as they become affordable.

- **Gating.** An upgrade's gate is a single `Condition` (§12.4), so it can gate on any currency, an
  earned total, an owned count, a flag, or completed bars. Which currency unlocks which upgrade
  defines the order the player develops each currency and gives each chapter its shape.
- **Effects & payloads.** An upgrade carries a `List<Effect>` (ongoing buffs, applied while the
  purchase latch exists) and a `List<Action>` (one-shot payloads executed at purchase: grant a
  currency, set a flag, add a modifier). No per-kind upgrade classes.
- **Reveal.** A content-unlock upgrade reveals its content by a `SetFlag` action; the revealed
  content gates on that flag. One reveal mechanism for every content type.
- **Lifetime is placement.** *Buff upgrades* are declared in a tier: their purchase latch clears when
  the tier resets and they are re-bought each run, faster as banked multipliers accumulate.
  *Content-unlock upgrades* are declared wherever their reveal should live: Ch. 1 files its reveal
  chain in the tier so the second run re-walks it.
- **Prestige-bought content.** A generator or upgrade priced in a banked currency is simply declared
  one scope out from the tier that resets — it survives the reset that pays for it. A generator's
  cost currency is independent of what it produces ("buy with Cash, produce Merch" is a data shape).

---

## 5. The album (prestige)

Releasing an album is the run reset. Its name escalates thematically across chapters (demo, EP,
record).

Mechanically a release is a **rung**: `{offerCondition, List<Action>}` declared on the tier
(§12.5). Chapter 1's:

```
tier1.release:
  offerCondition: All[ CurrencyAtLeast(fans, 50), BarsCompleted(1) ]
  rungActions:    [ AddCurrency([records, ch1_records], PayoutFormula),  // one evaluation, both targets
                    ResetScope(tier1) ]
```

The payout is an ordinary `AddCurrency` action whose amount comes from a `PayoutFormula` (§12.5) —
there is no payout field and no distinguished award kind. An `AddCurrency` may name several targets
paid from a single evaluation — the album pays root `records` and the chapter's gate counter
`ch1_records` identical amounts that can never drift. **Order is authoring**: the payout action
precedes the `ResetScope`, so the formula reads the Fans the reset is about to destroy. Actions
placed *after* a reset land in the fresh state, which is how a head-start reward ("start the next
run with 500 Cash") is authored.

- **Resets:** Cash, gear, learn-songs bars, Fans, working Catalog — everything the tier declares.
- **Keeps:** Records, Roadies, Discography — declared at root, unreachable by the tier's reset.
- **Awards Records** based on run performance:
  ```
  early chapters:  recordsEarned = f(fansThisRun)        // Ch. 1: floor((fans/5)^0.5)
  Ch. 6+:          recordsEarned = f(fansThisRun, totalCatalogQuality)
  ```
- **Each Record** grants about `+2%` permanent global income (additive within the Records term).
- **Records earned this chapter** unlock its capstone at a set threshold (§11). The counter is a
  chapter-declared currency, so the capstone's own reset zeroes it — every replay faces the same
  gate (§8.1).

An early album cycle takes seconds to minutes; cycles get faster as Records accumulate.

The release button is **pressable** exactly while its offer condition holds — its inputs are run
values the release itself resets, so the offer disarms at every release and re-arms on the re-climb.
The release *region* stays visible because the `album` flag it gates on lives at the chapter level.
Region coarse, action precise.

The offer condition is checked on **every** invocation — when the player presses the button
(`TryRung`) and when another rung invokes this one (`ExecuteRung`, §12.5). There is no bypass: a rung whose gate is
unmet no-ops, so the payout is only reachable through its own gate, and an unfinished run is
discarded by whatever reset follows — never banked.

**Formulas that reward pushing past the gate.** Because the payout formula reads the live balance at
press time, a "bank at 1000, keep accruing for more at a lower rate" mechanic (Ctrl C's
Lines → Knowledge) is entirely a formula shape: the offer condition sets the floor, a piecewise
formula pays `10% × min(x, 1000) + slower(x − 1000)`, and the press-now-or-push-on decision emerges
from the curve. The UI's "would bank: N" preview calls the same formula — one implementation, no
drift.

---

## 6. Within-a-chapter play & events

Moment-to-moment play:

- **Tap ("Jam")** — early Cash source; its relevance falls off as gear automates income. Jam is a
  **producer definition** (§12.2) — `tap_producer`: a cash yield, plus a rehearsal yield and
  passive-tick rate both gated on the reveal flag — Rehearsal accrues nothing before its reveal.
  The button is a module whose entire knowledge is
  `FireProducer(tap_producer)`; every payout, gate, and base number is data on the producer. "Tap"
  is a UI gesture; the economy only knows a producer was fired.
- **Generators** — exponential cost, `cost = base × growth^owned`, growth ~1.15; 4–6 themed per
  chapter. Because runs reset, a chapter's Cash stays in the thousands–millions range; cross-chapter
  growth comes from Records and Roadies.
- **Upgrades (§4).**
- **Learn-songs bars** — see below.
- **Fans** — passive accrual, band-size driven (§3), tuned loosely relative to Cash so income alone
  does not determine the album payout.
- **Capstone gig** — the chapter's rung (§12.5), declared on the chapter:
  ```
  chapter.capstone:
    offerCondition: CurrencyAtLeast(chapterN_records, gate)  // same gate, first clear and every replay
    rungActions:    [ ExecuteRung(tier1),          // cut the album if its own gate holds; else no-op
                      AddCurrency(roadies, RewardFormula),    // Ch. 1: the constant 1 (§8.1)
                      SetFlag(chapterN_complete),   // declared at ROOT
                      ResetScope(chapterN) ]        // the ENTIRE chapter, downward-closed
  ```
  `TryRung` is fail-closed — the operation checks its own gate, so a UI bug cannot complete a
  chapter early. The chapter **advance is not an action**: it is a reaction to the root completion
  flag, performed by `ChapterManager`, which makes it derivable from the save no matter how or when
  the flag was set. The first-clear story beat is likewise gated on state — `complete &&
  !story_seen`, a root latch set on dismissal (§10) — never on observing a transition.

### 6.1 Events

An event is a self-contained challenge inside a chapter that the player enters by choice. Events do
not gate chapter advancement — the gate is always Records — and their rewards are lateral (never
Records), so no event is ever a hard requirement.

How essential an event feels is a **per-event tuning decision set by the size of its reward**: a
small reward is a skippable bonus; a large one makes skipping a much slower grind. The chapter is
always completable without any given event, but only quickly with the events its tuning intends.

An event is **content plus one state record** (§12.8):

```
EventDefinition: availableWhen (Condition), goal (Condition), timeLimit (0 = untimed),
                 handicaps (List<Effect>), onEntry / rewards / onEnd (List<Action>)
ActiveEvent:     { eventId, remainingSeconds, goalReached }   - in host scope state
```

The definition carries no host field: it is declared on its host scope's `events` list, so placement
IS the host and the outward walk recovers it, exactly as for every other content family (§12.8).

- **Lifecycle: two self-guarding operations.** `StartEvent(event)` and `DismissEvent(event)` are
  **commands, not Action kinds** (12.11) - a player enters an event by choice, so no authored list
  can start or end one, and the whole class of one event spawning another has nowhere to live. Each
  is fail-closed against its own gate: start checks `availableWhen` **and that the host holds no
  event record - one event per scope, and an expired-but-undismissed record still counts**, dismiss
  checks that the host holds a record **for that event**. There is no separate claim and abort:
  dismissal is the single ending, and it pays the reward if the goal was reached - so a successful
  attempt cannot be thrown away by pressing the wrong button.
- **On start** (`StartEvent`), the event's `onEntry` actions run in order — typically
  `[RestartScope(host)]` - bank the run through the host rung's own gate, then clear it (§12.5) -
  and the ActiveEvent record is created after
  they finish, so it lives in the fresh state. The rung checks the host rung's own gate like every
  invocation does (§12.5), banking a gate-met run exactly as a release would and discarding an
  unfinished one. Nothing is lost that could have been kept — a run the gate refuses could not have
  been released manually either — so there is no bank-it-first ritual. And a rerun tier cannot be
  farmed for advancement currency, because the payout is only reachable through the same gate as a
  normal release. Whether entry resets at all is authoring — it is just whether `onEntry` contains
  a `ResetScope`.
- **Scale.** Events deliberately scale with the player's accumulated power: the host reset zeroes
  the tier's own facts, but root and chapter facts still apply. A tier may be *unbeatable* until the
  player has advanced further — "come back later" is the intended experience.
- **Goal:** a Condition, usually `CurrencyAtLeast`. Weirder goals are `All[...]` compounds.
- **Debuff (optional):** the handicaps are ordinary Effects with multipliers below 1 — generation
  halved is ×0.5, automation disabled or a currency locked is ×0. They apply while **an
  ActiveEvent record exists** and vanish with it (§12.8); expiry does not lift them. A failed
  attempt sits one tap from a tier reset, so dropping the handicap at expiry would hand the player
  a briefly unhandicapped tier that is about to be wiped. Nothing is installed or torn down.
- **Success is one rule, timer or not.** The goal latches the moment it holds while the attempt has
  not expired - the sweep, which runs inside every transaction, tick and command alike (§12.11),
  writes `goalReached` latch-first like every observer. A command that reaches a spendable-balance
  goal secures it even if a later command spends back below, and an untimed event works the same
  way: meeting the goal is something that happened, not a state the player must still be holding
  when the button is pressed. **A timer is the only thing that can end an attempt**, and it is the
  only difference between a timed and an untimed event.
- **Timer (optional):** `remainingSeconds` decrements on live ticks only, so the attempt pauses when
  the chapter is inactive or the app is closed — a deadline the player cannot attend is not a
  challenge. The exchange: a chapter holding a record for an event that **blocks idle** pays no idle
  earnings on switch-in, which closes both the app-close and the switch-away-to-wait-out-the-clock
  exploits at once. `blocksIdle` is derived - a timer is what blocks - so the idle path asks the
  event that one question and never inspects a timer (§12.9). Untimed events accrue idle normally;
  they can sit open for days while the player grinds toward the goal, and there is no clock to wait
  out. A goal met in the same segment the timer expires counts - the tie goes to the player, which
  is why the latch is taken at the segment edge before the decrement (§12.9). Expiry **ends nothing
  by itself** and does exactly one thing: it stops the goal from latching. The record stays,
  handicaps and all, until the **player** dismisses the attempt or a reset reaches the host. Because
  the record still occupies the host, `StartEvent` keeps failing until it is dismissed - enforced in
  code; the UI simply doesn't offer a new event while one is pending. An untimed event at
  insufficient power is merely unfinishable.
- **Dismissal is the ending, and it is claimed, not automatic.** A met goal arms nothing but the
  reward - exactly as 50 Fans arms the release button - and fires nothing by itself. `DismissEvent`
  **removes the record first**, then runs `rewards` if the goal was reached, then runs `onEnd`
  either way. Removing first is what opens a rung guarded by
  `Not(EventRewardPending(host))` so that an `onEnd` carrying `[RestartScope(tier)]` can actually
  bank. Nothing in an action list can observe the record's absence: handicaps are
  multipliers on production, and rewards read balances.
- **Two ending lists, because failure has an ending too.** `rewards` runs only on success and holds
  the payout - a chapter-durable buff (`AddModifier`), a Roadie, a Catalog song, local currency.
  `onEnd` runs either way and is where the reset lives, so reward-before-reset is structural rather
  than a convention an author can get backwards. What a failed run costs is therefore authoring:
  `[RestartScope(tier)]` in `onEnd` banks whatever the tier's own gate allows and wipes the rest, a
  bare `ResetScope(tier)` destroys it, and an empty `onEnd` leaves the leavings
  for the next reset to claim. And an event **dies with its scope**: any reset that reaches the host
  clears the record - a mid-event release, a capstone resetting the chapter from above.
- **Reward constraint:** never Records or any advancement currency - an event's payout is lateral.
  Authoring, not a validated rule: nothing in the model marks a currency as advancement, so a check
  would have to invent that vocabulary, and one that followed rung references would flag the
  legitimate case - an `onEnd` running `RestartScope` banks Records through the tier's own gate.
- **Levels:** a harder rerun of an event is simply **another event** — `silent_stage_2` is its own
  definition whose `availableWhen` gates on `silent_stage_1_done`; there is no level machinery, no
  selection contract, and `StartEvent(event)` is always fully specified. The rising requirement
  plus the player's power curve throttles level stacks as a repeatable Roadie source. Reward
  composition is per-chain authoring: each level's modifier is an *increment* that stacks with the
  previous level's, or the next level's `rewards` swaps them explicitly
  (`RemoveModifier` + `AddModifier`).

---

## 7. Songwriting: Catalog & Discography (Ch. 6+)

Songwriting unlocks at the Studio chapter.

- **Writing a song** rolls a quality tier — Common, Hit, or Classic — and the player names it. Song
  quality feeds a run-scoped global multiplier:
  ```
  catalogBoost = 1 + Σ(quality weight per song this run)   // e.g. Common .01 / Hit .05 / Classic .20
  ```
  The songs written this run are facts in the tier's state; the boost is an effect derived from them
  on read (§12.6) — it exists from Chapter 1 and contributes nothing until songs exist.
- The multiplier is driven by song **quality**, not count, so songwriting is about improving songs.
  It applies to all income and feeds royalty/idle income.
- **On album release,** total catalog quality is the main input to the Records payout (§5), and the
  working Catalog resets with the run. Routing catalog value into Records keeps permanent progression
  consolidated in one currency.
- **Discography** is a persistent root-level list of the player's best songs, kept for display.
  Songs reach it by **promotion: an action in the release rung, ordered before the `ResetScope`**,
  reading the dying run's Catalog and writing the chosen songs to the root list (`AddSong`). The
  selection rule is a Ch. 6 authoring decision, deferred (candidates we considered: auto-by-quality —
  Classics always chart, Hits at a threshold; best-N-of-run; player picks a song to immortalize at
  release).

The three song systems are separate: learn-songs bars pace early chapters (run-scoped); Catalog is
the studio-era multiplier (run-scoped, converts to Records); Discography is a persistent display list.

---

## 8. Roadies

Roadies are a permanent global multiplier. The player earns them from capstones and from replaying
cleared chapters, and can also buy them (§9). All Roadies go into one pool and can be reassigned
freely. The pool is the root `roadies` currency; the allocation is a root fact the venue boosts
derive from.

### 8.1 Replaying cleared chapters

Replaying cleared chapters is the main way to earn Roadies through play.

The capstone's `ResetScope(chapter)` leaves the chapter freshly reset in place, so **replaying a
chapter is just playing it again** — same container, same content, progression re-revealed as the
player re-walks it. There are no separate replay instances and no frontier/replay distinction; the
chapter scales with every root multiplier (Records, Roadies, Encore) because that is simply what
resolution reaches, exactly as it did the first time when those totals happened to be zero.

**One gate serves every clear.** The capstone's offer condition is
`CurrencyAtLeast(chapterN_records, gate)` — a counter currency declared at the chapter level and
fed by the album payout (§5). Because the capstone's own reset clears the counter with the rest of
the chapter, every replay starts at zero against the same goal: no first-clear/replay switch, no
clear count, no rising requirement. Replays get **faster, not harder** — banked Records and
stationed Roadies speed the climb to the same gate — which is what makes replaying worth doing at
all. Farming is throttled by **time**: the fan rate is band-size-and-time driven, deliberately not
income-multiplied (§3), so a clear keeps a wall-clock floor no multiplier stack removes, and the
concave roadie-spread multiplier (§8.2) favors rotating chapters over parking on one.

**Rewards and goals are formulas over stored facts.** The Roadie payout is an ordinary
`AddCurrency(roadies, PayoutFormula)`; Chapter 1's formula is the constant 1 and its goal is the
flat authored gate. A chapter that wants scaling reads a fact through the same families: a
per-clear counter (a root currency its own rung increments) feeding a reward or goal curve, or a
player-chosen difficulty whose handicaps derive from the choice exactly as event handicaps do
(§6.1) and whose reward formula pays more for the harder clear. Only the chosen-difficulty variant
needs anything new — an action recording the choice as a fact — and it is deferred until a chapter
wants it.

Roadies stationed at a chapter increase that chapter's local production (faster replays), and
clearing a chapter's goal adds a Roadie to the pool.

### 8.2 Boost formula

- **Within a chapter (additive):** `chapterBoost = 1 + 0.05 x roadiesStationedThere`
- **Across chapters (multiplicative):** `totalBoost = chapterBoost_1 x chapterBoost_2 x ...`

`totalBoost` is the permanent multiplier applied to income. Example: 9, 9, 8, and 9 Roadies across
four chapters give 1.45 x 1.45 x 1.40 x 1.45 = 4.27x.

Because chapter boosts multiply, distributing Roadies across more chapters beats concentrating them
(8 Roadies: 1.40x in one chapter, 1.46x split across four). **The chapter being played double-counts
its own factor**: `activeProduction = totalBoost x chapterBoost(played)` - its boost applies once
inside the global product and again locally, which is what makes stationing Roadies speed the
chapter being worked (8 Roadies stacked: ~1.96x there, 1.40x everywhere else; spread 2/2/2/2: 1.46x
globally and ~1.61x on the chapter being worked). Allocation balances spreading for the total
against concentrating to sprint an active replay - both are real strategies. **Both
factors are ordinary effects whose target is authored data** — a tag (Ch. 1: `income`, declared by Cash) — so
*what* Roadies help with is a per-chapter design decision, never a code decision. Both carry
`stat: rate`: Roadies are the passive-crew lever and scale production, never a tap's yield. They are
composed at different LEVELS, though. `roadie_total` is a currency-total effect - it is the same
product everywhere, so the currency is where it belongs. `roadie_active` targets the production
SOURCES (`{target: production, currencyId: income, stat: rate}`), because which chapter a number is
produced in is a property of its source; a currency total has already summed across sources and can
no longer name one chapter. **The fan rate must never carry a roadie-targeted tag**: the wall-clock
throttle of §8.1 stands on Fans being unbuffable by Roadies, and the `currencyId: income` narrowing
is what enforces it even on a bandmate that pays Cash and Fans from one definition. (Deliberately open: whether reallocating applies retroactively to a dormant
chapter's idle claim computed at current rates.)

**Where the numbers live.** Each factor is a `CareerEffectDefinition` on the root scope whose
formula carries its own `perRoadie` (`RoadieTotalBoost`, `RoadieActiveBoost`); both read the root's
`roadieAllocation` map and nothing else. The global one takes the product over the map's entries -
one entry per chapter holding Roadies, additive within the entry - and the local one reads the
entry for the chapter it resolves on. No per-chapter asset, no id pointing back at a chapter. There
is no stationing cap: if one is ever needed it belongs at the WRITE (`SetRoadieAllocation`), not in
the read.

---

## 9. Idle earnings & monetization

All ads are opt-in and return a concrete reward; there are no forced interstitials. Everything
purchasable is also earnable in-game.

**Idle earnings (per chapter).** The unit of idle is the **chapter**, and "active" is singular: the
chapter on screen ticks live; every other chapter is dormant. Each chapter's state carries one
`lastActiveUtc`, stamped on switch-away and — **for the foreground chapter only** — on save; a
global save never touches dormant chapters' stamps, or it would truncate their idle. **Switching into a chapter** computes, for each
of its currencies, `rate × min(elapsed, cap) × idleRate` — from *current* state, so Records earned
elsewhere while away correctly boost the payout — and stores it as a **pending claim** in the
chapter's state, presented as the **idle dialog**: the amount earned, plus "Double it" (a rewarded
ad that doubles *this claim*); a Backstage Pass owner sees the already-doubled amount and just OKs
it. The claim deposits when the dialog is dismissed — any exit path — and a claim that survives an
app kill re-offers on the next entry; then the chapter is live. **Closing the app is not a
mechanic**: it is the state where no chapter is active, and launching runs the same switch-in path.
In-game chapter switching and time away are one mechanic.

What accrues is settled by structure: every currency **rate** accrues (including Fans and the fill
currency — progress while away is what an idle game is); a **yield** never accrues, because nothing
fires a producer in the player's absence; and **bar progress never accrues**, because filling is
consumption, not production. So **time away fills the pool, presence spends it**: the player returns
to banked Rehearsal, chooses a bar, and watches it fill at the group's rate. No idle flags, no
exempt lists — the rate/yield split answers everything.

A dormant chapter with no generators accrues nothing (zero rate), so parking an untouched chapter
earns nothing — the system self-regulates without a rule.

`idleRate` (base 50%) and `cap` (base 4 h, plus a minimum-away threshold below which nothing pays)
are two **reserved effect target ids** — `idle_rate` and `idle_cap` — so the monetization features
are ordinary Effects:

| Player | Idle payout | How |
|---|---|---|
| Free, no action | 50% | Claimed from the idle dialog on switch-in |
| Free, watches ad | 100% (2×) for that claim | "Double it" on the dialog applies `{target: idle_rate, ×2}` to the pending claim |
| Backstage Pass owner | 100% (2×) always | The same effect, derived from a permanent entitlement fact; also raises `idle_cap` |

Idle income is themed as streaming/radio royalties and is largest at the Radio chapter.

**Encore (the accelerator).** A **game-speed multiplier**, not an income multiplier: a timed buff
(`{buffId, expiresAt}` in state) whose effect is `{target: game_speed, ×2}`. `game_speed` is a
reserved effect target whose sole consumer is the tick — `effective dt = real dt ×
GetMultiplier(game_speed)` — so every rate, accrual, and bar fill in the live chapter speeds up
automatically. Yields never scale (per-firing, no time component), and **wall-clock decrements
never scale**: event `remainingSeconds` and buff expirations burn real seconds. The timer is an
absolute expiry, so it counts down whether the app is open or closed. Rewarded ads add ~+4 h to the
timer; sustained use escalates to **4× ("Overdrive" / "Sold-Out Show")**, also capped.

**Backstage Pass** — lifetime IAP (~$5–10). Permanently doubles idle earnings, raises the idle cap,
and keeps Encore permanently active at Overdrive (4× speed). Since ads are opt-in, the Pass's value
is convenience.

**Buy Roadies** — consumable, repeatable IAP. Bought Roadies are identical to earned ones. No
purchase cap; throttled by escalating bundle price. (Allocation concavity punishes stacking one
chapter but does **not** cap the total: spread Roadies compound multiplicatively across chapters,
so price is the real throttle - §8.2 authors no stationing cap.) A `bought ≤ earned` cap
is held in reserve for a competitive leaderboard. A late-game Cash → Roadie sink may be offered.

**Tip Jar** — small one-time purchases with no gated content.

**Subscriptions** are not used — the content is replayable rather than expandable.

Any reward for playing beyond Roadie count goes in a separate, unbuyable track (e.g. a "reputation"
multiplier for first-clears).

---

## 10. Story

The story is delivered at chapter boundaries. A card at chapter open sets the scene and the goal
("Pull 200 people and the Friday slot is yours"); a beat at the capstone resolves it and introduces
the next chapter — gated on state, never on observing a transition: the beat shows while
`chapterN_complete && !storyN_seen`, and dismissing it sets the root `storyN_seen` latch, so a
crash between the completion save and the beat cannot skip it. There are no story interruptions
during the loop itself.

Named Catalog songs (§7) serve as story artifacts — the songs that chart appear in the Discography
and persist.

---

## 11. Pacing & tuning

Chapter pacing is set primarily by the per-chapter Records gate, which determines how many album
cycles a chapter takes and therefore the overall game length. Records gates are the first tuning
lever; generator curves are adjusted only after.

Two structural properties keep pacing stable against players with strong income multipliers:
- Chapters gate on Records, not Cash. Multipliers raise Cash; advancement requires album releases.
- Fan rate is tuned loosely relative to Cash, so income alone does not shortcut the album payout.

Tuning must hold at both ends: an unbuffed player's pace is doable and never feels impossible, and
a permanent-Overdrive player (4× game speed, plus Roadie and Catalog multipliers) still takes
meaningful play time per chapter and breaks nothing. Timed events feel Overdrive fully — speed
scales production but never timers (§9), so a 4× player meets a timed goal in a quarter of the
clock; author timed goals with that end in mind.

**Per-chapter economy template (to fill in):**
- 4–6 themed generators (exponential cost, growth ~1.15, Cash in the thousands–millions).
- A Fan target that makes an album cycle meaningful (seconds early, minutes later).
- A Records payout formula (Fans early; Fans × catalog quality from Ch. 6).
- A capstone gate on Records earned within the chapter.
- One new mechanic.

---

## 12. Architecture & build notes (Unity)

### 12.1 Principles

1. **State is stored; everything else is computed on read.** Multipliers, rates, yields, condition
   results, and derived completion (a bar at full, a goal met) are never stored — anything computable
   from other state is recomputed whenever asked. (A completion flag a rung *sets* is a stored fact,
   not a derivation.) Nothing derived can go stale, double-count, survive a reset it shouldn't, or
   disagree with a save.
2. **All durable gameplay state lives in the ScopeState tree** (§12.3). Transient orchestration —
   the foreground chapter selection, the session phase, command guards, derived caches — lives in a
   never-serialized `GameSession` (§12.9). Systems are stateless code that reads and writes those
   containers; no system instance per scope, no state hiding in managers.
3. **Lifetime is placement.** A currency, flag, upgrade latch, or bar lives in the scope that
   declares it; the reset that clears the scope clears the fact. Nothing declares a lifetime.
4. **Class families for open sets; flat data for closed records.** Conditions, Actions,
   and PayoutFormulas are polymorphic families — adding a kind adds a class and
   touches nothing that exists. An Effect is a flat struct because it has one shape.
5. **Validate at content load** (§12.12). Ids resolve, homes are unique, authoring mistakes are
   caught at import, not at runtime.
6. **The UI renders state and forwards intent** through fail-closed entry points. It never mutates.

### 12.2 The economy: Currency, Producer, Generator, Effect

**Currency** — `{id, balance}`. Pure state. It does not know how it is earned.

**Producer** — a named definition that owns base contributions. Each `produces` entry declares one
number — which currency, which **stat**, the base value — plus an optional condition that must hold
for the entry to count:

```csharp
class ProducerDefinition : Definition        // id + tags, like every Definition
{
    List<ProducesEntry> produces;            // { currency, stat, value, condition? }
}
```

Stats are **named, not enumerated**: `rate` (units/second — accrues idle time) and `yield`
(units/firing — paid when something calls `FireProducer(producer)`, never accrues). A stat means
something because a system consumes it — the tick consumes `rate`, `FireProducer` consumes `yield` —
so a later accumulation concept is a new stat name plus its consumer; no field grows, nothing
existing is touched. A stat no system consumes warns at load. Rate and yield are modified and
presented separately ("+12/sec" vs. "+5 per press"). Firing is external and unnamed: a button, an
automation, and a test are indistinguishable below the module layer. All outputs of one
`FireProducer` call resolve atomically from pre-fire state — conditions and amounts judged
together, then deposited — so no output can flip a sibling output's condition mid-fire.

Chapter 1's Jam is a producer, not UI logic:

```
tap_producer.produces: [ { cash,      yield, 1 },
                         { rehearsal, yield, 1,   FlagSet(rehearsal_revealed) },
                         { rehearsal, rate,  0.5, FlagSet(rehearsal_revealed) } ]
                         // the rate is gated too — Rehearsal accrues nothing before its reveal.
                         // (An UNconditioned rate entry is how a pre-banking mechanic would be
                         // authored, if a chapter ever wants one.)
```

**Generator** — the purchasable. Definition: `{id, tags, availableWhen, costCurrency, baseCost,
growth, produces: [...]}` — the same entry shape as a producer, scaled by `ownedCount`; state:
`ownedCount` in its declaring scope. A bandmate is a generator with two rate entries (cash, fans)
and a `bandmate` tag. Cost currency is independent of what it produces. `TryBuy` is fail-closed
against `availableWhen` and affordability — the domain owns the gate, never the UI's visibility.
(Producers need no equivalent field: their `produces` entries carry their own conditions.)

**Effect** — the modifier atom:

```csharp
[Serializable] public struct Effect
{
    public string target;      // a currency id, a producer/generator/bar id, or a TAG
    public string currencyId;  // optional — narrow to entries paying this currency (id or TAG)
    public string stat;        // optional — narrow to this stat ("rate"/"yield")
                               // both empty = every number the target has; both set = one entry
    public double multiplier;
}
```

Sources carry a `List<Effect>` — one factor per number, grouping lives in the list, so "×2 rate and
×3 yield" is two entries and no enum ever grows a `Both`. **Modifiers are multipliers only**; a flat
bonus is a *contribution* to the number it raises, authored as a `produces` entry — only producers
and generators carry entries; another fact (an upgrade, a flag) contributes flatly via an entry
*conditioned* on it. Every composed number has one shape: sum of matching contributions whose conditions hold ×
product of matching multipliers. An Effect never carries a count or growth; where a stored count
scales an effect (fill counts, modifier stacks), the carrying entry declares the growth (§12.7).

**Tags** — every Definition carries `tags: [...]`; an Effect's target matches an id or a tag. A set
gets its name from its members (`rhythm_section` declared by the drummer and bassist), so buffs never
list members and later additions join by declaring the tag. An effect applies at the level of what
it matches: on a producer or generator (by id or tag) it multiplies **inside the sum** (that
source's term); on a currency (by id or tag) it multiplies **the total** — one rule, no double
counting. "Global income" is this tag mechanism, not a reserved id: income currencies declare an
`income` tag (Ch. 1: cash), the career effects of §3 target it, and a later income stream joins the
stack by declaring the tag.

**Reserved target ids:** `idle_rate`, `idle_cap`, `game_speed` (§9) — each consumed by exactly one
system (`game_speed` by the tick, which scales the production dt; wall-clock decrements never scale).

`GetMultiplier(owner, currencyId, stat)` answers one question: *which factors apply to this
number?* A number is identified by its owner and coordinates — a `produces` entry by
(source, currencyId, stat), a reserved id by its name alone. An effect matches when its `target`
names the owner (by id or any of its tags) and each optional coordinate it sets agrees: both empty
matches everything the owner has, either one narrows, both name one entry exactly — the Effect
address mirrors the `produces` entry's coordinates. `currencyId` matches an id or a tag, exactly as
`target` does, so "every rate entry paying an income currency" is one effect rather than one per
currency — and a currency stays out of it by not declaring the tag (§8.2's fans rule). **Where matches are gathered from is two
explicit stages**, which is what keeps sibling scopes isolated (§12.3):

```
sourceContribution = base × source-targeted effects   gathered from the SOURCE's scope → root
currencyTotal      = Σ sourceContributions
                     × currency-targeted effects      gathered from the CURRENCY's home → root
```

A currency-targeted effect must therefore be declared at the currency's home scope or an ancestor
(validated, §12.12); a descendant scope wanting a local boost targets its producer, generator, or a
source tag instead. That is the entire modifier system.

### 12.3 Scopes: state containers

A **scope** is a plain state container. Content declares, per scope: its currencies, flags, bar
groups (which own their bars), **producers**, generators, upgrades, modifiers, career effects,
triggers (§12.5), and (for tiers) its rung. **Every content family is declared somewhere** — that is
what makes a reference resolvable by walking outward, and it is why there is no catalogue to look
anything up in. Events join when §12.8 lands, declared on `InteriorDefinition` rather than the base,
since root cannot host one: the definition is placed, the record is a fact of the host. Songs are
undecided until Chapter 6 (§7) — a `SongDefinition` may not exist at all, since a song is written at
runtime rather than authored. A producer's `produces`
entries are live exactly while its declaring scope belongs to the foreground chapter's **live
subtree** — activation is placement, several sibling or nested scopes participate in the same tick,
and each contribution resolves its facts and effects outward from its own declaring scope to root,
never crossing into siblings. The tick enumerates rates from that subtree's declared producers and
generators, never from what the UI happens to show. Runtime state per scope:

```csharp
class ScopeFacts   // the COMPLETE mutable state — nothing lives outside these payloads (§12.10)
{
    Dictionary<string, BigDouble> balances;
    Dictionary<string, BigDouble> earnedTotals;     // per currency, same home as its balance
    Dictionary<string, int>       generatorCounts;
    HashSet<string>               flags;
    HashSet<string>               purchasedUpgrades;
    HashSet<string>               firedTriggers;    // one-shot trigger latches — a reset re-arms (§12.5)
    Dictionary<string, BigDouble> barProgress;      // uncapped — overfill is allowed
    Dictionary<string, int>       fillCounts;       // repeating bars
    Dictionary<string, HashSet<string>> activeBars; // per group
    Dictionary<string, int>       modifierStacks;   // AddModifier grants, keyed like every other count
    List<TimedBuff>               timedBuffs;       // {buffId, expiresAt} — Encore lives at root
    List<SongEntry>               songs;            // tier = the run's Catalog; root = Discography (§7)
}

class InteriorFacts : ScopeFacts     // every scope inside another: chapters and tiers
{
    ActiveEvent                   activeEvent;      // at most one per host (§12.8): a field, not a list
}

class RootFacts : ScopeFacts        // the root scope's payload, and no other
{
    Dictionary<string, int>       roadieAllocation; // chapterId → stationed count (§8)
    HashSet<string>               entitlements;     // store-written (backstage_pass)
}

class ChapterFacts : InteriorFacts  // a chapter's payload
{
    PendingClaim                  pendingClaim;     // the idle dialog's claim (§9)
}

class TierFacts : InteriorFacts     // a tier's payload; nothing beyond what hosting brings
{
}
```

`ScopeFacts` and `InteriorFacts` are abstract - a payload is always one of the three leaf types, so
no scope holds a payload by default.

A scope that cannot use a fact does not carry it. **A scope is authored as one of three kinds** -
`RootDefinition`, `ChapterDefinition`, `TierDefinition`, with `InteriorDefinition` the abstract
middle the last two share - and the definition builds its own state node, so nothing infers a
scope's kind from where it sits in the tree. Each state class names one payload type, which makes a
root fact on a tier unrepresentable rather than filtered at load, and the exclusion runs the other
way too: root cannot host an event and so does not carry the record. The rule the placement follows:
**if a kind of scope cannot have a feature, its definition declares no data for that feature** - the
rung lives on `InteriorDefinition`, not on the base with a validation check behind it.
`lastActiveUtc` (§12.9) is a chapter field OUTSIDE the payload — the one thing a reset re-stamps
instead of clearing. A reset installs a fresh payload of the same type.

The tree: **root** (Records, Roadies, entitlements, completion flags, Discography, any counters a
chapter's curves read (§8.1)) → **chapters** → **tiers**, and tiers may nest. **Ids are unique along a CHAIN** — a read walks
outward and stops at the first scope that declares what it names, so two sibling chapters may each
declare their own `cash`; what must not repeat is an id visible from one scope. Scope ids stay
unique tree-wide, since the facts that name a scope key by them - a claim line's home, root's
roadie allocation map - and a saved name has to be unambiguous wherever it is read back. A
declaration in two scopes is refused at load. Flag reads walk the chain outward (set anywhere on it = set); `SetFlag`
writes to the flag's declared home.

**Flags are strings on purpose.** Everything else a scope declares is an asset, and everything that
names one holds a direct reference (§12.14) - but a flag has no data beyond its own existence, so
there would be nothing inside a `FlagDefinition` to author. The cost of that choice is that a flag
name is the one authored reference a typo can break, which is why `SetFlag` and `FlagSet` are
validated against the acting chain and `SetFlag` throws at runtime on a name no scope on the chain
declares. Tags and stat names are strings for the same reason: neither is a thing, both are
vocabulary.

**A scope is addressed by reference too.** An action or condition that names a scope holds the
`ScopeDefinition` itself, and getting from it to live state is a walk that compares assets, not
ids: there is one state node per definition and neither points at the other, so the walk is the
only link, but what it matches on is the reference the caller already holds. The only scope NAMES belong
to stored facts (a claim line's home, the roadie allocation map), because a save holds text and
nothing else: a name enters at the load boundary, resolves there, and travels no further. A scope
id is therefore never how running code finds a scope, which is what keeps tree-wide id uniqueness a
property of the save rather than a dependency of the economy.

**Structure encodes reset relationships:**
- **Nest** tier A inside tier B when "resetting B always resets A" is definitionally true (a ladder).
  An intermediate currency banked by A's rung is declared in B — exactly where B's reset claims it.
- **Siblings** when tiers reset independently.
- A rung that occasionally resets several siblings lists several `ResetScope` actions.
- Resetting an outer scope while keeping an inner one alive is unrepresentable (`ResetScope` is
  downward-closed) — wanting it means the scopes should be siblings.

Reset = a rung firing. Its actions run in order, payouts first so they read the dying state, and
the clear is the last action in that list - `ResetScope` is an ordinary action the author places
there, never something the rung does on its own. Clearing is complete by construction - a field added to ScopeState next month is cleared
because it is in the struct. Nothing re-projects, settles, or rebuilds, because nothing derived was
stored. `lastActiveUtc` is re-stamped, not cleared, so a fresh chapter owes no idle.

### 12.4 Conditions

One polymorphic family for every gate, unlock, visibility, and pressability rule:

```csharp
public abstract class Condition
{
    public abstract bool Evaluate(GameContext ctx);   // pure — reads state, stores nothing
    public virtual void Validate(ContentDatabase db) { }
}
```

Kinds: `CurrencyAtLeast`, `EarnedTotalAtLeast`, `OwnedCountAtLeast`, `FlagSet`, `UpgradePurchased`,
`BarsCompleted`, `EventRewardPending`, `EventRecordExists`, `Always`, `All`, `Any`, `Not` (the story gate
`chapterN_complete && !storyN_seen` is
`All[FlagSet, Not[FlagSet]]`), plus a formula-threshold kind (threshold computed from state — e.g.
a scaling goal or reward curve reading a per-clear counter, §8.1). Records need no special kind:
they are a currency.

The two event kinds read a named host's record state, holding a direct scope reference like
`ResetScope`: `EventRecordExists(host)` - any record
(running, expired-undismissed, or goal-reached-undismissed); `EventRewardPending(host)` - a record
whose `goalReached` is set. Both are pure fact reads: nothing evaluates a goal at read time. Unlike ordinary reads they may name the acting scope **or a scope it encloses** - their
guard use is a rung refusing to reset over a pending reward (§12.12), and a guard must see the
hosts its own reset closure contains. Composed with `Not`, they are how a rung disarms while an
event runs or a reward waits; the player is never wedged, because dismissal stays one tap away and
always legal - it clears the record and pays the reward if the goal was reached.

A condition may carry an optional **`uiText`** label ("Needs 50 fans", "Claim your event reward
first") — pure presentation data, never read by evaluation, rendered by the rung feedback
contract (§12.11).

**Every condition evaluates — and every action list executes — in an explicit scope**, supplied by
`GameContext`, never inferred and never inherited from a caller: a rung, upgrade, bar, or trigger
in its declaring scope; an event in its host scope; a `produces` entry in its producer's declaring
scope; a UI section or module in its authored `scopeId` (§12.11). Nested invocations **rebase** the
context to the owning object's scope — `ExecuteRung` runs the referenced rung in *that rung's*
declaring scope, which is how the capstone's rung legally reads tier-owned Fans. Reads walk outward
from there (§12.12).

Authoring guidance: gate persistent UI on monotonic facts (flags, earned totals), never on spendable
balances (§2). Serialized via `[SerializeReference]` in assets, or a `"type"` field mapped to the
class at import — a kind can never be authored without code behind it.

### 12.5 Actions, rungs, and formulas

**Action** — one polymorphic family for "something happens at a moment":

```csharp
public abstract class Action
{
    public abstract void Execute(GameContext ctx);
    public virtual void Validate(ContentDatabase db) { }
}
```

Kinds: `AddCurrency` (one or more target currencies paid from a single evaluation; amount constant
or from a `PayoutFormula`), `AddModifier(scope, modifier)`, `RemoveModifier(scope, modifier)`,
`SetFlag(flagId)`, `AddSong`, `ResetScope(scope)`, `ExecuteRung(tier)` and `RestartScope(scope)`
(fire that scope's rung through its own gate, then clear it - the restart idiom as one action; a
bare `ResetScope` remains for a pure wipe). The event lifecycle operations are commands rather than
Action kinds (§6.1, §12.11). Authored inline via
`[SerializeReference]` wherever needed — upgrade payloads, bar completions, event rewards, rungs.
No shared reward pool; a reused reward can be promoted to a shared asset later if duplication ever
hurts. Actions are one-shot: they run at their moment and are never replayed on load — the state
they mutated is what gets saved.

**`ResetScope`** clears the referenced scope and everything inside it (downward-closed). It only clears —
it never executes nested lists — so no recursion exists via resets.

**`AddModifier`** counts a stack under the modifier's id in the target scope's
`modifierStacks`. A modifier is declared content like everything else — `ScopeDefinition.modifiers` —
and a grant may only name one the target scope can reach outward, so the read resolves the stored id
by the same walk every reference gets. The numbers stay in the `ModifierDefinition` — a named `List<Effect>` with a
**`stacking` enum: `Replace | Linear | Multiply`**. `Replace`: a re-grant keeps count at 1;
`Linear` / `Multiply`: a re-grant increments count, and the name picks the count-scaling formula
(`1 + (m−1)·n` vs. `m^n`) — duplicate-grant policy and growth are one closed choice. The entry is
the fact, saved and cleared with its scope. Reserved for grants from
*moments that leave no other trace* (an event reward); when a count already exists as state, derive
from it instead (§12.6). **`RemoveModifier`** is its exact inverse: decrements one stack, deletes
the entry at zero, no-ops when absent.

**A rung** is `{offerCondition, List<Action>}` — the one shape behind the album release (declared
on its tier) and the capstone (declared on the chapter): a rung of the prestige ladder. Event
lifecycle commands are **not**
rungs - they execute authored `onEntry` / `rewards` / `onEnd` lists through the same action machinery
(§6.1). Every invocation
is **fail-closed against the rung's own gate**: `TryRung` (the UI entry point) checks the offer
condition before executing, and **`ExecuteRung` runs another rung's action list through the same
check — gate met, it executes; gate unmet, it no-ops.** There is no bypass: a payout is only
reachable through its own gate, so an unfinished run is discarded by whatever reset follows, never
banked. References are validated acyclic at load. No rung may be authored on the root scope.

**PayoutFormula** — a polymorphic family computing an amount from readable state
(`floor((fans/5)^0.5)`, piecewise diminishing-returns curves). Pure functions, so UI previews call
the same code the rung runs.

**Trigger** — the one sanctioned condition-observer: `TriggerDefinition {id, condition, actions}`,
declared per scope. A trigger firing is a *moment*: when its condition holds and its id is not in
the scope's `firedTriggers`, it **latches the id first, then executes its actions** — the same
discipline as `DismissEvent` removing the record before it runs a list, and it makes self-resetting triggers correct for free: an action
list that resets the declaring scope clears the just-written latch, re-arming the trigger for the
new life. The latch is a stored fact, so nothing derived is stored. **One-shot per scope-life,
never repeating**: the reset that clears the declaring scope re-arms it (a tier trigger fires once
per run; a root trigger once ever) — lifetime is placement. Repeating behavior is a producer or a
bar, never a trigger. Swept at the two refresh moments (§12.9) in a single pass, in
**deterministic order — scopes in tree order (parent before child), triggers within a scope in
declaration order** — whose **eligibility is a sweep-start snapshot**: conditions are evaluated
against the state as the sweep
began, so a trigger armed by an earlier trigger in the same pass fires at the *next* sweep, never
this one — no fixpoint — and a trigger whose declaring scope was reset during the sweep does not
execute against the replacement scope-life. Never evaluated while its scope is dormant: a threshold
crossed during idle fires on the first live sweep after switch-in, reading present state — nothing
is replayed or backdated. Trigger action lists pass every list validation (§12.12). An invisible
auto-finishing challenge is a trigger, not an event — events keep claimed completion.

### 12.6 Where effects come from

`GetMultiplier(owner, currencyId, stat)` gathers, from every scope on the chain outward:

| Source | The fact (stored) | The effects (derived on read) |
|---|---|---|
| Purchased upgrades | `purchasedUpgrades` set | the upgrade definition's `List<Effect>` |
| Owned generators | `generatorCounts` | `produces` entries scaled by count (contributions, not effects) |
| Timed buffs (Encore) | `{buffId, expiresAt}` list | the buff definition's effects while unexpired |
| Active events | an `ActiveEvent` record exists | the event definition's handicaps |
| Granted modifiers | `modifierStacks` counts | the `ModifierDefinition`'s effects, per its `stacking` enum |
| Repeating bars | `fillCounts` | the bar's `perFill` effects applied count times, read through the declaring scope's `barGroups` |
| Career facts | Records balance, Roadie allocation, songs this run, entitlements | a `CareerEffectDefinition`'s `MultiplierFormula`, computed on read (§3/§7/§8) |

A `MultiplierFormula` computes against the **gather-origin** context - the source's scope in stage 1,
the currency's home in stage 2 - never one rebased to where the effect is declared. This is a
deliberate asymmetry with §12.4, where conditions and action lists evaluate in their declaring scope:
a multiplier is addressed to a NUMBER, and the number's identity includes the chain it resolves on.
A root-declared formula that reads chapter state is unimplementable any other way. Reads stay
chain-only either way, so no sibling reach opens up - and it is why an effect whose formula depends
on the producing chapter must target the SOURCES rather than the currency total (§8.2).

The fill-count row is REPEATING bars only: a non-repeating bar's completion is a moment that leaves
no count to scale, so its reward is an `AddModifier` grant and a `perFill` entry on one is an error
(12.12). Like the upgrade row, the read walks the declaring scope's own list, so a stray count for a
bar that scope never declared cannot contribute.

Every row is the same pattern: a fact in state, effects computed from it. All of these exist from the
first minute of the game and contribute 1× until their facts exist. Nothing in this table is ever
serialized except the facts column.

### 12.7 Bars

Generic fillable bars: pacing bars (learn covers), repeating currency bars, cascade bars.

```csharp
class BarDefinition : Definition
{
    CurrencyDefinition fillCurrency; // what it drinks; null = fills from time alone
    BigDouble    fillAmount;
    BigDouble    fillRate;        // this bar's own fill speed (units/sec)
    bool         repeating;       // fill → fire onComplete → reset to 0 → go again
    Condition    availableWhen;
    List<Action> onComplete;      // fires at each threshold crossing
    List<PerFillEntry> perFill;   // cascade: {effect, growth}, applied fillCount times on read
}

class BarGroupDefinition : Definition
{
    int             maxActive;
    List<BarDefinition> bars;     // the group OWNS its bars: membership is placement
}
```

**A group owns its bars**, so a bar's declaring scope IS its group's: the progress fact and the
`activeBars` set that selects it have one home and one lifetime. That is also what makes the
settlement order below literal, and it gives `SetActiveBars` its membership test for free.

**A group holds bars and caps how many run at once.** That is its entire job. It owns no currency
and no throughput: a bar names what it drinks and fills at its own rate, and throttling a bar is that
rate rather than a second cap over the set. A bar with no `fillCurrency` fills from time alone, which
is the whole of the distinction a behavior class used to carry.

**Owning no number, a group is not an effect target.** `maxActive` is an int and stays outside the
multiplier system - nothing addresses it - and there is nothing else on a group to multiply. Buffing
a set of bars is a TAG they share, which is the mechanism that already fans one effect out to many
owners; a group target would instead mean "all my members", a second kind of target resolution the
vocabulary does not need.

**A bar's rate resolves stage 1 ONLY**: `GetMultiplier(declaringScope, bar, bar.fillCurrency, rate)`,
with no currency stage. Stage 2 is "effects on this currency's total production", and a bar CONSUMES -
letting a currency-total buff through would speed the drain as well as the supply, which is not what
either buff means. The bar's own currency is passed as the coordinate, so an effect may narrow to it
(`{target: cover_1, currencyId: rehearsal}`); a bar that fills from time passes none, which no
narrowing effect matches.

**A null `availableWhen` on a bar is OPEN**, the opposite of a purchase gate: fail-closed binds entry
points that create value out of a spend, and a bar's availability is a selection filter.

**A bar drinks what it names.** Each active, available, unfilled bar wants `fillRate × dt` and takes
what is there, in the deterministic order below. When a pool cannot cover its bars, the ones later in
the order simply stall - which is correct feedback, not unfairness: the amount delivered per second is
the inflow whatever the split, so dividing it proportionally would deliver no more, it would only
replace one bar visibly filling with three inching. Per-bar speed is buffable by bar id or tag, and a
buffed bar drinks proportionally faster.

**Selection is state**: `activeBars` per group; empty = the pool accrues and nothing drains.
`SetActiveBars` is **fail-closed** like every entry point (§12.11): it rejects a set that exceeds
`maxActive`, names a bar outside the group, names an unavailable bar (`availableWhen` false), or
names a completed non-repeating bar. On completion the stream **stops** - choosing is the mechanic in
Ch. 1, and letting more run in parallel is `maxActive` rising, which is an unlock rather than a
multiplier.

**Completion is derived**: complete ⇔ `progress ≥ fillAmount`. For a non-repeating bar, progress is
monotonic until reset and **uncapped** — overfill is allowed and readable — so the crossing happens
once, and that is when `onComplete` fires. No completed-set is stored; `BarsCompleted(n)` counts
bars at full. **Repeating bars settle a tick iteratively**: while `progress ≥ fillAmount` and the
bar is still active and available, subtract `fillAmount`, increment `fillCount`, execute
`onComplete` — re-reading state each iteration, so a completion action that resets the host or
flips availability stops the loop honestly instead of executing precomputed fires against a changed
world. Residual progress is retained; a buffed rate crossing several thresholds in one tick pays
every crossing. (The arithmetic shortcut `fires = floor((progress + Δ)/fillAmount)` is a valid
optimization only when `onComplete` cannot affect the bar's environment, which in practice means an
EMPTY list; a bar with actions iterates.)

**The snapshot admits; live state may only disqualify.** Every rate and gate the draw needs is
resolved BEFORE the segment's production deposits (12.9), and only the bars that snapshot
recorded as drawing enter settlement at all. The per-iteration re-reads may take a bar OUT of the
loop; they may never put one in. Without that asymmetry the seam has a second door: a repeating bar
can sit at full progress with its gate closed - its own `onComplete` shut it last segment and the
residual is retained, and a save can load in that state - and a live-only test would let this
segment's deposits open the gate and pay the whole backlog. Both the draw and the
settlement run in one deterministic order — scopes in tree order (parent before child), then groups,
then bars within a group, in declaration order — and a reset during settlement invalidates the
remaining completions from the old scope-life, exactly as the trigger sweep rule (§12.5).

**Cascades** (bar B buffs bar A per fill): B declares `perFill: [{effect, growth}]` — e.g.
`{{target: barA, ×1.05}, multiply}`; the applied count is B's `fillCount` — the same pattern as
generator contributions scaling by `ownedCount`. Growth lives on the carrying entry, never on the
Effect atom: `multiply` (m^n) or `linear` (1 + (m−1)·n) — the same growth vocabulary
`ModifierDefinition`'s `stacking` enum uses for granted stacks (§12.5). **`linear` saturates at
zero.** A multiplier below 1 is legal - a debuff that decays linearly is an ordinary thing to author
- but the formula crosses zero once n > 1/(1-m), and a negative factor would run production
backwards; a negative yield reaches the deposit path, which would drive an earned total DOWNWARD.
Reduced to nothing is a semantic; reduced past nothing is not. The rule binds both consumers of the
vocabulary: cascade entries and granted stacks.

### 12.8 Events (runtime)

Defined in §6.1. The definition is declared on its host scope like every other content family, so
`StartEvent` holds the reference and the stored record's id resolves by walking outward (§12.3).
Runtime is one record per host scope - **at most one**, and the state schema holds a single record
rather than a list, so a second is unrepresentable rather than filtered at load. `StartEvent`
rejects a host that already holds a record, running or expired-but-undismissed.

`goalReached` latches the moment the goal holds while the attempt has not expired, and one rule
covers timed and untimed alike. Two callers take that latch, because neither covers the other. The
tick's timer phase latches before it decrements, which is what makes the tie - a goal met in the
same segment the timer expires - go to the player, since the sweep runs after the last segment when
the record already reads zero. The sweep (inside every transaction, tick and command alike) latches
too, judged on the sweep-start snapshot and before any trigger actions execute, which is what
catches a goal reached by a command outside any tick.

`StartEvent` / `DismissEvent` are the two self-guarding commands. Start checks `availableWhen` and
the empty host, runs `onEntry`, then creates the record - after the list, so an `onEntry` that
resets the host puts the record in the fresh payload. Dismiss checks that the host holds a record
**for the named event** - a sibling's record is an ordinary refusal, not a licence to pay this
event's reward - then **removes it first**, then runs `rewards` if `goalReached` was set, then runs `onEnd` either way.
Removing first is what opens a rung gated on `Not(EventRewardPending(host))`, so an `onEnd`
carrying `[RestartScope(tier)]` banks instead of silently no-oping. Nothing in an
action list can observe the record's absence: handicaps multiply production, and rewards read
balances. Record removal is therefore the operation's job and never the author's.

Any reset that reaches the host kills the event - lifetime is placement. An expired, goal-unreached
record **persists until the player dismisses it** or a reset reaches the host - nothing expires it
away automatically, and while it sits there the host stays occupied and the handicaps still apply.
Expiry does exactly one thing: it stops the goal from latching. Handicaps apply purely by a record
existing (§12.6). Nothing installs, so nothing tears down.

### 12.9 Idle (runtime)

Per §9: the foreground chapter's live subtree ticks (§12.3) — on scaled time, `effective dt = real dt ×
GetMultiplier(game_speed)`, with wall-clock decrements (event timers, buff expiries) on real dt. A
tick that crosses an expiry timestamp (buff or event) is **segmented at it** — each segment
resolves with the multipliers live in that segment, so update order can never hand a whole tick the
wrong multiplier. Within each segment the economy phases are **fixed**, resolved from a **start-of-segment snapshot
of effects and entry conditions** — every rate entry is judged and sized against pre-deposit state
before any deposit lands, so definition order never changes production: rate production deposits
(pool currencies included) → bar
consumption (§12.7) → iterative bar completion — production before
consumption, so an empty pool fed at +1/sec serves a 1/sec bar demand in the same tick. Bar DEMAND
is resolved BEFORE the deposits, from the same start-of-segment snapshot as everything else; the pool
BALANCE is the one thing read live, which is exactly what that carve-out says. Resolving demand after
the deposits would let a segment's own production open a bar's gate and draw for the whole dt — then wall
clocks advance to the segment boundary for every running timer in the swept set - root plus the
foreground chapter, the same set the sweep walks, so a root-hosted event's timer advances and a
dormant chapter's does not. No event can be born mid-segment now that starting one is a command
rather than an action. A handicap or buff live at segment start governs the whole segment, expiring
only at its edge. After the last segment: the sweep, commit, and refresh
(§12.11).
`lastActiveUtc` stamped on switch-away and, for the foreground chapter only, on
save. Switch-in computes
`rate × min(elapsed, cap) × idleRate` per currency at current rates — skipped below the minimum-away
threshold and skipped entirely while that chapter holds a record for an event that blocks idle
(§6.1: `blocksIdle` is derived from the event carrying a timer, and the idle path asks the event
rather than inspecting one) - and stores it as the
chapter's pending claim for the idle dialog; deposit on dismissal, ad-doubling at claim time.
The claim is an exactly-once transaction — `{claimId, amounts: [{scopeId, currencyId, amount}],
doubled, settled}`: a line names its HOME, because a claim held at the chapter addresses currencies
homed at tiers below it, and nothing resolves a name downward. The ad callback
marks `doubled`, deposit flips `settled`, and replaying either after an app kill is idempotent by
`claimId`. `idle_rate`, `idle_cap`, and `game_speed` resolve through `GetMultiplier` like
everything else. Triggers (§12.5) are swept inside each transaction — after its mutation, before
commit — for ticks and commands alike; live scopes only, single pass. The same sweep observes
event goals **from the sweep-start snapshot and latches `goalReached` before any trigger
actions execute** — success is judged on the transaction's own mutation, never on trigger
payloads; a trigger that spends the goal currency changes nothing already secured, and its effect
on goals is seen next sweep (§12.8).

**GameSession** — the transient execution context, never serialized:
`{foregroundChapterId, phase: NoChapter | AwaitingIdleClaim | Live, commandInProgress}` — launch
and backgrounding are `NoChapter`, so no-foreground states are explicit rather than a null id.
Durable facts live in the tree; the session holds only orchestration. The chapter to reopen at launch is a
**non-authoritative UI preference**, never inferred from economy timestamps — `lastActiveUtc`
records idle-settlement boundaries, not UI history. While `phase == AwaitingIdleClaim`, only claim
and switch commands are legal — mutating commands are refused, so a rung or automation can never
reset away an unsettled claim; settling flips the session to `Live`. Authenticated ad/store
callbacks are always **phase-eligible, never reentrant**: a callback is a serialized mutation
transaction — queued behind `commandInProgress`, then mutation → sweep → commit → one refresh like
any command — so marking the pending claim `doubled` repaints the dialog even while no ticks run.
Callbacks are not UI commands (§12.11). **The session also draws the
command boundary**: **every chapter-local mutation** — `TryBuy`, `FireProducer`, `TryRung`,
`SetActiveBars`, the event operations, the song operations, and any future mechanic command — is
rejected when its owning scope lies outside the foreground chapter's live subtree; ids are unique
tree-wide, but reachable is not the same as mutable. Root-owned commands
(`SetRoadieAllocation`, `AcknowledgeStory`) and the session commands (`SwitchChapter`, `ClaimIdle`)
are the exceptions. This guard is orchestration — it lives here, never in chapters or scopes.
**Switching away settles first**: the switch transaction deposits the outgoing chapter's unsettled
claim at its undoubled value (switching is an exit path, §9) before stamping out, so pending
dialogs never accumulate across chapters.

### 12.10 Save

Serialize the ScopeState tree, nested as the scopes are — **and nothing else**: every root fact
(Records, Roadies, the allocation, entitlements, timed buffs, Discography) is a field of the root
scope's state (§12.3), so principle 2 is literally the save format. JSON + checksum, validated on
load (ids resolve; unknown ids from removed content are dropped with a warning). No grants, no
derived values, no replay of actions. Idle payouts are capped client-side.

A schema version becomes a compatibility contract the moment a build that can write it reaches a
player. Before that, format changes revise the current version in place and dev saves are deleted
rather than migrated; after it, every format change bumps the version and registers a migration.

Production hardening: the save carries a `schemaVersion`, and loading an older version runs explicit
per-version migrations — never silent best-effort parsing. Writes are atomic (write temp, verify,
swap) and keep the previous save as backup; a checksum failure falls back to it. A negative clock
delta (device clock moved backwards) clamps elapsed time to zero — rollback can extend a buff's
`expiresAt` wait, but it never mints currency.

### 12.11 UI

**Authored layout**: a chapter's screen is an ordered list of `SectionDefinition {visibleWhen,
scopeId, modules}`; a `ModuleDefinition {prefabId, contentId, visibleWhen?, scopeId?}` binds a
widget prefab to content by id. A section's `scopeId` is its **evaluation scope** — the context its
conditions read from, which is how a chapter-owned section legally gates on a tier-declared flag; a
module defaults to the home scope of its bound content **when that home lies within the chapter's
subtree, else to the chapter itself** (root-owned content like Records is readable from any
context — root is on every chain). Validated at load: the chapter itself or one of its descendants. A `ModuleRegistry` maps prefab ids via Addressables — a new widget type is
a prefab plus an entry. Sections live on the `ChapterDefinition`.

**Refresh** is coarse, on two triggers: after each tick of the foreground chapter, and after every
**completed command transaction** (nested Actions never refresh individually — the outer
transaction publishes one final state change, so a rung's payout, flag, and reset render as one).
The full pipeline is fixed: **mutation → one trigger sweep → trigger actions → transaction commit →
one refresh** — the sweep runs inside the transaction, so a trigger's payload renders atomically
with the command that armed it.
Visible sections re-evaluate `visibleWhen`; visible modules re-read what they show. Event-driven,
never per-render-frame; fine-graining is a mechanical optimization if profiling ever asks.

**Entry points** — the only ways the UI touches the game. Each mutation that depends on live state
is a `Can*` query plus a `Do*` command, with `Try*` as the convenience wrapper over the two: the
query is what renders pressability and the feedback text, the command performs the mutation and
refuses to run when the query says no. A reference that cannot resolve is never an answer either
one returns - static content cannot legitimately be in that state, so those throw. The set:
`IsOffered(rung)` / `ExecuteRung` / `TryRung(rung)`, `CanBuy` / `Buy` / `TryBuy(generator |
upgrade)`, `FireProducer(producer)`, `SetActiveBars(group, set)`, the event operations
`StartEvent / DismissEvent (event)`, `SwitchChapter(chapterId)` (stamps
`lastActiveUtc`, computes the pending claim, §12.9), `ClaimIdle(chapterId)` (settle the pending
claim, §9 — the dialog's double button only *requests* the rewarded ad; marking the claim `doubled`
is AdManager's authenticated callback, never a UI call), `SetRoadieAllocation(map)` (nonnegative integers, Σ ≤ owned Roadies, unlocked chapters only), the Ch. 6 song operations (write / name), and
`AcknowledgeStory(storyId)` (sets the root `storyN_seen` latch, §10). All fail-closed — each checks
its own gate. Ad and store
callbacks (AdManager / IAPManager) mutate through their own equally fail-closed operations (extend
a buff, mark an idle claim doubled, write an entitlement, grant Roadies) — they are not UI paths.

**A disarmed rung explains itself**: the rung-button widget evaluates its gate's top-level legs
individually at the two refresh moments and lists the unmet legs' `uiText` (§12.4); threshold
kinds additionally expose current/target so a leg can render as progress ("37/50 fans"). The
widget reads the same condition objects the operation enforces — one implementation, no drift,
exactly as payout previews call the rung's own formula (§5). Feedback is per-condition, never
per-feature: any kind an author gates with explains itself for free.

**Widgets interpolate** displayed numbers and bar fills between ticks; presentation only.

### 12.12 Validation at content load

- Every referenced id resolves (currencies, flags, generators, modifiers, scopes, tags in targets).
- Every id is unique along a CHAIN — currencies, flags, bars, groups, producers, generators,
  upgrades, events, triggers, modifiers, songs; a declaration in two scopes is refused, and sibling
  subtrees may reuse an id freely because neither can see the other. Scope ids stay unique tree-wide.
- A tag may not collide with any id; an Effect target matching nothing reachable warns.
- A `SetFlag` naming an undeclared flag is an error, as is one whose home is off the acting chain -
  including a home the acting scope encloses, which the outward write can never reach (§2). A
  declared flag with no setter warns.
- A rung that resets a scope containing tier rungs with unreferenced payout actions warns
  (stranded value); a formula-driven grant placed after a `ResetScope` that clears its inputs warns
  (reads zeros); reference cycles across ALL nested action references - `ExecuteRung`,
  `RestartScope`, and trigger lists - are errors; a rung on the root scope is an error.
- A rung whose reset closure contains an event host, and whose offer condition carries no REQUIRED
  `Not(EventRewardPending(host))` - the whole condition, or a conjunct reached through `All` alone -
  warns (stranded reward: an armed, unclaimed reward would die with the record). Requiredness is the
  test, not the mere presence of the leg: a positive leg means the opposite, and one under an `Any`
  is satisfied by its sibling branch. A warn, not an error: resetting over cheap disposable events
  is authorable on purpose. `EventRewardPending` / `EventRecordExists` reach is validated like every
  scope reference: the acting scope or a scope it encloses.
- Scope references are checked for reach: `ResetScope` may target the acting scope, a scope it
  encloses — never a peer, the root, an ancestor, or an unrelated subtree. Peers are cleared by the
  scope that CONTAINS them, since resetting it is downward-closed. `ExecuteRung` may
  only reference a rung declared within the acting scope. `AddModifier` and `RemoveModifier` may
  target the acting scope or an ancestor (grants live outward), never an unrelated subtree; a
  `RemoveModifier` naming a modifier nothing reachable grants warns.
- Ordinary reads and writes (`AddCurrency`, `SetFlag`, `AddSong`, Condition reads, `produces`
  targets) may address only the acting scope's chain — itself or an ancestor. The runtime state
  walk cannot reach siblings, so a cross-tree reference is a load-time error rather than a silent
  runtime miss.
- Effect reach is validated for every target kind (§12.2): a currency-total effect must be declared
  at the currency's home scope or an ancestor; an exact source target (producer, generator, bar)
  must be declared at the target's scope or an ancestor — a sibling-declared effect resolves
  at load but the target's outward walk never visits it, so it is an error, not a warning; a tag
  target must match at least one member within the effect's declaring scope's subtree.
- A bar group's `maxActive` is at least 1, and it lists no null bars. A bar's `fillAmount` and
  `fillRate` are positive - a nonpositive threshold is an unbounded settlement loop, and a zero rate
  is a bar no multiplier can move - and a named `fillCurrency` must be reachable on its own chain
  (none is legal: it fills from time). `perFill` on a NON-REPEATING bar is an error (12.6). A
  repeating bar carrying `perFill` records its fill-count write ahead of `onComplete`, so a
  completion list resetting the scope that homes its own count trips set-then-wiped. An Effect
  targeting a bar GROUP warns: a group owns no number for a gather to reach. `BarsCompleted` reaches
  like every other scope-attached read: the group's declaring scope is the acting scope or an
  ancestor.
- An action list that sets a fact and later resets the scope declaring it errors (set-then-wiped —
  e.g. an event's `event_tierN_done` flag must be declared outside the scope its own `onEnd`
  resets).
- A balance goal on an event whose `onEntry` never resets the host scope warns.
- An event declared on the root scope is an error: its handicaps would gather into every chapter's
  outward walk and its occupancy would be global, and an event is a challenge inside a chapter
  (§6.1). The state agrees - the record lives on a payload root does not carry (§12.3).
- A gate may not be null: a generator's or event's `availableWhen`, an upgrade's gate, a rung's
  `offerCondition`, a trigger's condition. An unauthored gate is dead content, and `Always` is how
  an author says the gate is open. Filters keep their optional conditions - a bar's `availableWhen`
  and a `ProducesEntry.condition` both default to active, because neither is a gate.
- A polymorphic kind in data with no class behind it is an import error.

### 12.13 File layout

```
Assets/Scripts/
  Core/
    GameManager.cs          // bootstrap, save/load, chapter switching
    GameSession.cs          // transient orchestration: foreground chapter, phase, command guard — never serialized
    TickSystem.cs           // fixed-interval tick on real (DateTime) time
    BigNumber.cs            // wraps break_infinity.cs
    Definition.cs           // base: id + tags, declared once for every content family
    ContentDatabase.cs      // loads the root scope, and with it the whole graph; runs the §12.12 pass
    ScopeDefinition.cs      // the base, InteriorDefinition, and the three authored kinds
    ScopeState.cs           // the base, ScopeState<TFacts>, the three state classes and their payloads
    Condition.cs  Action.cs  PayoutFormula.cs  Trigger.cs   // the class families (+ kind classes)
    Effect.cs               // the flat struct
    GameContext.cs          // read access for Evaluate/Execute: state chain + defs
  Economy/
    CurrencyDefinition.cs  ProducerDefinition.cs
    Producer.cs             // stateless resolution: Σ matching produces entries × Π multipliers + GetMultiplier
    GeneratorDefinition.cs  UpgradeDefinition.cs
    Purchasing.cs           // TryBuy(generator | upgrade): fail-closed gate, spend, count or latch, payload
    CareerEffectDefinition.cs  // formula-shaped multipliers + the MultiplierFormula family
    ModifierDefinition.cs   // named List<Effect> + stacking enum (Replace|Linear|Multiply)
    BarDefinition.cs  BarGroupDefinition.cs  BarSystem.cs
  Loop/
    ChapterManager.cs      // forward-only advance, reacting to root completion flags
  Events/
    EventDefinition.cs  EventSystem.cs
  Meta/
    RoadieAllocation.cs    // SetRoadieAllocation; the boost arithmetic is Economy's
  Content/
    SongDefinition.cs      // Catalog (run) + Discography (root)
  Save/
    SaveSystem.cs          // JSON + checksum; serializes the ScopeState tree
  Monetization/
    AdManager.cs           // rewarded only (Encore top-up + Double it)
    IAPManager.cs          // Backstage Pass, Roadie bundles, Tip Jar
  UI/
    SectionDefinition.cs  ModuleDefinition.cs  ModuleRegistry.cs
    Widgets/ (GeneratorRowUI, BarGroupUI, RungButtonUI, CurrencyHeaderUI, JamButtonUI, ...)
    NumberFormatter.cs  StoryBeatUI.cs  CollectScreenUI.cs  RoadieAllocationUI.cs
ScriptableObjects/  Chapters/  Currencies/  Generators/  Upgrades/  Events/  Bars/  Modifiers/  Songs/
```

### 12.14 Requirements

1. `break_infinity.cs` (BigDouble) for all currency and production values, wrapped in `BigNumber`.
   **The wrapper refuses NaN and infinity at construction** - every literal, conversion, arithmetic
   result, and value read back from a save passes through one constructor, so a poisoned number
   throws where it is born rather than spreading. The library deliberately PRESERVES both and every
   comparison answers false against a NaN operand, so no downstream sign check could catch one; that
   is why the invariant lives in the type and consumers never test for it. A corollary: arithmetic
   in raw `double` that then converts (a count times a multiplier, say) can overflow before the
   wrapper sees it, so mixed expressions convert FIRST.
2. Tick on real elapsed time (`DateTime.UtcNow` deltas), not frame time.
3. UI refresh on the two triggers of §12.11; no per-frame polling of balances.
4. Versioned, checksummed saves (explicit migrations, atomic write + backup), validate on load, cap
   idle earnings client-side.
5. Content in ScriptableObjects, reached by DIRECT REFERENCE from the root scope; Addressables
   carries the root, and the direct references bring the rest of the tree with it, so the whole
   graph is resident from boot. Per-chapter streaming would need indirect handles, staged
   validation, and staged state construction; it is a later architectural change if load time or
   memory ever argues for it, not a property of today's design. **Authoring is a
   JSON document per chapter, materialized into SO assets by an editor importer**; the assets stay
   fully hand-authorable — `[SerializeReference]` plus the subclass-picker drawer create and edit
   polymorphic kinds in the inspector. An authored reference is an object field: it cannot name
   something that does not exist, so only flags, tags, and stat names stay strings.
   **Re-import overwrites**: the JSON is the source of truth for a chapter it authored; content
   born in the editor is simply never re-imported over. (If round-tripping ever matters, a
   chapter/game exporter back to JSON is a later addition — deliberately not built now.) A
   polymorphic kind in data with no class behind it is an import error (§12.12); regular
   per-chapter gear curves can be generated at import.
6. Run the §12.12 validation pass at boot in development builds; fail loudly.
7. **A content fault throws, in every build.** Content is static and validated, so an unresolvable
   reference, a fact naming something no scope on its chain declares, or a number outside its legal
   range cannot happen against good content - reaching one means the content or the code is broken,
   and no sensible play continues from there. Release builds skip the validation pass, so these are
   the checks that catch it: they throw rather than logging and carrying on, because a skipped
   deposit or a silently smaller multiplier is saved as if it were real. Refusals ARE different: an
   unmet gate, an unaffordable cost, an unavailable bar are ordinary answers a `Can*` query reports
   as false and a `Try*` wrapper passes through (§12.11).
8. **No runtime lookup searches the tree.** A name is resolved by walking OUTWARD from the acting
   scope and stopping at the first scope that declares it. There is no catalogue, no id index, and
   no "find all X" pass. Two walks are legitimate, because both start from a scope the caller
   already holds: outward along the chain (resolution), and downward through ONE named subtree
   (aggregation like `GetRate`, and the downward-closed clear of `ResetScope`). What is forbidden is
   resolving a name from anywhere else - a global map, a scan of every scope, or any search that
   leaves the acting chain. Validation is the exception and the reason the rule holds: it audits the
   whole tree at load, once, which is what lets every runtime walk assume its own chain is enough.
   Content that seems to need a registry is content that has not been placed on a scope yet.

---

## Appendix — at a glance

- **Structure:** nested prestige on a tree of **state containers** — root / chapters / tiers (tiers
  may nest for ladders, sibling for independence). Lifetime is placement; ids unique per chain;
  everything derived is computed on read from stored facts.
- **Records:** the single permanent progression currency; each Record raises global income ~+2%; the
  capstone gates on Records earned within its chapter — a chapter-declared counter fed by the album
  payout and zeroed by the capstone's own reset.
- **Rungs:** the album release and the capstone are `{offerCondition, List<Action>}`; events are
  lifecycle commands executing authored lists (§6.1). Payout-before-clear is list order; `ResetScope` is a bare
  downward-closed clear; every invocation — `TryRung` from the UI, `ExecuteRung` from another
  rung — is fail-closed against the rung's own gate (an unmet gate no-ops; unfinished runs
  discard, never bank). The capstone resets the **entire chapter**; completion facts live at root; replays are the same
  chapter played again against the same gate — Records earned within the chapter, zeroed by the
  capstone's own reset; rewards and goals are formulas over stored facts (§8.1).
- **Economy:** producers are named definitions owning base contributions —
  `produces: [{currencyId, stat, value, condition?}]`, stats (`rate`, `yield`) named and extensible;
  generators contribute the same entry shape, scaled by owned count; **Effect** =
  `{target: id-or-tag, currencyId?, stat?, multiplier}`, gathered on read from
  facts (purchases, timed buffs, events, modifier grants, fill counts, career totals) and never
  stored; flat bonuses are contributions; tags name sets from the member side.
- **Bars:** generic fillables — each names an optional pool currency and its own fill rate, taking
  what is there in declaration order; a group only caps how many run at once; repeating bars carry
  per-fill cascade effects scaled by fill count; uncapped overfill, completion derived from progress.
- **Events:** data + one ActiveEvent record; `StartEvent`/`DismissEvent` are self-guarding
  commands, never Action kinds; entry runs `onEntry` (banking the run if the host rung's own
  gate is met, discarding it otherwise) then creates the record; handicaps are ×<1 effects that
  exist while a record does, expiry included; a timer is the only thing that ends an attempt, ticks
  live only, and blocks idle; the goal latches whenever it holds before expiry, timed or not;
  dismissal is the one ending - record removed first, then `rewards` if the goal was reached, then
  `onEnd` either way, which is where the reset lives; any reset reaching the host kills it; expired
  records persist, occupying the host and still handicapping, until dismissed; rewards lateral,
  never Records.
- **Triggers:** `{condition, actions}` per scope — the one sanctioned condition-observer; one-shot
  per scope-life (`firedTriggers` latch, a reset re-arms), swept after ticks and command
  transactions in live scopes only, never during idle; repeating behavior is a producer or a bar,
  never a trigger.
- **Idle:** per chapter — one active chapter ticks; switch-in computes `rate × min(t, cap) ×
  idleRate` (base 50%/4 h; `idle_rate`/`idle_cap` are effect targets) into a pending claim presented
  as the idle dialog — the ad doubles the claim, deposit on dismissal; app close is not special;
  yields and bar progress never accrue.
- **Monetization:** opt-in ads only; double-the-claim idle ad; Encore = game speed 2×/Overdrive 4×
  (`game_speed` reserved target, tick-consumed; wall clocks never scale); Backstage Pass (lifetime,
  permanent Overdrive); Buy Roadies (repeatable); Tip Jar; no subscriptions.
- **Engine:** Unity; break_infinity numbers; DateTime ticks; checksummed JSON save of the state tree;
  one Addressables load of the root scope, which carries the whole directly-referenced content
  graph; load-time validation of all authored data.
