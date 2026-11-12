---
name: ask-before-the-work-not-after
description: "Which open points get asked, which get decided without asking, and when - everything before the first write, nothing after the report"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-12T20:05:09.684Z
---

**Why:** John's standing complaint is "one more thing" after every delivery. Each one costs him a
review of finished work and another round. When he says go, that go is meant to finish the stage.

Run these in order on every unsettled point, BEFORE the first write:

1. **Would a player see it and think the game is broken?** (An empty dialog, a stuck button, a
   number that jumps.) Then it is a bug, not a pick. Build the one shape that is not a bug. Do not
   ask. Edge cases are mine: "I can't design every edge case. you're supposed to be smart."
2. **Does nothing break today, and does the engine already let content author it either way?** Then
   it is that chapter's design decision, made when the chapter is designed. Do not ask, do not list
   it, and never answer it with a new rule or restriction.
3. **Is it already decided?** His words decided it, or I stated it and he built on it or moved on.
   Do not re-ask. A recommendation of mine he has not responded to is NOT decided, including one cut
   off by an aside; when an aside closes, return to the open question.
4. **Otherwise it is a pick:** two shapes a player or reader would notice and prefer differently.
   Ask every pick in ONE message, each with a recommendation, then write once with his answers.

**Before calling anything "open" or "not ruled on"** (2026-11-10: I listed four code changes as
awaiting a ruling; three were plain needs and the fourth had a shape with no downside that I had not
looked for. John: "none of your 'decisions' were really decisions".)
- A missing piece the design needs is a need. State it as part of the work. His "yes" is not
  required for something with no alternative.
- When an option carries a consequence, look for the shape that removes the consequence before
  presenting it as a trade-off. Only what is left after that search is a pick.
- When the reference game answers it, follow the reference and say so.

**Cleanup is part of the change, never a question** (2026-11-12: I left 14 orphaned trigger assets
from removed content across two commits and then asked whether to delete them. John: "in who's
fucking world would those be kept around?" and "things that should have OBVIOUSLY been done with the
first run".)
- Whatever a change makes dead goes in the same run: orphaned generated assets (the importer never
  deletes; its "no longer authored ... left on disk" lines are a to-do, not a report), unused flags,
  triggers, tests and doc lines. Verify nothing references it, delete it, say so in one line.
- Never list leftover cleanup as "for you to decide". If the answer is obvious, it is not a pick.

**During and after**
- A pick that surfaces mid-work pauses the work for one question. Never finish and append it.
- Before launching agents, settle every hit of the grep that sizes the change. Whatever an agent
  hands back as unsettled goes through tests 1-4 before it reaches John.
- After the report: no "one pick you have not ruled on", no "open picks" section in a doc, no
  "decisions made" list, no "pending your call". Each is the question I skipped at the start.
- Before writing "this one is yours", name the fact that changed and made it a question. If a
  mechanism moved and the rule did not, there is no question.
- A decision attributed to John needs his message making it.
