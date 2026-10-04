---
kind: adr
id: ADR-0027
status: Accepted
date: 2026-09-24
supersedes: ADR-0025 (push clause only)
---

# ADR-0027 — The agent pushes its branch and opens a pull request

## Context

[[ADR-0025-agent-git-protocol]] forbade pushing outright: one branch and one commit per spec, and
a human did everything that reached the remote. In practice that makes every cycle end in a manual
step, which defeats the point of an unattended run — the human becomes the bottleneck on the
cheapest part of the work.

Pushing is an outward action. A misfiring cycle would write to the remote, and that is exactly
what the prohibition prevented. Removing it needs something better than a promise in a prompt.

> **Amended 2026-10-03: the repository carries auto-merge, and the gate is the reviewer.**
>
> Owner decision. Pull requests piled up waiting for a human to type `gh pr merge`, which is the
> same backlog that produced two stacks and everything they cost (`AGENTS.md` §5b). Repository
> auto-merge is now on, and a pull request whose required `gate` check passes merges by itself.
>
> What that changes in the list below: **`--auto` merge is no longer forbidden.** Merging directly
> and approving still are. The agent's hands stay off the merge button; GitHub presses it when
> `gate` is green.
>
> **Amended again 2026-10-04: the pull request is not a draft.** Auto-merge refuses a draft —
> `enablePullRequestAutoMerge` answers *"Pull request is a draft"* — so #68 and #70 sat at
> `auto=false` until a human marked them ready. The draft and the amendment above could not both
> stand, and the draft was the half with nothing behind it: it meant *a human should look*, written
> when a human merged, and `gate` is what looks now. The agent opens an ordinary pull request and
> enables auto-merge on it in the same step.
>
> What that costs, stated rather than discovered: nobody reads the diff before it lands. `gate`
> becomes the whole of review — restore, format, build `-warnaserror`, test, the vulnerable-package
> scan and the OpenAPI drift check. A defect those six do not catch reaches `main` unread. The
> trade was taken knowing it: a pull request nobody merges is not reviewed either, it is only
> delayed.
>
> `/review-diff` is the compensating control and it is a human's to run, before or after the merge.

## Decision

At stage 5, **after** `RESULT: pass`, the agent:

1. pushes its own `feat/spec-<nnn>-<slug>` branch, and
2. opens a pull request with `gh pr create`, and
3. enables auto-merge on it with `gh pr merge --auto --merge`.

   Not a draft: auto-merge refuses one outright.

One pull request per spec. Nothing else changes about the git protocol from
[[ADR-0025-agent-git-protocol]].

### Still forbidden, absolutely

- pushing to `main`, or to any branch that is not the spec's own
- `--force` in any form, on any branch
- deleting a remote branch or a tag
- merging directly, or approving
- reopening, closing or editing anyone else's pull request

### What makes this safe rather than hopeful

Three layers, none of them a prompt:

| Layer | Mechanism |
|---|---|
| Precondition | the push happens only after `RESULT: pass`; a blocked or environment result pushes nothing |
| Remote | `main` carries branch protection with **`enforce_admins: true`** — direct pushes, force pushes and branch deletion are refused even for the repository owner's token |
| Machine gate | the required `gate` check. Auto-merge lands the pull request when it passes and never before, so nothing reaches `main` that did not build, format, test and pass the vulnerable-package and OpenAPI drift scans — amended 2026-10-03 and 2026-10-04 |

`enforce_admins` matters more than it looks: the agent runs with the owner's `gh` token. With
admin enforcement off, branch protection would have been decorative.

## Consequences

- A cycle now ends with reviewable work on the remote and CI already running against it. The human
  reviews a diff in a pull request instead of a working tree.
- **Per-spec review stays intact.** One spec, one branch, one commit, one pull request, one
  decision to merge or discard.
- CI runs per spec, so a regression is attributed to one unit of work.
- Branches and pull requests accumulate during a long run. Cheap, and self-describing.
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
- ~~**Normal pull request instead of draft**~~ — rejected in 2026-09, adopted on 2026-10-04 once
  auto-merge made the draft a blocker rather than a gate. The original objection, below, was that it
  is one fewer thing standing between
  an unattended cycle and `main`.
- ~~**Auto-merge on green CI**~~ — rejected in 2026-09, **adopted on 2026-10-03**. The objection
  stands as written and is now a stated cost rather than a reason: green CI is not a correct
  implementation, subject V.2 grades race-condition handling, and a concurrency bug can pass every
  test that exists. What changed is the comparison. The alternative was never human review; it was
  a pull request nobody merged — eight of them stacked, twice, costing what `AGENTS.md` §5b
  measures. A review that does not happen is not a control. `/review-diff` is the one that remains,
  and it is a human's to run.
- **Push at the end of a run rather than per spec** — fewer CI runs, but a failure then spans
  several pull requests at once.
