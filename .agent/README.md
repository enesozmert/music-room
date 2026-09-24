---
aliases: [agent-runner]
kind: tooling
updated: 2026-09-24
---

# .agent/

Unattended agent operation. Recipes: `docs/prompt-library/PROMPTS.md` Part 2.

| Path | What |
|---|---|
| `run.sh` | tool-agnostic driver. Runs Part 2 cycles until done, blocked, or the cycle budget runs out |
| `STOP` | create this file to halt after the current cycle. Delete it to resume |
| `runs/` | per-cycle logs. **Git-ignored** — transient, never committed, never copied into `brain/` |

## Use

```bash
AGENT_CMD="claude -p" ./.agent/run.sh 5
AGENT_CMD="gemini -p" ./.agent/run.sh 3

touch .agent/STOP     # pull the cord
rm .agent/STOP        # resume
```

## What keeps this safe

Three independent layers, because a prompt alone is not a control:

1. **The spec gate** — `docs/specs/README.md`. Seven questions; any "yes" needs a human.
2. **The guardrail block** — `PROMPTS.md` section 2.8, pasted into every unattended prompt.
3. **The driver** — a cycle cap, a `STOP` file, a refusal to start on a dirty working tree, and a
   refusal to continue on an unrecognised result line.
4. **The git protocol** — [[ADR-0025-agent-git-protocol]]: one branch and one commit per spec,
   never a push. Rollback is `git branch -D feat/spec-<nnn>-<slug>`.

Add a fifth if you run this often: a Claude Code `PreToolUse` hook that mechanically refuses
writes under `reference/`. See `PROMPTS.md` section 3.1.

## What autonomy does not cover

Architecture. The subject requires the team to own and defend its decisions, so an agent may
draft an ADR and stop, never adopt one. Same for dependencies, schema changes beyond a spec,
the public API contract, and anything touching authentication, authorisation, tokens or secrets.

## Related

`../AGENTS.md` · [[prompt-library]] · [[specs]] · [[delivery-plan]]
