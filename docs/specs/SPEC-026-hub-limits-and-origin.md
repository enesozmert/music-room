---
id: SPEC-026
title: The hub limits ADR-0030 decided — a connection ceiling, a call limit, and an origin check
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9              # which DELIVERY-PLAN slice this belongs to
requirements: [V.6, V.2.1]
decisions: [ADR-0030, ADR-0019, ADR-0041]
standards: [signalr-realtime, authentication-authorization, application-security, testing]
approved_by: enesozmert # the numbers, 2026-10-04, in ADR-0030
escalation: |
  **Not blocked any more.** This unit waited on numbers ADR-0030 left as `UNKNOWN - requires decision`; the
  owner set them on 2026-10-04 — five concurrent connections per user, twenty per IP, sixty invocations per
  minute per connection. They are implemented as written and not reinterpreted.

  **One thing the ADR assumes that is not true yet:** it says the handshake validates `Origin` against *"the
  same `CORS_ALLOWED_ORIGINS` list REST uses"*. No code reads that variable. REST has had no CORS policy at
  all, so this unit gives it one — otherwise the handshake would compare an origin against nothing and the
  decision would be satisfied in name only. That is scope the ADR implies rather than scope this spec invented,
  and it is the half of O21 the brain-check of 2026-10-02 recorded as the unread variable.

  **Gate 5 answers yes:** connection ceilings and an origin check are authorisation controls on the realtime
  surface.
---

# Hub limits and the handshake origin

## Goal

After this spec the realtime surface carries the three controls [[ADR-0030-hub-rate-limits-and-origin]] decided
and S5 and S6 shipped without: a concurrent connection ceiling per user and per IP, a per-connection invocation
limit, and an `Origin` check on the handshake. Issue **O21** closes and threat **T-12** narrows from "bounded
payloads and timeouts" to a stated limit with a test behind it.

Subject V.6 is the requirement: an authenticated user may reach their own data and not another's, and nothing
in the contract says they may hold a hundred connections or invoke a hub method at request rate while doing it.

## Out of scope

- **A REST rate limit for hub-adjacent writes.** A vote, a reorder and a queue advance are REST calls and
  already carry their own policies (SPEC-006, SPEC-018, ADR-0041). This unit bounds the hub transport.
- **A distributed ceiling.** The counts are per process, exactly like every other limiter here — issue O8. One
  instance is the ceiling ADR-0015 deploys, and the same note applies.
- **Re-measuring the numbers.** ADR-0030 says the first Gatling run after this lands says whether sixty is
  generous or tight. Running it is the next unit, not this one, and `load-testing` wants the change and its
  measurement separated so the comparison means something.
- **A CORS policy wider than the handshake needs.** REST gets the list the ADR refers to and nothing more:
  no wildcard, no credentials-with-any-origin.

## Decisions taken under the approval of 2026-10-04

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The numbers come from ADR-0030 unchanged**: 5 connections per user, 20 per IP, 60 invocations per minute per connection. Configuration may override each one, and a non-positive override is ignored. | They are the owner's, and an implementation that quietly rounded them would make the ADR a suggestion. The override exists so a load run can raise one without a code change; ignoring a non-positive value is the same rule the credential limits use, so configuration cannot switch a ceiling off |
| 2 | **The ceiling is enforced twice: at the handshake and at connect.** The negotiate refuses with `429` when the caller is already at the ceiling; the hub filter refuses the connection itself if one slips past. | SPEC-016 learned that a refusal thrown in `OnConnectedAsync` does not reliably fail a client's `StartAsync` over long polling, so the client-visible refusal belongs in the middleware. But the middleware reads a count it does not own, and two simultaneous negotiates can both pass it — the filter holds the authoritative count and is what makes the ceiling true. Neither alone is both reliable and correct |
| 3 | **The invocation limit lives in the existing audit filter**, keyed by connection id, as a fixed one-minute window. | ADR-0030 says it: one filter, two jobs. A second filter would mean two orderings to reason about, and the audit filter already sees every invocation — a limit installed anywhere else could be bypassed by a hub method added later |
| 4 | **A refused invocation throws `HubException` and is audited `refused`.** | The ADR asks for the `HubException`; the audit record is free, because the filter already records a refused join that way (SPEC-019). A client that is throttled and a client that is not permitted both get a refusal they can act on, and the log says which |
| 5 | **The trip is logged with the hub, the method and the wait, and nothing that identifies the account.** | SPEC-006's rule for a REST limiter trip, applied to the same control on a different transport. The account is in the audit record, which is the place that may name it |
| 6 | **REST gets the `CORS_ALLOWED_ORIGINS` policy, and the handshake validates `Origin` against the same list.** An empty list allows no cross-origin request; a request with **no** `Origin` passes. | The ADR's own wording requires one list, not two. No `Origin` passes because the React Native client is not a browser and sends none — a deliberate hole the ADR records, with the JWT as the control standing behind it. An empty list meaning "none" rather than "all" is the only safe reading of an unset variable |
| 7 | **The ceiling counts connections, not handshakes.** A negotiate that never becomes a connection releases nothing, because it took nothing. | Counting handshakes would let a client exhaust its own ceiling by negotiating and walking away, which is a denial of service against itself and a support ticket against us |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — this *implements* [[ADR-0030-hub-rate-limits-and-origin]], including the numbers it was waiting for |
| 2 | new or upgraded dependency? | no — `AddCors` and the built-in rate-limiting primitives are in the framework |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — the hub section gains the refusals: `429` on a handshake over the ceiling, `403` for a disallowed origin, and a `HubException` on a throttled invocation |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — these are authorisation controls on the realtime surface, and a refused invocation writes an audit record |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubConnectionLimits.cs` | create — the three numbers and the origin list, read once at start-up |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubConnectionRegistry.cs` | create — the per-process counts, and the only place that knows them |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubInvocationLimiter.cs` | create — the per-connection window |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubAuditFilter.cs` | change — the limit and the connect/disconnect bookkeeping, one filter and two jobs |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/ClientContextMiddleware.cs` | change — refuse a handshake over the ceiling or from a disallowed origin |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — the CORS policy, the limit options, the registry |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubLimitTests.cs` | create — the ceiling, the invocation limit and the origin check |
| `.env.example` | change — `CORS_ALLOWED_ORIGINS` documented, and the three limits |
| `docs/api/ENDPOINTS.md` | change — the hub refusals |
| `docs/SDD.md` | change — §9.2, §13.3, §16's V.6 row, §17 closes **O21** |
| `docs/owasp/THREAT-REGISTER.md` | change — T-12 narrows to a stated limit with evidence |
| `docs/DELIVERY-PLAN.md` | change — S9 |
| `brain/projects/musicroom-backend.md` | change — S9 |
| `docs/specs/SPEC-026-hub-limits-and-origin.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** All in `WebAPI`. `Application` gains nothing: a transport limit is not a use case, and no
handler learns that one exists.

**2. Boundary types.** `HubConnectionLimits` is `public` because the composition root builds it. The registry and
the limiter are `internal`.

**3. Invoked?** On every handshake, every connect, every disconnect and every hub invocation.

**4. CLI or package.** None.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **Two enforcement points are not redundancy**, they are a reliability problem and a correctness problem with
  different answers — decision 2.
- **`CORS_ALLOWED_ORIGINS` has never been read.** The ADR's comparison needs a list that exists, so REST gets
  the policy in the same change. Found by the brain-check of 2026-10-02 and left open as part of O21.
- **The registry must release on disconnect or the ceiling leaks.** A client that reconnects five times becomes
  permanently unable to connect, which is a worse bug than the one being fixed — so the release path is what the
  tests are built around.

## Steps

1. `HubConnectionLimits`, read from configuration with ADR-0030's values as the defaults.
2. `HubConnectionRegistry`: try-add and release, keyed by user and by address, per process.
3. `HubInvocationLimiter`: a fixed one-minute window per connection id, and a released entry on disconnect.
4. `HubAuditFilter`: enforce the invocation limit, register on connect, release on disconnect, audit a refusal.
5. `ClientContextMiddleware`: refuse a handshake over the ceiling (`429`) or from a disallowed origin (`403`).
6. REST CORS from the same list, and the wiring in `Program.cs`.
7. Tests, then `make agent-check`, then the documents.

## What the build added to this plan

- **`Main` went over CA1506 twice.** Reading the ceilings there was the forty-second coupled type, so
  `RealtimeServices.Add` now takes the builder and reads them itself; naming the CORS extension in `Main` was the
  forty-second again, so `ApiServices.UseCrossOrigin` applies the policy. The same extraction SPEC-024 and
  SPEC-025 each needed once.
- **`ClientContextMiddleware.InvokeAsync` went over the complexity gate** once the two handshake checks were
  added, so reading the three client values moved into `TryReadContext` — the same branches, in a method of their
  own.
- **`HubConnectionRegistry` had to become public.** `ClientContextMiddleware` is public and `UseMiddleware`
  activates it through a public constructor, which cannot take a less accessible parameter. Documented on the
  type so the next reader does not take it for an invitation.
- **A slot has to be released when a connect *fails*, not only when a connection closes.**
  `OnDisconnectedAsync` does not run for a connection that never established, so the filter releases in a
  `catch` as well as in the `finally` — without it, a refused join during connect would leak a slot until the
  process restarted.

**A known flake, recorded rather than re-run quietly:** across four full-suite runs of this branch,
`PlaylistTrackOrderingTests.AddTrack_AtHead_InsertsBeforeEverythingAsync` failed in two and passed in two, and
passes 8/8 when its own suite runs alone. It belongs to SPEC-015 and this unit does not touch that path; the
suite now starts one more PostgreSQL container and seven more hosts, which makes an already timing-sensitive
test more likely to lose. Worth its own look — it is not evidence that these limits work or do not.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `HubLimitTests` — the sixth connection | one user's sixth concurrent connection is refused, and the fifth is not |
| — the release | closing a connection frees its slot, so a client that reconnects is not locked out |
| — the per-IP ceiling | a twenty-first connection from one address is refused even across accounts, which is the venue case |
| — the invocation limit | the sixty-first invocation in a minute throws, and the sixtieth does not |
| — the refusal's record | a throttled invocation leaves an audit row with outcome `refused` |
| — the log | the trip names the hub, the method and the wait, and no account |
| — an allowed origin | a handshake carrying an origin on the list connects |
| — a disallowed origin | is refused at the handshake, not after the connection is open |
| — no origin at all | connects, which is the native client ADR-0030 deliberately admits |
| `make agent-check` | the OpenAPI document and the PDFs stay in step with the contract change |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §17 closes **O21**, and T-12 carries the stated limit and its evidence
- [x] `docs/api/ENDPOINTS.md` says what a refused handshake and a throttled invocation answer
- [x] `status: done` in this file
