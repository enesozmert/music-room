---
kind: migrations-index
updated: 2026-09-23
---

# Migrations Index

Tracks schema and data transitions. Empty by design — no database exists yet
([[ADR-0006-datastore]] is Open).

| # | Name | Type | Applied | Reversible | Notes |
|---|---|---|---|---|---|
| — | — | — | — | — | no migrations yet |

## Rules

- One row per EF Core migration once the first one is generated.
- Every destructive migration (drop column, drop table, narrow a type) needs a stated
  rollback path **before** it is written. Add the note to this table, not to a commit message.
- Data backfills are separate from schema changes, and idempotent.
- Never put seed data containing real user records or secrets in a migration.

Standard: `database-design-migrations` in [[standards-router]].
