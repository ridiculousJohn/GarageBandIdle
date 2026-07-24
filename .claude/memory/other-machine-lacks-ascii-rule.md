---
name: other-machine-lacks-ascii-rule
description: "Commits pulled from John's other computer may contain non-ASCII glyphs (em-dashes, multiplication signs) in comments and strings"
metadata: 
  node_type: memory
  type: project
  originSessionId: 7fdee689-40c0-4d19-beaf-29fedcff0cea
  modified: 2026-07-24T17:14:33.768Z
---

John's other development machine does not have the no-non-ASCII rule in its
CLAUDE.md, so commits originating there use em-dashes, multiplication signs,
and similar glyphs in C# comments and string literals (confirmed 2026-07-24
after a merge brought in ~145 of them).

**Why:** The global rule (ASCII only in comments and string literals) applies
per-machine, and remote-side commits bypass it.

**How to apply:** After merging or pulling work authored on another machine,
sweep C# sources with a check like `grep -rnP "[^\x00-\x7F]" --include="*.cs"`
and normalize (em-dash to "-", multiplication sign to "x", ">=" for the
greater-equal glyph). String literals pinned by LogAssert.Expect in tests must
change together with the messages they pin, then run the editor tests. The
UTF-8 BOM in Plugins/BreakInfinity/BigDouble.cs is vendored code; leave it.
