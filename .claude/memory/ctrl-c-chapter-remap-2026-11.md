---
name: ctrl-c-chapter-remap-2026-11
description: "The Ctrl C to Garage Band chapter map John approved on 2026-11-10, and the three picks he ruled on; not yet written into the docs"
metadata:
  node_type: memory
  type: project
  originSessionId: 9bcd0e53-5626-49e8-9aa2-6f02854d91fd
  modified: 2026-11-11T01:11:21.973Z
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

**REVERSED later on 2026-11-10: John keeps the song quality mechanic.** "I'd like to keep it, and
resolve the open questions." He had not seen that cutting the Catalog left songs with no mechanical
weight. Open and being resolved with him: which songs reach the Discography, whether the boost
stays inside the Studio (my recommendation) or crosses chapters, what a song is in the
ideas-to-tracks economy, how the tier is rolled, what quality feeds. Design section 7 carries a note
I wrote that day calling the quality roll "dropped"; that note is now wrong and waits on an order to
fix. The built `AddSong` / Discography has no quality field yet.

**The song design below is WRITTEN INTO THE DOCS** (2026-11-10, his order "Write it into docs"):
design section 7 rewritten as 7.1-7.5, the Catalog boost removed from sections 1, 2, 3, 5, 6.1, 10
and 11, section 1 gains the song loop, and the build plan has the entry "Songs: standing, sets and
the Hall of Fame" with three steps. Not built. The stale docs were fixed the same day (his order
"Fix the stale docks first"): the backlog's chapter numbers, branch pair home (chapter 8), Records
note and reference path; the build plan's chapter 2 and chapter 3 lines; the chapter 3 draft JSON
is marked SUPERSEDED in its `_meta.status` and is replaced when chapter 3 is designed. John has
played chapter 2 by hand: "chapter 2 is fine for now, it might change".

**Songs, as John shaped them late on 2026-11-10**
- The Studio is a pipeline: lay down tracks, mix them, master the mix into a song ("Probably made
  from tracks... laying down tracks, mixing the tracks, then recording the master to make a song").
- No quality roll. A song's standing comes from popularity, which is not part of the Studio ("just
  recording the song doesn't make it ... a 'classic'... That should be based on popularity").
- Songs may cross chapters ("yes, we can add songs to be something that can cross chapters"). He
  expects them to matter in Radio and in Arenas ("you play your hit song") and other later chapters.
- He likes it as a "meta-prestige": replay the Studio for songs, raise their popularity on tour
  and radio, so arena gigs pay more. His brainstorm (explicitly "just me brainstorming", NOT
  decisions): Tour - assign up to N songs from the catalog at the chapter's start, those are the
  ones that level; Radio - plays your top songs and needs a base popularity across the selection,
  so one big hit can carry a new song onto the air instead of touring it; Arenas - pick a setlist
  and the outcome scales with the songs' popularity.
- John's rulings after my pros and cons (2026-11-10):
  - The chapters stop being independent here: Studio -> Tour -> Radio -> Arena is a repeatable meta
    loop, and a weaker first pass is the idle-game point. The one requirement is that the first
    play is completable. A long loop is fine; chapters can be made shorter if needed.
  - Songs are ASSIGNED to a chapter by the player, "sort of like Roadies can be allocated". Radio's
    selection is chosen, not automatic.
  - Arena pay reads the song's classification (Song / Hit / Classic), not raw popularity.
  - How many songs a chapter takes is defined on the chapter and tunable; Arenas may take more
    than the Tour.
  - Tour slow, Radio fast: fine, tuning.
  - With fewer songs than slots, the songs you have are the ones played.
  - The catalog is limited; when full, a replay means retiring a song.
  - Lock-in is answered by restarting the chapter; if no way exists, a "restart" / "start over"
    button for the chapter in settings.
- Ideas he floated, explicitly not to implement yet: a Hall of Fame for retired songs giving a
  game-wide boost; making songs "a more abstract concept" and implementing songs in that system.
  My advice given: model songs on the Roadie allocation precedent, no generic layer until a second
  thing needs it.
- Second round of rulings (2026-11-10), after he asked for my thoughts and not just a record:
  - The Roadie comparison meant only "lives outside a chapter". A chapter's selected songs cannot
    be changed until the chapter is restarted or completed.
  - A song can be in ANY chapter's set at once - one set list can serve the tour, radio and arena.
    He overruled my one-chapter-at-a-time recommendation.
  - Forced obsolescence stays: he disagreed with making retirement optional. His candidate rule: a
    new song replaces only a less popular one, otherwise it is discarded. I pointed out a new song
    has zero popularity, so that rule as worded never replaces anything; unresolved.
  - A restart button on every chapter is fine.
  - Classifications are AUTHORED, with finer steps than three.
  - The Hall of Fame is IN, as the loop's reward (my point 1): it is what the loop is for.
- A song in both the Tour and Radio sets gaining popularity in both while away is intended: "if
    Im on tour while my song is getting airtime, then that's a natural bonus".
- Third round (2026-11-10): Hall of Fame induction is how catalog slots are freed ("I like
    that"). Songs do NOT start with any popularity ("I don't like them starting with any
    popularity"), so there is no bottom replacement: with a full catalog a new song is discarded
    until an induction frees a slot. A song cannot be inducted while it is in a set list. Maybe
    (his word "might"): an Arena set may include a Hall of Fame song, perhaps one - "if led
    Zeppelin played an arena now, they would play Stairway to Heaven". I suggested making that an
    authored per-chapter number like the slot count.
- Superseded: the "replaces a less popular song" rule and my "born with popularity" fix.
- Induction needs the highest classification, "and also maybe based on time as well": a one-hit
    wonder should be harder to induct than a song that stayed on the charts. I recommended counting
    completed chapter runs the song sat in a set as top classification (not clock time, not decay);
    unresolved.
- DECIDED ("ok go with your recommendation"): induction needs the highest classification AND a
    number of completed chapter runs in which the song sat in a set at that classification. The
    number is authored. Which chapters count toward it is not settled (Radio only, or any chapter
    with a set); I proposed authoring it per chapter.
- Which runs count for induction is AUTHORED, not limited: an action placed on a chapter's
    capstone credits the run ("if we only wanted Radio to do it then we'd only put that action on
    the radio's capstone").
- The Hall of Fame boost "doesn't matter right now"; it will be some boost to yield, a currency or
    other stat(s) per song.
- The save holds songs wherever other persistent data is kept (root already saves the list).
- Radio's bar DECIDED ("yes your suggestions are fine"): the set's TOTAL popularity gates the fast
    airplay gain; below it songs still play and gain slowly; the chapter never locks.
- The two engine pieces "we'd need to implement" (his words; a statement of need, not yet an order
    to build). Shape I proposed, APPROVED 2026-11-10 ("that's all fine") as a design, with the
    build order songs-with-standing, then sets, then the Hall of Fame; no order to write docs or
    code was given: popularity is paid by an ACTION at
    authored moments (a show or spin completing, a release) into every song in the acting chapter's
    set - no continuous rate, so the tick and idle claim do not change and away completions pay
    through the existing claim; arena pay is a multiplier formula reading the set's classifications
    the way `RoadieActiveBoost` reads the played chapter's allocation; Radio's bar is a condition on
    the set's total popularity. Sets live in the chapter's facts so a capstone or restart clears
    them; songs, classifications and the catalog limit live on root.
- Still open: what happens to a retired or inducted song that sits in a
  locked set, where exactly popularity accrues, and the engine shape (songs are runtime things, not
  authored definitions - a conversation before code).
- Lesson: when John answers a design question he wants my assessment of the answer, not a
  transcript of it ("thoughts on my answers instead of just recording them?").

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

**All seven code changes were BUILT on 2026-11-10** (1092/1092; build-plan rows "Pre-chapter
runtime: ..."; design 5, 7, 12.2, 12.4, 12.5). Built names: `DebtFactor`, `CurrencyExceeds`,
`LogOnBalance`, the `payout` and `exponent` stats (a currency's plans at its home, read by
`AddCurrency.AmountFor`), `SetCurrency` + `InverseOfBalance` (the recorded pace), `AddSong` +
`GameSession.RenameSong` + `DiscographyUI`. John chose "Rename later" for songs. Not done: the
Discography window has not been looked at in the running game; design sections 2, 3, 5 and 11 still
carry the Catalog-boost sketch; the chapters themselves are not designed. The "not built" wording
in the notes below describes the state before that day's work.

**The code-change list as it stood before building**
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
