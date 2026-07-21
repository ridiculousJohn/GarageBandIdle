---
name: closed-sets-are-enums
description: "John: closed, code-defined value sets must be C# enums, never strings; strings only for open designer-extensible ids"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 72f7735f-b559-4457-a686-45b8e3da765f
---

John rejected string-typed fields for closed value sets (found on reward/upgrade/bar-group Scope): "i don't think any closed-ended fields should be strings. This is a concrete and well-defined set, doesn't need to be designer-created."

**Why:** A closed set that code dispatches on gains nothing from being a string - it just loses type safety and silently accepts typos from imported JSON. The data-driven/open-set rule (see [[project-layout-and-workflow]]) applies to designer-extensible ids (currencies, groups, generators), not to code-defined vocabularies.

**How to apply:** When a field's value set is closed and code switches on it, model it as a C# enum (e.g. ContentScope) and have the JSON importer map spellings to it, failing loudly on unknown values. Candidates that were still strings as of 2026-07-21: UpgradeDefinition._type, UpgradePayload._effect, BarGroupDefinition._fillMode, EventDefinition._type, EventTier._debuffEffect. String ids remain correct for open sets referenced by id.
