---
aliases: [agy-skills]
kind: tooling
updated: 2026-10-01
---

# .agents/

Antigravity CLI (`agy`) assets. Committed, so the commands travel with the repository instead of
living in one person's home directory.

| Path | What |
|---|---|
| `skills/<name>/SKILL.md` | one skill per prompt. `agy` turns each into a `/<name>` slash command |

`agy` discovers these at `{workspace}/.agents/skills/{skill_name}/SKILL.md`. The `name` and
`description` in the frontmatter are what the `/` menu shows.

## The same nine commands as Claude Code

| Command | Does | Source |
|---|---|---|
| `/bootstrap` | load the router, skip the workspace scan | prompt 1 |
| `/plan <task>` | file list first, no code this turn | prompt 2 |
| `/adr <decision>` | draft an ADR for a decision you already made | prompt 7 |
| `/sdd-sync <change>` | bring `docs/SDD.md` back in line after a change lands | prompt 11 |
| `/review-diff` | review the diff against the standards the files touch | prompt 12 |
| `/brain-check` | audit the knowledge layer, no source code | prompt 15 |
| `/status <thing>` | is it built? evidence-backed verdict, read-only | prompt 16 |
| `/spec-status` | every spec that is not done, and what it waits on | section 2.9 |
| `/progress` | count how far the backend has got, and refresh the board | `.agent/progress.sh` |

**These are generated from `.claude/commands/`.** Same bodies, same source prompts, one place to
change a rule. Regenerate with:

```bash
make agy-skills
```

`agy` reads `AGENTS.md` by itself, so no rule file repeats the contract here.

A skill added while a session is open is not visible to that session. Restart it.

## Why the repository and not `~/.gemini/config/skills/`

Both work. The home directory does not get committed, does not reach the other developer, and
drifts from `.claude/commands/` silently. `agy` supports a workspace customization root precisely
so a team can commit this.

Related: `../.claude/README.md` · [[prompt-library]] · `../AGENTS.md`
