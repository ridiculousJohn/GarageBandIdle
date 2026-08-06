# Garage Band Idle — Claude Code build prompts (Chapter 1 → playable)

Feed these to Claude Code **one at a time**, in order. After each: run it in the Unity Editor,
confirm the stated goal, then `git commit`. Don't move to the next slice until the current one works.

**Setup assumptions:** empty Unity 6000.5.4f1 2D project created in Hub; `git init` done;
`garage-band-idle-design.md` and `chapter-01-garage.json` sitting in `/docs`; Claude Code opened in
the project root. The design doc is the source of truth — every prompt references its sections.

Build order and why: each slice depends on the ones before it (offline earnings need the real-time
tick; prestige needs the currency block split; the content-unlock upgrades are what reveal
fans/covers/album). Building bottom-up keeps a break isolated to the slice you just added.

**Progress marker:** slices 0–5, **5.4**, **5.5**, **5.6** and **5.7** are already built and tested. Slice **3.5** is a dedicated consolidation pass
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
still play identically.

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

## 7 — Records manager + capstone / chapter gate

> Implement Records tracking and the chapter capstone per §1–§2, §5 and the `capstone` entry.
>
> - `RecordsManager` tracks cumulative Records and exposes the permanent income multiplier.
> - The capstone `unlock` is a `recordsCumulative ≥ 30` Condition (same evaluator, same type the event
>   availability uses). When it holds, unlock "Play the Backyard Party."
> - On capstone completion: first perform the standard album release (slice 6's path — the run's
>   Fans bank as Records; design §1–§2: the capstone implicitly cuts an album, so no run value is
>   stranded at the chapter boundary); then grant 1 Roadie to a permanent pool but keep the Roadie
>   allocation/replay UI LOCKED (`roadieSystemUIUnlocked: false` — deferred to Chapter 2); fire the
>   `storyBeatCapstone` text; set an "advance to Chapter 2" flag (Chapter 2 content doesn't exist
>   yet — just mark it).
>
> Goal: reaching 30 cumulative Records unlocks the capstone; playing it shows the story beat, banks
> the first Roadie (no allocation UI), and flags chapter advancement. Stop here.

✅ **Test & commit:** capstone unlocks at 30 Records via `recordsCumulative`; the implicit release
banks Fans as Records; story beat fires; Roadie banked; advance flag set.

---

## 8 — Events (Garage Jam Challenge)

> Implement the event system per §6.1 and the `events` array (garage_jam).
>
> - `EventManager` runs a self-contained challenge in a **freshly constructed economy context**
>   (design §12 rule 12), never in the player's run: on entry, build an event context whose recipe
>   projects the chapter's permanent-in-chapter facts only — earlier tiers' rewards apply, no run
>   facts carry in, and the Records buff (a *derived* modifier, §12 rule 11) is never registered
>   because the recipe names no global facts. That absence IS the fixed baseline: nothing is filtered,
>   nothing is reset, and the suspended run — balances, lifetime-earned totals, modifiers, the Records
>   balance itself — is never touched. On quit, fail, or success the context is discarded; nothing to
>   unwind makes failure and quit free, and the sandbox's earnings die with it instead of polluting
>   the run's earned-total gates.
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
>   the permanent block holds Records, `contentUnlock` effects, **flags**, Roadies, and the
>   permanent-in-chapter **facts** (the cleared event tiers are the Ch1 case; their tap buffs
>   re-project on load). An album release clears the run block and writes the permanent block.
> - **No modifier is ever serialized.** Derived modifiers compute from a source on every read, so the
>   Records buff comes back from the restored Records balance with nothing to save or migrate; writing
>   one would create a second answer that can disagree with its source. Grants are the same decision,
>   already made (design §12 rules 11–12): the save records only the facts that produced them — which
>   buffs are bought, which bars completed, which tiers cleared — and each economy context re-projects
>   its grants from those facts at construction. One source of truth: an effect can never disagree
>   with the fact that produced it.
> - `IdleEarnings` (design §9, per-economy): each economy context stores a last-interaction
>   timestamp in its state block. On focus-gain — an app launch is just a focus-gain on the one Ch1
>   context — pay `generatorProductionPerSecond × min(idleSeconds, cap) × rate` using `DateTime`
>   deltas, rate = 0.5, cap = 4 hours, and nothing below a minimum idle threshold. Generator
>   production only: fans, rehearsal, and bars pause while unfocused. Show a collect screen with the
>   amount and a placeholder "Double it" button — a timed double-idle buff (an expiry fact modifiers
>   derive from, §12 rule 11), not a per-collect double; wire the actual ad later. With one chapter
>   this behaves exactly like classic offline earnings, but written against the context, chapter
>   switching (Ch2+) needs nothing new.
>
> Goal: closing and reopening restores state (including flags and bar progress); time away grants
> idle Cash at 50% capped at 4h, and a below-threshold absence pays nothing; a tampered save is
> rejected. Stop here.

✅ **Test & commit:** state persists across restart; flags/bars restore; idle payout correct, capped,
and zero below the threshold; checksum rejects edits.

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
