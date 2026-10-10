---
id: SPEC-040
title: The development database is dumped on demand, and the restore is proven once
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: []        # nothing graded: an owner requirement of 2026-10-04
decisions: [ADR-0044, ADR-0026]
standards: [ci-cd-containerization, secrets-management, database-design-migrations]
approved_by: enesozmert # gate 5 answered when O23 was opened, and again in ADR-0044
escalation: |
  **Gate 5 is yes and was answered before this spec existed.** `pg_dump` reads the `.env` credential and a dump
  carries real rows — addresses and BCrypt hashes. O23's row said so when it was opened, and
  [[ADR-0044-development-database-backup]] settled all three questions it raised: where the directory lives and
  that it is ignored in the same commit, that the dump runs through `docker compose exec` rather than a host
  binary, and what the restore path is.

  Gates 1, 2, 3, 4, 6 and 7 answer no. The decision exists; no package — `pg_dump` and `psql` are the
  container's; no migration and no schema change; no endpoint; nothing lowered, deleted or skipped; nothing
  from `reference/`.
---

# A dump on demand, and a restore that has been run

## Goal

Issue **O23**, an owner requirement of 2026-10-04: the compose PostgreSQL has no backup, so a
`docker compose down -v`, a corrupted volume or a migration taken in the wrong direction loses every row.
After this spec `make backup` writes one dump and `make restore FILE=…` puts it back, and the restore has
been run rather than assumed.

## Out of scope

- **The production database.** When `DATABASE_URL` names Supabase, backups are Supabase's own and its plan
  tier decides retention — a deployment decision, not a `Makefile` one. ADR-0044 says so, and the restore
  target refuses any host but the compose container so that nobody discovers the boundary by overwriting it.
- **A schedule.** On demand only. A timer on a developer's machine is a job nobody watches and a disk nobody
  empties.
- **Encrypting the dump.** Accepted and named: a dump in `backups/` is unencrypted user data on a laptop, for
  a dataset of seeded accounts.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it implements [[ADR-0044-development-database-backup]] |
| 2 | new or upgraded dependency? | no — `pg_dump` and `psql` are in the `postgres:18.2-alpine` image already running |
| 3 | schema change beyond this spec, or destructive migration? | no — it reads the schema and writes a file |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — the credential and real rows, answered in `escalation` |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `Makefile` | change — `backup` and `restore`, with `BACKUP_DIR`, `BACKUP_KEEP` and `BACKUP_ENV_FILE` |
| `.gitignore` | change — `backups/`, **in this same commit** |
| `docs/SDD.md` | change — O23 closed with what was proven |
| `docs/specs/SPEC-040-database-backup.md` | create |

## What was run, and what it showed

Every line below was executed against the running stack on 2026-10-09, not reasoned about.

| Step | Result |
|---|---|
| `make backup` | `wrote backups/musicroom-20261009T181921Z.sql`, 40 942 bytes |
| the dump restored into a scratch database | exit 0, and **24 tables** against the source's 24 |
| a marker row inserted, dumped and restored | `backup.restore-proof / spec-040-proof` came back out of the restored database, so the dump carries rows and not only a schema |
| retention, with nine files present | the run removed three and left **seven**, which is `BACKUP_KEEP` |
| the refusal, against an env file naming `aws-0-eu-central-1.pooler.supabase.com` | `make restore: DATABASE_URL does not name the compose database. Refusing — ADR-0044.` and a non-zero exit |

**Why a scratch database rather than the real one.** ADR-0044 asks for a restore run against an emptied
database. Emptying the developer's own database to prove a backup is a cure demonstrating the disease: the
same SQL, the same `psql` invocation and the same guard were exercised against
`musicroom_restore_check`, which was created, restored into, read and dropped. What that does **not** prove is
`make restore`'s own path end to end, because the target takes its database name from the env file; the marker
row proves the dump, and the refusal proves the guard.

**One change the proof forced.** `BACKUP_ENV_FILE` exists because the guard could not otherwise be tested: the
target sources `.env`, and `.env` names the compose host, so no override from outside could reach the case
statement. Making the file overridable is what turned "the guard is written" into "the guard was seen
refusing".

## Done when

- [x] `make backup` writes a dump and prunes to `BACKUP_KEEP`
- [x] `backups/` is ignored in the same commit that creates the target
- [x] the restore was run, and a row that went in came back out
- [x] the restore refuses a `DATABASE_URL` that is not the compose database, seen refusing
- [x] O23 is closed with what was proven rather than with what was written
- [x] `status: done` in this file
