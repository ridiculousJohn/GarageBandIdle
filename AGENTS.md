# Highest-Priority Repository Safety Rule

Never restore, revert, reset, discard, overwrite, clean, or otherwise roll back any repository or workspace change without John's explicit direction to perform that exact destructive action.

This prohibition includes, but is not limited to, `git restore`, `git revert`, `git reset`, `git checkout --`, `git clean`, stash deletion, and manual replacement or deletion intended to return files to an earlier state.

All existing and newly appearing changes are user-owned. Codex never owns workspace changes, including changes Codex authored, changes that appear during tests, generated files, importer output, logs, or changes that seem temporally related to a Codex action. Temporal correlation is not proof of ownership and never grants permission to discard anything.

If a command, test, editor, importer, formatter, or other tool creates unexpected changes, leave every change untouched, report exactly what appeared, and wait for explicit direction. Do not clean up, undo, or restore those changes on your own.

This rule overrides cleanup preferences, test-artifact cleanup, assumptions about generated content, and all inferred authorization. If there is any uncertainty, do nothing destructive and ask John.

# Unity Test Rule

Never run Unity tests or request approval to run Unity tests unless John explicitly instructs Codex to run them in the current task.

# Review Rule

For every review, report defects grounded in a realistic current or future use of the actual product. Future chapters and planned features count when they exercise the documented mechanics. Do not manufacture sequences that the product's UI or lifecycle cannot permit merely because public APIs could be called that way. Every finding must identify the plausible product path and its observable incorrect consequence; speculative risks and implementation preferences are not defects.

# Remember Rule

When John says to "remember" an instruction, persist it in this `AGENTS.md` file during the same response. Acknowledging or promising to follow it only in the current conversation does not satisfy the request.
