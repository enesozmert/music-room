---
kind: adr
id: ADR-0016
status: Accepted
date: 2026-09-24
---

# ADR-0016 — Supabase Free tier, Frankfurt

## Context

[[ADR-0006-datastore]] chose Supabase. Plan tier and region stayed open as issue O5, and subject
V.7 requires them to be declared before the capacity measurement means anything.

## Decision

**Free tier, region `eu-central-1` (Frankfurt).**

## Consequences

- Frankfurt is the lowest-latency Supabase region for a team working from Türkiye. That latency is
  part of every request the API makes and therefore part of the V.7 numbers.
- The free tier imposes a small database quota, a low ceiling on direct connections, and pausing
  after a period of inactivity. **Confirm the current figures on Supabase's pricing page before
  running the load test** — they change, and the report must quote what was true on the day.
- The connection ceiling is the operative constraint, not the storage quota. Keep the EF Core pool
  small and connections short-lived; a long-running SignalR process holding connections open will
  hit the ceiling before the database is anywhere near full. See [[ef-core-persistence]].
- **An inactive project pauses.** A paused project on defence day is a failed demo. Touch the
  project in the days before the defence, and have the local PostgreSQL container as a fallback
  path — the engine is identical, the migrations are the same.
- Migrations run through the **direct** connection string, the API through the pooled one. Both
  are secrets in `.env`.
- Upgrading to Pro later changes a connection string and a line in the load report. No code change.

## Alternatives rejected

- **Free tier, London** — slightly higher latency, no compensating benefit.
- **Pro tier** — no pausing and a higher connection ceiling, at a monthly cost that a school
  project does not need.
- **Local PostgreSQL only** — would supersede [[ADR-0006-datastore]] rather than parameterise it.
  It stays the development and test database via Testcontainers ([[ADR-0012-test-stack]]).

Closes issue O5.
