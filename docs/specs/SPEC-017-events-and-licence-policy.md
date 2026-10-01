---
id: SPEC-017
title: Events — the aggregate, its three gates, the licence policy, invitations and attendance
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S6              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.1, V.6]
decisions: [ADR-0011, ADR-0012, ADR-0014, ADR-0028, ADR-0033]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29 with the six
  decisions below. Gate 1 is answered by ADR-0014, which is already accepted: this spec implements its
  policy rather than deciding anything new about it. Decision 3 is the load-bearing one — a
  `LocationAndTimeRestricted` event with no presence policy is refused at creation, so the mode can never
  exist in a state where nothing is actually checked.
---

# Events, their licence policy, invitations and attendance

## Goal

After this spec subject V.2.1's **container** exists: an event with a name, a UTC window, a visibility and
a licence; the three gates (`member`, `owner`, `licence`) decided server-side; invitations that make a
private event reachable; and attendance that records who is at it. Eleven of the twenty rows of
`docs/api/ENDPOINTS.md` §4 — everything up to, but not including, the contended write path.

First unit of slice S6, the same shape SPEC-014 had for S5: build the aggregate and its gates first, so the
vote race lands on a surface whose authorisation is already settled and tested.

## Out of scope

- **Suggestions, votes, the queue and `now-playing`.** The contended write path is the second unit, with
  the unique-index arbitration, the presence checks and the parallel vote tests. This spec leaves the
  licence *policy* stored and readable and does not yet enforce it on any write, because there is no write
  to enforce it on.
- **Event codes.** `POST /events/{eventId}/event-codes` is credential-shaped — short-lived, single-use per
  user, rate limited — and belongs with the vote that consumes it.
- **`EventHub`.** Third unit, with ADR-0019's filter and ADR-0035's post-commit behaviour, both of which
  SPEC-016 already built and proved on `PlaylistHub`.
- **Resolving a video id.** Slice S4's catalog, still blocked on a YouTube key.

## Decisions taken under the blanket approval, 2026-10-01

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The licence policy lives on the `events` row**, in nullable columns: `presence_proof`, `geofence_latitude`, `geofence_longitude`, `geofence_radius_metres`. | One policy per event, so a second table would be a join that can never return more than one row. Nullable because the two other licence modes have no policy, and a row of defaults would read as "a geofence at the equator" rather than "no geofence". Rejected: a JSON column, which no query can constrain and no migration can rename |
| 2 | **`PresenceProof` is one `smallint` with `Geofence`, `EventCode` or `Both`** — SDD §6.2's enum, unchanged. | Two booleans would allow a fourth state, "neither", which decision 3 exists to make impossible. One column with three values cannot express it |
| 3 | **A `LocationAndTimeRestricted` event must carry a presence policy, and `422` otherwise.** A geofence proof additionally requires a centre and a radius. | ADR-0014's whole point is that the server rules on evidence. A licence mode that claims to restrict by place and time, with nothing configured to check, is the failure that looks like a feature. Refused at creation and at `PUT .../licence`, so it cannot exist in the database at all |
| 4 | **The window is two required UTC instants, and `endsAtUtc` must be after `startsAtUtc`.** Server time decides everything later; a client clock is evidence of nothing (ADR-0014). | An open-ended event is not a "given time window", which is the subject's wording. Rejected: a nullable end, which would make the licence mode's time half unenforceable |
| 5 | **Attendance is a roster, not a gate.** `POST /attendances` records presence and is **never** required before anything else. On a public event any authenticated caller may attend; on a private one the invitation is the gate. | Subject V.2.1 says that when an event is public, *all* users can find it and vote. Making attendance a precondition would quietly narrow that to "all users who remembered to check in". The contract's design notes say the same |
| 6 | **The geofence centre is stored at six decimal places; a voter's coordinates are never stored at all.** | The centre is a venue, not a person — it is the event's own published fact, and 0.1 m precision is what makes a radius check meaningful. Data minimisation (`application-security`, ADR-0014) is about the *voter's* position, which unit 2 will check and discard. Recorded here so unit 2 inherits the rule rather than rediscovering it |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes, and already recorded** — ADR-0014 decided the geofence-plus-code policy; this spec stores and validates it. Nothing new is decided, and nothing contradicts it |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — three new tables, additive, no backfill |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — eleven existing rows. Design notes gain the `422` of decision 3 and the window rule |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — three gates on a new aggregate, and a mistake lets a stranger read or rewrite somebody's private event |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Common/PresenceProof.cs` | create — SDD §6.2's enum |
| `MusicRoom/src/MusicRoom/Domain/Entities/Event.cs` | create — window, visibility, licence, policy, `xmin`, soft delete |
| `MusicRoom/src/MusicRoom/Domain/Entities/EventInvitation.cs` | create |
| `MusicRoom/src/MusicRoom/Domain/Entities/EventAttendance.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IEventRepository.cs` | create — the gate read, the writes, and the outcome types |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/CreateEvent/*` | create — command, handler, validator |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/UpdateEvent/*` | create — the guarded `PATCH` |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/SetEventLicence/*` | create — decision 3's refusal lives in the validator |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/DeleteEvent/*` | create — soft delete |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/InviteToEvent/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/UninviteFromEvent/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/AttendEvent/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/LeaveEvent/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Queries/GetEvent/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Queries/ListEvents/*` | create — the three scopes |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Queries/ListInvitations/*` | create |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/EventConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/EventInvitationConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/EventAttendanceConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/EventRepository.cs` | create — the gate read and the guarded writes |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/EventRosterWriter.cs` | create — invitations and attendance; the file-length gate did need the split |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — three `DbSet`s |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_Events.cs` + `.Designer.cs` + snapshot | create/change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventEndpoints.cs` | create — the five aggregate routes |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventPayloads.cs` | create — the wire shapes, kept out of the endpoint file |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventLicenceEndpoints.cs` | create — `PUT .../licence`, its own class against S1200 |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventChildRoutes.cs` | create — one call for the licence and roster routes |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventRosterEndpoints.cs` | create — invitations and attendance |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/ConcurrencyHeaders.cs` | create — the `ETag` and `If-Match` helpers, moved out of `PlaylistEndpoints` and now shared |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaylistEndpoints.cs`, `PlaylistTrackEndpoints.cs` | change — call the shared helpers |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map them |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Events/EventCommandValidatorTests.cs` | create — 12 cases, including decision 3 from both directions |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventRequests.cs` | create — the requests the three suites share |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventEndpointTests.cs` | create — the aggregate and the `ETag` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventAccessEndpointTests.cs` | create — the three gates |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventRosterEndpointTests.cs` | create — invitations and attendance |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/migrations/migrations-index.md` | change |
| `brain/projects/musicroom-backend.md` | change — S6 started |
| `docs/specs/SPEC-017-events-and-licence-policy.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Entities in `Domain`; the abstraction, commands and queries in `Application`;
configurations and repositories in `Persistence`; endpoints in `WebAPI`. No new project, every reference
inward.

**2. Boundary types.** The entities, `IEventRepository`, the commands, the queries and the outcome types are
`public`; handlers and configurations `internal sealed`.

**3. Invoked?** Eleven routes, one repository pair.

**4. CLI or package.** `dotnet ef migrations add` only.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **This is SPEC-014 again, with a window and a policy.** The gate shape (`member`/`owner`/`licence`), the
  `xmin` `ETag`, the soft delete and the `404`-not-`403` rule are all established; reusing them exactly is
  the point, and any divergence here would be a bug rather than a decision.
- **The `licence` gate has no consumer yet.** `FindAccessAsync` answers it anyway, because unit 2's vote is
  the consumer and splitting the gate across two specs is how two half-gates happen.
- **`Visibility` and `LicenceMode` already exist** in `Domain/Common`, shared with playlists — SDD §6.2.
  `PresenceProof` is the only new enum.

## Steps

1. `PresenceProof`, `Event`, `EventInvitation`, `EventAttendance`, their configurations and one additive
   migration. `xmin` as the concurrency token, `deleted_at_utc` with a global query filter, unique indexes
   on `(event_id, user_id)` for both roster tables.
2. `IEventRepository.FindAccessAsync`: the three gates in one read — owner, invitee, public — plus whether
   the event is inside its window, which unit 2 needs and this one reports.
3. The five aggregate routes: create, list by scope, read with an `ETag`, the guarded `PATCH`, the soft
   delete. `PUT .../licence` is owner-only, like a playlist's.
4. Validation: a name, a window where the end is after the start, and decision 3's refusal — a
   `LocationAndTimeRestricted` event with no presence policy, or a geofence proof with no centre and radius,
   is `422` naming the field.
5. Invitations: owner-only, `409` on a duplicate from the unique index, `404` for an event the caller does
   not own.
6. Attendance: any authenticated caller on a public event, an invitee on a private one, `409` on a
   duplicate, and `DELETE .../attendances/current` for leaving.
7. Tests, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Create_ThenRead_ReturnsTheEventToItsOwner` | the aggregate exists, round-trips, and carries an `ETag` |
| `Create_WithAnEndBeforeTheStart_Returns422` | decision 4 |
| `Create_WithTheRestrictedLicenceAndNoPolicy_Returns422` | **decision 3 — the mode cannot exist unchecked** |
| `Create_WithAGeofenceProofAndNoCentre_Returns422` | the same, one level down |
| `Create_WithAGeofencePolicy_StoresTheCentreAndRadius` | the policy is kept, not silently dropped |
| `Patch_WithAFreshIfMatch_Succeeds_AndTheETagChanges` | the token moves when the row does |
| `Patch_WithAStaleIfMatch_Returns412` | the `ETag` guard, as SPEC-014 established |
| `Patch_WithNoIfMatch_Returns412` | an absent precondition is a failed one |
| `Delete_AsTheOwner_HidesItFromEveryRead` | the soft delete and its query filter |
| `CreateEvent_WritesAnAuditRecord` | ADR-0033 on a new surface |
| `Get_APrivateEventAsAStranger_Returns404` | the `member` gate, and `404` over `403` |
| `Get_APrivateEventAsAnInvitee_Returns200` | an invitation is what makes a private event reachable |
| `Get_APublicEventAsAnyone_Returns200` | subject V.2.1's "all users can find it" |
| `Patch_AsANonOwner_Returns404` | `owner` is not `member`: an invitee holds a real `ETag` and it buys nothing |
| `Uninvite_RemovesTheInviteesAccess` | the gate is a live read, not a snapshot |
| `Invite_AsANonOwner_Returns404` | an invitee cannot bring a friend |
| `ListInvitations_AsAnInvitee_Returns404` | the guest list is the owner's |
| `Invite_ADuplicate_Returns409` | the unique index arbitrates, not a read-then-write check |
| `Invite_TheOwner_Returns409` | the owner already holds the strongest grant |
| `Invite_AnUnknownAccount_Returns404` | an invitation names a real account |
| `ListInvitations_AsTheOwner_ReturnsThem` | the read path |
| `Attend_APublicEventWithoutAnInvitation_Succeeds` | **decision 5 — attendance is not a gate** |
| `Attend_APrivateEventAsAStranger_Returns404` | the invitation is the gate on a private event |
| `Attend_Twice_Returns409` | one attendance per person |
| `LeaveAttendance_ThenAttendAgain_Succeeds` | leaving is not a ban |
| `LeaveAttendance_WithoutAttending_Returns404` | leaving nothing is `404` |
| `EventCommandValidatorTests` (12 cases) | the window, the policy, the radius and the coordinate bounds, and decision 3 from both routes |
| `SchemaConventionTests` (existing) | the three new tables obey the naming rules |

## Discovered during implementation

- **`EventEndpoints` hit the coupling gate (S1200) at 31 types**, exactly as `PlaylistEndpoints` did when the
  track routes were added. Two moves fixed it honestly rather than by suppression: `PUT .../licence` became its
  own class — it is a policy decision, not metadata — and the `ETag` and `If-Match` helpers came out of
  `PlaylistEndpoints` into a shared `ConcurrencyHeaders`. The second is a real improvement: three aggregates now
  read `If-Match` through one implementation, so no route can quietly start accepting a wildcard the others
  refuse.
- **The wire payloads moved to their own file.** `EventEndpoints` would otherwise have been past the
  file-length gate before the roster routes were written.
- **`EventAccess.MayVote` answers only what the row can decide.** For `LocationAndTimeRestricted` that is "may
  see it, and server time is inside the window"; the presence proof is the vote's business. Stating that here
  rather than leaving it implicit is what stops unit 2 from treating the gate as already complete.
- **`EventLicenceRules` is shared by two validators** so that creation and `PUT .../licence` refuse the same
  things. A mode accepted at creation and rejected on update — or the reverse — is a way to reach a state
  neither route intended.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §8 documents the three tables, and §16's V.2.1 visibility and licence rows say what is
      built rather than designed
- [x] `docs/api/ENDPOINTS.md` §4 records decision 3's `422` and the window rule
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] OWASP checklists walked; no new finding — T-17's GPS-spoofing residual risk already covers the geofence,
      and the check it describes arrives with unit 2
- [x] `status: done` in this file
