---
name: is-the-editor-open
description: "The ONLY way to tell whether John's Unity editor is open on this project - never by process name. Applies to every task, not just verify runs."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 419a5cc1-800f-4018-923c-04aa2418a7ad
  modified: 2026-11-12T18:09:15.667Z
---

Whether the editor is open on this project is answered by exactly this, for ANY reason the question
comes up (a save edit, a warning to John, a verify run, anything):

```powershell
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like '*Hub\Editor*' -and $_.CommandLine -like '*Garage Band Idle*' }
```

Never `Get-Process Unity`, never a name match, never `Temp/UnityLockfile`. A process named `unity`
is usually the Unity CLI's MCP server (`...\AppData\Local\Unity\bin\unity.exe mcp --project-path ...`),
which runs with every session and has no window.

**Why:** John has corrected this many times; the rule lived only inside
[[unity-headless-verify-loop]], so any task that was not a verify run skipped it. On 2026-11-12 I
told him Unity was open (it was the MCP server) while making him a fresh chapter 2 save.

**How to apply:** before saying anything about the editor's state, run the query above. Empty
result = not open.
