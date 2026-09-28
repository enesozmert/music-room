---
kind: adr
id: ADR-0034
status: Accepted
date: 2026-09-28
---

# ADR-0034 — Profile storage: typed columns, a tier per field, and preferences as rows

## Context

Subject V.1 requires "profile fields in three visibility tiers: public, friends-only, private. Plus
music preferences." [[subject-requirements]] repeats that sentence and **names no field**. [[sdd]] §6.2
defined the `ProfileFieldVisibility` enum and §8 defined no table. [[endpoint-inventory]] said
`PATCH /me/profile` takes "any subset of profile fields, each with its `visibility`" and listed none.

So SPEC-011 could not store a profile without deciding what a profile is, and the shape of that answer
is inherited by every later read — in particular by the per-field projection that
`GET /users/{userId}` will need.

## Decision

Owner decisions of 2026-09-28.

### Typed columns, each with its own visibility column

`profiles`: one row per account, with `real_name`, `bio`, `location`, `birth_date` and a `smallint`
visibility column for each. The tier persists as `smallint`, not text: an enum stored as a string is a
value nothing validates, and these four decide who may read a field.

**The tiered field list is those four.** A fifth field is a migration, which this project will write
when it has a reason.

### Default tiers are a decision, not a fallback

`real_name` and `location` default to `Friends`, `bio` to `Public`, `birth_date` to `Private`. A field
whose tier defaulted to public would leak the first time somebody filled it in.

### A value sent without a tier is refused

`422`, never a silent default. This required the tier to be **nullable on the wire model**: with a
non-nullable enum, an omitted `visibility` binds to the enum's zero value, which is `Public` — so the
first implementation stored a public field the caller never marked public, and the test for the rule
caught it.

### `display_name` stays on `users`, always public, untiered

It is the handle an account is known by, and a search result or an attendee list must show it. Offering
a tier that search would have to ignore is worse than not offering one.

### Music preferences are normalised rows, private, untiered

`music_preferences(user_id, kind, value)` with a unique index on the triple, so a duplicate is a write
failure. Queryable, because a preference set nothing can query is one nothing will ever use. No tier:
nothing reads them yet, so exposing them would be a decision with no benefit, and adding a tier later
is additive.

### A profile row exists from registration

Created in the same transaction as the account, and an **idempotent data migration**, separate from the
schema migration, gave one to every account that predates the table. `GET /me` therefore has no
"account with no profile" branch.

## Consequences

- **`Friends` is stored and not yet evaluated.** With no friends graph, every reader who is not the
  owner is a stranger, so a `Friends` field is currently as hidden as a `Private` one — the safe
  direction to be wrong in. [[sdd]] §16 and [[endpoint-inventory]] both say so, and the friends graph is
  the next unit.
- The per-field projection of `GET /users/{userId}` becomes a column-by-column decision rather than a
  `WHERE` clause. That is the cost of typed columns, and it is paid once in one query.
- Each new profile field is a migration. Accepted: the field list is small and stable, and typed columns
  keep validation in FluentValidation where every other rule in this project lives.
- Preferences are storage-only, which keeps issue **O14**'s note true: V.1 requires storing and updating
  them, and nothing consumes them.
- `SchemaConventionTests` holds both tables to the naming rules without being told about them, and it
  corrected one index name during implementation.

## Alternatives rejected

- **A `profile_fields` key/value table with a tier per row.** No migration per field, and the projection
  becomes `WHERE visibility IN (...)`. Rejected: every value becomes a string, validation moves out of
  the validator, and a date stops being a date.
- **A `jsonb` document plus a `jsonb` visibility map.** Flexible and compact, and nothing here needs
  document flexibility; `database-design-migrations` is written around typed columns and indexes.
- **Tiering `display_name`.** Consistent, and it means every list in the product needs a "some user"
  case forever.
- **Preferences in one `jsonb` column.** One row, one write, no join — and unqueryable, which would make
  O14's missing consumer permanent.
- **Creating the profile lazily on first write.** No backfill, and an "empty profile" branch in every
  read path forever.
