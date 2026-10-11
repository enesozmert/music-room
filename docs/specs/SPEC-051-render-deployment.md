---
id: SPEC-051
title: The backend goes live on Render, with the vault beside it
status: approved        # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1b              # which DELIVERY-PLAN slice this belongs to
requirements: [V.7, V.8]
decisions: [ADR-0046, ADR-0015, ADR-0029, ADR-0016]
standards: [ci-cd-containerization, secrets-management, testing]
approved_by: enesozmert # 2026-10-11: "live'a geçireceğiz backend'i doğru şekilde", Render chosen from three
escalation: |
  **Gates 1 and 5 are yes.** A new ADR chooses the deployment target (ADR-0046), and the unit moves every
  production secret into a platform's hands. Gates 2, 3, 4, 6 and 7 are no.

  **The owner named three free tiers and asked for whichever is sensible, with Infisical on it too.** The
  comparison is in ADR-0046 and the answer is Render, on one fact: it is the only one of the three that can
  run a second Docker service for free, and the vault is a second Docker service. Koyeb's free tier is one
  service; Azure App Service F1 has neither a free PostgreSQL nor a free Redis for the vault to use, and 60
  CPU-minutes a day on top.

  **What the owner should know before the URL exists**, because none of it is reversible by a code change:

  1. **The free tier sleeps**, so a demo begins by warming the stack. Written into the runbook as a step
     rather than discovered during a defence.
  2. **The vault becomes a boot-order dependency.** SPEC-047 makes the API refuse to start when the vault
     is unreachable, which is correct and becomes visible here: two services that sleep independently can
     wake in the wrong order. This unit adds a bounded retry — about ninety seconds — and then **still
     refuses**. A retry is not a fallback, and the distinction is the whole value of SPEC-047.
  3. **This deployment is not a capacity measurement.** Subject V.7 is answered by the declared machine in
     `docs/load/REPORT.md`. A 512 MB shared-CPU container that sleeps is a live URL, not a number, and the
     report will keep saying so.
---

# The backend goes live on Render

## Goal

`https://music-room-api.onrender.com` answers, over HTTPS, against the Supabase database, with the vault
running beside it and the application reading its secrets from the vault. The stack is described in
`render.yaml` so it can be reviewed and rebuilt, and the sequence is written down so it can be followed.

## Out of scope

- **A custom domain.** Render's certificate answers the TLS half of T-33; a domain is a purchase and a DNS
  record that demonstrate nothing further.
- **Moving the capacity measurement.** V.7 stays on the declared machine.
- **A deploy from CI.** Render builds on push to `main` by itself; a workflow that also deployed would be a
  second path to production. Migrations are the exception, and they are **manual** by design.
- **The client.** Next, and this is what it will be pointed at.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0046, which ADR-0015 deferred to |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The Docker build context is the repository root**, and the Dockerfile's `COPY` paths say so. | Found by deploying: Render resolves the Dockerfile path from the repo root and the context from a separate setting, and the first deploy failed with `"/src": not found`. Two settings that must agree are one too many — the context is now a property of the file, identical in compose, CI and Render |
| 2 | **Both databases are Supabase projects.** | Render's free PostgreSQL is **deleted 30 days** after creation, and the one that would vanish is the vault's — every secret with it. ADR-0029 also wants the vault's store separate from the application's, which two projects give |
| 3 | **The API's connection string is the transaction pooler**, with `No Reset On Close=true`. | Npgsql returns a connection by sending `DISCARD ALL`, which PgBouncer in transaction mode refuses; without the flag the failure appears later as unrelated errors on random requests |
| 4 | **Migrations run from a dispatched workflow against the session pooler.** | Transaction-mode pooling cannot carry a migration, and Supabase's direct host is IPv6-only while a GitHub runner is not. Manual because schema ownership belongs to migrations run deliberately — SDD §8.5 |
| 5 | **The health check is `/health`, not `/health/ready`.** | Readiness asks the database. A paused free Supabase project would turn a healthy deploy into a failed one, which is the platform asking the wrong question |
| 6 | **Start-up retries the vault for ~90 seconds and then still refuses.** | Two services that sleep can wake in the wrong order. A retry waits for a vault that is coming; a fallback would read secrets from somewhere else and say nothing, which is what SPEC-047 exists to prevent |
| 7 | **`SECRET_PROVIDER=env` first, `infisical` after the vault holds this deployment's secrets.** | The vault cannot be the source of the credentials that open it, and a deployment that must be perfect before it can start is a deployment nobody can debug |

## Files

| Path | Action |
|---|---|
| `brain/decisions/ADR-0046-live-deployment-render.md` | create — the target, the comparison and the costs |
| `brain/decisions/decisions-index.md` | change |
| `render.yaml` | create — the stack as code, no secret in it |
| `docs/deploy/RENDER.md` | create — the runbook: order, values, traps, warm-up |
| `.github/workflows/migrate-prod.yml` | create — dispatched, with a typed confirmation |
| `MusicRoom/Dockerfile` | change — the context is the repository root |
| `.dockerignore` | move and extend — Docker reads it from the context |
| `docker-compose.yml` | change — `context: .` |
| `.github/workflows/backend-prod.yml` | change — `docker build -f MusicRoom/Dockerfile .` |
| `MusicRoom/src/MusicRoom/Infrastructure/Configuration/SecretProviderFactory.cs` | change — the bounded retry |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Configuration/SecretProviderFactoryTests.cs` | change — it retries, and it still refuses |
| `docs/SDD.md` | change — §14.1 topology, §14.3, §16, O11 closed |
| `docs/specs/SPEC-051-render-deployment.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| `docker build -f MusicRoom/Dockerfile .` | the image builds from the repository root — the fix for the deploy that failed |
| `make up` and `make e2e` | the same context works locally, so the change did not buy the platform at the project's expense |
| a cold vault at start-up | the API retries rather than crash-looping, and refuses if the vault never answers |
| `make agent-check` | nothing else moved |
| the live `/health`, `/health/ready` and `/.well-known/openid-configuration` | the deployment serves, reaches its database, and is the provider it says it is |

## Done when

- [ ] `render.yaml` describes the stack and holds no secret
- [ ] the runbook's order has been followed once, by the owner, and the API answers over HTTPS
- [ ] the API runs with `SECRET_PROVIDER=infisical`
- [ ] `make agent-check` and `make e2e` pass
- [ ] O11 is closed and §14.1 names the live topology
- [ ] `status: done` in this file
