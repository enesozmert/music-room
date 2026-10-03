---
id: SPEC-022
title: ControlHub — telling both sides that control moved
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S7              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.2, V.6]
decisions: [ADR-0004, ADR-0017, ADR-0019, ADR-0038, ADR-0039]
standards: [signalr-realtime, clean-architecture-cqrs, authentication-authorization, application-security, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gate 5 answers "yes" — a third hub, and a group key that is a person rather than a shared aggregate. No new
  ADR: this implements SDD §9.2's third hub under ADR-0019's auditing rule and ADR-0038's post-commit posture,
  with the payload shape ADR-0039 already decided. Decision 2 is the load-bearing one, and it is a reading of
  SDD §10.5 step 4: a grant tells **both** sides, because the owner's other clients are as wrong as the
  grantee's until they hear.
---

# ControlHub

## Goal

After this spec subject V.2.2 is complete. When control of a device moves, the people affected are told:
the account that gained it, the account that lost it, and the owner whose device it is. `ControlHub` at
`/hubs/control`, with the three events SDD §9.2 names — `ControlGranted`, `ControlRevoked`,
`ControlTakenOver` — and `GET /api/v1/devices/{id}/control` remains the read a client reconciles against.

Third and last unit of slice S7.

## Out of scope

- **A watchdog for a silent controller.** ADR-0017 rejected it, SDD §11.2 repeats it, and SPEC-021 did not
  build it. Takeover and revoke are the manual paths.
- **Broadcasting playback commands.** `NowPlayingChanged` already goes to the source's hub (SPEC-021); a
  delegate's advance is not a control change.

## Decisions taken under the blanket approval, 2026-10-03

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The group is the listener, not the thing listened to**: `user:{userId}`, joined automatically on connect from the validated token. The hub has **no invocable method**. | A client cannot ask to join somebody else's group because it cannot ask at all — the strongest form of the rule `signalr-realtime` states, and the only one available when the group is a person. Rejected: a `JoinControl(userId)` method, which would be a route whose entire job is to be refused for every argument but one |
| 2 | **A control change tells both sides.** A grant sends `ControlGranted` to the grantee **and** to the owner; a revoke or a release sends `ControlRevoked` to both; a grant that displaces an existing controller additionally sends `ControlTakenOver` to the one displaced. | SDD §10.5 step 4 says "notifies `user:{ownerId}` and `user:{granteeId}`". The owner's *other* clients are as wrong as the grantee's until they hear — the one that made the call got the `200`, and a phone in another pocket did not. `ControlTakenOver` is a separate event because "you have control" and "you no longer have control" are different facts that must not be inferred from each other's absence |
| 3 | **The grant write reports who held control before it.** `TryGrantControlAsync` answers with the previous controller, read in the same statement that replaced it. | Without it the handler would read, then write, then guess who to tell — and a concurrent grant between those two reads would make it tell the wrong person. `ExecuteUpdateAsync` cannot return a row, so the previous value is read **inside** the guarded transaction, before the update, keyed on the same `xmin`: the token already proves nothing moved in between |
| 4 | **No row version in a control payload.** Each carries the device id and the accounts involved. | ADR-0039's rule. A device's `xmin` is the `If-Match` of its claim route, and a listener who never holds that token would learn nothing from it. The authoritative value is "who controls this device now", which the payload states outright, and `GET .../control` is the reconcile point |
| 5 | **Nothing here is audited, and that is ADR-0019's own answer.** | That ADR audits `JoinEvent`, `LeaveEvent`, `JoinPlaylist` and `LeavePlaylist` — hub **methods**, which are client actions — and explicitly excludes connect and disconnect as reconnect noise. This hub has no methods, so it has no client action to record. The control changes themselves are already audited on the REST side as `device.grant-control` and `device.release-control` (SPEC-020) |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0019, ADR-0038 and ADR-0039 cover the auditing, the timing and the payload shape. Decision 2 is a reading of SDD §10.5, written down here |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration at all |
| 4 | changes `docs/api/ENDPOINTS.md`? | §9's `ControlHub` row becomes implemented, with the hub path and the fan-out in the design notes |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — a third hub, a group key derived from the token rather than from a request, and a message that tells one account about another's device |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/ControlBroadcasts.cs` | create — the three payloads |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/DeviceContracts.cs` | change — a grant reports the previous controller |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IDeviceRepository.cs` | change — the same |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/DeviceRepository.cs` | change — read the previous controller under the guard |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Commands/GrantControl/GrantControlCommandHandler.cs` | change — enqueue `ControlGranted` ×2 and `ControlTakenOver` |
| `MusicRoom/src/MusicRoom/Application/Features/Devices/Commands/ReleaseControl/*` | change — enqueue `ControlRevoked` ×2 |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/ControlHub.cs` | create — auto-join, no methods |
| `MusicRoom/src/MusicRoom/WebAPI/Realtime/RealtimeBroadcaster.cs` | change — route the three to `user:{id}` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the hub |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubClients.cs` | unchanged — SPEC-019's `path` parameter already covered a third hub |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ControlHubTests.cs` | create — the handshake and the fan-out |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `brain/projects/musicroom-backend.md` | change — S7 complete |
| `docs/specs/SPEC-022-control-hub.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** The payloads in `Application`; the hub and the route in `WebAPI`. No new project, and
`Application` still names no SignalR type.

**2. Boundary types.** The three payloads are `public`; `ControlHub` is `public` because `MapHub` needs it.

**3. Invoked?** No hub method at all — the connection *is* the subscription. Two handlers enqueue.

**4. CLI or package.** None.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **`AggregateId` means "the group" and here that is a person.** The interface's name reads oddly for a user
  group; the payload keeps the device id as a field. Recorded rather than renamed, because renaming it would
  touch every payload in two hubs to make one read better.
- **The grant's previous controller has to come from the write path**, not from a read beside it — decision 3.
- **No migration, no new gate.** This unit adds a transport over state that already exists, which is why it is
  the smallest of the three in S7.

## Steps

1. The three payloads, each carrying the recipient as its group and the device as a field.
2. `DeviceWriteResult`: a grant reports `PreviousControllerUserId`, read inside the guarded transaction.
3. The two handlers enqueue: granted to grantee and owner, taken-over to the displaced, revoked to both.
4. `ControlHub` with `[Authorize]`, the handshake contract, and an automatic group join in
   `OnConnectedAsync`.
5. The broadcaster routes the three to `user:{id}` on the control hub.
6. Tests, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Connect_WithATokenAndClientContext_Succeeds` | the handshake, and that no join call is needed |
| `Connect_WithoutClientContext_IsRefused` | ADR-0019's handshake rule covers the third hub too |
| `Connect_WithoutAToken_IsRefused` | the group key comes from a validated token or not at all |
| `Grant_ReachesTheGrantee` | **decision 2: the account that gained control is told** |
| `Grant_ReachesTheOwner` | and so is the owner, whose other clients were wrong until now |
| `Grant_ThatDisplacesAController_SendsControlTakenOverToThem` | **decision 3 — the displaced controller is named from inside the guarded transaction** |
| `Revoke_ReachesThePreviousController` | `ControlRevoked`, to the person who lost it |
| `Revoke_ReachesTheOwner` | and to the owner |
| `AThirdParty_ReceivesNothing` | the group is the listener: nobody else hears about somebody's device |
| `ARefusedGrant_SendsNothing` | a `422` is not a control change |
| `DeviceControlEndpointTests` (12, existing) | the REST behaviour did not move |

## Discovered during implementation

- **The release handler had to read before it wrote.** The row is about to stop saying who held control, and
  that person is the one to tell. `FindControlAsync` answers only for the owner or the controller, so the read is
  also the gate the write repeats — no extra exposure, one extra query.
- **`ExecuteUpdateAsync` cannot return the row it replaced**, so the previous controller is read inside the same
  guarded transaction, before the update, keyed on the same `xmin`. If the token turns out to be stale the update
  matches nothing and the value is discarded with it, so it can never name somebody a concurrent grant has
  already displaced.
- **Three enqueues, not one payload with a list of recipients.** The group key *is* the recipient, so the
  transport sends one message per person either way; a list would hide that and invite a reader to think one
  message fans out.
- **The branch was cut from `origin/main` and the number claimed there**, per the rule added on 2026-10-03.
  `origin/main` held SPEC-021 and ADR-0039, so this is SPEC-022 and it needed no new ADR. The ADR references in
  this spec are the current ids — ADR-0038 for the broadcast posture, ADR-0039 for the payload shape — which are
  the renumbered forms of what SPEC-016 and SPEC-019 called ADR-0035 and ADR-0036.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §9.2 and §16's V.2.2 rows say `ControlHub` is built
- [x] `docs/api/ENDPOINTS.md` §9 carries the hub path and the fan-out
- [x] `brain/projects/musicroom-backend.md` says S7 is complete
- [x] OWASP checklists walked; no new finding — the group key is the token's own `sub`, so there is no argument to
      abuse, and no new state is stored
- [x] `status: done` in this file
