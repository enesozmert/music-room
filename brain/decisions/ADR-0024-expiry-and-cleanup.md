---
kind: adr
id: ADR-0024
status: Accepted
date: 2026-09-24
---

# ADR-0024 — Lazy expiry checks, one cleanup service

## Context

Several records expire: email verification tokens (24h), password reset tokens (1h), event codes
(short), cached `TrackRef` entries, refresh tokens. How expiry is enforced and how dead rows are
removed was open (T8).

## Decision

Two separate mechanisms, deliberately not one:

1. **Expiry is checked at the point of use**, inside the handler, every time. A token, code or
   cache entry past its lifetime is rejected regardless of whether anything has cleaned it up.
2. **One `BackgroundService`** runs periodically and deletes rows that are already expired.

## Consequences

- **Security does not depend on the scheduler.** If the background service is stopped, delayed, or
  never deployed, an expired token is still refused, because the handler checks. A design where a
  timer is the only thing standing between a stale token and a valid session is a design with a
  single point of failure in the wrong place.
- Cleanup is therefore **hygiene, not enforcement**: it keeps tables small and keeps the database
  inside the Supabase free tier's storage envelope ([[ADR-0016-supabase-plan-region]]).
- One service, not one per record type. It sweeps each table in turn on a single timer. Simple to
  reason about and simple to disable.
- Deletes are batched and bounded, so a sweep cannot lock a table or spike the connection pool —
  the free tier's connection ceiling is the operative constraint.
- Nothing scheduled is business logic. The moment something time-triggered starts making a domain
  decision, it needs its own ADR.
- No scheduling framework enters the project.

## Alternatives rejected

- **Lazy checks only, no cleanup** — smallest surface, and adequate at this scale. Rejected mostly
  so that "why do expired rows accumulate" has a better answer than "they do".
- **Background service as the primary mechanism** — reverses the safety property: a stopped timer
  would leave expired tokens usable.
- **Hangfire or Quartz** — scheduled-job infrastructure with a dashboard and its own tables, for
  one periodic sweep.

Closes T8.
