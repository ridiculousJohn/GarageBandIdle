---
name: quote-directive-before-editing
description: "Standing protocol - before any change to John's repo (edit, write, commit, add, reset, branch, push), state the directive being acted on, quoted from John's own words; no quotable directive = no change"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 2cb43079-7ede-4654-8e74-32228598a513
  modified: 2026-08-13T22:20:09.156Z
---

Agreed protocol (2026-07-24): before changing anything in the repo, the
message must state the directive being acted on, quoted from John's words
(e.g. Acting on: "fix the label"). If there is no directive to quote, no
change happens - deliver analysis instead. Scratchpad and .claude/memory
writes are exempt; everything under the repo is covered.

**A COMMIT IS A CHANGE.** So is `git add`, `reset`, `branch`, `push`, or any
other repo-state mutation - the protocol is not about file contents, it is
about John's tree moving. Amended 2026-08-13 after committing a doc fix off
"fix item 2": the fix had a directive, the commit did not, and the previous
turn's "commit that" was treated as if it carried forward. It does not.
Approval attaches to the one change it was given for and expires with it -
which is the same rule his CLAUDE.md states as "his fine-grained commit
cadence is his habit, not standing authorization: commit only when he asks."

**Why:** John cannot trust self-discipline claims after repeated unrequested
edits ([[bug-reports-are-verify-only]]), and per-edit approval mode is
babysitting he does not want. Quoting the mandate makes an invented one
loudly visible in one glance, before or as it happens, instead of after his
tree changed. He has said this costs him time every day; reading the protocol
narrowly and finding a category it does not literally name is how the same
failure keeps recurring under a new label.

**How to apply:** First line of the message containing the first repo change
of a task: Acting on: "<his words>". Multi-turn tasks carry the original
quote forward for the WORK it authorized, never to a new kind of action. If
the quote would have to be paraphrased or inferred, stop and ask instead -
including when the next step seems obviously implied by the last one.
