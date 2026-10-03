---
kind: adr
id: ADR-0040
status: Accepted
date: 2026-10-03
---

# ADR-0040 — A pull request need not be up to date with `main` to merge

## Context

[[ADR-0027-agent-push-and-pr]], amended the same day, turned repository auto-merge on so a pull
request lands by itself once `gate` passes. It did not land by itself. Every one sat at
`mergeStateStatus: BEHIND` instead, because branch protection on `main` carried
`required_status_checks.strict: true` — GitHub's *"require branches to be up to date before
merging"*.

`strict` means: the head must contain the tip of the base. Any merge into `main` puts every open
pull request behind, and auto-merge does not fix that. It waits. So a human still had to rebase
and force-push each one, which is the step auto-merge was supposed to remove — the backlog came
back one layer down.

The right instrument is GitHub's **merge queue**: it rebases and tests each entry against `main`
automatically, which is `strict` without the manual rebase. It is not available here. Creating a
ruleset with a `merge_queue` rule is refused:

```
422 Validation Failed — Invalid rule 'merge_queue'
```

The repository is private on a personal account, and merge queue needs Team or Enterprise Cloud.

## Decision

**`required_status_checks.strict` is off.** Everything else about the protection stands:

| Setting | Value |
|---|---|
| Required check | `gate` |
| `strict` (up to date before merging) | **false** |
| `enforce_admins` | true |
| Required approvals | 0 |
| Auto-merge | on |
| Delete branch on merge | on |

A pull request merges when `gate` is green on its own head, whatever `main` has done since.

## Consequences

- **A pull request can land having been tested against an older `main`.** GitHub's
  `pull_request` event builds the merge of head and base as it stood when the run started, so
  `gate` tested a real merge — just not the merge that finally happens. Two pull requests that
  each pass alone and break together land anyway. That is the whole of what this decision costs,
  and it is the cost `strict` existed to prevent.
- **`backend-prod` is the compensating control, and it already exists.** It runs on every push to
  `main` that touches `MusicRoom/`, the workflows, the `Makefile` or `docker-compose.yml` —
  release build, tests, vulnerable-package scan. A `main` broken by a semantic conflict is red
  within minutes rather than at the next person's checkout. A documentation-only merge does not
  trigger it and cannot break the build.
- **`main` can be red.** It could not before: `strict` plus a required check meant every merge was
  tested against the exact tree it produced. The honest reading is that the project traded a
  guarantee for throughput, and the recovery path is a revert, which is one command because
  every spec is one commit ([[ADR-0025-agent-git-protocol]]).
- `.agent/land-stack.sh` loses its last routine job. Rebasing a behind branch was the only reason
  to run it on a healthy repository; what remains is genuine conflicts and stacks, and
  `AGENTS.md` §5b forbids building a stack.
- **Revisit if the repository moves to Team.** Merge queue is strictly better than this: it keeps
  the guarantee and removes the manual step. This decision is a workaround for a plan limit, not
  a preference.

## Alternatives rejected

- **Merge queue.** The correct answer. Refused by the API on this plan, as above.
- **Keep `strict` and have a workflow rebase every behind branch.** A push made with
  `GITHUB_TOKEN` does not trigger workflow runs, so `gate` would never re-run on the branch it
  just updated and the pull request would sit `BLOCKED` for ever — the same trap that lost #43.
  Fixing that needs a personal access token in a secret, which is a credential in the repository's
  configuration to buy back a setting we can simply turn off.
- **Keep `strict` and rebase by hand.** What happened today. It is the backlog this and ADR-0027's
  amendment exist to remove.
- **Require an approving review as the compensation.** Two developers, one of whom is on the
  client workstream. A required review that one person cannot give blocks everything; a review
  they rubber-stamp proves nothing.

## Affected

Design: [[sdd]] §14.1. Rules: `git-workflow`, `ci-cd-containerization`.
Related: [[ADR-0027-agent-push-and-pr]], [[ADR-0025-agent-git-protocol]],
[[ADR-0015-hosting-and-deployment]].
