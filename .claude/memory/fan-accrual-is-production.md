---
name: fan-accrual-is-production
description: "What slice 5.7 established in Garage Band Idle - FanSystem retired, section 9's no-idle-fans promise made structural, and the two rules a review had to catch"
metadata:
  node_type: memory
  type: project
---

Slice 5.7 is committed (`1227bab`, 2026-08-04) and confirmed in Play: `FanSystem` is deleted. Fan accrual is a tick config on a `band` producer that carries no module address, composing `FanRate`, gated `ownedCount drummer >= 1`; the per-bandmate bonus is `BandmateFanRateModifier`, a `DerivedModifier` adding on the global `FanRate` target. Composed: `(0.2 + 0.02n) x rewards`. Follows [[reveal-is-a-condition]].

**Why:** the build-prompts doc says what 5.7 was asked to build. It cannot say which guarantees changed character, or why one rule now lives in a single place.

**How to apply:**

- **"Fans never idle-pay" is now structural, not remembered.** Section 9's boundary is the HOLDER: only generator-held configs idle-pay. A module-less producer is still module-held in that sense, so slice 9's idle payout needs no fans exclusion and there is no per-config idle flag to author or get wrong. Do not reintroduce a fans-specific tick.
- **A producer with no `ModuleAddress` is a passive source.** Nothing presents it. Two sites permit it - the importer's producer loop and `ContentValidator.ValidateProducer` - and both still REQUIRE production, since a producer with neither would do and show nothing. Hanging fan accrual on the jam producer instead was considered and rejected: behaviorally fine (configs fire from the chapter's producer list, not from visible modules) but it encodes a lie and breaks the first chapter with fans and no Jam button.
- **`ProductionConfig.IsComposable` is the ONLY home for what a config may compose.** `None` is legal (raw amount); anything else must be a defined target that does NOT require a qualifier, because a config composes through `ModifierTargetKey.Global(kind)` - a qualified target like `GeneratorOutput` composed globally reads an empty bucket and scales by nothing, which is worse than a refusal because it looks like it worked. `ProductionSystem` and `ContentValidator` both ask it. They briefly did not, and Chapter 1's own band producer failed boot validation as a result.
- **One guarantee moved from impossible to checked.** Records could never reach the fan rate while `FanSystem` only composed `FanRate`; now the only thing keeping it off is `recordBuff.affects`, so `ContentValidator` refuses the chapter's fans currency there (section 11: time away must not shortcut the Records payout).
- **Refuse a stale JSON key on PRESENCE, never contents.** The three retired fans keys carry no field initializers, so null means absent and `"activeWhen": {}` / `"revealFlag": ""` / `baseFansPerSec: 0` are all caught. A contents test waves through the emptiest spelling - the one least likely to be spotted by eye. `IsImportableBarGroup` had this hole for `"revealFlag": ""` and it is closed (2026-08-05): the fix is TWO coupled edits, the `== null` check and dropping the DTO's `= ""` initializer, because changing only the check makes an absent key read as `""` and refuses every bar group.
- **`RealChapterContent_PassesBootValidation` is the only thing validating shipped content outside Play.** The importer does not run `ContentValidator`, and every other validator test builds its own broken fixture - which is exactly how a boot error sat in the tree through a green suite. It repeats `GameManager.Awake`'s four steps over the real Addressables database and expects silence. Keep it green; a rule shipped content must satisfy has to be exercised against shipped content.
- **Next: slice 6 (album prestige).** Its release walks each system's facts, and 5.7 removed one of them. See [[project-layout-and-workflow]] for the slice workflow and [[unity-headless-verify-loop]] for verification.
