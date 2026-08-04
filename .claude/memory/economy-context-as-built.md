---
name: economy-context-as-built
description: "What slice 5.5 actually established in Garage Band Idle's runtime, which of its guarantees are only test-covered, and what 5.6 and slice 6 inherit"
metadata:
  node_type: memory
  type: project
---

Slice 5.5 is committed (`f12ba3e`, 2026-08-04): one permanent pool plus one frontier `EconomyContext` built by `EconomyContextFactory` from (chapter, database, permanent pool, recipe). `GameManager` keeps only the database, the permanent pool, tick routing, focus, and thin `Jam`/`BuyUpgrade`/`BuyGenerator` routes. Supersedes the pre-5.5 normalization file: passes A-D and 5.5 are all in, and the build-prompts doc records them.

**Why:** the doc says what 5.5 was asked to build. What it cannot say is which of 5.5's guarantees have actually run in the game versus only in tests, and that distinction is what the next slice needs.

**How to apply:**

- **Two guarantees are test-only, never yet exercised in play.** (1) `EconomyContext.ProjectModifiers()` after construction - nothing in production calls it, because the release does not exist until slice 6, so the rebuild path has only ever run on an empty store. (2) Focus switching with more than one context: `SetFocus` has only ever seen one, so "exactly one focused" and the `Unfocus` timestamp are covered by `EconomyContextTests` alone. Slice 6 is the first thing that makes (1) real; slice 8 does the same for (2). Do not treat either as field-proven.
- **Re-projection is the only door a modifier enters through.** `ModifierSystem.ResetRunScoped()` is DELETED; `ResetGranted()` is total (grants only - derived modifiers are untouched, their lifetime being their source's). A boundary resets the facts it owns and calls `ProjectModifiers()`; nothing filters the store. `UpgradeSystem.ResetRunScoped()` stays, because a purchase latch is a fact. A release written as "remove the run-scoped grants" is the mistake this shape exists to prevent.
- **The totality obligation is structural, not an assertion.** `EconomyContext` derives its projection list by filtering the systems it holds for `IModifierFactSource` (implemented by `UpgradeSystem` and `BarSystem`), so holding a fact source and not projecting it is inexpressible. The gap that remains, named in the code comment: a future slice could construct a fact source and never pass it to the context. A new fact class (slice 8's cleared event tiers) needs only to implement the interface and be a constructor parameter.
- **Systems take `ICurrencies`, not `CurrencyManager`.** `CurrencyRouter` resolves an id to its owning pool at construction and aggregates both pools' `BalanceChanged` into one event. The interface is also why single-pool fixtures still work: they hand over a flat `CurrencyManager`, which implements it. Never give a system both pools and let it choose - choosing at the call site means choosing from the currency's name, which is what rule 12 forbids. Shadowing (an id in both pools) is refused at construction, not resolved.
- **Placement is enforced at exactly one point.** `CurrencyGroupDefinition.Placement` (`None`/`Chapter`/`Global`) decides the pool; `ContentValidator.ValidateCurrencyPlacement` is the only check, because group assets are hand-authored and no importer generates them. Roster checks (unresolvable id, global id in a chapter roster, shadowed id) live in the factory, at construction.
- **The importer resolves a currency asset by ID, not filename** (`LoadOrCreateCurrency`). `Cash.asset` and `Fans.asset` are hand-authored with capitalised names and carry a symbol and decimal count no JSON field expresses; resolving on `{id}.asset` would create a second `cash` asset on a case-sensitive filesystem. Applies to any future hand-authored asset of a kind the importer manages.
- **Next: slice 5.6, then slice 6.** 5.6 retires the last three bare `revealFlag` fields and must land before 6, which would otherwise give `album.revealFlag` its first consumer and make it four sites. See [[project-layout-and-workflow]] for the slice workflow and [[unity-headless-verify-loop]] for verification.
