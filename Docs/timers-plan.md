# Timers

Plan for the timer refactor, 2026-09-14. It replaces the Encore tier plan, which built the wrong
thing: a 4x tier the code required. Sections cited are `garage-band-idle-design.md`. Suite at the
start: 762/762, at commit 7d91632.

## What John said, verbatim

The plan is these sentences. Every line under "The changes" traces to one of them, and a line that
does not is the defect.

1. "I want the ability to be able to do EITHER THE 2X OR THE 4X BUFF, BASED ON A FUTURE YET TO BE
   MADE DECISION. I DON'T WANT TO HAVE TO WRITE ANY MORE CODE TO SUPPORT IT."
2. "it's a buff. it exists if it's added or not, by content."
3. "every single part of that should be content. the ABILITY TO HAVE BUFFS ON THE ROOT is the code.
   And if we have timed buffs, the ABILITY TO EXTEND THE TIME ON A BUFF IS FUNCTIONALITY. From all
   of that, Encore can be made."
4. "we have a second place to keep values (like buff timers) that buffs can reference. that way two
   buffs could reference the same value."
5. "when you buy Encore with an ad, it just adds 4 hours to some global 'encore-timer' registry entry
   on the scope (this could just be a general variable store on scopes). Then in this case, buffs
   can read their time from the registry if it's configured to do so"
6. "so TimedBuffs just becomes Timers? ExtendBuff just extends a timer. Why does BuffActive have to
   change? that should be a function of the buff itself"
7. "if we want absolute timed events then it would have to be an option on the event definition"
   (not this plan)
8. "revert everything you fucked up, and re-plan timers and everything related to Timed buffs to
   match this"

"Registry" in 5 means a declared value on a scope, resolved by the outward walk like a flag or a
currency (12.14.8); nothing here is a lookup.

## What this is

A timer is a declared value on a scope: an absolute UTC expiry under a name the scope declares. A
buff declares which timer it reads and how much time must remain for it to count as active. The
ability to extend a timer is a command and an authored action. From those three things Encore is
content: root declares timer `encore`, the `encore` modifier reads it, and the Encore ad's reward
is the authored action "add 14400 seconds to timer `encore`, cap 86400". A 4x, if it is ever
decided, is one more modifier reading the same timer with 86400 seconds as its band, and nothing in
code changes for it or knows about it (sentences 1, 2).

The code side stops naming Encore. The `Encore` static, its `CodeReferences` check, the two Encore
numbers in `GameConfig`, and the ad callback's resolve of the modifier all go. What remains named
in code is the AD PLACEMENT, which is a product slot the SDK knows by id, and the Encore chrome,
which is a pill and a window; both read what root authors for them.

What exists and is kept, checked 2026-09-14:

- `TimedBuff { buffId, expiresAtUtc }` on `ScopeState.timedBuffs`: the record. Its shape is the
  timer's value already; only the meaning of the id changes, from a modifier's to a timer's.
- `GameContext.IsBuffActive(id)`: the outward walk to the record and `expiresAtUtc > NowUtc`.
- `TickSystem.Boundaries`: every record's expiry strictly inside the window is a segment edge, and
  the idle claim walks the same segments. Pruning at the tick's end. Nothing here changes except
  one more kind of edge (below).
- `GameSession.ExtendBuff`: create at now plus the grant, or extend from the later of expiry and
  now, then clamp remaining time. The body is the timer extend; its cap and its caller are what
  tie it to Encore today.
- `ActionList` executes an authored action list in a context. The ad reward runs through it.
- The save filter keeps expired records and drops an id nothing on the chain declares.

## Decisions

1. **Timers are a declared family** (sentences 4, 5). `ScopeDefinition.declaredTimers`, a list of
   ids like `declaredFlags`, in the per-chain name space (12.12's collision rule). Root declares
   `encore`. A timer's value is the `TimedBuff` record with that id on the declaring scope; absent
   or expired both read as not running. The type and field names `TimedBuff` / `timedBuffs` /
   `buffId` STAY: they are the save schema's field names, and renaming them is a save migration
   with a version bump for no behavioral gain (12.10). Their comments say what they hold now. If
   John wants the rename anyway it is its own line item with the migration.
2. **A buff reads a timer it declares** (sentences 5, 6). `ModifierDefinition` gains `timer`
   (string id, optional) and `activeAfterSeconds` (double, default 0). `BuffActive(modifier)` keeps
   its shape and its content gate; its body asks the modifier for its timer and answers "remaining
   time exceeds `activeAfterSeconds`", so a buff with no band is active while its timer runs, and a
   buff with 86400 is active while more than 24 hours remain. Two buffs naming one timer is the 4x.
   Validation: a modifier's `timer` resolves on its chain like a flag; `BuffActive` on a modifier
   with no `timer` is an error; `activeAfterSeconds` finite and nonnegative.
3. **The extend is a timer command and an action** (sentences 3, 5, 6). `ExtendBuff` keeps its name
   and takes `(scope, timerId, seconds, capSeconds, nowUtc, completed)`: the body is unchanged, the
   cap is the caller's number, and the record it writes carries the timer id. `ExtendTimer` joins
   the `GameAction` kinds beside `SetFlag` and `AddCurrency`: `{timer, seconds, capSeconds}`, writing
   at the timer's home through the same outward walk `SetFlag` uses, validated the same way
   (declared on the chain, seconds finite positive, cap finite and at least seconds).
4. **The ad's reward is content** (sentence 5). `RootDefinition.encoreAdReward`, a list of
   `GameAction`, executed at root's context by the ad callback through `ActionList` as one command
   when the rewarded ad completes. `root.json` authors `[ExtendTimer(encore, 14400, 86400)]`.
   The field is named for the placement it pays, `AdPlacement.EncoreExtension`, which is a product
   slot in code; what it grants is whatever the list says. `GameConfig` loses `encoreAdSeconds` and
   `encoreCapSeconds` and their `Require` lines.
5. **The Encore chrome binds a timer root names** (sentence 5, and the pill exists). `RootDefinition.
   encoreTimer`, a string id, the timer the pill counts down and the window shows. The window's
   "Boost for N" reads N off the reward list's `ExtendTimer` action, so the promise is the grant.
   The window's factor line keeps reading the game-speed effect of the modifiers whose timer is
   `encoreTimer`, as it reads `encore`'s today. Both fields validated on root: the timer declared,
   the reward list non-empty and valid. This is the one place a code-side surface has a name for
   Encore, and it is the name of a pill.
6. **The walk learns one edge** (sentence 1: no code later). A buff with a band flips at its timer's
   expiry minus the band. `Boundaries` already walks root and the foreground subtree; on each node
   it also visits the scope's own modifier list, and for a modifier with a timer and a nonzero band
   it admits that timer's expiry minus the band, when the edge lies strictly inside the window. The
   record's own expiry is still admitted as today. This is what makes a 4x pure content: its edge is
   cut without anything named.
7. **The `Encore` static and its code reference go.** `CodeReferences` keeps the Pass and Roadies
   checks. `EncoreTime` reads root's `encoreTimer` record instead of the `Encore.ModifierId` record.
8. **Events are unchanged** (sentence 7). Their remaining-seconds counter stays; an absolute event
   is a future option on the event definition.

## The changes

### Content: `ScopeDefinition`, `ModifierDefinition`, `RootDefinition`, DTOs, importer

- `ScopeDefinition.declaredTimers` (List<string>) with `DeclaresTimer(id)`, imported from a
  `timers` array like `flags`; the per-chain uniqueness check covers it.
- `ModifierDefinition.timer` (string) and `activeAfterSeconds` (double), imported from the modifier
  object; `Validate` checks the timer on the chain (`RequireTimerOnChain`, the flag's shape) and the
  band's range.
- `RootDefinition.encoreTimer` (string) and `encoreAdReward` (List<GameAction>, `SerializeReference`
  like every action list); imported; validated at root.
- `ExtendTimer : GameAction { timer, seconds, capSeconds }` with its DTO and `KindRegistry` entry.
- `root.json`: `"timers": ["encore"]`; the `encore` modifier gains `"timer": "encore"`;
  `"encoreTimer": "encore"`; `"encoreAdReward": [{ "type": "ExtendTimer", "timer": "encore",
  "seconds": 14400, "capSeconds": 86400 }]`. No other content changes. Reimport; `root.asset` and
  `encore.asset` regenerate, rid churn elsewhere.

### Runtime: `GameContext`, `Condition`, `GameSession`, `TickSystem`, `SaveSystem`

- `GameContext.IsBuffActive(string timerId, double activeAfterSeconds)`: the same outward walk,
  `expiresAtUtc > NowUtc.AddSeconds(activeAfterSeconds)`. `TimerHome(timerId)` beside `FlagHome`.
  `ExtendTimer(timerId, seconds, capSeconds)`: the write at the timer's home, the body that is in
  `ExtendBuff` today.
- `BuffActive.Evaluate` calls `ctx.IsBuffActive(modifier.timer, modifier.activeAfterSeconds)`;
  `Validate` requires the modifier on the chain as today and a non-empty `timer` on it.
- `GameSession.ExtendBuff(scope, timerId, seconds, capSeconds, nowUtc, completed)` runs
  `c.ExtendTimer(...)` through `RunCommand`. `GameSession.RunReward(List<GameAction> actions,
  DateTime nowUtc, Action<bool> completed)`: root's context, `ActionList` over the list, one
  transaction. The ad callback calls it with root's `encoreAdReward`.
- `TickSystem.Boundaries`: the modifier-band edges (decision 6).
- `SaveSystem`: the record filter asks `DeclaresTimerOnChain` instead of `DeclaresModifierOnChain`;
  the warning text says timer.
- `GameConfig`: the two Encore fields and their `Require` lines removed; `GameConfig.asset` unchanged
  (it never listed them).
- `Meta/Encore.cs` deleted; `CodeReferences` loses its entry; 12.13 loses the file.

### UI: `AdManager`, `EncoreWindowUI`, `TopBarUI`

- `AdManager`: the `EncoreExtension` delivery runs `session.RunReward(root.encoreAdReward, ...)`;
  `EncoreAdSeconds` goes, and the window reads the grant off the reward list's first `ExtendTimer`
  (a small static on the window, `GrantSeconds(RootDefinition)`, zero when the list has none, and
  the button then reads "Boost").
- `EncoreTime.Text` reads the record named by root's `encoreTimer`.
- `EncoreWindowUI.RefreshDescription` reads the game-speed factor of the root modifiers whose
  `timer` is `encoreTimer` (all of them, multiplied, so a future 4x prints its ladder's top without
  a code change); the sentence stays "While active, game speed is N.NNx.".

### Docs

- Design 9: Encore is content over timers; the tier paragraph becomes "undecided; if authored, one
  more modifier on the same timer with a band, no code". 12.3's state row for `timedBuffs` says
  timer. 12.11's entry points list `ExtendBuff(scope, timer, seconds, cap)` and `RunReward`. 12.12
  gains the timer checks. 12.13 drops `Encore.cs`, adds nothing. 12.14.5 or wherever declaration
  families are listed gains timers.
- Content doc section 2: root declares timer `encore`, the modifier's `timer`, `encoreTimer`, the
  reward list; the tier sentence becomes undecided.
- Build plan: the after-the-plan bullet becomes "undecided; content-only when decided"; the landing
  line goes on this plan's status and a build-plan row.
- `encore-tier-plan.md` deleted.

### Tests

- Declaration: a timer id colliding with a flag or currency on the chain is refused; a modifier
  naming an undeclared timer, or one declared off its chain, is refused with the flag-shaped
  messages; `BuffActive` on a modifier with no timer is refused; the band's range rows.
- `ExtendTimer` action: writes at the timer's home through the outward walk; a reach miss is the
  `SetFlag` error shape; seconds and cap range rows; the cap clamps.
- `ExtendBuff` rows convert to the timer signature; the cap is the argument.
- Two modifiers on one timer: with a band of 86400 on the second, `GameSpeed` is 2 below 24 hours
  remaining and 4 above; a tick spanning expiry minus 24 hours pays 4x before and 2x after; the
  idle claim over such a window pays the same walk (the six-hour example); a fixture with the
  second modifier absent reads 2x everywhere and validates clean, which is sentence 2 as a test.
- The ad reward: `RunReward` over root's authored list extends the timer, saves in the callback,
  closes the window; a reward list of two actions runs as one transaction.
- Save: a record whose timer is not declared on the chain is dropped; declared ones survive.
- Content keystone: root declares `encore` as a timer, the modifier names it, `encoreTimer` is it,
  the reward is one `ExtendTimer` of 14400 capped at 86400. Validator: root missing `encoreTimer`
  or with an empty reward list is refused.
- UI: the pill and window rows read the record by root's `encoreTimer`; "Boost for 4 hours" from
  the reward.
- `MonetizationTests`, `EncoreTests`, `ScreenHostTests`, `TestContent.DeclareCodeReferences` (the
  Encore modifier declared with a timer, root declaring it; the code-reference helper no longer
  declares Encore for validation's sake, only the fixtures that use it do).

## Out of scope

- The 4x itself. Nothing in root.json authors it. The tests prove it is one modifier away.
- Absolute timed events (sentence 7).
- Renaming `TimedBuff` / `timedBuffs` / `buffId` (decision 1's save-schema note).
- Wording of the pill and window.

## Status

Not started.
