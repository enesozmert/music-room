---
id: SPEC-048
title: One place defines the document build's environment — closing O30
status: done            # draft | approved | in-progress | done | blocked
tier: A                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S3a              # which DELIVERY-PLAN slice this belongs to
requirements: [V.8]
decisions: [ADR-0035, ADR-0015]
standards: [ci-cd-containerization, openapi-documentation]
approved_by: agent      # all seven gate answers are no; the values involved are published placeholders
escalation:             # none
---

# One place defines the document build's environment

## Goal

`make openapi` is the only definition of what the OpenAPI generator needs to run. Both workflows call it
instead of carrying their own copy, so the next start-up requirement is added once rather than three times —
issue **O30**, which closes here.

## Out of scope

- **What the generator produces.** Not one byte of `docs/api/openapi.json` changes; this is about where the
  environment that produces it is written down.
- **Running `make e2e` in CI.** A different missing job, already recorded as O31.
- **The rest of either workflow.** Restore, format, build, test, the vulnerability scan and the image build
  are untouched.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no |
| 2 | new or upgraded dependency? | no — `make` is on `ubuntu-latest` |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | no — the values moved are the published placeholders the Makefile already carries, and the signing key is generated and discarded as before |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The workflows run `make openapi`.** | O30's own finding: the generator boots the real host, so every start-up requirement has to be satisfied in the environment, and three copies of that list diverge the first time one grows. SPEC-043 proved it — the Makefile gained `OIDC_SIGNING_KEY`, the local gate passed, and CI failed. SPEC-024's mail guard and SPEC-005's JWT settings had already done the same thing twice |
| 2 | **`OPENAPI_BUILD_FLAGS` keeps CI's `--no-restore`.** | Both workflows restore in an earlier step, so re-restoring is a network round trip for nothing. A variable with an empty default means the local target is unchanged and CI keeps the flag it had — consolidating must not quietly make CI slower |
| 3 | **The Makefile keeps the comments explaining each value.** | They are the reason each placeholder is safe to publish. Moving the list without the reasoning would leave three deleted explanations and one list nobody can audit |

## Files

| Path | Action |
|---|---|
| `Makefile` | change — `OPENAPI_BUILD_FLAGS`, and a note that CI calls this target |
| `.github/workflows/backend-dev.yml` | change — the step calls `make openapi` |
| `.github/workflows/backend-prod.yml` | change — the same |
| `docs/SDD.md` | change — O30 closes |
| `docs/specs/SPEC-048-one-openapi-environment.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| `make openapi` locally, then `git diff --exit-code -- docs/api/openapi.json` | the target still produces the committed document |
| `make agent-check` | the gate that calls the same target is unaffected |
| **this pull request's own `gate` run** | the modified workflows work — a CI change that cannot be proven locally proves itself by running, which is exactly how O30 was found |
| `grep OIDC_SIGNING_KEY .github/workflows/` | no copy of the generator's environment remains outside the Makefile |

## Done when

- [x] both workflows call `make openapi`
- [x] no start-up variable for the document build is named in a workflow file
- [x] the `gate` run on this pull request passes
- [x] O30 is in the closed table
- [x] `status: done` in this file
