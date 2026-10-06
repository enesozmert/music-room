---
kind: adr
id: ADR-0044
status: Accepted
date: 2026-10-06
---

# ADR-0044 — The development database is dumped on demand, into an ignored directory

## Context

Issue **O23**, an owner requirement of 2026-10-04: the local PostgreSQL that `docker compose` runs has
no backup. A `docker compose down -v`, a corrupted volume or a migration taken in the wrong direction
loses every row, and the rows are not nothing — seeded accounts, playlists, events and the audit trail
a defence walkthrough is demonstrated against.

Nothing graded depends on this. The subject does not ask for it. It is here because losing a working
dataset mid-slice costs an afternoon of reseeding, and because the restore path is the half nobody
writes down until they need it.

The issue row names three things this record has to settle, and they are settled below in that order.
A spec cannot settle them: question 1 of its gate has no accepted decision to point at, and question 5
is a yes twice over — the command reads the `.env` credential, and a dump carries real user rows,
password hashes and addresses among them.

## Decision

### On demand, never on a schedule

`make backup` writes one dump. Nothing runs in CI, nothing runs on a timer, and nothing runs in a
container that outlives the command. A scheduled job on a developer's machine is a job nobody watches
and a disk nobody empties; the dataset this protects is recreated by seeding anyway, so the honest
scope is *"before I do something I might regret"*.

### `docker compose exec`, not a host binary

The dump runs as `docker compose exec -T db pg_dump`. `psql` and `pg_dump` are **not installed on the
host** — ADR-0026 keeps the host empty on purpose, and the engine in the container is the one that
wrote the files. A host binary of a different minor version is the classic way to produce a dump that
restores with warnings.

The container's own `POSTGRES_USER` and `POSTGRES_DB` are used from the environment `docker compose`
already supplies it. **The credential is never passed on the command line**, where it would reach the
process table and the shell history.

### `backups/`, git-ignored in the same commit that creates it

The directory is `backups/` at the repository root, and the `.gitignore` entry lands **in the commit
that adds the target**, never in a follow-up. Subject V.8 makes a committed credential an automatic
failure, and a dump contains addresses and BCrypt hashes — the one file in this repository that is
both large and sensitive. One commit, both halves, so there is no window where a `git add -A` can
catch it.

The file name carries a UTC timestamp: `backups/musicroom-YYYYmmddTHHMMSSZ.sql`. Sorting by name
sorts by age, which is what the retention step needs.

### Seven kept, the rest deleted by the same command

`make backup` keeps the **seven** most recent dumps and deletes the older ones. Seven because the thing
being protected is a few days of local work, and because a retention rule nobody enforces is a disk
that fills silently. The deletion is part of the same command rather than a second one, for the same
reason.

### The restore path is written down and tested by hand once

`make restore FILE=backups/<name>.sql` pipes the dump back through `docker compose exec -T db psql`.
The spec that implements this runs it once, against a deliberately emptied database, and records what
it saw. A backup whose restore has never been run is a file, not a backup.

**The restore refuses to run against anything but the compose database.** It reads the same
`DATABASE_URL` the application does and stops if the host is not the local container — a restore
pointed at Supabase would overwrite rows nobody asked it to.

## Consequences

- One more `make` target pair, and `backups/` in `.gitignore`. No application code changes, no schema
  changes, and nothing runs unless a person asks for it.
- The dump is plain SQL rather than a custom-format archive, so it is readable and diffable by the
  person restoring it, at the cost of size. For a development dataset that trade is the right way
  round.
- **The production database is explicitly out of scope.** When `DATABASE_URL` points at Supabase (slice
  S4), backups are Supabase's own — its plan tier decides retention, and that is a deployment decision
  rather than a Makefile one. This record covers the compose container and says so to stop the next
  reader assuming it covers more.
- A dump sitting in `backups/` is unencrypted user data on a laptop. That is accepted for a development
  dataset of seeded accounts, and it is named in the threat register rather than left implicit.

## Related

[[ADR-0026-environment-bring-up]] · [[ADR-0006-datastore]] · [[threat-register]] · [[sdd]]
