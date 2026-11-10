---
name: design-judgment
description: "How to judge a mechanism in this project - reuse what exists, smallest mechanism, systems true of every instance, one vocabulary, delete instead of specify"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T22:29:24.924Z
---

**Why:** feature creep killed this project's first attempt, and every repair since has been removing
a mechanism I added. The repo `CLAUDE.md` states the lookup rule; this is the rest.

**Reuse before building**
- Before adding a field, type, id, walk or any mechanism, name the existing primitive that covers
  the case and read its class and callers. If one exists, use it. If none fits, stop and raise it
  with John before writing the alternative.
- Before naming a thing as its own kind, say which existing family it is an instance of (a "buff" is
  a modifier). A family that already scopes, validates and saves leaves nothing to build.
- Content decisions are authoring, not architecture: which actions a list holds, how a gate is
  composed, what an event pays, what a failure costs. If the engine already lets content author it
  either way, that is each chapter's design decision. It gets no new rule and no restriction.
- A stored record is a fact, facts are read by conditions, and a modifier with `appliesWhen` is
  already "effects while a fact holds". Check that shape before planning a new source of effects.
- Truth is data already in hand (a timestamp the context carries), never the presence of a record
  plus an ordering rule.
- A number is collected by the thing it belongs to: a currency, bar or generator count collects its
  payers and its modifiers. Before reporting a design "gap", restate it as the target collecting.
- A field or table row that the doc's prose lists is built only if some reader cannot be served by
  data already in hand. Otherwise the doc's list is corrected, not implemented.

**The design leads, the engine follows**
- Never reshape a chapter's design so that it fits what the code can already do. On 2026-11-10 I
  proposed that Cash survive chapter 3's scene prestige because a reset could not reach it, then
  argued it "suited the chapter". John: "trying to change the design from what is designed in a way
  that just 'makes it work' instead of makes sense."
- Order of work on a gap: first say what the mechanic is conceptually and why it makes sense in
  play; then say what the engine lacks; then the smallest engine piece that supplies it. A
  no-code substitute is offered only if it also makes sense on its own, and it is labeled as a
  change to the design.

**Pick the smallest mechanism**
- Weigh the options, then take the smallest MECHANISM that satisfies the constraint and state it as
  the answer. Diff size is never a design reason; "it was the smaller edit" reads as feature creep.
- When proposing a fix, describe the smallest change that makes the finding false.
- The elaborate option needs a constraint that breaks the simple one, not a benefit it adds.

**Systems, not tasks**
- A mechanism is justified only by a statement true of every instance of its kind ("a bar is a
  target that fills at the rates paying it"). Never by what a chapter, step or test needed; "it was
  what chapter N needed" is banned. If the only justification names a task, the mechanism is wrong.
- A mechanism in the live path is judged on its structure, not on what current content happens to
  feed it.
- Do not build a mechanism, policy or check for a state no content reaches. That is premature.
- A node-kind restriction on an economy read ("only chapters", "only tiers") is a deviation from
  the scoping design, whoever wrote it. Every aggregation compiles per scope.

**Scope walks**
- A name resolves outward from the acting scope. A root effect reaching every chapter's `cash` is
  the feature, not an aliasing bug.
- Any traversal that does not start at the acting scope and go outward is either a propagation the
  child answers for itself (a reset) or a read that belongs at the scope owning the fact.
- Before hardening a lookup, ask whether the failing case is content the validator already refuses.
  If so the fix belongs at load, not at every read.

**Player commands**
- A player action owns its mutation and the flush that settles banked time before it. It never
  reads a clock, compares times, or writes anything the tick owns (the sample, the bank, the report).

**Delete instead of specify**
- When a review finds a hole, first ask whether the mechanism should exist. Deleting it closes the
  finding; specifying around it grows the spec.
- The same operand producing a finding in two consecutive review rounds means delete it.
- Every refusal, validator error or guard names the thing it prevents from going wrong today. No
  such thing, no restriction. A plan sentence is not a reason.
- When trimming a doc, cut argument. Never cut a sentence that names a check, a rule or a test.

**One vocabulary**
- A thing has one name, the design doc's, in code, docs, plans and chat. "Tap" is the Jam button.
  If existing code carries an invented word, the change that touches it renames it.
- When John asks what something is, answer in game words (a line on a producer, a Garage Jam, Demo
  Tapes), never architecture words (a coordinate, a holder, an event, a prestige currency).

**Qualifiers describe, they do not specify**
- A phrase like "(like now)" names the state he is looking at, not a condition to build. If no
  other state is reachable, implement the rule without it.
