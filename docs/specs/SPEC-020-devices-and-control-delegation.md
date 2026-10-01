---
id: SPEC-020
title: Devices and control delegation — one row, one state machine, one token
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S7              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.2, V.6]
decisions: [ADR-0017, ADR-0028, ADR-0033]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29 with the seven decisions
  below. Gate 1 is a reading of ADR-0017 rather than a change to it: that ADR says a control grant is what
  changes `controllerUserId` on a playback session, and decision 1 is where the grant itself lives so that
  `/devices/{id}/control` works before any session exists. Decision 5 is the load-bearing one — two
  simultaneous claims, one `200`, one `412`, and the loser loses visibly.
---

# Devices and control delegation

## Goal

After this spec subject V.2.2's **first half** exists: a device registered to an account, a control grant
to a friend, a revoke, a release, and the list of grants a caller holds. Seven rows of
`docs/api/ENDPOINTS.md` §6, with the race a peer evaluator will reach for — two people claiming one device at
once — resolved by the database and proven by a parallel test.

First unit of slice S7, and the same shape every aggregate in this repository has had: build the state and its
gates, prove the race, then build what reads it.

## Out of scope

- **`PlaybackSession`.** The second unit: the session, the queue advance with `expectedNowPlayingId`, the
  explicit jump, and `NowPlayingChanged`. Until it exists a control grant decides *who may command playback*
  and there is nothing yet to command, which is the honest order — ADR-0017 says the session reads the grant,
  not the reverse.
- **`ControlHub`.** With the session, for the same reason `EventHub` waited for the queue.
- **A watchdog for a silent controller.** ADR-0017 rejected it and said why: takeover and revoke are the manual
  paths, and a server-side timer reintroduces the drift that decision avoids.

## Decisions taken under the blanket approval, 2026-10-01

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **Control state lives on the `devices` row**: a nullable `controller_user_id` and PostgreSQL's `xmin`. | The contract's own words — "a single row with a state machine and a concurrency token". Rejected: a separate `device_control_grants` table, which is two rows that can disagree about who controls one device; and holding it only on the playback session, which would leave `/devices/{id}/control` unusable before playback starts and give ADR-0017's grant nowhere to live between sessions |
| 2 | **Null means the owner controls.** A revoke writes null, not the owner's id. | Two spellings of one state is how a query ends up checking only one of them. Null is also the honest answer to "who was delegated control": nobody |
| 3 | **A device is unique per `(owner_id, platform, model)`**, `409` otherwise. | A phone that updates its app is the same phone. Including `app_version` in the key would register a new device per release and quietly fill the table with ghosts of one handset |
| 4 | **A grantee must be a friend of the owner, else `422`.** Checked server-side against the friends graph SPEC-012 built. | The contract's rule. Delegating playback to a stranger is a feature nobody asked for and an abuse surface somebody would find |
| 5 | **`PUT .../control` requires `If-Match` and is arbitrated by a conditional `UPDATE`.** Two simultaneous claims: one `200`, one `412`. | Subject V.2.2's race. The loser must lose **visibly** — a silent overwrite means two clients each believe they hold control, which is the failure mode this endpoint exists to prevent. Rejected: last-write-wins, and a read-then-write check in C# |
| 6 | **`DELETE .../control` is the owner's revoke and the controller's release**, both `204`, both returning the row to owner control. | One route for two legitimate intents with the same effect. A separate release route would be a second way to write the same state |
| 7 | **Registration reads `platform`, `model` and `app_version` from the mandatory client headers**, not from the body. | `X-Client-Platform`, `X-Client-Device` and `X-Client-App-Version` are already required on every mutation (SDD §10.7) and already bounded to 64 characters. A body that repeats them is a second source that can disagree with the audit record written for the same request. The contract's design note says exactly this; the body column in its table is now the header list |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes, as a reading** — ADR-0017 says a control grant changes a session's `controllerUserId`; decision 1 settles where the grant lives so that it can exist before a session does. Nothing in that ADR is contradicted, and no new record is needed |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — one new table, additive, no backfill |
| 4 | changes `docs/api/ENDPOINTS.md`? | §6's seven rows become implemented; the `POST /devices` body column becomes the header list (decision 7) and the design notes record the `409` key |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — a new ownership gate, a delegation that hands one account control over another's device, and a race that must be lost visibly |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/Device.cs` | create — owner, the three client values, `controller_user_id`, `xmin` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IDeviceRepository.cs` | create — register, list, remove, read control, claim, release |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/DeviceContracts.cs` | create — the access read and the outcome types |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Commands/RegisterDevice/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Commands/RemoveDevice/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Commands/GrantControl/*` | create — the guarded claim, with the friendship check |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Commands/ReleaseControl/*` | create — revoke and release |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Queries/ListDevices/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Queries/GetDeviceControl/*` | create — with the `ETag` the claim sends back |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Queries/ListControlGrants/*` | create — grants held by the caller |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/DeviceConfiguration.cs` | create — `ux_devices_owner_id_platform_model` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/DeviceRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — one `DbSet` |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_Devices.cs` + `.Designer.cs` + snapshot | create/change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/DeviceEndpoints.cs` | create — registration, list, removal |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/DeviceControlEndpoints.cs` | create — the three control routes and the grant list |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map them |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — `CreateAuditedClient` takes an optional app version, for the update test |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Devices/DeviceCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/DeviceRequests.cs` | create — the requests the three suites share |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/DeviceEndpointTests.cs` | create — registration, the `409`, the list, removal |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/DeviceControlEndpointTests.cs` | create — the gates, the friendship rule, revoke and release |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/DeviceControlConcurrencyTests.cs` | create — **twenty simultaneous claims** |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/migrations/migrations-index.md` | change |
| `brain/projects/musicroom-backend.md` | change — S7 started |
| `docs/specs/SPEC-020-devices-and-control-delegation.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Entity in `Domain`; abstraction, commands and queries in `Application`; configuration and
repository in `Persistence`; endpoints in `WebAPI`. No new project, every reference inward.

**2. Boundary types.** `Device`, `IDeviceRepository`, the commands, the queries and the outcome types are
`public`; handlers, the configuration and the repository `internal sealed`.

**3. Invoked?** Seven routes, one repository.

**4. CLI or package.** `dotnet ef migrations add` only.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **`ConcurrencyHeaders` already exists** (SPEC-017), so the `ETag` and `If-Match` of this aggregate cost
  nothing new and cannot drift from the other two.
- **The friendship check is a read of somebody else's graph**, so it belongs in the handler and not in a
  validator: a validator cannot reach the database, and faking it would make the `422` a guess.
- **`Platform` is an enum in SDD §6.2** (`iOS | Android | Web | Other`) but arrives as a free string in a
  header the audit log stores verbatim. The device row keeps the string, and the enum stays what it is today:
  a documented vocabulary, not a constraint the header path enforces. Recorded here rather than silently
  resolved one way or the other.

## Steps

1. `Device` with the three client values, `controller_user_id`, `xmin` and its configuration:
   `ux_devices_owner_id_platform_model`, `ix_devices_controller_user_id`, cascade from `users` on both sides.
   One additive migration.
2. `IDeviceRepository`: register (`409` from the index), list own, remove own, read control with the version,
   `TryGrantControlAsync` (guarded by `xmin`, owner only), `TryReleaseControlAsync` (owner or controller).
3. The friendship check in the grant handler, against `IFriendshipRepository`.
4. Endpoints: registration from the headers, the three control routes with `If-Match` on the claim, and the
   grant list.
5. Tests, including twenty simultaneous claims, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Register_ThenList_ReturnsTheDevice` | the aggregate round-trips, and the values come from the headers |
| `Register_TheSameDeviceTwice_Returns409` | decision 3, arbitrated by the index |
| `Register_AfterAnAppUpdate_IsTheSameDevice` | decision 3's point: `app_version` is not part of the key |
| `Register_WritesAnAuditRecord` | ADR-0033 on a new surface |
| `List_ReturnsOnlyTheCallersDevices` | the `self` gate |
| `Remove_AsTheOwner_Succeeds` · `Remove_AnotherAccountsDevice_Returns404` | removal, and that it is the owner's alone |
| `GetControl_WithNoGrant_ReportsTheOwner` | decision 2 — null means the owner |
| `GetControl_AsAStranger_Returns404` | the control state is not public |
| `GrantControl_ToAFriend_Succeeds` | the delegation subject V.2.2 asks for, and the controller can then read the state |
| `GrantControl_ToAStranger_Returns422` | decision 4, checked server-side |
| `GrantControl_AsANonOwner_Returns404` | only the owner delegates — **and the `404` comes before the friendship rule, so a non-owner learns nothing** |
| `GrantControl_WithAStaleIfMatch_Returns412` · `WithNoIfMatch_Returns412` | decision 5, on one caller |
| `ReleaseControl_AsTheOwner_ReturnsItToTheOwner` | revoke |
| `ReleaseControl_AsTheController_ReturnsItToTheOwner` | release — decision 6's other half |
| `ReleaseControl_AsAThirdParty_Returns404` · `WithNoGrant_Returns404` | and nobody else, and nothing to release is `404` |
| `ListControlGrants_ReturnsWhatTheCallerHolds` | the `self` read of §6's last row, and that an owner holds no grant over their own device |
| `GrantControl_TwentySimultaneousClaims_ProduceOneWinner` | **subject V.2.2's race — one `200`, nineteen `412`, and one controller in the database, who is one of the twenty** |
| `ReleaseControl_TwentyParallelReleases_LeaveItWithTheOwner` | one `204`, nineteen `404`, and the device is the owner's |
| `DeviceCommandValidatorTests` (3 cases) | the input rules |
| `SchemaConventionTests` (existing) | the new table obeys the naming rules |

## Discovered during implementation

- **The gate must be checked before the payload rule.** The first draft asked the friendship question first, so a
  **non-owner** received `422` telling them their intended grantee was not a friend of theirs — a `404` route
  answering a question about the caller's own social graph on somebody else's device. Ownership is now read
  first, and only an established owner is told why a grantee was refused. A test pinned the wrong status before
  the fix, which is how it was found.
- **`TestBearer.SignInAsync` can be called once per address.** It asserts that registration produced a mail
  token, and a second registration of an existing address answers `202` and sends nothing — correctly, because
  it must not be an enumeration oracle. Two suites signed the same account in twice and failed inside the helper
  with `Expected: 3, Actual: 2`; they now pass the client around instead.
- **This branch was cut from `main` rather than from SPEC-019.** `main` had moved when the branch was created, so
  the first build failed on a `DbContext` that knew nothing about events. The branch was moved onto the SPEC-019
  tip before any commit, so the stack is intact — recorded because the next unit should check its base first.
- **`Platform` stays a string on the device row.** SDD §6.2 lists it as an enum, and it arrives in a header the
  audit log already stores verbatim; constraining it here would make the two disagree. Noted rather than
  resolved in one direction.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §8 documents the table and §16's two V.2.2 rows say what is built
- [x] `docs/api/ENDPOINTS.md` §6 records decision 7's header source and decision 3's key
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] OWASP checklists walked; no new finding — delegation is bounded to friends and to one row, and T-17's
      spoofing risk does not touch this surface
- [x] `status: done` in this file
