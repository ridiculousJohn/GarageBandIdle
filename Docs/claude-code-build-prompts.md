# Garage Band Idle — Claude Code build prompts (Chapter 1 → playable)

Feed these to Claude Code **one at a time**, in order. After each: run it in the Unity Editor,
confirm the stated goal, then `git commit`. Don't move to the next slice until the current one works.

**Setup assumptions:** empty Unity 6000.5.4f1 2D project created in Hub; `git init` done;
`garage-band-idle-design.md` and `chapter-01-garage.json` sitting in `/docs`; Claude Code opened in
the project root. The design doc is the source of truth — every prompt references its sections.

Build order and why: each slice depends on the ones before it (offline earnings need the real-time
tick; prestige needs the currency block split; the content-unlock upgrades are what reveal
fans/covers/album). Building bottom-up keeps a break isolated to the slice you just added.

---

## 0 — Git hygiene (.gitignore + .gitattributes)

> Target Unity 6000.5.4f1. At the repo root, create a standard Unity `.gitignore` and
> `.gitattributes`. Do not stage or commit — just create the two files.
>
> `.gitignore`: base it on Unity's official template. Ignore `Library/`, `Temp/`, `Obj/`, `Logs/`,
> `UserSettings/`, `Build/`, `Builds/`, `MemoryCaptures/`, `.vs/`, `.idea/`, generated
> `*.csproj`/`*.sln`/`*.user`, `*.apk`/`*.aab`/`*.unitypackage`, and crash logs. Keep `Assets/`,
> `Packages/`, and `ProjectSettings/` tracked.
>
> `.gitattributes`: set `* text=auto`; force LF for source (`*.cs`, `*.json`, `*.md`, `*.shader`);
> mark Unity YAML files (`*.unity`, `*.prefab`, `*.asset`, `*.mat`, `*.anim`, `*.controller`,
> `*.meta`) as `text merge=unityyamlmerge eol=lf`; mark binary asset types (`*.png`, `*.jpg`,
> `*.psd`, `*.fbx`, `*.wav`, `*.mp3`, `*.ogg`, `*.ttf`, `*.otf`, `*.dll`) as `binary`. Include
> commented-out Git LFS `filter` lines for those binary types that I can enable later with
> `git lfs track` if the project grows.

✅ **Test & commit:** confirm both files exist and look sane; `git add . && git commit -m "chore: unity gitignore + gitattributes"`.

---

## 1 — Core loop (currency, tick, tap, one generator)

> Target Unity 6000.5.4f1. Read `/docs/garage-band-idle-design.md` and follow its §12 architecture
> and terminology. Build ONLY the core loop as a runnable vertical slice:
>
> - Add the C# BreakInfinity library (BreakInfinity.cs) and a thin `BigNumber` wrapper used for all
>   currency and production values.
> - Currencies and their groups must both be **data-driven and open to extension — neither is a
>   hardcoded field or a closed C# enum.** New currencies and new groups are added as assets, with no
>   change to manager code.
>   - `CurrencyGroupDefinition` ScriptableObject: stable string `id`, display name, and behavior flags
>     the code acts on rather than special-casing by name — at minimum `resetsOnAlbumRelease` (bool).
>     Seed two: `run` (resetsOnAlbumRelease = true) and `permanent` (false).
>   - `CurrencyDefinition` ScriptableObject: stable string `id`, display name, a `groupId` referencing
>     a `CurrencyGroupDefinition`, number-formatting hints, and a starting value. Seed Cash and Fans
>     (group `run`) and Records (group `permanent`).
>   - `CurrencyManager` stores balances in a dictionary keyed by currency `id` (value `BigNumber`),
>     populated from whatever `CurrencyDefinition` assets exist — it must not assume a fixed set.
>     Expose `Get(id)`, `Add(id, amount)`, `Set(id, amount)`, and a change event carrying the `id`.
>     Fire change events on updates — no per-frame polling.
>   - Group-driven behavior operates on the flags, never on named ids: an album release (later slice)
>     resets every currency whose group has `resetsOnAlbumRelease = true`. This must keep working when
>     new currencies OR new groups are added later.
>   - On load, validate that every currency `id` referenced elsewhere resolves to a real
>     `CurrencyDefinition`, and every `groupId` resolves to a real group — fail loudly if not.
> - `TickSystem` that updates the economy on real elapsed time using `DateTime.UtcNow` deltas, not
>   frame time.
> - A minimal single-scene UI: a Cash label, a "Jam" tap button (+1 Cash/tap), and ONE hardcoded
>   generator (Practice Amp: base cost 60, +0.4 Cash/sec) with a Buy button whose cost scales ×1.15
>   per owned.
>
> Goal: I press Play, tap to earn Cash, buy a Practice Amp, and watch Cash rise on its own. Cash,
> Fans, and Records already exist as CurrencyDefinition assets (Fans/Records just aren't used yet).
> Stop here — no upgrades, prestige, chapters, events, save, or monetization yet.

✅ **Test & commit:** number goes up on tap; amp produces passively; cost rises per purchase.

---

## 2 — Data-driven generators + ScriptableObjects + JSON importer

> Make content data-driven per §12. Read `/docs/chapter-01-garage.json`.
>
> - Create ScriptableObject definition classes matching the JSON schema: `ChapterDefinition`,
>   `GeneratorDefinition`, `UpgradeDefinition`, `EventDefinition`, `CoverDefinition` (fields: id,
>   name, cost, costGrowth, baseOutput, produces, unlock, gate, payload, scope, etc.).
> - Write an Editor menu script that reads `chapter-01-garage.json` and generates the corresponding
>   ScriptableObject assets under `Assets/ScriptableObjects/` (Chapters/, Generators/, Upgrades/,
>   Events/, Covers/).
> - Refactor the generator system to load `GeneratorDefinition` assets instead of the hardcoded amp.
>   Implement `CostCalculator` (`cost = baseCost × growth^owned`) and `ProductionCalculator`
>   (`Σ(gen.baseOutput × count)` then `× (1 + 0.02 × records)`).
> - Stay consistent with the currency design (slice 1): a generator's `produces` field is a currency
>   **string id**, not a hardcoded currency — production routes through `CurrencyManager.Add(id, …)`,
>   and runtime generator state is keyed by generator `id` in a dictionary, not fixed fields. Adding a
>   generator later = a new asset + JSON row, no code change. Validate that every `produces` id
>   resolves to a real `CurrencyDefinition` on load.
> - Honor each generator's `unlock` rule (cashEarnedTotal / ownedCount thresholds) so
>   Practice Amp → Drummer → Bassist → Guitarist reveal in sequence.
>
> Goal: the four Chapter-1 generators come straight from the JSON with correct costs/outputs and
> unlock in order. Stop here.

✅ **Test & commit:** all four generators buyable from data; costs match the JSON; unlock gates fire.

---

## 3 — Fans + the content-unlock pattern (Play for a Crowd)

> Implement the Fans currency and the content-unlock upgrade pattern that reveals it. Reference §3,
> §5, §6 of the design doc and the `fans` + `play_for_crowd` entries in the JSON.
>
> - Fans are run-scoped and hidden until unlocked. Implement a general "content-unlock" mechanism: a
>   `contentUnlock` upgrade whose gate is met flips a permanent-in-chapter unlock flag and fires an
>   event. `play_for_crowd` (gate: own 1 Drummer) sets the `fans` flag and reveals the Fans meter.
> - Fan rate: `baseFansPerSec 0.2 + 0.02 × (owned bandmate units: drummer/bassist/guitarist, NOT
>   practice_amp)`. Fans must be a function of band size and time ONLY — never Cash or Cash/sec (see
>   the couplingNote in the JSON and §11).
>
> Goal: recruiting the first Drummer reveals the Fans meter; Fans accrue passively from band size and
> time, provably independent of Cash income. Stop here.

✅ **Test & commit:** Fans hidden until first Drummer; then accrue; buying amps (Cash) does not change fan rate.

---

## 4 — Learn Covers + rehearsal + cover buffs

> Implement the Learn Covers system per §5–§6 and the `covers` + `learn_covers` entries in the JSON.
>
> - `learn_covers` (contentUnlock, gate: 25 Fans) reveals three cover bars and the Rehearsal tick.
> - Rehearsal points fill the bars: `pointsPerSec 1` passive + `pointsPerTap 2` on Jam taps. Bars:
>   fill requirements 120 / 300 / 600.
> - Completing a cover applies its `fanRateMultiplier` (1.15 / 1.15 / 1.20), stacking
>   multiplicatively on fan rate. Covers are run-scoped.
>
> Goal: at 25 Fans the covers appear; rehearsal fills them from play (not Cash); each completed cover
> visibly raises fan rate. Stop here.

✅ **Test & commit:** covers unlock at 25 fans; fill from taps/time; fan rate jumps on completion.

---

## 5 — Buff upgrades (any-currency gating)

> Implement the run-scoped buff upgrades per §4 and the `upgrades` array (the `type: buff` entries).
>
> - Load from `UpgradeDefinition` assets. Support gating on ANY currency per each upgrade's `gate`
>   field. Implement payloads: `tapValueAdd`, `generatorOutputMultiplier`, `allCashPerSecMultiplier`.
> - Stay consistent with the currency/generator design: an upgrade's `gate` currency is a **string
>   id** resolved against `CurrencyManager` (no hardcoded Cash/Fans branches), payload targets
>   (e.g. which generator) are referenced by `id`, and `scope` (run / permanentInChapter) is a data
>   field the reset logic acts on — not a per-upgrade special case. Adding an upgrade or a new payload
>   type later should be an asset/JSON change plus one payload handler, not a rewrite. Validate gate
>   and target ids on load.
> - Ch1 buffs: `stage_presence` (+1 tap, Cash-gated), `amp_strings` (×2 amp, Cash-gated),
>   `kit_upgrade` (×2 drummer, Cash-gated), `tight_set` (×1.5 all Cash/sec, **Fans-gated at 30** —
>   this proves non-Cash gating).
> - Mark buff upgrades as run-scoped via their `scope` field (they'll reset on album release in the
>   next slice, driven by scope, not by a hardcoded list).
>
> Goal: buff upgrades appear when their gate currency is met and apply their effect; `tight_set`
> unlocks on Fans, not Cash. Stop here.

✅ **Test & commit:** each buff applies; tight_set gates on Fans; effects stack correctly.

---

## 6 — Album prestige (Cut a Demo)

> Implement the album prestige per §5 and the `album` + `cut_demo` entries in the JSON.
>
> - `cut_demo` (contentUnlock, compound gate: 50 Fans AND 1 cover completed) reveals the "Cut a Demo"
>   (Release) button.
> - On release: reset the RUN block only — Cash, generator owned counts, buff upgrades, Fans, covers,
>   rehearsal. KEEP the permanent block — Records, contentUnlock flags, Roadies.
> - Award Records = `floor((fansThisRun / 5) ^ 0.5)`. Each Record adds +2% to the permanent global
>   income multiplier (`1 + 0.02 × records`).
>
> Goal: at 50 Fans + 1 cover the Release button appears; releasing resets the run, grants Records,
> and the next climb is visibly faster. Content unlocks stay unlocked across releases. Stop here.

✅ **Test & commit:** release resets run, keeps Records/unlocks, re-climb faster; Records formula matches examples in the JSON.

---

## 7 — Records manager + capstone / chapter gate

> Implement Records tracking and the chapter capstone per §1–§2, §5 and the `capstone` entry.
>
> - `RecordsManager` tracks cumulative Records and exposes the permanent income multiplier.
> - When cumulative Records ≥ 30 (`capstoneRecordsGate`), unlock the "Play the Backyard Party"
>   capstone.
> - On capstone completion: grant 1 Roadie to a permanent pool but keep the Roadie allocation/replay
>   UI LOCKED (`roadieSystemUIUnlocked: false` — deferred to Chapter 2); fire the `storyBeatCapstone`
>   text; set an "advance to Chapter 2" flag (Chapter 2 content doesn't exist yet — just mark it).
>
> Goal: reaching 30 cumulative Records unlocks the capstone; playing it shows the story beat, banks
> the first Roadie (no allocation UI), and flags chapter advancement. Stop here.

✅ **Test & commit:** capstone unlocks at 30 Records; story beat fires; Roadie banked; advance flag set.

---

## 8 — Events (Garage Jam Challenge)

> Implement the event system per §6.1 and the `events` array (garage_jam).
>
> - `EventManager` runs a self-contained challenge on a sandboxed economy snapshot: on entry, reset
>   the chapter economy to a fixed baseline for the event's duration (independent of the player's
>   Records).
> - garage_jam: debuff `automationDisabled` (generators paused, tap-only); goal reach Cash target;
>   timed and failable. Three tiers (goal 500/2500/10000, timer 60/60/45, reward tap
>   ×1.25/×1.50/×2.0, scope permanent-in-chapter).
> - Failure/quit: reset that event's progress only; costs time, never permanent progress. Available
>   once the player has ≥1 Record (after first demo).
>
> Goal: I can enter garage_jam, play it tap-only against the timer at a fixed baseline, succeed for a
> permanent-in-chapter tap buff or quit/fail for free, and repeat at higher tiers. Stop here.

✅ **Test & commit:** baseline reset on entry; tap-only; timer + fail/quit are cheap; tiers escalate; reward applies.

---

## 9 — Save/load + offline earnings

> Implement persistence and offline earnings per §12 (rules 2, 4, 6) and the offline table in §9.
>
> - `SaveSystem`: serialize to JSON with a checksum; validate on load and reject/repair tampered
>   saves. Model the run block and permanent block as separate sections in the schema (an album
>   release clears the run block, writes the permanent block).
> - `OfflineEarnings`: on load, compute `productionPerSecond × min(secondsAway, cap) × rate` using
>   `DateTime` deltas, rate = 0.5, cap = 4 hours. Show a collect screen with the amount and a
>   placeholder "Double it" button (wire the actual ad later).
>
> Goal: closing and reopening restores state; time away grants offline Cash at 50% capped at 4h; a
> tampered save is rejected. Stop here.

✅ **Test & commit:** state persists across restart; offline payout correct and capped; checksum rejects edits.

---

## 10 — Chapter 1 playable pass (wire it end-to-end)

> Tie Chapter 1 together into a playable first-run experience using the `progression` stages in the
> JSON as the spine (Stage 0 First Notes → Stage 7 Backyard Party).
>
> - Show the `storyBeatOpen` card on first launch and the `storyBeatCapstone` at the capstone.
> - Ensure the staged reveal reads cleanly: tap-only → first gear → Fans → covers → Cut a Demo (target
>   ~5 min to first demo) → repeat → Garage Jam available → capstone at 30 Records.
> - Minimal but legible UI: current Cash/Fans/Records, generator rows, upgrade list, cover bars,
>   Release button, event entry, collect screen. Use a `NumberFormatter` for big-number display
>   (1.23K / 4.56M / etc.).
>
> Goal: a new player can play Chapter 1 start to finish — tap, build, learn a cover, cut a demo,
> loop, do the event, and hit the Backyard Party capstone. Stop here.

✅ **Test & commit:** full Chapter 1 loop is playable end-to-end.

---

## After this

Chapter 1 is playable. The remaining work is a separate phase, roughly:
- **Tune** the pacing (time-to-first-demo, Records gate, cycles-to-capstone) by playing it — these
  are feel-based and can't be judged from numbers alone.
- **Chapter 2** content (a new `chapter-02-*.json`) plus unlocking the Roadie allocation/replay UI.
- **Monetization SDKs** last (ads mediation + IAP), once the game is fun — wire the "Double it" and
  Encore/Backstage Pass placements that are already stubbed.

Keep `garage-band-idle-design.md` as the source of truth; when a decision changes while building,
update the doc so it and the code don't drift.
