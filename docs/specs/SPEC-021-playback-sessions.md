---
id: SPEC-021
title: Playback sessions — the server picks what plays, the client says when
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S7              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.1, V.2.2, V.6]
decisions: [ADR-0017, ADR-0028, ADR-0033, ADR-0038, ADR-0039]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29 with the seven decisions
  below. Gate 1 is two readings of ADR-0017 rather than changes to it: decision 1 says the session does not copy
  the controller it reads from the device, and decision 4 reconciles that ADR's "returns the current state" with
  the contract's `412` by separating the two kinds of staleness. Decision 4 is the load-bearing one.
---

# Playback sessions

## Goal

After this spec subject V.2.1's **"a highly-voted track plays earlier"** is true in code, and V.2.2's
delegated control has something to command. A session is opened on a device for an event or a playlist; the
controller advances it and the **server** picks what is next — highest vote count for an event, next position
for a playlist; an explicit jump is possible; stopping ends it. Six rows of `docs/api/ENDPOINTS.md` §7, plus
`NowPlayingChanged` on whichever hub owns the source.

Second unit of slice S7.

## Out of scope

- **`ControlHub`.** The third unit: `user:{userId}`, `ControlGranted`, `ControlRevoked`,
  `ControlTakenOver`. SPEC-020's grant writes already exist, so that unit is a hub and three enqueues.
- **A watchdog for a silent controller.** ADR-0017 rejected it and said why; SDD §11.2 repeats it.
- **Resolving a track's duration.** The server never schedules on duration — that is the drift ADR-0017
  avoids. The client reports that a track ended.

## Decisions taken under the blanket approval, 2026-10-01

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The session does not store a controller.** Who may command it is read from the device: `controller_user_id ?? owner_id`. | ADR-0017 lists `controllerUserId` on the session; SPEC-020 then made the device row the single place a grant lives. Copying it onto the session would be two rows that can disagree about who controls one device — the exact thing SPEC-020 rejected one level down. The ADR's sentence "a control grant changes `controllerUserId` on the session" stays true in effect: the grant changes who controls the session, by being the thing the session reads |
| 2 | **At most one *live* session per device**, enforced by a unique index filtered on `ended_at_utc IS NULL`. A stopped session keeps its row. | ADR-0017's invariant, as a database constraint rather than a check. History is kept because "this device played this event last night" is a fact worth having, and because a hard delete would make the audit records that name the session point at nothing |
| 3 | **The source is a `(source_type, source_id)` pair with no foreign key**, `source_type` a `smallint` over `Event \| Playlist`. | One column pair for two possible parents is the honest shape; two nullable foreign keys would allow a row that is both or neither. The cost is stated: nothing in the database stops a session naming a deleted source, so the read path resolves it and the advance treats a vanished source as an exhausted queue |
| 4 | **Two kinds of staleness, two answers.** A stale `If-Match` is `412` — the contract's word, and the race arbiter. A stale `expectedNowPlayingId` is **success**: `200` with the current state, nothing advanced. | ADR-0017 says a duplicate advance "finds the expectation already stale and returns the current state instead of skipping a track", and the contract lists `412`. They are about different things, and conflating them loses one guarantee or the other: the token stops two controllers advancing at once, and the expectation stops one controller's retry skipping a song. Rejected: `412` for both (a retry then needs a reload to do nothing) and `200` for both (two clients could each advance once) |
| 5 | **An advance with nothing left ends the session and answers `409`.** | The contract lists `409 queue empty`; SDD §11.2's state machine says the session reaches `Ended`. Both are satisfied by doing the end and reporting the refusal, and `NowPlayingChanged` carries a null now-playing so listeners stop rather than waiting |
| 6 | **The server picks the next item, always.** For an event: the highest vote count, ties broken by the earliest suggestion — the same order the queue read uses. For a playlist: the next position. | ADR-0017, and the half of V.2.1 the vote count exists for. The client is the authority on *when* a track ended and never on *what* follows |
| 7 | **`NowPlayingChanged` carries the new now-playing id and no row version**, routed to `event:{id}` or `playlist:{id}` by the session's source. | ADR-0039's rule, extended: the authoritative value is the id itself, and it is idempotent to apply twice. The session's own `xmin` is the `If-Match` of its writes and would mean nothing to a listener who never holds it |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes, as two readings** — decisions 1 and 4 interpret ADR-0017 where it meets SPEC-020 and the contract. Neither reverses it, and both are written down here with what they rejected, so no new record is needed |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — one new table, additive, no backfill |
| 4 | changes `docs/api/ENDPOINTS.md`? | §7's six rows become implemented; design notes record decisions 4 and 5 |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — a new gate (the controller), a guarded write, and the fourth concurrency hotspot SDD §12 names |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Common/PlaybackSourceType.cs` | create — `Event \| Playlist` |
| `MusicRoom/src/MusicRoom/Domain/Entities/PlaybackSession.cs` | create — device, source, now-playing, `xmin`, `ended_at_utc` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaybackSessionRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/PlaybackContracts.cs` | create — the detail, the outcomes |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaybackQueueReader.cs` | create — "what plays after this", for both source kinds |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Commands/StartSession/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Commands/AdvanceSession/*` | create — decisions 4, 5 and 6 |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Commands/JumpToItem/*` | create — the explicit jump |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Commands/StopSession/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Queries/GetSession/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Queries/GetDeviceSession/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/PlaybackControlGate.cs` | create — who may command this session, from the device |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/PlaybackSessionWriter.cs` | create — the gates, the next item and the guarded writes, once |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/PlaybackBroadcasts.cs` | create — `NowPlayingChanged` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PlaybackSessionConfiguration.cs` | create — the filtered unique index |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaybackSessionRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaybackQueueReader.cs` | create — the next item, per source kind |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — one `DbSet` |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_PlaybackSessions.cs` + `.Designer.cs` + snapshot | create/change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaybackEndpoints.cs` | create — the five session routes |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/DevicePlaybackEndpoints.cs` | create — `GET /devices/{id}/playback-session`, its own class against the coupling gate |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/DeviceEndpoints.cs` | change — map it |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — split into three groups by bounded context, against S1200 |
| `MusicRoom/src/MusicRoom/WebAPI/Realtime/RealtimeBroadcaster.cs` | change — route `NowPlayingChanged` by source |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map them |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaybackRequests.cs` | create — the requests the three suites share |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaybackSessionEndpointTests.cs` | create — the lifecycle and the gates |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaybackAdvanceEndpointTests.cs` | create — the order, the idempotence, the exhausted queue |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaybackConcurrencyTests.cs` | create — **twenty parallel advances** |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistBroadcastTests.cs` | change — a listener race in SPEC-016's suite, found by this unit's gate run |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/migrations/migrations-index.md` | change |
| `brain/projects/musicroom-backend.md` | change |
| `docs/specs/SPEC-021-playback-sessions.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Entity and enum in `Domain`; abstractions, features and the gate in `Application`;
configuration, repository and the queue reader in `Persistence`; endpoints and the broadcast route in `WebAPI`.

**2. Boundary types.** The entity, the enum, both abstractions, the commands, the queries, the gate and the
payload are `public`; handlers, the configuration and the repositories `internal sealed`.

**3. Invoked?** Six routes, two repositories, one gate.

**4. CLI or package.** `dotnet ef migrations add` only.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **The next item is two queries behind one question.** An event's next is a vote count ordering and a
  playlist's is a position ordering; `IPlaybackQueueReader` is one abstraction so the advance handler contains
  no `if` on source type, and the two orderings stay beside the queries that already express them.
- **The controller gate is a read of another aggregate**, so it is a named collaborator rather than a line in
  four handlers — `EventVoteGate`'s shape, for the same reason.
- **`NowPlayingChanged` needs the session's source to pick a hub**, which means the broadcaster learns one more
  payload type rather than the handlers learning about hubs.

## Steps

1. `PlaybackSourceType`, `PlaybackSession`, the configuration with the filtered unique index, one migration.
2. `IPlaybackSessionRepository`: start (`409` when the device already has a live session), read by id, read by
   device, the guarded advance and jump, stop.
3. `IPlaybackQueueReader`: the next item after a given one, for an event and for a playlist, and the first item
   when a session starts.
4. `PlaybackControlGate`: the owner or the delegate of the session's device, read live.
5. The advance handler: the gate, then the expectation (decision 4), then the next item (decision 6), then the
   guarded write, then the enqueue; nothing left ends the session and answers `409` (decision 5).
6. Endpoints, and `NowPlayingChanged` routed by source.
7. Tests, including twenty parallel advances, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Start_OnAnEvent_PlaysTheTopOfTheQueue` | decision 6 on an event: the server picks, and it picks the most-voted |
| `Start_OnAPlaylist_PlaysTheFirstTrack` | and on a playlist: the first position |
| `Start_OnAnEmptySource_StartsWithNothingPlaying` | a room that starts the music before anybody suggested a track is waiting, not broken |
| `Start_TwiceOnOneDevice_Returns409` | decision 2, arbitrated by the filtered index |
| `Start_OnAnotherAccountsDevice_Returns404` · `OnASourceTheCallerMayNotSee_Returns404` | both gates on the open |
| `Get_AsAMemberOfTheSource_Returns200` · `Get_AsAStranger_Returns404` | the `member` gate on the read, with the `ETag` |
| `GetByDevice_AsTheOwner_Returns200` · `WithNoSession_Returns404` | §7's third row |
| `Stop_AsTheDeviceOwner_EndsIt_AndAllowsANewSession` | decision 2's point: the index is filtered, so history stays and a new session is allowed |
| `Stop_AsAStranger_Returns404` | stopping is the owner's |
| `Start_WritesAnAuditRecord` | ADR-0033 on a new surface |
| `Advance_AsTheController_PlaysTheNextMostVoted` | **V.2.1: a highly-voted track plays earlier** |
| `Advance_OnAPlaylist_PlaysTheNextPosition` | the other ordering |
| `Advance_AsTheDelegate_Succeeds` | **V.2.2: what a delegate actually controls** |
| `Advance_AsTheOwnerAfterDelegating_Returns404` | the grant is exclusive — the owner's recourse is to revoke |
| `Advance_WithAStaleExpectation_ReturnsTheCurrentState` | **decision 4 — a retry does not skip a track** |
| `Advance_WithAStaleIfMatch_Returns412` | decision 4's other half |
| `Advance_WithTheQueueExhausted_Returns409_AndEndsTheSession` | decision 5 |
| `Jump_ToAnItemInTheSource_Succeeds` · `ToAnItemInAnotherSource_Returns404` | the explicit jump, bounded to the source |
| `Advance_TwentyParallelAdvances_MoveOnce` | **ADR-0017's fourth hotspot — one `200`, nineteen `412`, and the session on the *second* track, not the third** |
| `Advance_TwentyParallelAdvancesOfALastTrack_EndItOnce` | one `409` ends it, and nothing resurrects it |
| `Advance_ReachesAJoinedClient_AsNowPlayingChanged` | decision 7, on the hub owning the source |
| `SchemaConventionTests` (existing) | the new table obeys the naming rules |

## Discovered during implementation

- **The registration method hit the coupling gate.** `PersistenceServiceRegistration` reached 31 types with two
  more repositories, so it is now three groups — identity, collaboration, playback — following SDD §5.2's bounded
  contexts. A new repository has one obvious home, which is a better outcome than a shorter list.
- **The broadcast route table hit the complexity gate** (S1541, 11 branches) once `NowPlayingChanged` joined it.
  It is now one table per hub plus the one payload that belongs to both, which also answers "what does
  `PlaylistHub` publish" with a list rather than a filter.
- **`Main` is at CA1506's limit**, so the playback routes are mapped from `RealtimeServices` beside the hubs
  rather than from `Main`. Noted because the next unit will hit the same wall and should not spend a turn
  rediscovering it.
- **The gate caught a latent flake in SPEC-016's suite, not in this unit.**
  `PlaylistBroadcastTests.AStaleWrite_SendsNothingAsync` waited for silence after registering its listener
  *between* a successful write and a refused one — and the successful write's `TrackAdded` travels
  asynchronously, so it could arrive after registration and look like the message that should not exist. The same
  race SPEC-019 fixed in the event suite; fixed here the same way, by counting messages from before the act
  instead of waiting for the next one. The product behaviour was always right, which is why the change is in the
  test.
- **One session test also failed once and passed on re-run**, taking 16 s where it normally takes 2 — a container
  flake rather than a fault, reported as such rather than silently re-run.
- **An advance by rank, not by cursor.** An event's next item is "the highest-voted suggestion other than this
  one", so a queue that reorders mid-track still plays what the room wants most. A playlist's next item is the
  next position, because that order is the thing a human arranged. Both live in `PlaybackQueueReader` beside the
  queries that already express them.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §11.2, §12 and §16's V.2.1 and V.2.2 rows say what is built
- [x] `docs/api/ENDPOINTS.md` §7 records decisions 4 and 5
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] OWASP checklists walked; no new finding — a session reads but never widens a source's gate, and the
      silent-controller gap is ADR-0017's accepted one, already recorded in SDD §11.2
- [x] `status: done` in this file
