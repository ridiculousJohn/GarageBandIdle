---
name: no-spec-accumulation
description: "Answer a review finding by deleting the thing that had the hole, not by adding a paragraph; plan length is a defect signal, not thoroughness"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: efd4f70d-d22b-4ea0-8736-2d55c0b412d5
  modified: 2026-08-20T18:18:24.777Z
---

When a review finds a hole, the first move is to ask whether the mechanism should exist at
all. Deleting it closes the finding permanently; specifying around it adds prose, tests, and
future readers. Never answer a finding by growing the spec if cutting the feature answers it.

**Why:** John's complaint (2026-08-20) about the step 5 plan: four review rounds took it
from 225 to 364 lines while the mechanism barely changed, because every finding got a
justifying paragraph. That accumulation is what killed the project's first attempt. Writing
plans to survive scrutiny is the wrong objective function - individually defensible rulings
are exactly the ones that pile up, because nothing ever triggers a "delete this instead"
reflex.

**How to apply:** Before specifying an edge case, ask what authored content actually
exercises it - if Chapter 1's data never reaches the state, the policy is premature and the
mechanism probably is too. Magic constants (a fills-per-settlement backstop), saturation
policies, and warnings on fields no behavior reads are the usual tells. Unauthored config
fields (`autoAdvance` with no chapter granting it) are content, not architecture: they wait.
Related: [[reuse-the-existing-mechanism]].
