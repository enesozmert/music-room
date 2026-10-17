---
id: SPEC-054
title: The secrets go into the vault by script, not by typing
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.8]
decisions: [ADR-0026, ADR-0029, ADR-0046]
standards: [secrets-management, ci-cd-containerization]
approved_by: enesozmert # 2026-10-17, choosing "automate, keep our own vault" over Infisical Cloud
escalation: |
  **Gate 5 is a yes** — this unit's whole subject is secrets. Gates 1, 2, 3, 4, 6 and 7 are no.

  **Why it exists.** The live vault migration is seven steps, and the owner said the work felt heavy.
  Four of the steps are unavoidable clicking: a Supabase project, a Render Key Value instance, a
  Render service, and the vault's own first admin. The fifth — **typing ten secret values into a web
  form** — is the one that is both the most tedious and the most likely to go wrong, and it is the one
  that does not have to be done by hand. A mistyped `DATABASE_URL` costs a deploy; a mistyped
  `OIDC_SIGNING_KEY`, which is a PEM block pasted as one line with `\n` escapes, costs an afternoon.

  **The alternative the owner rejected**, recorded because it was offered and declined: point the live
  deployment at Infisical Cloud. It would delete three of the seven steps and the boot-order fragility
  with them. It also contradicts ADR-0029's owner requirement — *the delivered system reads its API
  keys from a self-hosted Infisical vault* — and that requirement is the reason the vault exists at
  all. Keeping it and automating around it is the owner's choice, made on 2026-10-17.

  **This script writes secrets, so what it must never do is the design.** It never prints a value, not
  on success, not in an error, not under `set -x` — the shell's trace would carry every one, so tracing
  is refused rather than tolerated. It reads from a local file the repository cannot contain
  (`.env.*` is git-ignored, and `.claude/hooks/write-boundary.sh` blocks writing one). It takes an
  **allowlist** of names rather than everything in the file, so a file that also holds the vault's own
  bootstrap credentials cannot push those into the vault it opens.
---

# The secrets go into the vault by script, not by typing

## Goal

`make vault-seed` reads a local environment file and writes this deployment's secrets into the
Infisical project's environment over the API, idempotently, printing names and outcomes and never a
value. Running it twice is a no-op. Rotating one secret is the same command.

## Out of scope

- **Creating the vault, its project, its environments or its machine identity.** Infisical's first
  admin is an SRP exchange in a browser, and a project plus an identity is three minutes of clicking
  that happens once. Automating a one-time three-minute step is not worth a script that can break.
- **Reading secrets back.** The application does that, through `InfisicalVaultClient` — SPEC-047.
- **Deleting secrets.** A seed that can delete is a seed that can empty a production vault by typo.
- **Infisical Cloud.** See the escalation.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0029 and ADR-0046 are kept, deliberately |
| 2 | new or upgraded dependency? | no — `curl` and `python3`, which `scripts/` already requires |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is a write path for secrets |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **An allowlist of names, written in the script.** | The source file also holds the five `INFISICAL_*` bootstrap values. Pushing those into the vault would put the key to the vault inside the vault, and `POSTGRES_PASSWORD` and friends belong to compose rather than to the deployment |
| 2 | **No value is ever printed, and tracing is refused.** | `set -x` would echo every secret into a terminal buffer and a CI log. The script exits rather than run traced |
| 3 | **Create, then update on conflict.** | Idempotent without a read-modify-write: Infisical answers `POST` with a conflict when the name exists, and `PATCH` is then the right verb. A read first would pull values back out of the vault for no reason |
| 4 | **The file is named, never defaulted to `.env`.** | `.env` is the development stack's, and seeding production from it is how a development key reaches a production host. `FILE=` is required |
| 5 | **It refuses to run against a vault it was not told about.** | All five `INFISICAL_*` values must be present; a missing one is an error, not a default |
| 6 | **An empty value is skipped and reported, not written.** | An empty secret in the vault is worse than an absent one: the host boots and the feature fails later |

## Files

| Path | Action |
|---|---|
| `scripts/vault-seed.sh` | create — the seeder |
| `Makefile` | change — `vault-seed` |
| `docs/deploy/RENDER.md` | change — step 5 becomes one command |
| `.env.example` | change — says what a `.env.prod` is for, without holding one |
| `docs/SDD.md` | change — §14.3 |
| `docs/specs/SPEC-054-vault-seed.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| `make vault-seed FILE=…` against the local vault | each allowlisted name is written; names and outcomes printed, values not |
| running it twice | second run reports every name as updated, and nothing fails |
| a name absent from the file | reported as skipped, not written as empty |
| `INFISICAL_CLIENT_SECRET` missing | refuses before any request |
| `grep` of the output | no secret value, and no `Authorization` header, appears in it |
| `bash -n` | the script is syntactically sound |
| `bash -x scripts/vault-seed.sh`, and an exported `SHELLOPTS=xtrace` | both refused before any request. A parent shell's own `set -x` does not reach a child process, so there is nothing to catch and nothing to leak there either |

## Done when

- [x] `make vault-seed` writes the allowlist into the local vault and is idempotent — `created` on the
      first run, `updated` on the second, against the compose Infisical. The test secret was deleted
      afterwards and reads back `404`
- [x] no value appears in its output, including on failure
- [x] `docs/deploy/RENDER.md` step 5 is one command plus the five Render variables
- [x] `make agent-check` passes
- [x] `status: done` in this file
