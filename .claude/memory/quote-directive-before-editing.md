---
name: quote-directive-before-editing
description: "Standing protocol - before any Edit/Write to project files, state the directive being acted on, quoted from John's own words; no quotable directive = no edit"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 2cb43079-7ede-4654-8e74-32228598a513
  modified: 2026-07-24T17:37:05.279Z
---

Agreed protocol (2026-07-24): before touching any project file, the message
must state the directive being acted on, quoted from John's words (e.g.
Acting on: "fix the label"). If there is no directive to quote, no edit
happens - deliver analysis instead. Scratchpad and .claude/memory writes are
exempt; everything under the repo is covered.

**Why:** John cannot trust self-discipline claims after repeated unrequested
edits ([[bug-reports-are-verify-only]]), and per-edit approval mode is
babysitting he does not want. Quoting the mandate makes an invented one
loudly visible in one glance, before or as it happens, instead of after his
tree changed.

**How to apply:** First line of the message containing the first Edit/Write
of a task: Acting on: "<his words>". Multi-turn tasks carry the original
quote forward. If the quote would have to be paraphrased or inferred, stop
and ask instead.
