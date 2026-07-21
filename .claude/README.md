# Claude Code project configuration

`memory/` is Claude Code's persistent project memory, committed so it travels
with the repo across machines.

Claude only reads it from here if the machine-local settings point at it. On
each new machine, create `.claude/settings.local.json` (gitignored) containing:

```json
{
  "autoMemoryDirectory": "<absolute path to this repo>/.claude/memory"
}
```

The setting must live in `settings.local.json` or user settings - Claude Code
deliberately ignores `autoMemoryDirectory` in the checked-in `settings.json`.
