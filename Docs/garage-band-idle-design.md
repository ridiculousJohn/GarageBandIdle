# Garage Band Idle — Design & Build Spec

An idle game about a band rising from a garage to arenas. Play progresses through eight chapters, each
a bigger venue with a new mechanic. All numbers below are starting values for tuning.

> **Doc state.** This spec has been through seven revision passes; earlier wordings live in this file's
> git history rather than being reproduced here. What those passes settled, stated as the current
> architecture rather than as changes:
> - **One `Condition` type** and one evaluator for every gate, unlock, visibility and activation rule
>   (§12 rule 8); **one flag registry** for all progressive reveal (rule 9); every content
>   ScriptableObject discovered via Addressables (rule 10).
> - **One modifier registry** (rule 11) — systems compose on read instead of holding their own stacks.
>   A modifier **selects what it modifies by id or tag** rather than naming a closed stat kind; modifiers
>   are **multipliers only** and a flat bonus is a contribution; and a grant lives in the same scope as
>   the fact it projects from.
> - **A chapter is a tree of scopes** (rule 12). A scope owns its truth, owns what presents that truth,
>   and holds an *ordered* list of child scopes, so a fact's lifetime is **where it lives** rather than a
>   value it declares. Nothing declares a lifetime: the `run` / `permanent-in-chapter` enum, the economy
>   context and its projection recipe, `CurrencyPlacement`, and the event sandbox were all replaced by
>   position in the tree.
> - **One producer per currency** (rule 13), owning a **rate** (per second) and a **yield** (per
>   firing), each composed from individually addressable contributions. Generators and modules
>   *contribute* rather than produce, a contributor may feed several currencies, and it may only feed
>   its own scope or further out. "Tap" is a UI gesture, and no type, member, enum value, field or
>   local below the module presenting one is named for it (rule 13).
> - **A reset names a set of scopes**, chosen by a polymorphic reset target selector (rule 14).
> - **Run currencies are per-chapter ids** and idle accrual is per scope, paid when a scope is enabled
>   (§2, §9) — there is no app-level "offline" and no single focused economy.
>
> No open questions remain.

---

## 1. Core loop

The game has two *kinds* of loop — a fast one inside a chapter, a slow one across chapters
— and the inner kind is a **ladder** rather than a single step. A chapter declares an ordered list of
**tier scopes** (§12 rule 12), each with its own reset, its own banked currency, and its own offer.
Chapter 1 declares one rung (the album); a later chapter may declare three, where pressing a deeper
rung resets the shallower ones with it. What follows describes the shape of *a* rung — the shallowest
is the one the player presses most.

**The album loop (inner).** Within the current chapter the player taps for Cash, buys gear and
bandmates, grows Fans, and then releases an album. Releasing an album resets the run — Cash, gear,
Fans, and the working Catalog — and awards **Records**. Each Record permanently increases global
income, so the next run is faster. The player repeats this loop several times within a chapter.
A chapter with more than one rung banks a *different* currency at each: the shallow rung's
payout is an intermediate currency the player spends inside the chapter, and only the deepest rung
banks Records. Which rung carries the income multiplier, which carries the advancement gate, and
which is purely archival is a per-chapter authoring decision, not a property of the ladder.

**The chapter loop (outer).** Cumulative Records unlock the current chapter's capstone gig.
Playing the capstone **implicitly cuts an album** — the run's Fans bank as Records as part
of the show — and then advances the player to the next chapter, whose economy opens fresh (run
currencies are per-chapter, §2/§3). Progress filed *outside* the chapter's tier scopes —
Records, and any flag or unlock living in the chapter scope or the root — is never reached by a rung's
reset: the climb is forward only. What a rung *does* clear is whatever is filed inside it, flags and
unlocks included (§2), which is what makes a second run re-walk the progression. After advancing,
pressing a rung resets back to the start of the *current* chapter, not the garage.

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

**Chapter anatomy.** A chapter is a **scope** (§12 rule 12) holding an ordered list of child
**tier scopes**. The chapter scope owns whatever the whole chapter shares — the currencies more than
one tier reads, the chapter-durable flags, the capstone offer — and each tier scope owns what its own
reset destroys: that tier's currencies, generators, upgrades, bars, and flags. So the old list (local
currencies, generators, an upgrade tree, opt-in events, a capstone gated by Records) is unchanged in
content and changed in *filing*: every item now sits in the scope whose reset it should not survive,
and that placement IS its lifetime.

The consequence worth authoring against: **anything two tiers share must live in their nearest common
ancestor.** Sibling scopes are not on each other's resolution chain, so a currency or modifier one
tier needs from another cannot stay filed in that other tier — it moves up. Moving it up is a pure
data edit, because ids are unique tree-wide and every reference resolves by id (§3).

**Settled:** each chapter's run currencies are distinct ids, and advancement starts the new
chapter's economy fresh (the Ctrl-C-compatible reading). To keep un-released value from being
stranded — and to remove the release-before-capstone ritual stranding would create — the capstone
implicitly cuts an album (§1, §6): the run's Fans convert to Records as part of the show, then the
next chapter opens fresh. The frontier is therefore just the current chapter's scope
instance, built from the same definition a replay economy instantiates (§12 rule 7), and §6's promise
that a chapter's Cash stays in the thousands–millions range is structural rather than tuned.

**Progressive reveal.** A chapter does not present all its mechanics at once. Content-unlock upgrades
(§4) introduce new generators, currencies, and mechanics as the player buys them, so the chapter opens
up in stages. Each such upgrade should introduce a change in play — a new mechanic, sub-loop, or
automation step — rather than only increasing a number, so that a chapter keeps changing as the player
works through it instead of settling into a single repeated action.

**Settled:** a section is visible exactly *while* its `visibleWhen` holds — evaluated live,
with no latch or lifetime of its own. Persistence is a property of STATE, never of UI: "stays once
earned" is authored by gating on a fact with that lifetime — a flag (whose *placement* carries the
lifetime, §12 rule 12), or a monotonic value like an earned total — and a threshold moment worth
remembering is latched by a passive content unlock setting a flag (Ch. 1's `browse_gear` at 250
Cash). Gating a region directly on a spendable balance is an authoring smell: it strobes with every
purchase. Distinct from visibility is an action's *pressability* (e.g. the release button, §5), a
live condition on the content the module presents.

**Settled:** visibility and *activation* are different axes, and both are Conditions (rule 8) —
there is still exactly one reveal mechanism (rule 9), evaluated at three levels:

```
module shows  =  scope.activeWhen  AND  section.visibleWhen  AND  module's own condition
```

A **scope's** `activeWhen` governs **simulation** — is this state live, is it ticking, is it
accruing. A **section's** `visibleWhen` governs **presentation** — should the player see this now.
They must be independently settable, because an active scope has to keep simulating while its display
is off-screen: an outer scope's generators produce the whole time the player is looking at an inner
tier. Collapsing the two would make "visible" imply "ticking" and break background production and
idle accrual outright. Containment supplies the conjunction, so a module never restates its scope's
or its section's condition, and a module's own condition is not even evaluated when its scope is
inactive.

This is also why sections are **not** scopes: a section has no truth of its own and runs on its
scope's context, and its condition answers a different question. Two structurally similar things
answering different questions are two things.

**Settled:** a flag's lifetime is **the scope it lives in** — not a value on its declaration,
and never on the `setFlag` effects that set it, so one flag cannot carry two lifetimes. A flag filed
in a tier scope clears when that tier resets, and everything gating on it (sections, bar groups,
production contributions, meters) goes dark together, re-arming when a setter in that same tier re-fires —
so a whole sub-system re-opens through ONE condition authored in ONE place. This is how the second
run re-walks the chapter's progression (band → fans → covers → gear) instead of opening with every
system already on screen: Ch. 1 files `fans`, `covers` and `gear` (and their setter unlocks) in its
one tier scope, while `album` sits in the chapter scope — the release button's *region* is knowledge,
its pressability an offer (§5).

Boot validation enforces the pairing, generalized to placement: **a flag needs at least one setter in
its own scope or inside it.** If every setter is more durable than the flag, the rebuild re-asserts
the flag in the same reset that cleared it, and the sub-system never goes dark. A flag no content sets
still warns. The rule is unchanged in substance from the two-level version — only "run-scoped" and
"permanent" became "which scope."

Once a chapter is cleared it remains available as a replay economy (§8.1).

---

## 3. Currencies

A currency's durability is **which scope holds its balance** (§12 rule 12). There is no
`run` / `permanent` flag and no placement enum: a currency filed in a tier scope resets when that tier
resets, one filed in the chapter scope survives every tier reset in that chapter, and one filed at the
root survives everything. The headings below ("Run-scoped", "Permanent") therefore name Chapter 1's
*filing*, not a property of the currencies themselves.

A currency *definition* is still global — one registry of everything assignable — and **ids are unique
across the whole tree**. Uniqueness is what makes durability re-tunable by data alone: to make a
currency survive one more reset level you move its declaration up one scope, and every producer, cost,
bar and condition referencing it resolves exactly as before. Shadowing is refused rather than resolved
(rule 12) — an id present in two scopes has two balances, and every read would silently pick whichever
the resolver reached first.

Resetting a scope clears the balances *it* holds; it never reaches a balance held further out. Scope
teardown and scope reset are different events (§5).

**Run-scoped:**
- **Cash** — earned by tapping and generators; spent on gear and upgrades.
- **Gear & bandmates** — generators bought with Cash, each **contributing** to one or more currencies'
  rates (§12 rule 13). A bandmate is simply a generator that contributes to fans' rate as well as
  cash's — two contributions on one generator, not a flag some system branches on.
- **Rehearsal (and later chapters' equivalent fill currencies)** — a run-scoped currency earned from
  engagement (a passive tick plus taps), spent to fill learn-songs bars. Rehearsal is Chapter 1's fill
  currency; a later chapter may define its own. It is an ordinary currency — pure state like any
  other; the Jam module contributes to its producer's yield and to its rate (§12 rule 13), and bars
  reference it by id.
- **Learn-songs bars** — generic *fillable bars* that pace a chapter (learn covers, rehearse). Each bar
  declares a `fillCurrency` (Rehearsal in Ch. 1), a fill requirement, and a reward granted on
  completion; its group's fill behavior declares the rate that currency is consumed at (§6), so a bar
  never completes faster than that rate allows however much fill currency is banked. The fill logic
  reads `fillCurrency` and is not covers-specific — bars are fed by a fill currency rather than being
  their own opaque mechanic. Separate from the Catalog (§7).
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
  is the same shape with a different currency id — no special case.
- **Effects.** An upgrade can grant a flat bonus, a multiplier, a new generator, a new currency, an
  automation step, a new sub-loop, or a new mechanic — all through the one `GameEffect`/`GameAction`
  family (§12), never a per-kind class.
- **Reveal.** A content-unlock upgrade reveals its content by **setting a flag** in the single flag
  registry (§12); the revealed content (a currency, a section, a bar group, a button) gates its own
  visibility on that flag. Rewards (§6.1) can set flags too. There is one reveal mechanism, not one per
  content type.
- **Scope.** An upgrade's lifetime is **the scope it lives in** — never implied by its type,
  and never a value it declares (the same rule as flags and currencies, §2/§3). *Buff upgrades* are
  filed in a tier scope: their purchase latch clears when that tier resets and they are re-bought each
  run, faster as the banked currency accumulates. *Content-unlock upgrades* (new generator, currency,
  or mechanic) are filed wherever their reveal needs to live: Ch. 1 files its reveal chain (the
  `fans`/`covers`/`gear` setters) in the tier scope so the second run re-walks the progression (§2),
  while an unlock filed in the chapter scope persists across every tier reset with only owned counts
  going.
- **Prestige-bought content.** Because placement is lifetime, a generator or upgrade priced in a
  banked prestige currency needs no new concept — it is filed one scope out from the tier that resets,
  so it survives the reset that pays for it. A generator's cost currency has always been independent of
  what it produces (§6), so "buy with the intermediate currency, produce the tier's currency" is a
  filing decision plus a cost id, not a feature.

---

## 5. The album (prestige)

Releasing an album is the run reset. Its name escalates thematically across chapters (demo, EP,
record).

Mechanically the release is **one tier scope's reset** — the shallowest rung of the chapter's
ladder (§1, §12 rules 12 and 14) — and everything below describes that rung rather than a unique
operation. The award is not a distinguished kind of thing: a payout is a **`GameAction`**, the same
one-shot award category every other payout already uses, differing only in that a formula computes its
amount. There is no payout *field*, so "a rung with no payout" is a rung whose action list is empty,
like any other content that declares nothing.

What the operation guarantees is **order**. The scope's **parent** orchestrates it (only the parent
knows the sibling order the selector may name); **every selected scope runs its own rung's actions
first**, while the state those formulas read still exists; then the parent clears the selected scopes
and re-runs projection (rule 6); then one settle at the root (rule 12). A granted currency resolves by
ordinary outward lookup, so a rung can bank into the chapter scope or straight to the root without its
immediate parent being the recipient.

Every selected scope, not just the pressed one — that is what makes "the capstone implicitly cuts an
album" (§6) literally true rather than a special case. The capstone selects the chapter's tier scopes,
so the album rung's own Fans-to-Records action runs because its scope was selected. Nothing reaches
across scopes to compute it, and nothing needs a second mechanism for the multi-rung press.

Two authoring rules keep an award coherent, both already enforced for the single-rung case and both now
stated per rung: an award's **inputs** must live in a scope the reset clears (otherwise the same value
banks on every press, without limit), and its **target** must live further out than the reset reaches
(otherwise the award is destroyed by the reset that produced it).

A third rule follows from resolution rather than from lifetime: **an award can only read its own scope
and outward** (rule 12), so a rung must be filed with the state its formulas read. This is why the
per-scope arrangement above is the only one that works — a single capstone action trying to compute the
Fans payout from the chapter scope could not see Fans at all, and the first two rules would not catch
it, because *clears* and *can-read* are different relations: the capstone selects the tier holding Fans
and still cannot read it. A formula needing two siblings' state is telling you those currencies belong
in their common ancestor (§2), since sibling scopes are never on each other's chain.

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

**Settled:** the release is *offered* only while the chapter's album unlock condition holds
(the same condition that first revealed it — e.g. Ch. 1's 50 Fans + 1 learned cover). Its inputs are
run values the release itself resets, so the offer disarms at every release and re-arms on the
re-climb — including re-learning a cover, since bars are run-scoped. The release *region* stays on
screen because the `album` flag it gates on lives in the **chapter** scope, outside the rung that
resets (§2/§3); only pressability tracks the condition. The release *operation* is deliberately ungated: the capstone implicitly cuts an
album (§2) whether or not the offer holds.

---

## 6. Within-a-chapter play & events

Moment-to-moment play draws on the systems defined elsewhere:
- **Tap ("Jam")** — early Cash source; its relevance falls off as gear automates income. The button is
  an authored module that names a currency producer and fires it; what a press pays is that producer's
  **yield** (§12 rule 13), which the module contributes to — Cash always, Rehearsal once revealed —
  alongside its contribution to Rehearsal's rate. The gesture is the module's; the economy knows only
  that a producer was fired.
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
  class does.

  **Continuous delivery carries a consumption rate** — the fill currency it can absorb per second —
  rather than transferring whatever the pool holds. Without one, a pool that accumulated while nothing
  was selected empties into a bar in a single tick, which is most of a bar's progress arriving in one
  frame and is the difference between rehearsing and collecting. The rate is an identified number a
  modifier can select (§12 rule 11) — by the bar's id, its group's, or a tag either one carries — so
  "rehearse twice as fast" is authorable content rather than a special case. The **rate is what takes screen
  time; the pool is what accrues** — which is why the fill currency may earn while a scope is disabled
  (§9) while the bar it feeds does not move until the player is back and has chosen where to pour.
  A dump-the-pool sibling is exactly the behavior that declines to have one.
- **Fans** — accrue passively once revealed. The fans producer's rate composes a base contribution
  plus one from each bandmate generator (§12 rule 13), so it is a function of band size and time only
  — never Cash or income, because nothing contributing to it reads either. It is tuned loosely
  relative to Cash so that income alone does not determine the album payout.
- **Capstone gig** — unlocks at the Records gate; grants a Roadie and fires a story beat (§10).
  Playing it implicitly cuts an album (§5) — the run's Fans bank as Records — before
  advancing, so no run value is stranded at the chapter boundary. The completion is one
  atomic scope operation ending at a single root settle (rule 12), and unlike the deliberately ungated
  release it is fail-closed: it refuses on an already-set completion flag, on an unmet unlock
  Condition (the operation asks the gate itself, TryBuy-style — a completion latches a permanent
  flag, so a UI bug must not finish a chapter early), and on any one-shot action that answers
  `CanExecute` false, all before the irreversible release. The completed capstone is then a fact
  source like any latch: the declared completion flag IS the latch, and projection re-applies the
  capstone's `OnComplete` state from it at every rebuild. The offer surface is an ordinary module
  (`module/capstone`) in a section gated coarsely (first Record) while the button's pressability is
  the capstone's own unlock — region coarse, action precise, the release's exact arrangement.
  In ladder terms the capstone is the chapter's **deepest rung** (§1, §5): it selects every
  tier scope in the chapter, so "it implicitly cuts an album" stops being a special case and becomes
  simply what clearing a deeper rung means. What stays particular to it is declared rather than
  hardcoded — the fail-closed operation gate (the release's is deliberately absent) and the completion
  latch it runs. The chapter advance is **not** part of the operation: no action may change the tree's
  shape or its enabled set, since actions run before the clear, the projection and the settle. The
  advance is a *reaction* to the settled completion flag, performed by `ChapterManager`. Being one-shot,
  an action could not carry it anyway — it would never replay on load, so the flag has to drive the
  outcome regardless, and driving it from the flag is the only form that survives a save.

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

**Settled:** an event is **not a scope**. It is a *component attached to* a scope — the tier it
challenges — and it needs no economy of its own, no sandbox, no seed recipe, and no projection filter.
That is what replaces the isolated-context design this section previously specified.

- **On start,** the event **resets its host scope**, and that reset behaves exactly like the
  rung's ordinary reset (§5) — its **award actions run first**. So entry banks the run rather than discarding
  it, and entering costs nothing but time. This is deliberate and it is why entry does not ask: the
  reset happens either way, so the starting state is identical whether the player was paid or not, and
  declining payment would be pure loss. A "bank it first" ritual is the same stranding §2 removed from
  the capstone, and an option whose right answer never changes is a trap rather than a choice. Having
  reset its host, the event then registers its handicap (below) and runs the tier normally, receiving
  ticks like anything else in that scope, and tearing itself down on success, failure, or quit.
- **Scale.** An event **scales with the player's accumulated power**, deliberately. Resetting the
  host scope zeroes that tier's own facts, but outer scopes stay on the resolution chain, so every
  banked multiplier still applies. A tier may therefore be *unbeatable* until the player has advanced
  further through the normal loop — "come back later" is the intended experience, and the returning
  player's own growth is what makes the tier winnable. This replaces the fixed-baseline model, which
  excluded the main power source and so left an event ladder that only its own clears could advance:
  under a fixed floor a tier was beatable now or never. The promise that matters is untouched — an event
  gated on the player's *power* still never gates *chapter advancement*.
- **Goal:** reach a target amount of a currency.
- **Debuff (optional):** the run is modified — generation halved, automation disabled, tap-only, a
  currency locked. Debuffs change how the loop is played, which is where an event's variety comes from.
  A debuff is an ordinary modifier (rule 11) registered in the host scope by the event
  component, so it is resolved by the same outward walk as everything else and disappears when the
  component tears down. A debuff is a power check rather than a constant-difficulty puzzle.
- **Timer (optional):** adds a time limit. A timed event is the only kind that can be *failed*
  outright; an untimed event at insufficient power is not failed so much as unfinishable, and the player
  quits. **Settled:** the timer **pauses** while its host scope is disabled. The attempt waits where the
  player left it, so closing the app is never a way to lose one — a deadline the player cannot attend to
  is not a challenge. The exchange is that a timed event earns nothing while away: its host scope accrues
  nothing and is paid **no** idle earnings for that time when it is enabled again (§9), rather than the
  ordinary generator payout. Both halves are specific to *timed* events. An untimed event has no deadline
  to protect and nothing to exchange for, so its host accrues and pays idle exactly as any other scope.
- **Failure:** a failed timed event **tears its component down** — the timer and the attempt end and the
  handicap modifiers are removed, so the tier reverts to ordinary play. It clears nothing in the host
  scope: the component holds no progress of its own, since the goal reads ordinary host currency, and
  whatever the run accumulated stays where it is to be banked by the next reset like any other run. A
  quit is the same teardown on the player's initiative. Failing or quitting therefore costs only the time
  spent and never permanent progress — which holds because entry already banked the run.
- **Reward on success:** a lateral bonus — a chapter-durable buff, a Roadie, a Catalog song, or local
  currency, drawn from the shared reward pool (§12). Event rewards never include Records or any currency
  that gates advancement, so an event is never a hard prerequisite; its reward size (above) is what sets
  how much it matters. The event's own reset must not bank a *reward* — the entry emit pays the
  ordinary rung payout and nothing more, so a rerun tier cannot be farmed for advancement currency.
- **Tiers:** an event can repeat at higher tiers with a higher starting requirement, a stronger debuff,
  and a larger reward. The rising requirement across tiers is a natural throttle, which makes tiered
  events a repeatable source of Roadies. With scaling, the throttle is the player's power
  curve rather than the requirement alone, which is what ties the event ladder to main progression.

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
generators, and completion goal: a **second instance** of that chapter's scope definition (§12 rule 7),
placed in the tree like anything else. Nothing about a replay is special-cased. It resolves outward
exactly as the first playthrough did and scales with every global modifier it reaches — Records income,
Roadies, Encore — which is the same walk the first playthrough already ran, back when those totals
happened to be zero. What does *not* cross between the two instances is anything a scope owns: local
balances, generators, bars, cleared-tier facts. That falls out of each instance holding its own scopes
rather than out of any filter, and it is why event-tier buffs earned at the frontier do not appear in a
replay; after the capstone those facts are archival. The two instances are never enabled at once
(rule 7), which is what replaces the old exactly-one-focused guarantee.

What keeps an early chapter from being cleared instantly late is the **goal**, not a ceiling on the
player's power. One consequence to author for: a replay's completion goal is `replayGoal(k)` below,
which makes it **instance** data — a replay does not read the chapter definition's capstone gate, since
`recordsCumulative` would be satisfied the moment the chapter became replayable.

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

**Idle earnings (per scope).** There is no app-level "offline": each **scope** (§12 rule 12)
tracks when it was last interacted with, and a **disabled** scope accrues nothing live — instead it
pays `rate × min(idleSeconds, cap) × idleRate` for each of its currencies at the moment it is
**enabled**, where `rate` is that currency's composed production rate (rule 13), with
**idleRate = 50%**, **cap = 4 hours** per scope (raisable via the Backstage Pass), and no payout below
a minimum idle threshold (a too-quick re-enable earns nothing). Both `idleRate` and `cap` are identified
numbers composed from the registry rather than constants — the Backstage Pass contributes to one and the
"Double it" buff below multiplies the other. A number the game modifies that nothing can name is a gap
in rule 11 rather than a feature request, and closing it means giving that number an id. Closing the app is just the state where
every scope is disabled; launching enables the scopes you return to — so in-game chapter switching
(Ch. 2+) and time away are one mechanic, not two. Note this is per *scope*, not per economy: several
scopes are enabled at once (rule 7), and an outer scope's generators keep producing live while the
player works inside a tier, so only the scopes actually disabled accrue idle time.
**What accrues is settled by structure, not by a list.** Every currency's **rate** accrues, including
Fans and the fill currency: progress while away is what an idle game is, and a chapter whose
progression currency alone stood still would pay the returning player in a number they cannot advance
on. A **yield** never accrues, because nothing fires a producer while the player is gone. And **bar
progress never accrues** — filling is a tick-driven consumption of the fill currency (§6), not
production, so a disabled scope's bars do not move for the same reason its taps do not. There is no
idle flag on a currency or a contribution, and no list of exempt currencies: an earlier draft of this
section carried one, and it was three Chapter 1 nouns standing in for a rule.

The result is the intended shape rather than a compromise: **time away fills the pool, presence spends
it.** A player returns to banked Rehearsal, chooses a bar, and watches it fill at the group's
consumption rate (§6) — so the act of rehearsing still costs screen time, while the resource it costs
accumulated in their absence. The old worry, that idle Fans would let time away shortcut the Records
payout (§11), is a balance question with balance answers: `idleRate` and `cap` are exactly the levers,
and both are now composable. The base rate is set at 50% so that the doubled value is a full 100%.
Idle income is themed as streaming/radio royalties and is largest at the Radio chapter.

Doubling is a **timed buff**, not a per-collect choice: the "Double it" ad grants a fact
with an expiry — every idle payout collected while it is active is doubled — rather than doubling
one collect screen (doubling on every switch, as Ctrl C allows, over-serves frequent switchers).
Structurally it is the same shape as Encore: a timed multiplier fact that modifiers derive from
(§12 rule 11). The Backstage Pass is that fact permanently on.

| Player | Idle payout | How |
|---|---|---|
| Free, no action | 50% | Auto-collected when the scope is enabled |
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
not direct references or hardcoded lists (see architecture rule 10).

```
Assets/Scripts/
  Core/
    GameManager.cs        // bootstrap, save/load + tick orchestration
    TickSystem.cs         // fixed-interval update on real (DateTime) time
    BigNumber.cs          // wraps break_infinity.cs
    Definition.cs         // rule 10: the base every content definition inherits - id + tags, declared once instead of per class, so registries/validation/the inspector work on any family and every family gets grouping (rule 11)
    CurrencyManager.cs    // one class, one instance per scope that owns balances
    Scopes/ScopeDefinition.cs / Scope.cs   // rule 12: definition + instance (rule 7). A scope owns its truth (pool + systems + modifiers + flags), its sections, and an ORDERED list of child scopes; lifetime is placement, so this replaces EconomyContext, EconomyRecipe and CurrencyPlacement
    Scopes/ScopeChain.cs  // rule 12: the one iterator over "my scope outward to the root, enabled only" - three public resolvers (ResolveCurrency first-owner-wins / ResolveFlag any / ResolveModifiers accumulate) fold it their own way; no mode parameter
    Scopes/ResetTargetSelector.cs   // rule 14: polymorphic (self-and-contained | preceding-siblings | named), resolved by the scope owning the order, output closed downward
    PrestigeTierDefinition.cs   // rule 12/14: one rung - offer Condition, optional fail-closed operation gate, onComplete effect (requires a latch to project from), the one-shot GameActions the press runs (a payout is one of these, not a field of its own), an optional completionLatch slot holding ONE flag-setting GameAction (not a flag-id string: the slot keeps one declaration and the setter sweep finds it through the family's own Validate), reset target selector
    PayoutFormula.cs      // polymorphic like Condition; the amount a computed-grant GameAction awards - Ch1's floor((fans/5)^0.5) is one instance
    ContentDatabase.cs    // Addressables discovery of all definition SOs by label; id→def registries
    Condition.cs / ConditionEvaluator.cs   // one gate/unlock/visibility/availability type + one evaluator
    GameEffect.cs / GameAction.cs   // grants split by category: an effect is re-applicable state every rebuild re-runs; an action is a one-shot award only its player-action moment executes (a payout paid twice is inexpressible, not validated against). Setting a flag exists in BOTH families and the choice is not stylistic: an effect where the flag re-derives from a more primitive saved fact (an upgrade's reveal flag, from its latch), an action where the flag IS the fact (a rung's completion, which OnComplete projects from)
    FlagSystem.cs         // single reveal registry, one instance per scope; a flag's lifetime is the scope holding it
  Loop/
    ChapterDefinition.cs / Chapter.cs   // mechanic, capstone, Records gate, story beat
    ChapterManager.cs     // forward-only advancement + unlocks
  Economy/
    GeneratorDefinition.cs / Generator.cs   // a purchasable contributor: owned count x its rate contributions, one per currency it feeds (rule 13), each with its own id and tags so a buff can name one line - a bandmate is simply one that also contributes to fans, and `bandmate` is a tag (rule 10) rather than a bool a system branches on
    UpgradeDefinition.cs / Upgrade.cs   // payload = buff | setFlag (reveal via flag); gate = any Condition; NO scope field - lifetime is the scope it is filed in; one-shot awards are GameActions, executed by the purchase alone
    BarDefinition.cs / BarGroupDefinition.cs / BarSystem.cs   // generic fillable bars (fillCurrency-driven); replaces LearnSongBar
    RewardDefinition.cs / RewardManager.cs   // shared reward pool; Apply(rewardId) dispatches on type (incl. setFlag)
    CostCalculator.cs / ProductionCalculator.cs   // formula only; the modifiers that scale production live in the registry
    CurrencyProducer.cs / ProductionSystem.cs   // rule 13: one producer per currency owning rate + yield, each composed from gated contributions that stay individually addressable; the system holds a scope's producers, integrates rates over elapsed time, and fires a producer on request - it never learns what fired it
    ProductionContribution.cs / IProductionContributor.cs   // one line: the currency it feeds, which of that currency's two numbers (`feeds`), its own id and tags, and a gate. No trigger and no idle flag - what fires a producer is external, and whether a line accrues over an absence follows from the quantity (rule 13)
    ProducerDefinition.cs / AuthoredContributor.cs   // a bundle of flat lines, scaled by nothing: the Jam button's yields and the band's passive fan rate. Generators and applied upgrades contribute through the same interface, which is what lets a producer be ASSEMBLED without knowing contributor kinds
    Modifiers/ModifierSystem.cs   // one registry per scope: granted (lives where its fact lives, no scope value) + derived (computed from a source); the composition rule lives here, resolution is the ScopeChain walk
    Modifiers/ModifierSelector.cs   // rule 11: what a modifier names - ids and tags, empty = everything in reach. Matches(subject) is asked of the thing being matched, so the registry never compares strings and a later path form (drummer.cash) changes only what parses it. Replaces ModifierTarget + ModifierTargetKey: nothing names a closed stat kind, because every modifiable number has an id
    PrestigeSystem.cs     // one scope's rungs as a fact source: each declared completion latch IS its rung's fact, projection re-applies onComplete from it; the capstone is the chapter's deepest rung (rule 14)
  Events/
    EventDefinition.cs / GameEvent.cs   // optional debuff, optional timer, goal, tier, reward; NO baseline-reset field - entry resets the host scope
    EventComponent.cs     // rule 12 / section 6.1: an event is a COMPONENT on a scope, never a scope. Start = reset the host (whose award actions run first, so entry is free) + register handicap modifiers + start timers; ticks with the host; tears down on success/fail/quit, clearing nothing in the host. Replaces EventManager's sandboxed snapshot
  Meta/
    RoadieAllocation.cs   // Chapter 2: per-venue allocation, product boost, replay ramp; the Roadie pool is the global `roadies` currency, not a manager
  Content/
    SongDefinition.cs / Song.cs         // Catalog (run) + Discography (permanent)
  Save/
    SaveData.cs / SaveSystem.cs         // JSON + checksum; one block per SCOPE INSTANCE, nested as the scopes are, with stable instance identity (rule 6)
    IdleEarnings.cs       // per-SCOPE idle accrual paid when a scope is enabled (time since that scope's last interaction), 50% base
  Monetization/
    AdManager.cs          // rewarded only (Encore top-up + offline Double it)
    IAPManager.cs         // Backstage Pass (non-consumable) + Roadie bundles (consumable) + Tip Jar
  UI/
    ChapterScreenUI.cs  StoryBeatUI.cs  CollectScreenUI.cs
    SectionView.cs  ModuleRegistry.cs   // data-driven layout: sections + module→prefab (Addressables). A section belongs to a scope and is NOT a scope; a module shows when scope.activeWhen AND section.visibleWhen AND its own condition all hold (section 2)
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
   gear curve can be generated by an editor script.
6. The save is **one block per scope instance**, nested the way the scopes are (rule 12) —
   which replaces the flat run-block/permanent-block split, since "run" and "permanent" are no longer
   two categories but positions in a tree of arbitrary depth. This makes **stable scope-instance
   identity** a save requirement: a tier that has been reset and rebuilt must be recognizable as the
   same scope, and a replay instance must be distinguishable from the frontier's instance of the same
   scope definition (rule 7). Saves store **facts** (balances, purchase latches, completed bars,
   cleared tiers, clear counts) and never modifier grants; derived modifiers are never serialized. On
   load, each scope re-projects its modifiers from the restored facts at construction, so an effect can
   never disagree with the fact that produced it.
   Re-projection is the **only** way a modifier comes into existence, at every boundary and
   not just at load. A reset clears the facts held by the scopes it selected — balances, owned counts,
   purchase latches, bar progress, flags — and then re-runs the projection, which rebuilds the modifier
   store from whatever facts survived. It does **not** reach into the store to remove entries and leave
   the rest. A store that is rebuilt cannot hold a stale or double-counted effect, so rule 11's
   "durability follows the fact" stops being an invariant something has to maintain and becomes the only
   thing the code can express. Two mechanisms for one modifier set — filter in place on reset, rebuild
   from facts on load — would be written by different slices, exercised on different days, and able to
   disagree silently; that disagreement is exactly the compounding failure rule 11 describes. The
   obligation re-projection takes on in exchange is **totality**: every fact class that produces a
   modifier must be walkable at construction. The scope tree discharges that structurally — a scope
   holds the systems whose facts it owns, so a system that exists is a system that gets projected, and
   there is no second list to keep in step.
7. A cleared chapter's replay economy is **another instance of that chapter's scope
   definition** (local currency, generators, goal `k`, last-interaction timestamp), separate from the
   frontier's instance — which is what makes scopes need a **definition/instance split**, the same one
   `ChapterDefinition`/`Chapter` and `GeneratorDefinition`/`Generator` already have. Separation is then
   construction rather than exemption and needs no scope tags inside shared managers: each instance owns
   its own scopes, so an instance-local fact cannot reach the other one, while everything global stays
   reachable by the ordinary outward walk. A replay is **not** isolated from the player's accumulated
   power and is not meant to be (§8.1) — it runs the same resolution the first playthrough runs, and its
   throttle is its goal ramp.
   There is no single "focused" economy. Scopes are **enabled or disabled**, plural: several
   are enabled at once (an outer scope keeps producing while the player works inside a tier), only
   enabled scopes are ticked, and a disabled scope accrues nothing live and is paid idle earnings when
   re-enabled (§9). Since exactly-one-focused is what previously made double-counting impossible by
   construction, that guarantee needs its own statement here: **two instances of the same scope
   definition are never enabled at once** — a replay instance and the frontier's instance of one
   chapter are mutually exclusive. Each scope's last-interaction timestamp lives in its own save block.
8. Express every gate/unlock/visibility/availability rule as a single `Condition` type
   evaluated by one `ConditionEvaluator` — no per-currency or per-rule branches. Condition types:
   `currency`, `currencyEarnedTotal`, `ownedCount`, `flagSet`, `barsCompleted`, `recordsCumulative`,
   `compound` (all/any).
9. Drive all progressive reveal through one flag registry: a content-unlock upgrade (or a
   reward) sets a flag; revealed content gates its visibility on a `flagSet` Condition. No parallel
   reveal paths (no separate "unlockSystem").
10. Discover all content ScriptableObjects (chapters, currencies, currency groups, generators,
    upgrades, events, bars, rewards) via Addressables labels; managers build their id→definition
    registries from the labelled assets, not from hardcoded lists or direct references. Validate that
    every referenced id resolves on load.
    **A definition is one type.** Every content definition carries an **id** and a **tag list**,
    declared once on a shared base rather than re-declared per class — which is what lets a registry, a
    validator and an inspector dropdown work on any family without reflecting for a property named `Id`
    or being handed a per-type accessor for it. Tags are open content on that base, so every family gets
    grouping at once (rule 11) instead of each one growing its own bool when someone needs a set.
11. Compose every stat modifier through one registry — no system keeps its own multiplier or
    bonus stack. Each asks for the composition on the number it owns and applies it.

    **A modifier names what it modifies by id**, the way everything else in the game names things.
    There is no closed list of modifiable stats. Every modifiable number is an *identified thing*: a
    production contribution, a producer's **rate** or **yield** (rule 13), a generator's **cost**, a bar
    group's **fill rate** (§6), a scope's **idle rate** and **idle cap** (§9). Giving the game a new
    modifiable number means giving that number an id, not adding a member to an enum every reader then
    has to learn. The shape this replaces was a closed *kind* plus one designer id, and it broke the
    first time one generator fed two currencies: "double the drummer's output" had no way to say
    *which* output, because the kind named a family, the id named a member of that family, and the
    number itself was never named at all.

    **A term is a NAME, never a facet.** A modifier carries a **selector**: a list of terms, each one
    the id of a thing or the name of a tag. It is not a filter expression over a number's properties.
    `cash_rate` is the id of cash's rate; `["cash","rate"]` is not a way to say the same thing, and
    treating a term as a property test is the mistake this rule exists to forbid — it makes one array
    mean two incompatible things, and it makes a currency-level buff match both an aggregate and the
    contributions summed into it, applying once per line and again over their sum.

    **Everything with an id is selectable**, at every level: a contribution, a producer's rate or yield,
    a generator, a bar group. A contribution carries its own id precisely so a buff can name one line of
    a generator that holds several; the generator's own id still reaches all of them, through the owner
    the subject offers. An **empty selector reaches everything in reach**, which is what makes "double
    all generator output" or "-99% cost for this tier" placement rather than an authored id list.

    Matching is asked of the thing being matched rather than computed inside the registry: one
    implementation, which the composition and the change notification both ask, so a display can never
    refresh on a modifier the composition ignored or miss one it counted. Keeping it there is also what
    makes a later term form — a path like `drummer.cash` — a change to what parses a term and to nothing
    else.

    **Tags are how a set gets a name** (rule 10). Any definition may carry tags, and so may a
    contribution — `rhythm_section` on two generators' cash lines reaches both without either buff or
    generator listing the other. A set spelled out as ids has to be re-spelled at every buff that means
    it, and silently omits whatever is added later; a tag is declared once, by the member. A boolean
    beside a definition (`isBandmate`) is a tag that never got the concept.

    **Modifiers are multipliers.** A flat bonus is *not* a modifier — it is a **contribution** to the
    number it raises (rule 13), authored by whatever fact pays it. Every composed number in the game then
    has one shape, **the sum of its contributions × the product of the multipliers matching it**, and
    the old `(base + adds) × multipliers` ordering stops being a rule anything can disagree about: an add
    and a base are the same kind of thing. It also removes a question that has no correct answer — what a
    flat add against a *set* means, +1 to the total or +1 to each — by making it unsayable rather than
    documented. Cost previously needed a "multipliers only" exemption for a related reason (a flat
    reduction has no floor, and a cost at or below zero is a free generator); that exemption is now
    simply the rule everywhere.

    A **granted** modifier is a fact established at a moment (a bought buff, a completed bar, a cleared
    event tier) and is *stored*, in the scope holding that fact; a **derived** modifier is not stored at
    all — it computes from its source on every read, so it has no placement of its own and cannot give a
    second answer to "does this survive a release." Keep grants individually rather than accumulating them into one number:
    that is what makes a run reset exact, and it makes the reset a single call instead of a per-system
    enumeration that silently misses whichever system was added last. A modifier reaches only what its
    selector matches, which is what keeps an income buff off a fans or merch producer.
    A grant lives in the **same scope as the fact it projects from**, which is the single
    authoritative lifetime: **an effect's durability is exactly the durability of the fact it projects
    from.** That is now structural rather than copied — a grant does not carry a scope value that could
    drift from its source, it simply sits where its source sits, and the reset that clears the fact
    clears the grant with it. No lifetime is declared on the effect, nor on a reward definition, which is
    a reusable projection rather than a fact and so has no lifetime of its own: a scope on a shared
    reward could disagree with the content applying it, and the disagreement is invisible — a
    short-lived source granting a longer-lived effect re-grants it every run and compounds without limit.
    Anything that must survive a reset is therefore derived from a fact that lives further out (the
    Records total, the Roadie allocation, an entitlement, a clear count). Wanting a granted global effect
    is the smell that its underlying fact has not been named yet.
    Modifier *resolution* is the outward walk of rule 12: a target composes every contribution
    found from its own scope to the root, so a buff in an outer scope reaches inward without anything
    registering it inward. This is also the only channel by which an outer scope influences an inner one
    (rule 13).
12. **The scope tree.** A **scope** is the unit of economy, lifetime, and presentation at once. It
    owns: its **truth** — currency balances, modifiers, flags, and the systems (generators, upgrades,
    bars, production, conditions) whose facts it holds; what **presents** that truth — its sections and
    their modules (§2); and an **ordered list of child scopes**.

    This one concept replaces the economy context, its projection recipe, currency placement, and the
    `run` / `permanent-in-chapter` lifetime enum. A fact's lifetime is **where it lives** — the scope
    that resets it. Nothing declares a lifetime, so no declaration can disagree with the reset that acts
    on it, the same way a payout paid twice is inexpressible rather than validated against.

    ```
    root  (Records, Roadies, entitlements - nothing resets these)
      +-- chapter scope  (what the whole chapter shares; the capstone offer)
            +-- tier scope 1   (the shallowest rung - reset most often)
            +-- tier scope 2
            +-- ...            (ordered; the ladder of section 1)
    ```

    **Resolution walks outward.** A generator asking for its modifiers, a cost asking for a balance, a
    Condition asking for a flag — each starts at its own scope and walks toward the root, the direction
    of increasing durability. There are three resolutions and they are **three public functions**, not
    one with a mode parameter, because what each does at a link genuinely differs:

    | Resolving | At each link | Result |
    |---|---|---|
    | a currency | does this scope own the id? | first owner wins — one balance |
    | a flag | is it set here? | any link satisfies |
    | modifiers | collect whatever targets me | every link contributes |

    "Accumulate a currency" is not a concept, so a shared mode vocabulary would be a union of things that
    never apply to each other. One internal helper performs the iteration — which links, in what order,
    which are enabled — so **what is in scope** has exactly one answer, and the three functions consume
    it and fold it their own way.

    **Sibling scopes are not on each other's chain.** Anything two scopes share therefore lives in their
    nearest common ancestor (§2), and moving a declaration outward is how a fact becomes more durable.
    **Ids are unique tree-wide** and shadowing is refused rather than resolved: an id in two scopes has
    two balances, and every read would silently pick whichever the resolver reached first. Uniqueness is
    also what makes the move a pure data edit.

    **Invalidation runs the other way.** Reads go outward; change notifications go inward, because an
    inner module gating on a root currency must re-evaluate when that currency moves. A scope therefore
    subscribes to its ancestors' change signals, which makes disposal discipline load-bearing rather than
    tidy — a discarded scope still listening keeps a dead economy's subscribers alive and feeds them
    changes for something nobody is playing.

    Orchestration (a rung's reset, event entry, the capstone) is written against the scope, so a second
    economy is an instantiation rather than a rewrite (rule 7).

    **The settle boundary.** The boundary is the **root of the tree**, always. Every top-level operation
    ends at one root settle, so condition-dependent values re-evaluate exactly once after the whole
    mutation — the same invariant a single context has today, and fixed rather than discovered. The
    tempting alternative, that the outermost scope *touched* owns the settle, does not survive the
    operations that need it: which scope is outermost is learned during the mutation, not before it (a
    rung's reset emits its payout outward halfway through), while the deferral has to be open before the
    first fact moves. Opening it at the root and narrowing afterward is the root boundary with extra
    steps.

    What is scoped is the *work*, not the boundary. Each scope carries its own dirty flag, raised by the
    same condition inputs as today — its chain aggregates its ancestors' change signals, so an emit
    outward dirties the emitter and everything inward that can read the recipient. The root's settle
    drains the scopes whose flag is set, **outermost first**, because reads go outward and an inner
    evaluation must see final outer state; then it re-composes the currency producers of every enabled
    scope, unconditionally, since a granted modifier moves a rate or a yield without any condition
    input firing. The drain repeats while any scope is dirty, under the same bound as the
    single-context restore — but the exhaustion diagnostic must name **which** scopes are still pending,
    or it reports only that something somewhere re-triggers itself.

    The settled signal stays **per scope**, raised for the scopes that actually drained. A root-owned
    boundary is a statement about when a mutation is finished, not about who needs telling; one tree-wide
    signal would have every module in every enabled scope re-ask on any change anywhere.

    Two consequences worth stating because they are easy to miss. **Enabling a scope must dirty it** —
    the world moved while it was disabled and nothing raised its flag, the same reason a restore marks
    dirty explicitly rather than trusting the fresh-instance default. And **suppression is root-owned but
    must reach every scope**: the depth counter can live in one place, but if it gates only the root's
    invalidation, a restore's republish re-dirties descendants after the settle already consumed them and
    "which signal is terminal" has two answers again. That is the one piece of the deferral machinery
    that genuinely composes across scopes instead of nesting inside one.
13. **One producer per currency.** A currency is produced by exactly one thing — its **producer** —
    and that producer owns two numbers: a **rate**, in units per second, and a **yield**, in units per
    firing. Both are compositions of individually addressable **contributions**, and nothing else in
    the game creates currency. The currency itself stays pure state (a balance, a group, formatting)
    and does not hold its producer, so the dependency still points from producer to currency, the same
    direction a multiplier points at its targets (§3). "What creates cash" therefore has one answer —
    ask cash's producer — rather than being a scan across every holder that might name it.

    **Rate and yield are different quantities, not two flavours of one.** A rate is per unit *time* and
    accrues whether or not anyone is present. A yield is per *occurrence* and does not exist until
    something fires the producer — asking a yield for its rate would be asking how fast the player
    presses, which is not a fact about the economy. They are modified separately, presented separately
    ("+12/sec" against "+5 per press"), and only a rate can earn offline. Any attempt to unify them
    through a per-firing magnitude is a unit error: it multiplies a quantum by seconds and silently
    couples two numbers that must be authored independently.

    **Everything else contributes; it does not produce.** A **generator** contributes to a rate, scaled
    by owned count and wrapped in purchase mechanics. A **module** contributes a yield (the Jam
    button's cash) or a rate (Rehearsal's trickle). Neither creates currency of its own — both move one
    producer's numbers, which is why several generators can raise a single currency's rate without any
    of them owning it. One contributor may feed **more than one** producer: a bandmate raises cash's
    rate and fans' rate, two contributions on one generator. A contributor restricted to a single
    output cannot express that, and forces a boolean beside it that some system has to branch on.
    Each contribution carries its own
    gate, an ordinary rule-8 Condition, checked per composition; a contribution's durability is its
    contributor's, so it declares no lifetime of its own.

    **Firing is external and unnamed.** Something fires a producer and it pays its yield. The producer
    never records what fired it — a button, an automation, a story beat and a test are
    indistinguishable below this line, and **"tap" is a UI gesture that exists only in the module
    presenting one**. A buff reading "taps pay double" is a multiplier on that currency's yield and
    applies however the yield is fired. The economy holding a concept named after a gesture is the
    error this rule exists to forbid.

    **Idle needs no eligibility concept** (§9). A rate accrues while a scope is disabled and a yield
    does not, because nothing fires a producer in the player's absence. That falls out of the rate/yield
    split rather than being authored, so there is no flag on a contribution, no field on a currency, and
    no exempt list.

    **A contribution is an identified thing** (rule 11): it carries an id and tags of its own, so a buff
    can name one line — the drummer's cash — without touching the other line the same generator holds,
    and a tag can name a set of lines across generators. It needs no separate declaration of which
    composition scales it, since the number it feeds is what the contribution *is*. A **flat bonus a
    reward or an upgrade pays is itself a contribution**, authored by that fact rather than by a
    generator; only multipliers are modifiers.
    **Production direction.** A contributor may only feed a producer in **its own scope or further
    out** — never inward. An outer generator feeding an inner scope's currency would outlive its own
    target, and after the inner scope resets it would be raising a rate on a balance that no longer has
    an owner. The reverse is legal and load-bearing: an inner scope contributing to an outer currency is
    how a tier feeds the chapter continuously, alongside the rung payout it emits on reset (§5). So
    influence flows **inward only as modifiers** resolved on the outward walk (rule 11), and value flows
    **outward only as production and payouts**. A producer lives in the scope of the currency it
    produces, which is what makes the check static: resolve the contributor's scope and the target
    currency's scope at import and refuse a strictly-inner target.
14. **Reset selection.** A reset names a **set of scopes**, chosen by a polymorphic **reset target
    selector** — the same shape as `Condition`, `BarFillBehavior` and `GameEffect`, where the JSON
    vocabulary maps onto a concrete class at import so a mode can never be authored without code behind
    it. Members today:

    | Selector | Selects |
    |---|---|
    | self and contained | this scope plus every scope inside it |
    | preceding siblings | this scope plus the siblings *before* it in the parent's ordered child list |
    | named | an explicit list of scope ids |

    Two rules constrain any selector's output. **The set closes downward:** selecting a scope selects
    everything inside it, because an inner scope may legitimately use an outer currency, and clearing the
    outer while leaving the inner strands a cost or gate pointing at a balance with no owner. And **the
    selector is resolved by the scope that owns the ordering** — a tier asking for "preceding siblings"
    asks its parent, the only thing that knows the order. That is what keeps every read outward-only: no
    scope enumerates its own siblings.

    Order lives in the parent's **ordered child list** and nowhere else; a second ordering list would be a
    second home for one fact. Because that order is semantic, reordering the list is a game-logic change
    rather than a layout change, which the data should say out loud. Transient attachments (an event's
    host relationship, §6.1) carry no ordinal at all, so a selector can never sweep one up.

    The selector belongs to the **scope instance** (rule 7), never to the module that presents it: a
    module is a prefab that can be placed more than once, so a target list living on it would be two
    sources of truth for one scope's lifetime. The module reads whatever instantiated it.

**Starter prompt for a code assistant:**
> "In Unity (version X, iOS/Android), scaffold a nested-prestige idle core built on a **scope tree**: a
> scope owns its currency balances, modifiers, flags and systems, owns the sections that present them,
> and holds an ordered list of child scopes — so a fact's lifetime is where it lives and no lifetime enum
> exists anywhere; ids are unique tree-wide; balances use break_infinity.cs BigDouble. Resolution is one
> chain iterator walking a scope outward to the root, behind three functions — ResolveCurrency (first
> owner wins), ResolveFlag (any link), ResolveModifiers (accumulate) — never one function with a mode
> parameter. Content discovered via Addressables; a single Condition type + evaluator for every
> gate/unlock/visibility/activation rule; one flag registry per scope for all progressive reveal; one
> modifier registry per scope that every stat effect composes through, with no per-system multiplier
> stacks. Prestige is one parameterized operation rather than an album method: a rung declares a reset
> target selector and a list of one-shot GameActions, the actions run before the clear so their formulas
> can read what the reset is about to destroy, the clear is followed by re-projection from the surviving
> facts, and the whole operation ends at one root settle. A ChapterManager with forward-only advancement
> gated by cumulative Records; a TickSystem on DateTime deltas; and a checksummed JSON SaveSystem storing
> one block per scope instance under a stable identity, with idle earnings accrued per scope and paid
> when that scope is enabled, 50% base capped at 4h. Event-driven, no per-frame polling."

---

## Appendix — at a glance

- **Structure:** nested prestige on a **scope tree** — a chapter scope holding an ordered ladder of
  tier scopes, inside an outer, forward-only chapter climb (8 chapters). A fact's lifetime is the scope
  it lives in; resolution walks outward; a reset selects a downward-closed set of scopes.
- **Records:** the single permanent progression currency; each Record raises global income, and
  cumulative Records gate chapter advancement.
- **Per-chapter systems:** an upgrade tree (upgrades gated on any currency) plus opt-in events. Chapters
  reveal their mechanics progressively through content-unlock upgrades.
- **Album (prestige):** resets the run, awards Records from run performance (Fans, and catalog quality
  from Ch. 6); repeated several times per chapter.
- **Events:** a *component* on a scope, not a scope of its own. Entry resets the host scope and
  banks its payout, so entering costs nothing but time; optional debuff and/or timer; only timed events
  can fail; rewards are lateral (never Records); tiered. Events deliberately **scale with** the player's
  accumulated power, so a tier can be unbeatable until they advance further — "come back later" is the
  intent, and no event ever gates chapter advancement.
- **Catalog (Ch. 6+):** quality-driven global multiplier that converts to Records on album release;
  Discography keeps a persistent list of best songs.
- **Roadies:** permanent multiplier — additive within a venue (+5%/roadie), multiplicative across
  venues. Earned from capstones and from replaying cleared chapters as second scope instances; buyable; earned and bought
  Roadies are identical; no purchase cap.
- **Data model:** gates/unlocks/visibility/activation are a single `Condition` type (one
  evaluator); all progressive reveal runs through one flag registry (`setFlag` → `flagSet`); learn-songs
  bars are generic fillables driven by a `fillCurrency` (Rehearsal in Ch. 1); every content
  ScriptableObject is discovered via Addressables; **lifetime is placement** — a currency, flag, upgrade,
  bar or generator lives in the scope that resets it, ids are unique tree-wide, and there is no lifetime
  enum, no placement enum and no projection recipe; **each currency has exactly one producer** owning a
  rate and a yield, both composed from contributions that generators and modules declare (a currency
  never declares its own earn, a contributor may feed several currencies but only its own scope or
  further out, and "tap" is a UI gesture no economy identifier is named for); **every modifiable number has an id**
  and a modifier selects by id or tag with empty meaning everything in reach, so there is no closed list
  of stats — modifiers are multipliers and a flat bonus is a contribution; saves store facts, never
  grants, one block per scope instance, and each scope re-projects its modifiers from those facts at
  construction.
- **Monetization:** opt-in ads only (no forced interstitials); idle earnings are per scope (50% of
  generator production, paid when that scope is enabled) with a timed 2× double-idle buff from the
  "Double it" ad; Encore 2× / Overdrive 4×; Backstage Pass (lifetime); Buy Roadies (repeatable); Tip
  Jar; no subscriptions.
- **Engine:** Unity.
