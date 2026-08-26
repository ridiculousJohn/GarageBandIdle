# Chapter 1 — The Garage: Authored Content

Companion to `garage-band-idle-design.md`. Every shape here is a §12 shape; this file adds only
data. Numbers are tuning values — expected to churn — and this file is their single home. The old
`chapter-01-garage.json` seeded names and curves; deltas from it are listed at the end.

---

## 1. Pacing targets

| Target | Value | Verified by |
|---|---|---|
| First demo (fresh install) | ~5–6 min (352s @ 2 taps/s, 293s @ 3 taps/s) | Walkthrough 1 |
| Demo cycles to capstone | 7–10 | Walkthrough 1 |
| First chapter clear | ~35–50 min active | Walkthrough 1 |
| Replay clear | ~20–25 min → 1 Roadie | Walkthrough 3 |
| Primary pacing knob | the capstone gate (30) — raise/lower before touching curves | §11 |

Tap-rate assumption throughout: 2–3 presses/sec sustained. Tune against both an unbuffed player and
a permanent-Overdrive player (§11).

## 2. Scope tree & root declarations

```
root
└─ ch1            (chapter — "The Garage")
   └─ tier1      (the run — everything a demo release destroys)
```

**Root content** (declared once, game-wide; listed here because Ch. 1 is the first consumer):

- Currencies: `records`, `roadies` (both accumulate, never spent in Ch. 1), `discography` (list, Ch. 6+).
- Permanent modifiers (§12.5/§12.6 — root-declared, root-applied via `permanentModifiers`;
  formula-shaped effects that exist from minute one):
  - `records_income`: `{target: income, stat: rate, × (1 + 0.02 × records.balance)}` plus the same
    entry at `stat: yield` — additive within the term, and "rate and yield alike" is one entry per
    stat (§12.2).
  - `roadie_total`: `{target: income, stat: rate, × Π over chapters (1 + 0.05 × stationed there)}`
    (§8.2) — `perRoadie` lives on the formula.
  - `roadie_active`: `{target: production, currencyId: income, stat: rate,
    × (1 + 0.05 × stationed at the chapter on the resolution chain)}` — the active chapter's
    double-count (§8.2), aimed at the SOURCES because only a source knows which chapter it produces
    in. Every producer and generator declares the `production` tag; the `currencyId: income`
    narrowing is what keeps a bandmate's Fans line out of it.
- Idle bases: fraction 0.5 authored as a root modifier `{stat: rate, ×0.5}` applying only during
  idle accumulation (`appliesWhen`, §12.5); cap 14400s (4h) and minimum-away threshold 180s are
  `GameConfig` values. `game_speed` base 1; Encore buff `{stat: game_speed, ×2}`, Overdrive `×4` (§9).
- Flags: `ch1_complete`, `story_ch1_open_seen`, `story_ch1_end_seen`.

## 3. Currencies

| Id | Declared in | Tags | Notes |
|---|---|---|---|
| `cash` | tier1 | `income` | The income tag is what the Records and Roadie modifiers target (§12.2). |
| `fans` | tier1 | — | **Never income-tagged, never roadie-buffable** — the farm throttle (§8.2). |
| `rehearsal` | tier1 | — | Fill pool for the cover bars. |
| `ch1_records` | ch1 | — | Capstone gate counter; fed by the release payout, zeroed by the capstone's own reset (§5). |

Flags: tier1 declares `fans_revealed`, `rehearsal_revealed`; ch1 declares `album`, `gj1_done`,
`gj2_done`, `gj3_done`. (The design doc's §2 speaks loosely of "`fans` and `covers`" flags — authored
as `fans_revealed` / `rehearsal_revealed` because a flag id may not collide with the `fans` currency
(§12.12), and §12.2's `tap_producer` snippet already names `rehearsal_revealed`.)

## 4. Producers (all tier1)

```
tap_producer ("Jam")  [production]:                      # fired by the Jam button module
  { cash,      yield, 1 }
  { cash,      yield, 1,   UpgradePurchased(stage_presence) }   # the upgrade is a latch; the flat
                                                                # bonus is this conditioned entry (§12.2)
  { rehearsal, yield, 1,   FlagSet(rehearsal_revealed) }
  { rehearsal, rate,  0.5, FlagSet(rehearsal_revealed) }        # no pre-banking: rate gated too

band ("Local Buzz")  [production]:                       # nothing presents it; pure rate
  { fans, rate, 0.35, FlagSet(fans_revealed) }           # base fan accrual — band-size adds below
```

## 5. Generators (all tier1; cost currency `cash`, growth 1.15; tags shown)

| Id | Tags | Base cost | Produces (rate) | availableWhen |
|---|---|---|---|---|
| `practice_amp` | `gear`, `production` | 60 | cash 0.5 | `EarnedTotalAtLeast(cash, 100)` |
| `drummer` | `gear`, `bandmate`, `production` | 250 | cash 3, fans 0.02 | `OwnedCountAtLeast(practice_amp, 3)` |
| `bassist` | `gear`, `bandmate`, `production` | 4,000 | cash 20, fans 0.02 | `OwnedCountAtLeast(drummer, 5)` |
| `guitarist` | `gear`, `bandmate`, `production` | 30,000 | cash 130, fans 0.02 | `OwnedCountAtLeast(bassist, 5)` |

The `gear` tag exists for the event handicap (×0 = "generators paused"); `bandmate` is the §3 set;
`production` is what the roadie active boost targets (§2), carried by every source so nothing is
silently left out of it - the `currencyId: income` narrowing is what decides which lines it lifts.
Band size drives the fan rate because each bandmate's fans entry scales with `ownedCount` — no
per-bandmate constant anywhere.

## 6. Upgrades (all tier1 — the reveal chain is re-walked every run, §2)

**Buffs** (latch clears at each release; re-bought faster as multipliers bank):

| Id | Cost | Gate | Carries |
|---|---|---|---|
| `stage_presence` | 250 | `EarnedTotalAtLeast(cash, 250)` | nothing — pure latch; tap_producer's conditioned entry reads it |
| `amp_strings` | 500 | `EarnedTotalAtLeast(cash, 500)` | effect `{target: practice_amp, stat: rate, ×2}` |
| `kit_upgrade` | 5,000 | `EarnedTotalAtLeast(cash, 5000)` | effect `{target: drummer, currencyId: cash, stat: rate, ×2}` — fans line untouched |
| `tight_set` | 20,000 | `CurrencyAtLeast(fans, 30)` | effect `{target: cash, stat: rate, ×1.5}` — currency-total, declared at cash's home ✓ |

**Content unlocks** (each sets a flag; revealed content gates on it):

| Id | Cost | Gate | Action |
|---|---|---|---|
| `play_for_crowd` | 100 | `OwnedCountAtLeast(drummer, 1)` | `SetFlag(fans_revealed)` |
| `unlock_covers` | 200 | `CurrencyAtLeast(fans, 25)` | `SetFlag(rehearsal_revealed)` |
| `cut_demo` | 0 | `All[CurrencyAtLeast(fans, 50), BarsCompleted(learn_covers, 1)]` | `SetFlag(album)` — flag at **ch1**, so the release region persists across runs; the row's module hides on `Not(FlagSet(album))` |

The gear *region* has no unlock and no flag: it gates directly on `EarnedTotalAtLeast(cash, 250)` (§2).

## 7. Bars (tier1)

Group `learn_covers`: `{maxActive: 1}` — choosing the next cover is the mechanic (§12.7). The group
carries nothing else; each cover names Rehearsal as its own fill currency.

| Bar | fillCurrency | fillAmount | fillRate | onComplete |
|---|---|---|---|---|
| `cover_1` "Three-Chord Anthem" | rehearsal | 100 | 2/s | `AddModifier(tier1, cover_bonus_1)` |
| `cover_2` "Parking-Lot Standard" | rehearsal | 300 | 2/s | `AddModifier(tier1, cover_bonus_2)` |
| `cover_3` "The Crowd-Pleaser" | rehearsal | 600 | 2/s | `AddModifier(tier1, cover_bonus_3)` |

Non-repeating; 1,000 Rehearsal finishes all three. Completion is a moment that leaves no derivable
effect-fact for a non-repeating bar, so the fan-rate reward is an `AddModifier` grant — cleared
with tier1 like every run fact.

## 8. Modifiers (all `stacking: Replace`)

| Id | Effects |
|---|---|
| `cover_bonus_1` | `{target: fans, stat: rate, ×1.15}` |
| `cover_bonus_2` | `{target: fans, stat: rate, ×1.15}` |
| `cover_bonus_3` | `{target: fans, stat: rate, ×1.20}` |
| `gj_tap_1` | `{target: tap_producer, currencyId: cash, stat: yield, ×1.25}` |
| `gj_tap_2` | `{target: tap_producer, currencyId: cash, stat: yield, ×1.5}` |
| `gj_tap_3` | `{target: tap_producer, currencyId: cash, stat: yield, ×2}` |

Cover bonuses are three distinct ids, so all three stack multiplicatively (×1.587 total). The
`gj_tap` chain swaps explicitly (`RemoveModifier` + `AddModifier`, §6.1) — one live at a time.

## 9. Rungs

```
tier1.release ("Cut a Demo"):
  offerCondition: All[ CurrencyAtLeast(fans, 50),             # uiText "50 fans"
                       BarsCompleted(learn_covers, 1),        # uiText "Learn a cover"
                       Not(EventRewardPending(tier1)) ]       # uiText "Claim your Garage Jam reward first"
  rungActions:    [ AddCurrency([records, ch1_records], floor((fans/5)^0.5)),   # one evaluation, both targets
                    ResetScope(tier1) ]

ch1.capstone ("Play the Backyard Party"):
  offerCondition: All[ CurrencyAtLeast(ch1_records, 30),      # same gate, first clear and every replay
                       Not(EventRewardPending(tier1)) ]       # uiText "Claim your Garage Jam reward first"
  rungActions:    [ ExecuteRung(tier1),                   # cut the album if its own gate holds
                    AddCurrency(roadies, 1),              # Ch. 1's reward formula: the constant 1
                    SetFlag(ch1_complete),                # root
                    ResetScope(ch1) ]
```

Payout examples: 50 fans → 3, 125 → 5, 500 → 10, 2000 → 20. The concave curve rewards frequent
releases over hoarding (2× the fans in one press pays ~1.41×, not 2×).

The `EventRewardPending` legs are the §12.12 stranded-reward guard: no reset can destroy an armed,
unclaimed Garage Jam reward, and the disarmed button lists the reason per §12.11 - the player
dismisses with one tap, taking the reward, and presses again.

## 10. Events — the Garage Jam chain (host: tier1)

Three separate `EventDefinition`s (levels are separate events, §6.1). Shared shape: `handicaps:
[{target: gear, stat: rate, ×0}]` (tap only — generator cash *and* fans lines pause; `band`'s base trickle
continues), `onEntry: [RestartScope(tier1)]` - a gate-met run banks exactly as a
release would, an unfinished one is discarded.

| Event | availableWhen | Goal | Timer | rewards |
|---|---|---|---|---|
| `garage_jam_1` | `CurrencyAtLeast(records, 1)` | `CurrencyAtLeast(cash, 150)` | 60s | `[AddModifier(ch1, gj_tap_1), SetFlag(gj1_done)]` |
| `garage_jam_2` | `All[FlagSet(gj1_done), CurrencyAtLeast(records, 15)]` | `CurrencyAtLeast(cash, 300)` | 90s | `[RemoveModifier(ch1, gj_tap_1), AddModifier(ch1, gj_tap_2), SetFlag(gj2_done)]` |
| `garage_jam_3` | `All[FlagSet(gj2_done), CurrencyAtLeast(records, 30)]` | `CurrencyAtLeast(cash, 600)` | 90s | `[RemoveModifier(ch1, gj_tap_2), AddModifier(ch1, gj_tap_3), SetFlag(gj3_done)]` |

All three share `onEnd: [ResetScope(tier1)]`, which runs whether or not the goal was reached -
dismissal pays the bonus if it was, then clears the sprint either way, and the next run starts fresh
with whatever bonus was earned. Nothing pre-event is at stake: entry already banked the run through
the release's own gate before wiping tier1, so a failed attempt only loses what the attempt itself
built.
`gj*_done` flags and the reward modifiers live at **ch1** — they survive tier resets, die at the
capstone (§12.12's set-then-wiped check holds: nothing in these lists resets ch1). Both rungs
guard with `Not(EventRewardPending(tier1))` (§9), so no reset can destroy an armed, unclaimed
reward. Goals scale with banked Records (tap yield rides the `income` multiplier); feasibility
math in Walkthrough 2.

## 11. Triggers & story

**Chapter 1 authors zero triggers.** Its only threshold moments are pure reveals (direct monotonic
gates) or purchase moments (upgrade payloads); the Trigger family exists for later chapters.

Story (root latches, §10): the opening card shows while `Not(FlagSet(story_ch1_open_seen))`; the
capstone beat while `All[FlagSet(ch1_complete), Not(FlagSet(story_ch1_end_seen))]`; each
`AcknowledgeStory` sets its latch.

> *Open:* "It starts in the garage. Just you, a beat-up amp, and a handful of songs you half-know.
> Time to make some noise."
> *Capstone:* "The backyard's packed. You plug in, count off, and for three minutes the whole
> neighborhood is yours. Someone's already asking when the next one is — and a guy named Dave
> offers to haul your gear. You've got your first roadie."

## 12. UI sections (on `ChapterDefinition`, §12.11; scopeId = evaluation scope)

| Section | visibleWhen | scopeId | Modules |
|---|---|---|---|
| `garage_floor` | always | tier1 | currency header, Jam button (`FireProducer(tap_producer)`) |
| `the_band` | `EarnedTotalAtLeast(cash, 100)` | tier1 | generator list |
| `the_gear` | `EarnedTotalAtLeast(cash, 250)` | tier1 | upgrade list |
| `rehearsal_space` | `FlagSet(rehearsal_revealed)` | tier1 | bar list + Rehearsal readout |
| `the_release` | `FlagSet(album)` | ch1 | release rung button (+ "would bank: N" preview via the same formula) |
| `garage_jam` | `CurrencyAtLeast(records, 1)` | tier1 | event module (start/dismiss) |
| `backyard_party` | `FlagSet(album)` | ch1 | capstone rung button + `ch1_records`/30 readout |

Every gate above is a flag or a monotonic fact — nothing strobes with spending (§2).

---

## 13. Walkthroughs

### 13.1 Normal release (fresh install, 2 taps/s)

| t | What happens | Why |
|---|---|---|
| 0s | Tap for cash at 1/press | only `garage_floor` visible |
| ~50s | 100 cash earned → `the_band` appears, amps buyable | `EarnedTotalAtLeast(cash, 100)` |
| ~125s | 250 earned → `the_gear` appears | direct threshold gate, no flag |
| ~160s | buy `stage_presence` → taps now 2/press | conditioned entry on tap_producer |
| ~205s | 3 amps owned → drummer available → bought at 250 | `OwnedCountAtLeast(practice_amp, 3)` |
| ~217s | buy `play_for_crowd` → fans accrue at 0.35 + 0.02/s | `SetFlag(fans_revealed)` — nothing pre-banked |
| ~300s | 25 fans → buy `unlock_covers` → Rehearsal live (0.5/s + 1/press) | `SetFlag(rehearsal_revealed)` |
| ~350s | `cover_1` fills (100 rehearsal at its own 2/s rate) → fan rate ×1.15 | `AddModifier(tier1, cover_bonus_1)` |
| ~352s | 50 fans + 1 cover → **release**: `floor((50/5)^0.5)` = **3** → records 3, ch1_records 3; tier1 resets | one formula evaluation, two targets |

At 3 taps/s the same trace lands at ~293s. Second run re-walks band → fans → covers ~30% faster
(income ×1.06 from 3 records; reveals re-bought).

**To the capstone:** releasing at roughly 50 / 65 / 85 / 110 / 145 / 190 / 250 fans pays
3+3+4+4+5+6+7 = **32 ≥ 30 after 7 cycles** (pushing less each run means 8–10). Fan wall-clock —
untouched by every income multiplier — keeps each cycle ≥ ~2.5 min, so the first clear runs
~35–50 min. Both targets in §1 hold.

### 13.2 Event entry (mid-chapter, records = 17, mid-run: 60 fans, 1 cover done)

1. `StartEvent(garage_jam_1)` — `availableWhen` holds (17 ≥ 1), host empty ✓.
2. `onEntry` runs `RestartScope(tier1)`, which checks the **release's own gate** — 60 ≥ 50 fans, 1 cover ✓
   — so the run banks exactly as a manual release: `floor((60/5)^0.5)` = 3 → records 20,
   ch1_records +3, and tier1 clears. Had the gate been unmet, the rung would have no-opped and the
   clear would still have happened - the unfinished run *discarded*, never banked.
3. The `ActiveEvent` record is created in the fresh state: `{garage_jam_1, 60, false}`.
   Handicap `{target: gear, stat: rate, ×0}` now zeroes every generator line by derivation — tap only.
4. Tap yield = 1 × (1 + 0.02×20) = **1.4/press** (no stage_presence — the reset cleared it). Goal
   150 cash ⇒ ~107 presses in 60s ≈ **1.8 taps/s** — a real but fair sprint. (At records = 1, the
   earliest possible attempt, it's 2.4 taps/s — intentionally brutal; "come back later" is the
   experience, §6.1.)
5. Goal reached at t≈55s: the sweep latches `goalReached` — spending below 150 afterwards
   un-secures nothing.
6. `DismissEvent(garage_jam_1)`: the record is removed first, then `rewards` runs because
   `goalReached` was set - `AddModifier(ch1, gj_tap_1)` (+25% tap for the rest of the chapter) and
   `SetFlag(gj1_done)` - then `onEnd` runs `ResetScope(tier1)`. A fresh run starts with the bonus
   live. Delaying the dismissal is safe: the release disarms on its `EventRewardPending` leg
   ("Claim your Garage Jam reward first") until the record is gone, and removing the record first is
   what lets that guard reopen.
7. If the timer had expired first: the record persists - the `gear` handicap keeps applying, the
   host stays occupied and `StartEvent` refuses - until the player dismisses it or a reset reaches
   tier1. Dismissal then runs `onEnd` alone: no bonus, tier1 wiped, and nothing lost that the
   attempt did not itself create.

### 13.3 Replay clear

Capstone at end of cycle 7 (ch1_records 32, live run at 70 fans with a cover done):
`ExecuteRung(tier1)` banks the run (+3 → records 35), `AddCurrency(roadies, 1)`,
`SetFlag(ch1_complete)`, `ResetScope(ch1)` — ch1_records, the `album` flag, gj rewards and flags
all zero; the chapter sits freshly reset, immediately replayable (§8.1).

Replay with `SetRoadieAllocation({ch1: 1})`: income multiplier = (1 + 0.02×35) × 1.05 × 1.05
(records × roadie_total × roadie_active) ≈ **1.87×**. Cash milestones compress ~2× (first demo in
~2.5–3 min); the fan floor doesn't move (fans carry no roadie-reachable tag); cycles settle at
~2.5–3 min ⇒ **~20–25 min to the same 30-record gate** → Roadie #2. Faster, not harder — and the
concave spread math (§8.2) says the *next* Roadie is better stationed elsewhere once other
chapters exist.

### 13.4 4-hour idle claim

State when the player switches away: 10 amps, 5 drummers, 2 bassists; covers 1–2 done; both reveal
flags set; records = 20; no roadies. `lastActiveUtc` stamps on switch-away.

Current rates at switch-in, 4h later:
- cash: (10×0.5 + 5×3 + 2×20) × 1.40 = **84/s** (records multiplier applies — income tag)
- fans: (0.35 + 7×0.02) × 1.15 × 1.15 = **0.648/s** (no income multiplier — by design)
- rehearsal: **0.5/s** (the rate entry; tap yields pay nothing while away — nothing fires)

Claim = rate × min(14400, 14400) × 0.5 → **cash 604,800; fans 4,666; rehearsal 3,600** stored as
the pending claim; the idle dialog offers Double It. The ad callback marks the claim `doubled`
(1,209,600 / 9,332 / 7,200); dismissal deposits exactly once. Bar progress moved zero — the pool
banked instead, so the returning player insta-pours covers at their 2/s rate. Had a timed Garage Jam
been running, the claim would be zero (§9).

**Tuning observation, no action needed:** a doubled 4h fan claim (9,332 fans) releases for
`floor((9332/5)^0.5)` = 43 records — the whole capstone gate in one press. This is *not* the strong
path: the concave payout makes cycling ~10× more record-efficient per fan (100 active cycles in
the same 4h ≈ 300 records), so idle is the convenience path at ~10% efficiency, exactly the §5
intent. It does mean Ch. 1 can be cleared in one evening of check-ins — acceptable for the
tutorial chapter; the gate is the knob if not.

---

## 14. Deltas from `chapter-01-garage.json` (and why)

- Flags `fans`/`covers`/`gear` → `fans_revealed` / `rehearsal_revealed` / *deleted*: id-collision
  rule (§12.12), §12.2's authored snippet, and the pass-7 no-flag ruling for the gear region.
- The `learn_covers` content unlock renamed `unlock_covers`: the same id-collision rule. The JSON
  gave that one name to BOTH the unlock and the bar group and told them apart by kind
  (`"setBy": "upgrade:learn_covers"` against `"group": "learn_covers"`), but ids are unique per chain
  across ALL kinds - an Effect target is a string, so one word cannot address two assets. The GROUP
  keeps the name, since it is what `BarsCompleted` reads and what the player-facing "Learn Covers"
  matches; the unlock takes the verb-first form `play_for_crowd` and `cut_demo` already use.
- `browse_gear` upgrade deleted — the region gates directly on the earned total (§2).
- Garage Jam's internal tiers 1–3 → three `EventDefinition`s gated on completion flags (§6.1);
  `baselineReset` → `onEntry: [RestartScope]`; goals retuned 500/2500/10000 →
  150/300/600 (the old goals needed 8+ taps/s; new ones sit at 1.8–2.8 taps/s against the intended
  Records multipliers), timers 60/60/45 → 60/90/90.
- Retunes for the ~300s first-demo target: amp rate 0.4 → 0.5, drummer 500 → 250 with unlock at
  3 amps (was 5), base fan rate 0.2 → 0.35, `cover_1` 120 → 100.
- Upgrade gates moved from spendable-balance to `EarnedTotalAtLeast` (persistent rows never strobe, §2).
- `cut_demo` is tier-declared with its `album` flag chapter-declared; `stage_presence`'s +1 became
  a conditioned `produces` entry on `tap_producer` (§12.2) instead of an upgrade-owned contribution.
- The separate `practice` producer folded into `tap_producer`'s gated rate entry (§12.2 authors it
  there); effect narrowing by `(currencyId, stat)` now protects what separate producer ids used to.
