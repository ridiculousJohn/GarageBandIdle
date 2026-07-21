# Garage Band Idle — Claude Code build prompts (Chapter 1 → playable)

Feed these to Claude Code **one at a time**, in order. After each: run it in the Unity Editor,
confirm the stated goal, then `git commit`. Don't move to the next slice until the current one works.

**Setup assumptions:** empty Unity 6000.5.4f1 2D project created in Hub; `git init` done;
`garage-band-idle-design.md` and `chapter-01-garage.json` sitting in `/docs`; Claude Code opened in
the project root. The design doc is the source of truth — every prompt references its sections.

Build order and why: each slice depends on the ones before it (offline earnings need the real-time
tick; prestige needs the currency block split; the content-unlock upgrades are what reveal
fans/covers/album). Building bottom-up keeps a break isolated to the slice you just added.

**Progress marker:** slices 0–4 are already built. Slice **3.5** is a dedicated consolidation pass
that establishes the cross-cutting foundations — a single `Condition` type + evaluator, one flag
registry for all progressive reveal, full-Addressables ScriptableObject discovery, the rewards pool,
data-driven sections/modules, and `isBandmate` — and **retrofits slices 1–3 onto them**. These are
foundations that touch code already written, so they are introduced explicitly here rather than
pretended to be forward-only. Slices 4–10 assume 3.5 is in place and build on it.

---

## 0 — Git hygiene (.gitignore + .gitattributes)  ✅ done

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

## 1 — Core loop (currency, tick, tap, one generator)  ✅ done

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
*(Retrofitted in 3.5: currency/group assets now discovered via Addressables.)*

---

## 2 — Data-driven generators + ScriptableObjects + JSON importer  ✅ done

> Make content data-driven per §12. Read `/docs/chapter-01-garage.json`.
>
> - Create ScriptableObject definition classes matching the JSON schema: `ChapterDefinition`,
>   `GeneratorDefinition`, `UpgradeDefinition`, `EventDefinition`, `BarDefinition` (fields: id,
>   name, cost, costGrowth, baseOutput, produces, unlock, gate, payload, scope, isBandmate, etc.).
> - Write an Editor menu script that reads `chapter-01-garage.json` and generates the corresponding
>   ScriptableObject assets under `Assets/ScriptableObjects/` (Chapters/, Generators/, Upgrades/,
>   Events/, Bars/).
> - Refactor the generator system to load `GeneratorDefinition` assets instead of the hardcoded amp.
>   Implement `CostCalculator` (`cost = baseCost × growth^owned`) and `ProductionCalculator`
>   (`Σ(gen.baseOutput × count)` then `× (1 + 0.02 × records)`).
> - Stay consistent with the currency design (slice 1): a generator's `produces` field is a currency
>   **string id**, not a hardcoded currency — production routes through `CurrencyManager.Add(id, …)`,
>   and runtime generator state is keyed by generator `id` in a dictionary, not fixed fields. Adding a
>   generator later = a new asset + JSON row, no code change. Validate that every `produces` id
>   resolves to a real `CurrencyDefinition` on load.
> - Honor each generator's `unlock` rule so Practice Amp → Drummer → Bassist → Guitarist reveal in
>   sequence.
>
> Goal: the four Chapter-1 generators come straight from the JSON with correct costs/outputs and
> unlock in order. Stop here.

✅ **Test & commit:** all four generators buyable from data; costs match the JSON; unlock gates fire.
*(Retrofitted in 3.5: generators discovered via Addressables label; `unlock` becomes a unified
`Condition`; `isBandmate` is a real field the fan system reads.)*

---

## 3 — Fans + the content-unlock pattern (Play for a Crowd)  ✅ done

> Implement the Fans currency and the content-unlock upgrade pattern that reveals it. Reference §3,
> §5, §6 of the design doc and the `fans` + `play_for_crowd` entries in the JSON.
>
> - Fans are run-scoped and hidden until unlocked. Implement a general "content-unlock" mechanism: a
>   `contentUnlock` upgrade whose gate is met reveals a system and fires an event. `play_for_crowd`
>   (gate: own 1 Drummer) reveals the Fans meter.
> - Fan rate: `baseFansPerSec 0.2 + 0.02 × (owned bandmate units)`. Fans must be a function of band
>   size and time ONLY — never Cash or Cash/sec (see the couplingNote in the JSON and §11).
>
> Goal: recruiting the first Drummer reveals the Fans meter; Fans accrue passively from band size and
> time, provably independent of Cash income. Stop here.

✅ **Test & commit:** Fans hidden until first Drummer; then accrue; buying amps (Cash) does not change fan rate.
*(Retrofitted in 3.5: the ad-hoc "unlockSystem" reveal is replaced by the flag registry —
`play_for_crowd` sets the `fans` flag and the Fans currency/meter reveal on a `flagSet` Condition;
fan rate reads `isBandmate` instead of naming drummer/bassist/guitarist.)*

---

## 3.5 — CONSOLIDATION: unified Condition, flag registry, Addressables discovery, rewards, sections  ✅ done

This slice adds no new gameplay. It establishes the cross-cutting foundations the JSON now assumes and
**retrofits slices 1–3 onto them**, so there is one way to express a gate, one way to reveal content,
and one way to discover content. Do it as one slice, then confirm slices 1–3 still play identically.

> Read `/docs/garage-band-idle-design.md` (§4, §12) and `/docs/chapter-01-garage.json` — especially
> `_meta.modelNotes` (conditions, flags, bars, addressables) and the `flags` array. This is a
> refactor/foundation pass; **do not change observable gameplay**. Build these six foundations and
> retrofit the existing slices 1–3 onto them:
>
> **1. Full Addressables discovery for ALL definition ScriptableObjects.** Every definition type is
> discovered at runtime by Addressables **label**, never by direct reference, `Resources`, or a
> hardcoded list. Assign a per-type label to `CurrencyGroupDefinition`, `CurrencyDefinition`,
> `GeneratorDefinition`, `UpgradeDefinition`, `EventDefinition`, `BarDefinition`/`BarGroupDefinition`,
> `RewardDefinition`, and `ChapterDefinition`. Build a `ContentDatabase` (or extend the existing
> bootstrap) that, on boot, loads every asset for each label, builds `Dictionary<string, TDef>`
> registries keyed by `id`, and exposes typed getters. Make the editor importer from slice 2 also
> assign the correct Addressables label + address when it generates assets. **Retrofit** the slice-1
> `CurrencyManager` and slice-2 generator loading to source their definitions from `ContentDatabase`.
> Fail loudly on a duplicate `id` within a type or a missing label group.
>
> **2. One `Condition` type + one `ConditionEvaluator`.** Define a serializable, polymorphic
> `Condition` discriminated by a `type` string, covering every shape in the JSON:
> `currency` (current balance ≥ `amount`), `currencyEarnedTotal` (lifetime earned ≥ `value`),
> `ownedCount` (generator owned ≥ `value`), `flagSet` (`flag` is set), `barsCompleted`
> (≥ `value` bars completed in `group`), `recordsCumulative` (cumulative Records ≥ `value`), and
> `compound` (`all` / `any` arrays of nested `Condition`). Implement `ConditionEvaluator.Evaluate(
> Condition, EvalContext) → bool`, where `EvalContext` exposes the managers it needs (currency, flags,
> generators, bars, records). This one evaluator serves unlocks, gates, section visibility, and event
> availability. **Retrofit** the generator `unlock` checks (slice 2) and the `play_for_crowd` gate
> (slice 3) to use `Condition`. Track lifetime-earned per currency so `currencyEarnedTotal` works.
>
> **3. Flag registry + unified reveal.** Build a `FlagManager` holding a set of flags
> (permanent-in-chapter), with `IsSet(id)`, `Set(id)`, and a change event. The three Ch1 flags come
> from the JSON `flags` array (`fans`, `covers`, `album`). A `contentUnlock` upgrade's payload is
> `{ effect: "setFlag", flag: <id> }`; when its gate passes, it sets that flag. Anything that appears
> when a system exists gates on a `flagSet` Condition. **Retrofit** slice 3: delete the bespoke
> `unlockSystem` reveal path — `play_for_crowd` now sets flag `fans`, and the Fans currency/meter
> reveal on `{ type: "flagSet", flag: "fans" }`. There must be exactly one reveal mechanism.
>
> **4. Rewards pool.** Add `RewardDefinition` (fields per the JSON `rewards` array: id, name, type,
> value, scope; types `fanRateMultiplier`, `tapValueMultiplier`, `setFlag`). Discover them by
> Addressables label. Add a `RewardManager.Apply(rewardId, context)` that dispatches on `type` (one
> handler per type; `setFlag` routes through `FlagManager`, same registry as content-unlocks). Rewards
> are referenced by `id` from bars and events (built in later slices) — define the pool now.
>
> **5. Sections + module registry (data-driven layout & reveal).** Add `SectionDefinition` (id, name,
> `modules` = list of module string ids, optional `visibleWhen` Condition) discovered by label. A
> section reveals as a group when its `visibleWhen` evaluates true (no `visibleWhen` = visible from
> start). Resolve module ids (`module/currency-header`, `module/tap`, `module/generator-list`) to
> prefabs through an Addressables string→prefab lookup. Seed the two Ch1 sections from the JSON
> (`garage_floor`, `the_band`). Drive the existing slice-1/2 UI through this section layout.
>
> **6. `isBandmate` as behavior-as-data.** Ensure `GeneratorDefinition.isBandmate` exists and that the
> fan-rate calc (slice 3) sums owned units of generators where `isBandmate == true` — never a name
> list.
>
> Finally, run a **validation pass** on boot: every id referenced by a `Condition`, payload, reward,
> module, or `groupId` resolves to a real asset/flag; fail loudly otherwise.
>
> Goal: slices 1–3 play exactly as before, but now every gate is a `Condition`, every reveal is a
> flag, every definition is discovered via Addressables, and the rewards pool + sections exist for the
> slices ahead. Stop here.

✅ **Test & commit:** Chapter start → tap → 100 Cash reveals The Band section → buy amps → first
Drummer sets `fans` flag and reveals Fans — all behaving as in slice 3, but driven by
Condition/flags/Addressables. Boot validation passes; a deliberately broken id fails loudly.

---

## 4 — Fillable bars + Learn Covers (generic, `fillCurrency`-driven)  ✅ done

> Implement a **generic fillable-bar system** and use it for Learn Covers. Reference §3, §5, §6 of the
> design doc and the `rehearsal` + `bars` + `learn_covers` entries in the JSON. Build the system
> around `fillCurrency` — nothing here may hardcode "covers."
>
> - Add the `rehearsal` `CurrencyDefinition` (group `run`), discovered via Addressables like every
>   other currency. Its earn config comes from the JSON `rehearsal` block: a passive tick
>   `perSec = 1` plus `perTap = 2` on Jam taps. Rehearsal is revealed by the `covers` flag (model it
>   like `fans`: the currency owns its earn config).
> - `learn_covers` (contentUnlock, gate `currency fans ≥ 25`) sets the `covers` flag — which reveals
>   both the Rehearsal currency and the Learn Covers bar group.
> - `BarDefinition` (id, name, `fillCurrency`, `fillRequirement`, `reward`) and `BarGroupDefinition`
>   (id, name, `revealFlag`, `fillMode`, `scope`, ordered bar list), discovered by Addressables label.
> - `BarSystem` with `fillMode: "perBar"` (player-directed): each bar tracks its OWN accumulated
>   progress and the player chooses which bar to pour Rehearsal into; a fill action spends from the
>   shared Rehearsal pool into the selected bar. Bars are independent, NOT cumulative thresholds on one
>   counter (totals 120 / 300 / 600 = 1020 to finish all three). The fill logic reads `fillCurrency`
>   and works for any currency.
> - On bar completion, apply its `reward` via `RewardManager` (Ch1 rewards are `fanRateMultiplier`,
>   stacking multiplicatively on fan rate; wire `fans.barBonusesApply`).
> - Implement the `barsCompleted` Condition (count completed bars in a `group`) — `cut_demo` will use
>   it next slice. `cover_1` completing satisfies `barsCompleted(learn_covers) ≥ 1`.
> - Bars are run-scoped via the group `scope` (they reset on album release, next slice).
>
> Goal: at 25 Fans the `covers` flag reveals Rehearsal + three cover bars; Rehearsal fills from
> taps/time (not Cash); the player directs Rehearsal per-bar; completing a bar raises fan rate via the
> rewards pool; the fill system is generic (`fillCurrency`), not covers-specific. Stop here.

✅ **Test & commit:** covers reveal on the `covers` flag at 25 fans; per-bar player-directed fill from
taps/time; fan rate jumps on completion via RewardManager; `barsCompleted` reports correctly.

---

## 5 — Buff upgrades (any-currency gating via Condition)

> Implement the run-scoped buff upgrades per §4 and the `type: buff` entries in the `upgrades` array.
> Gating and discovery already exist from 3.5 — this slice adds the buff payloads and run scope.
>
> - Load `UpgradeDefinition` assets from the ContentDatabase (Addressables). Each buff's `gate` is a
>   `Condition` evaluated by the shared `ConditionEvaluator` — no per-currency branches. Its `cost` is
>   `{ currency, amount }` charged through `CurrencyManager`.
> - Implement payloads: `tapValueAdd`, `generatorOutputMultiplier` (target generator by `id`),
>   `allCashPerSecMultiplier`. Payload targets are referenced by `id`; adding a payload type later is
>   one handler, not a rewrite. Validate gate/target ids on load.
> - Ch1 buffs: `stage_presence` (+1 tap, Cash-gated), `amp_strings` (×2 amp, Cash-gated),
>   `kit_upgrade` (×2 drummer, Cash-gated), `tight_set` (×1.5 all Cash/sec, **Fans-gated at 30**).
>   `tight_set` proves non-Cash gating falls out of the unified Condition for free — same shape,
>   different `currency` id.
> - Buff upgrades are run-scoped via their `scope` field; the reset logic (next slice) acts on
>   `scope`, not a hardcoded list.
>
> Goal: buff upgrades appear when their gate `Condition` holds and apply their effect; `tight_set`
> gates on Fans via the same evaluator as the Cash buffs. Stop here.

✅ **Test & commit:** each buff applies; `tight_set` gates on Fans; effects stack correctly.

---

## 6 — Album prestige (Cut a Demo)

> Implement the album prestige per §5 and the `album` + `cut_demo` entries in the JSON.
>
> - `cut_demo` (contentUnlock, `compound.all` = [`currency fans ≥ 50`, `barsCompleted(learn_covers) ≥
>   1`]) sets the `album` flag, which reveals the "Cut a Demo" (Release) button. The gate is evaluated
>   by the shared `ConditionEvaluator`.
> - On release, reset the RUN block, driven by data, not a name list: reset every currency whose group
>   has `resetsOnAlbumRelease = true` (Cash, Fans, Rehearsal), every generator's owned count, every
>   upgrade/bar whose `scope == run` (buff upgrades, cover bars). KEEP the permanent block — Records,
>   `contentUnlock` upgrade effects, **flags** (content stays revealed across demos), Roadies.
> - Award Records = `floor((fansThisRun / 5) ^ 0.5)`. Each Record adds +2% to the permanent global
>   income multiplier (`1 + 0.02 × records`).
>
> Goal: at 50 Fans + 1 cover the `album` flag reveals Release; releasing resets the run (scope/group
> driven), grants Records, keeps flags/unlocks, and the next climb is visibly faster. Stop here.

✅ **Test & commit:** release resets run via scope/group flags, keeps Records/flags/unlocks, re-climb
faster; Records formula matches the examples in the JSON.

---

## 7 — Records manager + capstone / chapter gate

> Implement Records tracking and the chapter capstone per §1–§2, §5 and the `capstone` entry.
>
> - `RecordsManager` tracks cumulative Records and exposes the permanent income multiplier.
> - The capstone `unlock` is a `recordsCumulative ≥ 30` Condition (same evaluator, same type the event
>   availability uses). When it holds, unlock "Play the Backyard Party."
> - On capstone completion: grant 1 Roadie to a permanent pool but keep the Roadie allocation/replay
>   UI LOCKED (`roadieSystemUIUnlocked: false` — deferred to Chapter 2); fire the `storyBeatCapstone`
>   text; set an "advance to Chapter 2" flag (Chapter 2 content doesn't exist yet — just mark it).
>
> Goal: reaching 30 cumulative Records unlocks the capstone; playing it shows the story beat, banks
> the first Roadie (no allocation UI), and flags chapter advancement. Stop here.

✅ **Test & commit:** capstone unlocks at 30 Records via `recordsCumulative`; story beat fires; Roadie
banked; advance flag set.

---

## 8 — Events (Garage Jam Challenge)

> Implement the event system per §6.1 and the `events` array (garage_jam).
>
> - `EventManager` runs a self-contained challenge on a sandboxed economy snapshot: on entry, reset
>   the chapter economy to a fixed baseline for the event's duration (independent of the player's
>   Records).
> - Availability is a `recordsCumulative ≥ 1` Condition (available after the first demo). Each tier's
>   `goal` is a `currency` Condition evaluated by the shared evaluator.
> - garage_jam: debuff `automationDisabled` (generators paused, tap-only); timed and failable. Three
>   tiers (goal 500/2500/10000, timer 60/60/45, reward `tap_value_x1_25`/`_x1_50`/`_x2` applied via
>   `RewardManager`, scope permanent-in-chapter).
> - Failure/quit: reset that event's progress only; costs time, never permanent progress.
>
> Goal: I can enter garage_jam, play it tap-only against the timer at a fixed baseline, succeed for a
> permanent-in-chapter tap buff (from the rewards pool) or quit/fail for free, and repeat at higher
> tiers. Stop here.

✅ **Test & commit:** baseline reset on entry; tap-only; timer + fail/quit are cheap; tiers escalate;
reward applies from the pool.

---

## 9 — Save/load + offline earnings

> Implement persistence and offline earnings per §12 (rules 2, 4, 6) and the offline table in §9.
>
> - `SaveSystem`: serialize to JSON with a checksum; validate on load and reject/repair tampered
>   saves. Model the run block and permanent block as separate sections in the schema. The run block
>   holds Cash/Fans/Rehearsal balances, generator owned counts, buff-upgrade state, and bar progress;
>   the permanent block holds Records, `contentUnlock` effects, **flags**, and Roadies. An album
>   release clears the run block and writes the permanent block.
> - `OfflineEarnings`: on load, compute `productionPerSecond × min(secondsAway, cap) × rate` using
>   `DateTime` deltas, rate = 0.5, cap = 4 hours. Show a collect screen with the amount and a
>   placeholder "Double it" button (wire the actual ad later).
>
> Goal: closing and reopening restores state (including flags and bar progress); time away grants
> offline Cash at 50% capped at 4h; a tampered save is rejected. Stop here.

✅ **Test & commit:** state persists across restart; flags/bars restore; offline payout correct and
capped; checksum rejects edits.

---

## 10 — Chapter 1 playable pass (wire it end-to-end)

> Tie Chapter 1 together into a playable first-run experience using the `progression` stages in the
> JSON as the spine (Stage 0 First Notes → Stage 7 Backyard Party), driven by the section layout from
> 3.5.
>
> - Show the `storyBeatOpen` card on first launch and the `storyBeatCapstone` at the capstone.
> - Ensure the staged reveal reads cleanly — each stage a flag/Condition drives a section or module in:
>   tap-only → first gear (`the_band` section at 100 Cash earned) → Fans (`fans` flag) → Rehearsal +
>   covers (`covers` flag) → Cut a Demo (`album` flag, target ~5 min to first demo) → repeat →
>   Garage Jam available → capstone at 30 Records.
> - Minimal but legible UI, laid out through the module registry: current Cash/Fans/Rehearsal/Records,
>   generator rows, upgrade list, cover bars, Release button, event entry, collect screen. Use a
>   `NumberFormatter` for big-number display (1.23K / 4.56M / etc.).
> - Make the currency header data-driven: it currently names its currencies through hardcoded ids
>   (`GameManager.CashCurrencyId` / `FansCurrencyId` / `FansUnlockFlagId`, read by
>   `CurrencyHeaderModule` and `TapModule`). Replace those UI consts with display driven by the
>   chapter's revealed currencies, so a chapter with different currencies needs no UI code change.
>   (The fan SYSTEM already takes its currency/flag from the chapter's `fans` config — this is the
>   remaining UI half.)
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
  Because 3.5 made conditions, flags, rewards, bars, and layout fully data-driven and
  Addressables-discovered, Chapter 2 should be mostly new assets + JSON, not new systems.
- **Monetization SDKs** last (ads mediation + IAP), once the game is fun — wire the "Double it" and
  Encore/Backstage Pass placements that are already stubbed.

Keep `garage-band-idle-design.md` as the source of truth; when a decision changes while building,
update the doc so it and the code don't drift.
