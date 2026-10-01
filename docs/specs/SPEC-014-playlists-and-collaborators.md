---
id: SPEC-014
title: Playlists and collaborators — the aggregate, its gates, and an ETag that means something
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S5              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.3, V.6]
decisions: [ADR-0011, ADR-0012, ADR-0028, ADR-0033, ADR-0034]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1, 3 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29 with the four
  decisions below, which are the ones this spec would otherwise have escalated.
---

# Playlists and collaborators

## Goal

After this spec a user can create a playlist, list the ones they may see, read one with an `ETag`, change
its metadata under `If-Match`, set its visibility and licence, soft-delete it, and manage collaborators.
Ten of the thirteen rows in `docs/api/ENDPOINTS.md` §5. It is the first aggregate in this codebase with
**three different gates** — `member`, `owner` and `licence` — and the first with a concurrency token.

First unit of slice S5.

## Out of scope

- **Tracks.** Adding, reordering and removing them, with the sparse ordering key and the `412` that
  makes a reorder safe, is the next unit — it is the contended path subject V.2.3 grades, and it deserves
  its own spec rather than a paragraph in this one.
- **`PlaylistHub`.** The third unit, together with ADR-0019's hub filter, which finally has a hub to
  attach to.
- **Resolving a YouTube video id to metadata.** That is slice S4, blocked on a key that does not exist.
  Nothing here stores a track.
- **Hard delete.** `DELETE` is a soft delete, per the contract.

## Decisions taken under the blanket approval, 2026-09-29

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **PostgreSQL `xmin` is the concurrency token, and the `ETag` is derived from it.** | `docs/SDD.md` §12.2 already names `xmin` for the reorder path, so the aggregate uses one mechanism for both. Rejected: a `version` column the application increments — one forgotten increment and `If-Match` silently stops protecting anything |
| 2 | **Soft delete is `deleted_at_utc` plus a global query filter.** | Every read excludes deleted rows by default, so a forgotten `WHERE` cannot resurrect one. Rejected: a status enum, which makes "is it gone" a value to compare rather than a filter EF applies |
| 3 | **`member` is the owner or any collaborator row; `licence` is the owner or a collaborator with `canEdit`; `owner` is the owner.** A `Public` playlist is readable by any authenticated caller. | It is the smallest model that satisfies the contract's three symbols, and it puts the read gate and the write gate in different places, which is what `canEdit` exists for |
| 4 | **A playlist's licence accepts `Everyone` and `InvitedOnly` only.** `LocationAndTimeRestricted` is `422`. | Subject V.2.1 attaches location-and-time to an **event**, and ADR-0014's geofence and event code are event concepts. A playlist accepting the value would imply a check nothing implements |

`404`, never `403`, for a playlist the caller may not see — the contract's own rule.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — decisions 1, 2 and 4 are aggregate-shaping and inherited by the next two units |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — two new tables, additive, no backfill |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — ten existing rows. A design note records the licence restriction and what the `ETag` covers |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — three gates, and the first aggregate where "may this caller see it" is not simply "is it theirs" |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

### Discovered during implementation, 2026-09-28

| What | Why |
|---|---|
| `IPlaylistRepository` gained `PlaylistLicence` and `CollaboratorGrant` payload records | two members reached `error S107: Method has 6 parameters, which is greater than the 5 authorized`. The payloads also name what is being written instead of leaving four positional arguments to a reader |
| `PlaylistEndpointTests` split into two suites | `error S104: This file has 312 lines`. `PlaylistEndpointTests` keeps creation and the `ETag`/`If-Match` guard; `PlaylistAccessEndpointTests` takes the gates, listing and the soft delete |
| `PlaylistCollaboratorEndpoints` was planned as its own file and stayed that way | the walk predicted the 300-line limit and it was right: the two endpoint files are 267 and 118 lines |

One implementation note: `ListCollaborators` returns an empty list both when nobody is named and when the caller
may not see the playlist, so the endpoint asks for the playlist itself to tell `200 []` from `404`. The
alternative — a richer return type — would have put the authorisation answer in two places.

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/Playlist.cs` | create — name, visibility, licence, owner, `DeletedAtUtc`, `Version` (`xmin`) |
| `MusicRoom/src/MusicRoom/Domain/Entities/PlaylistCollaborator.cs` | create — `PlaylistId`, `UserId`, `CanEdit` |
| `MusicRoom/src/MusicRoom/Domain/Common/Visibility.cs` | create — `Public`, `Private`; SDD §6.2 |
| `MusicRoom/src/MusicRoom/Domain/Common/LicenceMode.cs` | create — the three values, with the playlist restriction enforced in validation |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaylistRepository.cs` | create — create, read, list, update, soft-delete, licence, and the three collaborator members, plus the access result type |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/*` | create — create, update metadata, set licence, delete, add collaborator, remove collaborator |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Queries/*` | create — get one, list, list collaborators |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PlaylistConfiguration.cs` | create — `xmin` as the token, the soft-delete query filter |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PlaylistCollaboratorConfiguration.cs` | create — `ux_playlist_collaborators_playlist_id_user_id` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaylistRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — two `DbSet`s |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_Playlists.cs` + `.Designer.cs` + snapshot | create/change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaylistEndpoints.cs` | create — the ten routes, `ETag` and `If-Match` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the group |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Playlists/CreatePlaylistCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistEndpointTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistCollaboratorEndpointTests.cs` | create |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/migrations/migrations-index.md` | change |
| `docs/specs/SPEC-014-playlists-and-collaborators.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Entities and the two enums in `Domain`; abstraction, commands and queries in
`Application`; configurations and repository in `Persistence`; endpoints in `WebAPI`. No new project, no
`.csproj` change, every reference inward. `Visibility` and `LicenceMode` go in `Domain/Common` beside
`ProfileFieldVisibility`, because SDD §6.2 calls them shared enumerations and the Track Vote slice will read
the same two.

**2. Boundary types.** Both entities, both enums, the repository interface, every command and query, and the
access result are `public`; handlers `internal sealed`.

**3. Invoked?** Ten routes, one consumer per repository member.

**4. CLI or package.** `dotnet ef migrations add` only. `xmin` needs no package: Npgsql maps it through
`UseXminAsConcurrencyToken`.

**5. corePackages.** Unchanged, still near-empty.

**What the walk caught.** Three things.
- **`PlaylistEndpoints.cs` will hold ten routes and their records and cannot fit in 300 lines.** It is two
  files from the start: `PlaylistEndpoints` for the aggregate, `PlaylistCollaboratorEndpoints` for the three
  collaborator routes.
- **The access decision is needed by every route**, so it is one repository read returning "may see / may
  edit / owner", not three.
- `Program.cs` maps a fifth group; the extension keeps `Main`'s coupling flat.

## Steps

1. `Visibility`, `LicenceMode`, `Playlist`, `PlaylistCollaborator`, configurations with
   `UseXminAsConcurrencyToken` and `HasQueryFilter(playlist => playlist.DeletedAtUtc == null)`, migration.
2. `IPlaylistRepository.FindAccessAsync` → `PlaylistAccess(bool Exists, bool MaySee, bool MayEdit, bool
   IsOwner)`. Every other member takes the caller and enforces its own gate in the statement.
3. Create: owner is the caller, `Private` and `Everyone` by default if omitted; licence restricted per
   decision 4.
4. Read one: projects the aggregate plus the caller's rights, and returns the `xmin` as the `ETag`.
5. Update metadata: `If-Match` required, compared against `xmin`; mismatch is `412`, absent header is `428`
   — **no**, the contract lists `412` only, so an absent header is treated as a mismatch and answered `412`.
6. Licence: `PUT`, owner only, `422` for `LocationAndTimeRestricted`.
7. Delete: soft, owner only, and the query filter makes it invisible everywhere afterwards.
8. Collaborators: owner adds and removes, `409` for a duplicate and for the owner adding themselves,
   `member` lists.
9. Tests, then regenerate the document.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Create_ReturnsTheNewPlaylistToItsOwner` | the aggregate exists |
| `Create_WithLocationAndTimeLicence_Returns422` | decision 4 |
| `Get_AsOwner_ReturnsAnETag` and `Get_TwiceUnchanged_ReturnsTheSameETag` | the token is stable |
| `Patch_WithAStaleIfMatch_Returns412` | the concurrency token protects a metadata write |
| `Patch_WithAFreshIfMatch_Succeeds_AndTheETagChanges` | it changes when the row does |
| `Patch_WithNoIfMatch_Returns412` | step 5 |
| `Get_APrivatePlaylist_AsAStranger_Returns404` | `404`, not `403` |
| `Get_APublicPlaylist_AsAnyAuthenticatedCaller_Returns200` | decision 3 |
| `Patch_AsACollaboratorWithoutCanEdit_Returns404` | the read gate and the write gate differ |
| `Patch_AsACollaboratorWithCanEdit_Succeeds` | `canEdit` means something |
| `Delete_AsOwner_MakesItInvisibleEverywhere` | decision 2, asserted through list and read |
| `Delete_AsACollaborator_Returns404` | owner-only |
| `AddCollaborator_Twice_Returns409`, `AddCollaborator_ForTheOwner_Returns409` | the unique index and the self case |
| `ListCollaborators_AsAStranger_Returns404` | the `member` gate on a read |
| `PlaylistMutation_WritesAnAuditRecord` | ADR-0033 on a new surface |
| `SchemaConventionTests` (existing) | both tables obey the naming rules |

## Done when

- [x] every file in the Files table exists and compiles, with the deltas above
- [x] `make agent-check` passes — exit 0 on 2026-09-28, 238 tests
- [x] `docs/SDD.md` §8 documents both tables and both §16 V.2.3 rows say what exists and that tracks and the
      hub are the remaining two units
- [x] `docs/api/ENDPOINTS.md` records the licence restriction, what the `ETag` is, why an absent `If-Match` is
      `412`, what each of the three gates means, and that `DELETE` is soft
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] OWASP checklists walked (A01, A04, API1, API3, API4, API5). Nothing new unfixed: every gate is decided in
      the statement that performs the write, and a playlist the caller may not see answers `404`
- [x] `status: done` in this file
