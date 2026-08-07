---
name: project-layout-and-workflow
description: "Garage Band Idle repo layout, docs-driven slice workflow, and where truth lives"
metadata: 
  node_type: memory
  type: project
  originSessionId: a3758315-7030-4a24-98c6-20cdec0e772e
  modified: 2026-08-07T22:08:08.935Z
---

Garage Band Idle is John's personal Unity 6000.5.4f1 idle game. The Unity project (Assets/, Packages/, ProjectSettings/) sits one level down at `<repo>/Garage Band Idle/`, with `Docs/` beside it at the repo root. There is a stray empty `Garage Band Idle/Garage Band Idle/Logs/` directory - it is not the project.

**Why:** Docs/garage-band-idle-design.md is the design source of truth; Docs/claude-code-build-prompts.md is an ordered build plan - slices 0 through 10, with half-numbered consolidation slices between them - fed one slice at a time, each tested in-editor and committed before the next.

**How to apply:** Derive the current position before starting work rather than trusting a recorded one - the slice headings in claude-code-build-prompts.md carry done markers, and `git log --oneline` confirms them. No other memory records which slice is next. Content is data-driven: chapter JSON (Docs/chapter-01-garage.json) imports to ScriptableObjects via an editor menu, discovered at runtime through Addressables labels. Systems act on group/scope flags and string ids, never named-currency special cases. If a design decision changes during a build, update the design doc so it and the code don't drift.
