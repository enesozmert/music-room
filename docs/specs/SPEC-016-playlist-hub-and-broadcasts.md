---
id: SPEC-016
title: PlaylistHub — a live playlist, authorised on join, audited, and broadcast only after the commit
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S5              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.3, V.6]
decisions: [ADR-0004, ADR-0019, ADR-0028, ADR-0033, ADR-0035]
standards: [signalr-realtime, clean-architecture-cqrs, authentication-authorization, application-security, rest-api-design, dotnet-csharp, testing, logging-observability]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1, 2 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29. Gate 1 is
  answered by a new record, ADR-0035, which decides where a post-commit broadcast comes from; gate 2 is
  one Microsoft-owned test-only package, `Microsoft.AspNetCore.SignalR.Client`, on the same grounds as
  the three earlier amendments to ADR-0021's set.
---

# PlaylistHub and the broadcasts

## Goal

After this spec a playlist is **live**. A client joins `playlist:{playlistId}` over SignalR, is authorised
server-side on that join, and receives `TrackAdded`, `TrackMoved`, `TrackRemoved` and
`CollaboratorChanged` — each carrying the playlist's new version — as soon as, and only after, the
transaction that caused it has committed. Join and leave write the same audit record a REST mutation
does, which is what makes subject V.6's "every action" true across both transports.

Third and last unit of slice S5. It closes the realtime half of subject V.2.3: SPEC-015 made concurrent
editing *correct*, and this makes it *visible*.

## Out of scope

- **`EventHub` and `ControlHub`.** Their aggregates do not exist: track suggestions and votes are slice
  S4's, control delegation is S6's. Both hubs stay in `docs/SDD.md` §9.2 as design.
- **Mutation over the hub.** `docs/api/ENDPOINTS.md` §10 lists it as deliberately absent, and §9 says
  why: one authorisation and validation path. The hub carries joins, leaves and server-pushed facts.
- **A scale-out backplane.** One instance, O8, deliberately open.
- **A durable outbox.** ADR-0035 accepts that a broadcast can be lost and says why.
- **Playlist rename, licence and delete broadcasts.** `docs/SDD.md` §9.2 lists four events for this hub
  and none of them is a metadata change. No contract row, no event.

## Decisions taken under the blanket approval, 2026-10-01

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A handler enqueues a broadcast; a behaviour registered between validation and auditing sends it.** Recorded as ADR-0035. | `AuditBehaviour` owns the transaction, so a handler has no post-commit moment of its own — broadcasting there announces state a rollback can still take away. A behaviour *outside* that transaction has one by construction. Rejected: broadcasting from the handler (uncommitted), from the endpoint (repeated per route, and the hub then only hears about REST), and a durable outbox (a table and a worker, out of proportion to one instance) |
| 2 | **Every payload carries the playlist's new `xmin` as `version`.** `TrackWriteResult` gains it, read inside the same transaction as the write. | `signalr-realtime` requires a monotonic version so a client that missed a message can detect the gap and resynchronise over REST. The `xmin` is the token the `ETag` already carries, so the hub and REST speak the same version and no second counter can disagree with the first |
| 3 | **Client context is read from the handshake's query string, and a handshake without all three values is refused.** | ADR-0019 requires it, and a hub invocation carries no HTTP headers of its own. A WebSocket handshake carries a query string, which is the only place a mobile client can reliably put these values. The refusal mirrors the `400` a REST mutation gets for the same omission, so there is no transport where an unattributable action is possible |
| 4 | **The bearer token is accepted from the `access_token` query parameter on the hub path only.** | The documented SignalR mechanism; browsers cannot set an `Authorization` header on a WebSocket. Narrowed to one path so no REST route gains a second way to authenticate, which would be a new attack surface for the sake of one transport. Serilog's request logging records the path and not the query string, so the token does not land in the log |
| 5 | **Join and leave are audited as `playlist.join` and `playlist.leave`; connect and disconnect are not.** | ADR-0019, verbatim. Mobile networks reconnect constantly, and auditing transport noise would bury the deliberate actions V.6 is asking about |
| 6 | **A send that fails is logged and swallowed.** | The database is the arbiter. A notification that could not be delivered must not undo an action that committed, and the client's own reconcile-on-version-gap rule is the recovery path |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0035 records decision 1. ADR-0019 and ADR-0004 are implemented, not changed |
| 2 | new or upgraded dependency? | **yes** — `Microsoft.AspNetCore.SignalR.Client` 10.0.12, **test projects only**, so a test can be a real client. Microsoft-owned, same version as the other ASP.NET Core packages. SignalR itself is in the shared framework and needs no package |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration at all |
| 4 | changes `docs/api/ENDPOINTS.md`? | §9's four rows are implemented for `PlaylistHub`; design notes gain the hub path, the query-string contract and the version rule |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — a second place a token is accepted, a second place a group membership is authorised, and a second writer of audit records |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/IPlaylistBroadcaster.cs` | create — the transport-free send port, and the four payloads |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/IPlaylistBroadcastQueue.cs` | create — the request-scoped queue, plus its in-memory implementation |
| `MusicRoom/src/MusicRoom/Application/Pipelines/Realtime/BroadcastBehaviour.cs` | create — drains the queue after the inner pipeline commits |
| `MusicRoom/src/MusicRoom/Application/ApplicationServiceRegistration.cs` | change — the behaviour between validation and auditing, and the scoped queue |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaylistTrackRepository.cs` | change — `TrackWriteResult` carries the new version |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaylistTrackWriter.cs` | change — read the playlist's new `xmin` after the write |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/AddTrack/AddTrackCommandHandler.cs` | change — enqueue `TrackAdded` |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/MoveTrack/MoveTrackCommandHandler.cs` | change — enqueue `TrackMoved` |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/RemoveTrack/RemoveTrackCommandHandler.cs` | change — enqueue `TrackRemoved` |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/AddCollaborator/AddCollaboratorCommandHandler.cs` | change — enqueue `CollaboratorChanged` |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/RemoveCollaborator/RemoveCollaboratorCommandHandler.cs` | change — enqueue `CollaboratorChanged` |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Queries/AuthoriseJoin/*` | create — the query the hub dispatches instead of reading data itself |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/PlaylistHub.cs` | create — `JoinPlaylist`, `LeavePlaylist`, and the handshake contract |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/PlaylistHubFilter.cs` | create — ADR-0019's filter: the audit record for join and leave |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubCaller.cs` | create — the per-invocation holder the two accessors read when there is no `HttpContext` |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubClientContext.cs` | create — the handshake's three values and the query names that carry them |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/ClientContextMiddleware.cs` | change — the handshake is refused by the same rule and the same `400` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaylistRepository.cs`, `Persistence/Repositories/PlaylistRepository.cs` | change — read the version a collaborator broadcast carries |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/HttpClientContextAccessor.cs` | change — fall back to the hub holder |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/HttpCurrentUserAccessor.cs` | change — fall back to the hub holder |
| `MusicRoom/src/MusicRoom/WebAPI/Realtime/PlaylistBroadcaster.cs` | create — the port, over `IHubContext<PlaylistHub>` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — `AddSignalR`, the filter, the broadcaster, `MapHub`, and the hub-path token reader |
| `MusicRoom/Directory.Packages.props` | change — the test-only SignalR client |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/MusicRoom.WebAPI.Tests.csproj` | change — reference it |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubClients.cs` | create — a real SignalR client against the test host |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistHubTests.cs` | create — the handshake, the join gate, the audit row |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistBroadcastTests.cs` | create — a joined client receives each event; a refused write sends nothing |
| `MusicRoom/tests/MusicRoom.Application.Tests/Pipelines/BroadcastBehaviourTests.cs` | create — sends after the inner pipeline, discards on a throw |
| `brain/decisions/ADR-0038-realtime-broadcast-posture.md`, `brain/decisions/decisions-index.md` | create/change |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `docs/owasp/THREAT-REGISTER.md` | change — the token in a query string, and T-12 |
| `brain/projects/musicroom-backend.md` | change — S5 complete |
| `docs/specs/SPEC-016-playlist-hub-and-broadcasts.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** The port, the queue and the behaviour are `Application`; the version comes from
`Persistence`; the hub, the filter, the holder and the `IHubContext` implementation are `WebAPI`, because
SignalR is a transport. No new project, every reference inward. `Application` gains no SignalR reference —
if it did, the layer boundary would be decoration.

**2. Boundary types.** `IPlaylistBroadcaster`, `IPlaylistBroadcastQueue`, the four payloads and
`BroadcastBehaviour` are `public`; the hub is `public` because `MapHub` needs it; the filter, the holder
and the broadcaster are `internal sealed`.

**3. Invoked?** Two hub methods, one behaviour in the pipeline, five handlers enqueueing.

**4. CLI or package.** One test-only package, gate 2.

**5. corePackages.** Unchanged. A broadcast queue is arguably generic; it stays out while one hub uses it.

**What the walk caught.** Three things.
- **A handler has no post-commit moment** — ADR-0033 gave the transaction to a behaviour, so the send
  belongs to a behaviour too. This is decision 1, and it is the reason this spec needed an ADR.
- **`ICurrentUserAccessor` reads `HttpContext`**, which a hub invocation does not have. Hence the
  per-invocation holder, set by the filter, read by both accessors as a fallback. The alternative — a
  second accessor abstraction — would let a handler be authorised differently depending on its transport,
  which is exactly what `signalr-realtime` forbids.
- **`Program.cs` is at CA1506's edge.** The hub wiring goes in its own class, as `CallerServices` and
  `PlaylistChildRoutes` already do.

## Steps

1. `IPlaylistBroadcaster` with `TrackAdded`, `TrackMoved`, `TrackRemoved` and `CollaboratorChanged`
   payloads, each carrying the playlist id and the version.
2. `IPlaylistBroadcastQueue` and its scoped implementation: enqueue, drain once.
3. `BroadcastBehaviour` between `ValidationBehaviour` and `AuditBehaviour`. Drain after the inner
   pipeline returns; discard on a throw; log and swallow a send failure.
4. `TrackWriteResult.Version`: the writer reads the playlist's `xmin` after its own update, inside the
   transaction, and the five handlers enqueue on a written outcome only.
5. `AuthorisePlaylistJoinQuery`: the `member` gate of SPEC-014, asked through CQRS so the hub holds no
   data access.
6. `PlaylistHub` with `JoinPlaylist` and `LeavePlaylist`, `[Authorize]`, and `OnConnectedAsync` refusing a
   handshake without `platform`, `device` and `appVersion`.
7. `PlaylistHubFilter`: set the per-invocation holder, dispatch, write the audit record — the same shape
   the REST behaviour writes, with the outcome the method produced.
8. `PlaylistBroadcaster` over `IHubContext<PlaylistHub>`, group `playlist:{playlistId}`.
9. `Program.cs`: `AddSignalR` with explicit `KeepAliveInterval` and `ClientTimeoutInterval`, the filter,
   the broadcaster, `MapHub<PlaylistHub>("/hubs/playlists")`, and `OnMessageReceived` reading
   `access_token` for that path only.
10. Tests, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Connect_WithATokenAndClientContext_Succeeds` | the happy path, so the refusals below mean something |
| `Connect_WithoutClientContext_IsRefused` | decision 3, ADR-0019's handshake rule, answered `400` by the REST middleware |
| `Connect_WithoutAToken_IsRefused` | the hub is not an unauthenticated side door |
| `Join_AsTheOwner_Succeeds` | the group is joinable by somebody who may see the playlist |
| `Join_APlaylistTheCallerMayNotSee_IsRefused` | the `member` gate holds on the second transport, and says no more than the REST `404` |
| `Join_WritesAnAuditRecord` | decision 5, ADR-0019 — with the platform, device and app version from the handshake |
| `Join_WhenRefused_WritesARefusedAuditRecord` | a refused hub action is recorded as `refused`, like a refused REST mutation |
| `Leave_WritesAnAuditRecord` | the same, on the way out |
| `AddTrack_ReachesAJoinedClient_WithTheNewVersion` | decision 2 — and the version in the message is the `ETag` the next write must send |
| `MoveTrack_ReachesAJoinedClient` | the event subject V.2.3 asks for |
| `RemoveTrack_ReachesAJoinedClient` | the third track event |
| `AddCollaborator_ReachesAJoinedClient` | `CollaboratorChanged` |
| `AStaleWrite_SendsNothing` | a `412` is not an event: the queue is drained only for an action that happened |
| `AClientWhoLeft_ReceivesNothing` | group membership means what it says |
| `BroadcastBehaviourTests.Handle_WhenTheInnerPipelineReturns_SendsWhatWasQueued` | decision 1 — and nothing is sent while the inner pipeline is still running |
| `BroadcastBehaviourTests.Handle_WhenTheInnerPipelineThrows_SendsNothing` | a rollback cannot be unsent, so it is never sent |
| `BroadcastBehaviourTests.Drain_Twice_YieldsTheFactsOnce` | no fact is sent twice |

## Discovered during implementation

- **A SignalR negotiate is a `POST` with no client headers**, so `ClientContextMiddleware` answered the
  handshake `400` before the hub could run. Rather than exempt the path, the middleware now *enforces* the
  rule there from the query string: one place decides what a client must say about itself, and ADR-0019's
  "mirroring the `400`" became literally the same `400`. The hub keeps its own check, so a misconfigured
  pipeline cannot produce unattributable rows.
- **A refusal in `OnConnectedAsync` does not reliably fail the client's `StartAsync`** over long polling, which
  is the second reason the handshake check belongs in the middleware: a refusal that the client cannot observe
  is not a refusal.
- **SignalR reports the C# method name, not the wire name.** The audit mapping matched the published
  `JoinPlaylist` and so recorded nothing until it also matched `JoinPlaylistAsync` — exactly the silent gap
  ADR-0019 exists to close. The naming rule requires the suffix, the `HubMethodName` attribute keeps the
  contract, and the filter trims the suffix before matching.
- **`TrackWriteResult` had to carry the version.** The payload needs a monotonic token, and the only honest
  one is the `xmin` the write itself produced, read inside the same transaction. Two places now read it; both
  read the same column, so the hub and REST cannot disagree.
- **Two analyzer-driven moves.** `app.MapHub<PlaylistHub>` pushed `Main` to 42 coupled types (CA1506), so the
  mapping lives in `RealtimeServices`; a `catch (Exception)` around the send would have tripped S2221, so the
  broadcaster catches the two types a send actually fails with and documents why an unexpected one propagates.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §9.2 and §16's V.2.3 rows say `PlaylistHub` is built, not designed
- [x] `docs/api/ENDPOINTS.md` §9 carries the hub path, the handshake contract and the version rule
- [x] ADR-0035 written, indexed, and in `docs/SDD.md` §15
- [x] OWASP checklists walked; the token-in-query-string decision recorded in the register as T-30
- [x] `brain/projects/musicroom-backend.md` says S5 is complete
- [x] `status: done` in this file
