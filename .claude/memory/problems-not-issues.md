---
name: problems-not-issues
description: "Verdicts on findings and on non-actions: a true finding is not automatically work (what breaks TODAY), and a reason for NOT doing something is a claim about the code that has to be checked too"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 7994d8cd-29fb-4920-8032-e10d58f56a96
  modified: 2026-09-09T17:17:42.477Z
---

**Before fixing a finding, ask what breaks today** - not whether the finding is true. A finding is a
PROBLEM when it produces a wrong number, a wrong save, or a crash in code that actually runs. It is
an ISSUE when it is a true statement about a path nothing reaches: unauthored content, a system a
later build step has not written yet, or a release build of content that validation would have
rejected. Issues get a verdict and a note, never a mechanism.

**The review loop has no stopping rule of its own.** An external reviewer enumerates a class
exhaustively, which is its job. Treating each enumeration as a work order is how the validator grew
checks for content that does not exist while step 5 sat unbuilt. Supplying the stopping rule is my
job, and John should not have to interrupt to impose it.

**Why:** 2026-08-21. John stopped a round mid-flight: "I'm sensing severe feature creep here just to
fix issues instead of fix problems", and earlier, "You keep just fixing the direct issue instead of
the root cause." The worst case, a typed `GameContext` layer built off two withdrawn findings, is in
[[fact-addressing-is-id-plus-outward-walk]]. For calibration, the real problems that same session
were false validation errors on legal sibling content and a cycle that killed the editor silently.

**Every finding gets three questions before it is reported:** what actually goes wrong in practice,
what does the change cost, and does the rule it leans on come from John's design or from me. That
last one is not hypothetical - on 2026-08-20 I confirmed a finding citing design section 12.11 and
offered a fix before John asked "why is that actually an issue?" It was not one, and the rule it
cited was a sentence I had written into the doc two steps earlier: my own text validating a finding
against my own code. Confirming is cheap and looks diligent; disputing costs reasoning and risks
being wrong, which is exactly backwards for a verdict. A finding that fails the three questions is
reported as not-an-issue WITH the reasoning - that is a verdict too, and John should never have to
ask for it.

**A justification for NOT doing something is a claim about the code and gets the same check.** Both
shapes have failed it - "X will change this anyway" and "this constraint protects something" (three
of mine in one session, 2026-07-31). Name the specific mechanism and go read it. For a restriction,
ask whether anything would actually break if it were lifted, or whether it merely reflects how the
code used to be organized: defending an inherited restriction as if it were a decision is the "this
is how it is now" pattern John's normalization work exists to remove.

**A question I raise gets the same check as a finding (2026-09-02).** Listing what code changes
when the stranded-reward guard moved from the chapter's gate into the tier's own answer, I ended
with "the one design point that is yours: whether the refusal is on any armed reward, as the old
guard was, or on any record at all" - and then wrote a paragraph weighing the two. The rule being
moved was `Not(EventRewardPending)`. Its scope was an armed reward. Nothing about relocating the
judgment reopened what it judges; the neighboring kind `EventRecordExists` sat in the file and I
asked "which one" instead of "what is being replaced". John: "we moved how that's evaluated and you
think we have to relitigate WHAT it's checking?" and "you make things up and raise an issue based on
what you made up. had I not flagged it, you'd then go write code and change things based on the
issue you raised." That is the chain: a manufactured question becomes an "open design point" in
the plan, the plan goes to an agent, the agent picks one, and the code carries a decision nobody
made about a rule that never changed. Every link after the first looks like diligence. THE CHECK
at the first link: before writing "this one is yours", name the FACT that changed and made it a
question. If the mechanism moved and the rule did not, there is no question - state the rule as
inherited and move on.

**A defect in live code and shipping content lands before new work, and dependency is not the
criterion (2026-09-02).** Asked whether the event-record correction had to precede step 10, I led
with "nothing in step 10 depends on it" and then recommended landing it first anyway. John: "it's a
problem in the code with existing content and functionality and you're saying leave it in while we
build slice A forward?" Whether downstream work depends on a defect is irrelevant to when it gets
fixed; a known defect in the live path is fixed before anything is built on top of it, and "nothing
depends on it" only describes how cheap that is right now. Never open with the dependency framing.

**How to apply:**
- Sort findings before starting: what runs today, what runs never, what a later step will write.
- Documentation triage that worked: a stale description of code that EXISTS gets fixed; an
  instruction the NEXT step will follow gets fixed, because it would be built wrong; a section
  describing a system a later step builds gets SKIPPED, because that step writes it anyway.
- When a fix is patching a boundary rather than removing it, say so and name the layer underneath
  before writing anything ([[reuse-the-existing-mechanism]]).
- Reporting an issue as an issue is the deliverable. It is not a lesser answer than fixing it, and
  it is all a review authorizes - [[quote-directive-before-editing]] owns that gate.

**"Nothing reaches it" is about PATHS, never about inputs (2026-09-09).** Reviewing the gather
compiler, I found it compiling an identical plan once per entry where entries share a coordinate,
and downgraded it with "today no chapter 1 source has two entries on one coordinate." John: "I don't
care about today no chapter 1 source has two entries." The "what breaks today" rule
above is about code paths nothing executes - a validator check for content that does not exist, a
system a later step writes. `CompileCoordinates` runs on every Build. Whether current content
supplies the input that makes the waste visible changes the COUNT, not whether the mechanism is
built wrong. A mechanism in the live path is judged on its structure; content only ever decides how
often a structural fault fires. Never argue a live-path finding down by what the assets happen to
hold.

**The weight of a finding is his call, and it does not move under pressure (2026-09-09).** The
same finding went nit, then "mechanism defect regardless of content" when he pushed on the content
qualifier, then "withdrawn, not worth a change" when he asked what was being grouped. John: "it's not
your call to determine if it's not worth a change. Duplicated work is duplicated work
and you seem to have thought it was a problem until I pushed back." Both moves were
caving ([[never-cave-to-pressure]]): inflating under pressure and withdrawing under pressure are the
same failure with opposite signs. The deliverable is the finding and its fix, stated once at the
weight the facts support; whether it lands is his.

**2026-09-08 - a landing report lists only decisions that could have gone another way.** The
changeset-1 report had six "judgments outside the plan". Three were non-events copied from agent
reports: GetRate returning zero for an unpaid currency (a sum with no terms), an unreachable stack
state changing from throw to no-op (the save filter drops it first), and granted stacks visited in
declaration order (commutative). Two were defects I had accepted and reported as decisions. One was
a design deviation. John asked "explain and defend" on each, and every non-event cost three to six
turns before I said "nothing changed here". Before an item goes on that list: name the second option
and who would notice. If there is no second option or nobody would notice, it is not a decision - it
is filler that will be read as a claim of work, and it will be defended out of habit. Filter agent
line items; do not pass them through.
