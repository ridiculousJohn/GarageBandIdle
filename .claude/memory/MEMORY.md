# Memory index

## Read first, then delete when stale
- [Session handoff](session-handoff-2026-11-10.md) - state at end of 2026-11-12: chapter 2 loop restructure (Rep, EPs Sold) committed, play-test next, items waiting on John, chapter 3 after

## The project
- [Project layout and workflow](project-layout-and-workflow.md) - where things live, which docs are authority, editing mechanics that bite
- [Design restart 2026-11](design-restart-2026-11.md) - decisions from the 2026-11-09 restart; chapters 3-8 are still old sketches
- [Ctrl C chapter remap 2026-11](ctrl-c-chapter-remap-2026-11.md) - the approved Ctrl C to Garage Band map for chapters 3-8 and John's three rulings; not yet in the docs
- [Ctrl C is the reference game](ctrl-c-is-the-reference-game.md) - read Docs/ctrl-c and ask John before reasoning from scratch; what he reported from play
- [Ctrl C bar card controls](ctrl-c-bar-card-controls.md) - levelled card features built through 2026-11-12, and the ones still deferred (later-runs ghosted bars, pill buttons, toast/cycle tidy-ups)
- [Unity headless verify loop](unity-headless-verify-loop.md) - batchmode compile, import and tests; the checks that make a run trustworthy; the script to copy
- [Code conventions](code-conventions.md) - BigNumber everywhere, enums for closed sets, generic base for narrowing, ASCII, what a test may contain

## Before saying the editor is open or closed
- [Is the editor open](is-the-editor-open.md) - ONLY: Win32_Process with path under Hub\Editor AND command line naming this project. NEVER a process named "unity" (that is the CLI MCP server)
- [Show, don't describe](show-dont-describe.md) - visual or animated change: record it from the running game (frames to MP4 via ffmpeg, never GIF) and send it
- [Open the editor yourself](open-the-editor-yourself.md) - a check needs the editor and it is closed: launch it, never ask John to

## Before writing
- [Quote directive before editing](quote-directive-before-editing.md) - what is never an order; what a commit contains; AGENTS.md is not mine
- [Ask before the work, not after](ask-before-the-work-not-after.md) - four tests for an open point; picks asked in one message before the first write; nothing after the report; cleanup a change leaves (orphaned assets, dead flags) is done in the same run, never asked
- [External review verdicts](external-review-verdicts.md) - confirm or deny each defect, accept or reject each fix, wait for "apply"
- [Orchestrated slice workflow](orchestrated-slice-workflow.md) - I orchestrate and review, Opus agents do the bulk, I run Unity, their work is mine

## Judgment
- [Design judgment](design-judgment.md) - reuse what exists, smallest mechanism, true of every instance, one vocabulary, delete instead of specify
- [Problems, not issues](problems-not-issues.md) - a finding matters when something breaks today on a path that runs; what belongs in a report
- [Docs and sweeps](docs-and-sweeps.md) - size by grep count, docs and build-plan row are part of the change, sweep every place that states the rule

## Talking to John
- [Verify your own prior statements](verify-your-own-prior-statements.md) - every claim about code, docs, Ctrl C or my own words is checked first; shape of answers
- [Never cave to pressure](never-cave-to-pressure.md) - facts change only with facts, one position, the fact leads, no apology formulas, one root-cause fix
