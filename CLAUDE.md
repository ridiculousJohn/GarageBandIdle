# Garage Band Idle

`Docs/garage-band-idle-design.md` is the authority for architecture. Section 12 is the
implementation spec; `Docs/build-plan.md` owns the order and the current position.

## Scope is the whole lookup mechanism

Resist the urge to add a registry, an id index, a label-based catalogue, or a "find all X" pass.
Every time this project has needed a repair, that urge is what caused it: a global content database,
tree-wide id uniqueness to prop it up, and a chain of aliasing questions that followed. All of it
was deleted.

The rule is design doc section 12.14 requirement 8. In short: everything is declared on a scope, and
a name resolves by walking OUTWARD from the acting scope to the first scope that declares it. Two
walks are legitimate, because both start from a scope the caller already holds - outward along the
chain, and downward through one named subtree. Nothing else is. Validation is the one exception: it
audits the whole tree at load, once, which is what lets every runtime walk trust its own chain.

If content seems to need a lookup, it has not been placed on a scope yet. That is a conversation
before it is code.
