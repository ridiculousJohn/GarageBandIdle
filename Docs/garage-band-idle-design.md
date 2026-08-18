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

**The chapter loop (outer).** Cumulative Records unlock the current chapter's capstone gig. Playing
the capstone **implicitly cuts an album** — the run's Fans bank as Records as part of the show — and
then completes the chapter. Completion facts (the completion flag, clear counts) live at the **root**
and survive everything; the capstone's reset clears the entire chapter, which is what makes the
chapter immediately replayable (§8.1). Advancement to the next chapter is forward-only and reacts to
the completion flag.

Records are the link between the loops: they raise income and gate chapter advancement. Chapter
advancement therefore depends on releasing albums over time, not on a single large Cash total.

```
   INNER (minutes):  tap → Cash → gear → Fans → release album ─┐
                      ▲  reset run, +Records, repeat faster     │
                      └─────────────────────────────────────────┘
                                    │ cumulative Records reach the gate
                                    ▼
   OUTER (hours/days): capstone gig → next chapter (forward only)
```

---

## 2. Chapters

Eight chapters. Each has its own gear, currencies, mechanic, and capstone gig, and is gated by
cumulative Records.

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
flags, generators, upgrades, and bars declared to it, plus its tier scopes. The chapter level owns
whatever the whole chapter shares; each tier owns what that tier's release destroys. **Lifetime is
placement**: a fact survives a reset by being declared further out, and anything two tiers share
lives in their nearest common ancestor. Moving a declaration up a level is a pure data edit, because
ids are unique tree-wide and everything references by id.

**Progressive reveal.** A chapter does not present all its mechanics at once. Content-unlock upgrades
(§4) introduce new generators, currencies, and mechanics as the player buys them. Each such upgrade
should introduce a change in play — a new mechanic, sub-loop, or automation step — rather than only
increasing a number.

Reveal runs through **one mechanism**: something sets a flag (an upgrade payload, a bar completion, a
passive threshold unlock), and revealed content gates its visibility on a `FlagSet` condition. A
section is visible exactly *while* its condition holds — evaluated live, no latch. "Stays once
earned" is authored by gating on a fact with that lifetime: a flag, or a monotonic value like total
Cash ever earned. Gating a region on a spendable balance is an authoring smell — it strobes with
every purchase. (Ch. 1's `browse_gear` flag latches at 250 Cash earned via a passive unlock for
exactly this reason.)

A flag's lifetime is the scope that declares it (§12.3). Chapter 1 declares `fans`, `covers`, and
`gear` (and the upgrades that set them) in its tier, so an album release clears them and the second
run re-walks the progression — band → fans → covers → gear — instead of opening with every system on
screen. The `album` flag is declared at the chapter level, so the release *region* stays on screen
across runs; only the button's pressability tracks the live offer condition (§5).

Boot validation: a flag whose setters all live in scopes more durable than the flag is warned about —
the reset clears the flag but nothing can ever set it again, and the gated sub-system goes dark for
good (§12.12).

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
- **Records** — each Record increases global income; cumulative Records gate chapter advancement.
  Accumulated, never spent.
- **Roadies** — crew; a global multiplier allocated across cleared chapters (§8).
- **Discography** — a list of the player's best named songs (§7). Display only.

**Income.** Every produced number is computed on read as *the sum of its contributions times the
product of the multipliers targeting it* (§12.2). The effective multiplier stack on Cash:

```
income = Σ(generator base × owned × per-generator effects)
         × catalogBoost      (run-scoped fact, §7)
         × recordsMultiplier (root fact, §5)
         × roadieTotalBoost  (root fact, §8)
         × encoreBoost       (timed buff, 1×/2×/4×, §9)
```

Each of those is an **Effect** (§12.2) that declares which target it multiplies; a currency never
opts into a multiplier — the dependency points from the effect to its target. All of these effects
exist from the start of the game and simply contribute nothing until the facts they derive from exist.

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

Mechanically a release is a **press**: `{offerCondition, List<Action>}` declared on the tier
(§12.5). Chapter 1's:

```
tier1.release:
  offerCondition: All[ CurrencyAtLeast(fans, 50), BarsCompleted(1) ]
  pressActions:   [ AddCurrency(records, PayoutFormula), ResetScope(tier1) ]
```

The payout is an ordinary `AddCurrency` action whose amount comes from a `PayoutFormula` (§12.5) —
there is no payout field and no distinguished award kind. **Order is authoring**: the payout action
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
- **Cumulative Records** unlock each chapter's capstone at a set threshold (§11).

An early album cycle takes seconds to minutes; cycles get faster as Records accumulate.

The release button is **pressable** exactly while its offer condition holds — its inputs are run
values the release itself resets, so the offer disarms at every release and re-arms on the re-climb.
The release *region* stays visible because the `album` flag it gates on lives at the chapter level.
Region coarse, action precise.

The offer condition is checked when the *player* presses (`TryPress`, fail-closed). When another
press references this one (`ExecuteRung`, §12.5), the gate is deliberately bypassed — that is what
lets the capstone bank a 12-Fan run even though the album offer requires 50.

**Formulas that reward pushing past the gate.** Because the payout formula reads the live balance at
press time, a "bank at 1000, keep accruing for more at a lower rate" mechanic (Ctrl C's
Lines → Knowledge) is entirely a formula shape: the offer condition sets the floor, a piecewise
formula pays `10% × min(x, 1000) + slower(x − 1000)`, and the press-now-or-push-on decision emerges
from the curve. The UI's "would bank: N" preview calls the same formula — one implementation, no
drift.

---

## 6. Within-a-chapter play & events

Moment-to-moment play:

- **Tap ("Jam")** — early Cash source; its relevance falls off as gear automates income. The button
  is a module that fires a producer; what a press pays is that producer's **yield** (§12.2). The
  Jam module contributes to Cash's yield (and Rehearsal's, once revealed) and to Rehearsal's rate.
  "Tap" is a UI gesture; the economy only knows a producer was fired.
- **Generators** — exponential cost, `cost = base × growth^owned`, growth ~1.15; 4–6 themed per
  chapter. Because runs reset, a chapter's Cash stays in the thousands–millions range; cross-chapter
  growth comes from Records and Roadies.
- **Upgrades (§4).**
- **Learn-songs bars** — see below.
- **Fans** — passive accrual, band-size driven (§3), tuned loosely relative to Cash so income alone
  does not determine the album payout.
- **Capstone gig** — the chapter's press (§12.5), declared on the chapter:
  ```
  chapter.capstone:
    offerCondition: Any[ All[ Not-yet-complete, CurrencyAtLeast(records, gate) ],
                         All[ Complete, ReplayGoalMet ] ]        // first clear vs replay (§8.1)
    pressActions:   [ ExecuteRung(tier1),          // implicitly cut the album — banks Fans
                      AddCurrency(roadies, 1),
                      SetFlag(chapterN_complete),   // declared at ROOT
                      ResetScope(chapterN) ]        // the ENTIRE chapter, downward-closed
  ```
  `TryPress` is fail-closed — the operation checks its own gate, so a UI bug cannot complete a
  chapter early. The chapter **advance is not an action**: it is a reaction to the root completion
  flag, performed by `ChapterManager`, which makes it derivable from the save no matter how or when
  the flag was set. The first-clear story beat is likewise a UI reaction to the flag's transition.

### 6.1 Events

An event is a self-contained challenge inside a chapter that the player enters by choice. Events do
not gate chapter advancement — the gate is always Records — and their rewards are lateral (never
Records), so no event is ever a hard requirement.

How essential an event feels is a **per-event tuning decision set by the size of its reward**: a
small reward is a skippable bonus; a large one makes skipping a much slower grind. The chapter is
always completable without any given event, but only quickly with the events its tuning intends.

An event is **content plus one state record** (§12.8):

```
EventDefinition: goal (Condition), timeLimit (0 = untimed), handicaps (List<Effect>),
                 onEntry / onComplete (List<Action>), hostScopeId, resetOnEntry (default true), tiers
ActiveEvent:     { eventId, tier, remainingSeconds }   — lives in the host scope's state
```

- **On start,** the event resets its host scope — via the same press machinery, payout first — so
  entry banks the run rather than discarding it and costs nothing but time. This is deliberate:
  the starting state is identical whether the player was "paid" or not, and a bank-it-first ritual
  would be pure loss. Entry pays only the ordinary payout, so a rerun tier cannot be farmed for
  advancement currency. (`resetOnEntry` exists as data for future event kinds; a balance goal
  authored with it off is warned about at load.)
- **Scale.** Events deliberately scale with the player's accumulated power: the host reset zeroes
  the tier's own facts, but root and chapter facts still apply. A tier may be *unbeatable* until the
  player has advanced further — "come back later" is the intended experience.
- **Goal:** a Condition, usually `CurrencyAtLeast`. Weirder goals are `All[...]` compounds.
- **Debuff (optional):** the handicaps are ordinary Effects with multipliers below 1 — generation
  halved is ×0.5, automation disabled or a currency locked is ×0. They apply while the ActiveEvent
  record exists and vanish with it; nothing is installed or torn down.
- **Timer (optional):** `remainingSeconds` decrements on live ticks only, so the attempt pauses when
  the chapter is inactive or the app is closed — a deadline the player cannot attend is not a
  challenge. The exchange: a chapter running a timed event pays **no idle earnings** on switch-in,
  which closes both the app-close and the switch-away-to-wait-out-the-clock exploits at once. Only
  timed events can be failed; an untimed event at insufficient power is merely unfinishable.
- **Failure / quit / success** are all the same teardown: delete the ActiveEvent record. Nothing in
  the host is touched; whatever the run accumulated stays to be banked by the next reset.
- **Reward on success:** `onComplete` actions — a chapter-durable buff (`AddModifier`), a Roadie, a
  Catalog song, local currency. Never Records or any advancement currency.
- **Tiers:** an event can repeat at higher tiers with a higher requirement, stronger debuff, larger
  reward. The rising requirement plus the player's power curve throttles tiered events as a
  repeatable Roadie source.

Authoring guidelines: most events use debuffs; timed events are used sparingly; failure stays cheap;
larger events include a decision (risk/reward, which song to submit) rather than a single confirm.

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

What keeps an early chapter from being cleared instantly late is the **goal**. The chapter press's
offer condition switches on the root completion flag: the first clear is gated by the Records
threshold; subsequent clears by a rising goal read from the root clear count:

```
replayGoal(k) = base × H^k      // k = times already cleared, H ≈ 1.6
```

The first few clears stay at the base goal. The press's actions serve both lives unchanged —
`AddCurrency(roadies, 1)` is correct every clear, and re-setting the completion flag is harmless.
Because the goal rises and the roadie-spread multiplier is concave (§8.2), farming one low chapter
has diminishing returns.

Roadies stationed at a chapter increase that chapter's local production (faster replays), and
clearing a chapter's goal adds a Roadie to the pool.

### 8.2 Boost formula

- **Within a venue (additive):** `venueBoost = 1 + 0.05 × roadiesOnVenue`
- **Across venues (multiplicative):** `totalBoost = venueBoost₁ × venueBoost₂ × …`

`totalBoost` is the permanent multiplier applied to income. Example: 9, 9, 8, and 9 roadies across
four venues give 1.45 × 1.45 × 1.40 × 1.45 = 4.27×.

Because venue boosts multiply, distributing roadies across more venues beats concentrating them
(8 roadies: 1.40× on one venue, 1.46× split across four). Each `venueBoost` also sets that chapter's
replay speed, so allocation balances spreading for total multiplier against concentrating to speed an
active replay.

**Per-venue scaling (planned).** Larger venues will use a higher per-roadie rate and cap (e.g. +5%
up to 5 roadies at the garage; +8% up to 20 at an arena). Values set during tuning.

---

## 9. Idle earnings & monetization

All ads are opt-in and return a concrete reward; there are no forced interstitials. Everything
purchasable is also earnable in-game.

**Idle earnings (per chapter).** The unit of idle is the **chapter**, and "active" is singular: the
chapter on screen ticks live; every other chapter is dormant. Each chapter's state carries one
`lastActiveUtc`, stamped on switch-away and on save. **Switching into a chapter** pays, for each of
its currencies, `rate × min(elapsed, cap) × idleRate` — computed from *current* state, so Records
earned elsewhere while away correctly boost the payout — then the chapter goes live. **Closing the
app is not a mechanic**: it is the state where no chapter is active, and launching runs the same
switch-in path. In-game chapter switching and time away are one mechanic.

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
| Free, no action | 50% | Auto-collected on switch-in |
| Free, watches ad | 100% (2×) while the buff lasts | "Double it" ad grants a timed buff: `{target: idle_rate, ×2}` |
| Backstage Pass owner | 100% (2×) always | The same effect, derived from a permanent entitlement fact; also raises `idle_cap` |

Idle income is themed as streaming/radio royalties and is largest at the Radio chapter.

**Encore (active boost).** A 2× income boost for a set duration — a timed buff (`{buffId, expiresAt}`
in state; the multiplier derives from it on read). Rewarded ads extend it (~+2 h per ad, cap ~8 h);
sustained use escalates to 4× ("Overdrive" / "Sold-Out Show"), also capped.

**Backstage Pass** — lifetime IAP (~$5–10). Permanently doubles idle earnings, raises the idle cap,
and makes Encore free and automatic. Since ads are opt-in, the Pass's value is convenience.

**Buy Roadies** — consumable, repeatable IAP. Bought Roadies are identical to earned ones. No
purchase cap; throttled by escalating bundle price and by concavity (§8.2). A `bought ≤ earned` cap
is held in reserve for a competitive leaderboard. A late-game Cash → Roadie sink may be offered.

**Tip Jar** — small one-time purchases with no gated content.

**Subscriptions** are not used — the content is replayable rather than expandable.

Any reward for playing beyond Roadie count goes in a separate, unbuyable track (e.g. a "reputation"
multiplier for first-clears).

---

## 10. Story

The story is delivered at chapter boundaries. A card at chapter open sets the scene and the goal
("Pull 200 people and the Friday slot is yours"); a beat at the capstone resolves it and introduces
the next chapter — a UI reaction to the completion flag's transition. There are no story
interruptions during the loop itself.

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

Tuning should assume a player with the full multiplier stack active (up to 4× Encore plus Roadie and
Catalog multipliers) and confirm each chapter still takes meaningful play time.

**Per-chapter economy template (to fill in):**
- 4–6 themed generators (exponential cost, growth ~1.15, Cash in the thousands–millions).
- A Fan target that makes an album cycle meaningful (seconds early, minutes later).
- A Records payout formula (Fans early; Fans × catalog quality from Ch. 6).
- A cumulative-Records capstone gate.
- One new mechanic.

---

## 12. Architecture & build notes (Unity)

### 12.1 Principles

1. **State is stored; everything else is computed on read.** Multipliers, rates, yields, condition
   results, and completion are never stored — they are recomputed from state whenever asked. Nothing
   derived can go stale, double-count, survive a reset it shouldn't, or disagree with a save.
2. **All mutable state lives in the ScopeState tree** (§12.3). Systems are stateless code that reads
   and writes those containers; no system instance per scope, no state hiding in managers.
3. **Lifetime is placement.** A currency, flag, upgrade latch, or bar lives in the scope that
   declares it; the reset that clears the scope clears the fact. Nothing declares a lifetime.
4. **Class families for open sets; flat data for closed records.** Conditions, Actions,
   PayoutFormulas, and BarFillBehaviors are polymorphic families — adding a kind adds a class and
   touches nothing that exists. An Effect is a flat struct because it has one shape.
5. **Validate at content load** (§12.12). Ids resolve, homes are unique, authoring mistakes are
   caught at import, not at runtime.
6. **The UI renders state and forwards intent** through fail-closed entry points. It never mutates.

### 12.2 The economy: Currency, Producer, Generator, Effect

**Currency** — `{id, balance}`. Pure state. It does not know how it is earned.

**Producer** — one per currency, identified by the currency's id; a stateless calculator queried for
two numbers:
- **Rate** (units/second): `Σ(generator output × owned × effects targeting that generator) ×
  effects targeting the currency`. Rates accrue idle time.
- **Yield** (units/firing): the tap/press payout, base contributions × effects. A yield exists only
  when something fires the producer; it never accrues.

Rate and yield are different quantities — per-time vs. per-occurrence — modified separately and
presented separately ("+12/sec" vs. "+5 per press"). Firing is external and unnamed: a button, an
automation, and a test are indistinguishable below the module layer.

**Generator** — the purchasable. Definition: `{id, tags, costCurrency, baseCost, growth,
outputs: [{currencyId, baseRate}]}`; state: `ownedCount` in its declaring scope. A bandmate is a
generator with two outputs (cash, fans) and a `bandmate` tag. Cost currency is independent of
outputs.

**Effect** — the modifier atom:

```csharp
[Serializable] public struct Effect
{
    public string target;      // a currency id (its producer), a generator/bar/group id, or a TAG
    public string stat;        // optional; e.g. "rate"/"yield" on a producer, a currency id on a
                               // generator, empty = every number the target has
    public double multiplier;
}
```

Sources carry a `List<Effect>` — one factor per number, grouping lives in the list, so "×2 rate and
×3 yield" is two entries and no enum ever grows a `Both`. **Modifiers are multipliers only**; a flat
bonus is a *contribution* to the number it raises, authored by whatever fact pays it. Every composed
number has one shape: sum of contributions × product of matching multipliers.

**Tags** — every Definition carries `tags: [...]`; an Effect's target matches an id or a tag. A set
gets its name from its members (`rhythm_section` declared by the drummer and bassist), so buffs never
list members and later additions join by declaring the tag. Tag-or-generator-targeted effects
multiply **inside the sum** (that generator's term); currency-targeted effects multiply **the
total** — one rule, no double counting.

**Reserved target ids:** `idle_rate`, `idle_cap` (§9).

`GetMultiplier(target)` gathers matching effects from every source in §12.6 and multiplies. That is
the entire modifier system.

### 12.3 Scopes: state containers

A **scope** is a plain state container. Content declares, per scope: its currencies, flags, bar
groups, generators, upgrades, and (for tiers) its press. Runtime state per scope:

```csharp
class ScopeState
{
    Dictionary<string, BigDouble> balances;        // + earned totals per currency
    Dictionary<string, int>       generatorCounts;
    HashSet<string>               flags;
    HashSet<string>               purchasedUpgrades;
    Dictionary<string, BigDouble> barProgress;      // uncapped — overfill is allowed
    Dictionary<string, int>       fillCounts;       // repeating bars
    Dictionary<string, HashSet<string>> activeBars; // per group
    List<ActiveModifierEntry>     activeModifiers;  // AddModifier grants: {modifierId, count}
    List<ActiveEvent>             activeEvents;
    DateTime                      lastActiveUtc;    // chapters only (§12.9)
}
```

The tree: **root** (Records, Roadies, entitlements, completion flags, clear counts, Discography) →
**chapters** → **tiers**, and tiers may nest. **Ids are unique tree-wide**; a declaration in two
scopes is refused at load. Flag reads walk the chain outward (set anywhere on it = set); `SetFlag`
writes to the flag's declared home.

**Structure encodes reset relationships:**
- **Nest** tier A inside tier B when "resetting B always resets A" is definitionally true (a ladder).
  An intermediate currency banked by A's press is declared in B — exactly where B's reset claims it.
- **Siblings** when tiers reset independently.
- A press that occasionally resets several siblings lists several `ResetScope` actions.
- Resetting an outer scope while keeping an inner one alive is unrepresentable (`ResetScope` is
  downward-closed) — wanting it means the scopes should be siblings.

Reset = execute the press's actions in order (payouts read the dying state), then clear the selected
containers. Clearing is complete by construction — a field added to ScopeState next month is cleared
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

Kinds: `CurrencyAtLeast`, `EarnedTotalAtLeast`, `OwnedCountAtLeast`, `FlagSet`, `BarsCompleted`,
`All`, `Any`, plus a formula-threshold kind (threshold computed from state — the replay goal reads
the root clear count). Records need no special kind: they are a currency.

Authoring guidance: gate persistent UI on monotonic facts (flags, earned totals), never on spendable
balances (§2). Serialized via `[SerializeReference]` in assets, or a `"type"` field mapped to the
class at import — a kind can never be authored without code behind it.

### 12.5 Actions, presses, and formulas

**Action** — one polymorphic family for "something happens at a moment":

```csharp
public abstract class Action
{
    public abstract void Execute(GameContext ctx);
    public virtual void Validate(ContentDatabase db) { }
}
```

Kinds: `AddCurrency` (amount constant or from a `PayoutFormula`), `AddModifier(scopeId, modifierId)`,
`SetFlag(flagId)`, `AddSong`, `ResetScope(scopeId)`, `ExecuteRung(tierId)`. Authored inline via
`[SerializeReference]` wherever needed — upgrade payloads, bar completions, event rewards, presses.
No shared reward pool; a reused reward can be promoted to a shared asset later if duplication ever
hurts. Actions are one-shot: they run at their moment and are never replayed on load — the state
they mutated is what gets saved.

**`ResetScope`** clears the named scope and everything inside it (downward-closed). It only clears —
it never executes nested lists — so no recursion exists via resets.

**`AddModifier`** appends a pointer-fact `{modifierId}` to the target scope's `activeModifiers`. The
numbers stay in the `ModifierDefinition` (a named `List<Effect>` with an explicit **stack vs.
replace** field); the entry is the fact, saved and cleared with its scope. Reserved for grants from
*moments that leave no other trace* (an event reward); when a count already exists as state, derive
from it instead (§12.6).

**A press** is `{offerCondition, List<Action>}` — the one shape behind the album release (declared
on its tier), the capstone (declared on the chapter), and event entry/completion. `TryPress` is the
UI entry point and is **fail-closed**: it checks the offer condition before executing. **`ExecuteRung`
executes another press's action list *without* re-checking its gate** — deliberate and load-bearing
(the capstone banks whatever Fans exist). References are validated acyclic at load. No press may be
authored on the root scope.

**PayoutFormula** — a polymorphic family computing an amount from readable state
(`floor((fans/5)^0.5)`, piecewise diminishing-returns curves). Pure functions, so UI previews call
the same code the press runs.

### 12.6 Where effects come from

`GetMultiplier(target)` gathers, from every scope on the chain outward:

| Source | The fact (stored) | The effects (derived on read) |
|---|---|---|
| Purchased upgrades | `purchasedUpgrades` set | the upgrade definition's `List<Effect>` |
| Owned generators | `generatorCounts` | contributions scaled by count (rates, not effects) |
| Timed buffs (Encore, Double-it) | `{buffId, expiresAt}` list | the buff definition's effects while unexpired |
| Active events | `ActiveEvent` record | the event definition's handicaps |
| Granted modifiers | `activeModifiers` entries | the `ModifierDefinition`'s effects, per stack rule |
| Repeating bars | `fillCounts` | the bar's `perFill` effects applied count times |
| Career facts | Records balance, Roadie allocation, songs this run, entitlements | the formula-shaped effects of §3/§7/§8 |

Every row is the same pattern: a fact in state, effects computed from it. All of these exist from the
first minute of the game and contribute 1× until their facts exist. Nothing in this table is ever
serialized except the facts column.

### 12.7 Bars

Generic fillable bars: pacing bars (learn covers), repeating currency bars, cascade bars.

```csharp
class BarDefinition : Definition
{
    BigDouble    fillAmount;
    double       fillRate;        // this bar's own max fill speed (units/sec)
    bool         repeating;       // fill → fire onComplete → reset to 0 → go again
    Condition    availableWhen;
    List<Action> onComplete;      // fires at each threshold crossing
    List<Effect> perFill;         // cascade: applied fillCount times, on read
}

class BarGroupDefinition : Definition
{
    string          fillCurrencyId;  // the shared pool (ContinuousDelivery)
    double          pipeRate;        // total throughput the group can spend per second
    int             maxActive;
    BarFillBehavior behavior;        // class family
}
```

**Behaviors:** `ContinuousDelivery` drains the pool currency into the active bars; `TimedFill` fills
from time alone (no pool). Future variants (tap-a-chunk, dump-the-pool) are sibling classes.

**Rate is the pipe.** Each active, unfilled bar demands its own `fillRate`; if the pipe (and the pool
balance) covers total demand, every bar fills at its rate; otherwise all throttle proportionally.
Buffing the pipe (`{target: groupId, ×2}`) eventually lets multiple bars run at their caps in
parallel — rehearsal speed becomes parallel learning. Per-bar speed is buffable by bar id or tag.

**Selection is state**: `activeBars` per group; empty = pool accrues, nothing drains. The UI enforces
`maxActive`; the system doesn't care. On completion the stream **stops** (choosing is the mechanic in
Ch. 1); auto-advance is a `ContinuousDelivery` field a later chapter's automation can grant.

**Completion is derived**: complete ⇔ `progress ≥ fillAmount`. Progress is monotonic until reset, so
the crossing happens once, and that is when `onComplete` fires. Progress is **uncapped** — overfill
is allowed and readable. No completed-set is stored; `BarsCompleted(n)` counts bars at full.

**Cascades** (bar B buffs bar A per fill): B declares `perFill: [{target: barA, ×1.05}]`; the applied
count is B's `fillCount` — the same pattern as generator contributions scaling by `ownedCount`. Each
`perFill` effect declares its stack growth: `multiply` (m^n) or `linear` (1 + (m−1)·n).

### 12.8 Events (runtime)

Defined in §6.1. Runtime is one record in the host scope; the tick decrements `remainingSeconds` and
evaluates the goal; success fires `onComplete` and deletes the record; failure and quit just delete
it. Handicaps apply purely by the record's existence (§12.6). Nothing installs, so nothing tears
down.

### 12.9 Idle (runtime)

Per §9: one active chapter ticks; `lastActiveUtc` stamped on switch-away and save; switch-in pays
`rate × min(elapsed, cap) × idleRate` per currency at current rates, skipped below the minimum-away
threshold and skipped entirely while a timed event runs in that chapter. `idle_rate` and `idle_cap`
resolve through `GetMultiplier` like everything else.

### 12.10 Save

Serialize the current state: the ScopeState tree, nested as the scopes are, plus root facts and the
timed-buff list. JSON + checksum, validated on load (ids resolve; unknown ids from removed content
are dropped with a warning). That is the whole save — no grants, no derived values, no replay of
actions. Idle payouts are capped client-side.

### 12.11 UI

**Authored layout**: a chapter's screen is an ordered list of `SectionDefinition {visibleWhen,
modules}`; a `ModuleDefinition {prefabId, contentId, visibleWhen?}` binds a widget prefab to content
by id. A `ModuleRegistry` maps prefab ids via Addressables — a new widget type is a prefab plus an
entry. Sections live on the `ChapterDefinition`.

**Refresh** is coarse, on two triggers: after each tick of the active chapter, and after an action
list finishes executing (never mid-list — a press's payout, flag, and reset render as one change).
Visible sections re-evaluate `visibleWhen`; visible modules re-read what they show. Event-driven,
never per-render-frame; fine-graining is a mechanical optimization if profiling ever asks.

**Entry points** — the only ways the UI touches the game: `TryPress(press)`, `TryBuy(generator |
upgrade)`, `FireProducer(producerId)`, `SetActiveBars(group, set)`. All fail-closed.

**Widgets interpolate** displayed numbers and bar fills between ticks; presentation only.

### 12.12 Validation at content load

- Every referenced id resolves (currencies, flags, generators, modifiers, scopes, tags in targets).
- Declarations are unique tree-wide (currency, flag, bar ids).
- A tag may not collide with any id; an Effect target matching nothing warns.
- A `SetFlag` naming an undeclared flag is an error; a flag with no setter warns; a flag whose
  setters all live in scopes more durable than the flag warns (§2).
- A press that resets a scope containing tier presses with unreferenced payout actions warns
  (stranded value); a formula-driven grant placed after a `ResetScope` that clears its inputs warns
  (reads zeros); `ExecuteRung` reference cycles are errors; a press on the root scope is an error.
- A balance goal on an event with `resetOnEntry: false` warns.
- A polymorphic kind in data with no class behind it is an import error.

### 12.13 File layout

```
Assets/Scripts/
  Core/
    GameManager.cs          // bootstrap, save/load, active-chapter switching
    TickSystem.cs           // fixed-interval tick on real (DateTime) time
    BigNumber.cs            // wraps break_infinity.cs
    Definition.cs           // base: id + tags, declared once for every content family
    ContentDatabase.cs      // Addressables discovery by label; id→def; the §12.12 validation pass
    ScopeDefinition.cs / ScopeState.cs
    Condition.cs  Action.cs  PayoutFormula.cs   // the class families (+ kind classes)
    Effect.cs               // the flat struct
    GameContext.cs          // read access for Evaluate/Execute: state chain + defs
  Economy/
    CurrencyDefinition.cs
    Producer.cs             // stateless rate/yield calculation + GetMultiplier
    GeneratorDefinition.cs  UpgradeDefinition.cs
    ModifierDefinition.cs   // named List<Effect> + stack/replace
    BarDefinition.cs  BarGroupDefinition.cs  BarFillBehavior.cs  BarSystem.cs
  Loop/
    ChapterDefinition.cs  TierDefinition.cs   // presses declared here
    ChapterManager.cs      // forward-only advance, reacting to root completion flags
  Events/
    EventDefinition.cs  EventSystem.cs
  Meta/
    RoadieAllocation.cs    // root fact + venue-boost derivation
  Content/
    SongDefinition.cs      // Catalog (run) + Discography (root)
  Save/
    SaveSystem.cs          // JSON + checksum; serializes the ScopeState tree
  Monetization/
    AdManager.cs           // rewarded only (Encore top-up + Double it)
    IAPManager.cs          // Backstage Pass, Roadie bundles, Tip Jar
  UI/
    SectionDefinition.cs  ModuleDefinition.cs  ModuleRegistry.cs
    Widgets/ (GeneratorRowUI, BarGroupUI, PressButtonUI, CurrencyHeaderUI, JamButtonUI, ...)
    NumberFormatter.cs  StoryBeatUI.cs  CollectScreenUI.cs  RoadieAllocationUI.cs
ScriptableObjects/  Chapters/  Currencies/  Generators/  Upgrades/  Events/  Bars/  Modifiers/  Songs/
```

### 12.14 Requirements

1. `break_infinity.cs` (BigDouble) for all currency and production values.
2. Tick on real elapsed time (`DateTime.UtcNow` deltas), not frame time.
3. UI refresh on the two triggers of §12.11; no per-frame polling of balances.
4. Checksum saves, validate on load, cap idle earnings client-side.
5. Content in ScriptableObjects discovered via Addressables (a label per type); regular per-chapter
   gear curves can be generated by an editor script. Whether authoring stays in SO assets or moves to
   JSON imported into them is open — the runtime shapes are identical.
6. Run the §12.12 validation pass at boot in development builds; fail loudly.

---

## Appendix — at a glance

- **Structure:** nested prestige on a tree of **state containers** — root / chapters / tiers (tiers
  may nest for ladders, sibling for independence). Lifetime is placement; ids unique tree-wide;
  everything derived is computed on read from stored facts.
- **Records:** the single permanent progression currency; each Record raises global income ~+2%, and
  cumulative Records gate chapter advancement.
- **Presses:** every prestige operation — album release, capstone, event entry — is
  `{offerCondition, List<Action>}`. Payout-before-clear is list order; `ResetScope` is a bare
  downward-closed clear; `ExecuteRung` composes presses without re-checking gates; `TryPress` is
  fail-closed. The capstone resets the **entire chapter**; completion facts live at root; replays are
  the same chapter played again with a rising goal (`base × 1.6^k` from the root clear count).
- **Economy:** one producer per currency (rate + yield, computed on read); generators contribute,
  scaled by owned count; **Effect** = `{target: id-or-tag, stat, multiplier}`, gathered on read from
  facts (purchases, timed buffs, events, modifier grants, fill counts, career totals) and never
  stored; flat bonuses are contributions; tags name sets from the member side.
- **Bars:** generic fillables — own fill rate per bar, group pipe rate (proportional throttle),
  optional pool currency or time-fill, repeating with per-fill cascade effects scaled by fill count,
  uncapped overfill, completion derived from progress.
- **Events:** data + one ActiveEvent record; entry resets the host (banking its payout, so entry is
  free); handicaps are ×<1 effects that exist while the record does; timers tick live only and
  suppress idle; success/fail/quit all just delete the record; rewards lateral, never Records.
- **Idle:** per chapter — one active chapter ticks; switch-in pays `rate × min(t, cap) × idleRate`
  (base 50%/4 h; `idle_rate`/`idle_cap` are effect targets); app close is not special; yields and
  bar progress never accrue.
- **Monetization:** opt-in ads only; timed double-idle buff; Encore 2×/Overdrive 4×; Backstage Pass
  (lifetime); Buy Roadies (repeatable); Tip Jar; no subscriptions.
- **Engine:** Unity; break_infinity numbers; DateTime ticks; checksummed JSON save of the state tree;
  Addressables content discovery; load-time validation of all authored data.
