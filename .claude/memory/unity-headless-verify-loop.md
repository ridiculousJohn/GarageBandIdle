---
name: unity-headless-verify-loop
description: "How to compile, import content and run the edit-mode tests headlessly, the checks that make a run trustworthy, and the script to copy"
metadata:
  node_type: memory
  type: reference
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:46:46.021Z
---

Unity batchmode is the ONLY compiler allowed here. No Roslyn, csc or dotnet. If batchmode cannot run,
compilation is unverified: say so and ask John to close the editor.

**Before launching**
- Editor path: read the version from `<repo>/Garage Band Idle/ProjectSettings/ProjectVersion.txt`
  and use `Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe`. Never the newest installed.
- Is John's editor open on THIS project? Check with `Get-CimInstance Win32_Process`: executable path
  under `Hub\Editor` AND a command line naming this project. Never by process name (the Unity CLI
  MCP server is also `unity`), and another project's editor does not block. If it is open, batchmode
  aborts; use the in-editor tools below or ask him. Ask before assuming he is not mid-test.
- `Temp/UnityLockfile` is not that check. Batchmode leaves it behind on compile errors. Delete a
  stale one, delete the old log and the old results file, and note the launch time.

**Running**
- Compile only: `-batchmode -nographics -projectPath <project> -quit -logFile <log>`.
- Import content: add
  `-executeMethod RidiculousGaming.GarageBandIdle.Editor.ChapterJsonImporter.ImportAll`. It throws on
  any import or validation error. Run it before tests whenever content JSON or a definition class's
  serialized fields changed.
- Tests: `-runTests -testPlatform EditMode -testResults <xml>` and NO `-quit`. Write results to
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Garage Band Idle\TestResults.xml`; John's reviewer
  reads them there.
- Run in the foreground with a 600000 ms timeout. Wait for the process to exit (same predicate as
  the pre-check) before reading the log; the results XML lands a few seconds after that.

**Reading the result**
- The exit code proves nothing. Grep the log for `error CS`.
- A log or results file older than the launch is stale: refuse it. A missing results file is a
  failed run, not a clean one.
- The verdict is the first `<test-run ...>` line: total, passed, failed.
- The expected count is the latest row of `Docs/build-plan.md`, never recall.

**What a reimport does to the tree (not content changes, not findings)**
- `rid` values regenerate, so every asset holding a condition, action or formula shows modified.
- An empty serialized string is written `uiText: ` with a trailing space.
- The importer never deletes. An asset no document authors any more is left on disk and logged as
  "no longer authored by any document - left on disk".

**When John's editor IS open** (`unity-editor-mcp` tools, when connected)
- The editor does not see outside edits until it refreshes. Run `eval` with
  `UnityEditor.AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)`, then poll
  `recompile_status` until completed, before `run_tests`. Otherwise it runs the old assemblies.
- `eval` is a method body: fully qualified names, no `using`, `return` the answer.
- `run_tests` output is large: summarize from the saved file, never read it whole.
- That runner fails about fifteen `ChapterImporterTests` rows every time; they pass headless.
  Batchmode is the verdict.

**The script.** Copy it into the scratchpad as `verify.ps1`; never retype it from recall. Run with
`-Mode import` or `-Mode tests`.

```powershell
param([ValidateSet('import','tests')][string]$Mode = 'tests')
$ErrorActionPreference = 'Continue'
$project = '<repo>\Garage Band Idle'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'   # version from ProjectVersion.txt
$log = "<scratchpad>\$Mode.log"
$results = Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\Garage Band Idle\TestResults.xml'

if (-not (Test-Path $unity)) { Write-Output "NO UNITY AT $unity"; exit 2 }
$editor = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like '*Hub\Editor*' -and $_.CommandLine -like '*Garage Band Idle*' }
if ($editor) { Write-Output "EDITOR RUNNING: $($editor.CommandLine)"; exit 3 }

$lock = Join-Path $project 'Temp\UnityLockfile'
if (Test-Path $lock) { Remove-Item $lock -Force }
if (Test-Path $log) { Remove-Item $log -Force }
$launched = Get-Date

if ($Mode -eq 'import') {
    & $unity -batchmode -nographics -projectPath $project -quit -executeMethod RidiculousGaming.GarageBandIdle.Editor.ChapterJsonImporter.ImportAll -logFile $log | Out-Null
} else {
    if (Test-Path $results) { Remove-Item $results -Force }
    & $unity -batchmode -nographics -projectPath $project -runTests -testPlatform EditMode -testResults $results -logFile $log | Out-Null
}

while (Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like '*Hub\Editor*' -and $_.CommandLine -like '*Garage Band Idle*' }) { Start-Sleep -Seconds 2 }
Start-Sleep -Seconds 5

if (-not (Test-Path $log)) { Write-Output "NO LOG WRITTEN"; exit 4 }
if ((Get-Item $log).LastWriteTime -lt $launched) { Write-Output "STALE LOG"; exit 5 }

Write-Output "--- compile errors ---"
Select-String -Path $log -Pattern 'error CS' | ForEach-Object { $_.Line }
if ($Mode -eq 'import') {
    Write-Output "--- import ---"
    Select-String -Path $log -Pattern 'no longer authored|ContentImportException|ContentValidationException|Exiting batchmode|Aborting batchmode' | ForEach-Object { $_.Line }
} else {
    Write-Output "--- results ---"
    if (Test-Path $results) {
        if ((Get-Item $results).LastWriteTime -lt $launched) { Write-Output "STALE RESULTS FILE" }
        (Select-String -Path $results -Pattern '<test-run ' | Select-Object -First 1).Line
        Select-String -Path $results -Pattern '<test-case .*result="Failed"' | ForEach-Object {
            if ($_.Line -match 'fullname="([^"]+)"') { $Matches[1] }
        }
    } else {
        Write-Output "NO RESULTS FILE"
    }
}
```
