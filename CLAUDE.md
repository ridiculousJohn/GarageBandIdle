# Garage Band Idle

`Docs/garage-band-idle-design.md` is the authority for architecture. Section 12 is the
implementation spec; `Docs/build-plan.md` owns the order and the current position.

## Scope is the whole lookup mechanism

Resist the urge to add a registry, an id index, a label-based catalogue, or a "find all X" pass.
Every time this project has needed a repair, that urge is what caused it: a global content database,
tree-wide id uniqueness to prop it up, and a chain of aliasing questions that followed. All of it
was deleted.

The rule is design doc section 12.14 requirement 8. In short: everything is declared on a scope, and
a name resolves by walking OUTWARD from the acting scope to the first scope that declares it. Two
walks are legitimate, because both start from a scope the caller already holds - outward along the
chain, and downward through one named subtree. Nothing else is. Validation is the one exception: it
audits the whole tree at load, once, which is what lets every runtime walk trust its own chain.

If content seems to need a lookup, it has not been placed on a scope yet. That is a conversation
before it is code.

## No write without a live order

Before any tool call that writes - Edit, Write, a mutating Bash command, a commit, `git add`, a
branch, a push - name the outstanding order that authorizes it, quoted from John's chat:
`Acting on: "<his words>"`. No quote, no call. A paraphrase is not a quote. A quote that is not an
imperative aimed at me is not an order: "the X should do Y", a design fact stated in anger, a
question, and an answer to a question I asked are all inputs to a verdict. Writes to the scratchpad
and to `.claude/memory` are outside this.

An order is live only until it is satisfied. Delivering the work extinguishes it - once completion
is reported there is no standing authority, and the next write needs a new order. Mid-task is the
only carry-forward, and only for the work that order described: never a new kind of action, never a
widened scope. No fix is obvious and no change is trivial; diff size is not risk.

A review, a bug report, an audit finding, a correction, or a question is a request for an ANSWER.
Confirm, deny, accept, reject, explain, analyze - none of them is a go, and naming the fix inside a
verdict is not permission to write it. The same holds when the reasoning concludes that something is
blocked or should be asked about: send the question and wait. Building a second path around a
blocked one is this failure wearing a different label.

This has been written down as memory four separate times and broken anyway, because every breach
felt like finishing a thought already in motion rather than starting an action. That is why the
check is mechanical and not a principle: a quote exists or it does not, and there is no reasoning
that produces one John did not write.
