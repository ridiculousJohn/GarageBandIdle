---
name: narrowing-a-member-type
description: "How a derived class narrows a base member's type here: generic base class, not covariant override (unavailable) and not `new` hiding (rejected)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 0af2b0a1-2929-45fb-8145-6601f4a8a0ed
  modified: 2026-08-25T00:40:36.783Z
---

When a derived class needs a base member at its own concrete type, the shape is a generic base
class parameterized on that type - `ScopeState<TFacts>` holding `Facts => (TFacts)facts`, with each
leaf naming its type argument. Two alternatives are closed:

- **Covariant return overrides do not compile in Unity.** `public override TDerived Member` against
  a base declaring `TBase` gives `error CS8831: Target runtime doesn't support covariant types in
  overrides`. C# 9 has the feature but it needs .NET 5 runtime support; this project is
  `apiCompatibilityLevel: 6` (.NET Standard 2.1), which is as high as Unity goes. Not a setting to
  flip. It is native in C++, which is why it looks like it should work.
- **`new` hiding is rejected.** John, 2026-08-24: "I absolutely hate that 'public new' shit. That's
  garbage and shows a fucked up API." A generic helper that casts at one boundary
  (`DefinitionAs<T>()`, used only by the save's write path) is acceptable where the caller
  statically knows the type; a `new` member is not.

**Why:** both were tried and reverted in one session, costing two Unity round trips and a wrong
claim to John that the covariant version worked.

**How to apply:** reach for the generic base first. If the narrowing is needed at exactly one
boundary rather than throughout, a single generic accessor beats spreading casts across call sites -
but check whether the site needs the narrow type at all, since the answer is often that a signature
upstream should have carried it. Related: [[pick-the-simplest-option]].
