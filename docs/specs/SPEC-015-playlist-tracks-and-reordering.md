---
id: SPEC-015
title: Playlist tracks — sparse ordering, a guarded reorder, and the parallel move that proves it
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S5              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.3, V.6]
decisions: [ADR-0011, ADR-0012, ADR-0023, ADR-0028, ADR-0033]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1, 3 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29 with the five
  decisions below, which are the ones this spec would otherwise have escalated. Decision 2 is the load-
  bearing one: it is what makes `If-Match` protect a collection rather than a row.
---

# Playlist tracks and reordering

## Goal

After this spec a playlist holds tracks, and two people moving the same track at the same time is a
detected conflict rather than a silent overwrite. `POST`, `PATCH` and `DELETE` on
`/api/v1/playlists/{playlistId}/tracks`, plus the paged `GET` with its `ETag`: the three remaining rows of
`docs/api/ENDPOINTS.md` §5. Subject V.2.3 grades reorder race conditions explicitly, and this is where
that is answered — with a test that moves one track twenty times in parallel and reads the final order
back.

Second unit of slice S5.

## Out of scope

- **`PlaylistHub`.** The broadcast after commit is the third unit, with ADR-0019's hub filter. This spec
  leaves the data correct and silent.
- **Resolving a video id to a title, duration or thumbnail.** That is slice S4's catalog and `TrackRef`
  cache (ADR-0023), blocked on a YouTube key that does not exist. A track here is the id the client sent,
  stored and returned verbatim.
- **Deduplication across playlists**, playlist copying, and import. No contract row.

## Decisions taken under the blanket approval, 2026-09-29

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **Position is a `bigint` sparse key with a gap of 1000**, and an insert between neighbours takes the midpoint. | `brain/architecture/realtime-architecture.md` already names sparse keys with a gap of 1000. Inserting between two tracks writes **one row**, so a 300-track playlist does not rewrite 300 rows because somebody dropped a song in the middle. Rejected: a contiguous `index` column, which rewrites the tail on every insert and turns every move into a mass update |
| 2 | **The playlist's `xmin` is the `ETag` for its track list too, and every track write touches the playlist row in the same transaction.** | `If-Match` has to protect a *collection*, and a collection has no row of its own. Touching the parent gives the list one version that changes whenever its contents do — so a client that read the list and then moves a track is told `412` if anybody changed anything first. Rejected: an `ETag` computed from the track rows (a hash nobody can compare cheaply) or no guard at all, which is the silent overwrite V.2.3 grades |
| 3 | **When no gap remains between neighbours, the whole playlist is renumbered** at gap 1000 inside the same transaction. | A sparse key eventually runs out: 1000, then 500, then 250 — about ten moves into the same slot. Renumbering is bounded by decision 4 and happens inside a transaction that already holds the playlist row |
| 4 | **A playlist holds at most 500 tracks**, `409` beyond that. | It bounds the renumber above, and an unbounded collection behind an authenticated endpoint is OWASP API4 |
| 5 | **The same video may appear twice in one playlist.** | The contract lists no `409` on `POST .../tracks`, unlike the vote endpoint which does. Playing a song twice in a set is a legitimate thing to want, and the duplicate the vote endpoint refuses is a *vote*, not a track |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — decisions 1, 2 and 3 are the reorder mechanism `docs/SDD.md` §12.2 sketches; this spec makes it concrete, and decision 2 extends the `ETag` of SPEC-014 from a row to a collection |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — one new table, additive, no backfill |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — three existing rows. Design notes record the gap, the renumber, the cap and the duplicate rule |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — every route is behind the `licence` gate SPEC-014 built, and a mistake lets a reader rewrite a playlist |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/PlaylistTrack.cs` | create — `PlaylistId`, `YoutubeVideoId`, `Position`, `AddedByUserId` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaylistTrackRepository.cs` | create — add, move, remove, list, plus the outcome types |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/AddTrack/*` | create — command, handler, validator |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/MoveTrack/*` | create — command, handler, validator |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/RemoveTrack/*` | create — command, handler |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Queries/ListTracks/*` | create — query, handler, `PlaylistTrackSummary` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PlaylistTrackConfiguration.cs` | create — `ix_playlist_tracks_playlist_id_position` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaylistTrackRepository.cs` | create — the transaction boundary and the paged read |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaylistTrackWriter.cs` | create — the three guarded writes, each inside the caller's transaction |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaylistTrackPositions.cs` | create — the sparse-key arithmetic, the neighbour lookup and the renumber |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — one `DbSet` |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_PlaylistTracks.cs` + `.Designer.cs` + snapshot | create/change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaylistTrackEndpoints.cs` | create — the four routes, `ETag` and `If-Match` |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaylistChildRoutes.cs` | create — one call for the collaborator and track routes, against S1200 |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaylistEndpoints.cs` | change — map the child routes, and share `ETag`/`If-Match` |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Playlists/TrackCommandValidatorTests.cs` | create — both validators in one class, one subject |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackEndpointTests.cs` | create — add, list, remove, the guard, the cap and the `licence` gate |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackOrderingTests.cs` | create — where a track lands, and the renumber |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackConcurrencyTests.cs` | create — the parallel move and the parallel add |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackRequests.cs` | create — the guarded requests the three suites share |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/migrations/migrations-index.md` | change |
| `brain/projects/musicroom-backend.md` | change — "eight specs" and the stale date the last `/brain-check` found |
| `docs/specs/SPEC-015-playlist-tracks-and-reordering.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Entity in `Domain`; abstraction, commands and queries in `Application`; configuration and
repository in `Persistence`; endpoints in `WebAPI`. No new project, no `.csproj` change, every reference
inward.

**2. Boundary types.** `PlaylistTrack`, `IPlaylistTrackRepository`, the three commands, the query, the
summary and the outcome enums are `public`; handlers `internal sealed`.

**3. Invoked?** Four routes, four repository members.

**4. CLI or package.** `dotnet ef migrations add` only.

**5. corePackages.** Unchanged. A sparse ordering key is arguably domain-agnostic, and it still stays out:
`Core.Persistence` holds a `.csproj` and nothing else, and one caller does not justify a shared helper.

**What the walk caught.** Two things.
- **The reorder needs the playlist row in the same transaction**, so the repository takes the expected
  `xmin` and updates the parent first: if that update matches no row, the caller is stale and nothing else
  runs. ADR-0033's `IUnitOfWork` already gives the transaction, because every one of these commands is
  auditable.
- **`PlaylistEndpoints.cs` is at 267 lines**, so the four track routes go in their own file from the start.

## Steps

1. `PlaylistTrack` with `Position` as `long`, configuration with `ix_playlist_tracks_playlist_id_position`
   and a cascade to `playlists`, migration.
2. `IPlaylistTrackRepository.TryAddAsync`: guard on the playlist's `xmin`, count the tracks against the
   cap, compute the position — after a named neighbour, at the head, or at the tail by default — insert,
   and touch the parent.
3. `TryMoveAsync`: guard on `xmin`, compute the new position from the named neighbour, renumber if no gap
   remains, update the one row, touch the parent.
4. `TryRemoveAsync`: guard on `xmin`, delete the row, touch the parent.
5. `ListAsync`: ordered by position, paged, with the playlist's `xmin` returned so the endpoint can set the
   `ETag` the client will send back.
6. Validation: a video id is 11 characters of the YouTube alphabet; `afterTrackId` and `beforeTrackId`
   together are `422`; both absent on a move is `422`, because a move with no destination is not a move.
7. Endpoints: `GET` sets the `ETag`; the three writes require `If-Match` and answer `412` without it, as
   SPEC-014 established.
8. Tests, including the twenty-way parallel move, then regenerate the document.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `AddTrack_WithNoDestination_Appends` | the default position is the end |
| `AddTrack_AfterANamedTrack_InsertsBetween` | decision 1, read back as order |
| `AddTrack_AtHead_InsertsBeforeEverything` | the head is asked for explicitly — see the delta below |
| `AddTrack_AfterAnUnknownTrack_Returns404` | a refusal leaves the collection's `ETag` alone |
| `AddTrack_TwiceWithTheSameVideo_IsAllowed` | decision 5 |
| `AddTrack_ToAFullPlaylist_Returns409` | decision 4 |
| `AddTrack_WithAMalformedVideoId_Returns422` | the id is validated, not trusted |
| `AddTrack_ByAReaderWhoMayNotEdit_Returns404` | the `licence` gate from SPEC-014 holds on a new surface |
| `AddTrack_ChangesTheTrackListETag` | decision 2 — a track write changes the collection's version |
| `AddTrack_WithAStaleIfMatch_Returns412` | decision 2, on the add path |
| `AddTrack_WithNoIfMatch_Returns412` | an absent precondition is a failed one, as on SPEC-014's `PATCH` |
| `MoveTrack_BeforeTheFirst_PutsItFirst` | the move contract |
| `MoveTrack_WithBothNeighbours_Returns422` | the contract's own rule |
| `MoveTrack_WithNoNeighbour_Returns422` | a move needs a destination |
| `AddTrack_RepeatedlyIntoOneGap_RenumbersAndKeepsTheOrder` | decision 3, by inserting into the same gap eleven times |
| `RemoveTrack_LeavesTheRemainingOrder` | a removal leaves a hole and rewrites nothing |
| `ListTracks_ByAStranger_Returns404` | the `member` gate, and `404` over an empty list |
| `Reorder_TwentyParallelMovesOfOneTrack_ProduceOneWinner` | **AGENTS.md's parallel rule, on the write subject V.2.3 grades** — exactly one `200`, nineteen `412`, and a final order that is a permutation of the original with distinct positions |
| `Add_TwentyParallelAddsWithOneToken_AddOneTrack` | the same guard covers growth, not only reordering |
| `AddTrack_WritesAnAuditRecord` | ADR-0033 |
| `TrackCommandValidatorTests` (8 cases) | the validation rules of step 6 |
| `SchemaConventionTests` (existing) | the new table obeys the naming rules |

## Discovered during implementation

- **`afterTrackId: null` cannot mean "the head" on a `PATCH`.** An absent field and a null field are the same
  request over the wire, so that rule would make an empty body a silent reorder. The add path takes an explicit
  `atHead`, and defaults to appending; the move path requires **exactly one** neighbour, and moving to the head
  is `beforeTrackId` naming the current first. `docs/api/ENDPOINTS.md` §5 now says this instead of the old note.
  This is a contract clarification, not a new capability — gate 4's answer becomes "design notes only, plus the
  statuses the three rows omitted".
- **Every refusal is decided before the parent is touched.** The first draft counted the cap after the `xmin`
  guard, which made a `409` bump the `ETag` the caller had just read — a refused write that forces a reload. The
  cap check and the neighbour lookup now run first; a racing writer cannot slip past them, because that writer
  also touches the playlist row and so makes this caller stale.
- **The index on `(playlist_id, position)` is not unique.** A renumber rewrites every row of one playlist in one
  batch, and a unique constraint checked statement by statement would collide with positions the batch has not
  reached yet. Nothing needs it: the parent touch serialises every track write.
- **Three analyzer-driven splits.** `PlaylistTrackWriter` and `PlaylistTrackPositions` came out of the
  repository (S104), `PlaylistChildRoutes` came out of `PlaylistEndpoints` (S1200 — naming both child route sets
  put it at 31 coupled types), and the shared request helpers came out of the track suites (S104). Two handler
  signatures went over the five-parameter gate and now bind `[AsParameters]` records, with the query names
  spelled out so the generated document still advertises `page` and `pageSize`.
- **Four test files, not three.** The cap, the ordering and the race each want their own fixture, and the
  shared request helpers are the fifth file.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §8 documents the table, §12.2's reorder row and §16's two V.2.3 rows say what is now
      proven rather than designed
- [x] `docs/api/ENDPOINTS.md` records the gap, the renumber, the 500-track cap and the duplicate rule
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] `brain/projects/musicroom-backend.md` no longer says "eight specs" or carries a stale date
- [x] OWASP checklists walked; unfixed findings in the register
- [x] `status: done` in this file
