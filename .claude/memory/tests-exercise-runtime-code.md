---
name: tests-exercise-runtime-code
description: "Never write a test-only implementation of behavior the runtime has; fixtures are fine, second copies of a walk or a rule are not"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 2f41389f-362c-4e3b-876e-d8c5bc97092c
  modified: 2026-08-24T21:34:56.858Z
---

A test may build content the game does not build - `TestTree` making scopes and currencies is a
fixture and that is its job. A test may NOT contain a second implementation of something the runtime
does. If the tests reach for one, the suite is proving the copy works.

**Why:** 2026-08-24. Removing the by-name scope lookup from `ScopeState` left ~30 test call sites
using it, and rather than convert them I added a test-only extension method with the same name and
the same body. John: "having tests that use methods that the runtime doesn't use is just
stupid." Two harms, and the second was the one I missed - the reference walk that production
actually depends on was then exercised by four call sites instead of the whole suite, and
`FindInSubtree("tier1")` and `FindInSubtree(Tier1Def)` read identically while resolving through
different code.

**How to apply:** If converting call sites is the obstacle, convert them - that is the work, not a
detour around it. When a test cannot reach the real API, treat it as a signal about the API rather
than a licence to write around it (here `Load` built its own fixture internally and had to hand it
back). Related: [[reuse-the-existing-mechanism]].
