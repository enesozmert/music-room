---
kind: adr
id: ADR-0025
status: Accepted
date: 2026-09-24
---

# ADR-0025 — Git protocol for unattended agent runs

## Context

[[prompt-library]] Part 2 lets an agent implement a spec without supervision. The guardrail block
forbids `git push` and destructive git commands, but said nothing about committing. That left two
bad defaults: the agent never commits and a five-cycle run piles into one undifferentiated dirty
tree, or it commits straight to `main` and breaks [[git-workflow]].

## Decision

**One branch and one commit per completed spec. Never push.**

```
branch:  feat/spec-<nnn>-<slug>          created from the current branch at stage 3
commit:  <type>(<scope>): <subject>      one per spec, at the end of stage 5
push:    never
```

- The branch is created **only after** the spec reaches `approved` (stage 2 gate clean).
- The commit happens **only after** stage 4 reports `RESULT: pass`. A blocked spec leaves its work
  uncommitted on the branch for a human to inspect.
- The commit message follows Conventional Commits and names the spec id in the body.
- `git push`, `git reset --hard`, `git rebase`, force operations, tag creation and remote
  manipulation remain forbidden.

The driver prints `git --no-pager diff --stat` and the branch name after each passing cycle.

## Consequences

- **Rollback is per spec.** `git branch -D feat/spec-007-...` throws away exactly one unit of work.
  That property is why an unattended run is reviewable at all.
- Each cycle starts from a clean tree, so `git status` stays a usable signal instead of noise.
- The human does two things the agent cannot: open the pull request and push. Both are outward
  actions, and both are where review belongs.
- Branches accumulate locally during a long run. Acceptable; they are cheap and self-describing.
- **A clean working tree is a precondition for starting a run.** An agent committing on top of
  unrelated uncommitted changes would capture them in the spec's commit. The driver should refuse
  to start on a dirty tree.
- A blocked spec deliberately leaves work uncommitted: the diff is the evidence a human needs.

## Alternatives rejected

- **Commit on the current branch, no branching** — less movement, but reverting one spec then needs
  a cherry-pick against a linear pile.
- **No commits at all** — maximally controlled and incompatible with the point of unattended runs:
  five cycles of changes stacked on each other cannot be reviewed as five units.
- **Commit and open a pull request automatically** — the most automated, but it requires pushing.
  A misfiring run would then write to the remote, and the guardrail exists precisely to keep a bad
  cycle local.
