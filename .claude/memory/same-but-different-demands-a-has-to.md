---
name: same-but-different-demands-a-has-to
description: "When John says two mechanisms are the same thing spelled twice, split the bundle and answer HAS-TO per item; conceding equivalence while defending the bundle just prolongs the argument"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 533d6416-ac70-421c-9f20-8833435edd57
  modified: 2026-08-26T18:39:17.759Z
---

The idle-stats argument (2026-08-27): John pressed that idle_rate was "a rate modifier
conditionally applied during idle" and that same-but-different "is a STRONG indication that it
shouldn't be different... I have yet to hear a real reason why it HAS to be different." I conceded
the arithmetic equivalence early but kept defending the three-stat design as a block for many
rounds. The argument resolved in one message once I split the bundle: game_speed has a has-to
(scales seconds not units/second, sole clamp point, reaches bar fills), idle_cap was never a
multiplier at all, idle_rate had no has-to. Two of three conceded, one kept, and his follow-up
extensions (live-only buffs, chapter-local idle) made the replacement design strictly better.

**Why:** "It's equivalent but I prefer this spelling" is not an answer to "why does it HAVE to be
different" - once equivalence is conceded, the design point is conceded unless a per-item has-to
exists. Defending a bundle hides that its members have different answers. This is
[[reuse-the-existing-mechanism]] arriving from John's side: when he invokes it, the burden of
proof is mine, per item. See also [[never-cave-to-pressure]] - splitting the bundle is not caving;
it is where the facts actually were.

**How to apply:** on a same-but-different challenge, immediately decompose into the smallest
independently-justifiable pieces and answer each with a concrete has-to (a different quantity, a
different consumer point, a different policy like a clamp) or a concession. Small residual
preferences ("it's displayable", "fewer validator cases") are worth one line, stated as small -
they never carry a defense on their own.
