---
name: no-inaction-epilogues
description: Never close a response by stating what you did not do; it is wasted text
metadata: 
  node_type: memory
  type: feedback
  originSessionId: facbda27-5ff8-43e7-8f5f-aef0a5155adc
  modified: 2026-08-19T17:04:06.691Z
---

Do not state what you did not do or will not do, at either end of a response - "nothing edited", "no commits made", "Orienting only - no edits". John called the closing form wasted text on 2026-07-24; on 2026-08-19 he tore into the opening form: announcing compliance with an instruction is as bad as narrating restraint after the fact. If you are doing what you were told, doing it is the whole message.

**Why:** The authorization protocol ([[quote-directive-before-editing]]) is satisfied by acting correctly, not by narrating restraint. Repeating it every turn adds length without information, and after a correction it reads as defensive rather than reassuring.

**How to apply:** Give the finding, the change, or the answer, then stop. A turn that produced no edits shows that already through the absence of tool calls. The exception is repository state John actually needs - an unpushed commit hash, a dirty tree, a half-applied change - which is a fact about the repo, not a claim about your conduct; report that plainly and without framing it as restraint. The second exception is a blocked edit: when [[quote-directive-before-editing]] stops work because there is no directive to quote, say so and name what would unblock it. Without that he waits on an edit that was never coming, so it is information he needs, not narrated self-discipline.
