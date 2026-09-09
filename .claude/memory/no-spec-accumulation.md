---
name: no-spec-accumulation
description: "Answer a review finding by deleting the thing that had the hole, not by adding a paragraph; plan length is a defect signal, not thoroughness"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: efd4f70d-d22b-4ea0-8736-2d55c0b412d5
  modified: 2026-08-31T16:54:19.509Z
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

**Trimming has the opposite failure, 2026-08-24.** Cutting the step 6 plan from 343 lines to 152 by
paragraph size dropped a required validation check, because the checks are one-liners and the
argument prose is what runs long. Cut argument - the paragraph defending a ruling nobody has
challenged - never a sentence that names a check, a rule, or a test.

**The tell that it is time to delete: one operand generating a finding in CONSECUTIVE review
rounds (2026-08-28).** Currency `activeWhen` took four rounds, and `IdleAccumulation` inside one was
the root of three: a settlement that threw mid-payment, a validator blessing gates nothing could
reach, then the same reach test still over-permissive because entry conditions could block it. Each
round I sharpened the reach walk, and each sharpening was individually correct and still inexact.
Refusing the operand outright deleted the walk, closed all three, and cost no expressiveness - the
mechanic it looked like was already a wildcard x0 modifier with an `appliesWhen`. Two rounds on the
same thing is the signal to stop asking "how do I check this better" and start asking "should this
be authorable at all". A check that needs sharpening twice is usually guarding something that should
not exist.

**How to apply:** Before specifying an edge case, ask what authored content actually
exercises it - if Chapter 1's data never reaches the state, the policy is premature and the
mechanism probably is too. Magic constants (a fills-per-settlement backstop), saturation
policies, and warnings on fields no behavior reads are the usual tells. Unauthored config
fields (`autoAdvance` with no chapter granting it) are content, not architecture: they wait.
Related: [[reuse-the-existing-mechanism]].

**A restriction needs a reason or it goes (John, 2026-09-09).** Slice A shipped a validator refusal
of `BuffActive` inside a currency's `activeWhen` because the plan listed it. The plan's reason - a
prune moving the fact - had died in a review the same day the plan was written, when truth became
the timestamp; the sentence survived and I copied it into the agent's contract. John: "if there's no
reason to restrict it then restricting it is limiting capability that is not needed to be limited.
delete it." The check to run on every refusal, validator error, or guard before it ships: name the
mechanism it prevents from going wrong TODAY. No mechanism, no restriction - and a plan sentence is
not a mechanism ([[verify-your-own-prior-statements]]).
