---
kind: adr
id: ADR-0027
status: Accepted
date: 2026-09-24
supersedes: ADR-0025 (push clause only)
---

# ADR-0027 — The agent pushes its branch and opens a draft pull request

## Context

[[ADR-0025-agent-git-protocol]] forbade pushing outright: one branch and one commit per spec, and
a human did everything that reached the remote. In practice that makes every cycle end in a manual
step, which defeats the point of an unattended run — the human becomes the bottleneck on the
cheapest part of the work.

Pushing is an outward action. A misfiring cycle would write to the remote, and that is exactly
what the prohibition prevented. Removing it needs something better than a promise in a prompt.

## Decision

At stage 5, **after** `RESULT: pass`, the agent:

1. pushes its own `feat/spec-<nnn>-<slug>` branch, and
2. opens a **draft** pull request with `gh pr create --draft`.

One pull request per spec. Nothing else changes about the git protocol from
[[ADR-0025-agent-git-protocol]].

### Still forbidden, absolutely

- pushing to `main`, or to any branch that is not the spec's own
- `--force` in any form, on any branch
- deleting a remote branch or a tag
- marking a pull request ready for review
- merging, `--auto` merge, or approving
- reopening, closing or editing anyone else's pull request

### What makes this safe rather than hopeful

Three layers, none of them a prompt:

| Layer | Mechanism |
|---|---|
| Precondition | the push happens only after `RESULT: pass`; a blocked or environment result pushes nothing |
| Remote | `main` carries branch protection with **`enforce_admins: true`** — direct pushes, force pushes and branch deletion are refused even for the repository owner's token |
| Human gate | the pull request is a **draft**. Draft pull requests cannot be merged, so nothing reaches `main` without a person marking it ready |

`enforce_admins` matters more than it looks: the agent runs with the owner's `gh` token. With
admin enforcement off, branch protection would have been decorative.

## Consequences

- A cycle now ends with reviewable work on the remote and CI already running against it. The human
  reviews a diff in a pull request instead of a working tree.
- **Per-spec review stays intact.** One spec, one branch, one commit, one pull request, one
  decision to merge or discard.
- CI runs per spec, so a regression is attributed to one unit of work.
- Branches and draft pull requests accumulate during a long run. Cheap, and self-describing.
- The human keeps the two decisions that matter: ready-for-review, and merge.
- `required_status_checks` is empty until the workflows exist (slice S0 creates them). Add the
  check names to the protection rule as soon as they do, otherwise CI is advisory.
- To push to `main` directly in an emergency, admin enforcement must be turned off deliberately:
  `gh api -X DELETE repos/<owner>/<repo>/branches/main/protection/enforce_admins`, and turned back
  on afterwards. Making that awkward is the point.

## Alternatives rejected

- **The driver script pushes, not the agent** — keeps the agent without the capability at all,
  which is stronger. Rejected for ergonomics: the stage that knows the spec id, the requirements
  advanced and the verification output is stage 5, and that is where a good pull request body is
  written.
- **Normal pull request instead of draft** — one fewer click, and one fewer thing standing between
  an unattended cycle and `main`.
- **Auto-merge on green CI** — the most automated option and the wrong one here. Green CI is not a
  correct implementation: subject V.2 grades race-condition handling, and a concurrency bug can
  pass every test that exists. Human review is the control for exactly that class of defect.
- **Push at the end of a run rather than per spec** — fewer CI runs, but a failure then spans
  several pull requests at once.
