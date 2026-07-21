---
name: no-ai-attribution-in-commits
description: "John: never add Co-Authored-By Claude trailers or AI attribution to commits or PRs"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 72f7735f-b559-4457-a686-45b8e3da765f
---

John rejected the Co-Authored-By Claude trailer on a commit ("without the 'co-authored by Claude' bullshit") and had attribution disabled permanently.

**Why:** He considers AI attribution noise in his history; commits are his.

**How to apply:** Write commit messages and PR bodies with no Co-Authored-By trailer, no "Generated with Claude Code" footer, and no other AI attribution. This is enforced in ~/.claude/settings.json (`attribution.commit` and `attribution.pr` set to empty strings) as of 2026-07-21, but also never author such trailers manually even if the setting disappears.
