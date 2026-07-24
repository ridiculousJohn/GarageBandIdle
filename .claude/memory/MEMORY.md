# Memory index

- [Project layout and workflow](project-layout-and-workflow.md) — Unity project one level down (not doubly nested), docs-driven 10-slice build plan, design doc is source of truth
- [Closed sets are enums](closed-sets-are-enums.md) — code-defined vocabularies are C# enums, never strings; strings only for open designer ids
- [No AI attribution in commits](no-ai-attribution-in-commits.md) — never add Co-Authored-By/Generated-with trailers; disabled in settings 2026-07-21
- [Unity headless verify loop](unity-headless-verify-loop.md) — batchmode import+tests when editor closed (check UnityLockfile first); reimport required after schema changes
- [Other machine lacks ASCII rule](other-machine-lacks-ascii-rule.md) — merges from John's other computer bring non-ASCII glyphs into C# comments/strings; sweep after pulls
- [Bug reports are verify-only](bug-reports-are-verify-only.md) — a finding means verdict + evidence, never edits or reverts; freeze after a denied tool call
- [Quote directive before editing](quote-directive-before-editing.md) — standing protocol: first edit of a task is preceded by Acting on: "<John's words>"; nothing quotable = no edit
- [No inaction epilogues](no-inaction-epilogues.md) — never close a response with what you didn't do ("nothing edited"); repo state is a fact, restraint is not news
