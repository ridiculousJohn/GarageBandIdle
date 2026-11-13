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

**The album loop (inner).** Within the current chapter the player grows the chapter's **base
resource**, spends it on generators and upgrades, fills the chapter's **release meter**, and then
releases an album (section 3 names the roles); in Chapter 1 that is tapping for Cash, buying gear and
bandmates, and growing Fans. Releasing an album resets the run - the base resource, gear and the
release meter - and pays a **prestige currency** (Chapter 1: Demo Tapes). The
chapter's **income boost** reads a prestige currency's balance (Chapter 1's Demo Tapes), so the next
run is faster. The player repeats this loop several times within a chapter.
A chapter with more than one tier may bank a different prestige currency at each, and a shallow
tier's can be one spent inside the chapter. Which tier
carries the income multiplier, which carries the advancement gate, and which is purely archival is a
per-chapter authoring decision.

**The chapter loop (outer).** The chapter's **capstone gate** reads a prestige currency (Chapter 1:
Demo Tapes) and unlocks its capstone gig. Playing the capstone completes the chapter. Completion
facts (the completion flag; optionally, counters a chapter's
reward curves read, §8.1) live at the **root** and survive everything; the capstone's reset clears
the entire chapter, which is what makes the
chapter immediately replayable (§8.1). Advancement to the next chapter is forward-only and reacts to
the completion flag.

**The song loop (chapters 5-8).** From the Studio on, the chapters are no longer independent of
each other: the Studio makes songs, the Coast Tour and Radio raise their popularity, and Arenas pay
by it, so replaying those four in turn is a loop of its own, whose reward is inducting a song into
the Hall of Fame (section 7). The first pass through it is the weakest and is always completable.

Prestige currencies are the link between the loops: the income boost reads one and the capstone
gate reads one - the same currency or two, as the chapter authors it. Chapter advancement therefore
depends on releasing albums, not on a single large total of the base resource.

```
   INNER (minutes):  tap -> base resource -> generators -> release meter -> release album --+
                      ^  reset run, +prestige currency, repeat faster                        |
                      +----------------------------------------------------------------------+
                                    | the prestige currency the capstone gate reads reaches it
                                    v
   OUTER (hours/days): capstone gig -> next chapter (forward only)
```

---

## 2. Chapters

Eight chapters. Each has its own gear, currencies, mechanic, and capstone gig, and is gated by one
of its own prestige currencies.

| # | Chapter | New mechanic | Capstone gig |
|---|---|---|---|
| 1 | Garage / Basement | Tap, gear/bandmate buffs, "learn covers" bars | First backyard / house party |
| 2 | Open Mic | Gigs as timed bars you book then automate; taps add time; Buzz and Rep multiply Cash payouts, EPs Sold multiply Rep payouts; Roadie allocation opens | Win the talent-show slot |
| 3 | Local Venues | The circuit: rooms with genre taste and fan fatigue; booking agent automates | Headline the ballroom |
| 4 | The Local Tour | The Van: routing across nearby cities; merch on the road | Complete the local circuit |
| 5 | Record Deal / Studio | Songs: ideas become tracks, mixes and masters (§7) | Sign the deal & cut the record |
| 6 | The Coast Tour | Sponsorships; long-haul routing, west to east; songs gain popularity on the road (§7) | Festival main stage |
| 7 | Radio / Streaming | Airplay raises songs' popularity fast (§7); large idle income | First charting single / radio play |
| 8 | Arenas / Stadiums | World tours whose pay reads the set list's songs (§7); endgame scaling | Sell out a stadium |

Every chapter uses the same rhythm - grow the base resource, buy, fill the release meter, release an
album - with new gear, a new mechanic, and a capstone gate on one of its own prestige currencies. The names
are the chapter's own (section 3): Chapter 1's are Cash, Fans and Demo Tapes, Chapter 2's Cash, Rep
and EPs Sold.

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
(and the upgrades that set them) in its tier, so an album release clears them, and the tier's Cash
earned total resets with the run too - so the second run re-walks the PURCHASE ladder: band, fans,
covers, gear are bought again, faster as banked multipliers accumulate. What a run has once shown
stays shown: a section's or a row's reveal is a chapter flag, set once by a tier trigger at the
moment the row's purchase gate first holds, so the band and the gear stay on screen across runs
with their buttons greyed until they can be bought again (a purchase gate is the button's
business, never a row's visibility). The `album` flag is declared at the chapter level for the
same reason, so the release *region* stays on screen across runs; only the button's pressability
tracks the live offer condition (§5).

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

**Roles.** A chapter names its own resources and the engine fixes only their roles. Every chapter
declares at least these three; it may declare more layers (several tiers, intermediate currencies,
section 1), never fewer:
- **Base resource** - what the chapter's generators produce and its purchases spend. It carries
  root's `income` tag, which is how the Roadie boosts (section 8) reach it.
- **Release meter** - what the release's payout formula reads. It never carries the `income` tag, so
  no Roadie boost reaches it; that is the wall-clock floor of 8.1.
- **Prestige currency** - what a release pays. It is declared outside the tier whose release pays
  it, so that release's reset keeps it (lifetime is placement); in a single-tier chapter that is the
  chapter scope, and the capstone's reset of the chapter zeroes it. A chapter with several tiers may
  have several prestige currencies.

Two jobs are done by prestige currencies, and which currency does each is the chapter's authoring:
the **income boost** (a permanent modifier the chapter declares reads a currency's balance to
multiply the chapter's income, section 5) and the **capstone gate** (the capstone's offer condition
reads a currency, section 6). One currency may do both - Chapter 1's Demo Tapes do - or two
different currencies may. The boost may also reach income through the release meter: Chapter 2's
EPs Sold gate its capstone and multiply what a gig pays in Rep, the release meter, and the Rep
balance multiplies what a gig pays in Cash. Nothing in the engine ties the two jobs: each is an
ordinary authored reference (a formula's currency, a condition's currency).

Chapter 1's are Cash, Fans and Demo Tapes; Chapter 2's are Cash, Rep and EPs Sold.

**Run-scoped (Chapter 1 tier):**
- **Cash** - the base resource: earned by tapping and generators; spent on gear and upgrades.
- **Gear & bandmates** — generators bought with Cash, each contributing to one or more currencies'
  rates (§12.2). A bandmate is simply a generator with two outputs — cash and fans — plus a
  `bandmate` tag; there is no flag any system branches on.
- **Rehearsal** — Chapter 1's fill currency, earned from engagement (a passive tick plus taps) and
  consumed by learn-songs bars (§6). An ordinary currency; later chapters may define their own fill
  currencies.
- **Learn-songs bars** — generic fillable bars that pace a chapter (§6, §12.7).
- **Fans** - the release meter: determines the album's Demo Tapes payout on release. Accrue
  passively once revealed; the fans rate composes a base contribution plus one from each bandmate,
  so it is a function of band size and time — never of Cash or income.

**Chapter-scoped (Chapter 1):**
- **Demo Tapes** - the prestige currency, paid by the album release (section 5). Each Demo Tape
  raises Chapter 1's income; the capstone gates on them. Accumulated, never spent.

**Permanent (root):**
- **Roadies** - crew; a global multiplier allocated across unlocked chapters (section 8), and the only
  currency that crosses chapters.
- **Songs** - made in the Studio and kept in the catalog, then in the Hall of Fame (section 7). Not
  a currency: a named thing with a popularity and a classification. With Roadies, songs are what
  crosses chapters, and the Hall of Fame's boost is the second game-wide one. The Discography is the
  screen that lists them.

**Income.** Every produced number is computed on read as *the sum of its contributions times the
product of the multipliers targeting it* (12.2). The effective multiplier stack on the base resource:

```
income = Σ(generator base × owned × per-generator effects)
         x prestigeMultiplier (chapter fact, 5)
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
- **Effects & payloads.** An upgrade carries its gate, a cost, an optional `ownedWhen` (below), a
  `List<Effect>` (ongoing buffs, applied while the purchase latch exists) and a `List<Action>`
  (one-shot payloads executed at purchase: grant a currency, set a flag, add a modifier), and an
  optional `icon`, the glyph its tile draws (section 12.11). No per-kind upgrade classes.
- **Ownership.** An upgrade is owned while its purchase latch is set at its declaring scope OR its
  optional `ownedWhen` condition holds there - the context its gate is asked in; a levelled upgrade
  (below) is owned at its last level. One rule
  (`UpgradeDefinition.IsOwned`, asked from any scope through `Purchasing.IsOwned`) answers every
  reader: `CanBuy` / `Buy` refuse an owned upgrade, `UpgradePurchased` holds while it is owned, and
  the tile, the row, the details window and the calendar's Auto button render it owned. The effects
  alone read the latch: they apply while the latch exists, so what must outlive the latch is carried
  by a flag, never by the upgrade's effects.
- **Levelled upgrades.** An upgrade may author `levels` instead of its own cost, effects,
  description and actions: each level is `{cost, effects, description?, actions?}`, the cost in the
  upgrade's `costCurrency` under the upgrade's cost factor. A buy buys the NEXT level - its price, its
  actions - and the owned level is a fact beside the latch (`upgradeLevels`, section 12.3). Levels REPLACE,
  never stack: while the upgrade stands at level N exactly level N's effects apply, so a x2 level
  followed by a x5 one is x5, not x10. The latch stands from the first level, so `UpgradePurchased`
  holds at level 1 and above; the upgrade is owned, and refuses the buy, at its last level. A
  levelled upgrade authors no `ownedWhen`: its ownership is its last level. A tier reset clears the
  level with the latch, so a buff ladder is re-climbed each run like any buff upgrade. Chapter 2's
  Crowd Work is the first: x2 on the crowd producers at 300 Cash, then x5 at 25,000.
- **Reveal.** A content-unlock upgrade reveals its content by a `SetFlag` action; the revealed
  content gates on that flag. One reveal mechanism for every content type.
- **Lifetime is placement.** *Buff upgrades* are declared in a tier: their purchase latch clears when
  the tier resets and they are re-bought each run, faster as banked multipliers accumulate.
  *Content-unlock upgrades* are declared wherever their reveal should live: Ch. 1 files its reveal
  chain in the tier so the second run re-walks it.
- **Prestige-bought content.** A generator or upgrade priced in a banked currency is simply declared
  one scope out from the tier that resets — it survives the reset that pays for it. A generator's
  cost currency is independent of what it produces ("buy with Cash, produce Merch" is a data shape).
- **Run-priced content that outlives the run.** A purchase priced in the run's currency is declared
  in the tier, where its price is, and its payload sets a flag declared on the chapter; whatever must
  survive reads the flag - a bar's `repeatWhen` (a standing booking), a chapter permanent modifier's
  `appliesWhen` (an automation switch, a discount). The upgrade declares `ownedWhen` on that flag, so
  after the tier resets its latch it still reads as owned: shown owned, and never bought again.

---

## 5. The album (prestige)

Releasing an album is the run reset. Its name escalates thematically across chapters (demo, EP,
record).

Mechanically a release is a **rung**: `{offerCondition, List<Action>}` declared on the tier
(§12.5). Chapter 1's:

```
tier1.release:
  offerCondition: All[ CurrencyAtLeast(fans, 50), BarsCompleted(1) ]
  rungActions:    [ AddCurrency([demo_tapes], PayoutFormula),
                    ResetScope(tier1) ]
```

The payout is an ordinary `AddCurrency` action whose amount comes from a `PayoutFormula` (12.5) -
there is no payout field and no distinguished award kind. An `AddCurrency` may name several targets,
each paid the same formula under the multipliers addressed to that currency (12.5) and all committed
together; Chapter 1's release names one.
**Order is authoring**: the payout action
precedes the `ResetScope`, so the formula reads the Fans the reset is about to destroy. Actions
placed *after* a reset land in the fresh state, which is how a head-start reward ("start the next
run with 500 Cash") is authored.

- **Resets:** Cash, gear, learn-songs bars, Fans - everything the tier declares.
- **Keeps:** the prestige currency it pays, declared outside the tier (Ch. 1: on the chapter), and
  Roadies and songs, kept at root - all unreachable by the tier's reset.
- **Awards the prestige currency** based on run performance:
  ```
  earned = f(releaseMeterThisRun)        // Ch. 1: floor((fans/5)^0.5) Demo Tapes
  ```
- **Each unit** raises the chapter's income where the chapter's income boost reads this currency,
  as Chapter 1's does: a permanent modifier the chapter declares and lists in
  its `permanentModifiers` reads the balance (Ch. 1: `demo_tapes_income`, `LinearOnBalance` at +2%
  per Demo Tape, additive within the term, on the `income` tag at `stat: rate` and `stat: yield`).
- **The prestige currency** unlocks the chapter's capstone at a set threshold where the capstone
  gate reads it, as Chapter 1's does (section 11). In a single-tier chapter it is declared on the
  chapter, so the capstone's own reset zeroes it - every replay faces the same gate from zero (8.1).

An early album cycle takes seconds to minutes; cycles get faster as the currency the income boost
reads accumulates.

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
from the curve. The prestige card's gain preview calls the same formula — one implementation, no
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
  chapter. Because runs reset, a chapter's base resource stays in a bounded range (Ch. 1's Cash in
  the thousands-millions); growth within a chapter comes from its income boost, and across
  chapters from Roadies.
- **Upgrades (§4).**
- **Learn-songs bars** — see below.
- **Fans** (Ch. 1's release meter) - passive accrual, band-size driven (section 3), tuned loosely
  relative to Cash so income alone does not determine the album payout.
- **Capstone gig** — the chapter's rung (§12.5), declared on the chapter:
  ```
  chapter.capstone:
    offerCondition: CurrencyAtLeast(chapterN_gate_currency, gate)  // Ch. 1: demo_tapes, 30; every clear
    rungActions:    [ AddCurrency(roadies, RewardFormula),    // Ch. 1: the constant 1 (8.1)
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
not gate chapter advancement - the capstone gate always reads a prestige currency - and their rewards
are lateral (never the prestige currency), so no event is ever a hard requirement.

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
  `Not(EventRewardPending)` so that an `onEnd` carrying `[RestartScope(tier)]` can actually
  bank. Nothing in an action list can observe the record's absence: handicaps are
  multipliers on production, and rewards read balances.
- **Two ending lists, because failure has an ending too.** `rewards` runs only on success and holds
  the payout - a chapter-durable buff (`AddModifier`), a Roadie, local currency.
  `onEnd` runs either way and is where the reset lives, so reward-before-reset is structural rather
  than a convention an author can get backwards. What a failed run costs is therefore authoring:
  `[RestartScope(tier)]` in `onEnd` banks whatever the tier's own gate allows and wipes the rest, a
  bare `ResetScope(tier)` destroys it, and an empty `onEnd` leaves the leavings
  for the next reset to claim. And an event **dies with its scope**: any reset that reaches the host
  clears the record - a mid-event release, a capstone resetting the chapter from above.
- **Reward constraint:** never the chapter's prestige currency or any advancement currency - an
  event's payout is lateral.
  Authoring, not a validated rule: nothing in the model marks a currency as advancement, so a check
  would have to invent that vocabulary, and one that followed rung references would flag the
  legitimate case - an `onEnd` running `RestartScope` banks the prestige currency through the tier's
  own gate.
- **Levels:** a harder rerun of an event is simply **another event** — `silent_stage_2` is its own
  definition whose `availableWhen` gates on `silent_stage_1_done`; there is no level machinery, no
  selection contract, and `StartEvent(event)` is always fully specified. The rising requirement
  plus the player's power curve throttles level stacks as a repeatable Roadie source. Reward
  composition is per-chain authoring: each level's modifier is an *increment* that stacks with the
  previous level's, or the next level's `rewards` swaps them explicitly
  (`RemoveModifier` + `AddModifier`).

---

## 7. Songs (Studio onward)

Songs are things the player makes in the Studio and keeps after it. With Roadies they are what
crosses chapters: a Roadie is a currency, a song is a named thing with a history. Decided
2026-11-10; this section replaces the earlier sketch, in which a song rolled a quality tier and its
quality multiplied all income (the "Catalog boost").

> **What is built (2026-11-10).** A song is a title on root's list: `AddSong(title)` (12.5) writes
> one from whatever list a chapter places it in, the title a pattern over the song's number
> (`"Untitled #{0}"` writes "Untitled #1"), so a release nobody is watching still names its song.
> The player renames any song from the Discography window - a session command, `RenameSong`, which
> trims the name, holds it to 40 characters and refuses a blank one. The window opens from a top-bar
> button that stands only while a song exists, and the save carries the list in order. Everything
> else in this section is design and has no code yet; 7.5 gives the build order.

### 7.1 What a song is

- **A song is made from tracks.** The Studio is a pipeline of three bars, each consuming what the
  one before it makes: lay down tracks (ideas become tracks), mix (tracks become mixes), master
  (a mix becomes a song). A song is rare and earned - a few a run, not thousands.
- **A song is born equal.** It has a title and no popularity. There is no quality roll: nobody
  knows a classic in the studio, and nothing about how a song was recorded sets it apart.
- **Popularity** is one number per song. It only goes up, and it goes up by the song being played
  to an audience (7.2). It lives with the song, so no chapter's reset touches it.
- **Classification** is read off popularity against steps authored once for the whole game, on
  root: each step a name and a threshold, as many steps as the loop needs. The last step is the
  highest classification.
- **The catalog** is the songs the player holds, and it has a limit authored on root. When it is
  full a newly mastered song is discarded; the only thing that frees a slot is induction (7.4).
  Since every song is born equal there is no weak song to clear out, only an under-played one.

### 7.2 The loop

The Studio, the Coast Tour, Radio and Arenas form one repeatable loop: make songs, develop them,
cash them in, and go round again with a better catalog. The first pass is the weakest, by design -
repeat something and it gets better - and the one requirement is that the first pass can be
completed. A long loop is acceptable; a chapter in it can be made shorter if a turn costs too much.

| Chapter | What its set of songs does there | What it does to those songs |
|---|---|---|
| 5 Studio | - | Makes them. |
| 6 Coast Tour | Unproven songs add little. | Raises their popularity, slowly: the road-tested path any song can take. |
| 7 Radio | The songs are the singles being played. | Raises their popularity fast, once the set clears Radio's bar. |
| 8 Arenas | What a show pays reads the classifications of the songs in the set. | Nothing. Arenas cash in. |

- **Popularity rises in steps**, at the moments a chapter authors: a show finishing, a spin
  completing, a release. It is not a continuous rate. Shows that finish while the player is away
  pay their songs through the same completion the claim already settles (section 9).
- **Radio's bar.** The set's TOTAL popularity against an authored number. At or above it the set
  gets full airplay and gains fast; below it the songs are still played and gain slowly. The
  chapter itself never locks, which is what keeps a first pass with unproven songs completable.
  Because the bar is on the total, one big hit lifts the whole set over it: a hit carries a new
  song onto the air, where touring it is the slow alternative.
- **A song in several sets gains in each**, the away chapters at the away rate. On tour while the
  song gets airplay is a natural bonus, not an exploit; the away cap bounds it.
- **Arena pay reads the classification, not the raw popularity**: steps are bounded and legible on
  a set list, where a number that grows without limit is what makes tuning run away. The effect
  stays inside the chapter that uses it.

### 7.3 Sets

- **A set is the songs a chapter plays**, chosen by the player when the chapter starts and locked
  until that chapter is completed or restarted.
- **Its size is authored on the chapter.** Arenas may take more songs than the Tour.
- **With fewer songs than slots, the songs the player has are the set.**
- **A song may be in any number of chapters' sets at once.** One set list can serve the tour, the
  radio and the arena. This is where a song differs from a Roadie, which is stationed in one
  chapter.
- **Hall of Fame songs** may sit in a set only where the chapter authors it, as a count: Arenas
  one, every other chapter none. A band that still plays arenas plays its biggest song.
- **A set belongs to its chapter's own state**, so the reset that completes or restarts the
  chapter clears it and no rule has to (lifetime is placement, section 2).
- **Every chapter can be restarted** from settings: the capstone's reset without its reward,
  behind a confirmation. It is the way out of a set chosen badly.

### 7.4 The Hall of Fame

The Hall of Fame is what the loop is for. Without it, a better catalog only clears Arenas faster,
and a faster clear buys nothing a Roadie replay does not already buy.

- **Induction** takes a song out of the catalog for good, frees its slot, and gives a permanent
  game-wide boost per inducted song. The boost's form is undecided; it will be an ordinary boost to
  a stat or a currency. It is the second game-wide boost, beside Roadies (section 8).
- **A song may be inducted when** it holds the highest classification AND has been credited a
  number of completed chapter runs at that classification, the number authored on root. The runs
  are what tell a lasting song from a one-hit wonder: popularity only rises, so "more popularity"
  and "played for longer" are the same requirement, and staying power needs its own count. Runs are
  earned by playing the loop, never by waiting, and neither game speed nor the device clock moves
  them.
- **A run is credited by an action a chapter places on its capstone**, to the songs in its set
  that hold the highest classification. Which chapters count is therefore authoring: a chapter
  whose capstone carries the action counts.
- **A song in any set cannot be inducted.** A set never changes under the player, and the end of a
  chapter is the moment a song is both free and possibly eligible. A chapter left unfinished pins
  the songs in its set, so the Discography shows which sets a song is in.
- **Induction is a trade**: the song leaves the Tour, Radio and Arena sets for a permanent boost.
  The Hall of Fame slot in an Arena set softens it for the first inductee.

### 7.5 Build order

1. **Songs with standing.** Popularity on each song, the authored classifications, the catalog
   limit, an action that adds popularity to every song in the acting chapter's set, a multiplier
   formula over the classifications of the songs in the chapter's set - read the way the played
   chapter's Roadie boost reads its own allocation (8.2) - and a condition on the set's total
   popularity.
2. **Sets.** Choosing, locking, clearing with the chapter, the size and the Hall of Fame count
   authored on the chapter, and the screen that chooses a set when a chapter starts.
3. **The Hall of Fame.** The run credit, induction, the boost, and the restart command with its
   settings row.

The Discography grows with each step: popularity, classification, runs and set membership per
song, then the Hall of Fame and the induct button.

Chapter 1's learn-songs bars (section 6) are unrelated to all of this: they are pacing bars that
reset with the run.

---

## 8. Roadies

Roadies are a permanent global multiplier. The player earns them from capstones and from replaying
cleared chapters, and can also buy them (§9). All Roadies go into one pool and can be reassigned
freely among unlocked chapters, including cleared chapters. The pool is the root `roadies` currency; the allocation is a root fact the venue boosts
derive from.

### 8.1 Replaying cleared chapters

Replaying cleared chapters is the main way to earn Roadies through play.

The capstone's `ResetScope(chapter)` leaves the chapter freshly reset in place, so **replaying a
chapter is just playing it again** — same container, same content, progression re-revealed as the
player re-walks it. There are no separate replay instances and no frontier/replay distinction; the
chapter scales with every root multiplier (Roadies, Encore) because that is simply what
resolution reaches, exactly as it did the first time when those totals happened to be zero.

**One gate serves every clear.** The capstone's offer condition is
`CurrencyAtLeast(chapterN_gate_currency, gate)` - a prestige currency declared at the chapter level
and fed by a release payout (section 5). Because the capstone's own reset clears it with the
rest of the chapter, every replay starts at zero against the same goal: no first-clear/replay switch,
no clear count, no rising requirement. Replays get **faster, not harder** - a replay starts the
gate's currency from zero as a first clear does, and what it carries in is Roadies, which speed the climb to
the same gate - which is what makes replaying worth doing at all. Farming is throttled by **time**:
the release meter never carries the `income` tag, so its rate is deliberately not income-multiplied
(section 3; Ch. 1's Fans are band-size-and-time driven), a clear keeps a wall-clock floor no
multiplier stack removes, and the concave roadie-spread multiplier (8.2) favors rotating chapters
over parking on one.

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
factors are ordinary effects whose target is authored data** - a tag (`income`, declared at root and
carried by each chapter's base resource; Ch. 1: Cash) - so
*what* Roadies help with is a per-chapter design decision, never a code decision. Both carry
`stat: rate` AND `stat: yield`, the pair a chapter's prestige modifier carries (Ch. 1:
`demo_tapes_income`): Roadies scale every production
of an income currency, a tap's yield and a gig's fired lump included. (Ctrl C's completion tokens
boost the press: at a 2.5x token boost the chapter 1 copy/paste pays 30 Lines where the unboosted
press pays 12 - John, 2026-09-18.) They are
composed at different LEVELS, though. `roadie_total` is a currency-total effect - it is the same
product everywhere, so the currency is where it belongs. `roadie_active` targets the production
SOURCES (`{target: production, currencyId: income, stat: rate}`), because which chapter a number is
produced in is a property of its source; a currency total has already summed across sources and can
no longer name one chapter. **The release meter must never carry a roadie-targeted tag**: the
wall-clock throttle of 8.1 stands on the release meter (Ch. 1: Fans, Ch. 2: Rep) being unbuffable by
Roadies, and the `currencyId: income` narrowing is what enforces it even on a bandmate that pays
Cash and Fans from one definition. **Reallocation is present state** (decided 2026-09-10): a dormant chapter's idle claim is computed at
switch-in over the allocation standing then, the same stance Encore takes - what was
stationed while the chapter was dormant is not reconstructed. An offer already computed keeps its
lines. Ctrl C offers no precedent; its Completion Tokens only "increase generator output".

**Where the numbers live.** Each factor is a formula-shaped effect inside a permanent modifier on
the root scope (`permanentModifiers`, §12.5) whose formula carries its own `perRoadie`
(`RoadieTotalBoost`, `RoadieActiveBoost`); both read the root's
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
`lastActiveUtc`, stamped when leaving it live — switch-away or backgrounding — and, **for the
foreground chapter only and only while live**, on save — a save under the dialog must leave the
unpaid window; a
global save never touches dormant chapters' stamps, or it would truncate their idle. **The stamp IS
the pending claim**: everything specific about an offer is computed from it, and nothing about an
offer is ever saved. **Switching into a chapter** computes, for each
of its currencies, `idleRate x game_speed x paid time` - the paid window is the first
`min(elapsed, cap)` REAL seconds after the stamp, segmented at the buff expiries inside it exactly as
the tick segments (12.9), each segment paying its length times the rate and speed live in it;
`idleRate` and `game_speed` are the ordinary gathers run
under the idle-accumulation context (§12.5), so the ×0.5 base joins and live-only buffs excuse
themselves - from *current* state, so a Roadie reallocated
while away is what the payout reads - into a **transient offer** (the lines, holding
what settlement pays, and the window's end; session-held, dead with the process), presented as the **idle
dialog**: the amount earned, plus "Double it" (a rewarded
ad that doubles *this offer*) and a Backstage Pass purchase button (buying it HERE doubles this
offer and pays it out in the store callback's own transaction, closing the dialog - the ad's
callback plus the entitlement write); a Backstage Pass owner's offer is computed already
doubled - the screen enters as if the ad had been watched - so the dialog shows the doubled amount and just OKs it. Both
buttons only REQUEST; the payout is the callback's. The offer deposits on dismissal or chapter
switch — or, doubled, in the ad or store
callback's own transaction (§12.9) — while backgrounding instead drops it and leaves the window
unpaid; settling always advances the
stamp to the window it paid. A kill with the dialog up saves nothing: the stamp never moved, so the
next entry recomputes the offer — same or slightly larger, capped — and what is shown is always
exactly what is paid, because only one offer is ever alive. Then the chapter is live. **Closing the
app is not a
mechanic**: it is the state where no chapter is active, and launching runs the same switch-in path.
In-game chapter switching and time away are one mechanic.

What accrues is settled by structure: every currency **rate** accrues (progress while away is what
an idle game is), and **every bar that is on fills** at its rate over the same window, capped by what
it consumes - the consumed currency's balance at the stamp plus what the window pays into it, less
what earlier bars in settlement order drew - and a completion the fill crosses pays what its
`onComplete` fires, resolved under the same context. A tap's **yield** never accrues, because nothing
fires a producer in the player's absence. The offer carries the bars' draws beside its lines and each
bar's carried progress and completions beside those; the claim deposits, spends, and writes them
(12.9). A cover left running finishes while away if what it drinks was there to drink. No idle flags, no
exempt lists — the rate/yield split answers everything.

A dormant chapter with no generators accrues nothing (zero rate), so parking an untouched chapter
earns nothing — the system self-regulates without a rule.

`idleRate` (base 50%) and `cap` (base 4 h, plus a minimum-away threshold below which nothing pays)
are two different kinds of number. The cap and the threshold are **seconds** — thresholds, not
multipliers — and live in `GameConfig`. The fraction IS a multiplier, and it is an ordinary rate
effect: a root modifier `{stat: rate, ×0.5}` that **applies only during idle accumulation**
(`appliesWhen`, §12.5), with `{stat: count, x0.5}` beside it so a grant into a generator's count
while away is halved too - away pays half of ALL of it, and the wildcard reaching every stat's one
coordinate (§12.2) is what makes that two lines at root rather than a rule each chapter repeats. The claim runs the same rate and `game_speed` gathers the tick runs, under
an idle-accumulation context, so idle-only factors join, live-only buffs excuse themselves, and no
second vocabulary exists:

| Player | Idle payout | How |
|---|---|---|
| Free, no action | 50% | Claimed from the idle dialog on switch-in |
| Free, watches ad | 100% (2x) for that offer | "Double it" doubles and settles the offer in the callback's own transaction (§12.9) |
| Free, Encore running | 2x on the part of the paid window the buff covers | The buff burns real time while away; the claim segments the paid window at its expiry, so 1 h of Encore left and 4 h away pays 1 h at 2x and 3 h at 1x |
| Backstage Pass owner | 4x always (both ads' rewards, no ad), and a higher cap | Permanent Encore (the same modifier, applied permanently on the entitlement, below) scales the window; the offer's lines are computed already doubled, so the existing claim path pays them as they stand; the cap raise is entitlement plumbing on the config read |

Idle income is themed as streaming/radio royalties and is largest at the Radio chapter.

**Encore (the accelerator).** A **game-speed multiplier**, not an income multiplier: an ordinary
root permanent modifier reading a TIMER. A timer is a declared value on a scope
(`declaredTimers`, bare strings like flags): its value is the timed record (`{buffId, expiresAt}`,
the id the timer's) on the declaring scope, and absent or expired both read as not running. A buff
declares which timer it reads (`ModifierDefinition.timer`) and how much time must remain on it for
the buff to count as active (`activeAfterSeconds`, the band, default 0); `BuffActive(modifier)` is
true while the record's expiry is later than now plus the band, the record resolved outward from
the acting scope to the first scope holding it, so the record's placement is its lifetime and no
scope is named. Root declares timer `encore_timer`, and `encore` is `{stat: game_speed, x2}` with
`timer: encore_timer` and `appliesWhen: Any[HasEntitlement(backstage_pass), BuffActive(encore)]` - the
same shape as the idle fraction, a membership that counts only while a fact holds. Extending a
timer is one command, `ExtendBuff(scope, timer, seconds, cap)`, and one authored action,
`ExtendTimer(timer, seconds, capSeconds)` (12.5), writing at the timer's home through the flag's
outward walk; the Encore ad's reward is root's authored `encoreAdReward` list,
`[ExtendTimer(encore_timer, 14400, 86400)]`, run at root's context by the ad callback as one transaction.
The code names no timer: the chrome's pill authors `timer="encore_timer"` in `Screen.uxml` and the window
reads the pill's. Two buffs reading one timer with different bands is a speed ladder authored with
no code. `game_speed` is a stat with exactly two consumers, the two
places real time becomes production: the tick - `effective dt = real dt x
GetMultiplier(game_speed)` - so every rate, accrual, and bar fill in the live chapter speeds up
automatically, and the idle claim, which scales each segment of the paid window the same way. For
idle the two levers are one: nothing but rate accrues while away, so 2x speed over the paid window
IS 2x rate over it - except that the cap is REAL time, so the player accrues for the first `cap`
seconds after the stamp and Encore multiplies what those seconds pay, never how many of them there
are (4 h away at 2x pays 8 h of base rate). Yields never scale (per-firing, no time component), and
**wall-clock decrements never scale**: event `remainingSeconds` and buff expirations burn real
seconds. The timer is an absolute expiry, so it counts down whether the app is open or closed, and
a buff that expires inside the paid window scales only the segment it was live for, and a buff
with a band flips at its timer's expiry minus the band, which the walk cuts at as it cuts at an
expiry (12.9). Rewarded ads add 4 h to the timer, repeatable, to 24 h of remaining time - both
numbers on the reward action root authors, and a cap bounds only what its own grant may bring the
remaining time to, never time another grant banked. The tick prunes an expired record at its end as
housekeeping; `BuffActive` judges a record by the context's own time
(`expiresAtUtc > NowUtc + activeAfterSeconds`), so a segment, a closing sweep, or a module gate
reads the truth at the moment it asks, and no answer depends on when a prune ran.

**A higher speed tier is undecided** (2026-09-14). Cells to Singularity banks 2x ad time to 24 hours and
runs the excess at 4x to a higher cap; Ctrl C's Overclock has no tier above its 2x. Nothing is
decided, because a 4x earned by ads has not been reconciled with the Pass. What IS settled is that
the engine can do either with no further code, by content alone, and the timers above are that
ability. A 4x, if ever authored, is one more root modifier reading the `encore_timer` timer with
`activeAfterSeconds: 86400`; removing it is deleting that modifier. The walk cuts a segment where
such a buff flips, expiry minus band, as it cuts at an expiry.

**Backstage Pass** - lifetime IAP (~$5-10). Every reward the two ads give, without the ads, plus a
raised idle cap: permanent Encore - the entitlement is the first leg of `encore`'s own
`appliesWhen`, so a Pass owner applies the same one membership a free player's timer applies, and
the Encore window shows
"Time remaining" as infinity with no ad button, the Pass superseding the timer - the idle claim always doubled
(the offer's lines computed at twice the amount, exactly what the ad callback does to them before
it settles, so the claim path is unchanged and no second idle modifier exists), and the cap raise on the
config read. A free player who watches both ads reaches the same 4x on an offer; the Pass makes it
automatic. Since ads are opt-in, the Pass's value is convenience; how the Pass would relate to any
higher speed tier is the open question above.

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
the next chapter — gated on state, never on observing a transition: the beat's button goes live on
`chapterN_complete`, opening the card sets the root `storyN_seen` latch - from the button or by the
beat's own mark alike - and only a beat MARKED to pop opens itself, while
`chapterN_complete && !storyN_seen`, so a crash between the completion save and the beat cannot
skip it, and a beat read before an app kill stays read. Popping is the exception, never the
default; chapter 1 marks neither beat. Every read beat is listed in the story log, opened from the
chapter's top bar: one entry per beat whose latch is set, over every chapter in root's roster; an entry
reopens its card without writing anything, and closing that card returns to the log. There are no story
interruptions during the loop itself.

Named songs (§7) serve as story artifacts: the Discography keeps every song the player holds or
has inducted, with the standing it earned.

---

## 11. Pacing & tuning

Chapter pacing is set primarily by each chapter's capstone gate, which
determines how many album cycles a chapter takes and therefore the overall game length. Capstone
gates are the first tuning lever; generator curves are adjusted only after. A replay starts with the
gate's currency at zero, so how much faster it runs than a first clear is set by Roadies.

Two structural properties keep pacing stable against players with strong income multipliers:
- Chapters gate on a prestige currency, not the base resource. Multipliers raise the base
  resource; advancement requires album releases.
- The release meter's rate is tuned loosely relative to the base resource and never carries the
  `income` tag, so income alone does not shortcut the album payout.

Tuning must hold at both ends: an unbuffed player's pace is doable and never feels impossible, and
a permanent-Encore player (2x game speed, with `maxGameSpeed` 4 as headroom above it, plus Roadie and
Hall of Fame multipliers) still takes
meaningful play time per chapter and breaks nothing. Timed events feel Encore fully — speed
scales production but never timers (§9), so a 4× player meets a timed goal in a quarter of the
clock; author timed goals with that end in mind.

**Prestige raises the payout, never the fill rate.** A chapter's prestige currency may raise what a
completion pays, or how many completions a run affords before diminishing returns bite — that is what
raises the curve so each run reaches further, which is the point of prestige. It may NOT raise how
fast a pacing bar or a fan meter fills. The wall-clock floor §8.1 relies on is the time a clear takes,
not the size of what it pays, so buffing a payout keeps the floor while buffing a rate removes it and
lets a late replay collapse to seconds. A per-chapter payout buff is also the safer shape than a
global one: it is chapter-local, so nothing leaks across chapters and no narrowing rule is needed to
keep it out of the fan rate. Chapter 2's gigs are the first instance, and the rule holds there:
EPs Sold multiply what a completed gig pays in Rep, Buzz and Rep multiply what it pays in Cash
(the generator yield its bar fires), and nothing multiplies a gig's cycle.

**Per-chapter economy template (to fill in):**
- A base resource, a release meter and a prestige currency, named for the chapter (section 3).
- 4-6 themed generators producing the base resource (exponential cost, growth ~1.15, Ch. 1's Cash in
  the thousands-millions).
- A release meter target that makes an album cycle meaningful (seconds early, minutes later).
- A prestige payout formula over the release meter.
- An income boost and a capstone gate, each reading a prestige currency - one currency or two.
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
   the foreground chapter selection, the session phase, the command queue, derived caches — lives in a
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

**Currency** - `{id, balance, activeWhen?}`. Pure state: it does not know how it is earned, but it
may say when it is **active**. An inactive currency accrues nothing and refuses an authored grant -
every `SourceTerm` returns zero for it, so a source's rate and yield lines and the total agree at
zero rather than a readout promising income the balance never receives, and `Deposit` throws, so an
authored `AddCurrency` naming it is a content bug that surfaces the moment it fires instead of a
silent write behind a reveal. Absent means always active. The condition is judged at the currency's
own **home**, so every source gets the same answer.

The gate is answered **once per payment**, and which write it is answered by is the difference
between the two deposit entry points. A GATHERED payment - a firing, the tick's rate phase, an idle
settlement - sizes every amount against one snapshot and then commits them, so the gate is answered
by `SourceTerm` at resolve time and the commit goes through `DepositResolved`, which does not re-ask.
Re-asking there would be a filter inside a commit loop whose own earlier deposits move the state the
gate reads: one output could abort a sibling's write mid-firing, which is the atomicity this section
promises, and a settlement could refuse a line the offer already presented, leaving the stamp
unadvanced and the offer live to pay twice. An AUTHORED payment has no gather to have judged it, so
`AddCurrency` is refused at the write or nowhere - and because its targets are TIED, paid together
or not at all (§5), every one of them, and every amount, is checked before any is written. A per-target
check-then-write loop would bank the first and refuse the second, and the command exits without closing its transaction, so a retry pays the first again.

`IdleAccumulation` is refused inside one, at any depth. It is the one condition whose answer depends
on which GATHER is asking rather than on the tree, and a gate deciding whether a currency takes
income at all cannot hold that. What the spelling COSTS varies, which is why the kind is refused
rather than one shape of it. Bare, the gate is shut for every acting scope but the claim, and no
action list ever runs under one, so a rung payout or an event reward throws on content that is
otherwise correct. Negated, those keep working and the currency instead vanishes from the idle offer
with nothing reported, since its rate gathers to zero under the claim and a zero line is never
presented. Both mechanics - "earns only while away" and "earns nothing while away" - are a wildcard
x0 modifier on `rate` and `yield` carrying an `appliesWhen`, which does the same thing in whichever
direction the author wants it and leaves authored payouts alone.

It is the only currency-level gate, and the sentence it says is "this does not exist for you yet."
"It exists but the income is paused" is a different sentence with a mechanism already: an x0 modifier
on `rate` and `yield` carrying an `appliesWhen` (§12.5). The two differ exactly where it matters - a
freeze still takes an authored payout, an inactive currency does not - and that difference is why
only one of them is a field. A modifier can reach the gather and zero a number; it cannot refuse a
`Deposit`, because `AddCurrency` never consults one.

**Producer** — a named definition that owns base contributions. Each `produces` entry declares one
number — which currency, which **stat**, the base value — plus an optional condition that must hold
for the entry to count:

```csharp
class ProducerDefinition : Definition        // id + tags, like every Definition
{
    List<ProducesEntry> produces;            // { currency, stat, value, condition? }
}
```

**An entry pays exactly one target**: a currency (its balance), a generator (its granted count), or
a bar (its progress, 12.7) - `{ currency | generator | bar, stat, value, condition? }`, and two or
none is a load error. A generator paying another generator's count is how a generator-generator is
authored: Scoring's yield entry names Querying, and a bar firing Scoring's yield
(`FireGeneratorYield`, 12.5) pays Querying so many per cycle, count and multipliers included. The
compiler files the sources paying any target at that target's home, exactly as it does for a
currency, so "how fast is my count growing" is the read a currency's rate is.

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
                         { rehearsal, yield, 1 },
                         { rehearsal, rate,  0.5 } ]
                         // No conditions: `rehearsal` declares activeWhen
                         // FlagSet(rehearsal_revealed), so both entries pay nothing before
                         // the reveal - and so does every Rehearsal source authored later.
                         // A reveal is a property of the CURRENCY; per entry it is a rule
                         // each new source has to remember, and one that forgets fails open.
                         // (A currency with NO activeWhen is how a pre-banking mechanic is
                         // authored, if a chapter ever wants one: it accrues while its
                         // readout is still hidden.)
```

**Generator** — the purchasable. Definition: `{id, tags, availableWhen, costCurrency, baseCost,
growth, produces: [...], thresholds: [...]}` — the same entry shape as a producer, scaled by its owned count - purchased plus granted;
state: `generatorCounts` (purchased, an int the player's buys bound) and `grantedCounts` (a BigNumber
other sources' entries pay into, keeping its fraction) in its declaring scope. The price reads the
purchased count alone, so granted copies never raise it; production and `OwnedCountAtLeast` read the
sum, and the condition's threshold is a BigNumber like every number the runtime can outgrow. A
bandmate is a generator with two rate entries (cash, fans)
and a `bandmate` tag. Cost currency is independent of what it produces. A purchase is a count:
buying `n` units at owned count `o` costs the geometric series
`baseCost * growth^o * (growth^n - 1) / (growth - 1)`, or `n * baseCost * growth^o` when growth is
exactly 1 (validation refuses only a nonpositive growth), and `Purchasing.CostOf(generator, ctx, n)`
is the only thing that computes a generator cost - the row's line, the affordability search, the
info screen and the charge all call it, so what the screen says is what the bank pays. The series
factor is computed as its own quotient and then multiplied onto `baseCost * growth^o`, so at n = 1
the factor is exactly 1 and the single buy costs exactly the unit cost. `MaxAffordable(generator)`
answers M, the largest count the gate and the balance allow at the declaring scope, or zero: it
doubles a count until the cost exceeds the balance and bisects, evaluating affordability through
that one function at every probe and returning only a count it found affordable - a last-digit
wobble in the power can hide a larger affordable count and never offer an unaffordable one.
`CanBuy` / `Buy` / `TryBuy(generator, count)` take a count of at least one (below one throws as a
caller bug), and a buy is one spend of the series sum and one write of `ownedCount + count`, never
a loop of unit buys: nothing in the effect vocabulary observes a unit landing and counts scale on
read, so the only differences from `count` unit buys are that the trigger sweep runs once at the
transaction's close (12.9) and a residual in the last digits, one rounding against `count`
roundings, that no read compares. `TryBuy` is fail-closed against `availableWhen` and the
affordability of the whole count - the domain owns the gate, never the UI's visibility - and
refuses whole when the balance does not cover it.
(Producers need no equivalent field: their `produces` entries carry their own conditions.)

**A price is a multiplier target like a rate.** `cost` is a stat in the same open vocabulary, and an
effect naming the generator or upgrade (by id or tag) with `stat: cost` multiplies the unit cost
before the series - stage 1 only, gathered from the declaring scope outward, since a price has no
total for a currency stage to modify ("everything priced in cash" is a tag the generators carry).
The factor is constant across one purchase, so `MaxAffordable` only ever evaluates `CostOf`. A
constant x0 on a cost effect is refused at load: a free generator is an unbounded rate printer, and
"nearly free" is a small factor. An upgrade's `cost` takes the factor the same way.

**Effect** — the modifier atom:

```csharp
[Serializable] public struct Effect
{
    public string target;      // a currency/producer/generator/bar id or a TAG; empty is the
                               // wildcard — "every currency", applied at the currency stage
    public string currencyId;  // optional — narrow to entries paying this currency (id or TAG)
    public string stat;        // REQUIRED and exact — the one stat this factor answers for
    public BigNumber multiplier;   // the gather's product is BigNumber, so an authored
                                   // constant spans the same range (§12.14 req 1)
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
gets its name from its members (`rhythm_section` carried by the drummer and bassist), so buffs never
list members and later additions join the set by carrying the tag. **The vocabulary itself is
declared on a scope**, beside its flags (§12.3): `declaredTags` names the words a scope's subtree may
carry, and a definition CARRYING a tag resolves it by walking OUTWARD to the scope that declares it.
An effect filtering on one resolves nothing - a selector is a match string the gather evaluates
against a candidate it already holds (§12.12), so the declaration binds carriers and only carriers.
Declaring a word high enough that everyone who touches it can see the declaration is how a set stays
one vocabulary, but that placement is authoring discipline; what the pass enforces is the carrier's
walk. An effect applies at the level of what
it matches: on a producer or generator (by id or tag) it multiplies **inside the sum** (that
source's term); on a currency (by id or tag) it multiplies **the total** — one rule, no double
counting. "Global income" is this tag mechanism, not a wildcard: root declares `income`, income
currencies carry it (each chapter's base resource; Ch. 1: cash), the prestige and Roadie modifiers of
3/5/8 filter on it, and a
later income stream joins the stack by carrying the tag.

**Consumer-owned stats:** `game_speed` (§9) — a stat in the same open vocabulary as `rate` and
`yield`, read by exactly two systems, the two that turn real time into production (the tick, which
scales the production dt, and the idle claim, which scales each segment of the paid window;
wall-clock decrements never scale). Nothing marks it special: the stat vocabulary is open precisely so a new
consumer adds its name and its own query, no new machinery. The idle fraction is deliberately NOT
a stat: idle pay is the live rate gathered under an idle-accumulation context (§9, §12.9), so its
factors are ordinary rate effects on modifiers that apply only then (`appliesWhen`, §12.5) —
"rate but idle" is a circumstance of one gather, never a second vocabulary.

Every multiplier the game computes answers one question, asked at one node: *at this origin, for this
owner, this currency, and this stat, which factors apply to this number?* A number is identified by
its owner and coordinates — a `produces` entry by (source, currencyId, stat), a consumer-owned stat
by its name alone. An effect matches when its
`target` names the owner (by id or any of its tags), its `stat` names the queried stat EXACTLY,
and the optional `currencyId` agrees — empty matches everything the owner has, set narrows by id
or tag. **The stat is required**: a query resolves one number and a number always has a stat, so a
stat-less effect would claim to answer questions of different kinds with one factor — "rate and
yield alike" is two entries, an empty stat is a load-time error, and at runtime it matches nothing
(fail-closed). **An empty `target` is
the wildcard: every number of that stat**, at the one coordinate the stat has per number. For
`rate` and `yield` that is the target's total - a currency's, or a bar's, whose fill is a rate the away
window accrues (section 9) and resolves stage 1 only, so the wildcard meets it once - while a source's
own term stays out, since it would otherwise meet the factor twice with root on both gather walks; so
`{stat: rate, x2}` speeds every currency's rate and every bar's fill. `count` has one coordinate per generator and `cost`
one per price, so `{stat: count, x0.5}` halves every grant and `{stat: cost, x0.5}` halves every
price - which is what lets root's idle fraction say "away pays half of all of it" in two lines.
`{stat: game_speed, x2}` - Encore - is a wildcard read owner-less by the tick and the idle claim,
which match wildcards only. `currencyId` matches an id or a tag, exactly as
`target` does, so "every rate entry paying an income currency" is one effect rather than one per
currency — and a currency stays out of it by not carrying the tag (§8.2's fans rule). **Where matches are gathered from is two
explicit stages**, which is what keeps sibling scopes isolated (§12.3):

```
sourceContribution = base × source-targeted effects   gathered from the SOURCE's scope → root
currencyTotal      = Σ sourceContributions
                     × currency-targeted effects      gathered from the CURRENCY's home → root
```

A currency-targeted effect must therefore be applied at the currency's home scope or an ancestor; a
descendant scope wanting a local boost targets its producer, generator, or a source tag instead.
That is a placement rule rather than a load-time check: an effect nobody walks up to meet simply has
no takers, which is also what a root buff looks like before any chapter carrying its tag is loaded.

**The second stage belongs to the target, whatever kind it is**, asked under the target's own stat:
a currency's total under `rate` or `yield`; a bar's under `rate` - the same plan as its fill rate, so
"10x this bar" speeds its own fill and what is paid into it alike - and `yield`; a generator's
granted count under `count`, one stat however the grant arrives, so an effect naming a generator
with `rate` keeps meaning that generator's own output. `cost`, `count`, `autobuy` and `consumption` are
effect addresses like `game_speed`: an effect may name them, a produces entry may not, and a wildcard
reaches every generator's count, every price, every generator's switch or every drinking bar's per-unit
price, as the paragraph above says. `consumption` is a bar's coordinate per consumed currency (12.7),
stage 1 only as `cost` is.

**`payout` and `exponent` are a currency's coordinates too**, compiled at its home beside `rate` and
`yield`. `payout` multiplies what an `AddCurrency` pays into the currency; `exponent` multiplies the
exponent of the payout formula paying it (12.5), so a Linear-stacking modifier at 1.05 raises an
exponent of 0.20 by 0.01 a grant. Both are read at the currency's home, so an effect on either lives
at that home or further out. Nothing reaches a payout but an effect naming the stat: a `rate` or
`yield` effect on the same currency leaves an authored grant exactly what it says, which is why a
capstone pays one Roadie whatever multiplies income.

**`autobuy` is a switch, not a factor.** A generator's coordinate, homed where the generator is
declared, read by the tick alone (12.9): on when an effect naming the generator - by id, tag or the
wildcard - is live and no handicap on the same coordinate is, the multiplier unread. The upgrade
that grants automation carries the effect and names its targets as its other effects do; the
generator declares nothing; an event handicap on the coordinate is section 6's "automation
disabled" for as long as its record exists.

**A generator that multiplies another by how many of it are owned** is a permanent modifier carrying
`LinearOnOwnedCount` - `1 + coefficient x count`, the count read outward from the gather origin like
every count read, purchased alone or purchased plus granted by its `purchasedOnly` flag, since the
two are separate facts. Add and multiply are the two shapes: a produces entry adds, an Effect
multiplies, and the factor inside an Effect is a formula kind; there is no expression language.

**A generator's count thresholds** are "at this owned count, these effects" (Ctrl C's, 2026-11-12):
`thresholds: [{count, effects: [{target?, currencyId?, stat, multiplier}]}]` on the generator, the
effects in the Effect JSON keys. Each effect is live while the generator's owned count - purchased
plus granted, the sum `OwnedCountAtLeast` reads - is at least its threshold's count, and only then;
reached-ness is derived from the count on every read and nothing about it is stored or saved, so a
reset that lowers the count withdraws the factor with it. A threshold is a CARRIER kind of the
generator's declaring scope (12.6), between bar cascades and handicaps, so the coordinate plans
compile its effects like every other carrier's and the tick, the idle offer and claim, the previews
and `CompletionRate` all gather them with no path of their own. The one difference from an Effect
is the owner: a threshold names one row, so `target` is a REFERENCE, resolved outward from the
generator's scope like `level` - the generator itself when unauthored - and the gather's selector
is that row's id. The factor is a constant (a formula is an import error), because the toast prints
it (12.11). The gather meets an effect applied at a scope only from an owner declared there or
below, so the target must be declared at the generator's own scope (12.12). A bar's speed is its
fill `rate`, named by the bar's id; a gig's payout is its draw's `yield`, named by the draw's id.
Chapter 2's gig draws are the first content: each authors one threshold, at 25, that doubles the
next gig's payout - that gig's draw at `yield` - and the qualifier, with no gig above it, doubles its
own; nothing in the chapter addresses a gig's `rate` (section 11). `GeneratorDefinition.NextThreshold`
answers the first count not yet reached (at an owned count, or at the declaring scope's own count),
and `ThresholdsCrossed` the thresholds a move between two counts reaches - none for a move down.

**Debt is a factor that shrinks, not a term that subtracts.** `DebtFactor` is
`max(0, 1 - coefficient x debt)`, where debt is a list of currencies' balances combined by sum or by
maximum (`combine`), read outward from the gather origin. A source authors `coefficient` as one over
its own base value, which makes the factor equal to subtracting the debt from that source's output
before every other factor: `(base - debt) x count x m` is `base x (1 - debt / base) x count x m`. The
floor at zero is what keeps a debt larger than the base from running production backwards. The mode
is fixed per effect; content that changes mode partway through authors two modifiers, one per mode,
switched by `appliesWhen`. An optional `relief` currency divides the combined debt once its balance
passes one. Debt accruing is an ordinary produces entry paying the debt currency.

**`LogOnBalance`** is `1 + coefficient x log10(1 + balance)`: a factor that keeps growing with a
balance at a falling rate, exactly one at a zero balance.

**`InverseOfBalance`** is one over a balance, and zero while the balance is zero. It turns a recorded
value into a rate: a rate entry worth an amount per run, under this factor over a recorded run time,
pays that amount per second, and pays nothing while there is no record. **A recorded pace** is this
with `SetCurrency` (12.5) and no further mechanism: a run keeps its clock as a currency fed at one
per second in the tier its reset clears; the list that completes the run writes that clock into a
currency further out and writes the other runs' records to zero; and a producer's rate entry
conditioned on `IdleAccumulation` pays the run's amount under `InverseOfBalance` of its record, so
an absence repeats the run last completed, at the time it actually took, and the idle claim is
unchanged. The record must be written by a list that runs while the chapter is live: a bar's
`onComplete` runs once per completion at the claim, every run against the clock as the window left
it (12.9), so a run that completes during an absence would record the wrong time.

**Both stages are compiled, once, when the tree is built.** Everything above but liveness is authored
and validated, so the load pass (12.14.8) asks the question for every coordinate the content authors
and files the answer, at the node it will be asked from, as a **coordinate plan**: walk outward from
the origin, take each scope's carriers in the order it declares them, and keep every effect the match
rule above accepts. The kept effects, chain order then carrier order, ARE the plan, each held as a
link to the node whose fact decides it (12.6); `GetMultiplier(origin, plan)` multiplies those links,
and the selector never runs again. There is no separate stage-1, currency-stage, bar or `game_speed`
plan: those are the same plan asked with a different owner. The queries compiled are exactly the ones
the content authors - each source entry at its declaring scope (stage 1), each currency at its home
for `rate` and `yield` (stage 2, the currency its own owner, so its own tags match), each bar at its
declaring scope for `rate` (its fill, and what is paid into it; stage 1 for the fill itself, 12.7)
and `yield` and once per consumes entry for `consumption`, each generator for `count`, `cost` and `autobuy`, each upgrade for `cost`, and
`game_speed` at each chapter. A definition's plans sit under the definition itself at its home, one per stat it is
ever asked for. A
coordinate no content authors has no plan, and a consumer asking for one throws rather than
answering 1x.

**The aggregation is compiled the same way**, as one **contributor plan** per scope, held at every
node over its own subtree: for each currency any source in that subtree pays at `rate`, its
contributors in tree order (parent before child) then declaration order, each with the scope it sits
at and the stage-1 plan its entries share, plus the currency's home resolved outward from a paying
scope; and that subtree's bars in settlement order - scopes parent before child, then bars in
declaration order - with each bar's fill-rate plan and its consumes entries, each carrying the consumed
currency's home and its consumption plan. Every node also holds its membership: which of its groups
list each definition it declares, so the seams of 12.7 read a link rather than search.
`GetRate` sums that list and applies the currency stage, `RatePairs` is its currency column, bar
settlement is its bar list. None of them walks the
subtree, and each is asked at the scope whose subtree is meant; the tick asks the foreground chapter
because that is what ticks (12.9), which is a session choice and not an economy rule.

That is the entire modifier system.

### 12.3 Scopes: state containers

A **scope** is a plain state container. Content declares, per scope: its currencies, flags, declared
tags (§12.2 - the vocabulary, distinct from the tags the scope itself carries), bar
groups (which own their bars), **producers**, generators, upgrades, modifiers,
triggers (§12.5), and (for tiers) its rung. **Every content family is declared somewhere** — that is
what makes a reference resolvable by walking outward, and it is why there is no catalogue to look
anything up in. A scope also carries `permanentModifiers` — a USAGE list, not a declaration:
each entry references a modifier declared on the reachable chain and applies it permanently
(§12.5), so the list joins no id space and homes nothing. Events are declared on `InteriorDefinition` rather than the base,
since root cannot host one: the definition is placed, the record is a fact of the host. Songs are
facts on root and nothing else (§7) - there is no `SongDefinition`: a song is written at runtime by `AddSong`
(12.5) rather than authored. A producer's `produces`
entries are live exactly while its declaring scope belongs to the foreground chapter's **live
subtree** — activation is placement, several sibling or nested scopes participate in the same tick,
and each contribution resolves its facts and effects outward from its own declaring scope to root,
never crossing into siblings. The tick enumerates rates from that subtree's declared producers and
generators, never from what the UI happens to show. Runtime state per scope:

```csharp
class ScopeFacts   // the mutable state a reset DESTROYS; what a reset keeps sits beside it (below, §12.10)
{
    Dictionary<string, BigDouble> balances;
    Dictionary<string, BigDouble> earnedTotals;     // per currency, same home as its balance
    Dictionary<string, int>       generatorCounts;  // purchased; the price reads this alone (12.2)
    Dictionary<string, BigDouble> grantedCounts;    // paid in by other sources' entries, fraction kept
    HashSet<string>               flags;
    HashSet<string>               purchasedUpgrades;
    Dictionary<string, int>       upgradeLevels;    // a levelled upgrade's owned level, 1..levels; the latch stands beside it (section 4)
    HashSet<string>               firedTriggers;    // one-shot trigger latches — a reset re-arms (§12.5)
    Dictionary<string, BigDouble> barProgress;      // uncapped — overfill is allowed
    Dictionary<string, int>       fillCounts;       // bars with a repeatWhen, the ones that complete more than once
    Dictionary<string, HashSet<string>> activeMembers; // per group: the member ids that are ON (12.7)
    Dictionary<string, int>       modifierStacks;   // AddModifier grants, keyed like every other count
    List<TimedBuff>               timedBuffs;       // {buffId, expiresAt}, the id a declared TIMER's: the timer's value; root's encore_timer lives here
    List<SongEntry>               songs;            // root = the Discography; unused on other scopes (§7)
}

class InteriorFacts : ScopeFacts     // every scope inside another: chapters and tiers
{
    ActiveEvent                   activeEvent;      // at most one per host (§12.8): a field, not a list
}

class RootFacts : ScopeFacts        // the root scope's payload, and no other
{
    Dictionary<string, int>       roadieAllocation; // chapterId → stationed count (§8)
    HashSet<string>               entitlements;     // store-written; the ids root DECLARES on its own entitlements list (backstage_pass), read by HasEntitlement (§12.4), never a flag
    string                        currentChapterId; // where play left off; boot returns here (§12.9)
}

class ChapterFacts : InteriorFacts  // a chapter's payload; nothing beyond what hosting brings —
{                                   // the idle stamp (lastActiveUtc) lives BESIDE the payload,
}                                   // re-stamped rather than cleared, and IS the pending claim (§9)

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
`lastActiveUtc` (§12.9) is a chapter field OUTSIDE the payload, re-stamped instead of cleared, and
so is every scope's `lifetimeTotals`: a currency's every deposit across rounds, seeded per declared
currency once when the node is built, written beside the balance and the earned total by every
deposit, read by the payout selector (§12.5), carried by the save beside the payload, and cleared by
no reset. A reset installs a fresh payload of the same type and leaves both standing.

The tree: **root** (Roadies, entitlements, completion flags, songs, any counters a
chapter's curves read (§8.1)) → **chapters** → **tiers**, and tiers may nest. **Ids are unique along a CHAIN** — a read walks
outward and stops at the first scope that declares what it names, so two sibling chapters may each
declare their own `cash`; what must not repeat is an id visible from one scope. Scope ids stay
unique tree-wide, since the facts that name a scope key by them - root's roadie allocation map,
the recorded current chapter - and a saved name has to be unambiguous wherever it is read back. A
declaration in two scopes is refused at load. Flag reads walk the chain outward (set anywhere on it = set); `SetFlag`
writes to the flag's declared home.

**Flags are strings on purpose.** Everything else a scope declares is an asset, and everything that
names one holds a direct reference (§12.14) - but a flag has no data beyond its own existence, so
there would be nothing inside a `FlagDefinition` to author. The cost of that choice is that a flag
name is the one authored reference a typo can break, which is why `SetFlag` and `FlagSet` are
validated against the acting chain and `SetFlag` throws at runtime on a name no scope on the chain
declares. **Tags are declared the same way**, and for the same reason: a tag has no data beyond its
own existence either, so a scope's `declaredTags` is a list of bare strings, and a definition may
carry only a tag some scope on its own chain declares. **Timers are declared the same way**
(`declaredTimers`): a timer is an expiry under a name, its value the timed record at the declaring
scope, and a modifier's `timer` and an `ExtendTimer` are validated against the chain as `FlagSet`
and `SetFlag` are (section 9). Stat names and the `Effect` selectors
(`target`, `currencyId`) stay strings too: a selector matches by id OR tag (§12.2), so it cannot be
a reference to one thing - all of these are vocabulary, not things.

**A scope is addressed by reference too.** An action or condition that names a scope holds the
`ScopeDefinition` itself, and getting from it to live state is a LINK resolved when the tree is
built, never a search: there is one state node per definition, and `ScopeState.Build` ends with one
pass that walks the new tree and writes onto the runtime nodes what each static reference means in
THIS tree (12.14.8). The pass resolves through a definition-to-node map `Build` keeps for
construction and drops when it returns - a map that outlived construction would be the id index
nothing may hold. A link is keyed by the static object that HOLDS the reference (a `ResetScope`
instance, a `SectionDefinition`) and stored at the node where that object executes or is evaluated,
so the read is `node.Link(holder)`, a dictionary read at a node the caller already has, and a miss
throws: construction omitted a site, and nothing falls back to a search. Nothing is written into an
asset, so two trees built from one content set hold their own links. The only scope NAMES belong
to stored facts (the roadie allocation map, the recorded current chapter), because a save holds text and
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

Kinds: `CurrencyAtLeast`, `EarnedTotalAtLeast`, `CurrencyExceeds` (one balance strictly ahead of
another, 12.5), `OwnedCountAtLeast`, `FlagSet`, `UpgradePurchased`,
`BuffActive`, `HasEntitlement`, `BarsCompleted`, `EventRewardPending`, `EventRecordExists`, `Always`, `All`, `Any`, `Not` (the story gate
`chapterN_complete && !storyN_seen` is
`All[FlagSet, Not[FlagSet]]`), plus a formula-threshold kind (threshold computed from state — e.g.
a scaling goal or reward curve reading a per-clear counter, 8.1). A prestige currency needs no
special kind: it is a currency.

`UpgradePurchased` holds while a latch for the upgrade stands on the chain from the acting scope,
read outward as every fact is, or while its `ownedWhen` holds at its declaring scope (section 4). A
levelled upgrade's latch stands from its first level, so the condition holds at level 1 and above.

`BuffActive` names a modifier as `UpgradePurchased` names an upgrade and reads the TIMER that
modifier declares (`ModifierDefinition.timer`) outward from the acting scope, true while the
record's `expiresAtUtc` is later than the context's own time plus the modifier's
`activeAfterSeconds` - never by the record's presence (section 9). A `BuffActive` on a modifier
that names no timer is refused at load. `HasEntitlement` names a store
product root declares on its `entitlements` list (12.3) and is true while root's set holds it: the
read is the chain's root, the id is validated against root's own list, and the store callback is
its only writer. The two event kinds read a record the way `FlagSet` reads a flag: outward from the acting scope to
the first interior scope holding one, no scope named. `EventRecordExists` - any record (running,
expired-undismissed, or goal-reached-undismissed); `EventRewardPending` - a record whose
`goalReached` is set. Both are pure fact reads: nothing evaluates a goal at read time, and neither
looks down. A scope's record is visible to that scope and to the scopes inside it, never to its
parent - a parent knows nothing of what its children host. Composed with `Not`, they are how a
scope's OWN rung disarms while an event runs or a reward waits; the player is never wedged, because
dismissal stays one tap away and always legal - it clears the record and pays the reward if the
goal was reached. A reset from above authors no such leg and cannot: protecting an armed reward
from a parent's clear is the reset's own refusal (12.5).

A condition may carry an optional **`uiText`** label ("Needs 50 fans", "Claim your event reward
first") — pure presentation data, never read by evaluation, rendered by the rung feedback
contract (§12.11). A condition's rendered **`Text`** follows one rule at every node: a leaf's is its
`uiText`; a compound (`All`, `Any`, `Not`) formats its `uiText` as a `string.Format` pattern over its
children's texts (`{0}`, `{1}`, ...), so a pattern with no placeholders is a whole override, and with
no `uiText` an `All` reads "A, B, and C", an `Any` "A, B, or C", and a `Not` nothing, since prose does
not negate mechanically. `Text` is context-free - it describes the requirement, and whether it is
met is a per-leg judgment - which is what lets the load pass compute every gate's text once (12.12).

**Every condition evaluates — and every action list executes — in an explicit scope**, supplied by
`GameContext`, never inferred and never inherited from a caller: a rung, upgrade, bar, or trigger
in its declaring scope; an event in its host scope; a `produces` entry in its producer's declaring
scope; a UI section or module in its authored `scopeId` (§12.11). Nested invocations **rebase** the
context to the owning object's scope — `ExecuteRung` runs the referenced rung in *that rung's*
declaring scope, which is how a chapter's rung invoking a tier's release legally reads the tier-owned
release meter. Reads walk outward
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

Kinds: `AddCurrency` (one or more target currencies; each is paid the constant or the
`PayoutFormula`'s amount under the `payout` and `exponent` factors that currency collects, 12.2, every
amount computed before any is paid and the targets committed together), `SetCurrency(currency,
amount or formula)` (overwrites the balance at the currency's home with a recorded value, under no
multiplier, moving neither the earned nor the lifetime total and never asking `activeWhen` - a
record, not income), `AddModifier(scope, modifier)`, `RemoveModifier(scope, modifier)`,
`SetFlag(flagId)`, `ExtendTimer(timer, seconds, capSeconds)` (adds to a declared timer at its home
through the flag's outward walk, remaining time clamped to the cap - the Encore ad's reward, §9),
`FireGeneratorYield(generator)` (fires a generator's yield entries the way `FireProducer` fires a
producer's, at the generator's declaring scope and scaled by its owned count - on a bar's
`onComplete` a team paying per cycle, on an upgrade or a rung a one-shot grant that scales),
`AddSong(title)` (appends a song to root's list under the title formatted over the song's number,
section 7), `ResetScope(scope)`, `ExecuteRung(tier)` and `RestartScope(scope)`
(fire that scope's rung through its own gate, then clear it - the restart idiom as one action; a
bare `ResetScope` remains for a pure wipe). The event lifecycle operations are commands rather than
Action kinds (§6.1, §12.11). Authored inline via
`[SerializeReference]` wherever needed — upgrade payloads, bar completions, event rewards, rungs.
No shared reward pool; a reused reward can be promoted to a shared asset later if duplication ever
hurts. Actions are one-shot: they run at their moment and are never replayed on load — the state
they mutated is what gets saved.

**`ResetScope`** clears the referenced scope and everything inside it (downward-closed). It only clears —
it never executes nested lists — so no recursion exists via resets.
The clear is a parent INFORMING its subtree of an event, never a read of it - and the subtree
answers. A scope holding an armed, unclaimed reward (a record with `goalReached` set) refuses to be
cleared, judged from its own facts, and the refusal comes back up the walk the clear goes down. The
rule is about RUNNING AN ACTION LIST, so the one runner every authored list executes through asks
that question before any list runs - a rung, an event's entry and its ending lists, a trigger, an
upgrade payload, a bar completion alike - and a list runs whole or not at all, so each site's guard
closes before any action runs and no list half-executes; the gate feedback renders the refusal as a
leg naming the event by its `displayName`. A reset forced past a refusal throws (requirement 7).
Three kinds answer the runner, each for what it would do: `ResetScope` and `RestartScope` with the
refusal of the subtree they would clear, `ExecuteRung` with the refusal of the list it would run - so
a refusal nested in a named rung closes the outer list rather than reading as a closed gate one level
down. `DismissEvent` removes the record before running `onEnd`, so a restart from an ending list is
never refused by its own event.

A scope never clears an ancestor (§12.12), so a moment INSIDE a scope that is meant to end an
enclosing lifetime is authored on the scope that owns that lifetime. The child writes a fact
outward - a flag it can reach, a currency total - and the owning scope declares the trigger or the
event whose list carries the `ResetScope` or `RestartScope` of itself, gated on that fact. The sweep
runs inside the same transaction as the child's write, in tree order, so the clear lands in the same
command. Chapter 1 is the worked case: the Garage Jam is hosted at tier1 and its `onEnd` restarts
tier1, rather than anything below the tier reaching up. Wanting a child to reset its parent is the
signal that the moment belongs one level up; the child's condition gets there as a fact, never as a
reference.

A moment in one scope that must clear a SIBLING is the same rule. The acting scope adds one to a
counter declared on their common ancestor, and the sibling declares a trigger gated on
`CurrencyExceeds(counter, its own copy)` whose list is `ResetScope` of itself followed by an
`AddCurrency` paying the copy the counter's balance. The reset clears the copy and re-arms the
trigger; the deposit after it lands in the fresh state and closes the condition until the counter
moves again. Several scopes may add to one counter, so several prestiges can each clear their own
scope and a shared one: reset sets that overlap, with one rung per scope and no reset reaching
sideways.

**`AddModifier`** counts a stack under the modifier's id in the target scope's
`modifierStacks`. A modifier is declared content like everything else — `ScopeDefinition.modifiers` —
and a grant may only name one the target scope can reach outward, so the read resolves the stored id
by the same walk every reference gets. The numbers stay in the `ModifierDefinition` — a named `List<Effect>` with a
**`stacking` enum: `Replace | Linear | Multiply`**. `Replace`: a re-grant keeps count at 1;
`Linear` / `Multiply`: a re-grant increments count, and the name picks the count-scaling formula
(`1 + (m−1)·n` vs. `m^n`) — duplicate-grant policy and growth are one closed choice. A modifier may
also declare an optional **`appliesWhen`** condition, judged at gather time at the node the modifier
is APPLIED to - the link's own node, the one holding the stack or the permanent membership, which is
the site validation judges it from - with the querying context's circumstance and clock riding the
rebase (formulas are the opposite: they compute against the origin, since a multiplier is addressed
to a number whose identity includes its chain, 12.6): a modifier excuses
itself from circumstances it does not apply to - a live-only buff, or section 9's idle fraction, which
applies only during idle accumulation. Absent means always; the Effects themselves stay
unconditional, the timing lives on the carrier. The entry is
the fact, saved and cleared with its scope. Reserved for grants from
*moments that leave no other trace* (an event reward); when a count already exists as state, derive
from it instead (§12.6). **`RemoveModifier`** is its exact inverse: decrements one stack, deletes
the entry at zero, no-ops when absent.

A scope may also list **`permanentModifiers`** — a USAGE list, the parallel of an `AddModifier`
grant minus the moment: each entry references a modifier declared on the reachable chain (ownership
stays where it is), and the gather reads it directly — nothing granted, nothing saved, reset-immune.
Root's apply to everything; a chapter's apply exactly to its own chain, which is the
chapter-unique-buff case. Permanent membership contributes an implicit application count of 1,
MERGED with the scope's stored stacks for the same modifier and resolved through the modifier's own
stacking kind — `Replace` means permanent-plus-granted is still one application, so the two paths
can never double-apply outside the vocabulary. The Roadie boosts of 8, inside root-declared
modifiers, and each chapter's prestige modifier of 5, inside a modifier its chapter declares, are
this list's first content, as formula-shaped effects (12.6).

**A rung** is `{offerCondition, List<Action>}` — the one shape behind the album release (declared
on its tier) and the capstone (declared on the chapter): a rung of the prestige ladder. Its
presentation is its own content too: `label`, the prestige card's title, and two optional texts,
`info` (what the card's (i) opens) and `confirmText` (the sentence the confirmation window asks
before the rung runs, naming what it loses), JSON keys `label`, `info` and `confirmText` on the rung
object (12.11). Neither optional text is validated: empty is how an author says the card has no (i)
or asks nothing. Event
lifecycle commands are **not**
rungs - they execute authored `onEntry` / `rewards` / `onEnd` lists through the same action machinery
(§6.1). Every invocation
is **fail-closed against the rung's own gate**: `TryRung` (the UI entry point) checks the offer
condition before executing, and **`ExecuteRung` runs another rung's action list through the same
check — gate met, it executes; gate unmet, it no-ops.** There is no bypass: a payout is only
reachable through its own gate, so an unfinished run is discarded by whatever reset follows, never
banked. Reach is self or enclosed (12.12), so rung references only ever point down and the one cycle
possible is a rung's list naming its own rung, through `ExecuteRung` or `RestartScope`; that is
refused at load and thrown at Build, since the recursion it would start is unbounded. A rung is declared on `InteriorDefinition`
(§12.3), so the root has no field to hold one - unauthorable rather than refused.

**PayoutFormula** — a polymorphic family computing an amount from readable state
(`floor((fans/5)^0.5)`, piecewise diminishing-returns curves). Pure functions, so UI previews call
the same code the rung runs. `RootCurveFormula` names which of the currency's three totals it reads
- `reads: EarnedThisRound | Balance | Lifetime` - and the default is what this round earned, since a
prestige payout is about the run just played and a spend never lowers it; `Balance` is what a spend
left standing, `Lifetime` the total across rounds (§12.3). Chapter 1's album reads fans earned this
round, the same number as the balance because fans are never spent and both clear with the tier.

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

**The candidates for every multiplier are fixed when the tree is built; only liveness is read at
gather time.** Which effects can ever apply to a number - this owner, this currency, this stat, from
this origin - is authored and validated, so the load pass answers it once and files the answer as a
coordinate plan at the node the question is asked from (12.2, 12.14.8). `GetMultiplier(origin, plan)`
multiplies the plan's links in the order the compiler fixed: no chain is walked and no selector is
compared at gather time. What each link reads every time is the FACT - purchased, stacked, filled,
recorded, `appliesWhen` holding - which is exactly the fact column of the table below, and exactly
the half a plan does not fix.

A scope names its own carriers in ONE enumeration, and that order IS the multiplication order at a
node: upgrades, permanent modifiers, granted modifiers, bar cascades, generator thresholds, then
handicaps, each kind in declaration order and effects in declaration order within a carrier (a
levelled upgrade's level effects follow its own, level by level). The enumeration names no source
from outside, so a kind of scope with a source the others lack adds it in one override rather than as
another read inside a walk. That is what lets `events` live on `InteriorDefinition` (§12.3) with
nothing on the base naming them - the interior scope folds its handicaps in, and root, having no
override, contributes none. The same shape covers what else is compiled off a scope: the sources it
declares are one enumeration too, each producer and generator carried with the count fact that scales
it, and the declaration walk asks each scope whether it declares a definition rather than reading a
named list off it. Across the tree the sources are:

| Source | The fact (stored) | The effects (derived on read) |
|---|---|---|
| Purchased upgrades | `purchasedUpgrades` set; `upgradeLevels` for a levelled upgrade | the upgrade definition's `List<Effect>`; a levelled upgrade's are each level's, live while the owned level is exactly that level (section 4) |
| Owned generators | `generatorCounts` | `produces` entries scaled by count (contributions, not effects) |
| Timed records (Encore) | `{buffId, expiresAt}` list, the id a declared TIMER's | none directly - a record is a timer's value, a FACT the `BuffActive` condition reads through the modifier that names the timer and its band, and the modifier is a permanent membership with that condition in its `appliesWhen` (§9), the idle fraction's shape |
| Active events | an `ActiveEvent` record exists | the named event's handicaps, read through the declaring scope's `events` |
| Granted modifiers | `modifierStacks` counts | the `ModifierDefinition`'s effects, per its `stacking` enum |
| Repeating bars | `fillCounts` | the bar's `perFill` effects applied count times, read through the declaring scope's `bars` |
| Generator thresholds | `generatorCounts` and `grantedCounts` | each threshold's effects once the owned count reaches its count, read through the declaring scope's `generators` (12.2) |
| Permanent modifiers | none - the `permanentModifiers` usage list is authored, not stored (12.5) | the modifier's effects at an implicit application count of 1, merged with granted stacks; the factors of 3/5/7/8 (a prestige currency's balance, the Roadie allocation) live here as formula-shaped effects |

**An effect's factor is a constant or a formula**: the authored `multiplier` when no formula is
present, a `MultiplierFormula` computed on read when one is - every effect site (upgrades, granted
modifiers, handicaps, cascades) carries the capability, and count scaling composes on the computed
value. A `MultiplierFormula` computes against the **gather-origin** context - the source's scope in stage 1,
the currency's home in stage 2 - never one rebased to where the effect is declared. This is a
deliberate asymmetry with §12.4, where conditions and action lists evaluate in their declaring scope:
a multiplier is addressed to a NUMBER, and the number's identity includes the chain it resolves on.
A root-declared formula that reads chapter state is unimplementable any other way. Reads stay
chain-only either way, so no sibling reach opens up - and it is why an effect whose formula depends
on the producing chapter must target the SOURCES rather than the currency total (§8.2).

The fill-count row is for bars with a `repeatWhen`, the ones that can complete more than once: a
bar with none completes once, a moment that leaves no count to scale, so its reward is an
`AddModifier` grant and a `perFill` entry on one is an error (12.12). Like the upgrade row, the read walks the declaring scope's own list, so a stray count for a
bar that scope never declared cannot contribute.

Every row is the same pattern: a fact in state, effects computed from it. All of these exist from the
first minute of the game and contribute 1× until their facts exist. Nothing in this table is ever
serialized except the facts column.

### 12.7 Bars

Generic fillable bars: pacing bars (learn covers), currency bars that go again, cascade bars.

```csharp
class BarDefinition : Definition
{
    List<ConsumesEntry> consumes;    // what it drinks per unit of fill: {currency, amount}; empty = fills from time alone
    BigDouble    fillAmount;
    BigDouble    fillRate;        // this bar's own fill speed (units/sec)
    Condition    repeatWhen;      // null: fill once, stay full; true: pay, keep the residual, go again;
                                  // false: the manual team - pay once, zero, leave the active set
    ProducerDefinition tap;       // the producer the row fires when tapped (12.11); null: not a tap target
    GeneratorDefinition level;    // the generator whose owned count is the bar's level (12.11); null: no level
    UpgradeDefinition automation; // the upgrade whose purchase makes the bar go again; null: nothing automates it
    Condition    availableWhen;
    List<Action> onComplete;      // fires at each threshold crossing
    List<PerFillEntry> perFill;   // cascade: {effect, growth}, applied fillCount times on read
}

class GroupDefinition : Definition
{
    int              maxActive;
    List<Definition> members;     // declared on the group's OWN scope: bars, generators, producers, currencies, upgrades
}
```

**A bar is declared on its scope** in `bars`, like a generator, and its progress and fill count are
homed there.

**A bar names its level and its automation**, both ordinary references resolved outward from the
bar's scope like `tap`. `level` is the generator whose owned count is the bar's level: a row that
shows the bar with its level reads the count, the cost and the buy buttons from it. `automation` is
the upgrade whose purchase makes the bar go again by itself - the purchase is what flips
`repeatWhen`. Neither is a second truth: what a completion pays is still its `onComplete` list's
answer, and whether the bar IS automated is `repeatWhen` holding at its home, never whether the
upgrade is bought. Validated as `tap` is, each on the bar's chain; `automation` on a bar with no
`repeatWhen` is an error, since a bar that fills once has nothing to make go again.

**A level carries the bar's thresholds.** A levelled bar's milestones are its `level` generator's
count thresholds (12.2): "at level 25, the next gig's payout x2" is a threshold on the draw whose
effect names that gig's draw at `yield`, and a speed milestone, "at level 10, this gig x2 speed",
would be one naming the bar at `rate`, which chapter 2 does not author. The bar declares nothing
for them; the row that shows the level reads the next one for its Buy Next count and watches the
count for crossings to toast (12.11).

**What a completion is worth** is two reads beside the settlement, depositing nothing.
`BarSystem.CompletionPayout` answers what the `onComplete` list would deposit to each currency if
it ran now: a `FireGeneratorYield` entry through the resolution its firing commits
(`Producer.ResolveGeneratorYield`: the owned count and every multiplier), an `AddCurrency` entry
through its `AmountFor`, one line per (currency, home), summed, zeros kept. Only currency targets
are income, so a yield entry naming a bar or a generator is left out. Every entry is sized against
one state, so a list whose later action reads what an earlier one deposited is where the preview
and the execution part; no authored completion list has that shape. `BarSystem.CompletionRate` is
that payout times completions per REAL second - the bar's fill speed (`BarSystem.FillRate`, the
single-bar form of the resolution the draw uses) times `game_speed`, over `fillAmount` - and it
answers whether or not the bar is drawing, from its nominal speed even when it consumes.

**A group is a set of members with an active set.** A group is declared on a scope in `groups` and
lists members that scope declares - a bar, a generator, a producer, a currency, an upgrade - so a
member and the `activeMembers` set that holds it share one home and one lifetime, and a definition may
be listed by more than one group. A member of a group is OFF unless the group's active set holds it,
and one listed by several groups is on only while every one of them holds it. Membership is read at
one seam per kind, each an existing check with one more clause: a bar draws only while on; a
generator's terms are zero while off and its fired yield pays nothing; a producer fires nothing while
off; a currency reads inactive while off; an upgrade's effects gather only while on. Buying is not
gated. Which groups list what is compiled once per node when the tree is built, so every seam reads a
link (12.14.8). A group caps how many of its members are active at once and owns nothing else: no
currency, no throughput, and, owning no number, it is not an effect target - `maxActive` is an int
outside the multiplier system, and buffing a set is a TAG its members share, the mechanism that
already fans one effect out to many owners.

**Consumption is a list on the bar.** Each `consumes` entry names a currency and an amount per unit of
fill. A drawing bar wants `fillRate x dt`; each entry covers `balance / (amount x factor)` units, the
fill is the smallest of the want and every cover, and each entry then spends `fill x amount x factor`
from its currency's home, in the deterministic order below. An empty list is a bar that fills from time
alone. Nothing banked in any one entry fills nothing, and the bar stalls until production refills it.
`consumption` is a stat (12.2): an effect naming the bar by id or tag, with `currencyId` the consumed
currency, multiplies that entry's per-unit price - `{target: cover_1, currencyId: rehearsal, stat:
consumption, x0.1}` is "this bar consumes 90% less Rehearsal", a wildcard on the stat is efficiency on
everything that drinks, and zero is legal and means free. Stage 1 only, as `cost` is: a buff on the
consumed currency's total means its SUPPLY and never scales what drinks it.

**A bar's fill rate resolves stage 1 ONLY**, at the coordinate (bar, no currency, `rate`); a rate paid
into the bar collects the same plan, so "10x this bar" speeds its own fill and what is paid into it
alike. Stage 2 is "effects on this currency's total production", and a bar CONSUMES rather than
produces. Per-bar speed is buffable by bar id or tag, and by the rate wildcard, which reaches a bar's
fill as it reaches a currency's total (12.2); an effect narrowed by `currencyId` addresses the
`consumption` stat, never the fill rate, which carries no currency coordinate.

**A null `availableWhen` on a bar is OPEN**, the opposite of a purchase gate: fail-closed binds entry
points that create value out of a spend, and a bar's availability is a selection filter.

**A bar drinks what it names.** Each on, available, unfilled bar wants `fillRate x dt` and takes
what its tightest entry covers, in the deterministic order below. When a currency cannot cover its
bars, the ones later in the order simply stall - which is correct feedback, not unfairness: the amount
delivered per second is the inflow whatever the split, so dividing it proportionally would deliver no
more, it would only replace one bar visibly filling with three inching. A buffed bar drinks
proportionally faster.

**Activation is the player's fact**: `activeMembers` per group, at the group's scope; empty = nothing
the group lists runs, and a consumed currency accrues undrunk. `SetActiveMembers(group, set)` is
**fail-closed** like every entry point (12.11): it rejects a set that exceeds `maxActive`, names a
member the group does not list, names a bar whose `availableWhen` is false, or names a completed bar
that has no `repeatWhen`. The select control TOGGLES: pressing an off member adds it, pressing an on
member removes it, and adding into a full group is refused - the player deselects first; one behavior
for every cap. On a fill-once completion the stream **stops**: the completed bar leaves the active set
of every group listing it, as the manual team does, so its slot is free for the next choice - choosing
is the mechanic in Ch. 1, and letting more run in parallel is `maxActive` rising, which is an unlock
rather than a multiplier.

**Completion is derived**: complete ⇔ `progress ≥ fillAmount`. For a bar with no `repeatWhen`,
progress is monotonic until reset and **uncapped** — overfill is allowed and readable — so the crossing happens
once, and that is when `onComplete` fires. No completed-set is stored; `BarsCompleted(n)` counts
bars at full. **Whether a bar goes again is its `repeatWhen`**, judged at its home the moment it
completes; one condition, so an upgrade or a handicap flips a team between the two shapes below
without a second field. **A bar whose `repeatWhen` holds settles a tick iteratively**: while
`progress ≥ fillAmount` and the bar is still active and available, subtract `fillAmount`, increment `fillCount`, execute
`onComplete` — re-reading state each iteration, so a completion action that resets the host or
flips availability stops the loop honestly instead of executing precomputed fires against a changed
world. Residual progress is retained; a buffed rate crossing several thresholds in one tick pays
every crossing. (The arithmetic shortcut `fires = floor((progress + Δ)/fillAmount)` is a valid
optimization only when `onComplete` cannot affect the bar's environment, which in practice means an
EMPTY list; a bar with actions iterates.) **A `repeatWhen` that refuses is the manual team**: the
crossing pays once, progress returns to zero - the excess past the threshold is discarded, the team
ran one cycle - the bar leaves the active set of every group listing it, and its fill count is bumped as a repeating bar's is,
since a present `repeatWhen` counts completions whichever way it evaluated. Selecting it again is
how the player runs it again; there is no new command.

**A bar is a payment target** (12.2), and it collects what pays it the way a currency does. A rate
entry naming a bar is collected by the bar at its draw beside its own `fillRate`, so it fills only
while the bar is drawing - selected, available, not full - and the rate phase deposits nothing into
a bar. A yield entry naming a bar (a tap's, a fired generator's) moves progress the moment it is
paid, and the bar settles its own crossing right there through the same settlement the tick runs,
because the tick settles only the bars its draw admitted and would read a bar filled between ticks
as one that fired earlier. Each mover settles only the crossing its own fill made - a settlement
compares the progress before its fill with that fill, never with live progress - so a payment
landing inside a tick's settlement fires once and not again when the pass reaches that bar.
Clamped at `fillAmount` for a bar with no `repeatWhen`; a bar with one takes the whole payment and
settles by the condition's answer - every threshold crossed, or the manual team's one cycle. Membership and availability do not gate a paid
completion - they govern drinking, and a payment is not a drink. A refused completion
list leaves the payment undelivered, as the draw excludes the bar for its segment.

**The snapshot admits; live state may only disqualify.** Every rate and gate the draw needs is
resolved BEFORE the segment's production deposits (12.9), and only the bars that snapshot
recorded as drawing enter settlement at all. The per-iteration re-reads may take a bar OUT of the
loop; they may never put one in. Without that asymmetry the seam has a second door: a repeating bar
can sit at full progress with its gate closed - its own `onComplete` shut it last segment and the
residual is retained, and a save can load in that state - and a live-only test would let this
segment's deposits open the gate and pay the whole backlog. Both the draw and the
settlement run in one deterministic order — scopes in tree order (parent before child), then bars in
declaration order — and a reset during settlement invalidates the
remaining completions from the old scope-life, exactly as the trigger sweep rule (§12.5).

**Cascades** (bar B buffs bar A per fill): B declares `perFill: [{effect, growth}]` — e.g.
`{{target: barA, stat: rate, ×1.05}, multiply}`; the applied count is B's `fillCount` — the same pattern as
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
catches a goal reached by a command outside any tick. Both callers walk the same set - root plus
the foreground chapter's subtree: root never resets and is on every chain, while a dormant chapter
waits, so a threshold crossed while away fires on the first sweep after switch-in.

`StartEvent` / `DismissEvent` are the two self-guarding commands. Start checks `availableWhen` and
the empty host, runs `onEntry`, then creates the record - after the list, so an `onEntry` that
resets the host puts the record in the fresh payload. Dismiss checks that the host holds a record
**for the named event** - a sibling's record is an ordinary refusal, not a licence to pay this
event's reward - then **removes it first**, then runs `rewards` if `goalReached` was set, then runs `onEnd` either way.
Removing first is what opens a rung gated on `Not(EventRewardPending)`, so an `onEnd`
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
(consumed currencies included) → bar
consumption (§12.7) → iterative bar completion — production before
consumption, so an empty consumed currency fed at +1/sec serves a 1/sec bar demand in the same tick.
Bar DEMAND is resolved BEFORE the deposits, from the same start-of-segment snapshot as everything
else; the consumed currency's BALANCE is the one thing read live, which is exactly what that carve-out says. Resolving demand after
the deposits would let a segment's own production open a bar's gate and draw for the whole dt — then wall
clocks advance to the segment boundary for every running timer in the swept set - root plus the
foreground chapter, the same set the sweep walks. Root sweeps for its triggers but cannot host an
event (§12.3), so only the foreground chapter's subtree contributes timers, and a dormant
chapter's does not advance. No event can be born mid-segment now that starting one is a command
rather than an action. A handicap or buff live at segment start governs the whole segment, expiring
only at its edge. After the last segment, the tick's one purchase phase: every generator in the
swept set - root, then the foreground subtree in tree order, each scope's generators in declaration
order - whose `autobuy` switch reads on (§12.2) buys the largest count it can afford through the
same path the row's button takes, so every segment's bars have drunk before anything is spent and
the sweep at the close sees the buys; a refusal is a no-op, every tick is the only rate, and the
idle claim buys nothing. Then the sweep, commit, and refresh (§12.11).

**The tick report** is what ONE tick moved, recorded at the mutation sites and never as a balance
delta, keyed by the scope object plus an id, over the tick's real seconds. It holds three ledgers:
the deposit net per (home, id) - the rate phase's deposits less the draw's spends; the fill per
(bar scope, bar id); and the completion income per (currency home, currency id). Completion income
is recorded at the draw: for every bar that actually filled in the segment, its one-completion
payout times the fraction of a cycle it filled, the payout sized with the bar's rate in the
start-of-segment snapshot, before the deposits and before its own completions. A bar that is not
drawing records nothing, and a stalled bar records what its short fill is worth. Each ledger reads
as a slope per real second, and for a running bar that does not stall the completion slope over a
tick is its `CompletionRate`. The deposit slope never carries a completion's lump: the lump lands in
bursts at the crossing, and the currency readout interpolates by the deposit slope between ticks.
The idle offer builds no report, and its lines carry no completion income beyond the completions
they pay.
`lastActiveUtc` stamped when leaving a LIVE chapter — switch-away or backgrounding — and, for the
foreground chapter only and only while Live, on save (a save under the dialog must leave the
unpaid window); every write is monotonic (max), so a rolled-back clock can delay
a stamp but never regress one. **The stamp IS the pending claim** — nothing about an offer is ever
saved. Switch-in computes
`idleRate x game_speed x paid time` per rate target - a currency, or a generator's granted count - and,
per segment after that segment's rate lines, every on bar of the chapter's subtree in settlement
order: its fill over the segment at its own rate under the same context (so root's idle fraction halves
it as it halves every rate), capped by what it consumes - each consumed currency's balance at the
stamp plus the window's inflow so far, less what earlier bars drew - the completions that fill crosses
by the bar's `repeatWhen` (fill once and stop drinking; every threshold with the residual kept; the
manual team's one cycle, then off), and the payout each completion's `FireGeneratorYield` resolves,
times the completions, into the lines. What the bars drew is the offer's own list of DRAWS, one per
consumed currency, and each moved bar is a bar entry (carried progress, completions, whether the manual
team went off); progress is carried whether or not anything crossed. All of it at current rates over the paid window - the first
`min(elapsed, cap)` REAL seconds after the stamp, segmented at the buff expiries inside it by the
tick's own segment walk (`TickSystem.Segments`, the one place a window is divided) and reading
`game_speed` through the tick's own clamped read, each segment paying its own length at the rate
and speed live in it; the cap
bounds real seconds, and speed multiplies what they pay - `idleRate` and `game_speed` being the
ordinary gathers under the idle-accumulation context - skipped below the minimum-away
threshold and skipped entirely while that chapter holds a record for an event that blocks idle
(§6.1: `blocksIdle` is derived from the event carrying a timer, and the idle path asks the event
rather than inspecting one) - into a **transient offer** for the idle dialog: the lines
(target, home, amount - all references) and the window's end B, computed once
over the explicit window [stamp, B], held by the session, never serialized. The lines hold what is
paid: a Pass owner's are computed doubled, and the ad callback doubles them before it settles. The claim never
computes anything: it deposits the stored lines as they stand, spends the stored draws at their homes
(a deposit is a grant and a draw is a spend, so the earned total moves by the inflow alone, 12.3),
writes each bar entry - progress, the fill counts the crossings added, the manual team out of every
group listing it, and its `onComplete` once per completion with the `FireGeneratorYield` actions skipped,
since those were the lines - and advances the stamp to B, the window actually paid, in one
transaction. The dialog prints one row per balance the claim changes, the line less the draw, signed;
bar progress is a write and never a row. A window that changed no balance - a bar part-way through a
cycle, a bar that drank exactly what came in - settles on entry with no dialog: the same claim, the
progress and the stamp written, nothing shown, because the dialog exists for a balance the player is
owed and is never empty. Double It and the Pass double the lines alone: twice the Cash, never twice
the cover. That is the
whole exactly-once mechanism: the save is the tree, so a kill anywhere keeps both writes or
neither. A kill or backgrounding with the dialog up destroys the offer and leaves the stamp, so
the next entry recomputes from the same window start — the unpaid window simply stays open,
capped — and what is shown is always what is paid, because only one offer is ever alive. A
SETTLED window ends at B: time past B is foreground presence, never idle — the next live exit
stamps over it.
The idle fraction rides the rate gather via `appliesWhen` (§12.5), the cap is a
`GameConfig` threshold beside the minimum-away one, and `game_speed` is gathered once per
segment, by the tick and the claim alike — the same gather as everything else. Triggers (§12.5) are swept inside each transaction — after its mutation, before
commit — for ticks and commands alike; live scopes only, single pass. The sweep is conditional on
the transaction's RESULTING phase: only a transaction ending in `Live` sweeps. One ending in
`AwaitingIdleClaim` or `NoChapter` commits and refreshes without sweeping — an offer awaits its
dialog, and any sweep (root included, since a root trigger may legally reset a descendant
chapter) could reset the chapter, re-stamping the unpaid window away — and the transaction that enters `Live` performs the deferred sweep. The same sweep observes
event goals **from the sweep-start snapshot and latches `goalReached` before any trigger
actions execute** — success is judged on the transaction's own mutation, never on trigger
payloads; a trigger that spends the goal currency changes nothing already secured, and its effect
on goals is seen next sweep (§12.8).

**GameSession** — the transient execution context, never serialized:
`{foregroundChapter, phase: NoChapter | AwaitingIdleClaim | Live, currentOffer, commandQueue}` — launch
and backgrounding are `NoChapter`, so no-foreground states are explicit rather than a null reference.
Durable facts live in the tree; the session holds only orchestration. The CURRENT chapter is one of
those durable facts — a root-held id written by `SwitchChapter` when it enters a chapter
(backgrounding leaves it: it names where play left off, which is where boot returns), so it travels
with the save, cloud restore included. Boot auto-enters the recorded chapter and offers no
selection; a save with no recorded chapter - a fresh game - enters the first chapter, root's
roster head, and such a save owes no idle to conflict with. The chapter select is the switcher
over Live chrome, and the screen only while the session is `NoChapter` - which boot reaches only
if the switch refused the entry as locked. That is what makes an unpaid window unstrandable:
every load lands on the chapter whose stamp holds it, and the offer recomputes. Never
inferred from economy timestamps — `lastActiveUtc`
records idle-settlement boundaries, not UI history. While `phase == AwaitingIdleClaim` the
sections are down under the dialog, so no widget exists to issue a chapter command and nothing
can reset away an unpaid window; settling flips the session to `Live`. Authenticated ad/store
callbacks are ordinary submissions to the command queue: the same pipeline as any command, its
sweep conditional on the resulting phase like every transaction's - and because a submission may
run at the frame's drain rather than at the call, the save that follows a grant, and the store
acknowledgement that follows the save, run in the grant's completed callback, never at the call. The idle double is ATOMIC with its
settlement: the rewarded-ad callback doubles and claims in one transaction, so a doubled offer never sits
exposed to a backgrounding's drop (a kill mid-ad takes the callback with the process; the stamp
never moved, so the next launch offers the window again), and the transaction ends in `Live` and sweeps
like any claim. A Backstage Pass bought FROM the dialog is the same callback with one more write:
record the entitlement, double the offer, claim - one transaction ending `Live`, so the
dialog closes with the phase, and a kill between the store's confirmation and the claim leaves the
stamp unmoved, the entitlement to be restored from the store, and the next offer doubled by it. A
callback that leaves the phase alone (an entitlement written mid-dialog by any other path)
sweeps nothing yet still repaints the dialog, because the refresh is unconditional: the offer it
shows stays what was computed and is what OK pays - shown and paid never differ - and only the
button set changes.
Callbacks are not UI commands (§12.11). **One pipeline runs every command, as one queued
transaction**: the flush, the command's own check and mutation, the sweep when the resulting phase
is `Live`, one refresh. **The command queue**: `RunCommand` appends the transaction and returns.
When the entry is the only one it runs at once - an optimization no call site relies on; a caller
that needs the command's answer passes a completed callback and is told when it ran. The frame
drains the entries present when the drain starts, in order, each left at the front until it
returns, so a transaction submitted from inside a running one - a refresh handler, a trigger
action - lands behind it and runs at the next drain as its own transaction with its own refresh.
Nothing nests, so nothing guards. The session tests neither the phase nor the acting scope: widgets exist only for the
foreground chapter while it is `Live`, and a dormant chapter is never executed, so no caller can
present a scope outside the foreground subtree.
**Switching away settles first**: the switch transaction deposits the outgoing chapter's
outstanding offer as it stands (switching is an exit path, §9: a free player's was never doubled, a
Pass owner's was computed so), advancing the stamp to
the offer's window; leaving a live chapter just stamps out at now. Backgrounding is the exception:
it drops the offer and leaves the stamp, so backgrounding and an app kill behave identically and
pending dialogs never accumulate across chapters.

### 12.10 Save

Serialize the ScopeState tree, nested as the scopes are — **and nothing else**: every root fact
(Roadies, the allocation, entitlements, timed buffs, the current chapter, Discography) is
a field of the root
scope's state (§12.3), so principle 2 is literally the save format. JSON + checksum, validated on
load (ids resolve; unknown ids from removed content are dropped with a warning). No grants, no
derived values, no replay of actions. Idle payouts are capped client-side. A levelled upgrade's
level takes the same filter plus its range: one naming an undeclared upgrade or one with no levels,
or below 1, is dropped; one past the last level is clamped to the last, since the purchases it
records were made. The latch then follows the level - a latch with no level is dropped, a level
with no latch has it restored - each with a warning.

A schema version becomes a compatibility contract the moment a build that can write it reaches a
player. Before that, format changes revise the current version in place and dev saves are deleted
rather than migrated; after it, every format change bumps the version and registers a migration.

Production hardening: the save carries a `schemaVersion`, and loading an older version runs explicit
per-version migrations — never silent best-effort parsing. Writes are atomic (write temp, verify,
swap) and keep the previous save as backup; a checksum failure falls back to it. A negative clock
delta (device clock moved backwards) clamps elapsed time to zero — rollback can extend a buff's
`expiresAt` wait, but it never mints currency.

### 12.11 UI

**Authored layout**: a chapter's screen is an ordered list of `SectionDefinition {title?,
description?, visibleWhen, scope, layout, modules}`; a `ModuleDefinition {prefabId, content?, visibleWhen?, scope}` binds
a widget to content. Both are inline serialized data on the `ChapterDefinition`, and both reference
by direct object field (12.14.5): `scope` is a `ScopeDefinition`, `content` a base-typed
`Definition` (a producer, a currency, an event, a group - whose `group` module renders each member
with the row its kind already has plus the control that toggles it, and one readout per currency its
bar members consume, 12.7), and only `prefabId` stays a string, since it names
a widget through the registry rather than a content asset. A section's `scope` is its **evaluation
scope** - the context its conditions read from, which is how a chapter-owned section legally gates
on a tier-declared flag. A module's `scope` is always concrete: the JSON may omit it, and the
importer normalizes to the home scope of the bound content **when that home lies within the
chapter's subtree, else to the chapter itself** (root-owned content like Roadies is readable from
any context - root is on every chain), so the runtime computes no default. Validated at load: the
chapter itself or one of its descendants. A section's and a module's scope is LINKED at the chapter
node when the tree is built (12.3), keyed by the `SectionDefinition` or `ModuleDefinition`, and
that reach rule is checked at the link rather than thrown at a chapter build; `ScreenHost` reads
`chapter.Link(section)` and builds over the node it names.
The `ModuleRegistry` is a hand-made ScriptableObject
mapping each `prefabId` to a `VisualTreeAsset` held by direct reference - the UXML is the UI
Toolkit shape's "prefab" - so the widgets load with the scene as the registry's own dependency
graph and instantiate synchronously mid-refresh; the behavior is a plain C# `ModuleWidget`
controller a code-side factory constructs for the same id. A new widget type is a UXML, a factory
line, and a registry entry. Every `Definition` and every section may carry an optional
`description`, player-facing text saying what the thing does; the widgets that render a row or a
band show it beneath the name or title when present, a generator's is read on its info screen
instead, and nothing else reads it.

**A section's title is optional, and its layout is one of two.** The section view draws the title
and the description each only when authored, so a section with neither is a bare card of modules.
`layout` (`SectionLayout`, JSON key `layout`, `List` when absent) arranges the modules container:
**List** stacks the visible modules down the card; **Strip** puts them in one row inside a sideways
`ScrollView` (`SectionView.Strip`) with a thin scroll bar always drawn beneath and no vertical
scroll, each module keeping its own width. A module's `visibleWhen` works the same in both, and a
module that turns visible late lands in its authored place. The strip carries its own
`DragScrollManipulator` on the horizontal axis beside the page's vertical one; one press is heard by
both, and the axis that has travelled further owns the drag (`DragScrollManipulator.Starts`, a tie
going to the page), so a sideways drag on a tile scrolls the strip and a vertical one the page. The
view whose drag starts captures the pointer, and each view hears every capture of a press it is
tracking on its panel's root: the strip taking the pointer ends the page's tracking of that press,
and the page taking it ends the strip's, so one press moves one view.

**A section or a module turning visible during play is revealed** (Ctrl C's, 2026-11-12). The
host shows and hides each section's root and each built module's root through one helper,
`Reveal` (`SectionView.Reveal`, `ModuleView.Reveal`). A hide is at once. A show is at once on the
first pass over a built chapter - after bind, a chapter switch, or a return to Live - so a player
loading a save does not watch the screen assemble; on any later pass a hidden-to-visible flip
reveals: the element fades in from transparent while its size along the axis its container stacks
on grows from zero to what it lays out at, with the padding and margins on that axis, over
`Reveal.Seconds` (0.3 s) on an ease-out curve, so whatever follows it slides along to make room.
Down the page that is the height; a strip's tile grows in width. A length of auto cannot be
tweened, so the reveal lays the element out of flow and transparent for one layout pass, reads the
size it took, and grows it in flow from there on the element's own scheduler (panel time, never a
game read), clipping its content and keeping its children at their own size. A module showing in
the pass its section turns visible comes in with the section's reveal and has none of its own. A
hide mid-reveal cancels it, and a later show starts over. When a reveal ends every inline value it
wrote is cleared, so the element is exactly as it would be without one and a size its content took
mid-reveal is the size it ends at. `Revealing` and `Shown` say where it stands, and
`CompleteReveal` ends one at once - the last frame's state, for a headless test.

**Upgrade tiles.** `upgrade_row` is the full-width upgrade, a row like a generator's. `upgrade_tile`
(`UpgradeTileUI`, `UpgradeTile.uxml`) is the strip's widget: one upgrade bound by `contentId`,
resolved as the row resolves it, drawn as a rounded, outlined tile of fixed width - the upgrade's
own glyph when it names one (`UpgradeDefinition.icon`, JSON `icon`: the file name stem of a PNG
under `UI/Icons`, which the importer resolves to the texture and aborts on when no such file
exists), else the shared guitar pick (`UI/Icons/upgrade.png`, the stylesheet's default), either
grey and in the accent once owned (`ShownIcon` is the upgrade's own, null while the pick shows);
the display name on one line, cut with an
ellipsis; and beneath it the price with its currency's name, through `NumberFormatter`, or "Owned"
in the accent once owned (section 4: the latch, or the upgrade's `ownedWhen`, or a levelled
upgrade's last level). A levelled upgrade's name carries the owned level in parentheses once one is
owned, "Crowd Work (1)", and its price is the next level's; the row does the same, its description
the level it stands at (the first before any is owned). A tap issues the upgrade row's purchase and does nothing on an owned or
unbuyable tile. A tile that cannot be bought now is dimmed by a class (`tile-unbuyable`) and never
disabled, since a disabled element receives no pointer events and the hold is how a player reads
what the upgrade does before affording it. A hold (`GameConfig.longPressSeconds`, the same
`LongPressManipulator` Auto uses under the hold's rule, so a hold is never also a tap and a drag
that starts on a tile is never a hold) opens the **upgrade details
window**, a host-owned overlay (`UpgradeDetailsUI`) reached through `IUpgradeDetailsOpener` with the
upgrade and its declaring scope, as the automation window is: the title is the upgrade's name, one
paragraph reads "On buy: " and the description while it is not bought and "Current effect: " and
the description once it is, and "Close" in the accent at the bottom right closes it. A levelled
upgrade's title carries the owned level as its tile's name does; before any level it reads "On buy: "
and the first level's description, partly bought "Current effect: " with the owned level's and
"Next: " with the next level's, and at the last level only the current effect. A purchase made
beneath it turns the paragraphs on the host's refresh. An owned upgrade's tile stays on the strip:
content gating a tile on a fact that can fall gives it `Any(UpgradePurchased <itself>, ...)`.

**The prestige card.** `rung_button` (`RungButtonUI`, `RungButton.uxml`) binds nothing and presses
its evaluation scope's own rung, as `TryRung` reads it; it is Ctrl C's prestige card, a stack of
centred lines. The title is the rung's `label` with its gain, "Cut a Demo (Gain 3.00 Demo Tapes)",
one amount and currency per target of the payout preview (`RungFeedback.TryPreviewPayout`) joined by
commas, shown while locked too (it may read 0.00), and the label alone when the rung's first action
is not an `AddCurrency`; the title is greyed (`rung-title-locked`) while the rung is not offered.
Beside it a round (i) is drawn only when the rung authors `info`. Beneath it "Currently: " prints
each previewed currency's balance, read at the home the payout deposits to, hidden with the preview.
While locked the requirement lines follow in the accent text: one per unmet top-level leg
(`GateFeedback.UnmetLegs` and `LegText`, below), then the refusal line when the rung's actions are
refused (`ActionList.Refuses`, `RungFeedback.RefusalText`); each goes when it is met, so an offered
card shows none. Last is "Confirm", a centred text button in the accent while the rung is offered and
grey and disabled while it is not. Confirm on a rung authoring `confirmText` opens the **rung
confirmation window**, and on any other runs `TryRung` at once; on a rung not offered it does
nothing. A tap on the (i) opens the **rung info window**. Both are host-owned overlays reached
through `IRungOpener`, as the upgrade details window is: the info window (`RungInfoUI`) titles itself
with the label, shows the `info` text, and closes on "Close" in the accent at the bottom right; the
confirmation window (`RungConfirmUI`) titles itself with the label and a question mark, shows the
`confirmText`, and offers "Cancel" in grey and "Confirm" in the accent at the bottom right - Cancel
closes it, and Confirm closes it and runs `TryRung` at the scope the card handed over, fail-closed
as the card's own press is. Neither window reads state, so a refresh beneath them changes nothing.
The upgrade details window stays its own class: its paragraphs are judged from the owned state on
every refresh, which a window of fixed texts does not do.

**Rates on screen.** A currency's header line is the name, the balance, and the tick report's
realized income beside it as "(X/s)": the deposit slope plus the completion slope
(`TickReport.CompletionSlope`, what the fills of every drawing bar's completions are worth per real
second), both read at the currency's home, which the readout resolves once and exposes. It is zero
before the first tick and for a currency nothing pays. The balance itself interpolates on the
deposit slope alone, because completion income lands in lumps at the crossing; the pinned headline
copies the line's texts and so shows the same total. A levelled row prints its bar's earnings per
second, or per completion, inside the fill (below); the header stays per second either way. A
generator row is the name, the owned count - the purchased count in parentheses with the granted
count added inside them when there is one, `(3)` and `(3+8.89e11)`, the reference game's shape - a
line beneath the name reading the next unit's cost
and what that one unit pays - per second for its rate entries, per firing for the yield entries a
bar fires ("250.00 Cash => 3.00 Cash"; a draw's "5.00 Cash => 3.00 Cash, 0.50 Rep"), through
`Producer.UnitRate` and `Producer.UnitYield` at the declaring scope, and two buy buttons: "+1", and "+M" printing `Purchasing.MaxAffordable` at
the declaring scope, both disabled and both reading "+1" when M is zero, and both reading "+1"
when one unit is affordable. A button buys the count printed on it: the tap submits `TryBuy` with that count, the
command recomputes the series cost against the balance after its flush, and buys that count or
refuses whole - never a count the player did not see. The flushed frames may have moved the
balance either way (production up, a bar's draw down), and the refresh at the transaction's close
repaints both labels. A long press on the row's text - a hold of
`GameConfig.longPressSeconds`, judged by the element's own scheduler since it is presentation and
never a game read, under the hold's rule (**The hold** below) - opens the generator's info screen,
so a drag that starts on the text scrolls the page and opens nothing, and the pointer crossing
between the text's labels is still a hold. The screen is a host-owned overlay: the description, the
same cost and yield line, the owned count in the row's form, and what the owned units pay - per second for rates
("Producing: 3.00 Cash/s") and per firing for yields ("Pays: 9.00 Cash, 1.50 Rep per gig"), each the
owned sum times the per-unit figure because nothing in the effect vocabulary reads the count. The
row reaches the host through `IGeneratorInfoOpener`, the shape `IStoryOpener` has, and the screen
holds the generator and its declaring scope for as long as the request stands.

**The look is modelled on Ctrl C** (2026-11-10), in `Screen.uss` alone: sized for the panel's
1080 x 1920 portrait reference, one typeface (Roboto, whose digits are of equal width, so a counting
number does not jitter), neutral greys, and ONE accent colour, used on the Jam button, a bar's fill
and the text actions that commit something. A section is a rounded card on the page; a row is two
lines, the name and count above and the cost and yield in smaller grey beneath, with bare text
buttons at its right and no line between rows; a row that cannot be bought is dimmed whole
(`row-unaffordable`, `row-bought`, set by the row from the same answer that disables its button).
Windows are rounded panels over a scrim with their actions as text at the right. The widgets carry
no colour or size of their own.

**The headline and the pills.** The two pills are fixed at the top and never move - this is where
the screen departs from Ctrl C, whose pills fold into icons as the page scrolls. The section
carrying a currency header is the page's headline and not a card (`section-hero`), and the first header line
showing in it is the headline number (`currency-primary`): both classes are set by the host from
what is built and visible. That line is drawn by `PinnedHeadlineUI`, an overlay above the page and
beneath every window: UI Toolkit draws in hierarchy order with no z-index, so a line inside the page
could not stay above the cards scrolling over it. The page's own line keeps its space and is not
drawn (`currency-pinned-source`). Each frame the overlay copies that line's three texts and takes
its position from the line's own `worldBound`: over it while it is on screen, stopped beneath the
pills once the page has scrolled it away, shrinking in between to `PinnedScale`, with a strip of
page colour behind it as it arrives. It ignores picking, and it stays down for the frame in which
the page has not been laid out.

**The page scrolls by drag.** A `ScrollView` drag-scrolls for a touch or pen pointer alone, so
`DragScrollManipulator` gives the left mouse button the same gesture for the editor and a desktop
build, modelled on the `ScrollView`'s own touch drag. The page carries it, a strip carries it on the
horizontal axis (above), and so do the story log's and the Discography's lists, so every list that
scrolls drags. The down trickles to the view before the element under the pointer and is neither
captured nor stopped: a press is an ordinary click until it has travelled `StartDistance`. Once it
has, along the view's axis and further than across it (`Starts`), the view captures the pointer,
which takes the press away from a button that had it, so the release clicks nothing; the content
follows the pointer (`Offset`, clamped to the scroll range), and the drag's moves and its up are
stopped at the view. A pressed button captures the pointer itself, and a captured pointer's events
reach the capturing element alone, so while a press is tracked the view hears every capture of that
pointer on its panel's root: a child of the view taking it is hooked and its moves and endings are
heard on that child until it lets go; a nested scroll view or anything outside the view taking it
ends the view's tracking. The up, a cancel, the view losing the pointer and the view leaving its
panel end the press, and every ending lets go of the view's capture, dragging or not. A release can
be lost (Unity's UUM-138133), so a move of the tracked pointer with the left button no longer
pressed is an ending too, and a down that finds the view still holding the pointer lets it go first:
the page never keeps the pointer. The page's scroll bar is hidden.

**The hold.** `LongPressManipulator` is a press held still on an element: it fires once,
`GameConfig.longPressSeconds` after the down on the element's own scheduler, if and only if that
press is still down, the pointer has not travelled `DragScrollManipulator.StartDistance` from the
down, it is still over the element, and no element other than the element itself has taken the
pointer since the down. A press that travelled the drag distance is a drag, never a hold. The left
button arms it (a touch's primary press counts as one); the up, a cancel, a move heard on the
element that has travelled, the pointer leaving the element itself (a child's leave is not one), any
other element taking the pointer - heard on the panel's root for as long as the hold is armed, since
a dragging view's capture sends every later event to that view alone and the content moving with
the pointer keeps the element under it - the element losing the pointer, and the element leaving its
panel each end it. At the fire the rule is asked again of the panel: the pointer's capture is
nobody's or the element's, and the element under the last position heard is the element or inside
it. Firing ends the hold first, then lets go of a capture the element holds - a button's click -
and then runs the action. A hold is never also a tap, whatever the action opens: the panel tells a
button's click that it lost the pointer only after the next pointer event, and with nothing over
the button that event is the release, landing on the button while its click is still pressed, so
the hold stops the next up or cancel of the fired press heard on the element on the way down,
before the click hears it, and the click lets go on the loss of the pointer alone. The hold never
captures and stops nothing while it is armed, so a drag that starts on it still scrolls.

**A widget exposes what it shows as data, and no test reads a label.** Each widget that prints a
number or a state keeps it in a public read-only member set on the line that writes the label, from
the value that line formats: `ShownValue` and `ShownRate` on a currency line, `ShownPurchased`,
`ShownGranted`, `ShownMax`, `ShownCost` and `ShownYields` on a generator row, `ShownBought` on an
upgrade row, `Mark(member)` on a group (`MemberMark`, whose names are not the button's wording),
`Row(bar)`, `BookTarget`, `ShownBookCost`, `BookEnabled`, `AutoTarget`, `AutoEnabled`, `BuyControl`,
`Mode` and `Figures` on a levelled group with `ShownPurchased`, `ShownGranted`, `ShownProgress`,
`ShownRates`, `ShownPayouts`, `ShownCost`, `ShownMax`, `ShownCount`, `ControlKind`, `Idle`, `ShownLevelZero` and `Visible` on its rows, `Shown` and
`Queued` on the threshold toast, `IsOpen`, `ShownGroup` and `Rows` on the automation
window with `Bar`, `State` and `ShownCost` on each row, `Upgrade`, `ShownCost`, `ShownOwned` and
`ShownBuyable`, `ShownLevel` and `ShownMaxLevel` on an upgrade tile (`ShownLevel` and `ShownMaxLevel`
on an upgrade row too), `IsOpen`, `ShownUpgrade`, `ShownOwned`, `ShownLevel`, `ShownCurrent`,
`ShownNext`, `ShownCurrentLevel` and `ShownNextLevel` on the upgrade details window,
`PreviewShown`, `ShownAmounts`, `ShownCurrencies`, `ShownBalances`, `ShownUnmetLegs`, `ShownRefused`,
`InfoShown` and `ConfirmEnabled` on a prestige card, `IsOpen` and `ShownRung` on the rung info
window with `HeldScope` on the confirmation window, `ShowsClaim` on an
event, `ShownSeconds` and `ShownPermanent` on the pill and the Encore window, and their like. The
label and the member cannot differ, because one is formatted from the other. A test compares that
member as a number, a bool or an enum; an authored name on screen is compared against the
definition's own field; and wording the code owns is not asserted at all. No test compares an
element's text against a literal string (John, 2026-11-10): that pins a sentence to prove a
number, and breaks on a rewording that changed nothing.

**Modal dialogs block automatic story cards.** While settings, chapter select, Roadie allocation,
Encore, the story log, the Discography, a generator's info screen, the automation window, an upgrade's details window or a rung's info or confirmation window is open, the host does not run the automatic story walk. The modal and any unsubmitted
allocation draft remain intact. A waiting marked beat stays unseen and opens on the first refresh
after the last overlay closes, if it is still available. The walk reads state, not a transition.

**Refresh** is coarse, on two triggers: after each tick of the foreground chapter, and after every
**completed command transaction** (nested Actions never refresh individually — the outer
transaction publishes one final state change, so a rung's payout, flag, and reset render as one).
The full pipeline is fixed: **mutation → one trigger sweep → trigger actions → transaction commit →
one refresh** — the sweep runs inside the transaction, so a trigger's payload renders atomically
with the command that armed it (the sweep itself is conditional on the transaction's resulting
phase, §12.9; the refresh never is).
Visible sections re-evaluate `visibleWhen`; visible modules re-read what they show. Event-driven,
never per-render-frame; fine-graining is a mechanical optimization if profiling ever asks.

**Entry points** — the only ways the UI touches the game. Each mutation that depends on live state
is a `Can*` query plus a `Do*` command, with `Try*` as the convenience wrapper over the two: the
query is what renders pressability and the feedback text, the command performs the mutation and
refuses to run when the query says no. A reference that cannot resolve is never an answer either
one returns - static content cannot legitimately be in that state, so those throw. The set:
`IsOffered(rung)` / `ExecuteRung` / `TryRung(rung)`, `CanBuy` / `Buy` / `TryBuy(generator, count |
upgrade)`, `FireProducer(producer)`, `SetActiveMembers(group, set)`, the event operations
`StartEvent / DismissEvent (event)`, `SwitchChapter(chapterId)` (stamps
`lastActiveUtc`, computes the idle offer, §12.9), `ClaimIdle(chapterId)` (settle the
offer, §9 — the dialog's double button only *requests* the rewarded ad and its Backstage Pass button
only *requests* the purchase; doubling and settling the
offer is AdManager's or IAPManager's authenticated callback, never a UI call), `SetRoadieAllocation(map)` (nonnegative integers, Σ ≤ owned Roadies, unlocked chapters only), the Ch. 6 song operations (write / name), and
`AcknowledgeStory(beat)` (writes the beat's seen latch at its home through the `SetFlag` walk, §10;
it checks nothing of its own - the host opens a card only for a beat whose button is live or whose
mark pops it). All fail-closed — each checks
its own gate. Ad and store
callbacks (AdManager / IAPManager) mutate through their own equally fail-closed operations -
`ExtendBuff(scope, timer, seconds, cap)`, `RunReward(actions)` (root's authored reward list as one
transaction), `DoubleAndClaimIdle()`, `GrantEntitlement(id)` and
`PurchasePassFromDialog()`, `GrantRoadies(count)` - they are not UI paths.

**A disarmed rung explains itself**: the prestige card evaluates its gate's top-level legs
individually at the two refresh moments and lists the unmet legs' `Text` (§12.4) - a leg's own
`uiText`, or for a compound leg the one composition rule, so an `Any` inside a gate explains itself
without a hand-written line unless the author wants one; the gate's top-level `All` is rendered as
those rows and never as a text of its own. Threshold
kinds additionally expose current/target so a leg can render as progress ("37/50 fans"). The
widget reads the same condition objects the operation enforces — one implementation, no drift,
exactly as payout previews call the rung's own formula (§5). Feedback is per-condition, never
per-feature: any kind an author gates with explains itself for free.

**Widgets interpolate** displayed numbers and bar fills between ticks; presentation only.

**A bar row is a tap target when its bar names a producer** (`tap`, §12.7): a click on the row
outside the select button submits `FireProducer` on that producer, the same command the Jam button
issues, and the producer's yield entry naming the bar is how a tap adds time. Choosing stays the
button's and paying is the row's, so the button's own click is never also a tap.

**Two layouts for a group of bars.** The `group` module is the split layout: the bars in one list
with a select control each, and their level generators, if any, as `generator_row` modules
elsewhere. The `leveled_group` module (`LeveledGroupUI`, `LeveledGroup.uxml`) is the combined one,
Ctrl C's card of levelled bars, for a group whose members are all bars naming a `level` (12.7); it
binds a `GroupDefinition` by `contentId` exactly as `group` does, and a member that is not a bar or
a bar with no `level` throws at bind as a content fault. A section picks a layout by the module it
names. Each member is one row (`LeveledBarRowUI`), shown while the bar is available, as the group
module shows its bars. **The card is never a Book button alone**: while no member's bar is
available - the first entry into a chapter, and every run after a reset - the FIRST member in group
order shows as a row at level 0 (`ShownLevelZero`) when its level is offered, as Ctrl C's card
opens with its first bar on it: the name with its count of 0, an empty fill whose figures are the
ordinary reads and so zero, every line kept, and the buy control on its level, dimmed as any row's.
A press of that control is the Book button's pair - the purchase, then the bar added to the active
set - so the first hire starts the gig; the row reads whether it stands at level 0 before the
purchase, whose own refresh moves it off, and the add is fail-closed, so a refused purchase starts
nothing. Its fill takes no tap, since its bar is not available, and it is never Auto's target, its
gig not being hired. From level 1 it is an ordinary row. Each row:
- the title line is the bar's name and its level, the level generator's count printed by
  `GeneratorRowUI.CountText`; holding it opens that generator's info screen, as a generator row's
  hold does;
- the fill rides the tick's bar slope between refreshes as a bar row's does, and prints inside it
  the bar's earnings in the card's figure mode (`FigureMode`): **Per Second**, each line of
  `BarSystem.CompletionRate` at the chapter's `game_speed`, "27.60 Cash/s, 4.60 Rep/s"
  (`ShownRates`), or **Raw**, each line of `BarSystem.CompletionPayout`, what one completion pays,
  "30.87 Cash, 4.11 Rep" (`ShownPayouts`); zero lines kept, and the mode not printed leaves its
  member empty. The figures are set at each refresh and only the fill moves between refreshes;
- a tap on the fill starts a bar that is not running - `SetActiveMembers` with the bar added,
  never removed - and works a running one through its `tap` producer; a bar that fills once and
  stands full takes no tap. There is no select control. A row available and not running carries
  `leveled-idle`, which the stylesheet draws as a bar waiting for its tap;
- the buy control at the right is one of two, chosen by the module's `buyControl`
  (`BuyControlKind`, JSON key `buyControl`, `Pair` when absent), which the host hands the card
  before it binds. **Pair** is the generator row's "+1" and "+M" on the level, with its enabling and
  arithmetic, over the next unit's cost. **Cycle** is Ctrl C's: one "Hire: xN" button over the price
  of those N levels, N set by the card's cycle button below - Buy One is 1, Buy Max is
  `Purchasing.MaxAffordable` at the level's declaring scope (1 while that is zero), and Buy Next is the
  levels still needed to reach the level's next count threshold (`NextThreshold`; 1 when none is
  left). Nothing about a threshold shows in One or Max, and a threshold's effect never shows before
  it is reached. A press submits `TryBuy` with the N printed, fail-closed like every buy, and the
  button is dimmed by a class (`leveled-hire-unaffordable`) and never disabled while N is not
  affordable. Either way the row dims by `row-unaffordable` while what the control buys is not
  affordable. Each control is its own class behind one base, so the row never reaches into it.
Every card opens with its **figures toggle** at the top left, an accent text action reading "Per
Second" or "Raw" (`Figures`), each press flipping it and repainting every row's figures; the header
totals stay per second. A Cycle card adds its **cycle button** at the top right, the same accent
text action reading "Buy One", "Buy Max" or "Buy Next" (`BuyMode`), each press stepping to the next
and back round to Buy One and repainting the card; a Pair card draws none, so its toggle stands
alone. Both modes belong to the card, in memory for as long as the card is built - a chapter switch
or a return from the idle dialog builds it afresh at Per Second and Buy One - and are never saved.
The card ends in two buttons. **Book** names the first member in group order whose bar is not
available, is not the row shown at level 0, and whose level is offered, as the bar's gate text (or its name when the gate has none)
with the level's price; it buys one level and in the same press adds the bar to the active set, so
the gig starts at once, and it is disarmed when unaffordable and hidden when no member qualifies.
**Auto** reads "Auto" and nothing else. Where one member's automation stands is ONE answer,
`LeveledGroupUI.Automation` (`AutomationState`): **Purchased** when the bar goes again by itself
(`repeatWhen` holding at its home, never the purchase) or its upgrade is owned (section 4), **Buy** when
`Purchasing.CanBuy` says the upgrade row's purchase would succeed, **Price** otherwise. A tap on Auto
buys the automation of the first available member, in group order, that names an `automation` and
does not read Purchased (a row at level 0 shows a member that is not available); the tap buys exactly when that member reads Buy, and the button is hidden
when no member qualifies - so it leaves when every visible bar repeats and returns when another is
booked. While that member reads Price the button is dimmed by a class (`leveled-auto-unaffordable`)
and is never disabled: a disabled element receives no pointer events, and the hold is how a player
who cannot afford the target reads its price.
A hold on Auto (`GameConfig.longPressSeconds`, the row's hold) opens the **automation window**, a
host-owned overlay (`AutomationWindowUI`) reached through `IAutomationOpener` with the group and the
scope the card resolved, as the info screen is: the title "Auto", one line saying that a gig with
Auto starts again by itself, then one row per member that is available and names an `automation`,
in group order - the bar's name, taking the width it needs and wrapping at a word, and at the right
its state from that same answer: grey "Purchased", an accent "Buy" that issues the upgrade row's
purchase for THAT bar, or the upgrade's price in grey. Neither grey state is pressable. A purchase
in the window leaves it open, and the host's refresh repaints it like every overlay, so a Buy, a gig
booked or Cash arriving moves a row with no reopen; any gig is automated from here in any order
while the tap keeps buying the first. "Dismiss" at the bottom right closes it. The hold and the tap
share the button under the hold's rule (**The hold**): the hold's manipulator listens on the way
down, because a button's own click handler stops a press and a release where it handles them and a
listener on the way up hears neither, and it lets go of the pointer the button's click captured when
it fires and stops the release that follows, so the click is no longer pressed and the release
clicks nothing. A drag that takes
the pointer from the button ends the hold before it fires and its release clicks nothing, so a press
on Auto is a tap, a hold or a drag and never two of them. The rows' and the buttons' presses are public actions
(`Tap`, `BuyOne`, `BuyMax`, the cycle row's `Buy`, `OpenInfo`, the card's `ToggleFigures`, `Cycle`,
`Book`, `Auto`,
`OpenAutomation`, and the window's `Buy` and `Dismiss`) so a headless test presses what the pointer
presses, and the toast's `Advance` ends the shown toast as its last frame would; an upgrade tile's are `Buy` and
`OpenDetails`, and its window's `Close`; a prestige card's are `Press` (what Confirm does) and
`OpenInfo`, the info window's `Close`, and the confirmation window's `Confirm` and `Cancel`.

**The threshold toast.** When a levelled row's level crosses a count threshold during play - a
purchase, an autobuy, a grant, whatever moved the count - a toast names each effect the threshold
switched on: "<target displayName> x<multiplier> <stat word>", "House Party Draw x2 payout", the stat
word from one mapping (`ToastUI.StatWord`: `rate` is "speed", `yield` "payout", `cost` "cost", any
other stat its own name). The row detects it: every refresh reads the level's owned count, and a
count above the last one it read raises the effects of `ThresholdsCrossed` between them, in order,
through `IToastOpener`, the shape `IGeneratorInfoOpener` has. The row's first refresh only takes the
count as it stands, so nothing toasts on the first render after a bind, a chapter switch or a return
to Live; a count that fell (a reset) raises nothing and is the new count measured from. The idle
claim toasts nothing, since the return to Live after the dialog builds the rows afresh. The toast
(`ToastUI`) is host-owned, a dark rounded pill near the bottom of the screen over the page and
beneath every window, ignoring picking: each stands `ToastUI.Seconds` (2 s), fading in and out over
`FadeSeconds` on the element's own scheduler, and crossings queue and show one after another. A
chapter switch or the phase leaving Live drops the toast and its queue.

### 12.12 Validation at content load

- The scope tree is a tree, and it is **root, then chapters, then tiers all the way down**: exactly
  one scope has no parent, no scope has two, every scope is reachable from the root, the top scope
  is a `RootDefinition`, root's children are `ChapterDefinition`s, and everything below a chapter is
  a `TierDefinition`. The kind rules are not decoration - a scope's authored kind picks its state
  class and its payload (§12.3), so a kind out of place builds a node whose payload and parentage
  disagree with where it sits: a root handed a parent reports itself parentless while sitting in
  someone's children, and a tier directly under root is read as a chapter by a walk that then finds
  the wrong payload. The graph rules cannot catch either, because they resolve the root
  structurally - a tree topped by a tier looks rooted. `RootDefinition` also refuses a parent at
  construction, since this pass is dev-only and a mis-parented root would corrupt every chain walk
  quietly.
- Every referenced id resolves (currencies, flags, generators, modifiers, scopes). An `Effect`
  selector is not in that list: `target` and `currencyId` are match strings the gather evaluates
  against a candidate it already holds (§12.2), never references to resolve.
- Every id is unique along a CHAIN — currencies, flags, bars, groups, producers, generators,
  upgrades, events, triggers, modifiers, songs; a declaration in two scopes is refused, and sibling
  subtrees may reuse an id freely because neither can see the other. Scope ids stay unique tree-wide.
- A tag is declared on a scope like a flag, and a declared tag joins the same per-chain name space ids
  and flags share: colliding with either is refused in both directions, and so is the same word
  declared twice on one chain. Scope ids stay out of that space - they are unique tree-wide and no
  selector ever addresses a scope, so a tag may spell one. Carrying a tag no scope on the definition's
  own chain declares is an error, found by the carrier's own outward walk, and that walk is the only
  tag check the pass performs. Two SIBLING scopes may each declare the same word: a declaration is
  permission, not identity, and matching is a string compare, so the word means the same thing
  wherever it is carried - an effect above both chains matching an id in one and a carried tag in the
  other is the aliasing that placement asks for (§12.2), not a collision.
- An `Effect` selector is never checked. `target` and `currencyId` are answered from the other end - a
  definition walks outward, meets the effect, and answers to the word or does not - so a selector
  naming something declared below the scope the effect is applied at is ordinary authoring (Ch. 1:
  `gj_tap_1` applied at ch1, naming tier1's `tap_producer`), and an effect nothing answers to has no
  takers, which is legal: it is what a correct root buff looks like before a chapter carrying its tag
  loads. Whether anything ANSWERS to a selector is statically decidable and exact - the definitions,
  their ids, their tags, their produced currencies and stats, and every site an effect is applied at
  are all authored - but only by one pass over the whole tree, walking every definition outward and
  recording which effects it meets. No single site can answer it without looking down, which is why
  nothing here does. What is genuinely runtime is only whether a match goes LIVE: a gate opening, an
  `appliesWhen` holding, a grant firing. Until that pass exists a misspelled selector - `incone`,
  `tap_produer` - ships as an inert effect with no finding, which is accepted: its home is the orphan
  sweep, where unreferenced modifiers, flags nothing sets, and selectors nothing answers to are one
  collection problem.
- A `SetFlag` naming an undeclared flag is an error, as is one whose home is off the acting chain -
  including a home the acting scope encloses, which the outward write can never reach (§2). A
  declared flag with no setter warns.
- A timer takes the flag's rule (§9): a modifier's `timer` that no scope declares, or one homed off
  the chain from the modifier's declaring scope, is an error, and so is an `ExtendTimer` whose timer
  is undeclared or off the acting chain; a `BuffActive` on a modifier with no timer is an error. A
  modifier's `activeAfterSeconds` is finite and nonnegative; an `ExtendTimer`'s `seconds` is finite
  and positive and its `capSeconds` finite and at least `seconds`. A timer shares the chain's name
  space with ids, flags and tags. A root whose `encoreAdReward` is empty is an error: the ad
  placement pays what that list says, and an empty list is an ad that pays nothing.
- A currency's `activeWhen` is judged at the currency's own home, so its operands take the reach
  check there rather than at any site that reads the currency. An `IdleAccumulation` anywhere inside
  one is an ERROR, not the inert-operand warning it draws elsewhere: the circumstance does reach the
  gate, and that is the problem - it names which gather is asking, so the gate stops being a property
  of the currency. The damage varies with the spelling, which is why the kind is refused and not one
  shape of it: bare, every authored payout throws; negated, the currency silently leaves the idle
  offer (§12.2). There is deliberately
  no static check on an `AddCurrency` naming a gated currency: whether the gate holds at that site
  when the action actually fires is a runtime question this pass cannot decide, and `Deposit`
  throwing is the answer - loud, at the moment it matters.
- A rung on the root needs no check: the field is on `InteriorDefinition` (§12.3), so there is
  nowhere to author one.
- A rung that would reset over an armed, unclaimed reward is refused at its gate by the scope
  holding the record (12.5). No load-time check stands in for that and none could: a guard authored
  on the parent would be a read down the tree, which the event kinds do not express - they read
  outward only, like every other fact (12.4).
- Scope references are checked for reach: `ResetScope` may target the acting scope, a scope it
  encloses — never a peer, the root, an ancestor, or an unrelated subtree. Peers are cleared by the
  scope that CONTAINS them, since resetting it is downward-closed. `ExecuteRung` may
  only reference a rung declared within the acting scope, and never the rung its own list belongs
  to - the one cycle the downward reach leaves possible, and an unbounded recursion; `RestartScope`
  from its own rung's list is the same fault. `AddModifier` and `RemoveModifier` may
  target the acting scope or an ancestor (grants live outward), never an unrelated subtree; a
  `RemoveModifier` naming a modifier nothing reachable grants warns.
- A produces entry names exactly one target - a currency, a generator, or a bar; two or none is an
  error - and the target sits on the acting chain like every other ordinary read. A cost effect with
  a constant x0 is an error: a free generator is an unbounded rate printer. A generator with yield
  entries that no `FireGeneratorYield` names warns: nothing fires them.
- Ordinary reads and writes (`AddCurrency`, `SetFlag`, `AddSong`, Condition reads, `produces`
  targets) may address only the acting scope's chain — itself or an ancestor. The runtime state
  walk cannot reach siblings, so a cross-tree reference is a load-time error rather than a silent
  runtime miss.
- Effect PLACEMENT is not validated (§12.2). Where an effect is applied decides which definitions
  walk up and meet it, and an effect nothing meets is content with no takers rather than a defect.
  Judging it would mean enumerating an effect's possible askers - a downward search, and the exact
  inversion of how the gather works. The exception is a stat whose gather origin is
  fixed in CODE rather than by content: `game_speed` is read once per segment by an owner-less,
  currency-less query originating at the foreground chapter - the tick's, and the idle claim's over
  the chapter being entered (§9) - so an effect carrying any narrowing
  can never be passed a coordinate to match, and one applied below chapter level is never on that
  walk. Both are warnings, and both are answered by the site's own KIND with nothing enumerated -
  which is what makes them checks where the general case is not one.
- A generator's count thresholds have positive counts in strictly increasing order and at least one
  effect each (a formula on one is an import error). Each effect names an owner that resolves on the
  generator's chain (`ChainReach` otherwise) and is declared at the generator's OWN scope - the gather
  meets an effect applied there only from an owner declared there or below, so an owner further out
  would never take the factor (`InertOperand`) - and a stat that owner's kind is asked for: a
  currency's `rate`, `yield`, `payout` or `exponent`; a generator's `rate`, `yield`, `count`, `cost` or
  `autobuy`; a producer's `rate` or `yield`; a bar's `rate`, `yield` or `consumption`; an upgrade's
  `cost`. The multiplier takes the Effect's numeric checks.
- A group's `maxActive` is at least 1, and it lists no null member, no member twice
  (`DuplicateMember`) and only members its own scope declares (`MemberOffScope`) - the active set and
  the member share a home. A bar's `fillAmount` and `fillRate` are positive - a nonpositive threshold
  is an unbounded settlement loop, and a zero rate is a bar no multiplier can move; each `consumes`
  entry names a currency reachable on the bar's chain with a positive per-unit amount, and no currency
  twice (an empty list is legal: it fills from time). A group bound by a module needs no displayName:
  it renders its members, not itself. `perFill` on a bar with no `repeatWhen` is an error (12.6); a
`repeatWhen` is validated as `availableWhen` is, its operands under the reach rules; a `tap` producer
must be on the bar's own chain, and nothing checks that it pays this bar; a `level` generator and an
`automation` upgrade must be on it too, and `automation` on a bar with no `repeatWhen` is an error.
A `LinearOnOwnedCount`
names a generator on the chain and a nonnegative coefficient, as `LinearOnBalance`'s currency and
coefficient are checked.
  `BarsCompleted` reaches like every other scope-attached read: the group's declaring scope is the
  acting scope or an ancestor. A group is not an effect target (§12.7), but nothing checks a
  selector for naming one:
  that would mean interpreting the word, which is the search this pass does not do.
- A balance goal on an event whose `onEntry` never resets the host scope warns.
- An event on the root scope needs no check. `events` is declared on `InteriorDefinition` (§12.3),
  so root has no field to hold one: handicaps gathering into every chapter's outward walk and a
  globally occupied host are unrepresentable rather than refused, and the state agrees - the record
  lives on a payload root does not carry. This is the placement rule, not an exemption: if a kind of
  scope cannot have a feature, its definition declares no data for it and there is no rule to write.
- A gate may not be null: a generator's or event's `availableWhen`, an upgrade's gate, a rung's
  `offerCondition`, a trigger's condition. An unauthored gate is dead content, and `Always` is how
  an author says the gate is open. Filters keep their optional conditions - a bar's `availableWhen`
  and a `ProducesEntry.condition` both default to active, because neither is a gate. An upgrade's
  `ownedWhen` is optional too, and validated as its gate is, from the declaring scope.
- A levelled upgrade (section 4) authors no top-level `cost`, `effects`, `description`, `actions` or
  `ownedWhen` beside its `levels`: each would be a second answer the purchase never reads, so each is
  an error. Each level has a nonnegative cost and at least one effect or action; its effects are
  validated as the upgrade's own are and its actions with every other list
  (`ScopeDefinition.ActionLists`). An authored empty `levels` is an import error, since an asset cannot
  tell it from an absent one.
- A rendered gate's text is computed at load: the legs of a rung's `offerCondition` and an event's
  `availableWhen` are formatted through `Text` (12.4). A pattern `string.Format` refuses is an
  error; a default join over a child with no text is an error, a hole in the sentence; `uiText` on
  the top-level `All` itself warns, since the button renders its legs as rows and never that text.
  A textless leg at the top level is the author's choice - a threshold leg renders as progress alone.
- A polymorphic kind in data with no class behind it is an import error.
- **Validation has a second input beside the content tree: the code side's own references.** A
  screen, a manager, or the session that names content from code - a currency (`roadies`), an
  entitlement id (`backstage_pass`) - states
  what it needs as a static check on the class holding the reference, asking existence with the
  same primitives a definition uses (`RequireOnChain` from root, the declaration lists).
  `CodeReferences` lists every such check explicitly, the way the kind registry lists every kind and
  the widget factory every `prefabId`, and the pass runs the list after the tree walk, at import and
  at boot alike. Nothing is constructed to ask: a check reads only the composed content. At runtime
  the class resolves the same reference off root's own declaration list and holds the asset - a miss
  there is a throw, not a guard, because the pass already answered. This is not a content lookup (12.14.8): it enumerates
  code, inside the one pass that audits the whole tree.
- **Widget registry cross-check.** Import and development boot call
  `ModuleWidgetFactory.Validate(content, registry)` directly beside content validation. The factory
  verifies that authored widget ids have controllers and layouts, and cross-checks its controller
  ids against the registry entries. Missing layouts, missing controllers, duplicate ids and invalid
  entries are errors. The importer uses the project registry settings asset; boot uses the scene's
  registry reference. Neither `ContentValidator` nor `CodeReferences` receives the registry.

### 12.13 File layout

```
Assets/Scripts/
  Core/
    GameManager.cs          // bootstrap, save/load, chapter switching
    GameSession.cs          // transient orchestration: foreground chapter, phase, the idle offer, the command queue — never serialized
    TickSystem.cs           // the segmented tick over one real-time window; returns the TickReport
    TickReport.cs           // what ONE tick moved, recorded at the mutation sites; interpolation's slopes
    GameClock.cs            // the one time source: driver-owned, advanced at every entry point
    GameConfig.cs           // the global tuning knobs: maxGameSpeed, the idle thresholds, tickIntervalSeconds, longPressSeconds
    BigNumber.cs            // wraps break_infinity.cs
    Definition.cs           // base: id + tags, declared once for every content family
    ContentDatabase.cs      // loads the root + labeled chapter roots, composes the pair, runs the §12.12 pass
    ComposedContent.cs      // the runtime content set: the root asset plus the chapter roster (12.14.5)
    ContentValidator.cs     // the 12.12 pass: the check enum, the ValidationContext, one member per check family
    CodeReferences.cs       // validation's second input: every code-side content reference, one static existence check per class, run by the pass after the tree walk
    ScopeDefinition.cs      // the abstract base: declaration lists + the permanentModifiers usage list, Declares, CreateState
    InteriorDefinition.cs   // chapters and tiers: the rung and the events list
    RootDefinition.cs  ChapterDefinition.cs  TierDefinition.cs   // one file each - Unity binds one ScriptableObject per script, by file name
    ScopeState.cs           // the base, ScopeState<TFacts>, the three state classes and their payloads
    ScopeStateExtensions.cs // chain walks answering with a typed scope (Root(), Chapter()); the base names no derived class
    ScopeLinks.cs           // the one post-construction pass: what each static reference means in THIS tree, keyed by its holder
    GatherCompiler.cs       // the gather's static half, compiled in that pass: a coordinate plan per authored query, a contributor plan per scope
    EffectLink.cs           // one (carrier, effect, node) a plan holds: Factor reads its own fact at its own node, or One
    ActionList.cs           // the one runner an authored list executes through: Refuses / TryRun / Run, whole or not at all
    Condition.cs  GameAction.cs  PayoutFormula.cs  TriggerDefinition.cs   // the class families (+ kind classes); GameAction because System.Action shadows the doc's name
    Rung.cs                 // offer condition + action list: the album release and the capstone (12.5)
    SubclassPickerAttribute.cs   // marks a [SerializeReference] field for the inspector's concrete-type popup
    Effect.cs               // the flat struct
    GameContext.cs          // read access for Evaluate/Execute: state chain + defs
    Sweep.cs                // the trigger sweep: latch event goals, collect eligible triggers, run them in order
  Economy/
    CurrencyDefinition.cs  ProducerDefinition.cs
    Producer.cs             // stateless resolution: a source's entries summed, times the product of its coordinate plan's links; GetRate and RatePairs read the contributor plan of the node they are asked at
    GeneratorDefinition.cs  UpgradeDefinition.cs   // the generator with its count thresholds (CountThreshold, ThresholdEffect, NextThreshold, ThresholdsCrossed); the upgrade with its levels (UpgradeLevel, LevelAt, NextLevel) and its optional tile icon
    Purchasing.cs           // CostOf(generator, n) the series, MaxAffordable, TryBuy(generator, count | upgrade): fail-closed gate, one spend, count or latch (and next level), payload; IsOwned(upgrade): the latch or ownedWhen, or the last level
    MultiplierFormula.cs    // the formula family an Effect's factor can compute from
    ModifierDefinition.cs   // named List<Effect> + stacking enum (Replace|Linear|Multiply) + optional appliesWhen
    BarDefinition.cs  GroupDefinition.cs  BarSystem.cs   // a bar's consumes list; a group of members with an active set; the draw, the settlement, SetActiveMembers
  Loop/
    ChapterManager.cs      // forward-only advance, reacting to root completion flags
  Events/
    EventDefinition.cs  EventSystem.cs
  Story/
    StoryBeatDefinition.cs // a chapter-boundary card: text, availableWhen, seenFlag, opensWhenAvailable (section 10)
  Meta/
    RoadieAllocation.cs    // SetRoadieAllocation; the boost arithmetic is Economy's
    BackstagePass.cs       // the backstage_pass entitlement id, the one Owned(root) read, and its CodeReferences check
    Roadies.cs             // the roadies currency id and its CodeReferences check
  Save/
    SaveSystem.cs          // JSON + checksum; serializes the ScopeState tree
  Editor/
    ChapterJsonImporter.cs // the authoring importer: JSON documents in, ScriptableObject assets out, preflight and post-write validation (12.14.5)
    ContentDto.cs          // the authored JSON shape, strict, plus the kind registries the importer maps `type` through
    SubclassPickerDrawer.cs   // the inspector popup behind [SubclassPicker]
  Utilities/
    SingletonManager/      // SingletonManager.cs + SingletonPropertyAttribute.cs: scene-lifetime singleton plumbing, independent of the game
  Monetization/
    IAdService.cs          // AdPlacement, AdResult, the request-now-result-later seam a rewarded-ad SDK presents
    IStoreService.cs       // ProductId, PurchaseOutcome, PurchaseResult; Purchase / Acknowledge / RestoreEntitlements
    FakeAdService.cs  FakeStoreService.cs   // the seams until a real SDK: scripted outcomes, recorded calls
    AdManager.cs           // rewarded only (the Encore placement's authored reward + Double it); polls its requests from the driver's Update
    IAPManager.cs          // Backstage Pass, Roadie bundles, Tip Jar; grant, save, acknowledge in that order
  UI/
    SectionDefinition.cs  ModuleDefinition.cs  ModuleRegistry.cs   // ModuleDefinition carries BuyControlKind for a levelled card
    ModuleWidget.cs  ModuleWidgetFactory.cs   // the plain-C# controller base and the closed prefabId constructor table; direct factory/registry validation at import and development boot
    ScreenHost.cs  UIRoot.cs                  // the structure logic (the ONE Refreshed subscriber) and its MonoBehaviour shell
    GateFeedback.cs  RungFeedback.cs   // the feedback contract: legs, text, progress; the payout preview
    IStoryOpener.cs         // the one method a story row asks of the host: open the card for a beat at its scope
    IGeneratorInfoOpener.cs // the one method a generator row asks of the host: open the info screen for a generator at its declaring scope
    IAutomationOpener.cs    // the one method a levelled group's Auto asks of the host: open the automation window for a group at its declaring scope
    IUpgradeDetailsOpener.cs // the one method an upgrade tile asks of the host: open the details window for an upgrade at its declaring scope
    IRungOpener.cs          // the two methods a prestige card asks of the host: open the rung's info window, and its confirmation window at the card's scope
    IToastOpener.cs         // the one method a levelled row asks of the host: show the toast for one effect a crossed threshold switched on
    ToastUI.cs              // the threshold toast: a pill near the bottom, one effect at a time for 2 s with a fade, crossings queued
    LongPressManipulator.cs // the hold gesture over an element's own scheduler: fires once if the press stays down, still, over the element and untaken; never captures, lets go of a button's capture when it fires
    DragScrollManipulator.cs // a left-button mouse drag scrolls a view along its axis as a finger does - the page and the overlay lists vertically, a strip sideways; a click stays a click, and every ending lets go of the pointer
    PinnedHeadlineUI.cs     // the headline currency line as an overlay: over its line at rest, shrunk beneath the pills once scrolled away
    Reveal.cs               // how a section or a module shows: at once, hidden at once, or revealed - a fade-in that grows it along its container's axis
    Widgets/  CurrencyHeaderUI  CurrencyReadout  JamButtonUI  GeneratorRowUI  UpgradeRowUI
              GroupUI  BarRowUI  RungButtonUI  EventUI  StoryRowUI   // every row module binds ONE thing; visibility is the module's visibleWhen
              // RungButtonUI is the prestige card (RungButton.uxml): title with the gain and the (i), Currently, requirement lines, Confirm
              LeveledGroupUI  LeveledBarRowUI   // the levelled group (LeveledGroup.uxml): a bar with its level per row, the Pair or Cycle buy control, the Per Second / Raw toggle, Book and Auto; AutomationState, BuyMode, FigureMode
              UpgradeTileUI   // an upgrade as a strip tile (UpgradeTile.uxml): its own icon or the shared pick, name (with the owned level), price or Owned; a tap buys, a hold opens the details window
    ChapterSelectUI.cs  CollectScreenUI.cs        // chapter select over Live (the screen while NoChapter), and the AwaitingIdleClaim screen
    StoryBeatUI.cs          // the story card overlay, host-owned like the two screens: title, text, one button
    StoryLogUI.cs           // the story log overlay: one button per read beat over root's roster, reopening the card through the host; the list drags
    DiscographyUI.cs        // the Discography overlay: one row per song root keeps, a name field and a button that renames through the session; the list drags
    NumberFormatter.cs
    TopBarUI.cs             // the two pills; the countdown pill's timer, authored in Screen.uxml, is read by the window too
    TimerPill.cs            // the countdown pill element: a Button with a `timer` UXML attribute, so the chrome names its timer once
    SettingsUI.cs          // the Roadies entry and close button
    RoadieAllocationUI.cs  // local allocation draft; Done submits the whole map
    EncoreWindowUI.cs      // remaining time and ad/Pass requests through the existing managers
    GeneratorInfoUI.cs     // a generator's info screen: description, the row's cost and yield line, owned count, production
    AutomationWindowUI.cs  // a levelled group's automation window: one row per visible gig naming an automation, Purchased / Buy / price, Dismiss
    UpgradeDetailsUI.cs    // an upgrade's details window: the name (with the owned level), "On buy:" or "Current effect:" with the description - a levelled upgrade's "Current effect:" and "Next:" by level - Close
    RungInfoUI.cs          // a rung's info window: the label, the info text, Close
    RungConfirmUI.cs       // a rung's confirmation window: the label with "?", the confirmText, Cancel and Confirm (TryRung at the card's scope)
Assets/UI/                // the UI Toolkit text assets: Screen.uxml + Screen.uss, the runtime theme, Widgets/*.uxml
                          // (LeveledGroup.uxml and UpgradeTile.uxml among them), Icons/ (the white glyphs the
                          // stylesheet tints: the top bar's, the shared upgrade pick, and each upgrade's own),
                          // and Fonts/ (Roboto Regular, Medium and Bold with its Apache 2.0 licence)
Assets/Settings/          // hand-made settings, never imported: GameConfig, ModuleRegistry, PanelSettings
ScriptableObjects/       // the importer's managed root: DOCUMENT then FAMILY (12.14.5)
  root/                  // the document's own top scope id
    root.asset           // the scope sits at its document root
    Currencies/  Modifiers/
  ch1/
    ch1.asset  tier1.asset
    Currencies/  Modifiers/  Producers/  Generators/  Upgrades/  Bars/  Groups/  Events/  Triggers/  StoryBeats/
  ch2/
    ch2.asset  tier2.asset
    Currencies/  Modifiers/  Producers/  Generators/  Upgrades/  Bars/  Groups/  Events/  Triggers/  StoryBeats/
Content/                 // the authored JSON the importer reads
  root.json  chapter-01.json  chapter-02.json
```

### 12.14 Requirements

1. `break_infinity.cs` (BigDouble) for all currency and production values, wrapped in `BigNumber`.
   **The wrapper refuses NaN and infinity at construction** - every literal, conversion, arithmetic
   result, and value read back from a save passes through one constructor, so a poisoned number
   throws where it is born rather than spreading. The library deliberately PRESERVES both and every
   comparison answers false against a NaN operand, so no downstream sign check could catch one; that
   is why the invariant lives in the type and consumers never test for it. A corollary: arithmetic
   in raw `double` that then converts (a count times a rate, say) can overflow before the
   wrapper sees it, so mixed expressions convert FIRST. **An author may write any number the game
   can compute**: every field the runtime holds as `BigNumber` is authored as one, RATIOS included -
   `Effect.multiplier`, a generator's `growth`, the formula coefficients - because the gather's
   product and every formula factor are themselves unbounded. The exceptions are the numbers the
   runtime cannot widen either: counts (`int` in state), `Pow`'s power (a `double` by `BigDouble`'s
   own signature), and wall clocks, which are seconds.
2. Tick on real elapsed time (`DateTime.UtcNow` deltas), not frame time.
3. UI refresh on the two triggers of §12.11; no per-frame polling of balances.
4. Versioned, checksummed saves (explicit migrations, atomic write + backup), validate on load, cap
   idle earnings client-side.
5. Content in ScriptableObjects, reached by DIRECT REFERENCE within each authored subtree.
   **Startup composes the tree**: Addressables carries the root definition at a fixed address and
   every chapter root under one `chapter` label; boot loads both, sorts the chapters by id (until
   ordering becomes an explicit authored fact), and carries the PAIR — the root asset plus the
   chapter set — into state construction and validation, which answer "children of root" from the
   set and every deeper child from the serialized lists. Nothing is cloned or mutated: scope
   operations resolve by asset identity, so the tree's root definition must BE the loaded asset —
   a clone would strand every authored reference to root — and with no mutation, nothing writes
   through to the editor asset. Every handle is held for the session. Identity also constrains
   PACKING: root-owned assets are implicit dependencies of both the root entry and every chapter
   entry, and Addressables duplicates implicit dependencies across bundles — so the root and
   chapter entries share one `PackTogether` group, keeping every definition a single runtime
   instance. `RootDefinition`'s
   serialized child
   list stays empty: the chapter document set IS the roster, and no asset stores a copy of it.
   The label is consumed exactly once, at the load boundary, exactly as the save consumes scope
   names (§12.3); no runtime read ever consults it, so requirement 8 stands untouched. Each
   chapter's direct references still bring its whole subtree, and the graph is resident from
   boot. Per-chapter streaming would need indirect handles, staged
   validation, and staged state construction; it is a later architectural change if load time or
   memory ever argues for it, not a property of today's design. **Authoring is a
   JSON document per chapter, materialized into SO assets by an editor importer**; the assets stay
   fully hand-authorable — `[SerializeReference]` plus the subclass-picker drawer create and edit
   polymorphic kinds in the inspector. An authored reference is an object field: it cannot name
   something that does not exist, so only flags, tags, stat names, and the `Effect` selectors
   (`target`, `currencyId` — id-or-tag matchers, §12.2) stay strings.
   **Re-import overwrites**: the JSON is the source of truth for a chapter it authored; content
   born in the editor is simply never re-imported over. A write resets an existing asset to a
   blank instance's state before filling it, so nothing a previous import wrote survives it, and
   every `[SerializeReference]` object it lands takes as its managed reference id a hash of its
   field path on its asset, set before the asset first serializes (Unity fixes an id then), so the
   same content writes the same bytes and a re-import changes only what the content changed.
   (If round-tripping ever matters, a chapter/game exporter back to JSON is a later addition —
   deliberately not built now.) A polymorphic kind in data with no class behind it is an import
   error (§12.12); regular per-chapter gear curves can be generated at import.
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
   (aggregation like `GetRate`, and the downward-closed clear of `ResetScope` - a parent informing
   its subtree, which answers, never a parent reading a child's fact). What is forbidden is
   resolving a name from anywhere else - a global map, a scan of every scope, or any search that
   leaves the acting chain. An EXPLICIT scope reference takes neither walk: it names exactly one
   node the moment the tree exists, so the link pass resolves it once at load and every execution
   afterward reads it by reference (12.3) - no runtime execution searches for a named node, and a
   reference that failed to resolve is a content fault at load rather than a miss mid-tick.
   Validation is the exception and the reason the rule holds: it audits the whole tree at load,
   once, which is what lets every runtime walk assume its own chain is enough.
   Content that seems to need a registry is content that has not been placed on a scope yet.

---

## Appendix — at a glance

- **Structure:** nested prestige on a tree of **state containers** — root / chapters / tiers (tiers
  may nest for ladders, sibling for independence). Lifetime is placement; ids unique per chain;
  everything derived is computed on read from stored facts.
- **Prestige:** a chapter names its base resource, release meter and prestige currency; a prestige
  currency is paid by a release and declared outside that release's tier. The income boost (a chapter
  permanent modifier, +2% per unit in Ch. 1 and 2) and the capstone gate each read a prestige
  currency the chapter authors - the same one in Ch. 1 and 2, declared on the chapter and zeroed by
  the capstone's own reset. Roadies are the only currency that crosses chapters; songs cross too,
  as named things and not a currency (section 7).
- **Rungs:** the album release and the capstone are `{offerCondition, List<Action>}`; events are
  lifecycle commands executing authored lists (§6.1). Payout-before-clear is list order; `ResetScope` is a bare
  downward-closed clear; every invocation — `TryRung` from the UI, `ExecuteRung` from another
  rung — is fail-closed against the rung's own gate (an unmet gate no-ops; unfinished runs
  discard, never bank). The capstone resets the **entire chapter**; completion facts live at root; replays are the same
  chapter played again against the same gate - a prestige currency declared on the chapter, zeroed
  by the capstone's own reset; rewards and goals are formulas over stored facts (§8.1).
- **Economy:** producers are named definitions owning base contributions —
  `produces: [{currencyId, stat, value, condition?}]`, stats (`rate`, `yield`) named and extensible;
  generators contribute the same entry shape, scaled by owned count; **Effect** =
  `{target: id-or-tag, currencyId?, stat, multiplier-or-formula}` (the stat required and exact), gathered on read from
  facts (purchases, timed buffs, events, modifier grants, permanent memberships, fill counts) and never
  stored; flat bonuses are contributions; tags name sets from the member side.
- **Bars:** generic fillables — each names the currencies it consumes per unit of fill and its own
  fill rate, taking what is there in declaration order; a group lists members of any kind and caps how
  many are active at once; repeating bars carry
  per-fill cascade effects scaled by fill count; uncapped overfill, completion derived from progress.
- **Events:** data + one ActiveEvent record; `StartEvent`/`DismissEvent` are self-guarding
  commands, never Action kinds; entry runs `onEntry` (banking the run if the host rung's own
  gate is met, discarding it otherwise) then creates the record; handicaps are ×<1 effects that
  exist while a record does, expiry included; a timer is the only thing that ends an attempt, ticks
  live only, and blocks idle; the goal latches whenever it holds before expiry, timed or not;
  dismissal is the one ending - record removed first, then `rewards` if the goal was reached, then
  `onEnd` either way, which is where the reset lives; any reset reaching the host kills it; expired
  records persist, occupying the host and still handicapping, until dismissed; rewards lateral,
  never the prestige currency.
- **Triggers:** `{condition, actions}` per scope — the one sanctioned condition-observer; one-shot
  per scope-life (`firedTriggers` latch, a reset re-arms), swept after ticks and command
  transactions in live scopes only, never during idle; repeating behavior is a producer or a bar,
  never a trigger.
- **Idle:** per chapter — one active chapter ticks; switch-in computes `idleRate × min(t, cap)`
  (the rate gather under the idle-accumulation context; base 50% an idle-only root modifier via
  `appliesWhen`, cap 4 h a `GameConfig` threshold) into a transient offer presented
  as the idle dialog — the stamp IS the pending claim, the ad callback doubles and settles
  atomically, a plain dismissal deposits the base, settlement advances the stamp; app close is not
  special; a tap's yield never accrues, while bars fill capped by what they consume and their
  completions pay.
- **Monetization:** opt-in ads only; double-the-claim idle ad; Encore = game speed 2x, a root buff
  reading root's declared `encore_timer` timer, which the ad's authored reward extends (`game_speed` stat,
  consumed by the tick and the idle claim over a real-time cap; wall clocks never scale; a higher
  speed tier is undecided and would be content alone over the same timer); Backstage Pass (lifetime: permanent Encore, the idle claim computed already
  doubled, and a raised idle cap); Buy Roadies (repeatable); Tip Jar; no subscriptions.
- **Engine:** Unity; break_infinity numbers; DateTime ticks; checksummed JSON save of the state tree;
  boot composes the root address plus the `chapter` label into the tree, each chapter's direct
  references carrying its subtree; load-time validation of all authored data.
