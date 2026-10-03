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
| `.lock` | pid of the running cycle. The driver refuses to start a second one — two drivers switch branches under each other and commit each other's work. **Git-ignored** |
| `land-stack.sh` | lands a stack of open pull requests onto `main`, bottom first. Plan-only unless `--ready` |
| `progress.sh` | counts how far the backend has got, from the repository. `--write` refreshes `docs/progress/PROGRESS.json` |
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

## Landing a stack

```bash
make land                 # plan only, changes nothing
make land READY=1         # land them
make land READY=1 LIMIT=3 # land the bottom three and stop
```

Spec work arrives as one branch and one draft pull request each ([[ADR-0025-agent-git-protocol]],
[[ADR-0027-agent-push-and-pr]]), and several stack up. Merging a stack by hand is three commands
per pull request and two traps:

- **Branch protection requires the head to be up to date with `main`**, so every child needs
  rebasing the moment its parent lands.
- **GitHub closes a pull request whose base branch is deleted** rather than retargeting it, and a
  closed one cannot be reopened once that branch is gone. #22 was lost that way on 2026-09-28.

`land-stack.sh` reads the base chain **before** touching anything, retargets every pull request in
the stack to `main` while the chain still exists, then for each one in order: replays its single
commit onto `main` with `git rebase --onto`, regenerates the PDFs, force-pushes with a lease, waits
for `gate`, and merges.

It replays **one commit per branch** on purpose. A stacked branch still carries the pre-rebase
copies of its ancestors, and those conflict with the same changes already landed under a different
sha. One spec is one commit, so `HEAD~1` is the whole of it.

A conflict in a `.pdf` is resolved by rerunning `docs/build-pdf.sh`, because Markdown is the source
and the PDF is an artifact. A conflict anywhere else stops the run and names the files: everything
below that pull request is on `main`, nothing above it was touched.

`--ready` marks the batch ready for review, which is the human signal ADR-0027 asks for — one flag
instead of one command per pull request.

**Since 2026-10-03 the common case does not need this script.** Repository auto-merge is on and an
agent enables it on its own pull request, so one lands as soon as `gate` passes. `land-stack.sh` is
for the case auto-merge cannot handle: a pull request that is behind, conflicted, or sitting on
another open branch. Which is to say, a stack — and `AGENTS.md` §5b now forbids building one. **A human runs this.** Wiring it into CI would mean an
agent merging its own work, which ADR-0027 forbids; amend the ADR first if that is ever wanted.

It takes the same `.lock` as `run.sh` and refuses to start under a live cycle, and it works in its
own detached worktree so a branch checked out in the main working copy is never disturbed.

After a stack lands, the knowledge layer is behind: run `/brain-check`, then `/sdd-sync`.

## Measuring progress

```bash
make progress             # print the summary
make progress WRITE=1     # also refresh docs/progress/PROGRESS.json
```

Everything in the snapshot is counted, never estimated — except two values, which no amount of
grepping can tell:

| Source | Column | Values |
|---|---|---|
| `docs/DELIVERY-PLAN.md` slice table | `State` | `merged`, `in progress`, `blocked`, `not started` |
| `brain/decisions/decisions-index.md` | `Lands in` | `landed`, `landed, <slice>`, `<slice>` |

**Those two columns are the contract.** They are data, not notes: the script reads them, the
GitHub Actions job `progress` recounts the snapshot on every push to `main` that touches the plan,
the SDD, the endpoint inventory, the specs, the decision log or `MusicRoom/`. It cannot push to
`main` — the branch is protected and `enforce_admins` is on — so it force-pushes one fixed branch,
`chore/progress-snapshot`, and writes the progress table plus a compare link into the run summary.

**It does not open the pull request, on purpose.** A pull request opened with `GITHUB_TOKEN` does
not trigger workflow runs, so `gate` never starts on it, and `gate` is a required check on `main`:
the result is a pull request blocked forever that cannot be rescued. #43 was exactly that. A person
opens it from the compare link, or `/progress` does — either way the checks run.

The snapshot on `main` is a convenience for the next session. `/progress` does not need it: it runs
the script itself, then republishes the board. A stale column is therefore a stale
number on a page someone is reading, not a stale note nobody sees.

The snapshot is a generated file. Never edit `docs/progress/PROGRESS.json` by hand — fix what it
counted.
