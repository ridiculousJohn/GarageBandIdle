---
name: show-dont-describe
description: "When a change is visual or animated, record it from the running game and send it to John - never describe it and leave him to go play"
metadata:
  node_type: memory
  type: feedback
  originSessionId: c7163722-abf0-496a-bd00-e0131f4a8e97
  modified: 2026-11-12T23:29:20.657Z
---

For anything John would judge by eye (a layout, an animation, a window), show him the running game:
a `capture_game_view` screenshot for a static screen; for motion, capture every frame with
`ScreenCapture.CaptureScreenshot` from a `schedule.Execute(...).Every(0)` in play mode, build an
MP4 - NEVER a GIF (John, 2026-11-12: every other project gave him a movie file) - with
`C:\Program Files\ffmpeg\bin\ffmpeg.exe` (`-c:v libx264 -crf 18 -vf scale=540:-2,format=yuv420p
-movflags +faststart`; a slowed copy at ~3 fps plus real time), keep it in the scratchpad, and send
it with SendUserFile.

**Why:** 2026-11-12, after I reported the section reveal only as numbers: "why can't you show me
what you're doing with the reveal instead of just explaining it and making me go play myself?"

**How to apply:** part of verifying any visual change, before reporting it done. Clear the game
session by reflection before stopping play so nothing saves. The page ScrollView is the one with
`mode == Vertical`; the first ScrollView found is an overlay's.
