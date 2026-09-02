---
name: step-9-open-ui-items
description: "Decisions and open items from the first hand playthrough of the step 9 screen (2026-09-02) that the code and docs do not record - the digit font, the omitted group title, the chrome literals"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3215b57f-b4ac-47f3-a94d-d30a07004e33
  modified: 2026-09-02T03:23:02.335Z
---

State after commit 857bedc (2026-09-02), the slice D playthrough:

- **Open: a font with equal-width digits.** Measured in John's editor: the default runtime theme's
  numerals share one advance width EXCEPT "1" (33.33 vs 35.04 panel units for a five-glyph string
  at 14px), so a right-anchored value label still shifts by a fraction of a glyph whenever a "1"
  enters or leaves. UI Toolkit has no tabular-figures switch, so the fix is a font asset (monospace,
  or a face with tabular digits) plus a `-unity-font-definition` rule for numeric labels. John
  deferred it ("we can fix that issue later") and does not know Ctrl C's font. Alignment and
  formatting are NOT the fix - both were tried in thought and the measurement ruled them out.
- **Decided: the bar group block has no title.** Bar groups are not on the validator's closed
  displayName list and `learn_covers` authors none, so a title rendered blank; the section title
  names the band. If a group title is ever wanted: displayName on the group in the JSON, bar groups
  added to the closed list in `ContentValidator`, the title line back in `BarGroupUI`. John: "fine,
  I was asking in general if in the future we need it".
- **Accepted as code-owned English:** the chrome literals "Select"/"Selected"/"Done" (bar row),
  "Start"/"Dismiss"/"Claim reward"/"Ns left"/"Time's up"/"Goal" (event row), "Would bank:" (rung),
  "cost => yield" (generator). John saw them and raised no objection; every NAME is content.
- **Future: bulk buy** ("+1"/"+N") on the generator row, per Ctrl C - see
  [[ctrl-c-is-the-reference-game]].
- Slice E (select, collect dialog, `EntryChapter` stopgap removal) is the last step 9 changeset;
  until it lands a boot more than 180s after the last exit with any rate production renders a blank
  screen (AwaitingIdleClaim has no widget). John deleted his save once to get past it.

**How to apply:** when the font comes up, start from the measurement above, not from the display
format. When slice E's plan is written, the blank-screen note is its motivating symptom.
