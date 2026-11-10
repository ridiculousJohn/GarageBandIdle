---
name: code-conventions
description: "Code and test conventions for this project that are not obvious from the code - number types, enums, narrowing, ASCII, what a test may contain"
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:47:06.487Z
---

**Numbers**
- Every value the runtime could compute past a double is `BigNumber`, authored fields included:
  thresholds, amounts, formula constants, multipliers, growth, coefficients, bar fill amounts and
  rates. John: "I should be able to author any number in any field that the game could eventually
  calculate on its own." Only counts (`int`) and `Pow`'s power (`double`) are exempt; wall clocks
  are seconds.
- When transcribing a doc shape into code, transcribe the types literally.
- In JSON a number past about 1.8e308 is authored quoted (`"1e400"`).
- `BigNumber` refuses NaN and infinity in its constructors. Never add a finiteness check at a
  consumer. Convert to `BigNumber` before mixed arithmetic, or the double overflows on the way in.

**Types**
- A closed value set that code switches on is a C# enum; the importer maps spellings to it and fails
  loudly on an unknown one. Strings are for open, designer-created ids, flags, tags and stat names.
- A derived class needing a base member at its own type uses a generic base class
  (`ScopeState<TFacts>`). Covariant return overrides do not compile in Unity (CS8831), and `new`
  member hiding is rejected by John. A single generic accessor at one boundary is acceptable.
- A cast, null check, depth test or runtime guard standing in for a type means a class is missing:
  add the class.

**ASCII**
- ASCII only in comments and string literals. John's other machine lacks the rule, so after a pull
  or merge sweep with `grep -rnP "[^\x00-\x7F]" --include="*.cs"` and normalize. Strings pinned by
  `LogAssert.Expect` change together with the message. The BOM in `Plugins/BreakInfinity/BigDouble.cs`
  is vendored: leave it.

**Tests**
- A fixture that builds content is fine. A second implementation of anything the runtime does is
  not: tests drive the real runtime. If call sites need converting, convert them.
- A test that cannot reach the real API is a signal about the API, not a licence to write around it.
- A test holds for every value its inputs can legitimately take, real config defaults included.
  Never curate inputs, loosen an assertion or delete a test to get green; a workaround that weakens
  a test is named as a workaround in the same sentence that reports it.
- Tolerance for computed `BigNumber` chains; exactness only where the value is exact by construction.
