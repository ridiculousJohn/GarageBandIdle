---
name: doc-decisions-land-when-made
description: A decision goes into the design doc the moment it is made; only edits describing shipped code wait for the code
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 2f41389f-362c-4e3b-876e-d8c5bc97092c
  modified: 2026-08-24T21:34:46.766Z
---

When a design question is settled in chat, edit `garage-band-idle-design.md` then. Do not park it in
a plan's "docs on landing" list. That list is only for edits that describe code that does not exist
yet - a file-layout entry, a status line, a caveat about which checks are implemented.

**Why:** 2026-08-24. The step 6 plan deferred every doc edit to "on landing," so 6.1 still carried a
`hostScopeId` field and id-based lifecycle signatures from before the 2026-08-20 direct-reference
pass. The design doc is the authority, so the stale text is what a fresh session reads and reasons
from - the whole conversation went in circles on a field that had already been decided away. John:
"why would it defer something that is being written now?" The plan is not the authority and cannot
substitute for the doc.

**How to apply:** Split the edits by kind, not by convenience. Decision now, code-description later.
When a decision changes a doc sentence, also grep for the same claim in `build-plan.md`, the other
step plans, and `chapter-01-content.md` - stale content docs recreate the same failure one layer
down. Related: [[sweep-every-tier-of-a-defect-class]], [[no-spec-accumulation]].

**Grep finds renames; only a section walk finds a changed mechanism (2026-08-24).** The doc states
intent, the code states mechanism, and changing the mechanism usually leaves the intent sentence
true - so it hits no grep and reads fine. Section 12.6 said `GetMultiplier` "gathers from every scope
on the chain outward", which stayed accurate as a RESULT after the walks inverted to ask each scope
for its own factor, and it survived three identifier sweeps because of that.

**The check that works:** list what the change touched, and for each item name the doc section that
OWNS it - then open that section and read it against the code. Doing that once found a divergence
five greps had missed (the doc calls a currency's home the scope that DECLARES it; `GameContext`
resolves it by key presence in `balances`, which only agrees because seeding maintains it). Run it
as part of finishing the change, not when John asks whether anything was missed.
