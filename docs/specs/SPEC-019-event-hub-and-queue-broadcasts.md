---
id: SPEC-019
title: EventHub — the live queue, and one broadcast pipeline for both hubs
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S6              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.1, V.6]
decisions: [ADR-0004, ADR-0019, ADR-0033, ADR-0035, ADR-0036]
standards: [signalr-realtime, clean-architecture-cqrs, authentication-authorization, application-security, rest-api-design, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29. Gate 1 is answered by
  a new record, ADR-0036, which decides what an `EventHub` payload carries and writes down the deviation
  from `signalr-realtime`'s version rule, with its cost. Gate 5 is the second hub: a join must be
  authorised server-side and audited, exactly as `PlaylistHub`'s is.
---

# EventHub, and one broadcast pipeline

## Goal

After this spec the vote-ordered queue is **live**. A client joins `event:{eventId}` over SignalR — a join
authorised server-side against the `member` gate and audited by ADR-0019's filter — and receives
`TrackSuggested`, `VoteChanged` and `QueueReordered` after the transaction that caused them commits.

Third and last unit of slice S6. It also pays off SPEC-016: the queue and the behaviour ADR-0035 built for
one hub become **one pipeline for both**, rather than a second copy for the second hub.

## Out of scope

- **`NowPlayingChanged`.** It belongs to the `PlaybackSession` of ADR-0017, which is slice S7 and has a row
  of its own — so it can carry a version in the literal sense ADR-0036 could not.
- **`ControlHub`.** Slice S7 with the aggregate it serves.
- **A scale-out backplane.** One instance, O8, deliberately open.

## Decisions taken under the blanket approval, 2026-10-01

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A payload carries the authoritative value, not a row version.** Recorded as ADR-0036. `VoteChanged` carries the suggestion's **new count**; `TrackSuggested` and `QueueReordered` carry an id, which is idempotent by itself. | An event's `xmin` is already the `If-Match` of its metadata `PATCH`, so touching the event row on every vote would stop the owner renaming an event during the evening it runs. A `queue_version` column would reintroduce the shared mutable count SPEC-018 deliberately removed. For a count, state beats a delta anyway: a client that missed three messages and receives `votes: 7` is correct |
| 2 | **One queue, one behaviour, two hubs.** `IPlaylistBroadcastQueue` becomes `IBroadcastQueue` over a `RealtimeBroadcast` base, and `BroadcastBehaviour` drains it through one `IRealtimeBroadcaster` that routes by payload type. | A second queue and a second behaviour for the second hub would be the same ordering guarantee written twice, and the next hub would make it three times. The behaviour's position — outside the audit behaviour's transaction — is the guarantee, and it should exist once |
| 3 | **The hub filter audits both hubs**, mapping a method name to an action and a target type per hub. | ADR-0019 says a new hub method is audited because the filter is on the hub, not because somebody remembered. Two filters would make that false the first time one of them was forgotten |
| 4 | **`QueueReordered` is sent when a suggestion is removed**, not on every vote. | A vote already sends `VoteChanged` with the new count, and the order is derived from counts the client holds. Sending both would be sending the same fact twice |
| 5 | **A join is authorised by the `member` gate**, the same query `PlaylistHub` uses in its own shape, and a reconnect is a new join. | Who may hear about an event is who may see it. Voting is a stricter gate and belongs to the write, which is the only place that can rule on presence |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0036 is new, and it records a scoped deviation from `signalr-realtime`'s payload rule with the reason. ADR-0035's posture is extended, not changed |
| 2 | new or upgraded dependency? | no — SignalR is in the shared framework and the test client arrived with SPEC-016 |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration at all |
| 4 | changes `docs/api/ENDPOINTS.md`? | §9's `EventHub` rows become implemented; design notes record ADR-0036's payload rule |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — a second place a group membership is authorised, and a second pair of audited hub actions |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/IRealtimeBroadcaster.cs` | create — the port and the `RealtimeBroadcast` base, replacing the playlist-only pair |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/IBroadcastQueue.cs` | create — the request-scoped queue, renamed from the playlist one |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/PlaylistBroadcasts.cs` | change — the four playlist payloads, now deriving from the shared base |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/EventBroadcasts.cs` | create — `TrackSuggested`, `VoteChanged`, `QueueReordered` |
| `MusicRoom/src/MusicRoom/Application/Pipelines/Realtime/BroadcastBehaviour.cs` | change — one behaviour, one port |
| `MusicRoom/src/MusicRoom/Application/ApplicationServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/*` (5 handlers) | change — enqueue through the renamed queue |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/SuggestTrack/*` | change — enqueue `TrackSuggested` |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/CastVote/*` | change — enqueue `VoteChanged` with the new count |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Presence/EventVoteGate.cs` | create — the licence and the proof as one question, against S107 |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/WithdrawVote/*` | change — enqueue `VoteChanged` with the new count |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/RemoveSuggestion/*` | change — enqueue `QueueReordered` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IEventQueueRepository.cs` | change — a vote write reports the suggestion's new count |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/EventQueueRepository.cs` | change — count after the write, in the same transaction |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Queries/AuthoriseJoin/*` | create — the `member` gate for a hub join |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/EventHub.cs` | create — `JoinEvent`, `LeaveEvent`, the handshake contract |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubAuditFilter.cs` | rename + change — was `PlaylistHubFilter`; one filter, both hubs, an action **and** a target type per method |
| `MusicRoom/src/MusicRoom/WebAPI/Realtime/RealtimeBroadcaster.cs` | create — routes a payload to its hub, replacing the playlist-only sender |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the second hub, register the router, read the token on its path |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/ClientContextMiddleware.cs` | unchanged — `/hubs` already covers both |
| `MusicRoom/tests/MusicRoom.Application.Tests/Pipelines/BroadcastBehaviourTests.cs` | change — the renamed types |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubClients.cs` | change — build a connection to either hub |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventHubTests.cs` | create — the join gate and the audit rows |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventBroadcastTests.cs` | create — each event reaches a joined client |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/decisions/ADR-0039-queue-broadcast-payloads.md`, `decisions-index.md` | create/change |
| `brain/projects/musicroom-backend.md` | change — S6 complete |
| `docs/specs/SPEC-019-event-hub-and-queue-broadcasts.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** The port, the queue, the payloads and the behaviour are `Application`; the hubs, the
filter and the router are `WebAPI`. `Application` still references no SignalR type — if it did, the layer
boundary would be decoration.

**2. Boundary types.** `IRealtimeBroadcaster`, `IBroadcastQueue`, `RealtimeBroadcast` and the seven payloads
are `public`; `EventHub` is `public` because `MapHub` needs it; the filter and the router are
`internal sealed`.

**3. Invoked?** Two hub methods, four handlers enqueueing, one behaviour, one router.

**4. CLI or package.** None.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **This is a rename across SPEC-016's surface**, not an addition beside it. Five playlist handlers, the
  behaviour, the registration and one test file change spelling. That is the cost of decision 2 and it is
  paid once.
- **`VoteChanged` needs the count the write produced**, so `TryCastVoteAsync` and `TryWithdrawVoteAsync`
  must report it. Counting after the write inside the same transaction is the only honest source — counting
  in the handler afterwards would race with the next voter.
- **An unchanged `Unchanged` vote still broadcasts.** A duplicate vote changed nothing, so it sends
  nothing: the count a listener holds is already right.

## Steps

1. Rename the queue and the port to their hub-agnostic names, move the playlist payloads onto the shared
   base, and update the five playlist handlers and the behaviour test.
2. `EventBroadcasts`: `TrackSuggested`, `VoteChanged`, `QueueReordered`, each with the event id and the
   instant, and `VoteChanged` with the count.
3. `IEventQueueRepository`: the two vote writes report the suggestion's new count, counted in the same
   transaction.
4. The four event handlers enqueue after a successful write, and nothing after a refused or unchanged one.
5. `AuthoriseEventJoinQuery`: the `member` gate, asked through CQRS so the hub holds no data access.
6. `EventHub` with `JoinEvent` and `LeaveEvent`, `[Authorize]`, and the handshake contract the middleware
   already enforces for `/hubs`.
7. `HubAuditFilter`: one filter, both hubs, a method name mapped to an action and a target type.
8. `RealtimeBroadcaster`: route a payload to `playlist:{id}` or `event:{id}` by its type.
9. `Program.cs`: map the second hub and read `access_token` on its path.
10. Tests, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Connect_WithATokenAndClientContext_Succeeds` | the happy path, so the refusals below mean something |
| `Connect_WithoutClientContext_IsRefused` | the handshake rule covers the second hub, through the same middleware |
| `Connect_WithoutAToken_IsRefused` | the hub is not an unauthenticated side door |
| `Join_APublicEvent_Succeeds` | and it admits who it should |
| `Join_APrivateEventTheCallerMayNotSee_IsRefused` | the `member` gate, on the hub, saying no more than the REST `404` |
| `Join_WritesAnAuditRecord` · `Join_WhenRefused_WritesARefusedAuditRecord` · `Leave_WritesAnAuditRecord` | ADR-0019 on `event.join` and `event.leave`, with the handshake's client context |
| `Suggest_ReachesAJoinedClient` | `TrackSuggested` |
| `Vote_ReachesAJoinedClient_WithTheNewCount` | **decision 1 — the payload is the value, not a delta** |
| `Withdraw_ReachesAJoinedClient_WithTheLoweredCount` | the count goes down as honestly as it goes up |
| `RemoveSuggestion_ReachesAJoinedClient` | `QueueReordered` |
| `ARefusedVote_SendsNothing` | a `422` is not an event |
| `ADuplicateVote_SendsNothing` | one message for the vote that happened, none for the one that did not |
| `AClientListeningToAnotherEvent_ReceivesNothing` | the group key is the event, not the server |
| `PlaylistBroadcastTests` (6, existing) | the refactor did not break the first hub |
| `BroadcastBehaviourTests` (4, existing) | the ordering guarantee still holds, once, for both |

## Discovered during implementation

- **The shared base had to be an interface.** An abstract record with one abstract member is S1694, and an
  empty one is S2094; `IRealtimeBroadcast` carries `AggregateId` and nothing else, which is exactly what the
  two hubs have in common. The group's shape stays in `WebAPI`, so `Application` still names no SignalR type.
- **The vote handler reached six constructor parameters**, which S107 refuses. `EventVoteGate` came out of it:
  the licence check and the presence proof are one question asked in two halves, and putting them in one type
  means no future caller can ask one and believe it asked both. The same reason `SessionIssuer` exists.
- **Three broadcast tests failed on a race in the test, not the server.** A message from an earlier write
  travels asynchronously, so a listener attached *after* that write can still receive it — and a test that
  waited for "the next `VoteChanged`" read the previous one. Every listener is now registered before the act
  and waits for the message it actually means (`votes == 2`), or counts every message and asserts how many were
  sent. The product behaviour was right the whole time, which is why the fix is in the suite.
- **A withdrawal reports `Recorded`, not a separate outcome.** `VoteWriteResult` already says what happened and
  what the count is now; a third enum value for "removed" would have been a second spelling of the same fact.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §9.2 and §16's V.2.1 queue row say the queue is live
- [x] `docs/api/ENDPOINTS.md` §9 records ADR-0036's payload rule and the implemented `EventHub` rows
- [x] ADR-0036 written, indexed, and in `docs/SDD.md` §15
- [x] `brain/projects/musicroom-backend.md` says S6 is complete
- [x] `status: done` in this file
