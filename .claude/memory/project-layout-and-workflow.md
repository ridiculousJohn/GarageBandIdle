---
name: project-layout-and-workflow
description: "Garage Band Idle repo layout, docs-driven slice workflow, and where truth lives"
metadata: 
  node_type: memory
  type: project
  originSessionId: a3758315-7030-4a24-98c6-20cdec0e772e
---

Garage Band Idle is John's personal Unity 6000.5.4f1 idle game. The Unity project is nested at `Garage Band Idle/Garage Band Idle/` (repo root holds `Docs/` beside it).

**Why:** Docs/garage-band-idle-design.md is the design source of truth; Docs/claude-code-build-prompts.md is a 10-slice ordered build plan being fed one slice at a time, each slice tested in-editor and committed before the next.

**How to apply:** Check `git log --oneline` against the slice list in claude-code-build-prompts.md to find the current position before starting work. Content is data-driven: chapter JSON (Docs/chapter-01-garage.json) imports to ScriptableObjects via an editor menu, discovered at runtime through Addressables labels. Systems act on group/scope flags and string ids, never named-currency special cases. If a design decision changes during a build, update the design doc so it and the code don't drift.
