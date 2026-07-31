---
name: agents-md-is-not-a-claude-file
description: "AGENTS.md in the repo root is not a Claude directive file - never read it, cite it, or treat it as rules"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: de3a8a72-b32c-41c3-b489-c1620970bc9b
  modified: 2026-07-31T20:17:08.595Z
---

`AGENTS.md` at the Garage Band Idle repo root is not mine. Do not open it, do not cite it, and never treat anything in it as a rule governing my behavior. Skip it when orienting in the repo (see [[project-layout-and-workflow]]).

John has had to say this more than once (most recently 2026-07-31), so weight it accordingly: it is a standing prohibition, not a preference.

**Why:** it addresses a different agent, so reading it imports rules John never gave me. Worse, it makes me attribute constraints back to him that he never set - I told him "AGENTS.md says I don't run Unity tests unless instructed" and presented that to him as his own constraint on my work. That inverts the direction of authority: my rules come from John in chat, from `CLAUDE.md`, and from these memory files. Nothing else in the repo is a source of directives.

**How to apply:** if a supposed rule turns out to exist only in `AGENTS.md`, it is not a rule for me - drop it rather than honoring it, and ask John if it seems like something he would actually want. My real verification latitude for this project is [[unity-headless-verify-loop]], which records no permission gate on running the headless suite.
