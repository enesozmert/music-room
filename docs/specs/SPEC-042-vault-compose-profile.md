---
id: SPEC-042
title: The vault runs in this stack, behind a profile
status: done            # draft | approved | in-progress | done | blocked
tier: A                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1b              # which DELIVERY-PLAN slice this belongs to
requirements: [V.8]
decisions: [ADR-0029, ADR-0026]
standards: [ci-cd-containerization, secrets-management]
approved_by: agent      # all seven gate answers are no
escalation:             # none
---

# The vault, in this stack

## Goal

[[ADR-0029-containerised-dev-and-secret-vault]] made a self-hosted Infisical **required scope** and said where
it runs: *"a self-hosted Infisical running in the same compose stack"*, with *"`docker compose up` brings up
the whole development stack. The host needs Docker and nothing else."*

After this spec `make vault` starts it from this repository's own `docker-compose.yml`. The machine identity
the application will authenticate with is created in that instance — which is why this unit comes first: an
identity cannot be created in a vault that does not exist, and `InfisicalSecretProvider` cannot be written
against an identity nobody has made.

## Out of scope

- **`InfisicalSecretProvider`.** The other half of S1b, and it needs the credentials this unit makes it
  possible to create. Its own spec.
- **Moving any secret into the vault.** Nothing reads from Infisical yet. This unit starts a service and
  documents four variables.
- **Running the vault in CI.** `gate` builds and tests; it has no secret to fetch and no reason to start five
  containers to prove it.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **no** — it implements ADR-0029's "same compose stack" |
| 2 | new or upgraded dependency? | **no package.** Three images, which the ADR named when it chose Infisical |
| 3 | schema change beyond this spec, or destructive migration? | no — the vault owns its own database and this repository has no migration for it |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **no** — it starts a service. No application code reads a secret differently, and no credential is in the repository |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

All seven are no, so Tier A.

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **Behind a `vault` profile**, not in the default stack. | ADR-0029 priced it honestly before it was built: *"the compose stack grows from two services to roughly five, and each is something that can fail during a defence demo"*. `make up` stays two services; `make vault` is the opt-in |
| 2 | **Its own PostgreSQL and Redis**, separate from the application's. | One backup, one migration and one failure should not cover both the secret store and the user data |
| 3 | **Port 8090**, not 8080. | The API holds 8080. A vault that cannot start because the thing it serves is already listening is a bad first impression |
| 4 | **The four secrets default to empty, and `make vault` refuses a blank one** — rather than a `:?` guard in the compose file. | Measured, not assumed: see below |
| 5 | `TELEMETRY_ENABLED: "false"`. | A development machine reports nothing to anybody |

## What the walk caught, by running it

A `:?` guard on each vault secret looked right and was wrong. **Compose interpolates every service's variables
even when the profile is off**, so `docker compose config` failed with
`required variable INFISICAL_AUTH_SECRET is missing a value` — meaning `make up`, which starts two services and
no vault, would have failed for anyone who had not filled in four variables for a vault they were not starting.

Found by running `docker compose config` before the file was committed, not by reading it. The guards moved
into the `vault` target, where they can check a value without being evaluated by every other command:

```
make vault: INFISICAL_ENCRYPTION_KEY is empty in .env — see .env.example.
```

## Files

| Path | Action |
|---|---|
| `docker-compose.yml` | change — three services behind the `vault` profile, and two volumes |
| `Makefile` | change — `vault` and `vault-down`, with the four-variable guard |
| `.env.example` | change — the vault's own four secrets, and the four the provider will need |
| `docs/specs/SPEC-042-vault-compose-profile.md` | create |

## Verification

| Check | Result |
|---|---|
| `docker compose config --services` | `db`, `api` — the default stack is untouched |
| `docker compose --profile vault config --services` | five services, so the profile is wired |
| `make vault` with an unfilled `.env` | refuses, naming the first empty variable, and exits non-zero |
| `make agent-check` | passes: no C# changed |

## Done when

- [x] `make vault` starts the vault and `make up` does not
- [x] a blank secret is refused by name rather than by a compose interpolation error
- [x] `.env.example` documents all eight variables, and `.env` is still the only place a value lives
- [x] `status: done` in this file
