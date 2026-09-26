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
| `permissions.json` | scoped Claude Code permissions. Without it a non-interactive run cannot ask, so every ungranted write is refused and the cycle ends with nothing on disk |
| `STOP` | create this file to halt after the current cycle. Delete it to resume |
| `runs/` | per-cycle logs. **Git-ignored** — transient, never committed, never copied into `brain/` |

## Use

```bash
make agent-run            # recommended: scoped permissions + live output
make agent-run CYCLES=5

touch .agent/STOP         # pull the cord
rm .agent/STOP            # resume
```

`make agent-run` expands to:

```bash
AGENT_CMD="claude -p --settings .agent/permissions.json --permission-prompts none \
           --output-format stream-json --verbose" \
AGENT_FILTER="jq -r ..." ./.agent/run.sh 3
```

Three things that matter in that line:

- **`--settings .agent/permissions.json`** — a bare `claude -p` cannot ask for permission. Without
  the settings file an ungranted write is simply refused and the cycle ends having produced
  nothing. This was observed, not theorised.
- **`--permission-prompts none`** — anything that would prompt is denied instead of hanging. A
  stalled unattended run is worse than a refused one.
- **`--output-format stream-json` plus a `jq` filter** — text output arrives only at the end, so a
  long cycle looks frozen. The filter prints assistant text and tool names as they happen.

Any other agent still works: set `AGENT_CMD` yourself and leave `AGENT_FILTER` unset.

### What the permission file grants

Writes are scoped to `MusicRoom/`, `MusicRoomApp/`, `docs/specs/`, `docs/api/` and the handful of
index files a spec legitimately updates. Denied outright: `reference/`, `.env`, `.editorconfig`,
`brain/decisions/`, `docs/technical-guide/`, `.github/`, the permission file itself, pushes to
`main`, force pushes, `gh pr merge`, `gh pr ready`, `gh api`.

**Bash rules match a whole-command prefix.** `Bash(git push -u origin feat/spec-:*)` matches
nothing, because `feat/spec-` is half a word — the agent simply loses the capability and says
so at the end of a cycle. Allow the command broadly and deny the dangerous forms: push is
allowed, while `main`, `--force`, `--force-with-lease`, `--delete`, `--mirror`, `--all` and
`--tags` are denied. Branch protection with `enforce_admins: true` is what actually stops a
push to `main`; these rules are the first layer, not the last.

**Path rules must be written as `Edit(...)`, never `Write(...)`.** Claude Code matches file
permissions through `Edit` rules, which cover every file-editing tool; a `Write(path)` rule is
silently ignored and the protection you think you have does not exist. Both directions are
verified — an allowed path writes, `reference/` is refused.

A Bash allowlist by command prefix is defence in depth, not a sandbox: `dotnet` and `make` can
reach a shell. The spec gate, branch protection and draft pull requests are the real controls.

## What keeps this safe

Three independent layers, because a prompt alone is not a control:

1. **The spec gate** — `docs/specs/README.md`. Seven questions; any "yes" needs a human.
2. **The guardrail block** — `PROMPTS.md` section 2.8, pasted into every unattended prompt.
3. **The driver** — a cycle cap, a `STOP` file, a refusal to start on a dirty working tree, and a
   refusal to continue on an unrecognised result line.
4. **The git protocol** — [[ADR-0025-agent-git-protocol]] and [[ADR-0027-agent-push-and-pr]]:
   a blocked spec commits its work in progress on its own branch with a `wip(spec-<nnn>):`
   message and does not push, so the tree stays clean and the next cycle can resume;
   one branch, one commit and one **draft** pull request per spec. Never a push to `main`, never a
   merge. Rollback is `git branch -D feat/spec-<nnn>-<slug>` plus `gh pr close`.
5. **Branch protection on `main`** with `enforce_admins: true` — direct pushes, force pushes and
   branch deletion are refused by GitHub, not by a prompt. The agent runs with the owner's token,
   so without admin enforcement the rule would have been decorative.

Add a sixth if you run this often: a Claude Code `PreToolUse` hook that mechanically refuses
writes under `reference/`. See `PROMPTS.md` section 3.1.

## Reviewing what a run produced

```bash
gh pr list --draft                       # one draft per passing spec
gh pr diff <number>
gh pr ready <number>                     # you decide this
gh pr merge <number> --merge             # and this
```

## What autonomy does not cover

Architecture. The subject requires the team to own and defend its decisions, so an agent may
draft an ADR and stop, never adopt one. Same for dependencies, schema changes beyond a spec,
the public API contract, and anything touching authentication, authorisation, tokens or secrets.

## Related

`../AGENTS.md` · [[prompt-library]] · [[specs]] · [[delivery-plan]]
