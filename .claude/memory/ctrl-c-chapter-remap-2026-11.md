---
name: ctrl-c-chapter-remap-2026-11
description: "The Ctrl C to Garage Band chapter map John approved on 2026-11-10, and the three picks he ruled on; not yet written into the docs"
metadata:
  node_type: memory
  type: project
  originSessionId: 9bcd0e53-5626-49e8-9aa2-6f02854d91fd
  modified: 2026-11-10T22:46:58.364Z
---

On 2026-11-10 John approved a remap of Ctrl C chapters 3-8 onto Garage Band chapters 3-8 as the
basis for the future chapters. The map and the seven runtime changes it needs are recorded in
`Docs/build-plan.md` under "After the plan" ("Runtime work to do before chapters 3-8 are
implemented"), on his order that day. Design sections 2 and 7, the backlog and the chapter 3 draft
JSON were not touched and still show the earlier sketch.

**The map (approved)**
- 3 Local Venues <- Ctrl C 3: genres as divisions, rooms as the consuming bar, one scene prestige
  per genre with the softcap, capstone gate on all three creds.
- 4 Local Tour <- Ctrl C 4: tours as funding rounds, the take as durable spendable currency, the Van
  as the hardware that survives rounds, merch as the surplus conversion, the full circuit as the
  manual capstone.
- 5 Studio <- Ctrl C 6: ideas into tracks through one session bar, co-writers as the bonus-copy
  tier, two parallel prestiges, capstone gate on both.
- 6 Coast Tour <- Ctrl C 5: road wear as debt, shows / promoters / agents as the recursive ladder,
  leg / region / coast-to-coast as three nested resets, sponsorships suppress wear.
- 7 Radio <- Ctrl C 7: three debts, the back catalog as the clean tier, one prestige.
- 8 Arenas <- Ctrl C 8: box office as Liquidate, Records as the deep-reset currency, the branch
  pair, a rival counter forcing a restart.

**John's rulings**
- The 5/6 swap is approved ("Approve the swap").
- Debt moves off the Van chapter: it first appears in chapter 6 as road wear. This reverses
  decision 7 of `chapter-primitives-plan.md` (2026-09-15). ("yes those are fine")
- Section 7's Catalog: the Studio's economy is the ideas-to-tracks bar, the quality roll is
  dropped, song naming and the Discography stay display only; no catalog boost crosses chapters.
  ("yes those are fine")

**Debt, as John settled it on 2026-11-10 (design only, not built, not in the docs)**
- Debt is ONE shrinking multiplier formula, not a subtractive term. This replaces decision 11 of
  `chapter-primitives-plan.md`. ("ok that's fine")
- The formula reads a list of debt currencies and combines them by sum or by maximum; each show
  authors its coefficient as one over its own base yield; the factor floors at zero.
- "Only the worst debt counts" is two modifiers, one in sum mode and one in maximum mode, switched
  by the upgrade through `appliesWhen`. ("yes, the first: two effects, switched by an upgrade. that
  fits the model elsewhere")
- Sponsorships: I recommended an optional currency on the debt formula that divides the standing
  debt, over a separate reciprocal formula on the debt currency's rate. John has not ruled on which.

**Chapter 3's scene prestige, as John settled it on 2026-11-10 ("ok, remember that decision")**
- A scene prestige wipes the shared Cash as well as that scene's own songs, rooms, material and
  following, as Ctrl C's division prestige wipes the common currency. The other two scenes'
  machinery is the head start. Cash surviving was my engine-fit proposal and John rejected it: a
  prestige that keeps the wealth is not a trade.
- Each scene is its own tier holding its rooms (a room drinks one genre). Cash sits in a shared tier.
- The engine piece it needs (not built): a condition comparing one currency against another, so the
  shared tier's trigger resets itself when a chapter counter the scene rungs add to is ahead of its
  own copy, then copies the counter back in. This is design 12.5's "child writes a fact outward, the
  owner resets itself", and design 12.4 lists a state-computed threshold the code lacks. One rung
  per scope stays; reset reach does not widen.

**Chapter 4's tours while away, as John settled it on 2026-11-10 ("ok, that's fine")**
- Follows Ctrl C's Work From Home: away, the game repeats the tour last run at the time the player
  actually completed it, for a reduced take; the record belongs to the tour last run (a plain
  overwrite, so switching tours resets it). A fixed authored cycle was rejected as not tied to what
  the player can do.
- Exists today: live auto-complete by trigger, a rate line conditioned on `IdleAccumulation`, the
  fraction and its doublings as multipliers, a per-tour clock as a currency fed 1/s in the tier.
- Not built: an action that stores a value when the tour completes, and a formula turning the
  stored value into the away rate. Field shapes not worked out. The idle claim does not change.

**The code-change list as of 2026-11-10 (none built)**
- Agreed: (1) the debt formula, (2) the currency-against-currency condition, (3) the recorded tour
  pace (a store-value action plus a formula reading it).
- Agreed as plain needs ("5 is just 'we need that new formula'... 6 is just 'yes, we need that'. 7
  is 'yes, we need that'"): (5) a logarithmic bonus formula, (6) an upgrade-raised exponent on the
  Records payout, (7) song naming and the Discography (an action writing a song, a naming prompt, a
  Discography screen, and the save carrying songs).
- (4) is a new stat, `payout`, consumed by the payout action: a computed payout passes through the
  multipliers that name `payout` and no others, so nothing authored today changes. John, 2026-11-10:
  "none of your 'decisions' were really decisions other than needing a new 'payout' stat".
- Sponsorships are a field on the debt formula dividing the debt that bites, following the
  reference's "debt factor is min(1, 1/Serendipity)". Mine to state, not a pick.
- Nothing on this list is open.
- John's point: a change with no choice inside it is a need, not a pick. Do not list it as "not
  ruled on" ([[ask-before-the-work-not-after]]).

**My proposals inside the map that John has not ruled on** (mine, not his): keep fan fatigue in
chapter 3, drop the rising `maxActive`, move merch to chapter 4; Game Jam's tier unlocks become
purchases in chapter 6.

**How to apply:** design future chapters from this map and the translation rules of
[[design-restart-2026-11]] (capstone on a prestige currency, a release meter Roadies cannot reach,
prestige raises payouts never fill rates, events lateral). Check whether the docs have caught up
before citing them; the chapter 3 draft JSON still predates the restart.
