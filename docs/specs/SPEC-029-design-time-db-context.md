---
id: SPEC-029
title: Take the schema tool off the web host's start-up guards
status: done           # draft | approved | in-progress | done | blocked
tier: A                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S0              # which DELIVERY-PLAN slice this belongs to
requirements: []       # tooling: no subject requirement changes status here
decisions: [ADR-0026]
standards: [database-design-migrations, testing]
approved_by: agent     # Tier A, clean gate — owner approved the fix on 2026-10-04
escalation:            # none: every gate question answers no
---

# Take the schema tool off the web host's start-up guards

## Goal

After this `make migrate` and `make migration` depend on `DATABASE_URL` and on nothing else. Today they
depend on every start-up guard the web host has, because `dotnet ef` falls back to the start-up project when
no design-time factory exists. A `.env` with `MAIL_PROVIDER=brevo` and an empty `MAIL_FROM` stops a migration
with two messages, neither of which is about the schema:

```
Error: MAIL_PROVIDER is brevo but MAIL_FROM is not set. Use a verified sender or Brevo's sandbox sender — see ADR-0013.
Unable to create a 'DbContext' of type 'MusicRoomDbContext'.
```

The guard is right and stays: SPEC-024 closed threat T-26 with it, and a host that sends no mail must not
boot. What is wrong is that migrating a table needs a mail sender.

## Out of scope

- **Weakening any start-up guard.** None changes. The web host still refuses to boot without a valid
  `MAIL_PROVIDER`, the JWT settings or `DATABASE_URL`.
- **A second connection string for migrations.** SDD §8.5 already says migrations use the direct connection;
  which URL `DATABASE_URL` holds is a deployment decision, not this unit's.
- **Reading `.env` from C#.** The `Makefile` exports it before the call (`set -a; . ./.env`), which is where
  that responsibility already lives — ADR-0026.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **no** — ADR-0026 says `DATABASE_URL` is read with no fallback, and the factory reads it with no fallback |
| 2 | new or upgraded dependency? | **no** — `Microsoft.EntityFrameworkCore.Design` is already referenced by the persistence project, `PrivateAssets="all"` |
| 3 | schema change beyond this spec, or destructive migration? | **no** — no migration is added, and the model is untouched |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **no** — it reads one connection string and prints none of it |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Every answer is no, so Tier A.

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDesignTimeDbContextFactory.cs` | create |
| `MusicRoom/tests/MusicRoom.Persistence.Tests/DesignTimeDbContextFactoryTests.cs` | create |
| `docs/SDD.md` | change — §8.5 gains the rule that the schema tool does not start the application |
| `docs/specs/SPEC-029-design-time-db-context.md` | create |

## Steps

1. Add the factory. It reads `DATABASE_URL` from the environment, configures Npgsql, and returns the context.
2. Refuse an empty value with a message that names the key and ADR-0026 — the same shape the host's refusal
   has, for the same reason: a connection string nobody intended is worse than a stop.
3. Cover both paths with persistence-layer tests that build the model and never open a connection.
4. Record the rule in SDD §8.5, next to the migration rules it belongs with.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `DesignTimeDbContextFactoryTests.CreateDbContext_WithTheConnectionString_BuildsTheModel` | the model is readable with nothing configured but `DATABASE_URL`, and the provider is PostgreSQL |
| `DesignTimeDbContextFactoryTests.CreateDbContext_WithoutTheConnectionString_RefusesAndNamesTheKey` | ADR-0026's no-fallback rule holds on the design-time path too |
| `dotnet ef migrations list --no-connect` with `MAIL_PROVIDER=brevo` and an empty `MAIL_FROM` | the symptom is gone: it lists the migrations instead of failing on the mail guard |
| `make agent-check` | the model the factory builds is the same one the convention tests hold to the standard |

## Done when

- [x] every file in the Files table exists and compiles
- [x] every command under Verification passes
- [x] `make migrate` no longer reads the mail, JWT or vault configuration — the factory reads one key
- [x] no start-up guard changed
- [x] `status: done` in this file
