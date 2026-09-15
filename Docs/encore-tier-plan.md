# The Encore tier

Plan for the first after-the-plan item, 2026-09-14. The decision is design section 9's "The Encore
tier" paragraph (John, 2026-09-14); this file is the shape it takes in code, content and tests.
Sections cited are `garage-band-idle-design.md`. Suite at the start: 762/762.

## What this is

Banked Encore time up to a threshold runs at 2x; banked time beyond the threshold runs at 4x, up
to the cap. The Pass holds the top of that ladder permanently. It is two root buffs, each a
constant `game_speed x2`, and one grant writing two records, so that the moment speed falls from
4x to 2x is a record expiry, which is the only kind of edge the tick and the idle claim cut their
windows at. Nothing here touches the segment walk, the condition family, the effect shape, or the
speed read. The design paragraph says why two records and not one formula; this plan does not
re-argue it.

What exists and is reused, checked 2026-09-14:

- `encore`, root's `game_speed x2` modifier, `appliesWhen` `Any[HasEntitlement(backstage_pass),
  BuffActive(encore)]`, listed in `permanentModifiers`.
- `GameSession.ExtendBuff(scope, modifier, seconds, nowUtc, completed)`: the one grant. It creates
  or extends the record at `scope` and clamps remaining time to `config.encoreCapSeconds`. Its one
  caller is the ad callback in `AdManager`, passing root and `Encore.Modifier(root)`. Its cap has
  been Encore's since slice A, so it is Encore's grant in everything but its name.
- `TickSystem.Boundaries` admits every timed record's `expiresAtUtc` strictly inside a window, and
  `TickSystem.Segments` cuts there; `GameSession.EnterChapter` walks the paid idle window through
  the same two methods. `TickSystem.GameSpeed` is the one clamped speed read, `maxGameSpeed` 4.
- `BuffActive` judges a record by `expiresAtUtc > ctx.NowUtc`; the tick prunes expired records at
  its end; the save filter keeps expired records and drops only ids no scope on the chain declares.
- `Encore` (Meta): the modifier id, its resolve off root's own list, and its `CodeReferences`
  existence check. `EncoreTime.Text` (TopBarUI.cs) prints the pill's and the window's remaining
  time, infinity for a Pass owner. `EncoreWindowUI.RefreshDescription` prints the one modifier's
  game-speed factor through `Producer.FactorOf`.
- `GameConfig.encoreAdSeconds` (14400) and `encoreCapSeconds` (86400), `Require` refusing a cap
  below one grant.

## Decisions

1. **Two records, one grant.** `ExtendBuff` writes the tier record in the same transaction it
   writes Encore's: after the clamp, the tier record's expiry is Encore's expiry minus the
   threshold, unconditionally. When that lies in the past the record is simply expired: `BuffActive`
   reads it dead, `Boundaries` admits no past edge, and the next tick's end prunes it. No branch.
   The alternative was a new root command replacing `ExtendBuff`; rejected because `ExtendBuff` is
   already Encore's grant (its cap is Encore's config) and 12.11 lists it by name. The plan makes
   that explicit in 12.11's entry-point line rather than renaming it.
2. **The threshold is config, the factors are content.** `GameConfig.encoreTierSeconds` (86400)
   beside `encoreCapSeconds`, which rises to 129600. `Require` refuses a threshold that is
   nonpositive or non-finite, and one at or above the cap, since a tier the bank can never reach is
   malformed. The two x2 factors are the two modifiers' authored effects.
3. **The second modifier is content.** Id `encore_tier` (John's to rename), `game_speed x2`,
   `appliesWhen` `Any[HasEntitlement(backstage_pass), BuffActive(encore_tier)]`, listed in
   `permanentModifiers`. The code names it once, in `Encore.TierModifierId`, resolved and validated
   exactly as `encore` is.
4. **The Pass holds both.** Nothing in code says so; it is the first leg of both `appliesWhen`s. A
   Pass owner with no record reads 4x from the two entitlement legs. The idle claim for an owner
   then pays every segment at 4x and doubles the lines once more, which is the design's stance and
   a tuning fact, not a plan question.
5. **The UI reads the tick's speed.** The pill and the window print the live clamped speed from
   `TickSystem.GameSpeed` at the foreground chapter, not a modifier's factor, so they say what the
   tick uses: 4.00x for an owner or a deep streak, 2.00x for a plain streak, 1.00x with nothing
   live. The window's description becomes the ladder read from content and config. Wording is
   placeholder, the polish pass's.

## The changes

### GameConfig, GameConfig.asset

- `public double encoreTierSeconds = 86400;` beside the two Encore knobs, with the comment: banked
  Encore time past this runs at the tier's speed; a placeholder like the cap.
- `encoreCapSeconds` default becomes 129600 in the class and in `Assets/Settings/GameConfig.asset`
  (the asset does not list it today, so the class default has been governing; the asset gains both
  lines so the knobs are visible in the inspector).
- `Require`: the finite-positive check with the existing message shape, then
  `encoreTierSeconds >= encoreCapSeconds` refused: "a threshold at or above the cap is a tier the
  bank can never reach". The existing cap-below-grant check stays.

### Encore (Meta), CodeReferences

- `Encore.TierModifierId = "encore_tier"`, `Encore.TierModifier(root)` resolving off root's own
  list like `Modifier`, and `Validate` checking both ids with a message naming which is missing.
  `CodeReferences` is unchanged, since it already calls `Encore.Validate`.

### GameSession.ExtendBuff

- After the cap clamp, inside the same command body:
  `var tier = FindBuff(scope, Encore.TierModifierId)` or a new `TimedBuff` added to
  `scope.timedBuffs`; `tier.expiresAtUtc = record.expiresAtUtc.AddSeconds(-config.encoreTierSeconds)`.
- The method comment gains the tier sentence: the second record IS "Encore remaining exceeds the
  threshold" held as a stamp, so the tick and the claim cut at it as at any expiry. Design 9.
- The completed callback fires once, after both writes, as today; `AdManager` is unchanged.

### root.json

- The `encore_tier` modifier after `encore`, the shape above, and its `permanentModifiers` entry.

### Content import

- The runtime reads the generated ScriptableObjects, not the JSON, and the import is a manual
  editor step (`Garage Band Idle/Import Content`, or the headless `ImportAll`). The slice is not
  delivered until it has run: it regenerates `root.asset` with the new membership and writes
  `ScriptableObjects/root/Modifiers/encore_tier.asset`, and both ship with the slice. Without it a
  development boot is refused by `Encore.Validate`, and a release build, which skips validation,
  throws in the Encore window's constructor at the tier's resolve. The reimport also regenerates
  `SerializeReference` rids across the other content assets; that churn is the serializer's, not a
  content change, and is reported as such.

### The window and the pill

- `EncoreWindowUI` and `TopBarUI` take the `GameSession` in place of the `RootScopeState` (they
  read `session.Root` where they read `root` today), so they can ask the speed at
  `session.ForegroundChapter` with `session.Config`. Both are constructed by the host, which holds
  the session.
- `EncoreTime` gains `Speed(GameSession session, DateTime nowUtc)`: `TickSystem.GameSpeed` over a
  context at the foreground chapter, and 1 when no chapter is in front. The pill's `Interpolate`
  prints the pill's existing clock glyph, the remaining time, and the speed: "05:15:03  4.00x"
  after the glyph.
- `EncoreWindowUI.RefreshDescription` prints the ladder from the two modifiers' factors and the
  threshold: `"Encore runs at 2.00x. Bank more than 24:00:00 and it runs at 4.00x."`, the
  factors through `Producer.FactorOf` as today (the tier factor times the base factor is the top),
  the threshold formatted by the existing `GrantDuration`. The remaining line becomes
  `"Time remaining 05:15:03 at 4.00x"` from `EncoreTime.Text` and `EncoreTime.Speed`.
- No UXML or USS change.

### Docs

- Design 9: the tier paragraph's "not yet built" goes; 12.11's entry-point line for `ExtendBuff`
  says it writes both records; 12.13's `GameConfig.cs` line names `encoreTierSeconds`.
- Content doc: root's modifier table gains `encore_tier`; the Encore line's "not yet authored" goes.
- The build plan's after-the-plan bullet becomes the landing line; this file's status records it.

### Tests

- `GameConfig` rows (TickSystemTests): `encoreTierSeconds` zero, negative, NaN each throw; a
  threshold equal to the cap throws; the defaults pass.
- Content keystone: root declares `encore_tier`, `game_speed x2`, the two-leg gate over its own
  id, listed in `permanentModifiers`; `Encore.Validate` over content missing it reports the id.
- `EncoreTests`:
  - a grant of 4 hours from nothing writes Encore at now plus 4 hours and the tier at now minus 20
    hours: present, expired, `IsBuffActive` false for it and true for Encore;
  - a grant taking the bank from 22 to 26 hours writes the tier at now plus 2 hours, and a further
    grant moves both by 4 hours; the cap clamps Encore to 36 hours and the tier follows at 12;
  - the tick: with 26 hours banked, one tick spanning the tier's expiry pays the part before it at
    4x and the part after at 2x, and the tick's end prunes the tier record while Encore stands;
  - the claim: 26 hours banked at the stamp, away 6 hours under a cap that pays all six, the offer
    is 2 hours at 4x plus 4 hours at 2x, sixteen hours of base rate, computed through the same walk
    (the design paragraph's own example);
  - a Pass owner with no record reads speed 4 from `GameSpeed`; with a deep record, still 4 (the
    clamp is the ceiling of the authored product, nothing stacks past it);
  - the save round-trips both records and drops neither.
- `ScreenHostTests`: the window's description and remaining line for a plain streak, a deep
  streak, and an owner; the pill's speed suffix in the same three states; the existing "2.00x"
  and "01:01:01" assertions updated to the new strings.
- Walkthrough 13.4's Encore variant is unchanged: its record is under the threshold.

## Out of scope

- Any ad or store SDK. The fake completes as today.
- Wording, glyphs and layout of the pill and window: the polish pass.
- Tuning the threshold, the cap, or the factors: placeholders like every other number.
- Overclock-style stacking of a third tier: nothing designs one, and the clamp forbids it.

## Status

Not started.
