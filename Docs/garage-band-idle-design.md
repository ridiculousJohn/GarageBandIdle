# Garage Band Idle — Design & Build Spec

An idle game about a band rising from a garage to arenas. Play progresses through eight chapters, each
a bigger venue with a new mechanic. All numbers below are starting values for tuning.

> **Revision note (data-model consolidation pass).** Changes in this revision, all to keep the doc in
> sync with the restructured `chapter-01-garage.json` and the build prompts:
> - **§3** — Rehearsal added as a first-class run-scoped currency; "learn-songs bars" reframed as
>   generic *fillable bars* fed by a `fillCurrency`.
> - **§4** — content-unlock reveal is now stated to run through the single flag registry.
> - **§6** — learn-songs bars note that filling is *player-directed* when several are offered at once.
> - **§12** — three architecture rules added (unified `Condition` type + evaluator; one flag registry
>   for all reveal; all content ScriptableObjects discovered via Addressables); `LearnSongBar.cs`
>   generalized to a bar/bar-group system; `UpgradeDefinition` payload comment updated to `setFlag`.
> - **Appendix** — a "data model" line added.
> Edited passages are marked inline with **[rev]**.

---

## 1. Core loop

The game has two loops.

**The album loop (inner).** Within the current chapter the player taps for Cash, buys gear and
bandmates, grows Fans, and then releases an album. Releasing an album resets the run — Cash, gear,
Fans, and the working Catalog — and awards **Records**. Each Record permanently increases global
income, so the next run is faster. The player repeats this loop several times within a chapter.

**The chapter loop (outer).** Cumulative Records unlock the current chapter's capstone gig. Playing the
capstone advances the player to the next chapter and does not reset chapter progress — the climb is
forward only. After advancing, releasing an album resets the run back to the start of the *current*
chapter, not the garage.

Records are the link between the loops: they raise income and gate chapter advancement. Chapter
advancement therefore depends on releasing albums over time, not on a single large Cash total.

```
   INNER (minutes):  tap → Cash → gear → Fans → release album ─┐
                       ▲  reset run, +Records, repeat faster    │
                       └────────────────────────────────────────┘
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
| 7 | Radio / Streaming | Royalty catalog scaling; large offline income | First charting single / radio play |
| 8 | Arenas / Stadiums | World tours, endgame scaling | Sell out a stadium (Hall of Fame) |

Every chapter uses the same rhythm — tap, buy, grow Fans, release an album — with new gear, a new
mechanic, and a higher Records gate.

**Chapter anatomy.** A chapter consists of: local currencies (Cash, usually Fans, often a
chapter-specific currency); generators; an upgrade tree (§4); a set of opt-in events (§6); and a
capstone gig gated by Records.

**Progressive reveal.** A chapter does not present all its mechanics at once. Content-unlock upgrades
(§4) introduce new generators, currencies, and mechanics as the player buys them, so the chapter opens
up in stages. Each such upgrade should introduce a change in play — a new mechanic, sub-loop, or
automation step — rather than only increasing a number, so that a chapter keeps changing as the player
works through it instead of settling into a single repeated action.

Once a chapter is cleared it remains available as a replay economy (§8.1).

---

## 3. Currencies

Currencies are either **run-scoped** (reset on album release) or **permanent** (persist across
albums).

**Run-scoped:**
- **Cash** — earned by tapping and generators; spent on gear and upgrades.
- **Gear & bandmates** — generators bought with Cash (+Cash/tap, +Cash/sec, +Fan rate). A generator
  flagged as a bandmate also raises the Fan rate (§6); bandmate-ness is a data flag, not a hardcoded
  list. **[rev]**
- **Rehearsal (and later chapters' equivalent fill currencies)** — a run-scoped currency earned from
  engagement (a passive tick plus taps), spent to fill learn-songs bars. Rehearsal is Chapter 1's fill
  currency; a later chapter may define its own. It is an ordinary currency — it owns its earn config,
  and bars reference it by id. **[rev]**
- **Learn-songs bars** — generic *fillable bars* that pace a chapter (learn covers, rehearse). Each bar
  declares a `fillCurrency` (Rehearsal in Ch. 1), a fill requirement, and a reward granted on
  completion; the fill logic reads `fillCurrency` and is not covers-specific. Fed by a fill currency
  rather than being their own opaque mechanic. Separate from the Catalog (§7). **[rev]**
- **Fans** — the run's performance meter; determines the album's Records payout on release.
- **Catalog (Ch. 6+)** — songs written during the run; a global income multiplier that converts to
  Records on album release (§7).

**Permanent:**
- **Records** — each Record increases global income; cumulative Records gate chapter advancement.
  Accumulated, never spent.
- **Roadies** — crew; a global multiplier allocated across cleared chapters (§8).
- **Discography** — a list of the player's best named songs (§7). Display only.

**Income formula.** All multipliers combine multiplicatively:
```
income = Σ(generator base × count × buff upgrades)
         × catalogBoost      (run-scoped, §7)
         × recordsMultiplier (permanent, §5)
         × roadieTotalBoost  (permanent, §8)
         × encoreBoost       (temporary 1×/2×/4×, §9)
```

A multiplier is an output effect that **declares which currencies it affects** (plural, by id);
generator production of a currency no multiplier names is untouched. The Records multiplier affects
Cash in Chapter 1. A currency never opts into a multiplier — the dependency points from the
multiplier to its targets.

---

## 4. Upgrades

Upgrades are the primary way a chapter's content is delivered. The player buys them with chapter
currencies as they become affordable.

- **Gating.** An upgrade can be gated on any chapter currency, not only Cash. Which currency unlocks
  which upgrade defines the order in which the player develops each currency, and gives each chapter a
  distinct shape. A gate is expressed as a single `Condition` (§12), so gating on Fans instead of Cash
  is the same shape with a different currency id — no special case. **[rev]**
- **Payloads.** An upgrade can grant a flat bonus, a multiplier, a new generator, a new currency, an
  automation step, a new sub-loop, or a new mechanic.
- **Reveal.** A content-unlock upgrade reveals its content by **setting a flag** in the single flag
  registry (§12); the revealed content (a currency, a section, a bar group, a button) gates its own
  visibility on that flag. Rewards (§6.1) can set flags too. There is one reveal mechanism, not one per
  content type. **[rev]**
- **Scope.** *Buff upgrades* are run-scoped: they reset on album release and are re-bought each run
  (faster as Records accumulate). *Content-unlock upgrades* (new generator, currency, or mechanic) are
  permanent within the chapter: the unlock persists across albums; only owned counts reset.

---

## 5. The album (prestige)

Releasing an album is the run reset. Its name escalates thematically across chapters (demo, EP,
record).

- **Resets:** Cash, gear, learn-songs bars, Fans, working Catalog.
- **Keeps:** Records, Roadies, Discography.
- **Awards Records** based on run performance:
  ```
  early chapters:  recordsEarned = f(fansThisRun)
  Ch. 6+:          recordsEarned = f(fansThisRun, totalCatalogQuality)
  ```
- **Each Record** grants about `+2%` permanent global income (additive).
- **Cumulative Records** unlock each chapter's capstone at a set threshold (§11).

An early album cycle takes seconds to minutes; cycles get faster as Records accumulate.

---

## 6. Within-a-chapter play & events

Moment-to-moment play draws on the systems defined elsewhere:
- **Tap ("Jam")** — early Cash source; its relevance falls off as gear automates income.
- **Generators** — exponential cost, `cost = base × growth^owned`, growth ~1.15; a themed set per
  chapter. A generator's cost declares its currency, independent of what it produces (all Chapter 1
  gear costs Cash) — "buy with Cash, produce Merch" is a data shape, not a special case. Because runs
  reset, a chapter's Cash stays in the thousands–millions range; cross-chapter growth comes from
  Records and Roadies.
- **Upgrades (§4).**
- **Learn-songs bars** — generic fillable bars (§3) that give early chapters an activity beyond
  watching a number. A bar fills by spending a fill currency (Rehearsal in Ch. 1, earned from taps plus
  a passive tick), so progress comes from engagement rather than Cash. When a group offers several bars
  at once, filling is **player-directed**: the player chooses which bar to pour the fill currency into
  and each bar tracks its own progress independently — a small prioritization decision rather than an
  automatic sequence. *Delivery* — how currency moves from the pool into the chosen bar — is per-group
  data, separate from that bookkeeping: Chapter 1 uses `continuous` (accrued currency streams into the
  active bar; selecting a bar IS the interaction), and future chapters can add tap-a-chunk or
  dump-the-pool variants as new delivery values rather than new systems. **[rev]**
- **Fans** — accrue passively once revealed: a base rate plus a per-bandmate bonus, a function of
  band size and time only — never Cash or income. Fan rate is tuned loosely relative to Cash so that
  income alone does not determine the album payout.
- **Capstone gig** — unlocks at the Records gate; grants a Roadie and fires a story beat (§10).

### 6.1 Events

An event is a self-contained challenge inside a chapter that the player enters by choice. Events do not
gate chapter advancement — the gate is always Records, reachable by playing — and their rewards are
lateral (never Records), so no event is ever a literal hard requirement.

How essential an event feels is a **per-event tuning decision, set by the size of its reward.** Because
an event never blocks the gate, its reward magnitude alone places it anywhere on a spectrum: a small
reward makes an event a minor bonus a player can freely skip; a large reward makes completing it so
beneficial to chapter pace that a reasonable player will do it, and skipping it means a much slower
grind. The chapter is always completable without any given event, but only quickly with the events its
tuning intends the player to do. Chapter pacing is set with each event's intended engagement in mind.

- **On start,** the chapter's economy resets to a baseline for the duration of the event, so the
  challenge runs at a fixed scale independent of the player's accumulated power. This is what lets a
  debuff be meaningful — the player is working from a known floor rather than an arbitrary fortune.
- **Goal:** reach a target amount of a currency.
- **Debuff (optional):** the run is modified — generation halved, automation disabled, tap-only, a
  currency locked. Debuffs change how the loop is played, which is where an event's variety comes from.
- **Timer (optional):** adds a time limit. Timed events are the only events that can be failed.
- **Failure:** a failed timed event resets that event's progress; the player can quit an event at any
  time. Failing or quitting costs only the time spent, not permanent progress, so entering an event is
  always low-risk.
- **Reward on success:** a lateral bonus — a permanent-in-chapter buff, a Roadie, a Catalog song, or
  local currency, drawn from the shared reward pool (§12). Event rewards never include Records or any
  currency that gates advancement, so an event is never a hard prerequisite; its reward size (above) is
  what sets how much it matters. **[rev]**
- **Tiers:** an event can repeat at higher tiers with a higher starting requirement, a stronger debuff,
  and a larger reward. The rising requirement across tiers is a natural throttle, which makes tiered
  events a repeatable source of Roadies.

Event authoring guidelines: most events use debuffs; timed events are used sparingly; failure stays
cheap; larger events include a decision (risk/reward, or which song to submit) rather than a single
confirm.

---

## 7. Songwriting: Catalog & Discography (Ch. 6+)

Songwriting unlocks at the Studio chapter.

- **Writing a song** rolls a quality tier — Common, Hit, or Classic — and the player names it. Song
  quality feeds a run-scoped global multiplier:
  ```
  catalogBoost = 1 + Σ(quality weight per song this run)   // e.g. Common .01 / Hit .05 / Classic .20
  ```
- The multiplier is driven by song **quality**, not song count, so songwriting is about improving songs
  rather than accumulating them. It applies to all income, so a high-quality catalog raises earnings at
  every venue, and also feeds royalty/offline income.
- **On album release,** total catalog quality is the main input to the Records payout (§5), and the
  working Catalog resets with the run. Routing catalog value into Records keeps permanent progression
  consolidated in a single currency.
- **Discography** is a persistent list of the player's best songs, kept for display after the working
  Catalog resets.

The three song-related systems are separate: learn-songs bars pace early chapters (run-scoped);
Catalog is the studio-era multiplier (run-scoped, converts to Records); Discography is a persistent
display list.

---

## 8. Roadies

Roadies are a permanent global multiplier. The player earns them from capstones and from replaying
cleared chapters (§8.1), and can also buy them (§9). All Roadies go into one pool and can be
reassigned freely.

### 8.1 Cleared chapters as replay economies

Replaying cleared chapters is the main way to earn Roadies through play, which keeps Roadies earnable
rather than purchase-only.

A cleared chapter remains available as a self-contained economy with its own local currency,
generators, and completion goal. This replay economy is isolated: the player's global income and
progress do not apply inside it, so it runs at its own scale regardless of how far the player has
advanced overall. The isolation is what keeps an early chapter worth replaying late — it cannot be
cleared instantly by the player's accumulated power, because that power does not reach inside it.

Replaying a chapter means building its local economy up to the current goal and clearing it, which
awards a Roadie. Each clear raises that chapter's next goal:
```
replayGoal(k) = base × H^k      // k = times already cleared, H ≈ 1.6
```
The first few clears stay at the base goal before the requirement begins to rise. Because the goal
rises with each clear and the multiplier from spreading roadies is concave (§8.2), repeatedly farming
one low chapter gives diminishing returns rather than an unlimited source of Roadies.

Roadies connect a replay economy to the rest of the game in two ways: Roadies stationed at a chapter
increase that chapter's local production (faster replays), and clearing a chapter's goal adds a Roadie
to the global pool.

### 8.2 Boost formula

- **Within a venue (additive):** `venueBoost = 1 + 0.05 × roadiesOnVenue`
- **Across venues (multiplicative):** `totalBoost = venueBoost₁ × venueBoost₂ × …`

`totalBoost` is the permanent multiplier applied to frontier income. Example: 9, 9, 8, and 9 roadies
across four venues give 1.45 × 1.45 × 1.40 × 1.45 = 4.27×.

Because venue boosts multiply, distributing roadies across more venues yields a higher total than
concentrating them (8 roadies give 1.40× on one venue, 1.46× split across four). Each `venueBoost`
also sets that chapter's local replay speed (§8.1), so allocation balances two goals: spreading crew
for a higher total multiplier, and concentrating crew to speed up a chapter being actively replayed.

**Per-venue scaling (planned).** Larger venues will use a higher per-roadie rate and a higher roadie
cap than smaller ones (for example, +5% up to 5 roadies at the garage; +8% up to 20 at an arena), so
larger venues reward more crew. Values to be set during tuning.

---

## 9. Offline earnings & monetization

All ads are opt-in and return a concrete reward; there are no forced interstitials. Everything
purchasable is also earnable in-game.

**Offline earnings.** `offline = productionPerSecond × min(secondsAway, cap) × rate`, with **rate =
50%** and **cap = 4 hours** (raisable via the Backstage Pass). The base rate is set at 50% so that the
doubled value is a full 100%, i.e. the ad or the Pass grants full offline earnings rather than a bonus
on top. Offline income is themed as streaming/radio royalties and is largest at the Radio chapter.

| Player | Offline payout | How |
|---|---|---|
| Free, no action | 50% | Auto-collected |
| Free, watches ad | 100% (2×) | "Double it" ad on the collect screen |
| Backstage Pass owner | 100% (2×) | Automatic, no ad |

**Encore (active boost).** The player activates a 2× income boost for a set duration. Rewarded ads
extend the duration (~+2h per ad) up to a cap (~8h). Sustained use escalates it to 4× ("Overdrive" /
"Sold-Out Show"), also capped.

**Backstage Pass** — lifetime IAP (~$5–10). Auto-doubles offline earnings and makes the Encore boost
free and automatic. Raises the offline cap. Since ads are opt-in rather than forced, the Pass's value
is convenience: the boosts that free players get by watching ads are applied automatically instead.

**Buy Roadies** — consumable, repeatable IAP. Bought Roadies are identical to earned Roadies. There is
no purchase cap; buying is throttled by escalating bundle price and by the fact that a large early pile
of Roadies is inefficient (see the distribute-vs-concentrate behavior in §8.2). A `bought ≤ earned`
cap is held in reserve for the case where a competitive leaderboard is added. An in-game Cash → Roadie
sink may also be offered in the late game.

**Tip Jar** — small one-time purchases with no gated content.

**Subscriptions** are not used. The game's content is replayable rather than expandable, so there is no
recurring content to attach a subscription to.

Any reward for playing beyond Roadie count is placed in a separate, unbuyable track (for example, a
"reputation" multiplier for first-clears).

---

## 10. Story

The story is delivered at chapter boundaries. A card at chapter open sets the scene and the goal ("Pull
200 people and the Friday slot is yours"); a beat at the capstone resolves it and introduces the next
chapter. There are no story interruptions during the loop itself.

Named Catalog songs (§7) serve as story artifacts — the songs that chart appear in the Discography and
persist.

---

## 11. Pacing & tuning

Chapter pacing is set primarily by the per-chapter Records gate, which determines how many album cycles
a chapter takes and therefore the overall game length. Records gates are the first tuning lever;
generator curves are adjusted only after.

Two structural properties keep pacing stable against players with strong income multipliers:
- Chapters gate on Records, not Cash. Multipliers raise Cash, but advancement requires accumulating
  Records through album releases.
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

## 12. Build notes (Unity)

Content is data-driven via ScriptableObjects so chapters, gear, and songs are data assets. All
definition ScriptableObjects are discovered at runtime through **Addressables** (a label per type),
not direct references or hardcoded lists (see architecture rule 10). **[rev]**

```
Assets/Scripts/
  Core/
    GameManager.cs        // bootstrap, save/load + tick orchestration
    TickSystem.cs         // fixed-interval update on real (DateTime) time
    BigNumber.cs          // wraps break_infinity.cs
    CurrencyManager.cs    // run block (Cash/Fans/Rehearsal/gear/catalog) + permanent block (Records/Roadies)   // [rev]
    ContentDatabase.cs    // [rev] Addressables discovery of all definition SOs by label; id→def registries
    Condition.cs / ConditionEvaluator.cs   // [rev] one gate/unlock/visibility/availability type + one evaluator
    FlagManager.cs        // [rev] single reveal registry (permanent-in-chapter flags)
  Loop/
    ChapterDefinition.cs / Chapter.cs   // mechanic, capstone, Records gate, story beat
    ChapterManager.cs     // forward-only advancement + unlocks
    AlbumPrestige.cs      // reset run, compute + award Records
  Economy/
    GeneratorDefinition.cs / Generator.cs   // isBandmate is a data field the fan system reads   // [rev]
    UpgradeDefinition.cs / Upgrade.cs   // [rev] payload = buff | setFlag (reveal via flag); gate = any Condition; scope = run | permanent-in-chapter
    BarDefinition.cs / BarGroupDefinition.cs / BarSystem.cs   // [rev] generic fillable bars (fillCurrency-driven); replaces LearnSongBar
    RewardDefinition.cs / RewardManager.cs   // [rev] shared reward pool; Apply(rewardId) dispatches on type (incl. setFlag)
    CostCalculator.cs / ProductionCalculator.cs
  Events/
    EventDefinition.cs / GameEvent.cs   // baseline reset, optional debuff, optional timer, goal, tier, reward
    EventManager.cs       // enter/quit/fail/succeed, tiers, sandboxed economy snapshot
  Meta/
    RoadieManager.cs      // pool, per-venue allocation, product boost, replay ramp
    RecordsManager.cs     // permanent buff + chapter-gate thresholds
  Content/
    SongDefinition.cs / Song.cs         // Catalog (run) + Discography (permanent)
  Save/
    SaveData.cs / SaveSystem.cs         // JSON + checksum
    OfflineEarnings.cs    // DateTime delta on load, 50% base
  Monetization/
    AdManager.cs          // rewarded only (Encore top-up + offline Double it)
    IAPManager.cs         // Backstage Pass (non-consumable) + Roadie bundles (consumable) + Tip Jar
  UI/
    ChapterScreenUI.cs  StoryBeatUI.cs  CollectScreenUI.cs
    SectionView.cs  ModuleRegistry.cs   // [rev] data-driven layout: sections + module→prefab (Addressables) with visibleWhen Conditions
    RoadieAllocationUI.cs  GeneratorRowUI.cs  NumberFormatter.cs
ScriptableObjects/  Chapters/  Currencies/  Generators/  Upgrades/  Events/  Bars/  Rewards/  Songs/
```

**Architecture requirements:**
1. Use `break_infinity.cs` (or equivalent big-number type) for all currency and production values.
2. Run the tick loop on real elapsed time (`DateTime.UtcNow` deltas), not frame time, so offline
   calculation is correct.
3. Drive UI from events; do not poll balances per frame.
4. Checksum saves and validate on load; cap offline earnings in the client.
5. Keep content in ScriptableObjects, discovered via Addressables (rule 10); the regular per-chapter
   gear curve can be generated by an editor script. **[rev]**
6. Separate the run block and permanent block in the save schema. An album release clears the run block
   and writes the permanent block.
7. Store each cleared chapter's replay economy as its own state block (local currency, generators, goal
   `k`), separate from frontier state. The only cross-writes are Roadie allocation in and Roadie award
   out.
8. **[rev]** Express every gate/unlock/visibility/availability rule as a single `Condition` type
   evaluated by one `ConditionEvaluator` — no per-currency or per-rule branches. Condition types:
   `currency`, `currencyEarnedTotal`, `ownedCount`, `flagSet`, `barsCompleted`, `recordsCumulative`,
   `compound` (all/any).
9. **[rev]** Drive all progressive reveal through one flag registry: a content-unlock upgrade (or a
   reward) sets a flag; revealed content gates its visibility on a `flagSet` Condition. No parallel
   reveal paths (no separate "unlockSystem").
10. **[rev]** Discover all content ScriptableObjects (chapters, currencies, currency groups, generators,
    upgrades, events, bars, rewards) via Addressables labels; managers build their id→definition
    registries from the labelled assets, not from hardcoded lists or direct references. Validate that
    every referenced id resolves on load.

**Starter prompt for a code assistant:**
> "In Unity (version X, iOS/Android), scaffold a nested-prestige idle core: a CurrencyManager with a run
> block (Cash, Fans, Rehearsal, gear, catalog) and a permanent block (Records, Roadies) using
> break_infinity.cs BigDouble; content discovered via Addressables; a single Condition type + evaluator
> for all gates/unlocks/visibility; one flag registry for all progressive reveal; an AlbumPrestige
> action that clears the run block and awards Records from fans (later fans × catalog quality); a
> ChapterManager with forward-only advancement gated by cumulative Records; a TickSystem on DateTime
> deltas; and a checksummed JSON SaveSystem computing offline earnings at 50% base capped at 4h.
> Event-driven, no per-frame polling."

---

## Appendix — at a glance

- **Structure:** nested prestige — an inner album loop inside an outer, forward-only chapter climb
  (8 chapters).
- **Records:** the single permanent progression currency; each Record raises global income, and
  cumulative Records gate chapter advancement.
- **Per-chapter systems:** an upgrade tree (upgrades gated on any currency) plus opt-in events. Chapters
  reveal their mechanics progressively through content-unlock upgrades.
- **Album (prestige):** resets the run, awards Records from run performance (Fans, and catalog quality
  from Ch. 6); repeated several times per chapter.
- **Events:** self-contained challenges that reset the economy to a baseline; optional debuff and/or
  timer; only timed events can fail; failure costs time only; rewards are lateral (never Records);
  tiered.
- **Catalog (Ch. 6+):** quality-driven global multiplier that converts to Records on album release;
  Discography keeps a persistent list of best songs.
- **Roadies:** permanent multiplier — additive within a venue (+5%/roadie), multiplicative across
  venues. Earned from capstones and from replaying sealed chapter economies; buyable; earned and bought
  Roadies are identical; no purchase cap.
- **Data model [rev]:** gates/unlocks are a single `Condition` type (one evaluator); all progressive
  reveal runs through one flag registry (`setFlag` → `flagSet`); learn-songs bars are generic fillables
  driven by a `fillCurrency` (Rehearsal in Ch. 1); every content ScriptableObject is discovered via
  Addressables.
- **Monetization:** opt-in ads only (no forced interstitials); offline 50% with an optional 2×;
  Encore 2× / Overdrive 4×; Backstage Pass (lifetime); Buy Roadies (repeatable); Tip Jar; no
  subscriptions.
- **Engine:** Unity.
