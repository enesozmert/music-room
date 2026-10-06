---
aliases: [claude-commands]
kind: tooling
updated: 2026-09-27
---

# .claude/

Claude Code assets for this repository. Committed, so every session and every team member gets
the same commands and the same guard.

| Path | What |
|---|---|
| `commands/` | slash commands. Each one is a prompt from `docs/prompt-library/PROMPTS.md`, nothing new |
| `hooks/write-boundary.sh` | `PreToolUse` guard on `Edit` / `Write` / `NotebookEdit` |
| `settings.json` | wires the hook. Project-scoped; personal preferences belong in `settings.local.json`, which is git-ignored |

## Commands

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

**The prompt library stays canonical.** A command is a shortcut to a prompt, never a second copy
of the rules. Change the prompt first, then the command.

Slash commands are Claude Code only. Gemini CLI and the rest read the same prompts from
`docs/prompt-library/PROMPTS.md`, which is why the bodies stay tool-agnostic.

## The hook

Three rules have no legitimate exception, so they are enforced mechanically rather than by a
guardrail paragraph the model has to remember:

1. `reference/` is read-only — subject IV.1.
2. **No `.env` file is written** — `.env`, and `.env.prod` or any other suffix the owner keeps for a
   deployment. `.env.example` is the one exception, because it names keys and never values.
3. A `.pdf` is a generated artifact. Edit the Markdown, then run `docs/build-pdf.sh`.

Everything else stays a matter of judgement and review. A hook that blocks too much gets disabled,
and a disabled hook enforces nothing.

The hook needs `jq`. It exits 2 on a violation, which blocks the tool call and shows the reason.

Unattended runs are separate: `.agent/permissions.json` scopes what `claude -p` may touch. The
hook applies to both, because it is wired in `settings.json`.

Related: `../AGENTS.md` · `../CLAUDE.md` · [[prompt-library]] · [[agent-runner]]
