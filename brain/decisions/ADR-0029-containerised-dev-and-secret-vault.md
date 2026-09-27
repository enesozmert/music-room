---
kind: adr
id: ADR-0029
status: Accepted
date: 2026-09-24
---

# ADR-0029 — Fully containerised development, and a secret vault for API keys

> **Amended 2026-09-27.** The vault is **required scope**, not an optional extra. The original
> text left it sounding droppable — "beyond subject V.8", "opt-in", "must not delay S1 to S9".
> Owner requirement: the delivered system reads its API keys from a self-hosted Infisical vault.
> Subject V.8 sets the floor (`.env` git-ignored, no committed credential); this project goes
> past that floor deliberately, and the vault ships. It stays at slice S1b in
> [[delivery-plan]] — the sequencing is unchanged, the commitment is not.

## Context

Owner requirement, 2026-09-24:

1. The backend must be **properly containerised for development**. A contributor needs Docker and
   nothing else on the host.
2. **PostgreSQL runs in Docker** during development, and configuration must already support
   pointing at the real hosted database later without a code change.
3. **API keys must not sit in plain files.** A secret vault holds them, and the vault itself runs
   in Docker alongside the backend. Infisical was named as the candidate.

Points 1 and 2 are largely already true: [[ADR-0026-environment-bring-up]] put the development
database in `docker compose` and made configuration vendor-neutral (`DATABASE_URL`), and
[[ADR-0015-hosting-and-deployment]] made local Docker the runtime for the defence. This ADR makes
them explicit requirements rather than incidental choices, and adds the vault.

## Decision

### Containerisation is a requirement, not a convenience

- `docker compose up` brings up the whole development stack. The host needs Docker and nothing else.
- No step in the documented path requires a locally installed .NET SDK, PostgreSQL client, or CLI
  tool that is not restored by the repository — `dotnet-ef` is already pinned through
  `.config/dotnet-tools.json` for exactly this reason.
- `make up` from a clean clone remains the single graded path (subject IV.1).

### Configuration is provider-agnostic, already

`DATABASE_URL` and `DATABASE_DIRECT_URL` name a role, not a vendor
([[ADR-0026-environment-bring-up]]). Moving from the local container to Supabase at S4 is a
connection-string change and nothing else. This ADR adds one rule: **the application must never
read a provider-specific variable name**, so nothing in code may reference `POSTGRES_*` or a name
containing a vendor.

### Secrets come from a vault, behind an abstraction

Configuration is read through a **secret provider abstraction** with two implementations:

| Provider | When |
|---|---|
| `EnvSecretProvider` | the default. Reads the process environment, which `docker compose` fills from `.env` |
| `InfisicalSecretProvider` | enabled by configuration. Reads from a self-hosted Infisical running in the same compose stack |

The application does not know which one it has. Switching is a configuration change.

The vault lands as **its own spec at the end of S1**, alongside the mail provider adapter (O17).
The abstraction lands earlier, because writing it later means touching every configuration read.

## Consequences

### The honest cost, stated before it is paid

- **A vault does not remove the local secret file; it shrinks it.** Infisical needs its own
  credentials — a machine identity or service token — and those have to come from somewhere on
  first contact. `.env` survives, holding the vault's bootstrap secrets instead of every API key.
  Anyone who expects `.env` to disappear will be disappointed, and it is better to say so now.
- **Self-hosted Infisical is not one container.** It needs its own PostgreSQL, Redis, and an
  encryption key. The compose stack grows from two services to roughly five, and each is something
  that can fail during a defence demo.
- **Subject V.8 asks only that credentials live in a git-ignored `.env`.** The vault is beyond the
  requirement. Subject VI is explicit that extra work is only credited when the mandatory part is
  flawless, so the vault must not delay S1 through S9. That is why it is scheduled at the end of
  S1 and behind an abstraction that costs nothing until then.
- The `.env` that remains is still git-ignored, still placeholder-documented in `.env.example`, and
  still the thing that fails the project if committed. Nothing about V.8 gets easier.

### What it buys

- API keys stop being readable in a file on a laptop, which is the actual risk during a group
  project where the repository is shared and machines are not.
- Rotation becomes a vault operation instead of an edit-and-redistribute.
- The abstraction is small and useful on its own: it gives one place to validate that a required
  secret exists, instead of a null check at each use.

### Operational

- Infisical's own bootstrap secrets are the only values in `.env` once the vault is in place, and
  they are documented in `.env.example` like everything else.
- The compose file gains a `vault` profile. `docker compose up` without it stays two services, so
  an everyday loop and a Testcontainers run do not pay for five. The profile is a performance
  choice for local work, **not** a statement that the vault is optional: the delivered system runs
  with it, and the defence walkthrough brings it up.
- CI does not use the vault. It uses repository secrets, because a pipeline that needs a running
  vault to build is a pipeline that breaks for the wrong reasons.

## Alternatives rejected

- **Infisical Cloud free tier** — no extra containers and less to break, but it puts a network
  dependency and an account in the startup path, and the owner asked for the vault to run in Docker.
- **Docker secrets or .NET user-secrets** — much lighter, and adequate for a school project. They
  do not give rotation or shared access, which is the point of the request.
- **HashiCorp Vault** — more capable and considerably heavier to operate than this project warrants.
- **Do nothing; keep `.env`** — satisfies subject V.8 exactly. Rejected by the owner, and the
  rejection is a requirement rather than a preference: an API key readable in a plain file is the
  thing this decision exists to remove.

## Affected

Standards: `ci-cd-containerization`, `secrets-management`.
Design: [[sdd]] §14. Plan: [[delivery-plan]] S1b. Related: [[ADR-0026-environment-bring-up]].
