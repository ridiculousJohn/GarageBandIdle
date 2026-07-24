---
name: bug-reports-are-verify-only
description: "A bug report or code-comment finding from John means verify and report a verdict - never edit, never revert, no unrequested actions"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 2cb43079-7ede-4654-8e74-32228598a513
  modified: 2026-07-24T17:33:53.710Z
---

When John gives a bug report, a code-comment annotation, or an audit finding,
the deliverable is a verdict: confirmed or not, with evidence, plus the fix
shape in prose if he asked for one. Nothing else. Imperative phrasing inside
a finding body ("render X, subscribe to Y") describes the fix's shape - it is
not a directive to implement. (2026-07-24: implemented a fix from a finding
without a go-ahead, then attempted an unrequested revert, then kept issuing
tool calls after being told to stop.)

**Why:** His workflow is conceptualize, confirm, then implement on explicit
go-ahead. Unrequested edits - and equally unrequested reverts - take decisions
that are his. After a denied tool call, freeze: no follow-up actions, answer
in words only.

**How to apply:** On any finding/report: read-only verification, short verdict,
stop. Edit only on an explicit "fix it". Keep answers to the question asked -
no unrequested background explanation.
