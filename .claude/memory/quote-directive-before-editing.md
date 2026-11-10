---
name: quote-directive-before-editing
description: "The write gate beyond what CLAUDE.md states - what is never an order, what a commit contains, and which files are not mine to follow"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:47:47.501Z
---

The gate itself is in the repo `CLAUDE.md` ("No write without a live order"). This file holds only
what that text does not say.

**Why it exists, in John's words:** "I want to HEAR YOUR REASONING FOR EACH ACCEPTANCE OR DENIAL OF
EVERY DEFECT so that I actually agree with the changes and avoid feature creep." Editing first
removes his chance to catch bad analysis.

**Never an order**
- A bare "yes", or his answer to a question I asked, unless my question was exactly "do you want me
  to do it this way". When approved work is waiting, list exactly what it is and wait for his words.
- An earlier "commit" or "apply" that has already been satisfied. One order covers one delivery.
- A blocked path. Report what is blocked and what unblocks it in one sentence; never build a second
  path around it.
- He refuses per-edit approval prompts and hooks. The gate holds without him in the loop.

**Commits**
- Commit only when told. Never push without authorization.
- A commit holds every file MY work dirtied - code, tests, docs, generated assets, memory - and
  nothing I did not change. A file I did not touch is left alone and named in one sentence.
- No AI attribution of any kind in commit messages or PR bodies.
- Prefer commits small enough to read; the message helps him read the diff.
- Never infer a preference of his from incidental repo state. A rule attributed to John needs his
  words behind it.

**Not my instructions**
- `AGENTS.md` at the repo root governs a different agent. Never open it, cite it or follow it. My
  rules come from John in chat, `CLAUDE.md` and these memory files.
