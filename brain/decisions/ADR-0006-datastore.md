---
kind: adr
id: ADR-0006
status: Accepted
date: 2026-09-23
---

# ADR-0006 — Supabase (managed PostgreSQL) as the datastore

## Context

EF Core is the data-access layer ([[ADR-0001-backend-dotnet-narchitecture]]). The model is
relational and leans on real database guarantees — a unique index for the one-vote-per-user rule
and concurrency tokens for playlist reorder and control takeover ([[realtime-architecture]]).
No database client is installed on the dev machine, and the team wants to spend its time on
domain logic rather than on operating a database.

## Decision

Use **Supabase** as the datastore. Supabase is managed PostgreSQL, so the engine is Postgres and
the EF Core provider is `Npgsql.EntityFrameworkCore.PostgreSQL`.

Scope of what we adopt:

| Supabase feature | Used? | Reason |
|---|---|---|
| PostgreSQL database | **yes** | the datastore |
| Connection pooler (Supavisor) | **yes** | pooled connection string for the API |
| Auth (GoTrue) | **no** | identity is ours — [[auth-and-identity]], [[ADR-0005-api-style-rest-json]] |
| Realtime | **no** | realtime is SignalR — [[ADR-0004-realtime-signalr]] |
| Storage | **no** | no media is stored; only `TrackRef` metadata — [[ADR-0003-music-provider-youtube]] |
| Auto-generated REST / PostgREST | **no** | the API is ours, and it must not be bypassed |
| Row Level Security | see below | defence in depth, not the primary gate |

## Consequences

- **The database is reached only through our backend.** No client — mobile or web — holds a
  Supabase anon key or talks to Supabase directly. The subject requires the backend to be the
  single guardian of truth (V.3) and authorisation to be enforced server-side (V.6). A direct
  client connection would route around both.
- Schema is owned by **EF Core migrations**, not by the Supabase dashboard. Editing the schema in
  the dashboard breaks migration history. See the `database-design-migrations` standard.
- Postgres gives us what the concurrency requirements need: partial and unique indexes,
  `xmin` as a concurrency token, `SERIALIZABLE` and `REPEATABLE READ` when a handler needs them.
- Two connection strings exist: the pooled one for the API and the direct one for migrations.
  Migrations must not run through the transaction pooler.
- Both are secrets and live in `.env` (`SUPABASE_DB_URL`, `SUPABASE_DB_DIRECT_URL`).
  The service-role key, if ever issued, is server-side only and never reaches a client bundle.
- The database is **off-premise**. The load report must say so and state the plan tier, because
  subject V.7 requires the server specification to be declared. Network latency to Supabase is
  part of the measured numbers, not an excuse for them.
- RLS is enabled with deny-by-default policies as a second line of defence. The application still
  performs every ownership and licence check itself. RLS is never the only gate.
- Local development may run plain Postgres in Docker for tests; the engine is identical, so
  Testcontainers-backed tests stay valid (`testing` standard).
- Vendor coupling is limited to a connection string, because we use only the Postgres surface.
  Moving to self-hosted Postgres would be a configuration change.

## Alternatives rejected

- **Self-hosted PostgreSQL in Docker only** — no hosting cost and full control, but the team then
  operates backups and availability during a time-boxed school project.
- **SQL Server** — the pattern in `reference/nArchitecture`, but heavier locally and no managed
  free tier for the team.
- **Firebase / Firestore** — document model fights the unique-index and concurrency-token
  requirements that this subject grades directly.

## Follow-ups

- Record the chosen plan tier and region in `docs/load/REPORT.md` before the load run.
- Write the RLS baseline policies alongside the first migration.

Standards: `ef-core-persistence`, `database-design-migrations`, `secrets-management`.
