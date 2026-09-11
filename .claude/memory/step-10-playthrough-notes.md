---
name: step-10-playthrough-notes
description: "John's notes from the step 10 hand playthrough (2026-09-11) - the defect list, sorted by kind; 7 and 11 landed 2026-09-11, 10 is content-only and open, the rest are open"
metadata: 
  node_type: memory
  type: project
  originSessionId: 725fbcaf-f7c7-487b-a644-802df186dfc3
  modified: 2026-09-11T21:34:29.257Z
---

John's notes from the first hand playthrough after slice E, 2026-09-11, fresh save. He said
"making notes, not a directive to make changes" and then "we'll update the doc and content in a
moment". Each item carries the fact I checked behind it and its kind. Nothing here is an order.

**Code**
1. Colors: dark grey background with black text is unreadable. (UI polish pass territory, but
   noted here because he hit it first.)
1b. The Encore window stays open after "Boost for 4 hours" or "Boost forever" succeeds. Slice D
   built it that way: the grant lands as a callback command and nothing closes the overlay. He
   wants it to close on the service's successful return.

**Architecture first (a description field no content family has)**
2. Upgrades (Stage Presence, New Strings...) say nothing about what they do. Ctrl C shows cost and
   flavor icons on the button, and a long-tap gives an explanation.
3. The Rehearsal Space and its currency do not say what they are for.
4. Learned covers do not display what they provide.
5. The Gear section does not say why it exists or what its buttons do (Kit Upgrade, Tight Set,
   Time to Record).
   (2-5 are one gap: content authors displayName and gate uiText, and no upgrade, currency or bar
   has a description. That is a content shape question before it is a widget one.)

**Content only**
6. Time to Record costs 0 and only sets the `album` flag, revealing The Release and The Backyard
   Party; hard to see what it did.
7. LANDED 2026-09-11 (rename, leg uiText, content doc). Cut a Demo says nothing about why records matter (root `records` feeds `records_income`, cash
   rate everywhere). "Would bank: +8 Records, Garage Records" reads wrong. DECIDED: `ch1_records`
   ("Garage Records") is renamed "Demo Tapes" - what cutting a demo produces and what earns the
   gig. The capstone leg gets a uiText ("Hand out 30 Demo Tapes"); today it is textless, so the
   button shows bare progress "14/30". Two currencies exist and are equal until the first chapter
   completion: root `records` (career, never resets, header shows it) and `ch1_records` (capstone
   counter, zeroed by the chapter reset). Content doc section 3 row and section 13 mentions change
   with the rename.
8. Kit Upgrade (x2 drummer cash rate) produced no noticeable change with few drummers.
9. The Backyard Party button is grey until 30 `ch1_records`; the section itself appears on the
   `album` flag. Item 7's uiText is the fix.
10. OPEN, content only (John settled the shape 2026-09-11). After the Garage Jam's reward, the
   tier reset hides The Band (gate `EarnedTotalAtLeast(cash, 100)` at tier1; the reset clears the
   earned total). Content doc section 2 states reveals re-walk every run - intended today. John: an
   exposed section should stay exposed and its rows go disabled. The list widgets show a row while
   its own `availableWhen` holds and grey its button while unaffordable, so the shape is: a chapter
   flag per revealed thing, a tier1 trigger setting it at the reveal moment, and BOTH the section
   gate and the row's gate reading that flag (the amp shares the section's flag; a ladder-gated
   bandmate such as the drummer gets its own flag and trigger if it is to stay revealed). The Gear
   and its upgrades take the same treatment. Chapter flags survive a tier reset, as `album` does.
   I first reported this as blocked on a widget rule; it is not - a row whose gate has never been
   met stays hidden, which is what he asked for. One real caveat: the Rehearsal Space's flag also
   drives the `rehearsal` currency's `activeWhen`, so latching it at ch1 changes the economy
   (rehearsal accrues from the start of a replay); decide that separately. Plus the section 2
   sentence in the content doc.
11. LANDED 2026-09-11 (`reveal_release` trigger, upgrade deleted, orphan asset removed, content doc sections 6, 11, 12, 13.1, 14, four code comments). Time to Record is a zero-cost upgrade (the row prints "0 Cash") whose only action is
   `SetFlag(album)`; `album` is read by exactly two section gates, The Release and The Backyard
   Party. John: needless - a tier1 trigger with the upgrade's own gate (50 fans, one cover learned)
   sets the flag directly (chapter 1 authored NO triggers before this - `fans_revealed` and
   `rehearsal_revealed` are set by BOUGHT upgrades, so the trigger family is the shape, not a ch1
   precedent; I claimed otherwise and was wrong); the
   upgrade is deleted, The Gear loses a row, Cut a Demo's offer condition already repeats the two
   legs. Content only, plus the content doc's upgrade and trigger tables.

**How to apply:** when he says update the doc and content, this is the list; confirm each item's
kind against the code before editing, and land 7 (decided) first.
