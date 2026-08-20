---
name: reuse-the-existing-mechanism
description: "Never invent a second way to express what the architecture already expresses; a covered case that seems to need a new mechanism is a conversation, not a decision to make alone"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: efd4f70d-d22b-4ea0-8736-2d55c0b412d5
  modified: 2026-08-20T18:18:14.719Z
---

Before adding a field, a type, an id indirection, or any new mechanism, name the existing
primitive that already covers the case. If one exists, use it. If it genuinely does not fit,
STOP and raise it with John before writing the alternative - "there is a reason to do it
differently" is a decision you two reach together, never one to make and explain afterward.

**Why:** Feature creep destroyed the first attempt at this project. A parallel mechanism is
not a defensible local tradeoff - it is two ways to say one thing, and every later reader
has to learn both. The concrete instance (2026-08-20): step 1 gave `BarDefinition` a
`groupId` id-reference when scope placement ALREADY groups things - the system's whole
grouping mechanism - and step 5 then framed removing it as a decision needing John's nod
rather than as a correction. John had to spend attention on a fork that should not have
existed, in a doc long enough that he had to scrutinize every word to catch it.

**How to apply:** The test is not "is this defensible" - it is "does the architecture
already do this". Ids are for cross-references; placement, nesting, and declaration lists
express ownership and grouping. When a plan introduces a new shape, say out loud which
existing primitive it is NOT reusing and why, in the plan's first paragraph about it, not
buried. See [[no-spec-accumulation]] for the habit that hides these, and
[[quote-directive-before-editing]] for the standing edit protocol.
