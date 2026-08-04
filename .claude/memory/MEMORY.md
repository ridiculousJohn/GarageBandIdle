# Memory index

- [Project layout and workflow](project-layout-and-workflow.md) — Unity project one level down (not doubly nested), docs-driven 10-slice build plan, design doc is source of truth
- [Closed sets are enums](closed-sets-are-enums.md) — code-defined vocabularies are C# enums, never strings; strings only for open designer ids
- [No AI attribution in commits](no-ai-attribution-in-commits.md) — never add Co-Authored-By/Generated-with trailers; disabled in settings 2026-07-21
- [Unity headless verify loop](unity-headless-verify-loop.md) — batchmode import+tests when editor closed (check UnityLockfile first, one level down); exit code 0 proves nothing, grep for `error CS`; 201 tests as of 2026-08-04
- [Other machine lacks ASCII rule](other-machine-lacks-ascii-rule.md) — merges from John's other computer bring non-ASCII glyphs into C# comments/strings; sweep after pulls
- [Bug reports are verify-only](bug-reports-are-verify-only.md) — a finding means verdict + evidence, never edits or reverts; freeze after a denied tool call
- [Quote directive before editing](quote-directive-before-editing.md) — standing protocol: first edit of a task is preceded by Acting on: "<John's words>"; nothing quotable = no edit
- [No inaction epilogues](no-inaction-epilogues.md) — never close a response with what you didn't do ("nothing edited"); repo state is a fact, restraint is not news
- [AGENTS.md is not a Claude file](agents-md-is-not-a-claude-file.md) — never read or cite repo-root AGENTS.md; it governs a different agent, and quoting it invents constraints John never set
- [Economy context as built](economy-context-as-built.md) — slice 5.5 landed `f12ba3e`; re-projection is the only door a modifier enters; which guarantees are test-only (rebuild-after-construction, multi-context focus); next is slice 6
- [Reveal is a Condition](reveal-is-a-condition.md) — slice 5.6 landed `5b8a917`; the fail-open gap it left, the two stale-key refusal shapes, and why the fan base rate never checks for a band
- [Test the justification, not just the claim](test-the-justification-not-just-the-claim.md) — verify reasons for NOT doing something; three deferral/restriction justifications failed checking in one session
