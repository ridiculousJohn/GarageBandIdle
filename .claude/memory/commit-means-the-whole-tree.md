---
name: commit-means-the-whole-tree
description: "What a commit is here: the whole dirty tree including memory files, no AI attribution trailers, small enough to read - and never a preference inferred from incidental repo state"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 686db24c-b6c5-412c-aea3-669efcf46f19
  modified: 2026-08-28T20:24:53.411Z
---

**"Commit" means the whole dirty tree, memory files included.** 2026-08-25: on "commit" I committed
only the Docs files and left this conversation's memory files dirty, reasoning "that's been the
pattern so far." There was no pattern - one memory file happened to be dirty at session start
because the prior commit order had already been satisfied before I edited it. I promoted an accident
of timing into a preference John never expressed, then acted on it. He has never said to exclude
memory files from a commit. If something dirty genuinely looks like it should not ship (a scratch
file, someone else's half-work), say so and ask - do not silently curate.

**No AI attribution, ever.** John rejected the Co-Authored-By Claude trailer ("without the
'co-authored by Claude' bullshit") and had attribution disabled permanently: commits are his history
and AI attribution is noise in it. No Co-Authored-By trailer, no "Generated with Claude Code"
footer, nothing equivalent, in commit messages or PR bodies. Enforced in ~/.claude/settings.json
(`attribution.commit` and `attribution.pr` empty) since 2026-07-21 - but never author one by hand
either, even if the setting disappears.

**Size is part of the obligation.** Reading the diff is how he stays familiar with his own codebase,
so prefer several small commits that each stand alone over one unreviewable refactor, and write the
message to help him read the diff rather than to substitute for it. The gate on WHEN to commit
(never unasked) lives in [[quote-directive-before-editing]].

**Why the inference half matters:** a rule attributed to John needs his words behind it, exactly
like a write needs a quoted order. Inferring his preferences from incidental repo state is inventing
constraints he never set - the same defect as citing AGENTS.md ([[agents-md-is-not-a-claude-file]]),
just sourced from the tree instead of a file, and the same as promoting my own recommendation into a
decision ([[asides-do-not-close-the-main-question]]).

**How to apply:** before acting on any "John prefers X" belief, find the quote; no quote means it is
my invention.
