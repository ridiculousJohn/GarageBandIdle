---
name: fact-addressing-is-id-plus-outward-walk
description: "Runtime fact address = id + acting-scope outward walk; direct references constrain authoring and validation, they do NOT make asset identity the runtime address"
metadata: 
  node_type: memory
  type: project
  originSessionId: 7994d8cd-29fb-4920-8032-e10d58f56a96
  modified: 2026-08-21T17:55:44.583Z
---

**`address = id + acting-scope outward walk`.** Every runtime fact lookup resolves a NAME by walking
outward from the acting scope and stopping at the first scope that declares it. Direct asset
references constrain AUTHORING and give validation something to check reach against; they do not
redefine addressing as asset identity. Chain uniqueness plus the `ChainReach` rule make the two
extensionally identical over accepted content, because a chain is a path: at most one asset answers
to a name from any scope.

The one genuine exception is the DOWNWARD production walk. `GetRate` descends a subtree and so spans
sibling chains at once, where two chapters' `cash` producers coexist; `SourceTerm` compares
`entry.currency != currency` by REFERENCE, and that comparison is load-bearing. Everything else
walks outward, so nothing else needs identity. `FindCurrencyHome` resolves by reference too, for a
different reason: its caller already holds the asset, and passing only the id threw away information
the callee then had to re-derive.

**Aliasing across chains is the FEATURE.** A modifier at root targeting `cash` applies to every
chapter's `cash`. That is what placing an effect at root means, and it is the same rule the effect
coordinates follow - a target string is a filter over candidates the gather already produced, id or
tag, possibly many, each right where it sits.

**Why:** 2026-08-21. Two review rounds reported "direct references discard identity" as a P2, and I
built a typed `GameContext` layer (nine methods, fifteen call sites, modifier grant checks) before
John asked why aliasing was a problem at all. It was not. The whole thing was backed out. The
reviewer withdrew both findings with the diagnosis: the findings promoted behavior for REJECTED,
UNVALIDATED content into a runtime requirement, contrary to the build policy that release ships
dev-validated content. My own escalation was worse - I proposed making ids unique TREE-WIDE, which
is the same move as the deleted global content database: reach for a global guarantee instead of
trusting the scoped lookup. See [[reuse-the-existing-mechanism]] and [[no-spec-accumulation]].

**How to apply:** Before "hardening" a fact lookup, ask which walk it is. Outward means the name is
sufficient and identity machinery is redundant. Ask also whether the failing case is content the
validator already refuses - if so, the fix belongs at load, not at every read. A rule that must be
re-applied at each call site is not an invariant.
