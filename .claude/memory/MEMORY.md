# Memory index

## The project
- [Project layout and workflow](project-layout-and-workflow.md) - a rewind doesn't revert the filesystem; heredocs mangle C#; old commits are a dead architecture
- [Design review revisions](design-review-revisions.md) - register of designs the twelve review passes DELETED, plus the deferred questions; the doc cannot show an absence
- [Unity headless verify loop](unity-headless-verify-loop.md) - batchmode import+tests when the editor is closed; exit 0 proves nothing, grep for `error CS`
- [Fact addressing is id + outward walk](fact-addressing-is-id-plus-outward-walk.md) - names resolve outward from the acting scope; cross-chain aliasing is the feature
- [Currency values are BigNumber](currency-values-are-bignumber.md) - anything the runtime could compute past a double, authored fields included; only counts and Pow's power are exempt
- [Roadies and typed payloads](roadies-and-typed-payloads.md) - 2026-08-20: venue assets and stationing caps deleted, payloads typed by authored kind, currencies by direct reference
- [Ctrl C is the reference game](ctrl-c-is-the-reference-game.md) - the design descends from it; ask John how Ctrl C does it before reasoning from scratch
- [Closed sets are enums](closed-sets-are-enums.md) - code-defined vocabularies are C# enums; strings only for open designer ids
- [Narrowing a member type](narrowing-a-member-type.md) - generic base class; covariant overrides don't compile in Unity (CS8831) and `new` hiding is rejected
- [Other machine lacks ASCII rule](other-machine-lacks-ascii-rule.md) - merges from John's other computer bring non-ASCII glyphs into C# comments/strings; sweep after pulls

## Gates
- [Quote directive before editing](quote-directive-before-editing.md) - the rule is in the repo CLAUDE.md; this is the failure record: every evasion, and why the check is mechanical
- [Commit means the whole tree](commit-means-the-whole-tree.md) - everything dirty, memory included; no AI attribution; small enough to read; no preference inferred from repo state
- [Verify your own prior statements](verify-your-own-prior-statements.md) - do what I said I would do or stop and say why; read the record back before describing it
- [AGENTS.md is not a Claude file](agents-md-is-not-a-claude-file.md) - never read or cite it; it governs a different agent, and quoting it invents constraints John never set
- [Asides do not close the main question](asides-do-not-close-the-main-question.md) - an aside resolves only itself; my own recommendation is never a decision John made
- [No inaction epilogues](no-inaction-epilogues.md) - never narrate what you didn't do; repo state is a fact, restraint is not news

## Judgment
- [Problems, not issues](problems-not-issues.md) - what breaks TODAY, not what is true; and a reason for NOT doing something gets checked like any other claim
- [Reuse the existing mechanism](reuse-the-existing-mechanism.md) - name the primitive that already covers it, and when two shapes both work take the smaller one
- [No spec accumulation](no-spec-accumulation.md) - answer a finding by deleting the mechanism, not by adding a paragraph; cut argument, never a named check
- [Tests exercise runtime code](tests-exercise-runtime-code.md) - fixtures are fine, a second implementation of runtime behavior is not; convert the call sites instead
- [Doc decisions land when made](doc-decisions-land-when-made.md) - a settled decision goes in the design doc immediately; only code-describing edits wait for the code
- [Sweep every tier of a defect class](sweep-every-tier-of-a-defect-class.md) - "did you miss anything?" is a command to grep; Scripts, Tests, the live docs, the chapter JSON

## Disagreement
- [Never cave to pressure](never-cave-to-pressure.md) - the substance changes only when the facts do; and when I do concede, the concession leads
- [Pushback means re-derive](pushback-means-rederive.md) - his dispute of my model of HIS design means produce a discriminator; on same-but-different, split the bundle
