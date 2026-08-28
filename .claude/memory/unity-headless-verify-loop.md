---
name: unity-headless-verify-loop
description: "How to compile, reimport, and test Garage Band Idle headlessly when John's editor is closed"
metadata: 
  node_type: memory
  type: project
  originSessionId: ff77d597-62a9-412c-b32f-c1489e34fb56
  modified: 2026-08-28T03:05:06.181Z
---

Verification loop for [[project-layout-and-workflow]], established during slice 3.5 (2026-07-21). Repo paths here are relative to the repo root as `<repo>/...`, since the checkout lives at a different absolute path on each of John's machines.

Resolve the editor rather than hardcoding it: read the version from `<repo>/Garage Band Idle/ProjectSettings/ProjectVersion.txt` (6000.5.4f1 as of 2026-08-04) and use that version's `Editor/Unity.exe` under the local Unity Hub editors directory - `Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe` on the Windows boxes. Several versions are usually installed side by side; never take the newest.

**Why:** Changes can be verified without John pressing Play: batchmode compiles the code, re-runs the JSON import, and runs the edit-mode suite. Only Play-mode behavior and inspector UI need his eyes.

**How to apply:**
- First check for a running **process named `Unity`** (`Unity Hub` and `Unity.Licensing.Client` are not it). If one exists his editor has the project open, batchmode aborts with "another Unity instance is running" and writes almost nothing; hand verification to him instead. **Ask before assuming he is not mid-test** - on 2026-08-12 he had the editor open to play the game while a batchmode run was fired at the same project.
- **The lockfile is NOT that check.** `<repo>/Garage Band Idle/Temp/UnityLockfile` - ONE level down, not doubly nested (a stray empty `Garage Band Idle/Garage Band Idle/Logs/` exists and invites the wrong path) - is left behind by BATCHMODE too whenever it exits on compile errors, and the next run then aborts without writing a log - leaving the PREVIOUS run's log sitting there to be grepped as if it were this run's. That is how a false green happens: delete the log before launching and refuse any log whose timestamp predates the launch. Treating its presence as "the editor is open" stalls the loop; treating its absence as "safe to run" misses an editor that has not written it yet. Check the process, then delete a stale lockfile before launching.
- **Unity batchmode is the ONLY compiler allowed here** (John, 2026-08-20). No Roslyn/csc, no
  dotnet, no hand-assembled reference list, ever, unless he asks for it by name. Its result is
  the only one that means anything: a green build from any other toolchain that Unity would
  disagree with is simply wrong. Blocked (editor open) means compilation is UNVERIFIED - say so
  and ask him to close it. See [[quote-directive-before-editing]].
- Compile check: `Unity.exe -batchmode -nographics -projectPath "<repo>/Garage Band Idle" -quit -logFile <log>` (a plain -quit run compiles and imports). Grep the log for `error CS`.
- Content import (back since step 8 slice A): add `-executeMethod RidiculousGaming.GarageBandIdle.Editor.ChapterJsonImporter.ImportAll` to the compile line. It THROWS on any preflight or post-write ERROR, so the process fails rather than leaving yesterday's assets green. Run it before tests once authored content exists.
- **A clean exit code proves nothing** - batchmode exits 0 with compile errors sitting in the log, so the grep is the check, never `$?`.
- Tests: same as the import line but swap `-executeMethod ...` for `-runTests -testPlatform EditMode -testResults <xml>` and **DROP `-quit`** - the test runner exits on its own, and `-quit` tears the editor down before it ever starts. Check `total=.. passed=.. failed="0"` on the first summary line. **Write `-testResults` to the LocalLow default (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Garage Band Idle\TestResults.xml`)** - John's external reviewer verifies the run from there; a scratchpad-only XML is invisible to review.
- **The expected count is `Docs/build-plan.md`'s status column, not memory.** It moves every step (405/405 at step 8 slice A, 2026-08-27), the build plan records the count and the reason for each step's delta, and one of the counted tests is Addressables' TestStub. Re-derive from a clean run; never assert a count from recall.
- **A test run that never ran looks exactly like a clean one.** With `-quit` left on, the log holds `-runTests` in the arg dump and then `Batchmode quit successfully invoked` with no runner output: exit 0, zero `error CS`, and both checks above pass on zero tests executed. The only tell is that `<xml>` was never written, so **confirm the results file exists before reading a summary from it** - a missing file is a failed run, not a missing test. But check the default location before calling it failed: on 2026-08-06 the `-testResults` path was ignored and the XML landed at `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Garage Band Idle/TestResults.xml` instead. The log's `Saving results to:` line names where it actually went.
- **The results XML lands a beat AFTER the process returns.** Even from a synchronous foreground invocation, the very next command can see no file - and on 2026-08-07 a second read of the same path, issued immediately, still missed it while the log already held its `Saving results to:` line. Re-check before concluding the run failed. Related tell for a genuine failure: `-runTests` exits 1 when tests fail and leaves `$LASTEXITCODE` empty when they all pass, which is the reverse of the usual reading - the XML summary is the answer, not the code.
- **Wait for the Unity process to exit before grepping the log.** A background runner reports "completed" while batchmode is still flushing, and a half-written log has no importer summary and no `Exiting batchmode successfully` - which reads exactly like a failed import. Poll for no process named `Unity` (`Unity Hub` and `Unity.Licensing.Client` are not it), then grep.
- Any change to a definition class's serialized fields REQUIRES re-running the import: old assets deserialize stale/default values until rewritten (enum renumbering, renamed fields). Boot validation flagging `None`/unknown ids after a schema change usually means "reimport not run yet".
- Run import before tests: Chapter1ContentTests validates the imported assets against the JSON.
