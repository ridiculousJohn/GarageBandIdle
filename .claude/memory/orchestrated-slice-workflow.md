---
name: orchestrated-slice-workflow
description: "How implementation work is run here - I orchestrate and review, Opus agents do the bulk, I run Unity and own everything the agents do"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:48:23.774Z
---

John: "you are the orchestrator. use Opus-5 agents as your coding agents. You run code reviews. It
is your responsibility to approve their work." The global `CLAUDE.md` holds the standing agent rules
(model, brief contents, one checkout, disjoint files). This is what this project adds.

**Why:** the coding is delegated; the judgment is not. Every agent item passes through my review
before it reaches him, and the report is mine, not a relay.

**How to apply**
- Agents do the bulk of an implementation. Small edits in this context are fine (a word in a
  comment, one assertion). Say what is delegated as it happens, so the work does not look as if it
  is all being done here.
- Before briefing: read the code the change touches and fix the contract myself - exact ids, names,
  signatures and semantics. Settle every open point first ([[ask-before-the-work-not-after]]).
- Run the content or runtime agent and the tests agent in parallel on disjoint files against that
  contract. Neither runs Unity or git; I run one import and one test run
  ([[unity-headless-verify-loop]]).
- Briefs carry the house rules in full, because the agent has none of them: ASCII, no delta
  framing, no lookups or registries, tests drive the real runtime, no file deletions.
- Review reads every diff against the design rule it implements
  ([[verify-your-own-prior-statements]]).
- An agent's "I could not trace this" or "I could not settle this": first run the four tests in
  [[ask-before-the-work-not-after]]. What survives as a real pick goes to John BEFORE the suite runs
  and before any report of completion. Never resolved silently, never filed under "decisions made".
- Filter agent line items before the report ([[problems-not-issues]]).
- Everything an agent does is mine: its processes, its files, its mistakes.
