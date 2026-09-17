# Garage Band Idle — Design backlog

Ideas kept but not committed to a chapter. **Nothing here is spec.**
`garage-band-idle-design.md` is the authority; when an entry is committed, write it there and into
the chapter's JSON, and delete it from this file.

---

## Cost-growth inheritance

Chapter 1 authors `growth: 1.15` on every generator's cost block *and* restates it as
`constants.generatorCostGrowth` — two homes for one number. Chapter 2 authors one chapter default
(1.3) and no per-generator growth; Chapter 3's draft wants two curves in one chapter (cash gear vs
rehearsal gear) and would get it by overriding per generator.

The fix is scope resolution, not a new content kind: declare a cost-growth default on a scope and let
a generator's own `cost.growth` win when present, otherwise the outward walk finds the default
(§12.14 requirement 8). Buff targeting needs nothing at all — tags already reach a set of generators,
which is how Ch2's `gig_draw` and `promo` tags work.

Open: whether the default sits on the chapter scope only, or whether an intermediate scope per gear
group is worth existing.

**Why vary growth.** It sets how many purchases happen before the next tier opens. Low (1.10–1.15)
gives many cheap buys, good for onboarding; high (1.3–1.6) gives few expensive ones, each a decision,
leaving attention free for a dense new mechanic. Raise it as a chapter's mechanic density rises. Pick
growth first, then set base costs against it. Ctrl C varies it per chapter (1.6 in Ch1–3, 1.3 in
Ch4/5/7/8, 1.1 plus a 10x bonus tier in Ch6).

---

## Fan fatigue extensions

Base fatigue is Chapter 3's mechanic (`chapter-03-local-venues.json`): each room carries a `perFill`
factor below 1 scaled by its own `fillCount`, and it resets with the run. These are two ways it could
grow.

**Fatigue softens as the band gets big.** A local band that plays the same room three weekends running
stops drawing; a band people travel to see sells out four nights. Scaling the penalty down with
popularity makes fatigue a through-line rather than a rule the player outgrows. **Scale the floor, not
the per-play factor** — raising the floor keeps rotation better while removing the punishment for not
rotating; driving the factor toward 1 deletes the decision. Cumulative Records is the obvious input.

Caveat: a replay is just the chapter played again with root multipliers reaching in, so a late player
re-entering would arrive at full popularity and no fatigue at all — the rotation would vanish on
exactly the replay it was meant to pace. Whatever feeds the floor has to be something the chapter's
own reset clears.

**Ticket price as a second turnout lever.** Conditional on a ticket-price concept existing, which it
does not yet; Ch4 (booked venues) or Ch5 (touring) are the plausible homes. Higher price means a
thinner room but more Cash per head; lower means a fuller room, more Fans, less Cash. That turns the
standing Cash-vs-Fans tension into a per-show decision and pairs with fatigue — a tired room at a high
price is empty. Without that trade it is an income multiplier with extra steps. It would be a third
lever on one action, so it belongs in a chapter that has retired something else.

**Where fatigue recurs.** Ch3 house parties (same crowd, same houses). Ch5, where per-town fatigue is
the breadth-vs-depth question the branch pair below poses. By Ch7–8 the floor should be high enough
that fatigue is mostly flavor.

---

## Branch pair — "exclusive at first, both eventually"

Leading candidate home: **Chapter 5 (Regional Tour / The Van)**.

A chapter offers two mutually exclusive `contentUnlock` upgrades plus a third **merge** upgrade that
removes the exclusivity. Each branch opens its own sub-system, so the choice changes how the chapter
is played rather than what the numbers are.

### How it maps onto the primitives

- Each branch is a `contentUnlock` whose payload is `setFlag`, gated partly on the other branch's flag
  being unset (`Not[FlagSet]` — §12.4 has `Not`).
- **The choice recurs** if the two branch flags are `scope: run` — an album release clears them and
  the pick is made again — while the merge flag is `permanentInChapter`. Ch1 already uses this split
  to make the second run re-walk band → fans → covers.
- **The merge is priced in a different currency than the branches**, which decouples when it arrives
  from how deep the player is in the prestige layer.
- Each branch's sub-system reveals on its flag like anything else.

Nothing new is required.

### Requirements

1. **The branches must resolve on different axes.** Two always-on multipliers on the same number make
   an arithmetic problem with one right answer, not a choice, and the weaker rule is dead content.
   Reliable separations: a set-shape multiplier vs a per-unit one; different outputs; different
   contexts favoring different rules. Ctrl C separates by *kind of content unlocked* — generators vs
   upgrades — which is simpler than any of these.
2. **Owning both must have a visible joint optimum.** If one arrangement satisfies both rules the
   second purchase reads as a reward; if they can only be traded off, it reads as a tax.
3. **Open the fork early** and let it run most of the chapter.
4. **Neither branch may gate advancement.**

### Why Chapter 5

Its mechanic is routing across towns, so breadth vs depth is already the subject: play every town
(coverage) against camp the markets that respond (compounding). Joint optimum is a loop hitting strong
markets repeatedly while still covering fresh ground. Per-town fan fatigue is the axis the branches
are read against. **Alternative:** Ch4. Ch6 is a poor fit — Catalog is already the biggest new system
and "write more vs polish fewer" would compete with the quality roll.

### Worked example (was sketched for Ch2, kept because the shape is reusable)

- **Range** — a combo meter building during a performance; each item differing from the previous adds
  a stack multiplying the rest.
- **Signature** — pick one tag; items of that tag earn a currency that multiplies every item of that
  tag.

Adjacency vs count. With five slots and a Loud signature, `Loud → Sad → Loud → Fast → Loud` maxes both
at once — the signature takes every other slot. The joint optimum is "your sound, with range around
it."

---

## Difficulty-scaled replay rewards

§8.1 anticipates this: "a player-chosen difficulty whose handicaps derive from the choice exactly as
event handicaps do (§6.1) and whose reward formula pays more for the harder clear. Only the
chosen-difficulty variant needs anything new — an action recording the choice as a fact." The Roadie
payout is an ordinary `AddCurrency(roadies, PayoutFormula)`, so a harder clear paying more is a
different formula over a stored fact.

Ctrl C is precedent — it scales the completion-token reward by a stored difficulty index
(`1 + 0.1*d + 0.012*d^1.5`) rather than scaling the goal. One difference to carry over: its award is
fractional, which is fine when the UI says "1 token" but wrong here, since Roadies are allocated as
discrete units. Keep the payout integer.

---

## Reference

`C:\Users\jsp73\Downloads\Ctrl-C-Chapters-1-8\` — Reference, Data Appendix, Story Transcript. A static
reconstruction of Ctrl C (Android 1.12.2), tagged [D] direct data / [C] code-derived / [I] inference /
[U] unresolved. Useful for comparison on prestige layering, per-chapter cost curves, and the
completion-token system, which is structurally the same as Roadies at the same +5% rate.
