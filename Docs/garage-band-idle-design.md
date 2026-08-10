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

> **Revision note (modifier consolidation pass).** Changes in this revision, all following the move of
> every stat modifier out of the individual systems and into one registry:
> - **§12** — architecture rule 11 added (one modifier registry: systems compose on read rather than
>   holding stacks, granted modifiers carry a scope and derived ones do not, one call resets the
>   run-scoped ones); `ProductionCalculator` noted as formula-only, `Modifiers/` added to the file
>   tree, and the starter prompt names the registry beside the Condition and flag rules.
> Edited passages are marked inline with **[rev]**, as above.

> **Revision note (economy-context pass).** Changes in this revision, all following the decision to
> run every simultaneous economy (the frontier chapter, an event sandbox, a replay economy) as an
> instance of one machinery rather than as filtered views of a single economy:
> - **§2** — open decision flagged: whether frontier run currencies carry across a capstone
>   advancement or each chapter's are distinct ids; settle before Chapter 2 content.
> - **§3** — currency *definitions* stay global; *balances* live in per-context pools, and a
>   currency's **group** declares its pool (placement joins reset behavior as group data).
> - **§6.1** — the event baseline restated as a freshly constructed context, not a filtered view.
> - **§8.1** — replay isolation restated as construction (own context, own currency ids, no
>   chapter-permanent inheritance), not exemption.
> - **§12** — rule 6 sharpened (saves store facts, never grants); rule 7 (a replay economy is its own
>   pool and context instance); rule 11 extended (an effect's durability is its source fact's
>   durability); rule 12 added (the economy context and its per-context projection recipes);
>   `CurrencyManager` and the file tree updated to match.
> Edited passages are marked inline with **[rev]**, as above.

> **Revision note (idle-accrual pass).** Changes in this revision, all following the decision that
> there is no app-level "offline" — idle earnings are per-economy, based on how long that specific
> economy has been unfocused:
> - **§6.1** — timed events disable idle payouts while running; the timer pauses while the event is
>   unfocused *(provisional — pause behavior to be verified against Ctrl C)*.
> - **§9** — offline earnings restated as per-economy idle accrual on focus-gain (generator
>   production only; fans/rehearsal/bars pause while unfocused); the "Double it" ad grants a timed
>   double-idle buff (an expiry fact, the same shape as Encore) rather than doubling one collect.
> - **§12** — rule 7 gains the per-economy last-interaction timestamp; rule 12 gains the context
>   lifecycle (constructed → focused ⇄ unfocused → discarded, exactly one focused).
> - **File tree / appendix** — `OfflineEarnings.cs` renamed `IdleEarnings.cs`; summary lines updated.
> Edited passages are marked inline with **[rev]**, as above.

> **Revision note (per-chapter frontier pass).** The §2 open decision is settled: run currencies are
> per-chapter ids and advancement opens the next chapter's economy fresh; the capstone implicitly
> cuts an album so no run value is stranded at the boundary. Edits: **§1** (the capstone banks Fans
> as Records before advancing), **§2** (decision settled), **§6** (capstone bullet), **§12** rule 12
> (the unique-id policy now names each chapter's run currencies). Marked **[rev]** inline, as above.

> **Revision note (production-config pass).** A currency no longer declares how it is earned: the
> engagement earn config moves off the currency definition, and every flat-rate currency source
> becomes a **production config** — `{currency, amount, trigger: tick | tap, gate: Condition}` —
> held by its producer, pointing at the currency it creates (the same dependency direction
> multipliers already use). Two holder kinds today: **generators** (their `produces`/`baseOutput` is
> a tick-triggered config, scaled by owned count and wrapped in purchase mechanics) and **modules**
> (the Jam button holds its per-tap yields — Cash, and Rehearsal once revealed — plus Rehearsal's
> passive trickle). Currencies become pure state: a balance, a group, formatting. Idle eligibility
> falls out by construction: generators are the only idle-eligible holder (§9), so module-held
> production never idle-pays and no per-config idle flag exists. Edits: **§3** (Rehearsal bullet),
> **§6** (tap bullet), **§9** (idle boundary restated as the holder), **§12** (rule 13 added; file
> tree). Marked **[rev]** inline, as above.

---

## 1. Core loop

The game has two loops.

**The album loop (inner).** Within the current chapter the player taps for Cash, buys gear and
bandmates, grows Fans, and then releases an album. Releasing an album resets the run — Cash, gear,
Fans, and the working Catalog — and awards **Records**. Each Record permanently increases global
income, so the next run is faster. The player repeats this loop several times within a chapter.

**The chapter loop (outer).** Cumulative Records unlock the current chapter's capstone gig.
**[rev]** Playing the capstone **implicitly cuts an album** — the run's Fans bank as Records as part
of the show — and then advances the player to the next chapter, whose economy opens fresh (run
currencies are per-chapter, §2/§3). Permanent chapter progress — Records, and any flag or unlock
declared permanent-in-chapter — is never reset: the climb is forward only. Run-scoped flags and
unlocks are the other tier and reset with every album release (§2), which is what makes a second run
re-walk the progression. After advancing, releasing an album resets the run back to the start of the
*current* chapter, not the garage.

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

**[rev] Settled:** each chapter's run currencies are distinct ids, and advancement starts the new
chapter's economy fresh (the Ctrl-C-compatible reading). To keep un-released value from being
stranded — and to remove the release-before-capstone ritual stranding would create — the capstone
implicitly cuts an album (§1, §6): the run's Fans convert to Records as part of the show, then the
next chapter opens fresh. Every frontier context is therefore just the current chapter's context,
with the same lifecycle as an event sandbox or replay economy (§12 rule 12), and §6's promise that a
chapter's Cash stays in the thousands–millions range is structural rather than tuned.

**Progressive reveal.** A chapter does not present all its mechanics at once. Content-unlock upgrades
(§4) introduce new generators, currencies, and mechanics as the player buys them, so the chapter opens
up in stages. Each such upgrade should introduce a change in play — a new mechanic, sub-loop, or
automation step — rather than only increasing a number, so that a chapter keeps changing as the player
works through it instead of settling into a single repeated action.

**[rev] Settled:** a section is visible exactly *while* its `visibleWhen` holds — evaluated live,
with no latch or lifetime of its own. Persistence is a property of STATE, never of UI: "stays once
earned" is authored by gating on a fact with that lifetime — a flag (whose declaration carries the
scope), or a monotonic value like an earned total — and a threshold moment worth remembering is
latched by a passive content unlock setting a flag (Ch. 1's `browse_gear` at 250 Cash). Gating a
region directly on a spendable balance is an authoring smell: it strobes with every purchase.
Distinct from visibility is an action's *pressability* (e.g. the release button, §5), a live
condition on the content the module presents.

**[rev] Settled:** flags declare their lifetime on their declaration in the chapter's flags list —
never on the `setFlag` effects that set them, so one flag cannot carry two lifetimes. A run-scoped
flag clears at every release, and everything gating on it (sections, bar groups, production
configs, meters) goes dark together, re-arming when a run-scoped setter's own gate re-fires — so a
whole sub-system re-opens through ONE condition authored in ONE place. This is how the second run
re-walks the chapter's progression (band → fans → covers → gear) instead of opening with every
system already on screen: Ch. 1 authors `fans`, `covers` and `gear` (and their setter unlocks) as
run-scoped, while `album` stays permanent — the release button's *region* is knowledge, its
pressability an offer (§5). Boot validation enforces the pairing: a run-scoped flag whose setters
are all permanent is a content error (the release's own projection would re-assert it), and a flag
no content sets warns.

Once a chapter is cleared it remains available as a replay economy (§8.1).

---

## 3. Currencies

Currencies are either **run-scoped** (reset on album release) or **permanent** (persist across
albums).

**[rev]** A currency *definition* is global — one registry of everything assignable — but *balances*
live in per-context pools (§12 rule 12): one permanent pool created at startup, plus one pool per
economy context (the frontier chapter, an event sandbox, a replay economy). Which pool holds a
currency's balance is declared by its **group**, so placement is group data beside
`resetsOnAlbumRelease`; a run-scoped global currency is incoherent and fails validation. An album
release resets balances *within* the living chapter pool — instance death and run reset are
different events.

**Run-scoped:**
- **Cash** — earned by tapping and generators; spent on gear and upgrades.
- **Gear & bandmates** — generators bought with Cash (+Cash/tap, +Cash/sec, +Fan rate). A generator
  flagged as a bandmate also raises the Fan rate (§6); bandmate-ness is a data flag, not a hardcoded
  list. **[rev]**
- **Rehearsal (and later chapters' equivalent fill currencies)** — a run-scoped currency earned from
  engagement (a passive tick plus taps), spent to fill learn-songs bars. Rehearsal is Chapter 1's fill
  currency; a later chapter may define its own. It is an ordinary currency — pure state like any
  other; its accrual comes from production configs held by the chapter's Jam module (a per-tap yield
  plus a passive trickle, §12 rule 13) and bars reference it by id. **[rev]**
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
- **Scope.** **[rev]** An upgrade's lifetime is authored on its declaration — `run` or
  permanent-in-chapter — never implied by its type (one declaration owns the lifetime, the same rule
  as flags, §2). *Buff upgrades* are run-scoped: they reset on album release and are re-bought each
  run (faster as Records accumulate). *Content-unlock upgrades* (new generator, currency, or
  mechanic) carry the scope their reveal needs: Ch. 1 authors its reveal chain (the `fans`/`covers`/
  `gear` setters) run-scoped so the second run re-walks the progression (§2), while a
  permanent-in-chapter unlock persists across albums with only owned counts resetting.

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

**[rev] Settled:** the release is *offered* only while the chapter's album unlock condition holds
(the same condition that first revealed it — e.g. Ch. 1's 50 Fans + 1 learned cover). Its inputs are
run values the release itself resets, so the offer disarms at every release and re-arms on the
re-climb — including re-learning a cover, since bars are run-scoped. The release *region* stays on
screen because the `album` flag it gates on is permanent-in-chapter (§2); only pressability tracks
the condition. The release *operation* is deliberately ungated: the capstone implicitly cuts an
album (§2) whether or not the offer holds.

---

## 6. Within-a-chapter play & events

Moment-to-moment play draws on the systems defined elsewhere:
- **Tap ("Jam")** — early Cash source; its relevance falls off as gear automates income. **[rev]**
  The button is an authored module that holds its tap-triggered production configs (§12 rule 13) —
  Cash always, Rehearsal once revealed — plus Rehearsal's passive trickle, so what engagement yields
  is producer data, never currency data or code.
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
  automatic sequence. How a group fills is a polymorphic *fill behavior*, the same shape as Condition:
  the JSON's `fillMode` + `delivery` vocabulary maps onto a concrete behavior class at import, so a mode
  can never be authored without code behind it. Chapter 1's behavior is per-bar with continuous delivery
  (accrued currency streams into the active bar; selecting a bar IS the interaction); tap-a-chunk or
  dump-the-pool variants are sibling behavior classes, and their JSON vocabulary exists only once the
  class does. **[rev]**
- **Fans** — accrue passively once revealed: a base rate plus a per-bandmate bonus, a function of
  band size and time only — never Cash or income. Fan rate is tuned loosely relative to Cash so that
  income alone does not determine the album payout.
- **Capstone gig** — unlocks at the Records gate; grants a Roadie and fires a story beat (§10).
  **[rev]** Playing it implicitly cuts an album (§5) — the run's Fans bank as Records — before
  advancing, so no run value is stranded at the chapter boundary. **[rev]** The completion is one
  atomic `EconomyContext` operation ending at a single settle, and unlike the deliberately ungated
  release it is fail-closed: it refuses on an already-set completion flag, on an unmet unlock
  Condition (the operation asks the gate itself, TryBuy-style — a completion latches a permanent
  flag, so a UI bug must not finish a chapter early), and on any one-shot action that answers
  `CanExecute` false, all before the irreversible release. The completed capstone is then a fact
  source like any latch: the declared completion flag IS the latch, and projection re-applies the
  capstone's `OnComplete` state from it at every rebuild. The offer surface is an ordinary module
  (`module/capstone`) in a section gated coarsely (first Record) while the button's pressability is
  the capstone's own unlock — region coarse, action precise, the release's exact arrangement.

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

- **On start,** the event constructs a fresh economy context at a fixed baseline (§12 rule 12): its
  recipe projects the chapter's permanent-in-chapter facts only — earlier tiers' rewards apply, but
  no run facts carry in and no global derivation (Records) is registered — so the challenge runs at
  a fixed scale independent of the player's accumulated power, and the suspended run is never
  touched. Quitting or failing discards the context; there is nothing to unwind. This is what lets a
  debuff be meaningful — the player is working from a known floor rather than an arbitrary fortune.
  **[rev]**
- **Goal:** reach a target amount of a currency.
- **Debuff (optional):** the run is modified — generation halved, automation disabled, tap-only, a
  currency locked. Debuffs change how the loop is played, which is where an event's variety comes from.
- **Timer (optional):** adds a time limit. Timed events are the only events that can be failed.
  **[rev]** While a timed event is running, idle payouts (§9) are disabled; the timer pauses while
  the event is unfocused *(provisional — verify the pause against Ctrl C)*.
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
**[rev]** Isolation is achieved by construction, not exemption (§12 rule 12): a replay economy is
its own context with its own currency ids, projecting only the facts its recipe names — Roadie
allocation and its own replay-local facts. It does not inherit the chapter's permanent-in-chapter
facts (event-tier buffs earned at the frontier do not apply inside a replay); after the capstone,
those facts are archival.

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

**Idle earnings (per economy). [rev]** There is no app-level "offline": each economy context (§12
rule 12) tracks when it was last interacted with, and an unfocused economy accrues nothing live —
instead it pays `generatorProduction × min(idleSeconds, cap) × rate` at the moment it gains focus,
with **rate = 50%**, **cap = 4 hours** per economy (raisable via the Backstage Pass), and no payout
below a minimum idle threshold (a too-quick refocus earns nothing). Closing the app is just the
state where every economy is unfocused; launching is an ordinary focus-gain on the chapter you
return to — so in-game chapter switching (Ch. 2+) and time away are one mechanic, not two.
**Generator production only:** fans, rehearsal, and bar progress pause while unfocused — engagement
currencies never earn while the player is not engaging, and idle fan accrual would let time away
shortcut the Records payout (§11). **[rev]** With production configs (§12 rule 13) this boundary is
the holder, by construction: idle pays only configs held by generators. Module-held configs never
idle-pay — a tap-triggered config cannot fire while nobody taps, and Rehearsal's passive trickle
lives on the Jam module, not on a generator — so there is no per-config idle flag to author or get
wrong. The base rate is set at 50% so that the doubled value is a full
100%. Idle income is themed as streaming/radio royalties and is largest at the Radio chapter.

**[rev]** Doubling is a **timed buff**, not a per-collect choice: the "Double it" ad grants a fact
with an expiry — every idle payout collected while it is active is doubled — rather than doubling
one collect screen (doubling on every switch, as Ctrl C allows, over-serves frequent switchers).
Structurally it is the same shape as Encore: a timed multiplier fact that modifiers derive from
(§12 rule 11). The Backstage Pass is that fact permanently on.

| Player | Idle payout | How |
|---|---|---|
| Free, no action | 50% | Auto-collected on focus-gain |
| Free, watches ad | 100% (2×) while the buff lasts | "Double it" ad grants a timed double-idle buff |
| Backstage Pass owner | 100% (2×) always | The double-idle fact is permanently on |

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
    CurrencyManager.cs    // [rev] one class, one instance per pool: a startup pool (Records/Roadies) + one per economy context (run currencies)
    EconomyContext.cs     // [rev] rule 12: the per-economy bundle (currency pool + systems + modifiers + flags), built from a projection recipe; the album release is its ReleaseAlbum operation and the chapter gate its CompleteCapstone operation
    ContentDatabase.cs    // [rev] Addressables discovery of all definition SOs by label; id→def registries
    Condition.cs / ConditionEvaluator.cs   // [rev] one gate/unlock/visibility/availability type + one evaluator
    GameEffect.cs / GameAction.cs   // [rev] grants split by category: an effect is re-applicable state every rebuild re-runs; an action is a one-shot award only its player-action moment executes (a payout paid twice is inexpressible, not validated against)
    FlagSystem.cs         // [rev] single reveal registry; each flag's declared scope (run | permanent-in-chapter) decides what a release clears
  Loop/
    ChapterDefinition.cs / Chapter.cs   // mechanic, capstone, Records gate, story beat
    ChapterManager.cs     // forward-only advancement + unlocks
  Economy/
    GeneratorDefinition.cs / Generator.cs   // isBandmate is a data field the fan system reads   // [rev]
    UpgradeDefinition.cs / Upgrade.cs   // [rev] payload = buff | setFlag (reveal via flag); gate = any Condition; scope = run | permanent-in-chapter; one-shot awards are GameActions, executed by the purchase alone
    BarDefinition.cs / BarGroupDefinition.cs / BarSystem.cs   // [rev] generic fillable bars (fillCurrency-driven); replaces LearnSongBar
    RewardDefinition.cs / RewardManager.cs   // [rev] shared reward pool; Apply(rewardId) dispatches on type (incl. setFlag)
    CostCalculator.cs / ProductionCalculator.cs   // [rev] formula only; the modifiers that scale production live in the registry
    ProductionConfig.cs / ProductionSystem.cs   // [rev] rule 13: {currency, amount, trigger, gate} held by producers; the system fires module-held configs (tap + tick); replaces EngagementEarnSystem/TapSystem
    Modifiers/ModifierSystem.cs   // [rev] one registry for every stat modifier: granted (carries scope) + derived (computed from a source); the composition rule lives here
    CapstoneSystem.cs     // [rev] the completed-capstone fact source: the declared completion flag is the latch, projection re-applies OnComplete from it; the completion's own facts run only from CompleteCapstone
  Events/
    EventDefinition.cs / GameEvent.cs   // baseline reset, optional debuff, optional timer, goal, tier, reward
    EventManager.cs       // enter/quit/fail/succeed, tiers, sandboxed economy snapshot
  Meta/
    RoadieAllocation.cs   // [rev] Chapter 2: per-venue allocation, product boost, replay ramp; the Roadie pool is the global `roadies` currency, not a manager
  Content/
    SongDefinition.cs / Song.cs         // Catalog (run) + Discography (permanent)
  Save/
    SaveData.cs / SaveSystem.cs         // JSON + checksum
    IdleEarnings.cs       // [rev] per-economy idle accrual on focus-gain (time since that economy's last interaction), 50% base
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
6. **[rev]** Separate the run block and permanent block in the save schema. An album release clears
   the run block and writes the permanent block. Saves store **facts** (balances, purchase latches,
   completed bars, cleared tiers, clear counts) and never modifier grants; derived modifiers are
   never serialized. On load, each economy context re-projects its modifiers from the restored facts
   at construction (rule 12), so an effect can never disagree with the fact that produced it.
   **[rev]** Re-projection is the **only** way a modifier comes into existence, at every boundary and
   not just at load. An album release resets the facts it owns — balances, owned counts, run-scoped
   purchase latches, bar progress — and then re-runs the projection, which rebuilds the modifier store
   from whatever facts survived. It does **not** reach into the store to remove the run-scoped entries
   and leave the rest. A store that is rebuilt cannot hold a stale or double-counted effect, so rule
   11's "durability follows the fact" stops being an invariant something has to maintain and becomes
   the only thing the code can express. Two mechanisms for one modifier set — filter in place on
   release, rebuild from facts on load — would be written by different slices, exercised on different
   days, and able to disagree silently; that disagreement is exactly the compounding failure rule 11
   describes. The obligation re-projection takes on in exchange is **totality**: every fact class that
   produces a modifier must be walkable at construction, which is what the context's recipe (rule 12)
   declares.
7. **[rev]** Store each cleared chapter's replay economy as its own state block (local currency,
   generators, goal `k`, last-interaction timestamp), separate from frontier state — in code, its own
   currency pool and context instance (rule 12), not scope tags inside shared managers. The only
   cross-writes are Roadie allocation in and Roadie award out.
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
11. **[rev]** Compose every stat modifier through one registry — no system keeps its own multiplier or
    bonus stack. Each asks for the composition on its target and applies it, where a target is a closed
    kind (tap value, fan rate, a generator's output, a currency's production) plus, for the kinds that
    act on one thing, the designer id they name. The rule is `(base + adds) × multipliers`, expressed in
    exactly one place, so two systems cannot disagree about the order their modifiers apply in. A
    **granted** modifier is a fact established at a moment (a bought buff, a completed bar, a cleared
    event tier) and carries a `scope`; a **derived** modifier computes from a source on every read and
    carries no scope, because its lifetime is its source's and two answers to "does this survive a
    release" could disagree. Keep grants individually rather than accumulating them into one number:
    that is what makes a run reset exact, and it makes the reset a single call instead of a per-system
    enumeration that silently misses whichever system was added last. A modifier reaches only the target
    it names, which is what keeps an income buff off a fans or merch producer.
    **[rev]** The scope a grant carries is a working copy of its originating fact's declared scope, which
    is the single authoritative lifetime: **an effect's durability is exactly the durability of the fact
    it projects from.** Each fact declares that lifetime where the fact itself lives - the upgrade
    definition for a bought buff, the bar group for a completed bar, the event tier for a cleared tier -
    and never on the effect, nor on a reward definition, which is a reusable projection rather than a
    fact and so has no lifetime of its own to declare. A scope on a shared reward could disagree with the
    content applying it, and the disagreement is invisible: a run-scoped source granting a
    permanent-in-chapter effect re-grants it every run and compounds without limit. Anything that must
    survive a reset boundary is therefore derived from a fact that owns that lifetime (the Records total,
    the Roadie allocation, an entitlement, a clear count) — there is no global grant store and no grant
    ever migrates across a chapter boundary. Wanting a granted global effect is the smell that its
    underlying fact has not been named yet.
12. **[rev]** Bundle the per-economy systems (currency pool, generators, upgrades, bars, fans,
    production (rule 13), modifiers, flags, condition context) into one **economy context**, constructed
    from a recipe and instantiated per economy: one startup pool holds the global currencies, and each
    frontier chapter, event sandbox, and replay economy is its own context. A context's recipe declares
    which fact classes it projects into modifiers at construction:

    | Context | Projects |
    |---|---|
    | Frontier chapter | global facts + its chapter-permanent facts + run facts |
    | Event sandbox | chapter-permanent facts only |
    | Replay economy | Roadie allocation + replay-local facts |

    Currency ids are unique wherever two balances are genuinely different things — **[rev]** each
    chapter's run currencies (settled in §2) and a replay economy's local currency — so an id names
    one balance and resolution is a construction-time ownership check, never a runtime fall-through. Orchestration (album release, event entry, capstone)
    is written against the context, not against a global manager, so a second economy is an
    instantiation rather than a rewrite.
    **[rev]** A context has a lifecycle — constructed → focused ⇄ unfocused → discarded — with exactly
    one focused context at a time. Only the focused context is ticked; unfocused economies accrue
    nothing live and are paid idle earnings on focus-gain (§9), which is what makes double-counting
    impossible by construction. Each context's last-interaction timestamp lives in its state block
    (rule 7). The suspended run during an event is simply unfocused, and an app launch is an ordinary
    focus-gain.
13. **[rev]** Every flat-rate currency source is a **production config** — `{currency id, amount,
    trigger: tick | tap, gate: Condition}` — held by its producer, never by the currency: a currency
    is pure state (a balance, a group, formatting), and the dependency points from producer to
    currency, the same direction a multiplier points at its targets (§3). Two holder kinds exist
    today: a **generator** holds one tick-triggered config (its `produces` + `baseOutput`), scaled by
    owned count and wrapped in purchase mechanics; a **module** (the Jam button) holds its per-tap
    yields and Rehearsal's passive trickle. A config's gate is an ordinary rule-8 Condition — this
    replaces the bespoke reveal-flag string the old earn config carried. Idle eligibility (§9) is a
    property of the holder kind: generators are the only idle-eligible holder, so module-held
    production never idle-pays — by construction, not by flag; a producer whose output must idle-pay
    is a generator, full stop. The modifier vocabulary (rule 11) is unchanged by this: each holder
    composes exactly the targets it composes today (a generator's output; tap value is the Jam
    module's Cash config), so the pass moves where base values live, not how they scale. Which
    config takes a composition is itself a declaration on the config — the Jam Cash entry declares
    `composes: tapValue`, an undeclared config pays its raw amount — never an inference from a
    currency name or list position; `tapValue` is the only module-legal value today, and anything
    else is refused at import and reported at boot. Sources
    with formula-driven rates stay their own systems — the fan rate is a function of band size (§6),
    not a flat amount — and rewards/grants are rule-11 facts, not production.

**Starter prompt for a code assistant:**
> "In Unity (version X, iOS/Android), scaffold a nested-prestige idle core: a CurrencyManager with a run
> block (Cash, Fans, Rehearsal, gear, catalog) and a permanent block (Records, Roadies) using
> break_infinity.cs BigDouble; content discovered via Addressables; a single Condition type + evaluator
> for all gates/unlocks/visibility; one flag registry for all progressive reveal; one modifier registry
> every stat effect composes through, with no per-system multiplier stacks; an AlbumPrestige
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
  Addressables; currency definitions are global while balances live in per-context pools (the group
  declares placement); every flat-rate currency source is a production config held by its producer
  (generators and the Jam module — a currency never declares its own earn, and only generator-held
  production idle-pays); saves store facts, never grants, and each economy context re-projects its
  modifiers from those facts at construction.
- **Monetization:** opt-in ads only (no forced interstitials); idle earnings are per-economy (50% of
  generator production, paid on focus-gain) with a timed 2× double-idle buff from the "Double it" ad;
  Encore 2× / Overdrive 4×; Backstage Pass (lifetime); Buy Roadies (repeatable); Tip Jar; no
  subscriptions.
- **Engine:** Unity.
