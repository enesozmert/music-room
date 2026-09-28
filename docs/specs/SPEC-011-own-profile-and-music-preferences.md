---
id: SPEC-011
title: Own profile and music preferences — per-field visibility stored, read and updated by its owner
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S3              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1]
decisions: [ADR-0006, ADR-0011, ADR-0012, ADR-0028]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # repository owner, 2026-09-28, with the four decisions below
escalation: |
  Gates 1, 3 and 5 answer "yes". Four decisions belong to the repository owner before this spec may be
  implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) **which profile fields exist** — nothing in the subject distillation, the SDD or the
  contract names them, and the answer decides whether they are columns or rows; (2) whether
  `display_name`, which already lives on `users` and is what a search result shows, is a tiered field at
  all; (3) how music preferences are stored — normalised rows or one JSON column — and whether they carry
  a tier of their own; (4) whether every account gets a profile row at registration, which means
  backfilling the accounts that already exist, or lazily on first write.
---

# Own profile and music preferences

## Goal

After this spec an authenticated user can read their own profile, change any subset of its fields, and set
their music preferences — and **every field carries one of `Public`, `Friends` or `Private`**, stored
server-side, set in the same call as the value it governs. `GET /api/v1/me` returns the full own profile,
`PATCH /api/v1/me/profile` updates fields with their tiers, and `PUT /api/v1/me/music-preferences` replaces
the preference set. Subject V.1 requires three visibility tiers and music preferences; this is where the
tiers stop being an enum in a document and become a column.

It is the first unit of slice S3 and the first aggregate in this codebase that is neither a credential nor
an audit row.

## Out of scope

- **Reading somebody else's profile.** `GET /users/{userId}` and `GET /users` need the per-field projection
  *and* a relationship to project against. They are their own spec, after the friends graph.
- **The friends graph.** `Friends` is a tier this spec can store and cannot yet evaluate: with no graph,
  every non-self reader is a stranger. That is stated here and in `docs/SDD.md` §16 rather than implied.
- **Avatar upload.** A binary surface with its own storage, size and content-type decisions; the contract
  lists no route for it.
- **Music preferences having a consumer.** Issue O14 records that V.1 requires storing and updating them
  and nothing reads them yet. This spec closes the storing half and leaves O14's note true.
- **Profile completeness rules.** No field is mandatory. A user who fills nothing in is a user with an empty
  profile, not an invalid one.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — escalations 1 and 3 decide a storage shape that every later read of a profile inherits, and `docs/SDD.md` §8 names no profile table. **Seven ADR lines are already owed** (`brain/projects/musicroom-backend.md` "Open items"); this spec writes none of them |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **yes, in shape** — one or two new tables, defined here and additive, plus a **backfill** if escalation 4 says every existing account gets a profile row. A backfill is data, not schema, and `database-design-migrations` requires it to be separate from the schema change and idempotent |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — all three rows exist with their bodies and status codes. The field list this spec settles is documentation the contract currently leaves open, so it is added as a design note |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — three `self` endpoints, and the first place where a field's visibility decides who may see it. A mistake here leaks a private field to a stranger, which is subject V.6's own example |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — decided by the owner, 2026-09-28

| # | Decision |
|---|---|
| 1 | **Option A** — a `profiles` table with typed columns, each paired with its own visibility column. The field list is the one proposed: `realName`, `bio`, `location`, `birthDate`, plus `displayName` which stays on `users`. |
| 2 | **`displayName` is always public and not a tiered field.** It is the handle you are known by; the tiered fields are the ones carrying personal detail. Said out loud in the contract's design notes. |
| 3 | **Normalised rows, always private.** `music_preferences(user_id, kind, value)` with a unique index, and no visibility tier — nothing reads them yet, so exposing them would be a decision with no benefit. Adding a tier later is additive. |
| 4 | **A profile row at registration, plus an idempotent backfill** for the accounts that already exist. One branch removed from every future read. |

## Escalation — what a human must decide

### 1. Which profile fields exist, and therefore columns or rows (gates 1 and 3)

`brain/constraints/subject-requirements.md` says "Profile fields in **three visibility tiers**: public,
friends-only, private. Plus music preferences." **It names no field.** `docs/SDD.md` §6.2 defines
`ProfileFieldVisibility` and §8 defines no table. `docs/api/ENDPOINTS.md` says `PATCH /me/profile` takes
"any subset of profile fields, each with its `visibility`" and lists none.

So the field list is a decision, and it decides the storage:

| Option | Shape | Cost |
|---|---|---|
| A | a fixed set of columns on a `profiles` table, each with a `*_visibility` companion column | typed, validated per field, and a query can project exactly what a reader may see. Every new field is a migration. Two columns per field makes a wide table |
| B | one row per field in `profile_fields` — `user_id`, `field`, `value`, `visibility` | no migration per field, and the per-field projection of the next spec becomes a `WHERE visibility IN (...)`. Against it: every value is a string, validation moves into the application, and a typed field like a birthday stops being typed |
| C | a `jsonb` document plus a `jsonb` visibility map | flexible and compact. Against it: nothing in this project needs document flexibility, and `database-design-migrations` is written around typed columns and indexes |

A field list to confirm or replace, kept deliberately small — every one of these is a field a user might
reasonably want hidden from strangers, which is the whole point of the requirement:

| Field | Type | Note |
|---|---|---|
| `displayName` | string, 1-64 | already on `users`; see escalation 2 |
| `realName` | string, 0-128 | the obvious "friends only" field |
| `bio` | string, 0-512 | free text |
| `location` | string, 0-128 | free text, not coordinates — the geofence in ADR-0014 is a different thing entirely |
| `birthDate` | date | the obvious "private" field, and the one that argues for typed columns |
| `musicPreferences` | see escalation 3 | V.1 names it separately from the profile |

**Recommendation, not a decision:** A with that list. Five fields is a migration this project will write
once, and typed columns keep validation where FluentValidation already lives.

### 2. Is `displayName` a tiered field (gates 4 and 5)

It already exists on `users`, registration requires it, and it is what a search result and an event
attendee list must show. If a user can set it to `Private`, then either search results hide it — a list of
anonymous rows — or the tier is quietly ignored, which is worse than not offering it.

- **Always public, and not a tiered field.** Honest and simple: a display name is the handle you are known
  by. The tiered fields are the ones that carry personal detail.
- **Tiered like the rest**, with search and attendee lists falling back to a placeholder for anyone who
  hid it. Consistent, and it means every list in the product needs a "some user" case.

**Recommendation, not a decision:** always public, said out loud in the contract's design notes so a client
developer never builds a field editor for it.

### 3. How music preferences are stored, and whether they have a tier (gates 1 and 3)

The contract says `PUT /me/music-preferences` takes `genres[]` and `artists[]`, and V.1 lists preferences
separately from profile fields.

- **Normalised rows** — `music_preferences(user_id, kind, value)` with a unique index on the triple. Queryable,
  which matters if anything ever recommends on them; one write path that deletes and reinserts, or diffs.
- **One `jsonb` column on the profile.** One row, one write, no join. Against it: a preference set nothing can
  query is a preference set nothing will ever use, and O14 already warns that these have no consumer.

And separately: **do preferences carry a visibility tier?** The subject sentence puts them outside the
tiered fields. If they are always private, `GET /users/{userId}` never returns them and the next spec is
simpler. If they are tiered, they are a sixth field.

**Recommendation, not a decision:** normalised rows, and **always private** for now — nothing reads them,
so exposing them is a decision with no benefit yet, and adding a tier later is additive.

### 4. When a profile row appears (gate 3)

Nine accounts' worth of rows exist in every developer's database already, and registration writes no
profile.

- **At registration, plus a backfill** for accounts that predate this spec. `GET /me` always finds a row, so
  no read path has a null case. Cost: registration writes a second table — inside the same transaction the
  audit behaviour already opens — and the migration ships a separate, idempotent backfill.
- **Lazily, on first write.** No backfill, no change to registration. Cost: `GET /me` must answer for an
  account with no row, so every profile read has an "empty profile" branch forever.

**Recommendation, not a decision:** at registration with a backfill. One branch removed from every future
read is worth one idempotent `INSERT ... SELECT` written once.

## Files

### Discovered during implementation, 2026-09-28

Four deltas. Two are analyzer- or framework-forced; one is a **real bug this spec's own test caught**; one is a
naming correction the existing schema tests demanded. **The owner accepts or rejects this delta at review.**

| What | Why |
|---|---|
| `Application/Features/Profile` renamed to `Features/Profiles` | the namespace shadowed the `Profile` entity: `error CS0118: 'Profile' is a namespace but is used like a type` in `RegisterCommandHandler`. Plural, like no other feature folder needs to be, and cheaper than renaming the aggregate |
| `ProfileFieldRequest<T>` and `ProfileDateFieldRequest` added as wire types, distinct from the command's `ProfileField<T>` | **the bug.** With a non-nullable `ProfileFieldVisibility` on the wire model, a body that omitted `visibility` bound to the enum's zero value — `Public`. That is exactly the silent default to public the contract forbids, and it passed silently until `UpdateProfile_WithAValueAndNoVisibility_IsRefused` failed with "expected a refusal, got OK". The tier is now nullable on the wire and refused explicitly with `422` naming the field |
| `ProfileDateField` in the command, beside `ProfileField<T>` | an unconstrained `T?` is "maybe-null reference", not `Nullable<DateOnly>`, so a date field cannot share the generic |
| `ux_music_preferences_user_kind_value` renamed to `ux_music_preferences_user_id_kind_value` | `SchemaConventionTests.Model_EveryIndex_FollowsThePrefixConvention` requires every column in the name. The existing test is the authority, and it found this without being asked |
| `ProfileRepository.FindOwnProfileAsync` split | `error S138 ... has 42 lines, which is greater than the 40 lines authorized` |

One test datum changed rather than a rule: the profile test was written with Ada Lovelace's 1815 birth date,
which the validator's 1900 floor refuses — the floor is there to catch a bad parse, and the validator's own test
asserts it, so the test datum moved and the rule stayed.


The table below assumes the recommendations: a `profiles` table with typed columns and per-field visibility,
`displayName` untiered, normalised always-private preferences, and a row per account created at registration.
**A different answer changes the shape, and the table is re-walked against the decisions before
implementation starts.**

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/Profile.cs` | create — the five fields and their tiers |
| `MusicRoom/src/MusicRoom/Domain/Entities/MusicPreference.cs` | create — `UserId`, `Kind`, `Value` |
| `MusicRoom/src/MusicRoom/Domain/Common/ProfileFieldVisibility.cs` | create — `Public`, `Friends`, `Private`; `Domain` because it is a domain fact, not a persistence detail |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IProfileRepository.cs` | create — read own, update fields, replace preferences |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Queries/GetOwnProfile/GetOwnProfileQuery.cs` | create — not auditable; reading your own profile changes nothing |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Queries/GetOwnProfile/GetOwnProfileQueryHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Queries/GetOwnProfile/OwnProfile.cs` | create — every field with its tier, plus the preferences |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Commands/UpdateProfile/UpdateProfileCommand.cs` | create — a subset of fields, each with its tier; auditable, `profile.update` |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Commands/UpdateProfile/UpdateProfileCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Commands/UpdateProfile/UpdateProfileCommandValidator.cs` | create — lengths, and **a value without a tier is `422`**, never a silent default to public |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Commands/ReplaceMusicPreferences/ReplaceMusicPreferencesCommand.cs` | create — auditable, `profile.replace-music-preferences` |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Commands/ReplaceMusicPreferences/ReplaceMusicPreferencesCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Profile/Commands/ReplaceMusicPreferences/ReplaceMusicPreferencesCommandValidator.cs` | create — bounded counts and lengths; an unbounded array is OWASP API4 |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | change — create the profile row in the same transaction as the account |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — `TryRegisterAsync` also takes the profile |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — insert it |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/ProfileRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/ProfileConfiguration.cs` | create — table `profiles`, one row per account, cascade foreign key, tiers stored as `smallint` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/MusicPreferenceConfiguration.cs` | create — `ux_music_preferences_user_kind_value`, so a duplicate is a write failure |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — two `DbSet`s |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — register the repository |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_Profiles.cs` | create — schema only |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_Profiles.Designer.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_ProfileBackfill.cs` | create — **data, separate from the schema change and idempotent**, per `database-design-migrations` |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_ProfileBackfill.Designer.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/MusicRoomDbContextModelSnapshot.cs` | change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/ProfileEndpoints.cs` | create — the three routes, `RequireAuthorization`, request and response records |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the group |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Profile/UpdateProfileCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Profile/ReplaceMusicPreferencesCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ProfileEndpointTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Persistence.Tests/SchemaConventionTests.cs` | no change expected — it holds the new tables to the naming rules automatically, which is the point of it |
| `docs/api/ENDPOINTS.md` | change — design notes naming the fields, `displayName`'s status, and where preferences sit |
| `docs/api/openapi.json` | change — regenerated |
| `docs/SDD.md` | change — §8 gains the two tables; §16 V.1 "three profile visibility tiers" row, honest that `Friends` cannot be evaluated yet |
| `brain/migrations/migrations-index.md` | change — two rows, one schema and one data |
| `docs/SDD.pdf`, `docs/api/ENDPOINTS.pdf` | change — regenerated by `./docs/build-pdf.sh` |
| `docs/specs/SPEC-011-own-profile-and-music-preferences.md` | change — status transitions |

### The Files table walk

**1. Assembly membership and every reference it implies.** `Profile`, `MusicPreference` and
`ProfileFieldVisibility` are `Domain`, which references nothing — the enum goes there rather than in
`Persistence` because "who may see this field" is a domain fact that a different datastore would still have.
The abstraction, the query, the two commands and their handlers are `Application`. The configurations and the
repository are `Persistence`. The endpoints are `WebAPI`. **No new project, no `.csproj` change**, every
reference inward.

**2. Types that cross an assembly boundary.** `Profile`, `MusicPreference`, `ProfileFieldVisibility`,
`IProfileRepository`, `OwnProfile`, both commands and the query are `public`. `OwnProfile` gets its own file
because the endpoint maps it and a reader will open both together. The handlers stay `internal sealed` with a
public constructor.

**3. Does anything actually invoke it?** Each route is mapped here, the repository has exactly three consumers,
and the registration change has one. Nothing is added that nothing calls.

**4. CLI, tool manifest, or unpinned package.** `dotnet ef migrations add`, twice — schema and backfill are
separate migrations on purpose, which is `database-design-migrations`' rule and not a stylistic choice.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Application` holds
`ValidationBehaviour` only; `Core.Security`, `Core.CrossCuttingConcerns` and `Core.Persistence` hold nothing but
a `.csproj`. Nothing to reuse, and nothing here belongs there.

**What the walk caught.**

- **`TryRegisterAsync` gains a parameter and is already at four.** Adding the profile makes five, which is the
  S107 limit exactly — so it fits, and the *next* thing registration writes will force an options record. Worth
  knowing before it is a build failure.
- **`AuditBehaviour` requires client context for every auditable command.** Both new commands are auditable, so
  both need the three client headers — which authenticated clients already send. The `GET` is a query and needs
  none.
- **`Program.cs` is at 41 coupled types of the 41 CA1506 allows** after SPEC-010. Mapping one more endpoint group
  from `Main` may trip it; the map call goes through `AuthEndpoints`-style extension so the coupling lands in the
  endpoint file, not in `Main`.

## Steps

Written against the recommendations. **Re-walk against the four decisions before starting.**

1. `ProfileFieldVisibility`, `Profile`, `MusicPreference`, then their configurations. Tiers persist as
   `smallint`: an enum stored as text invites a value nothing validates.
2. The two migrations — schema, then an idempotent `INSERT INTO profiles (...) SELECT ... FROM users WHERE NOT
   EXISTS (...)`. Written by hand in the data migration, because EF does not generate a backfill.
3. `IProfileRepository`: read own profile with preferences, apply a field subset, replace the preference set.
   The replace is delete-then-insert inside one transaction, which the audit behaviour already owns.
4. Registration creates the profile row: `displayName` is already on `users`, the rest start empty, and each
   tier starts at its own default — `realName` and `location` `Friends`, `bio` `Public`, `birthDate` `Private`.
   Defaults are a decision recorded here, not a silent choice: a field whose tier defaults to public would leak
   the first time somebody fills it in.
5. `GetOwnProfileQuery`: no parameter, the account from the token, and it returns every field with its tier —
   the owner is the one reader who sees all of them.
6. `UpdateProfileCommand`: a subset. Each present field carries a value **and** a tier, and a value without a
   tier is `422`. Absent fields are untouched, which is what `PATCH` means.
7. `ReplaceMusicPreferencesCommand`: `PUT`, so the set is replaced wholesale. Bounded at 50 entries of 64
   characters each; an unbounded array is OWASP API4 and this endpoint is behind no rate limit of its own.
8. Endpoints, tests, then regenerate `docs/api/openapi.json`.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `GetOwnProfile_ReturnsEveryFieldWithItsVisibility` | V.1 — the tiers are stored and visible to their owner |
| `GetOwnProfile_WithoutAToken_Returns401` | the `self` gate |
| `GetOwnProfile_ForAFreshAccount_ReturnsAnEmptyProfileNotA404` | escalation 4 — the row exists from registration |
| `UpdateProfile_WithASubset_LeavesTheOtherFieldsUntouched` | `PATCH` semantics |
| `UpdateProfile_WithAValueAndNoVisibility_Returns422` | the contract's own rule: no silent default to public |
| `UpdateProfile_CannotChangeAnotherAccountsProfile` | there is no route that names a user id — asserted by absence and by the token being the only source of identity |
| `ReplaceMusicPreferences_ReplacesTheWholeSet` | `PUT` semantics, and the duplicate index holds |
| `ReplaceMusicPreferences_WithTooManyEntries_Returns422` | OWASP API4 |
| `Profile_AuditRecordNamesTheAccountAndTheAction` | SPEC-009 and SPEC-010 — an authenticated mutation is recorded with its caller |
| `SchemaConventionTests` (existing, unchanged) | `profiles` and `music_preferences` obey the naming rules |
| every existing suite | identity and audit behave exactly as before |

## Done when

- [x] every file in the Files table exists and compiles, with the deltas above
- [x] every command under Verification passes — `make agent-check` exited 0 on 2026-09-28, 180 tests
- [x] OWASP checklists walked (A01, A03, A04, API1, API3, API4). Nothing new is unfixed: the `Friends` tier
      being stored and not evaluated is a stated limitation of this slice, not a finding — no reader is given
      more than a stranger's view, which is the safe direction to be wrong in
- [x] `docs/SDD.md` §8 documents both tables, and §16's tier row says plainly that `Friends` is stored and not
      yet evaluated, and that reading another user's profile is still designed only
- [x] `docs/api/ENDPOINTS.md` names the four tiered fields, says `displayName` is always public and untiered,
      and states that a field sent without a tier is `422`
- [x] `brain/migrations/migrations-index.md` carries both rows, the data one marked idempotent by construction
- [x] `docs/api/openapi.json` regenerated and the PDFs rebuilt
- [x] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| The friends graph | its own spec; the `Friends` tier means nothing without it |
| `GET /users` and `GET /users/{userId}` with per-field projection | after the graph |
| Avatar upload | its own spec, with storage and content-type decisions |
| A consumer for music preferences | issue O14; storage-only remains the honest answer at defence |
