---
name: no-inaction-epilogues
description: Never close a response by stating what you did not do; it is wasted text
metadata: 
  node_type: memory
  type: feedback
  originSessionId: facbda27-5ff8-43e7-8f5f-aef0a5155adc
  modified: 2026-07-24T19:36:21.679Z
---

Do not end a response with a statement of what you did not do - "nothing edited", "no commits made", "I will make no changes until you say so". John called these wasted text on 2026-07-24.

**Why:** The authorization protocol ([[quote-directive-before-editing]]) is satisfied by acting correctly, not by narrating restraint. Repeating it every turn adds length without information, and after a correction it reads as defensive rather than reassuring.

**How to apply:** Give the finding, the change, or the answer, then stop. A turn that produced no edits shows that already through the absence of tool calls. The exception is repository state John actually needs - an unpushed commit hash, a dirty tree, a half-applied change - which is a fact about the repo, not a claim about your conduct; report that plainly and without framing it as restraint.
