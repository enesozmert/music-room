---
kind: adr
id: ADR-0046
status: Accepted
date: 2026-10-11
---

# ADR-0046 — The live deployment target is Render, with the vault beside the API

## Context

[[ADR-0015-hosting-and-deployment]] chose local Docker for the defence and **deferred** a remote
target: *"If a live URL is wanted later, the nearest viable targets are Fly.io, Railway, Render or
Azure Container Apps... That is the deferred ADR."* This is that ADR, and it closes issue O11.

Owner requirement, 2026-10-11: the backend goes live, on a **free** tier, and **Infisical goes with
it** — the vault is required scope ([[ADR-0029-containerised-dev-and-secret-vault]]) and a delivered
system that reads its secrets from a file in production would make that decision ornamental.

Three candidates were named by the owner: Render, Koyeb, Azure App Service F1.

## Decision

**Render.** It is the only one of the three that can run the API **and** a self-hosted Infisical for
free, which is the deciding requirement rather than a preference.

| | **Render** | Koyeb | Azure App Service F1 |
|---|---|---|---|
| Free Docker services | several; 750 instance-hours per workspace per month | **one** | one app, **60 CPU-minutes per day** |
| Free Redis — Infisical needs one | Key Value, 25 MB, **no persistence** | none | none; Azure Cache for Redis has no free tier |
| Free PostgreSQL | 1 GB, **expires 30 days** after creation | **5 active hours per month** | none outside a 12-month trial |
| Memory | 512 MB per service | 512 MB, 0.1 vCPU | 1 GB |
| Idle behaviour | sleeps after 15 minutes, ~1 minute cold start | sleeps after 1 hour | no always-on |

Koyeb's single free service cannot hold two containers, so the vault has nowhere to live. Azure F1
has neither a free PostgreSQL nor a free Redis for the vault to use, and 60 CPU-minutes a day is a
hostile budget for a .NET host that boots, migrates and serves a demo.

### The shape that ships

| Element | Where | Why not the obvious alternative |
|---|---|---|
| API | Render web service, `runtime: docker`, free plan, the **same image** `docker compose` builds | a buildpack deploy would be a second build path, and ADR-0015's "the image CI builds is the image that runs" would stop being true |
| Vault | Render web service running `infisical/infisical:latest-postgres`, free plan | Infisical Cloud is a hosted vault and would work unchanged — our provider only takes a base URL — but the owner's requirement is a vault **we** run |
| Vault database | a **second Supabase project** | Render's free PostgreSQL is deleted 30 days after creation, and losing the vault's database loses every secret in it. ADR-0029 also requires the vault's store to be separate from the application's, which two Supabase projects satisfy |
| Vault cache | Render Key Value, free | 25 MB and no persistence. Infisical keeps durable state in PostgreSQL, so a restart costs a cache, not data |
| Application database | the existing Supabase project, pooled connection string | unchanged — [[ADR-0016-supabase-plan-region]] |
| Secrets | `SECRET_PROVIDER=infisical`; the five bootstrap values are Render environment variables | the posture SPEC-047 built. Render's own environment variables hold only what opens the vault |
| TLS | Render's `*.onrender.com` certificate | a custom domain is a purchase and a DNS record this project does not need to demonstrate HTTPS — T-33's transport half closes either way |
| Migrations | a **manually dispatched** GitHub Actions job against `DATABASE_DIRECT_URL` | not at container start: schema ownership belongs to migrations run deliberately (SDD §8.5), and an app that migrates on boot migrates once per instance and once per restart |

## Consequences

### The honest costs, stated before they are paid

- **The free tier sleeps.** After 15 minutes idle a service stops; the next request waits about a
  minute. A defence demo therefore **warms the stack first**, and that is a step in the runbook
  rather than a surprise.
- **Sleeping makes the vault a boot-order problem.** The API reads its secrets once, at start-up,
  and SPEC-047 makes it **refuse to start** when the vault is unreachable — deliberately, because a
  fallback would silently restore reading secrets from a file. On Render both services sleep
  independently, so the API can wake while the vault is still cold. The answer is a bounded retry at
  boot: SPEC-051 gives the vault read a handful of attempts over about 90 seconds, and if the vault
  is still not there the host still refuses. A retry is not a fallback.
- **Supabase pauses a free project after 7 days idle**, both of them. A paused project is restored
  from the dashboard in a click, and the runbook says so.
- **750 instance-hours a month is one service running continuously, not two.** Keeping both awake
  around the clock does not fit, which is why the keep-warm workflow is dispatched rather than
  scheduled.
- **The capacity figure does not move.** Subject V.7 is answered by the declared machine in
  `docs/load/REPORT.md`; a 512 MB shared-CPU container that sleeps is not a measurement platform,
  and the report will not pretend otherwise. This deployment buys a live URL, not a number.

### What it buys

- A URL the client can be pointed at, which is what [[ADR-0020-monorepo-layout]]'s second workstream
  needs and what subject V.5's "configurable backend address" is configured *to*.
- **The vault runs in production**, so ADR-0029 is a property of the delivered system rather than of
  a development convenience.
- HTTPS without a certificate workflow, which closes the transport half of T-33.

## Alternatives rejected

- **Koyeb** — one free service. The vault has nowhere to go, and splitting it across providers would
  mean two dashboards and two failure modes for one requirement.
- **Azure App Service F1** — no free PostgreSQL or Redis for the vault, and 60 CPU-minutes a day.
- **Infisical Cloud free tier** — it would work with no code change at all, since
  `InfisicalSecretProvider` takes a base URL, and it removes the sleep, the 30-day database and the
  Redis caveat. Rejected because the owner's requirement is a vault this project runs; recorded here
  because if the self-hosted vault proves too fragile on a free tier, this is the one-variable
  retreat.
- **A VPS (Hetzner, DigitalOcean)** — a fixed machine, 24/7, and a defensible capacity figure, for a
  monthly cost the owner did not ask to pay.
- **Fly.io / Railway** — comparable, and not among the three the owner named.

## Affected

Standards: `ci-cd-containerization`, `secrets-management`. Design: [[sdd]] §14.1, §14.3.
Plan: [[delivery-plan]]. Related: [[ADR-0015-hosting-and-deployment]] (defers to this),
[[ADR-0029-containerised-dev-and-secret-vault]], [[ADR-0016-supabase-plan-region]].
Closes issue O11.
