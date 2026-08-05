---
name: unity-headless-verify-loop
description: "How to compile, reimport, and test Garage Band Idle headlessly when John's editor is closed"
metadata: 
  node_type: memory
  type: project
  originSessionId: ff77d597-62a9-412c-b32f-c1489e34fb56
---

Verification loop for [[project-layout-and-workflow]], established during slice 3.5 (2026-07-21). Repo paths here are relative to the repo root as `<repo>/...`, since the checkout lives at a different absolute path on each of John's machines.

Resolve the editor rather than hardcoding it: read the version from `<repo>/Garage Band Idle/ProjectSettings/ProjectVersion.txt` (6000.5.4f1 as of 2026-08-04) and use that version's `Editor/Unity.exe` under the local Unity Hub editors directory - `Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe` on the Windows boxes. Several versions are usually installed side by side; never take the newest.

**Why:** Changes can be verified without John pressing Play: batchmode compiles the code, re-runs the JSON import, and runs the edit-mode suite. Only Play-mode behavior and inspector UI need his eyes.

**How to apply:**
- First check `<repo>/Garage Band Idle/Temp/UnityLockfile` - ONE level down, not doubly nested ([[project-layout-and-workflow]] notes the stray `Garage Band Idle/Garage Band Idle/` that invites the wrong path) - if present his editor has the project open and batchmode aborts; hand verification to him instead.
- Import (also proves compile): `Unity.exe -batchmode -nographics -projectPath "<repo>/Garage Band Idle" -executeMethod RidiculousGaming.GarageBandIdle.EditorTools.ChapterJsonImporter.ImportChapter1 -quit -logFile <log>`. Grep the log for `error CS` and the `Imported 'ch01_garage'` summary.
- **A clean exit code proves nothing** - batchmode exits 0 with compile errors sitting in the log, so the grep is the check, never `$?`.
- Tests: same as the import line but swap `-executeMethod ...` for `-runTests -testPlatform EditMode -testResults <xml>` and **DROP `-quit`** - the test runner exits on its own, and `-quit` tears the editor down before it ever starts. Check `total=.. passed=.. failed="0"` on the first summary line. **240 at `7f9c259`** (2026-08-05, the importer condition preflight and chapter-scoped boot validation). 211 at `1227bab`, 202 at `5b8a917`, 201 at `f12ba3e`, 187 at `dfded84`, 60 at slice 3.5. The number moves every slice - re-derive from a clean run and update this line rather than trusting it.
- **A test run that never ran looks exactly like a clean one.** With `-quit` left on, the log holds `-runTests` in the arg dump and then `Batchmode quit successfully invoked` with no runner output: exit 0, zero `error CS`, and both checks above pass on zero tests executed. The only tell is that `<xml>` was never written, so **confirm the results file exists before reading a summary from it** - a missing file is a failed run, not a missing test.
- Any change to a definition class's serialized fields REQUIRES re-running the import: old assets deserialize stale/default values until rewritten (enum renumbering, renamed fields). Boot validation flagging `None`/unknown ids after a schema change usually means "reimport not run yet".
- Run import before tests: Chapter1ContentTests validates the imported assets against the JSON.
