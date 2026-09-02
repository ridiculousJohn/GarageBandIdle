---
name: reuse-the-existing-mechanism
description: "Never invent a second way to express what the architecture already expresses, and when two shapes both work take the smaller one; a covered case that seems to need a new mechanism is a conversation, not a decision to make alone"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: efd4f70d-d22b-4ea0-8736-2d55c0b412d5
  modified: 2026-09-02T18:19:41.223Z
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

A second instance, 2026-08-24: an event's ending list carrying a reset raised "should the reset be
intrinsic to a rung / a flag on the rung / a new run-ended concept," and the answer was that what
goes in an authored list is the AUTHOR's choice, covered already. John: "you're making this way
more complicated than it needs to be... inventing systems for things that already have clean
concise solutions." Content decisions - which actions a list holds, how a gate is composed, what a
failure costs - are authoring. Architecture is only what makes them expressible.

**How to apply:** The test is not "is this defensible" - it is "does the architecture
already do this", and then "is this even an architecture question, or is it what someone types into
an asset". Ids are for cross-references; placement, nesting, and declaration lists
express ownership and grouping. When a plan introduces a new shape, say out loud which
existing primitive it is NOT reusing and why, in the plan's first paragraph about it, not
buried. See [[no-spec-accumulation]] for the habit that hides these, and
[[quote-directive-before-editing]] for the standing edit protocol.

**The selection defect: the elaborate option always wins the pick (2026-08-24).** Laying out the
options is wanted - John's correction was exact: "by all means you should consider all options, just
pick the simpliest one." Four in one session, and he named the simple
option each time: a downcast `eventHost` accessor on the base instead of giving the tier its own
class; an untyped `ScopeDefinition` plus a depth test plus typed state classes instead of typed
definitions acting as their own factories; three hand-written `Facts` properties instead of one
generic base class; and constructor-parameter juggling instead of moving the initialize call to
where the field is assigned. Every one of them was mechanism added to work around a type that
should have existed.

So: weigh them, then take the smallest thing that satisfies the constraint. State the simple one as
the answer and the elaborate one as the alternative, not the reverse. A cast, a null check, a depth
test, or a runtime guard standing in for a type is the specific tell - the simple option is almost
always "add the class that was missing" ([[narrowing-a-member-type]] is the worked case). If the
elaborate option is genuinely right, the reason has to be a constraint that breaks the simple one,
not a benefit the elaborate one adds.

**A doc row naming a mechanism is not a reason to build it (2026-09-02).** The 12.6 table listed
timed buffs as a SOURCE of effects and a tick comment said "until the timedBuffs gather row lands",
so the step 10 plan built a fifth gather loop, and the Pass's permanence then had nowhere to sit
but on the modifier's `appliesWhen` - three exchanges deriving why that failed. John: "can't it be
'Owns the pass' OR 'timer > 0'?" - `encore` as a permanent membership whose `appliesWhen` is
`Any[FlagSet(pass), BuffActive(encore)]`, the `idle_base` shape one file away. A stored record is
a FACT; facts are read by CONDITIONS; a modifier with an `appliesWhen` is already "effects while
some fact holds". John: "why did you make that so complicated, again?" Before planning a new source
of effects, ask whether a condition over the fact inside an existing `appliesWhen` says the same
thing - the doc's own table was written before `appliesWhen` existed and describes the fact, not
the only way to read it.
