---
name: pick-the-simplest-option
description: Considering alternatives is wanted; the defect is that the most complicated one always wins the pick
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 27d7abf2-59c2-44b9-9505-eddad1ef18c1
  modified: 2026-08-24T22:37:36.906Z
---

Lay out the options - John wants that. The failure is the selection: given a simple option and
an elaborate one that both work, the elaborate one gets picked, every time. Weigh them, then
take the smallest thing that satisfies the constraint.

**Why:** 2026-08-24, the scope-state retype. His correction was exact: "by all means you should
consider all options, you just ALWAYS pick the most fucking complicated one." Four in one
session, and he named the simple option each time: a downcast `eventHost` accessor on the base
instead of giving the tier its own class; an untyped `ScopeDefinition` plus a depth test plus
typed state classes instead of typed definitions acting as their own factories; three
hand-written `Facts` properties instead of one generic base class; and constructor-parameter
juggling instead of moving the initialize call to where the field is assigned. Every one of
them was mechanism added to work around a type that should have existed.

**How to apply:** When two shapes both work, state the simple one as the answer and the
elaborate one as the alternative, not the reverse. A cast, a null check, a depth test, or a
runtime guard standing in for a type is the specific tell - the simple option is almost always
"add the class that was missing." If the elaborate option is genuinely right, the reason has to
be a constraint that breaks the simple one, not a benefit the elaborate one adds. Related:
[[no-spec-accumulation]], [[reuse-the-existing-mechanism]].
