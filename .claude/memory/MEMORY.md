# Memory index

- [Project layout and workflow](project-layout-and-workflow.md) — nested Unity project, docs-driven 10-slice build plan, design doc is source of truth
- [Closed sets are enums](closed-sets-are-enums.md) — code-defined vocabularies are C# enums, never strings; strings only for open designer ids
- [No AI attribution in commits](no-ai-attribution-in-commits.md) — never add Co-Authored-By/Generated-with trailers; disabled in settings 2026-07-21
- [Unity headless verify loop](unity-headless-verify-loop.md) — batchmode import+tests when editor closed (check UnityLockfile first); reimport required after schema changes
