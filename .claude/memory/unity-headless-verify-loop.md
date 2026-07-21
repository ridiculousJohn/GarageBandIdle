---
name: unity-headless-verify-loop
description: "How to compile, reimport, and test Garage Band Idle headlessly when John's editor is closed"
metadata: 
  node_type: memory
  type: project
  originSessionId: ff77d597-62a9-412c-b32f-c1489e34fb56
---

Verification loop for [[project-layout-and-workflow]], established during slice 3.5 (2026-07-21). Unity 6000.5.4f1 at `C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe`.

**Why:** Changes can be verified without John pressing Play: batchmode compiles the code, re-runs the JSON import, and runs the edit-mode suite. Only Play-mode behavior and inspector UI need his eyes.

**How to apply:**
- First check `Garage Band Idle/Garage Band Idle/Temp/UnityLockfile` - if present his editor has the project open and batchmode aborts; hand verification to him instead.
- Import (also proves compile): `Unity.exe -batchmode -nographics -projectPath <unity project> -executeMethod RidiculousGaming.GarageBandIdle.EditorTools.ChapterJsonImporter.ImportChapter1 -quit -logFile <log>`. Grep the log for `error CS` and the `Imported 'ch01_garage'` summary.
- Tests: same but `-runTests -testPlatform EditMode -testResults <xml>`; check `total=.. passed=.. failed="0"` on the first summary line (60 tests as of slice 3.5).
- Any change to a definition class's serialized fields REQUIRES re-running the import: old assets deserialize stale/default values until rewritten (enum renumbering, renamed fields). Boot validation flagging `None`/unknown ids after a schema change usually means "reimport not run yet".
- Run import before tests: Chapter1ContentTests validates the imported assets against the JSON.
