# CLAUDE.md — music-room

**Read `AGENTS.md` first.** It is the shared contract: retrieval path, write targets, authority
order, non-negotiable rules, definition of done, escalation triggers. Everything there applies.

This file holds only what is specific to Claude Code.

## Quick pointers

| Need | File |
|---|---|
| Context router | `brain/MEMORY.md` |
| What to build next | `docs/DELIVERY-PLAN.md` |
| API contract | `docs/api/ENDPOINTS.md` |
| Consolidated design | `docs/SDD.md` |
| Security conformance | `docs/owasp/README.md` |
| Prompts | `docs/prompt-library/PROMPTS.md` |

## Slash commands

`.claude/commands/` wraps the prompts in `docs/prompt-library/PROMPTS.md`. The library stays
canonical; a command is a shortcut, never a second copy of the rules.

`/bootstrap` · `/plan <task>` · `/adr <decision>` · `/sdd-sync <change>` · `/review-diff` ·
`/brain-check` · `/status <thing>` · `/spec-status`

`.claude/hooks/write-boundary.sh` blocks the three writes that have no legitimate exception:
anything under `reference/`, `.env`, and any `.pdf`. See `.claude/README.md`.

## Claude Code specifics

- **Plan mode** for anything touching more than two files. Present the file list before writing.
- **Subagents** for read-only fan-out only (locating code, mapping a directory). Implementation
  stays in the main thread so the diff is reviewable in one place.
- **`/loop`** drives the autonomous recipes in `docs/prompt-library/PROMPTS.md` Part 2. Use the
  stop conditions written there; an unbounded loop on this repository is a way to burn tokens and
  produce unreviewable diffs.
- Prefer `Bash` with `cat`/`sed`/`grep` for reads under bypass-permissions mode; use the dedicated
  file tools when Bash cannot do the job.

## Hard rules, repeated because they are the expensive ones

- Stay in your root: `MusicRoom/` for backend, `MusicRoomApp/` for the client. `reference/` is read-only.
- Never lower an `.editorconfig` severity to make a build pass.
- Never mark `docs/SDD.md` §16 `done` without code.
- Never read or print a secret.
