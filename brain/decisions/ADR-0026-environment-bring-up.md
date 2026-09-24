---
kind: adr
id: ADR-0026
status: Accepted
date: 2026-09-24
---

# ADR-0026 — Environment bring-up sequence

## Context

[[ADR-0006-datastore]] and [[ADR-0016-supabase-plan-region]] chose Supabase, but no Supabase
project exists, and no external account exists for YouTube, Google, Facebook or mail. Starting
work meant either creating every account first or hitting a wall mid-slice.

## Decision

### Database

| Slices | Database |
|---|---|
| S0 to S3 | **local PostgreSQL container** from `docker compose` |
| S4 onward | **Supabase** Free tier, Frankfurt |

The engine is identical, so migrations, constraints and Testcontainers tests carry over unchanged.
Only the connection string differs.

### Configuration naming

The application reads **vendor-neutral** variables:

| Variable | Local | Supabase |
|---|---|---|
| `DATABASE_URL` | the compose container | the pooled (Supavisor) string |
| `DATABASE_DIRECT_URL` | the same container | the direct string, for `dotnet ef` |

An application coupled to a variable named after a vendor is coupled to the vendor for no reason.

### External credentials — just in time

Created immediately before the slice that needs them, never in advance:

| Credential | Needed by |
|---|---|
| Mail provider API key | S1 — email verification |
| YouTube Data API key + Supabase project | S4 — catalog |
| Google / Facebook OAuth apps | S8 — social login |

## Consequences

- **Work starts today.** No account creation blocks S0.
- An unattended run that reaches a slice needing an absent credential **stops correctly**: the
  spec gate asks whether the change touches secrets, the answer is yes, and it escalates. That is
  the gate working, not a failure.
- Fewer keys exist earlier, so there is less to leak while the project is still being shaped.
- Cost: the Frankfurt latency and the free-tier connection ceiling are not felt until S4. Both are
  recorded in [[ADR-0016-supabase-plan-region]] so they are not a surprise, and the V.7
  measurement happens on Supabase regardless.
- The local container stays useful after S4 — it is what Testcontainers uses for every
  persistence test ([[ADR-0012-test-stack]]).
- `.env.example` is updated to the neutral names plus the mail provider variables.

## Alternatives rejected

- **Supabase from day one** — real latency and connection limits visible immediately, at the price
  of blocking S0 on account creation, and a free project pauses when unused.
- **Both, via compose profiles** — flexible, and two paths to keep working.
- **Create every credential up front** — the smoothest unattended run, and the largest set of
  unused secrets sitting in `.env` while the design is still moving.
