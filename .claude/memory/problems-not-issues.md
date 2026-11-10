---
name: problems-not-issues
description: How to weigh a finding - what breaks today on a path that runs - and what belongs in a report
metadata:
  node_type: memory
  type: feedback
  originSessionId: cb7418da-6e5b-4e8a-805b-52c995ee2b57
  modified: 2026-11-10T20:47:32.986Z
---

**Why:** fixing true-but-harmless findings is how the validator grew checks for content that did not
exist. John: "feature creep here just to fix issues instead of fix problems".

**Weighing a finding**
- A PROBLEM produces a wrong number, a wrong save, a crash, or something a player would see as
  broken, in code that runs. An ISSUE is a true statement about a path nothing executes: unauthored
  content, a system a later step writes, content validation already refuses. Issues get a verdict
  and one line, never a mechanism.
- "Nothing reaches it" is about code paths, never about inputs. A fault in a mechanism that runs is
  a fault regardless of what current content feeds it.
- Three questions before reporting a finding: what goes wrong in practice, what the change costs,
  and whether the rule it leans on is John's design or a sentence I wrote.
- A reason for NOT doing something is a claim about the code and gets the same check. For a
  restriction, ask what would break if it were lifted.
- A known defect in the live path is fixed before anything is built on top of it. Whether later
  work depends on it is irrelevant.
- State a finding once, at the weight the facts support. Whether it lands is John's call.
- The review loop has no stopping rule of its own. Supplying it is my job.

**What goes in a report**
- Only decisions with a second option someone would notice, real findings with evidence, and
  repository state. Agent line items are filtered, never passed through.
- Before reporting "one thing", check it against the project. If nothing has to be done, there is
  nothing to report.
- Nothing is added after the work is reported done. See [[ask-before-the-work-not-after]].

**Stale documentation**
- A stale description of code that exists gets fixed. An instruction the next step will follow gets
  fixed. A section describing a system a later step builds is skipped.
