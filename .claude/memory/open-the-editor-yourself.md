---
name: open-the-editor-yourself
description: "When a check needs the Unity editor and it is closed, open it myself - never stop and ask John to open it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: c7163722-abf0-496a-bd00-e0131f4a8e97
  modified: 2026-11-12T20:27:22.256Z
---

If a check needs the editor (in-editor test runs, play mode, screenshots) and the editor is closed
(see [[is-the-editor-open]]), launch it myself with the project's editor version, e.g.
`& 'C:\Program Files\Unity\Hub\Editor\<ProjectVersion>\Editor\Unity.exe' -projectPath '<repo>\Garage Band Idle'`
(started detached), wait for unity-editor-mcp to report ready, and carry on.

**Why:** 2026-11-12 I stopped a fix to ask John to open the editor. John: "why can't you just open
the fucking editor if it's closed?"

**How to apply:** only CLOSING his editor needs asking (he may be mid-play). Opening it is mine.
Batchmode still needs it closed, so pick headless or in-editor by which check is needed.
