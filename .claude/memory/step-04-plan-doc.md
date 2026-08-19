---
name: step-04-plan-doc
description: Approved step 4 implementation plan lives at Docs/step-04-plan.md; implementation not yet authorized
metadata: 
  node_type: memory
  type: project
  originSessionId: db4a99f7-1f5f-4be7-b7ad-4ebbeecf7b8e
  modified: 2026-08-19T17:47:05.651Z
---

The step 4 (producers, generators, upgrades + resolution) implementation plan was approved
2026-08-19 after absorbing two external review rounds, and John directed it saved durably:
it lives at `Docs/step-04-plan.md` in the repo. Read it before starting step 4 work - it
records design rulings (gather-origin formula context, null purchase gates fail closed and
warn, stats stay strings, StrandedValue stays rung-only, implicit latch write at index -1,
zero base cost is an error) that plain re-derivation from the design doc would miss.

As of 2026-08-19 John had NOT yet given the go-ahead to implement; the plan being approved
is not authorization to edit (see [[quote-directive-before-editing]]). Related:
[[design-review-revisions]], [[project-layout-and-workflow]].
