# Garage Band Idle — Claude Code build prompts (Chapter 1 → playable)

Feed these to Claude Code **one at a time**, in order. After each: run it in the Unity Editor,
confirm the stated goal, then `git commit`. Don't move to the next slice until the current one works.

**Setup assumptions:** empty Unity 6000.5.4f1 2D project created in Hub; `git init` done;
`garage-band-idle-design.md` and `chapter-01-garage.json` sitting in `/docs`; Claude Code opened in
the project root. The design doc is the source of truth — every prompt references its sections.

Build order and why: each slice depends on the ones before it (offline earnings need the real-time
tick; prestige needs the currency block split; the content-unlock upgrades are what reveal
fans/covers/album). Building bottom-up keeps a break isolated to the slice you just added.

**Progress marker:** slices 0–5, **5.4**, **5.5**, **5.6**, **5.7**, **6**, **6.5** and **7** are already built and tested. Slice **3.5** is a dedicated consolidation pass
that establishes the cross-cutting foundations — a single `Condition` type + evaluator, one flag
registry for all progressive reveal, full-Addressables ScriptableObject discovery, the rewards pool,
data-driven sections/modules, and `isBandmate` — and **retrofits slices 1–3 onto them**. These are
foundations that touch code already written, so they are introduced explicitly here rather than
pretended to be forward-only. Slices 4–10 assume 3.5 is in place and build on it. Slice **5.4**
establishes the production-config shape (§12 rule 13: every flat-rate currency source lives on its
producer — generators and the Jam module — never on the currency) and retrofits slices 1–5 onto it.
Between 5.4 and 5.5 sit four **audit-driven normalization passes**, committed but not written as
slices because each removed an accident rather than adding a capability: **A** — a purchase or unlock
latches before it grants, so a notification always finds the latch already set (state, then notify);
**B** — upgrade payloads and rewards grant through one `GameEffect` family with one importer
vocabulary; **C** — an effect's lifetime is declared exactly once, by the fact that owns it (§12 rule
11); **D** — condition evaluation is invalidation-driven, not polled: `ConditionContext` holds the
aggregate dirty signal and one post-mutation seam settles every operation (built as
`GameManager.Settle()`; 5.5 moved it to `EconomyContext.Settle()`). Find them in the
git log between `023bd47` and `dfded84`; slices 5.5 onward assume all four.
Slice **5.5** established the economy-context boundary from the design's multi-economy revision
(§12 rule 12) and retrofitted slices 1–5 onto it: one permanent pool plus one frontier
`EconomyContext` built from a recipe, `ICurrencies`/`CurrencyRouter` resolving an id to its owning
pool at construction, and re-projection from facts as the only way a modifier comes into existence
(`ModifierSystem.ResetRunScoped()` is deleted; `ResetGranted()` is total). Slice **5.6** finished 3.5's reveal
work by retiring the last bare `revealFlag` fields, so every progressive reveal is a Condition asked
through the one evaluator (§12 rules 8 and 9): `BarGroupDefinition.VisibleWhen` and
`FansConfig.ActiveWhen` are Conditions, `FanSystem` constructs after the condition context, the
importer refuses a stale `revealFlag` key, and `album.revealFlag` is deleted rather than converted
(no importer DTO ever read it), so slice 6 reveals Release through its section's `visibleWhen` like
every other module. One cost it carries forward: a null Condition means "no gate", so a chapter that
omits `activeWhen` accrues fans from the first frame where an empty flag id used to be reported.
Slice **5.7** retired `FanSystem`: fan accrual is a tick config on a module-less `band` producer plus
a derived `FanRate` add, so §9's "fans never idle-pay" holds because of who holds the config rather
than because a tick was left out of a list, and the gate names the band (`ownedCount drummer ≥ 1`)
instead of relying on which upgrade sets a flag. It also generalized `composes` beyond `tapValue`,
with `ProductionConfig.IsComposable` as the single home for what a config may compose — a rule
`ProductionSystem` and `ContentValidator` had briefly disagreed about, which is what made Chapter 1's
own content fail boot validation. Slices 6–10 assume all four.
Slice **6.5** is a consolidation pass, audit-driven like 5.4–5.7, that establishes the economy
snapshot/seed contract (one restore order, one state type, recipe-driven filtering), makes effect
replay behavior a property of the effect TYPE so a payout cannot be paid twice by any path, and lets a
module be told which definition it presents — which cashed in the producer/module binding that had
been authored and dead since 5.4, so a tap fires its own producer rather than every tap config in the
chapter. The module declares the FAMILY it requires, and that one answer settles both directions:
whether an entry's id resolves, and whether a tap producer is presented at all — so neither check can
be satisfied by an id that happens to belong to some other registry. It also made the capstone, story beats and Roadies into ordinary content: `CapstoneConfig`
holds the sole authored chapter gate (the scalar `capstoneRecordsGate` is deleted), story beats became
a definition type with a chapter id list instead of two inline strings, and Roadies is a global
currency in the existing permanent group. Slices 7–10 assume all of it: 7 consumes `CapstoneConfig`
instead of inventing a gate, and 9's load is that same restore with a wider fact set. It changed no
observable gameplay. One prediction it made has been overtaken: 6.5 expected slice 8 to build an event
sandbox by SEEDING a context, and **7.5 deletes that machinery outright** — an event is a component on
its host scope. Read slice 8 as written, not as 6.5 anticipated.

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
> `currency` (current balance ≥ `value`), `currencyEarnedTotal` (lifetime earned ≥ `value`),
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
>   (id, name, `revealFlag`, a polymorphic fill behavior mapped from the JSON's `fillMode` +
>   `delivery` pair at import, `scope`, ordered bar list), discovered by Addressables label.
> - The Chapter 1 fill behavior (`fillMode: "perBar"` + `delivery: "continuous"`, player-directed):
>   each bar tracks its OWN accumulated progress and the player chooses which bar to pour Rehearsal
>   into; a fill action spends from the
>   shared Rehearsal pool into the selected bar. Bars are independent, NOT cumulative thresholds on one
>   counter (totals 120 / 300 / 600 = 1020 to finish all three). The fill logic reads `fillCurrency`
>   and works for any currency.
> - On bar completion, apply its `reward` via `RewardManager` (Ch1 rewards are `fanRateMultiplier`,
>   stacking multiplicatively on fan rate). The bar's reward reference is the authority for whether
>   completion grants a fan-rate bonus; there is no second switch in the Fans config.
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

## 5 — Buff upgrades (any-currency gating via Condition)  ✅ done

> Implement the run-scoped buff upgrades per §4 and the `type: buff` entries in the `upgrades` array.
> Gating and discovery already exist from 3.5 — this slice adds the buff payloads and run scope.
>
> - Load `UpgradeDefinition` assets from the ContentDatabase (Addressables). Each buff's `gate` is a
>   `Condition` evaluated by the shared `ConditionEvaluator` — no per-currency branches. Its `cost` is
>   `{ currency, amount }` charged through `CurrencyManager`.
> - Implement payloads: `tapValueAdd`, `generatorOutputMultiplier` (target generator by `id`),
>   `currencyPerSecMultiplier` (multiplies production of the currencies its `affects` list names, the
>   same rule `constants.recordBuff.affects` follows — nothing unlisted is touched). Payload targets are
>   referenced by `id`; adding a payload type later is one handler, not a rewrite. Validate gate/target
>   ids on load.
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

## 5.4 — CONSOLIDATION: production configs (sources own production; currencies are pure state)  ✅ done

This slice adds no new gameplay. It moves "how a currency is earned" off `CurrencyDefinition` and
onto the producers, per the design's production-config revision pass (§12 rule 13), and **retrofits
slices 1–5 onto it** — before 5.5 bundles the earn machinery into the economy context in the old
shape. Do it as one slice, then confirm slices 1–5 still play identically.

> Read `/docs/garage-band-idle-design.md` — §3, §6, §9, and §12 rule 13 (the production-config
> revision pass). This is a refactor/foundation pass; **do not change observable gameplay.** Build
> these foundations and retrofit slices 1–5 onto them:
>
> **1. `ProductionConfig`.** `{currencyId, amount, trigger: tick | tap, gate: Condition, composes}`.
> The gate is an ordinary Condition evaluated by the shared evaluator (none = always on) — the
> bespoke `revealFlag` string dies with the earn config. `composes` declares which rule-11 target
> scales the config's output (`tapValue` on the Jam cash entry; absent = the raw amount) — a
> declaration, never an inference from a currency name. A negative amount, an unknown trigger, or an
> unknown composes is refused at import and reported at boot, the same fail-closed rule the earn
> config had.
>
> **2. The Jam module owns the tap.** Extend the chapter JSON and importer: a `producers` array
> authors the Jam producer — its module address (`module/tap`) plus its production list: cash
> 1/tap (retiring `constants.tapBaseValue`), rehearsal 2/tap and rehearsal 1/sec both gated on
> `flagSet covers` (retiring the `earn` block on the rehearsal currency entry). Producer definitions
> are generated assets discovered by an Addressables label like every other content kind. Currency
> entries in `currencies` become `{id, group}` only; delete `EngagementEarnConfig`, and the importer
> **refuses** an `earn` block (stale JSON) rather than silently ignoring it.
>
> **3. Generators expose the same shape.** `GeneratorDefinition.produces`/`baseOutput` reads as a
> tick-triggered production config scaled by owned count; the generator JSON is unchanged. Generators
> are the only idle-eligible holder (§9): slice 9 reads generator production per second only, so
> module-held configs never idle-pay by construction — do not add an idle flag to the config.
>
> **[rev]** The conclusion survives 7.4 and the "no idle flag" instruction still stands, but the reason
> changed: idle-eligibility is not a holder kind at all. Every currency's *rate* accrues while a scope is
> disabled, a *yield* cannot because nothing fires it, and bar progress cannot because filling is a tick
> drain rather than production (§9). Build slice 9 against that, not against this paragraph.
>
> **4. `ProductionSystem` replaces `EngagementEarnSystem` and `TapSystem`.** On tick, fire the
> tick-triggered module-held configs whose gates hold; on Jam, fire the tap-triggered ones. It keeps
> what the UI reads today: the composed tap value and its change event, and the per-second/per-tap
> rate displays. The modifier vocabulary is unchanged: the Jam cash config composes the existing
> `TapValue` target (stage_presence and event-tier buffs land exactly as before); the rehearsal
> configs compose nothing new.
>
> **5. Retrofit.** GameManager wires `ProductionSystem` in place of the two it replaces, and `Jam()`
> becomes "fire the tap trigger" instead of `Currencies.Add(CashCurrencyId, ...)`. Bars are untouched
> (they drain a currency by id, whoever produced it), and the fan system is untouched — its rate is a
> formula over band size, not a flat amount, so it is NOT a production config (§12 rule 13). Extend
> the boot validation pass (config currency ids resolve, gates validate, amounts non-negative) and
> update the editor tests.
>
> Goal: slices 1–5 play exactly as before, but what a tap or a tick yields is authored on the
> producer — the Jam module and generators — and a currency definition is pure state. Stop here.

✅ **Test & commit:** Chapter 1 plays identically end-to-end (tap pays 1 Cash; the `covers` flag
starts Rehearsal at 1/sec + 2/tap; stage_presence and tap buffs still apply); the importer refuses a
currency `earn` block; the test suite is green.

---

## 5.5 — CONSOLIDATION: the economy context (permanent pool, context factory, focus lifecycle)  ✅ done

This slice adds no new gameplay. It establishes the economy-context boundary from the design's
multi-economy revision passes and **retrofits slices 1–5 onto it**, so that slice 6's release is an
operation on a context, slice 8's event sandbox is a second instantiation of the same machinery, and
Chapter 2+ replay economies need no new architecture. Do it as one slice, then confirm slices 1–5
still play identically. *(Written before 7.5, which turns the context into a scope and deletes the event
sandbox — an event became a component on its host scope. The retrofit this slice performed still stands;
only its two forward predictions were overtaken.)*

> Read `/docs/garage-band-idle-design.md` — §3, §9, and §12 rules 6, 7, 11, 12 (the economy-context,
> idle-accrual, and per-chapter frontier revision passes). This is a refactor/foundation pass; **do
> not change observable gameplay.** Build these five foundations and retrofit slices 1–5 onto them:
>
> **1. Group-declared placement + the permanent pool.** `CurrencyManager` stays ONE class with no
> scope concept inside it; lifetime comes from who creates an instance. Add a placement flag to
> `CurrencyGroupDefinition` (global vs chapter) beside `resetsOnAlbumRelease`. At boot, build a
> startup instance holding every currency whose group is global — Records today, Roadies later —
> created once, never reset by any run operation, and destined to be the permanent save block
> (slice 9). A group that is both run-scoped and global is incoherent ("resets on whose release?"):
> boot validation reports it. That is the only enforcement point, and deliberately so — currency
> group assets are hand-authored (`[CreateAssetMenu]`, `ScriptableObjects/CurrencyGroups/`), never
> generated from the chapter JSON, so there is no import path to refuse the combination on. Do not
> make groups importable to gain one; the case that matters is an existing asset with both flags set,
> which is exactly what boot validation sees.
>
> **2. The chapter's currency roster is authored.** A chapter declares its full local currency list:
> `cash` and `fans` join `rehearsal` in the chapter JSON's `currencies` array, as the bare
> `{id, group}` entries 5.4 left them (production lives on producers, never on a currency entry).
> `ChapterDefinition.CurrencyIds` and the importer that fills it **already exist** — 5.4 left the
> field carrying a one-entry roster, and `ContentValidator` already checks that every id in it
> resolves. So this foundation is two things only: add the two missing JSON entries, and make the
> pool read the roster. What has no implementation yet is the reading: `CurrencyManager` is still
> constructed from `Database.Currencies.All` in `GameManager.Awake`, which is the line that has to
> die. A context builds its pool from the roster, never from `Database.Currencies.All`.
> Validate at construction: every roster id resolves, no roster id belongs to a global group, and no
> id exists in both the pool and the permanent pool — shadowing is an error, never a fall-through.
>
> **3. `EconomyContext` + factory/recipe.** Bundle the per-economy systems — currency pool,
> generators, upgrades, bars, fans, production (5.4's tap + trickle system), flags, modifiers,
> condition context — into
> an `EconomyContext` built by a factory from (chapter definition, ContentDatabase, permanent pool,
> recipe). The context ends every top-level operation (tick, tap, purchase, and later release,
> restore, focus-gain) with a **post-mutation evaluation step** — so condition-dependent
> published values (the tap value today; anything gate-driven later) re-evaluate exactly once, after
> the whole mutation settles, and a new operation cannot forget to. **That seam already exists** as
> `GameManager.Settle()` (pass D): the condition-invalidation drain plus the tap-value refresh, called
> from the tick, the tap, and each purchase. This is a **move** into the context, not a build — and a
> discarded context must `Dispose()` its `ConditionContext`, which holds live subscriptions to the
> systems it reads. The recipe declares which global derivations register (§12 rule 12): the frontier recipe
> registers the Records income modifiers (reading the permanent pool through the chapter's
> `recordBuff`); a later event recipe will not — that absence IS slice 8's fixed baseline, so the
> factory takes the recipe now even though only the frontier recipe exists yet. Inside the context,
> currency resolution is a construction-time ownership map over (pool + permanent pool): consumers
> keep one lookup and one aggregated balance-changed subscription.
>
> **The projection is the only door a modifier enters through** (§12 rule 6, settled 2026-08-04).
> Nothing outside the recipe grants, and no boundary edits the store in place: **delete
> `ModifierSystem.ResetRunScoped()`**, and make a reset mean "reset the facts, re-run the projection".
> Assert the property that buys: every fact class producing a modifier is walkable at construction, so
> a fact class added later cannot be silently skipped — that assertion is what replaces the safety the
> single central reset call used to provide. Eleven test call sites currently call
> `modifiers.ResetRunScoped()` to simulate a run reset — `FansAndContentUnlockTests` 3,
> `ModifierSystemTests` 3, `BarsAndRehearsalTests` 2, `UpgradePayloadTests` 2, `EconomyMathTests` 1 —
> and they move to resetting facts and re-projecting, which is what slice 6's release will actually do.
> Migrating eleven is the whole job; the counts are per file so a finished migration is checkable
> rather than estimated. `UpgradeSystem.ResetRunScoped()` **stays** — it clears purchase latches,
> which are facts — so the three `upgrades.ResetRunScoped()` calls in `UpgradePayloadTests` stay
> untouched, and the two receivers must not be confused when counting.
>
> **4. Focus lifecycle skeleton.** A context is constructed → focused ⇄ unfocused → discarded, with
> exactly one focused context at a time; only the focused context receives the tick (GameManager
> routes it). Record a last-interaction timestamp on focus loss — the value idle earnings (slice 9)
> will read. No idle payout logic yet.
>
> **5. Retrofit.** Move the slice 1–5 runtime construction out of `GameManager.Awake` into the
> factory. GameManager keeps the database, tick routing, focus switching, and the single frontier
> context; the UI's `ChapterContext` points at the context rather than at GameManager's properties.
> The hardcoded UI display ids (`CashCurrencyId` etc.) remain as declared slice-10 debt — leave them.
>
> Finally, extend the boot **validation pass** to the new shape (roster and placement checks above)
> and update the editor test fixtures to construct contexts through the factory.
>
> Goal: slices 1–5 play exactly as before, but the runtime is one permanent pool plus one frontier
> `EconomyContext` built from a recipe — the shape every later slice instantiates instead of
> rewrites. Stop here.

✅ **Test & commit:** Chapter 1 plays identically end-to-end; boot validation catches a misplaced
currency (a run-scoped global group, a roster id in a global group, a shadowed id); the test suite
is green with context-based fixtures.

---

## 5.6 — CONSOLIDATION: reveal is a Condition (retiring the last `revealFlag` fields)  ✅ done

This slice adds no new gameplay. It removes the last bare reveal-flag-id fields from the definition
assets, so progressive reveal has exactly one vocabulary — a Condition evaluated by the shared
evaluator (§12 rules 8 and 9) — rather than a Condition for sections and gates plus a hardcoded flag
id for three other things. 5.4 already did this once, where the production config's gate replaced
"the bespoke `revealFlag` string" the earn config carried; this finishes the same move on the
remaining sites. It sits here for two reasons: 5.5 has just rewritten every system's construction
into the factory, and slice 6 is the first thing that would give the currently-unread
`album.revealFlag` a consumer — after which this is four sites instead of three.

> Read `/docs/garage-band-idle-design.md` — §3, §6, and §12 rules 8 and 9 (one `Condition` type
> evaluated by one evaluator; one flag registry for all progressive reveal, with **no parallel reveal
> paths**). This is a refactor/foundation pass; **do not change observable gameplay.** Chapter 1
> authors a `flagSet` condition at every site below, so every reveal fires on exactly the flag it
> fires on today.
>
> **1. The three surviving fields.** `BarGroupDefinition.revealFlag` and the chapter `fans` block's
> `revealFlag` become Conditions. The `album` block's `revealFlag` is **deleted, not converted**: the
> importer does not read the album block at all today, so the field has no consumer, and slice 6
> reveals the Release button through its section's `visibleWhen` the way every other module is
> revealed. Do not let slice 6 make that field live.
>
> **2. Fan activation is a gate, not a flag lookup.** `FanSystem.Active` evaluates a Condition
> through the condition context instead of `_flags.IsSet(_config.RevealFlagId)`. This is a *gameplay*
> gate — it decides whether fans accrue at all — so `FanSystem` now constructs after the condition
> context, exactly the reordering `ProductionSystem` took in 5.4. Chapter 1 authors
> `flagSet fans`, so accrual still starts when `play_for_crowd` applies; what changes is that fan
> activation can gate on a balance or a completed bar like every other gate in the game.
>
> **3. Bar-group reveal is a gate.** `BarListModule` evaluates each group's Condition instead of
> comparing flag ids, and its `FlagSet` subscription collapses into the condition context's `Settled`
> signal (the same collapse `ChapterScreen` and `UpgradeListModule` already made); `BarProgressChanged`
> and `BarCompleted` stay, since those are bar display rather than a gate. The pool readout's
> `IsRevealed` becomes a Condition evaluation per owning group — keep the existing capability that two
> groups with different reveal gates can share one section, and keep the rule that a pool renders only
> while at least one owning group is revealed.
>
> **4. Importer + validation.** The JSON keys are named for the facts they express, not shared: a bar
> group takes `visibleWhen` (it is visibility, the same key a section uses) and the fans block takes
> `activeWhen` (it is accrual). The importer **refuses** a `revealFlag` key as stale JSON rather than
> silently ignoring it, the same fail-closed rule 5.4 applied to a currency `earn` block. In
> `ContentValidator`, the two `ValidateFlag` calls for these sites become ordinary Condition
> validation, which already reports an unresolvable id and a non-positive threshold — a reveal gate
> gets the same checks every other gate gets, which is the point.
>
> **5. Retrofit.** Update the editor fixtures (`TestContent.MakeBarGroup`, `MakeChapter`) to author
> Conditions, and the two content tests asserting a flag id (`chapter.Fans.RevealFlagId`,
> `group.RevealFlagId`) to assert the Condition instead. **Out of scope:** the hardcoded UI display
> ids in `CurrencyHeaderModule` (`CashCurrencyId`, `FansCurrencyId`, `FansUnlockFlagId`) — 5.5
> declared those slice-10 debt, they are independent of these fields, and they stay.
>
> Goal: Chapter 1 plays exactly as before, but no definition asset carries a reveal-flag id — every
> reveal in the game, from a section to a bar group to fan accrual, is one Condition asked through one
> evaluator. Stop here.

✅ **Test & commit:** Chapter 1 plays identically end-to-end (buying a Drummer still reveals Fans and
starts accrual; the `covers` flag still reveals the Rehearsal readout and the cover bars); the
importer refuses a `revealFlag` key; no definition asset or config struct holds a bare reveal-flag
id; the test suite is green.

---

## 5.7 — CONSOLIDATION: fan accrual is production (retiring `FanSystem`)  ✅ done

This slice adds no new gameplay. It removes the last currency source that produces outside the
production-config vocabulary 5.4 established: fan accrual is its own system, with its own tick, its
own rate math, and its own activation gate, none of it reachable through the mechanism every other
flat-rate source uses. It sits here for two reasons. §9's promise that fans never idle-pay currently
rests on `FanSystem` being a separate tick that slice 9 must remember to exclude — a fact held in a
comment rather than enforced by the architecture — and rule 13 already says the holder is what
decides idle-eligibility. And slice 6's release walks each system's facts, so deleting a system is
cheaper before that walk is written than after. `FanSystem` is also the only place a currency's rate
is computed by a bespoke formula rather than composed from modifiers, which is why the fan rate is
the one rate no reward, buff or event tier can reach without naming `FanRate` specifically.

> Read `/docs/garage-band-idle-design.md` — §3, §6, §9 (idle pays only generator-held configs), §11
> (fan rate must not be shortcut by time away), and §12 rules 11 and 13. This is a
> refactor/foundation pass; **do not change observable gameplay.** Chapter 1's fan rate must read
> 0.22/s with one Drummer owned and compose cover-bar rewards exactly as it does today.
>
> **1. The base rate becomes a module-held production config.** The chapter's `baseFansPerSec` moves
> into a `ProductionConfig`: `{ currency: fans, amount: 0.2, trigger: tick, composes: fanRate, gate:
> ownedCount drummer >= 1 }`. Author it on a **new `band` producer** rather than on the existing jam
> producer, and make `ProducerDefinition.ModuleAddress` **optional**, meaning "nothing presents this
> producer; it is a passive source." The alternative — hanging fans off the jam producer — was
> considered and rejected: it works behaviorally (configs fire from the chapter's producer list, not
> from which modules are visible) but it encodes a lie, and it breaks the first time a chapter has
> fans without a Jam button. Module-held is the whole point: §9's boundary is the holder, so a
> passive source that is not a generator can never idle-pay, with no per-config flag to author or get
> wrong.
>
> **[rev] Steps 2 and 3 below were built here and superseded by 7.4**, which deleted the target enum
> and `ModifierOperation.Add` with it: a flat bonus is a CONTRIBUTION to the number it raises, so the
> per-bandmate bonus is a fans rate line on each bandmate generator rather than a derived modifier, and
> a config composes through a selector rather than a target key. Read them as the state 7.4 started
> from, not as instructions.
>
> **2. The per-bandmate bonus becomes a derived modifier.** Add `BandmateFanRateModifier :
> DerivedModifier` — `Target = ModifierTargetKey.Global(ModifierTarget.FanRate)`, `Operation = Add`,
> `Value = perBandmateOwnedBonus × bandmateCount`, reading `GeneratorSystem` live. Register it in
> `EconomyContextFactory` beside the Records modifiers. This is `RecordsIncomeModifier`'s exact shape
> and for the same reason: nothing grants it, it is on from boot, and it carries no scope because its
> lifetime is its source's (rule 11). `ModifierComposition` is `(base + adds) × multipliers`, so the
> composed result is `(0.2 + 0.02n) × coverRewards` — the identity `FanSystem.RatePerSecond` computes
> today. **Prove that equality with a test before deleting anything.**
>
> **3. `ProductionSystem` stops hardcoding TapValue.** Two sites: the constructor guard that refuses
> any `composes` but `None`/`TapValue`, and `Composed()`, which applies only the TapValue target.
> Both generalize to `modifiers.For(ModifierTargetKey.Global(config.Composes))`. The importer's
> `ToComposes` gains `fanRate`. This deletes a special case rather than adding one — the restriction
> was a fossil of TapValue being the only composing target that existed in 5.4, exactly the shape
> `9ab9b75` removed from the effect vocabulary.
>
> **4. Fans in `recordBuff.affects` is refused.** One guarantee weakens in this slice and must be
> replaced rather than dropped. Today it is *impossible* for the Records income multiplier to touch
> fans, because `FanSystem` only ever applies `FanRate`; afterward, fan production flows through
> `ProductionSystem` and stays untouched only because `recordBuff.affects` happens to list `["cash"]`.
> Adding `"fans"` there would let Records inflate the fan rate and time away shortcut the Records
> payout — the coupling §11 forbids and the same failure `ContentValidator` already guards by
> requiring fans sit in a group that resets on release. So `ContentValidator` **refuses the chapter's
> fans currency in `recordBuff.affects`**, reported as the §11 violation it is. A compile-time
> impossibility becomes a content check, and an unchecked content mistake is not an acceptable trade.
>
> **5. `FanSystem` deletes, and `FansConfig` keeps only what is not production.** Consumers:
> `CurrencyHeaderModule` reads `Production.RatePerSecond(fansCurrencyId)`; `EconomyContext` drops the
> `Fans` property, the `Fans.Tick(seconds)` call, and `Fans` from its system list. `Active` and
> `BandmateCount` have no runtime callers outside the class — `BandmateCount` moves into the new
> modifier, which is the only thing that still needs it. `FansConfig` keeps `currencyId` and
> `perBandmateOwnedBonus` (the modifier's tuning) and loses `baseFansPerSec` and `activeWhen`, both of
> which are now production. Note what 5.6 bought here: because the gate is already a Condition, this
> is a **relocation onto the config's `gate`**, not a conversion.
>
> **6. Importer + validation + retrofit.** The fans block's `baseFansPerSec` and `activeWhen` keys are
> **refused** as stale JSON — the same fail-closed rule 5.4 applied to a currency `earn` block and 5.6
> applied to `revealFlag` — and since a fans config is not skippable content, they report and the
> chapter still imports (the `constants.tapBaseValue` shape, not the currency-entry shape). Validate
> that a producer with no module address still declares production, since it has no other reason to
> exist. Retrofit the ~11 `FanSystem` constructions in the editor tests onto `ProductionSystem`, and
> the Chapter 1 content assertions about `chapter.Fans` onto the `band` producer's config.
>
> Goal: no system computes a currency's rate by a bespoke formula; fan accrual is a gated production
> config plus a derived modifier, and "fans never idle-pay" is true because of who holds the config
> rather than because a tick was left out of a list. Stop here.

✅ **Test & commit:** Chapter 1 plays identically end-to-end — fan rate reads 0.22/s with one Drummer,
cover-bar rewards still stack multiplicatively on it, and fans still start only once a Drummer is
owned; `FanSystem` no longer exists; the importer refuses `baseFansPerSec` and `activeWhen`; boot
validation refuses the fans currency in `recordBuff.affects`; the test suite is green.

---

## 6 — Album prestige (Cut a Demo)  ✅ done

> Implement the album prestige per §5 and the `album` + `cut_demo` entries in the JSON.
>
> - `cut_demo` (contentUnlock, `compound.all` = [`currency fans ≥ 50`, `barsCompleted(learn_covers) ≥
>   1`]) sets the `album` flag, which reveals the "Cut a Demo" (Release) button. The gate is evaluated
>   by the shared `ConditionEvaluator`.
> - On release, reset the RUN block, driven by data, not a name list: reset every currency whose group
>   has `resetsOnAlbumRelease = true` (Cash, Fans, Rehearsal), every generator's owned count, every
>   upgrade/bar whose `scope == run` (buff upgrades, cover bars). KEEP the permanent block — Records,
>   `contentUnlock` upgrade effects, **flags** (content stays revealed across demos), Roadies.
>   The **effects** half is not a reset at all (§12 rules 6 and 11): the release resets *facts* and
>   then re-runs the context's projection, which rebuilds the modifier store from whatever facts
>   survived. Do NOT reach into the modifier registry to strip the run-scoped grants and leave the
>   rest — that is a second mechanism for the same modifier set, and it can disagree with the load
>   path, which has only facts to rebuild from. What the release walks per-system is exactly the
>   *facts*: currency balances, owned counts, bar progress, and the buff-upgrade purchase latches.
>   `ModifierSystem.ResetRunScoped()` is deleted in 5.5; if it still exists when you reach this slice,
>   that means 5.5 was left incomplete — it is not permission to call it.
> - Write the release as an operation on the bundled economy context (design §12 rule 12), not as
>   GameManager code reaching at its own properties: the Records award writes the permanent pool, the
>   resets walk the chapter context's systems, and the same orchestration must later run unchanged
>   against other context instances (the event sandbox, replay economies).
> - Award Records = `floor((fansThisRun / 5) ^ 0.5)`. Each Record adds +2% to the permanent global
>   income multiplier (`1 + 0.02 × records`).
>
> Goal: at 50 Fans + 1 cover the `album` flag reveals Release; releasing resets the run (scope/group
> driven), grants Records, keeps flags/unlocks, and the next climb is visibly faster. Stop here.

✅ **Test & commit:** release resets run via scope/group flags, keeps Records/flags/unlocks, re-climb
faster; Records formula matches the examples in the JSON.

---

## 6.5 — CONSOLIDATION: the snapshot/seed contract, effect projection, parameterized modules  ✅ done

This slice adds no new gameplay. It exists because slices 7–9 each introduce state or grants that the
current code has no mechanism for, and three of them would otherwise each invent their own.

The load-bearing gap is the snapshot. Slice 8 builds its event sandbox as "a freshly constructed
economy context whose recipe projects the chapter's permanent-in-chapter facts only" — that is a
partial restore, and slice 9's load is the same operation with a wider fact set. Today
`EconomyContextFactory.Build` can only produce an EMPTY economy: bars and generators have restore
helpers, upgrade latches and flags have none, and `CurrencyManager` can set a balance but not the
lifetime-earned total. That last one is not a gap in the abstract. Both the capstone gate
(`RecordsCumulativeCondition`) and the entire permanent income buff (`RecordsIncomeModifier`) read
`GetEarned("records")`, so restoring the Records *balance* and nothing else would load a save showing
the right number with the capstone re-locked and the multiplier back at 1.0.

The second gap is that re-projection re-applies whole payloads. **[rev]** (As built, this gap closed
structurally rather than through the projection filter this preamble originally motivated.) Safe
re-application is now what membership in `GameEffect` MEANS — grant a modifier, set a flag — and
slice 7's "grant 1 Roadie", the first payout in the game, is not an effect at all: payouts are
`GameAction`s on the player-action moment that earns them, unreachable from every release, load, and
projection. The dangerous path was never the projection; it is RE-ACQUISITION, which is re-entrant
by design: `ResetRunScoped` clears run-scoped latches at each release and `EvaluateContentUnlocks`
re-applies any unlock whose gate still holds. That is how the second-run reveal walk works, and it
is why the auto-apply path holds no actions to run.

The third gap is smaller but it is what makes two "later" items later forever: a module cannot be
told which definition it presents. `IChapterModule.Initialize` takes only a `ChapterContext`, so
`ProducerDefinition.ModuleAddress` is authored, validated, and dead — `FireTap()` fires every tap
config in the chapter — and a story beat cannot be a content piece because two cards would share one
prefab with no way to differ. One parameter fixes both.

> Read `/docs/garage-band-idle-design.md` — §5, §6.1, §9, §12 rules 6, 11, 12 and 13. This is a
> refactor/foundation pass; **do not change observable gameplay.** Chapter 1 must play exactly as it
> does today: same tap value, same fan rate, same reveal order, same release behavior and re-climb.
> The story-beat and Roadies content added here is not yet presented by any UI.
>
> **1. Split state ownership, then give restore its missing primitives.** The permanent pool's
> currencies (Records, Roadies) belong to the pool's owner and are captured exactly once;
> `EconomyContext.CaptureLocalState()` covers this context's own pool plus its generators, upgrade
> latches, bars and flags, and must never reach through the router into the permanent pool. Add
> `CurrencyManager.Restore(id, balance, earnedTotal, notify)`, `UpgradeSystem.RestoreApplied(ids,
> notify)`, `FlagSystem.Restore(flagIds, notify)`, and a `notify` parameter on the existing
> `GeneratorSystem.RestoreOwned` / `BarSystem.RestoreProgress` **without changing their current
> defaults**. Restore is REPLACEMENT, not merge: a flag, latch, count, bar or balance absent from the
> snapshot is cleared, zeroed, or returned to its starting value. Modifiers are never captured — they
> are always projected.
>
> **2. Make restore atomic.** `EconomyContext.Restore` runs one order and nothing else may: restore
> raw facts silently → `Conditions.MarkDirty()` → project with modifier notifications deferred →
> `Settle()` → replay the terminal notifications inside a condition-invalidation suppression scope →
> finish with the condition context clean. `MarkDirty` is mandatory, not decorative: a second restore
> into a settled context finds `_dirty == false` and `Drain` returns having evaluated nothing, so
> nothing may rely on the fresh-context default. Silencing only the fact primitives is not enough —
> `ResetGranted` and each re-`Grant` fire `Modifiers.Changed`, which `GeneratorListModule` subscribes
> to, so the projection is deferred too. The replay publishes balances, owned counts, bar progress and
> modifier targets. It does NOT publish `UpgradeApplied`/`UpgradeCleared`, because a
> restored latch is not an acquisition — the same reason `ProjectModifiers` already refuses to re-fire
> them — and it does NOT publish flags: a flag is only ever READ through a Condition, so everything
> gating on one already re-asked at the settle, and `FlagSet` means "just latched", which a restored
> latch is not. Use suppression rather than a second drain, or "which `Settled` is the terminal one" has two
> answers. This is scoped restoration behavior, NOT a general transaction system for every operation.
>
> **3. Build every economy through the same door.** `Build(database, chapter, permanentPool, recipe,
> seed)` applies the seed through `Restore`, so the order in 2 has exactly one implementation. The
> recipe gains permanent-pool ROUTING beside its projection filter: `Shared` for the frontier,
> `Isolated` for an event sandbox, which gets a private pool from `BuildPermanentPool` so a stray
> `Add("records")` can only reach the sandbox's own. State the consequence rather than leaving it to
> be discovered: inside an isolated sandbox `recordsCumulative` reads zero, and that IS the fixed
> baseline. Seed filtering is recipe-driven over ONE state type — empty for a new run,
> permanent-in-chapter facts only for a sandbox (no run latches, flags, counts or balances), the whole
> local snapshot for a load — so `frontier.CaptureLocalState()` filtered by recipe is how a sandbox is
> built, and the sandbox path and the load path are the same mechanism.
>
> **4. Effects re-apply; awards never replay — by category, not by filter.** **[rev]** (This step
> originally specified `EffectProjection { Projectable, OneShot }` with `ApplyOnAcquisition`/`Project`
> entry points and a `ContainsOneShot` content rule; the GameEffect/GameAction split replaced all of
> it — the text below is the design as built.) `GameEffect` is re-applicable state by definition of
> membership: `Apply` and `Validate` are its whole contract, and every rebuild boundary — release,
> load, reprojection — re-runs `Apply` on whatever the surviving facts carry. `GrantModifierEffect`
> re-grants over a store the projection just cleared; `SetFlagEffect` re-latches idempotently, and
> that staying true is load-bearing, not convenient — the release depends on the rebuild re-asserting
> flags whose setters' latches survived, and the importer and boot validation enforce the corollary
> (a run-scoped flag needs a run-scoped setter). Give it a regression test of its own.
> `CompoundEffect` mirrors `CompoundCondition` and owns the only child iteration anywhere; consumers
> hold ONE `GameEffect` and never learn whether it is a group.
> One-shot awards are NOT effects. `GameAction` — `Execute(EffectContext)`, `CanExecute`, `Validate`,
> and no scope parameter, because a one-shot has no lifetime to declare — is its own family, with
> `GrantCurrencyAction` its first member. Action lists live only on player-action moments (a bought
> buff's purchase, an event tier's clear, the capstone's completion) and are executed by that
> operation alone. No release, load, or reprojection path holds an action, so a payout paid twice is
> INEXPRESSIBLE: there is no projection filter, no `OneShot` enum, and no content rule refusing
> payouts on content unlocks, because the type system refuses them first — a currency award cannot
> sit in a payload. The one remaining checkable mistake is the inverse: actions on a content unlock
> would silently never pay (it is never bought), which `ContentValidator` reports.
>
> **5. A module can be told which definition it presents.** `SectionDefinition`'s module entries
> become `{address, definitionId?}` pairs and `IChapterModule.Initialize` takes the id (five of the
> six existing modules ignore it); `ChapterScreen`, the importer, and the duplicate-module validator
> check (keyed on address+id now) follow. Cash in immediately on the binding that was already
> authored: `module/tap` carries `definitionId: "jam"`, and `ProductionSystem.FireTap(producerId)` /
> `EconomyContext.Jam(producerId)` fire that producer's configs instead of every tap config in the
> chapter. Chapter 1 has one tap producer, so this changes nothing observable — and it is the whole
> reason a second one is not a rewrite.
>
> **6. The capstone and story beats become ordinary content.** Add `CapstoneConfig` to
> `ChapterDefinition` beside `AlbumConfig`: `id`, `displayName`, `unlock` Condition,
> `completionFlagId`, `onComplete` effect. The JSON's `capstone.unlock` becomes the sole authored gate
> — import it through the same condition parser every other gate uses, and DELETE
> `chapter.capstoneRecordsGate` from the JSON, the asset, the importer and its validator check rather
> than reading both. That check needs no bespoke replacement: `ValidateThreshold` already reports a
> non-positive threshold on all five threshold condition types, `ThresholdIsMet` already fails closed
> on one, and `CompoundCondition.Validate` already refuses an empty compound — so validating the
> authored `unlock` like any other gate covers it. What none of that covers is a NULL `unlock`, which
> by this codebase's own convention means "no gate" and would offer the capstone at boot, so that is
> the one new check. **[rev]** `capstone.onComplete` splits by category: `grantRoadies` imports as a
> one-shot `GrantCurrencyAction("roadies", 1)` in `CapstoneConfig.Actions`, and `completionFlag` is
> the config's own declaration (`completionFlagId`) — the completion OPERATION latches it (slice 7),
> no `SetFlagEffect` copy is built, so payload and declaration cannot disagree. That one flag is both
> the completion and the advance fact (two would be two facts for one event), declared
> permanent-in-chapter in the chapter's `flags` array.
> Story beats stop being two inline strings on the chapter and become `StoryBeatDefinition { id, text,
> readFlagId? }` — a definition, a `ContentLabels` entry, a `ContentDatabase` registry and a
> `chapter.storyBeatIds` list, which is the leg generators, upgrades and bars already have and beats
> lacked. They carry NO unlock and NO scope: reveal is their section's `visibleWhen`, exactly as it is
> for the Jam button, which is what 5 makes possible. `fireStoryBeat` is never imported — a beat is
> pulled by its section's condition, not pushed by the capstone. Retire
> `ChapterDefinition.StoryBeatOpen`/`StoryBeatCapstone` and move Chapter 1's two beats into assets.
> Do NOT author the beat sections or build a `module/story-beat` prefab here: no such prefab exists
> and boot validation fails a section whose module address resolves to no prefab. Slice 10 places
> them.
> Finally, add Roadies as an ordinary currency — hand-authored `Roadies.asset` mirroring
> `Records.asset`, filed in the existing Global `permanent` group, labelled for Addressables, and
> **not** in the chapter's currency roster, which `ChapterCurrencies` and `ResolveRoster` both refuse
> for a global id. No `RoadiesManager` and no `RecordsManager`: a currency already has a balance, an
> earned total, conditions, a save block and formatting.
>
> Goal: a context can be captured, restored, and seeded by recipe with one implementation of the
> order; a payout cannot be paid twice by any path; a module knows what it presents; and the capstone,
> story beats and Roadies exist as content for slice 7 to consume rather than invent. Stop here.

✅ **Test & commit:** Records balance and earned total round-trip independently, and a restored Records
total reactivates both the capstone gate and the income modifier; a different snapshot clears the
flags and latches absent from it while the same one reapplies idempotently; silent restore still
forces condition evaluation and finishes with the context clean; no observer sees partial state,
`Modifiers.Changed` included; **[rev]** an award pays only when its operation executes it, a compound
payload re-applies exactly at every rebuild, and a content unlock carrying actions is reported (it is
never bought, so they would never pay); sandbox
writes cannot reach the shared permanent pool; permanent-in-chapter seeding excludes run facts and
rebuilds the expected derived modifiers; the capstone consults its authored `Condition` and no scalar
gate; a null capstone unlock is refused at boot;
Roadies resolve through the Global permanent
group; `CaptureLocalState()` contains no permanent currency; and the existing release / second-run
walk still passes unchanged.

---

## 7 — Capstone / chapter gate (on 6.5's contract)  ✅ done

> Implement the chapter capstone per §1–§2, §5 and the `capstone` entry. **Records need no manager**
> — 6.5 settled this: Records are a global currency, the cumulative total is
> `CurrencyManager.GetEarned`, the income buff derives from that same fact, and the release already
> awards through `EconomyContext`. A second owner for one number is a synchronization bug waiting for
> a second writer.
>
> - The capstone's availability is `CapstoneConfig.Unlock` — the authored `recordsCumulative ≥ 30`
>   Condition 6.5 imports, asked through the one evaluator like every other gate. Do not re-derive the
>   threshold: there is no scalar gate left to read. When it holds, offer "Play the Backyard Party."
> - **[rev]** On capstone completion, as ONE atomic `EconomyContext` operation ending at a single
>   `Settle`: refuse if `CompletionFlagId` is already set (a finished chapter does not complete
>   twice), and refuse if any capstone action answers `CanExecute` false — the preflight runs BEFORE
>   the irreversible release below, the same charged-for-nothing rule `TryBuy` applies, because a
>   completion that releases the album and then fails to award would strand the run. **[rev]** The
>   operation ALSO refuses while its own `Unlock` is unmet (settled at build time, 2026-08-10):
>   TryBuy's fail-closed shape rather than the release's offer-only gate, because the release's
>   ungated operation is justified by the capstone needing to cut an album regardless of any offer,
>   and no caller needs an ungated completion — a completion latches a permanent flag, so a UI bug
>   must not be able to finish a chapter early. Then: first
>   perform the standard album release (slice 6's path — the run's Fans bank as Records; design
>   §1–§2: the capstone implicitly cuts an album, so no run value is stranded at the chapter
>   boundary); then `Apply` `CapstoneConfig.OnComplete` if authored (re-applicable state — Ch1
>   authors none), `Execute` each of `CapstoneConfig.Actions` (Ch1: the one Roadie — actions run
>   only from this operation, so no release, load, or reprojection can pay a second one), and set the
>   declared `CompletionFlagId` itself — the operation owns the flag from the declaration, no payload
>   carries a copy.
> - **[rev]** The completed capstone is a FACT SOURCE like any latch: whenever the declared
>   `CompletionFlagId` is set, projection re-applies `OnComplete` with permanent scope — the flag IS
>   the latch, so capstone-authored state survives every release, load, and reprojection exactly as
>   the `GameEffect` contract requires (rule 6). Ch1 authors no `OnComplete`, so this is wiring, not
>   behavior — but without it, any modifier a later chapter authors there would vanish at its first
>   release. The Roadie allocation/replay UI stays LOCKED (`roadieSystemUIUnlocked: false` — deferred
>   to Chapter 2), and Chapter 2 content does not exist yet, so the flag just marks the boundary.
> - The story beat is not fired by this code. `storyBeatCapstone` is a `StoryBeatDefinition` whose
>   section gates on `chapter_2_unlocked`, so setting the flag above IS what reveals it — the same
>   pull every other module's reveal uses. Slice 10 builds the card that presents it.
>
> - **[rev]** The offer surface lands in this slice (settled at build time, 2026-08-10): a
>   `CapstoneModule` mirroring `ReleaseModule` (label from the config's `DisplayName`, the
>   pending-Records preview from the one `PendingReleaseRecords` home, pressability from the
>   capstone's own `Unlock`), in a new authored section `the_backyard` whose `visibleWhen` is
>   `recordsCumulative >= 1` — deliberately NOT the gate's 30, which has exactly one authored home
>   in `capstone.unlock`; region coarse, action precise, the `the_release` arrangement.
>
> Goal: reaching 30 cumulative Records offers the capstone; playing it banks the run as Records,
> grants the first Roadie exactly once (no allocation UI), and sets the chapter-advance flag. Stop
> here.

✅ **Test & commit:** capstone unlocks at 30 Records via its authored `recordsCumulative` Condition;
the implicit release banks Fans as Records; the Roadie is granted once — executed by the completion
operation, invisible to every reprojection — and a second completion is refused on the set flag; an
action that cannot execute refuses the completion BEFORE the release; capstone-authored `OnComplete`
state re-projects from the completion-flag latch across a release; advance flag set by the operation
from the declaration.

---

## 7.4 — CONSOLIDATION: one producer per currency (rate and yield)

This slice adds no new gameplay. Chapter 1 must play *exactly* as it does after slice 7 — same tap
value, same fan rate, same costs, same reveal order.

It exists because **"what creates cash" has no single answer.** Today it is `GeneratorDefinition`'s
`baseOutput`, plus tick-triggered `ProductionConfig`s, plus tap-triggered ones — three mechanisms
producing one currency, none of which owns it. The tell is already in the code: `ProductionSystem`
carries `HasProduction(currencyId)` and `RatePerSecond(currencyId)`, which scan every config looking
for one currency because the UI needs a per-currency view the model does not have. Those queries are
the missing object, written as loops.

The modifier layer already addresses that object. `ModifierTarget.CurrencyProduction`, qualified by
currency id, means "the summed production of one currency" — a per-currency producer that production
never implemented. Two layers describing one thing, one of which does not exist, is how they drift.

Three separate symptoms all come from the same absence:

- **`ProductionTrigger.Tick | Tap` names callers, not quantities.** A rate is per unit *time*; a yield
  is per *occurrence*. Naming them after the clock and a gesture puts a UI action in the economy's
  vocabulary, and the first demand-fired producer that is not a button press — an automation, an event
  tier, a story beat — either lies about being a tap or gets a third value meaning the same as the
  second. (Do not "unify" the two through a per-firing magnitude: multiplying a quantum by elapsed
  seconds is a unit error that silently couples two numbers authored independently.)
- **`ModifierTarget.TapValue` is global while firing is already per producer.** `_tapByProducer`
  exists because flattening tap configs made one press fire every producer in the chapter; the
  multiplier on what a press pays never learned the same lesson, so a buff on Jam would also raise a
  Merch surface's yield, silently.
- **`isBandmate` is a bool because a generator can hold exactly one output.** A bandmate makes cash
  *and* fans; with one output there is nowhere to put the second, so it became a flag a system
  branches on.

Do this **before 7.5.** That slice makes a scope hold its producers; building it against the current
shape files three mechanisms under every scope and guarantees a later slice to undo it — which is the
pattern the `.5` slices have been paying for since 5.4.

> Read `/docs/garage-band-idle-design.md` — §3, §6, §9 and §12 rules 11 and 13. This is a refactor.
> **Do not change observable gameplay.** Every number Chapter 1 shows must be identical afterward.
>
> **The steps below are in build order and land as three commits — A (step 1), B (steps 2-3), C (steps
> 4-8).** Each commit compiles and passes the suite on its own. Do not reorder them by topic; the
> grouping is what can be built when, and step 1 is deliberately first because it is the only piece
> that touches nothing else.
>
> ### Commit A — the target vocabulary
>
> **1. `ModifierTarget` stops naming Chapter 1's surfaces, and gains the targets already designed but
> never added.** `TapValue` retires into **`CurrencyYield`** qualified by currency id; `FanRate` into
> **`CurrencyRate`**; and `CurrencyProduction` *is* `CurrencyRate` — it already meant "the summed
> production of one currency" and is already qualified, so that one is a rename. Add, at the same time,
> the three targets §12 rule 11 now names with nothing behind them: **`BarFillRate`** (§6),
> **`IdleRate`** and **`IdleCap`** (§9). They are observably inert — nothing authors one — and adding
> them here is one enum edit instead of three later, the same argument 7.5 makes for `GeneratorCost`.
> **The qualifier is optional and unqualified means every member in reach**, which is what lets "double
> all idle payouts" be placement rather than an id list; `ModifierTargetKey.RequiresQualifier` must
> stop forcing one.
>
> This is independent of everything below — `ProductionConfig` already carries `CurrencyId`, so
> `Of(CurrencyYield, config.CurrencyId)` composes exactly the set `Global(TapValue)` composed today.
> It is **not** content-free, though: a `tapValueMultiplier` reward names no currency, so the effect
> vocabulary gains a currency qualifier and the JSON, the importer's string maps, `GrantModifierEffect`
> and `ContentValidator` all move with it, then a reimport. Every number is unchanged.
>
> **[rev] Step 1 above was built (`75468ad`) and then superseded inside this slice (`0f48965`), before
> commit C landed. Read the paragraph below as the instruction; the one above records what the enum
> vocabulary looked like on the way past.** The qualified-target shape does not survive one generator
> feeding two currencies, which is what commit C's contributions make ordinary: Bigger Kit says "double
> the drummer's output", the drummer holds a cash line and a fans line, and a closed KIND plus one
> designer id cannot say which - the kind names a family, the id names a member of that family, and the
> number itself is never named at all. Reaching both changes Chapter 1's numbers and contradicts the
> chapter's own note that the fan rate is deliberately not a function of cash.
>
> **So there is no target enum.** `ModifierTarget` and `ModifierTargetKey` are both deleted. **Every
> modifiable number carries an id** - a contribution, a producer's rate or yield, a generator's cost, a
> bar group's fill rate, a scope's idle rate and cap - and a modifier carries a **`ModifierSelector`**,
> a list of terms where each term is an id or a tag. A term is a NAME, never a facet: `cash_rate` is the
> id of cash's rate, and `["cash","rate"]` is not another way to spell it. **An empty selector reaches
> everything in scope**, which is what the optional qualifier was for - "double all idle payouts" stays
> placement rather than an authored id list. Matching is asked of the number being matched
> (`ModifierSubject`), never computed inside the registry, so the composition and the change
> notification share one implementation and a row can never display a number the economy disagrees
> with; a subject offers its OWNER's id too, so naming a generator still reaches every line it holds.
> **Modifiers are multipliers only.** `ModifierOperation.Add` goes with the enum: a flat bonus is a
> CONTRIBUTION to the number it raises, authored by the fact that pays it, so every composed number has
> one shape - the sum of its contributions times the product of the multipliers matching it - and there
> is no application order left for two systems to disagree about. The content vocabulary follows: an
> effect authors `targets` (a term list, `[]` meaning everything in scope, an absent key refused), the
> importer's names are the OPERATION alone, and `ContentDatabase.ResolvesModifierTerm` reports a term
> nothing answers to, since an open vocabulary has no compiler behind it. §12 rule 11 is written to
> this; the design doc is the authority.
>
> ### Commit B — the new model, additive
>
> **2. `CurrencyProducer`, one per currency.** It owns two numbers: `Rate` (units per second) and
> `Yield` (units per firing). Each is composed from **contributions that stay individually
> addressable** — not a running scalar, because a generator row has to show what *that* generator
> makes. Same shape `ModifierSystem` already has, so production stops being a second, weaker pattern
> beside it.
>
> **How the producer gets its contributions: declared forward, assembled backward.** A contribution
> names the currency it feeds, so the producer never knows contributor *kinds* and a new kind touches
> nothing. Something then enumerates the reachable contributions naming that currency and hands the
> producer the list. **Assembled, never registered** — a contributor that assigns itself in must also
> remove itself, and every teardown bug in this repo is that shape (`CurrencyRouter` and
> `ConditionContext` are both `IDisposable` for exactly this reason). The list is rebuilt at the
> boundary that already re-composes, so enable, disable and reset need no bookkeeping. In this slice
> "reachable" is simply the economy's contributors; 7.5 swaps in the scope-subtree walk and nothing
> else about the shape changes.
>
> A contribution's *value* is derived, not stored — `owned × amount × modifiers`, read live — so buying
> a generator changes no structure, and gates stay evaluated per composition. Only a change in the
> *set* of reachable contributors needs a rebuild.
>
> **3. `ProductionContribution`** — `{currency id, amount, feeds: rate | yield, gate: Condition}`.
> `feeds` is what the contribution *is*, which retires both `trigger` (a caller) and the separate
> `composes` declaration (which number it scales is now the same fact). No lifetime field: durability is
> its contributor's. **No idle-eligibility field either** — §9 settles that structurally, since a rate
> accrues while a scope is disabled and a yield cannot, nothing having fired it.
>
> Both types are new and nothing constructs them yet, so this commit is additive and compiles alone.
>
> ### Commit C — the switchover
>
> Steps 4 through 8 are one commit whether or not that is convenient: changing the definitions' serialized
> fields forces the importer and a reimport in the same breath, and removing `ProductionSystem`'s
> tap-named members breaks its callers at compile time.
>
> **4. A contributor holds a LIST of contributions.** A **generator** holds contributions scaled by owned
> count — the Chapter 1 amp authors one cash contribution; a bandmate authors cash *and* fans, at the
> per-bandmate value `ChapterDefinition.perBandmate` carries today. A **module** holds contributions too,
> which is where Jam's cash yield and Rehearsal's trickle live.
>
> **5. Firing is external and unnamed.** `producer.Fire()` pays the composed yield. The producer never
> records what fired it — a button, an automation and a test are indistinguishable below this line.
> `TapModule` names the currency producer it fires, calls `Fire()`, and labels itself from `Yield`.
> **"Tap" survives only in that module.** If the word appears in `Economy/`, `Core/`, the JSON schema
> or a modifier term, the slice is not done. **[rev]** Authored content may say it freely - the
> `module/tap` address, a `tap_value_x1_25` reward id, a stage note - and so may the importer's refusal
> keys, which have to keep the exact spelling they detect. What must not survive is an internal name:
> a type, member, or local naming the gesture, or a comment describing an economy quantity as one.
>
> **6. `ProductionSystem` becomes the collection of the economy's currency producers.** It integrates
> rates over elapsed time and resolves a producer by currency id. `HasProduction` and `RatePerSecond`
> stop being scans and become properties of the producer the caller already has.
>
> **7. Re-author the chapter JSON and the importer to match**, then reimport. Every generator row grows a
> contributions list; the Jam producer's entries declare `feeds`; the bandmate bonus becomes fans
> contributions at the same per-unit value. `ContentValidator` gains what this makes checkable — a
> contribution's currency resolves, and a yield contribution belongs to a producer some module can fire.
>
> **8. Delete, don't deprecate.** `ProductionTrigger`, `ProductionConfig`, `ProducerDefinition`'s
> `HasTapConfigs`, `GeneratorDefinition.isBandmate`, **`BandmateFanRateModifier`**, **`ChapterDefinition`'s
> per-bandmate bonus field** (the fan bonus is a derived modifier today, not production at all — it
> becomes contributions on the bandmates), and every tap-named member of `ProductionSystem`.
>
> Goal: Chapter 1 plays identically, and the economy contains no concept named after a gesture. Stop
> here.

✅ **Test & commit (three times — A, B, then C).** For each: the project compiles and the suite passes.
For A, every Chapter 1 number is unchanged with the addressing rewritten: **[rev]** an empty selector
reaches every number in scope, a term naming an id reaches exactly that number, a term naming a
contribution's owner reaches every line the owner holds, and a term nothing in the content set answers
to is reported rather than filed. For B, nothing observable moves at all - the types exist and nothing
constructs them.

For C, the slice's real gate: every Chapter 1 number is unchanged — press payout, fan rate, generator
costs, the first-demo pacing; a bandmate generator raises cash's rate *and* fans' rate with no flag
anywhere; a yield buff moves the yield and leaves the rate alone, and vice versa; a buff on one
currency's yield does not reach another's; a contribution whose gate is unmet contributes zero to the
composition its readout displays, so the readout and the payout cannot disagree; a producer's list is
rebuilt rather than registered, so nothing needs unhooking when a contributor goes away; and
`grep -ri tap` over `Assets/Scripts/Economy`, `Assets/Scripts/Core` and the chapter JSON returns
nothing.

---

## 7.5 — CONSOLIDATION: the scope tree (lifetime becomes placement)

This slice adds no new gameplay. Chapter 1 must play *exactly* as it does after slice 7.

It exists because `ContentScope` is a cascade hardcoded to two rungs — `Run = 1`,
`PermanentInChapter = 2` — with a reset operation per rung (`ResetRunScoped` means "clear rung 1"; the
chapter boundary has no reset at all, it discards the context). `ChapterDefinition` carries
`AlbumConfig` and `CapstoneConfig` as two singleton fields, which is the same fixed-depth assumption
expressed in content. Every chapter, forever, therefore gets exactly one within-chapter prestige and
one capstone.

That is not what the game needs. A chapter wants an ordered **ladder** of prestige rungs, each banking
its own currency, where an intermediate currency is *spent* inside the chapter (higher generator tiers,
recovery accelerators) and only the deepest rung banks Records. Some rungs reset only themselves; some
reset every rung before them. None of that is expressible in a two-valued enum, and bolting a third
value on repeats the mistake at depth 3.

The replacement is one concept. A **scope** owns its truth (currency balances, modifiers, flags, and the
systems whose facts it holds), owns what presents that truth (its sections), and holds an *ordered list
of child scopes*. A fact's lifetime becomes **where it lives** rather than a value it declares — so no
declaration can disagree with the reset acting on it, the same way the GameEffect/GameAction split made
a double payout inexpressible rather than validated against. One concept absorbs `EconomyContext`,
`EconomyRecipe`, `CurrencyPlacement`, and `ContentScope`.

Do this **before slice 9.** `ContentScope`'s explicit enum values are a stated serialization contract,
and there are no saves yet — so today this is an asset migration and after slice 9 it is a save
migration. Slice 9 also builds its restore against whatever the lifetime model is.

> Read `/docs/garage-band-idle-design.md` — §1, §2, §3, §5, §6.1, §9 and §12 rules 6, 7, 9, 11, 12, 13,
> 14. This is a refactor. **Do not change observable gameplay:** same yields, same rates, same reveal
> order, same release behaviour, same re-climb, same capstone.
>
> **0. Build the settle boundary FIRST — everything else rests on it.** §12 rule 12 decides it; this
> step implements that decision rather than re-opening it. Today every top-level operation ends at one
> `Settle`, with one terminal `Settled`, so condition-dependent values re-evaluate exactly once after the
> whole mutation; `EconomyContext.Restore` additionally runs a bounded fixpoint inside `DeferSettled` and
> replays notifications under `SuppressInvalidation`. In a tree, one mutation spans scopes — a rung's
> reset emits into an outer scope whose subscribers must settle too.
>
> Per rule 12: the boundary is the **root**, fixed rather than discovered, so an operation never has to
> learn mid-mutation which scope owns its settle. Each scope carries its own dirty flag; the root's
> settle drains the dirty ones **outermost first**, then re-composes the currency producers of every
> enabled scope (7.4). The settled signal stays **per scope**, raised only for the scopes that
> drained. Two details that are easy to get wrong and expensive to find later: **enabling a scope must
> dirty it** (the world moved while it was disabled and nothing raised its flag — the same reason
> `Restore` calls `MarkDirty` explicitly), and **suppression is root-owned but must reach every scope**,
> or the restore's republish re-dirties descendants after the settle consumed them and "which signal is
> terminal" has two answers again.
>
> **[rev] The root's settle itself loops** — drain while any scope is dirty, under the same bound the
> restore uses today, with an exhaustion diagnostic naming **which** scopes are still pending, since
> naming the chapter says only that something somewhere re-triggers itself. This sentence previously read
> as widening the restore's fixpoint alone, which left the ordinary settle single-pass and contradicted
> §12 rule 12; the rule is the authority. `Drain` itself stays **one pass per call** — it clears the dirty
> flag before evaluating, so a flag set during an evaluation is not swallowed by the drain that caused it
> (`ConditionInvalidationTests` pins exactly this). The loop therefore belongs above `Drain`, in the
> root's settle, which is where `Restore` already puts it.
>
> **[rev] Two consequences of the loop.** Each scope's drain sequence sits inside its own `DeferSettled`,
> or a scope drained on two passes raises `Settled` twice — nesting within one scope, unlike suppression
> above, which is the one piece that composes across them. And `EconomyContext.Restore` stops hand-rolling
> its own fixpoint: its `DeferSettled` + drain-while-dirty + bound + producer re-composition block *is*
> the root settle's body, so it calls the root settle rather than keeping a second copy in step.
>
> **[rev] Per-scope `Settled` is inert until its subscribers move.** `ChapterScreen` and the six modules
> (`BarList`, `Capstone`, `CurrencyHeader`, `GeneratorList`, `Release`, `UpgradeList`) all subscribe to
> `context.Economy.Conditions.Settled` — one context's signal. Each binds instead to the `Settled` of the
> scope it lives in, or every module still re-asks on any change anywhere and the per-scope decision buys
> nothing.
>
> **1. `ScopeDefinition` / `Scope`, with a definition/instance split.** A definition names the scope's
> id, its `activeWhen` Condition, its currency roster, its sections, its ordered child scope ids, and
> its prestige rung if it has one. An instance holds the runtime — pool, systems, modifiers, flags,
> conditions — under a **stable instance identity**, because a replay economy (§8.1, rule 7) is a second
> instance of a chapter's definition and slice 9's save is one block per instance. This is the same split
> `ChapterDefinition`/`Chapter` and `GeneratorDefinition`/`Generator` already have.
>
> **2. One chain iterator, three public resolvers.** `ScopeChain` answers *what is in scope*: my scope
> outward to the root, in order, enabled only. Exactly one implementation of that iteration, because
> otherwise "in scope" has three answers that can drift. On top of it, **three public functions, not one
> with a mode parameter** — `ResolveCurrency` (first owner wins; one balance), `ResolveFlag` (any link
> satisfies), `ResolveModifiers` (every link contributes). "Accumulate a currency" is not a concept, so a
> shared mode vocabulary would be a union of things that never apply to each other. `CurrencyRouter` is
> already this at N=2 and shows the shape: it claims outermost-first, flattens the chain into an
> `_owners` map at construction (a cache of the walk, not a per-read walk), refuses shadowing rather than
> resolving it, and is `IDisposable` because it aggregates its pools' `BalanceChanged`. Generalize it;
> keep all four of those properties. **Ids stay unique tree-wide** — that is what makes moving a
> currency outward a pure data edit.
>
> Note the asymmetry and honour it: **reads go outward, change notifications go inward.** An inner module
> gating on a root currency must re-evaluate when that currency moves, so a scope subscribes to its
> ancestors' signals. `CurrencyRouter`'s own comment already names the failure — a discarded listener
> keeps a dead economy's subscribers alive — and at N levels that disposal discipline is load-bearing.
>
> **3. Reset as one parameterized operation.** Delete `AlbumConfig`/`CapstoneConfig` as separate shapes
> and add `PrestigeTierDefinition`: `id`, `displayName`, `offer` Condition, optional `operationGate`
> Condition (null = ungated, today's release; set = fail-closed, today's capstone), optional `onComplete`
> `GameEffect`, `GameAction[]`, an optional **`completionLatch`** slot, and a `ResetTargetSelector`.
>
> The latch is a **named slot holding one flag-setting `GameAction`**, not a bare flag-id string. Only
> `SetFlagEffect : GameEffect` exists today, and effect semantics are wrong here: an effect re-runs on
> every rebuild, which is right for an upgrade's reveal flag (re-derivable from the saved upgrade latch)
> and wrong for a completion, which has nothing more primitive behind it — `CapstoneSystem` projects
> `OnComplete` *from* the flag. So add the action counterpart, set once at the press and persisted. The
> slot keeps "which flag is this rung's completion?" readable off the definition, and boot validation's
> setter sweep finds it through the family's own `Validate` — the same `FlagSetterReport` path
> `SetFlagEffect` already uses — so no validator special case and no second declaration.
>
> **The slot's type is narrower than `GameAction`.** The operation has to *read* the target flag, not just
> execute it: the already-completed refusal below asks whether this rung's flag is already set, and a bare
> `GameAction` exposes no flag id. So the slot is typed to the flag-setting action concretely, or to an
> interface carrying a `FlagId` — whichever, the requirement is that the id is readable without executing
> anything, and that any other action type in that slot is refused at import and at boot. A
> `GrantCurrencyAction` sitting there is content that would latch nothing while reporting a completion.
>
> **The payout is one of those `GameAction`s, not a field of its own.** 6.5 already classified it —
> "payouts are `GameAction`s on the player-action moment that earns them, unreachable from every release,
> load, and projection" — and slice 7's Roadie award ships as one. A `payout` field sitting beside
> `GameAction[]` would be a second home for that concept and would re-special-case exactly what 6.5 spent
> a slice generalizing. So `PayoutFormula` (polymorphic, subclass-picked like
> `Condition`/`BarFillBehavior`, with Ch1's `floor((fans/5)^0.5)` as its first member) is the *amount*
> parameter of a computed-grant action, and a rung that awards nothing simply has an empty list — there
> is no null payout to represent, and no "does this rung have a payout" branch to write.
>
> The operation: **the parent orchestrates** (only it knows the sibling order a selector may name). The
> **participants** are the initiating rung's scope *plus* every scope its selector chose, **de-duplicated**.
> Both halves are load-bearing. The initiator is often *not* in its own selected set — Ch1's capstone rung
> sits on the chapter scope and selects only the tier scopes, so "every selected scope runs its actions"
> alone would bank the album and never grant the Roadie or latch the chapter's completion.
> And it sometimes *is* — the album rung's selector is self-and-contained, so without the de-dup an
> ordinary demo press would run its Fans-to-Records action twice.
>
> Then, in order:
>
> - **Refuse on the initiator alone, before anything moves.** A rung whose `completionLatch` flag is
>   already set does not run a second time — silently, because the UI calls this from a button and a double-tap is
>   not an error. A rung declaring an `operationGate` asks it here and refuses if it is unmet: fail-closed,
>   asked by the *operation* and not only by the button that offered it, because a press that latches a
>   permanent flag must not be reachable through a row the player is merely still looking at. A rung with
>   no gate is ungated — today's release, repeatable and harmless at any time. `offer` is **not** asked
>   here; it governs whether the rung is presented, not whether the press is legal.
> - **Preflight every participant's actions, and the latch.** Refuse the whole operation if any answers
>   `CanExecute` false, before anything executes and before any flag latches — the rule `TryBuy` and
>   today's capstone already apply, now covering actions drawn from several scopes rather than one. The
>   initiator's `completionLatch` is explicitly included: it sits outside `GameAction[]`, so "every
>   participant's actions" would otherwise skip the one action guaranteed to run last, and a latch that
>   cannot execute after every payout has already landed is the exact stranding this preflight exists to
>   prevent. Loud, unlike the refusals above: an action that cannot execute is broken content.
> - **Run the actions deepest scope first, initiating rung last**, while the state those formulas read
>   still exists, resolving outward so a rung can bank to the root without its immediate parent being the
>   recipient. Deepest-first is not an arbitrary tie-break: reads go outward, so an outer rung running
>   first would write state an inner rung's formula then measures. Each rung must measure what the player
>   pressed on, not what the press has already moved — a capstone awarding on cumulative Records has to
>   run after the demo it implicitly cuts, not before it. Depth alone is only a *partial* order, so
>   same-depth participants run in **the parent's authored child order, first to last** — the same list
>   `preceding-siblings` resolves against (rule 14), so one authored ordering answers both questions
>   instead of two that can disagree. Without it, two sibling tiers banking into the same ancestor currency
>   would produce a result that depends on incidental enumeration order.
> - A participant's own `offer`/`operationGate` is **not** re-checked. Those gate the player pressing that
>   rung; they say nothing about it participating in a deeper press. A participating rung whose offer is
>   currently false still runs its actions and pays whatever its formula computes, possibly zero —
>   otherwise "the capstone implicitly cuts an album" would fail exactly when the album rung is not
>   offerable, which is the stranding the implicit cut exists to prevent.
> - **Run the initiator's `completionLatch`**, if it declares one — from the slot, never from a duplicate
>   in `GameAction[]`. The flag is the latch and nothing more: `onComplete` is a `GameEffect`, so the
>   operation never executes it — the projection below re-applies it *from* the flag on every rebuild,
>   which is how `CapstoneSystem` already works and why a completed capstone survives a load with no
>   effect serialized. **`onComplete` therefore requires the latch slot to be filled**: without a flag
>   there is nothing to project from, so an authored effect would silently never exist. Refuse that
>   pairing at import and at boot rather than leaving it to be discovered as a buff that never appears.
>   The latch's flag must live outside whatever that rung's own selector clears — the same rule its
>   awards' targets already obey, for the same reason.
> - **No action may change the tree's shape or the enabled set.** Actions run before the clear, the
>   projection and a settle that walks enabled scopes only, so an action that enables, disables or
>   replaces scopes leaves the rest of the operation running against a tree that moved under it. Anything
>   structural is a *reaction* to the settled facts, not a step inside the mutation — chapter advancement
>   is the first instance, with `ChapterManager` acting on the latched completion flag once the operation
>   has settled. Being one-shot, an action could not do it anyway: it would never replay on load, so the
>   flag would have to drive the outcome regardless.
> - **Then clear the selected scopes** — the *selected* set, not the participants. A scope that only
>   initiated is not itself cleared, which is what keeps the capstone from wiping the completion flag it
>   just latched. Re-run projection — which is where `onComplete` re-applies — then one root settle per
>   step 0.
>
> Every selected scope, not just the pressed one. That is the whole of "the capstone implicitly cuts an
> album": the capstone selects the chapter's tier scopes, so the album rung's own Fans-to-Records action
> runs because its scope is in the set. No operation ever computes across scopes, so no rung ever needs
> to read inward. `ResetTargetSelector` is polymorphic —
> self-and-contained, preceding-siblings, named — its output **closes downward**, and it lives on the
> scope *instance*, never on the module presenting it (a prefab can be placed twice; a target list on it
> would be two sources of truth for one lifetime).
>
> **Clearing is in place.** A reset clears a scope's *contents*; it does not discard and rebuild the
> scope instance. The instance survives and each system resets what it owns, exactly as the album release
> does today. Three things rest on that. The save's scope-instance identity (rule 6) stays stable without
> having to be carried across a rebuild. Every UI binding held on a scope's systems — `Conditions.Settled`,
> `Currencies.BalanceChanged`, `Modifiers.Changed`, `Upgrades.UpgradeApplied`, a producer's composed-value
> signal
> — stays valid, where a rebuild would leave all of them pointing at a dead object several times per
> chapter. And per step 0, a surviving instance keeps its dirty flag, so the reset marks it rather than
> relying on a fresh instance's default. A later slice wanting true reinstantiation has to solve those
> three first.
>
> One hazard to fix while here, since it is the same class of thing: `GeneratorSystem` subscribes to each
> generator's `OwnedChanged` with an anonymous lambda and keeps no reference, so that handler can never be
> removed. It is harmless while the generator and the system die together, and it is a leak with no
> removal path the moment a reset rebuilds one without the other.
>
> This deletes a special case rather than adding a parameter: `ReleaseAlbumFacts()` exists only so
> `CompleteCapstone` can borrow the release's facts without its settle. With depth-based reset, "the
> capstone implicitly cuts an album" is what selecting a deeper rung *means*, and the split goes.
> `ReleaseModule` and `CapstoneModule` — already near-duplicates differing only in which gate they ask —
> collapse into one `PrestigeModule` parameterized by rung id, which finally gives 6.5's `definitionId`
> parameter its second real consumer.
>
> Enforce three authoring rules per rung. Two generalize the checks `ContentValidator` already has
> hardcoded for the album (`ValidateRecordsSurviveRelease`, and the fans-must-reset check): an award's
> **inputs** must live in a scope the reset clears (else the same value banks on every press, unbounded —
> and the release operation is deliberately ungated, so nothing else stops it), and its **target** must
> live further out (else the reset destroys the award that produced it).
>
> The third is new, and it is what the per-scope arrangement above exists to satisfy: **a rung must be
> filed with the state its formulas read.** An award resolves outward only, so a capstone rung in the
> chapter scope cannot read Fans held in a child tier. The first two rules do not catch it, because
> *clears* and *can-read* are different relations — the capstone selects the tier holding Fans, satisfying
> the input rule, and still cannot see it. Refuse at boot, naming the rung and the currency it cannot
> reach. In Chapter 1 this is already satisfied: the album rung sits in the tier scope holding Fans and
> banks them, and the capstone rung sits in the chapter scope, awarding and latching things that read
> nothing local. If a formula ever needs two siblings' state, the answer
> is to move that currency to their common ancestor (§2), since siblings are never on each other's chain.
>
> **4. Enabled scopes replace focus.** Delete the single focused context. Scopes are enabled or disabled,
> **plural**: several are enabled at once, only enabled scopes tick, and an outer scope keeps producing
> while the player works inside a tier. Exactly-one-focused is what previously made double-counting
> impossible by construction, so re-home that guarantee explicitly: **two instances of one scope
> definition are never enabled at once.** Module display becomes the three-way conjunction
> `scope.activeWhen && section.visibleWhen && module's own condition`, with containment supplying the
> implicit terms and a module's condition not even evaluated when its scope is inactive. Sections stay
> **not scopes** — a section has no truth of its own and its condition answers a different question:
> activation governs *simulation*, visibility governs *presentation*, and they must be independent
> because an active scope has to keep simulating while its display is off-screen. Rule 9 still holds:
> one mechanism, Conditions, evaluated at three levels.
>
> **5. Production direction, checked at import.** A **contributor** may feed a producer in its own scope
> or further out, **never inward** — an outer generator raising an inner currency's rate outlives its own
> target. A producer lives in the scope of the currency it produces, which is what makes this static:
> resolve the contributor's scope and the target currency's scope and refuse a strictly-inner target.
> A contribution gains **no** lifetime field: its durability is its contributor's and its gate already
> reads flags that carry their own placement. (7.4 built the contribution; this slice only gives the
> check a tree to resolve against.)
>
> **6. Cost composes modifiers, the way production already does.** **[rev]** 7.4 ended with no target
> enum at all: every modifiable number carries an id and a modifier is a `ModifierSelector` over ids and
> tags, so this step adds no enum member. What it adds is a cost that HAS an id and a `CostCalculator`
> that composes over it - today `Generator.NextCost` is a parameterless property over a static formula,
> so a cost buff is unauthorable and nothing would compose one if it could. The ladder above is designed
> around spending an intermediate currency on cost reduction (Ctrl C's Syntax Highlighting is the
> reference shape), and rule 11 claims to be the one place any number is modified - cost sitting outside
> it makes that claim false.
>
> Give a generator's cost an id on the same convention its contributions follow (`<generator>_cost`),
> give `CostCalculator.Cost` the composition `ProductionCalculator` already has, and let `NextCost`
> reach its scope's registry. **Multipliers only**, which rule 11 now gives for free: a modifier IS a
> multiplier, and a flat cost reduction would be a contribution - one that needs a floor, since a cost at
> or below zero is a free generator and `GeneratorRowUI` already tests `NextCost > Zero`. Naming the
> generator reaches its cost through the same owner rule a contribution uses, and an empty selector
> reaches every cost in scope, which is what makes "-99% for tier 1" pure placement rather than an
> authored id list. Both live call sites already go through `NextCost` (`Generator.TryBuy` and
> `GeneratorRowUI`), so the displayed cost and the charged cost cannot drift apart.
>
> This is observably inert in Chapter 1, and that is the point: nothing authors a cost buff, so every
> generator composes an empty set and the exact-cost assertions (`amp.NextCost` at 60.0 and 69.0) pass
> unchanged. It lands in this slice because step 2 rebuilds the resolution path, and adding a consumer of
> that path afterwards means opening it twice.
>
> **7. Re-author Chapter 1 as a one-rung ladder, changing nothing observable.** The chapter scope holds
> `album`/`cut_demo` and the capstone offer; one tier scope holds cash/fans/rehearsal, the generators, the
> buff upgrades, the cover bars, and the `fans`/`covers`/`gear` flags and their setters. Each scope holds
> the rung filed there: the **album rung on the tier scope**, whose Fans-to-Records action reads the Fans
> sitting beside it, with a self-and-contained selector; the **capstone rung on the chapter scope**,
> selecting every tier scope. Ch1 authors it as the JSON already reads —
> `"onComplete": { "grantRoadies": 1, "completionFlag": "chapter_2_unlocked" }` — one award in
> `GameAction[]` and one `completionLatch`. That is Chapter 1's content, not a cap: `GameAction[]` is a
> list and a richer chapter's rung may award several things. What never belongs in it is a second flag
> setter beside the latch slot, or anything structural, both by step 3. Chapter advancement is
> `ChapterManager` reacting to the latched flag after the settle. That is the step-3 placement rule
> already satisfied rather than worked around, and it is why a capstone press banks the demo without the
> capstone ever reading Fans. Every `scope: run` /
> `permanentInChapter` key leaves the JSON — placement replaces it — and the importer refuses the old
> keys rather than mapping them. Boot validation's flag rule generalizes verbatim: **a flag needs at
> least one setter in its own scope or inside it.**
>
> **8. Delete, don't deprecate.** `ContentScope`, `CurrencyPlacement`, `EconomyRecipe` +
> `EconomyRecipeKind`, `EconomyContext`, `CaptureSeedFor`, `PermanentInChapterFacts`, the `Isolated`
> permanent-pool routing, `ModifierSystem`'s grant `scope` parameter, `Upgrades.ScopeOf`,
> `Flags.IsRunScoped`, every `Reset*RunScoped*`, and `ICurrencies.ResetsOnAlbumRelease`. A left-behind
> field promising a lifetime the tree now decides will eventually be believed.
>
> Goal: a chapter is a scope tree; a fact's lifetime is its placement; one reset operation serves every
> rung; several scopes are enabled at once; and Chapter 1 plays identically to slice 7. Stop here.

✅ **Test & commit:** one operation ends at one root settle however many scopes it touched, and a scope
enabled after the world moved re-evaluates on enable rather than staying stale; **[rev]** an unlock whose
flag opens another unlock resolves inside that one settle rather than waiting for the next tick, a scope
drained on two passes of the loop raises `Settled` exactly once, and the bound's exhaustion error names
the scopes still pending rather than the chapter; Chapter 1's full slice-7
test suite passes unchanged, including the second-run
reveal walk and the capstone; moving a currency's declaration one scope outward makes it survive a rung
reset with no code change and every reference still resolving; a sibling scope's currency/flag/modifier
is NOT reachable from its sibling, and the same fact filed in the common ancestor IS; three resolvers
over one chain iterator, with a shadowed id refused rather than resolved; every selected scope runs its
own rung's actions before anything clears and a granted currency resolves outward past its immediate
parent, so a capstone press banks the album tier's Fans without the capstone reading them; a selector's
output is downward-closed; a reset clears in place, leaving the scope instance and every subscription on
it intact; a capstone press runs the capstone rung's OWN actions as well as the selected tiers' — the
Roadie and the completion latch both land — while an ordinary demo press, whose selector
contains its own scope, runs its payout exactly once; a participant whose `offer` is currently false
still runs its actions; the initiating scope is not cleared unless its own selector selected it; and one
participant answering `CanExecute` false refuses the whole press before any action runs or any flag
latches; a rung whose completion flag is already set refuses silently and a rung whose `operationGate` is
unmet refuses too, both before any state moves; same-depth participants run in the parent's authored child
order; `onComplete` is never executed by the press and comes back from the latched flag on every rebuild,
and a rung declaring `onComplete` with an empty `completionLatch` is refused at import and at boot; the
latch is found by the flag-setter sweep with no validator special case; the latch's flag id is readable
without executing it, a non-flag-setting action in that slot is refused at import and at boot, and the
latch's own `CanExecute` is part of the preflight that runs before any payout; no action changes the tree's shape
or enabled set, and `ChapterManager` advances off the latched flag after the settle rather than from an
action; preceding-siblings is resolved by the parent and no scope enumerates its own
siblings; an award whose inputs are filed outside the scopes its reset clears is refused at boot, so is
a target filed inside them, and so is a rung filed where its own formulas cannot read; a
producer targeting a strictly-inner currency is refused at import; a cost modifier filed in a tier scope
reduces that tier's generator costs and no others, while with none authored every exact cost is unchanged;
two instances of one scope definition cannot both be enabled; a module with a true condition stays hidden while its scope is inactive; and
`ContentScope`, `CurrencyPlacement`, `EconomyRecipe` and `EconomyContext` no longer exist anywhere in
the tree.

---

## 8 — Events (Garage Jam Challenge)

> Implement the event system per §6.1 and the `events` array (garage_jam). **[rev]** This slice was
> rewritten after 7.5: the sandbox-context design it originally specified no longer exists, and the
> machinery it was written against (`EconomyRecipe`, `CaptureSeedFor`, `PermanentInChapterFacts`, the
> `Isolated` pool routing) was deleted there. Build the model below, not the one 6.5 anticipated.
>
> - **An event is a COMPONENT on a scope, never a scope of its own** (design §6.1, §12 rule 12). It gets
>   no economy, no pool, no seed, no projection filter. `EventComponent` attaches to the tier scope it
>   challenges and lives inside that scope's lifecycle: it receives ticks with its host and tears itself
>   down on success, failure, or quit. `EventManager` is a registry of which events are attached and
>   running, not an owner of economies.
> - **On entry, reset the host scope through its own rung** — which means the rung's **award actions run
>   first** exactly as an ordinary press would (§5, rule 14). Entry therefore *banks* the run instead of
>   destroying it, and costs nothing but time. Do **not** offer a "bank first?" prompt: the reset happens
>   either way so the starting state is identical, declining payment is pure loss, and §2 already removed
>   this exact ritual from the capstone for the same reason. An option whose right answer never changes
>   is a trap, not a choice.
> - **The reset is the baseline; there is no fixed scale.** Outer scopes stay on the resolution chain, so
>   every banked multiplier still applies and the event **scales with the player's accumulated power** —
>   deliberately (§6.1). A tier may be unbeatable until the player has advanced further, and coming back
>   stronger is the intended experience. Do not reintroduce a resolution ceiling, a baseline recipe, or
>   any per-kind chain barrier to recover the old fixed floor; that model was dropped because it excluded
>   the main power source and left a tier beatable now or never.
> - **[rev]** DELETE `EventDefinition.BaselineReset` — with the JSON `baselineReset` key and its note, the
>   importer's DTO field, and the `TestContent` parameter; the reimport rewrites `garage_jam.asset`. Entry
>   *does* reset now, so the field is not merely unread — it is a boolean standing where a
>   `ResetTargetSelector` on the host's rung already decides what clears. Two declarations of one reset,
>   and the boolean is the one nothing consults.
> - The **handicap is ordinary modifiers** (rule 11) that the component registers in its host scope and
>   removes on teardown — resolved by the same outward walk as everything else, with no bespoke debuff
>   path. `automationDisabled` is the Ch1 case.
> - Availability is a `recordsCumulative ≥ 1` Condition (available after the first demo). Each tier's
>   `goal` is a `currency` Condition evaluated by the shared evaluator.
> - garage_jam: debuff `automationDisabled` (generators paused, tap-only); timed and failable. Three
>   tiers (goal 500/2500/10000, timer 60/60/45, reward `tap_value_x1_25`/`_x1_50`/`_x2` applied via
>   `RewardManager`). A cleared tier is a fact filed in the **chapter** scope, so the ladder is not
>   re-climbed after every demo; its reward's durability follows that placement (rule 11) with no scope
>   declared on the reward.
> - **The entry emit pays the rung payout and nothing more.** A rerun tier must not be farmable for
>   advancement currency — the reward comes from the clear, the payout from the reset, and the two are
>   separate. Guard it and test it.
> - While a timed event runs, idle payouts are disabled (§9), and the timer pauses while its host scope
>   is disabled. An untimed event at insufficient power is not *failed* — it is unfinishable, and the
>   player quits.
> - **[rev]** Tier one-shot awards are `GameAction`s on the tier, executed by the clear operation
>   alone — 6.5 §4 names the tier clear as one of the three player-action moments an action list may
>   live on. The clear runs the same preflight rule as `TryBuy` and the capstone: refuse if any tier
>   action answers `CanExecute` false, BEFORE the cleared-tier fact latches, because a tier that
>   latched and then failed to pay can never pay — the clear does not re-fire. Ch1 authors none
>   (garage_jam's tier rewards are re-applicable tap buffs through `RewardManager`, facts that
>   re-project), so this ships in the posture `UpgradeDefinition.Actions` already ships in: the C#
>   list and its execution seam exist and are tested with test content, and the JSON key waits for
>   the first content that authors one.
> - **Failure/quit is teardown, and clears nothing.** The component owns no progress — the goal reads
>   ordinary host-scope currency — so there is nothing of its own to reset. Failing or quitting ends the
>   timer and the attempt and removes the handicap modifiers; the host keeps whatever the run
>   accumulated, to be banked by the next reset like any other run. Do **not** invoke the rung reset on
>   failure (that would run the award actions a second time in one cycle) and do **not** clear the host
>   without one (that would be a second reset mechanism, reachable only from here). Costs time, never
>   permanent progress.
> - **Entry is an alternate release surface, and that is fine.** Entry runs the host rung's award actions,
>   quit clears nothing, so enter → play → quit → enter does bank Records each cycle. That is not farming:
>   it is the ordinary payout for Fans the player actually earned, the same thing pressing the release
>   button would have paid. Do not write a guard against it. What must hold is narrower — teardown awards
>   nothing at all, and entry runs the *rung's* actions only, never the event tier's reward.
>
> Goal: I can enter garage_jam — which banks my run on the way in — play it tap-only against the timer
> with my accumulated multipliers still applying, succeed for a chapter-durable tap buff (from the
> rewards pool) or quit/fail for free, and repeat at higher tiers. Stop here.

✅ **Test & commit:** entry resets the host scope *and* banks its payout, so a run entered at 50 Fans
awards its 3 Records and the player is never asked to bank first; the event is a component on the host
scope and no second pool, context, or seed is constructed anywhere; outer multipliers still resolve
inside a running event (a player with more Records measurably out-produces one with fewer — the scaling
is asserted, not incidental); handicap modifiers appear in the host scope on start and are gone after
teardown; `baselineReset` is gone from the class, JSON, importer, asset and test content; tap-only;
timer + fail/quit are cheap and cost nothing beyond time; a failed or quit run clears nothing — the
host's Fans, cash and gear stand exactly where the attempt left them, no second payout is awarded on the
way out, and the next reset banks them normally; the timer pauses while the host scope is
disabled and idle payouts are off while it runs; tiers escalate and a cleared tier survives a demo; the
reward applies from the pool; teardown awards nothing and entry runs the host rung's actions only, never
the event tier's reward — a repeated enter/quit cycle banks exactly what an equivalent sequence of
release presses would and no more; a test-authored tier action pays once from the clear operation and
never from any rebuild, and a failing `CanExecute` refuses the clear before the tier latches.

---

## 9 — Save/load + offline earnings

> Implement persistence and offline earnings per §12 (rules 2, 4, 6) and the offline table in §9.
>
> - **[rev]** `SaveSystem`: serialize to JSON with a checksum; validate on load and reject/repair
>   tampered saves. **The schema is one block per scope instance, nested as the scopes are** (design §12
>   rule 6) — *not* a run block and a permanent block, because after 7.5 "run" and "permanent" are no
>   longer two categories but positions in a tree of arbitrary depth. Each block holds only what its own
>   scope owns: the tier scope's block carries cash/fans/rehearsal balances, generator owned counts, buff
>   latches, bar progress and its flags; the chapter scope's carries the `album` flag, the `cut_demo`
>   latch and the cleared event tiers; the root's carries Records and Roadies. A reset writes the blocks
>   it did not clear and clears the ones it did, with no category rule on top.
> - **[rev]** This makes **stable scope-instance identity** a hard requirement, not a nicety: a tier whose
>   contents a reset has cleared must round-trip as the *same* scope, and a replay instance (§8.1, rule 7)
>   must be distinguishable from the frontier's instance of the same definition. Identity therefore cannot
>   be derived from anything a reset touches, since a reset changes all of it, nor from list position,
>   which reordering would silently reassign. It is part of the schema. Note the instance itself is never
>   rebuilt — 7.5 step 3 clears in place — so identity has to *survive a clear*, not survive a
>   reconstruction.
> - **A running event attempt is a scope fact and must persist.** Closing the app disables every scope,
>   and slice 8 says a timed event's timer pauses while its host is disabled and idle payouts are off
>   while it runs. With nothing in the schema naming an attempt, reload silently discards the component —
>   and worse, the scope enables and pays idle income for time the event contract says earns nothing. The
>   host scope's block carries the active attempt: event id, tier, and the timer's remaining state. The
>   handicap modifiers are *not* saved; they re-project from that fact like any other grant (rule 11), and
>   idle suppression is restored with it, before the scope is enabled and before any idle payout is
>   computed. The timer pauses while the host is disabled (§6.1, settled), so what persists is the
>   **remaining** time, never an absolute deadline — a deadline would make wall-clock absence burn the
>   attempt, which is the behaviour that was rejected. A timed event's host is also paid **no** idle
>   earnings for the time it was disabled; an untimed event's host is paid normally.
> - **No modifier is ever serialized.** Derived modifiers compute from a source on every read, so the
>   Records buff comes back from the restored Records balance with nothing to save or migrate; writing
>   one would create a second answer that can disagree with its source. Grants are the same decision,
>   already made (design §12 rules 11–12): the save records only the facts that produced them — which
>   buffs are bought, which bars completed, which tiers cleared — and each economy context re-projects
>   its grants from those facts at construction. One source of truth: an effect can never disagree
>   with the fact that produced it.
> - **[rev]** `IdleEarnings` (design §9, **per scope**): each scope stores a last-interaction timestamp
>   in its own save block. When a scope is **enabled** — an app launch just enables the scopes you return
>   to — pay, for each of its currency producers, `rate × min(idleSeconds, cap) × idleRate` using
>   `DateTime` deltas, where `rate` is that producer's composed rate (7.4). **Every currency's rate
>   accrues, Fans and Rehearsal included**; there is no exempt list and no eligibility flag anywhere.
>   What does not accrue falls out of structure instead: a **yield** pays nothing because nothing fired
>   the producer, and **bar progress** does not move because filling is a tick-driven consumption (§6),
>   not production. Read `idleRate` (0.5) and `cap` (4h) **through the modifier registry** as the
>   `IdleRate`/`IdleCap` targets 7.4 added — not as constants — because the Backstage Pass raises one and
>   the "Double it" buff multiplies the other, and a hardcoded constant is exactly the corner those
>   features would have to be special-cased around. Nothing below a minimum idle threshold. Note this is
>   per *scope*, not per economy, and several scopes are enabled at once (rule 7) — so only the scopes
>   actually disabled accrue idle time, and an outer scope that stayed enabled has already produced live
>   and must not be paid twice. Test that explicitly; it is the case the old exactly-one-focused rule made
>   impossible for free. Show a collect screen with the amount and a placeholder "Double it" button — a
>   timed double-idle buff (an expiry fact modifiers derive from, §12 rule 11), not a per-collect double;
>   wire the actual ad later.
> - **[rev] The fill behavior gains a consumption rate** (design §6), and this is the slice that needs it:
>   with Rehearsal now accruing while away, a returning player's banked pool would otherwise empty into
>   the selected bar in a single tick — most of a bar's progress in one frame, which is collecting rather
>   than rehearsing. `PerBarContinuousFill` gains a rate field (it has no authored fields today) and the
>   drain becomes `min(pool, bar.Remaining, rate × seconds)`. That needs **elapsed time threaded through
>   the bar tick** — `BarSystem.Tick()` and `BarGroupRuntime.Tick()` are parameterless today and
>   `EconomyContext` calls `Bars.Tick()` with nothing. Compose the rate from the `BarFillRate` target
>   (7.4) so "rehearse twice as fast" is authorable. A dump-the-pool sibling behavior is the one that
>   declines to have a rate; do not add a switch inside this class.
>   Tune the Chapter 1 value against the *measured* first-demo pacing recorded in slice 10 (12–18 min at
>   human press rates, not the 300s `balanceTargets` asserts), since a fill rate slower than Rehearsal
>   accrues makes the bars the binding constraint instead of the currency.
>
> Goal: closing and reopening restores state (including flags and bar progress); time away grants idle
> earnings on every currency's rate at 50% capped at 4h, and a below-threshold absence pays nothing; the
> banked fill currency then pours into a chosen bar at its consumption rate rather than instantly; a
> tampered save is rejected. Stop here.

✅ **Test & commit:** state persists across restart; flags/bars restore; **[rev]** every scope instance
round-trips into its own block and a tier whose contents were cleared by a reset is recognized as the same
scope; a timed event in progress survives a restart with its remaining time intact and its host paid no
idle for the absence, while an untimed event's host is paid normally; idle payout
correct, capped, and zero below the threshold; **[rev]** Fans and Rehearsal accrue over an absence exactly
as Cash does, a yield pays nothing for it, and a bar's progress is unchanged by time away even with its
group selected; a bar fills at its consumption rate rather than absorbing a banked pool in one tick, and
an authored `BarFillRate` multiplier measurably changes how long it takes; raising `IdleRate` through the
registry doubles a payout with no code path of its own; a scope that stayed enabled is NOT paid idle on top
of what it produced live; checksum rejects edits.

---

## 10 — Chapter 1 playable pass (wire it end-to-end)

> Tie Chapter 1 together into a playable first-run experience using the `progression` stages in the
> JSON as the spine (Stage 0 First Notes → Stage 7 Backyard Party), driven by the section layout from
> 3.5.
>
> - Show the `storyBeatOpen` card on first launch and the `storyBeatCapstone` at the capstone.
> - Ensure the staged reveal reads cleanly — each stage a flag/Condition drives a section or module in:
>   tap-only → first gear (`the_band` section at 100 Cash earned) → Fans (`fans` flag) → Rehearsal +
>   covers (`covers` flag) → Cut a Demo (`album` flag) → repeat → Garage Jam available → capstone at 30
>   Records. **[rev]** After 7.5 a module shows when `scope.activeWhen && section.visibleWhen && its own
>   condition` all hold, so check the reveal at all three levels — a section can be authored correctly and
>   still stay dark because its scope is inactive, and that is the first thing to look at when a stage
>   does not appear. Sections belong to a scope and are not scopes.
> - **[rev]** The `~5 min to first demo` target this bullet used to assert is **not met** — simulated
>   against the shipped numbers it is 12–18 min at human tap rates (`balanceTargets.timeToFirstDemoSeconds`
>   says 300). Do not treat the target as satisfied because the loop is playable. It is a tuning problem,
>   listed under "After this" below, and it is out of scope here — but do not paper over it either.
> - Minimal but legible UI, laid out through the module registry: current Cash/Fans/Rehearsal/Records,
>   generator rows, upgrade list, cover bars, Release button, event entry, collect screen. Use a
>   `NumberFormatter` for big-number display (1.23K / 4.56M / etc.).
> - Make the currency header data-driven: it currently names its currencies through hardcoded ids
>   (`GameManager.CashCurrencyId` / `FansCurrencyId` / `FansUnlockFlagId`, read by
>   `CurrencyHeaderModule` and `TapModule`). Replace those UI consts with display driven by the
>   chapter's revealed currencies, so a chapter with different currencies needs no UI code change.
>   (The fan SYSTEM already takes its currency/flag from the chapter's `fans` config — this is the
>   remaining UI half.) **[rev]** The same display absorbs `BarListModule`'s pool readout: the
>   fill-currency lines live there only as a stopgap (the module's own comment defers to this
>   slice), and once the header renders the chapter's revealed currencies, a second readout
>   answering "what do I have" from its own code path is a disagreement waiting for a divergence —
>   drop `_poolLabel` and let the one display carry the fill currencies' balances and rates.
> - **[rev]** Make module loading async and reveal-safe: `ChapterScreen.BuildSection` currently
>   blocks on `Addressables.InstantiateAsync(...).WaitForCompletion()` per module at boot. Load
>   asynchronously, but keep the invariant the blocking call was buying — a module is initialized,
>   with its subscriptions live, BEFORE its section can show — so a gate that holds while a prefab
>   is still in flight neither reveals an empty section nor costs the module the events it needed to
>   hear. That invariant is why `BuildSection` initializes modules that start hidden; async loading
>   must not reopen the gap the eager initialization closed.
>
> Goal: a new player can play Chapter 1 start to finish — tap, build, learn a cover, cut a demo,
> loop, do the event, and hit the Backyard Party capstone. Stop here.

✅ **Test & commit:** full Chapter 1 loop is playable end-to-end.

---

## After this

Chapter 1 is playable. The remaining work is a separate phase, roughly:
- **Tune** the pacing (time-to-first-demo, Records gate, cycles-to-capstone) by playing it — feel can't
  be judged from numbers alone. **[rev]** But four things *were* judged from the numbers and should be
  treated as known, not rediscovered by feel:
  - **Time to first demo is 12–18 min**, not the 300s `balanceTargets` states. The binding constraint is
    the release gate needing 50 Fans *and* a 120-Rehearsal cover when fan rate starts at 0.22/s and
    Rehearsal does not accrue until 25 Fans.
  - **The chapter takes ~2.2–3.4 h released-when-offered, or ~1.2 h in one long run.** Long runs win,
    which is backwards for a prestige loop.
  - **`estDemoCyclesToCapstone: "8-12"` is unreachable at both ends.** The 50-Fan offer gate guarantees
    `floor(sqrt(50/5)) = 3` Records minimum per demo, so 10 demos always clear a 30 gate — 10 is a hard
    *ceiling*, and 6–8 is typical.
  - **Cycles barely accelerate: 14.5 → 11.9 min across ten demos (18%)**, against §5's promise that
    "cycles get faster as Records accumulate." Root cause: `recordBuff.affects: ["cash"]` while the gate
    is Fans, and every reset zeroes the band that *is* the fan rate — so the prestige currency does not
    touch the bottleneck it exists to relieve.
  The scope tree (7.5) is what makes the fixes expressible — an intermediate currency with a real sink,
  prestige-bought generators filed one scope out, and Ctrl-C-style recovery accelerators that make each
  loop cheaper rather than merely making the player stronger. It does not decide any of the numbers.
- **Chapter 2** content (a new `chapter-02-*.json`) plus unlocking the Roadie allocation/replay UI.
  Because 3.5 made conditions, flags, rewards, bars, and layout fully data-driven and
  Addressables-discovered, Chapter 2 should be mostly new assets + JSON, not new systems.
- **Monetization SDKs** last (ads mediation + IAP), once the game is fun — wire the "Double it" and
  Encore/Backstage Pass placements that are already stubbed.

Keep `garage-band-idle-design.md` as the source of truth; when a decision changes while building,
update the doc so it and the code don't drift.
