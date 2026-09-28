---
kind: migrations-index
updated: 2026-09-27
---

# Migrations Index

Tracks schema and data transitions.

| # | Name | Type | Applied | Reversible | Notes |
|---|---|---|---|---|---|
| 1 | `20260924195820_InitialIdentity` | schema, additive | 2026-09-24, local compose PostgreSQL 18.2 | yes — `Down` drops both tables, no data loss path because the tables are new | SPEC-002. Creates `users` and `email_verification_tokens`. Carries `ux_users_email` (the registration race arbiter, SDD §8.2), `ux_email_verification_tokens_token_hash` and `ix_email_verification_tokens_user_id`. Proven to apply to a clean database by `RegistrationEndpointTests`, which calls `Database.MigrateAsync()` against a Testcontainers engine on every run. The EF-generated file was reformatted by hand to pass the analyzers — file-scoped namespace, argument null checks, and `Up` split into three helpers against the 40-line limit. The emitted SQL is unchanged |
| 2 | `20260927183531_RefreshTokens` | schema, additive | not applied yet — generated 2026-09-27, proven to apply to a clean database by `LoginEndpointTests` and `SessionRotationEndpointTests`, which call `Database.MigrateAsync()` against a Testcontainers engine on every run | yes — `Down` drops `refresh_tokens`, and there is no data-loss path because the table is new. Rolling back signs out every session, which is the expected cost of removing the feature that created them | SPEC-005. Creates `refresh_tokens` with `ux_refresh_tokens_token_hash` (the exchange arbiter, SDD §13.1), `ix_refresh_tokens_family_id` (one-statement family revocation) and `ix_refresh_tokens_user_id`. Cascade foreign key to `users`, no navigation property. The EF-generated file was reformatted by hand to pass the analyzers — file-scoped namespace, argument null checks, and `Up` split into two helpers against the 40-line limit. The emitted SQL is unchanged |

## Rules

- One row per EF Core migration once the first one is generated.
- Every destructive migration (drop column, drop table, narrow a type) needs a stated
  rollback path **before** it is written. Add the note to this table, not to a commit message.
- Data backfills are separate from schema changes, and idempotent.
- Never put seed data containing real user records or secrets in a migration.

Standard: `database-design-migrations` in [[standards-router]].
