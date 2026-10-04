---
kind: adr
id: ADR-0030
status: Accepted
date: 2026-09-27
---

# ADR-0030 — Hub call limits and handshake origin

> **Amended 2026-10-04: the numbers are set.** Owner decision — 5 concurrent connections per user,
> 20 per IP, 60 hub invocations per connection per minute. They were deliberately left
> `UNKNOWN - requires decision` below, and then S5 and S6 shipped both hubs without any of this
> (issue O21), so the sentence about them belonging to the S5 spec no longer has a spec to live in.
> They are here instead, and the work lands at S9.

## Context

Realtime lands at slice S5. Three decisions already cover it and none of them covers abuse of the
connection itself:

- [[ADR-0004-realtime-signalr]] chose SignalR and settled authentication: a hub connection carries
  the same JWT as REST.
- [[ADR-0019-hub-action-auditing]] added a hub filter so join and leave produce the same audit
  record as a REST command, and made a handshake without client context a rejected handshake.
- [[signalr-realtime]] settled group authorisation on every join, re-checked on reconnect, and
  broadcast only after commit.

Two gaps surfaced when the realtime surface was read against the S1 credential work:

1. **Nothing bounds how often a hub method may be called.** SPEC-006 gave every credential
   endpoint a per-IP policy and a per-account counter, but a hub method does not pass through the
   REST middleware pipeline, so none of that applies to it. [[owasp-api-top10-checklist]] says it
   in one line: *"A hub method skips the REST middleware pipeline unless it is wired
   deliberately."* T-12 in [[threat-register]] covers connection exhaustion with bounded payloads
   and explicit timeouts, and says nothing about call frequency. A client that holds one
   authorised connection to `event:{eventId}` can invoke a vote method at request rate.
2. **Nothing says whether the handshake checks its origin.** `CORS_ALLOWED_ORIGINS` exists in
   `.env.example` and governs REST. No rule connects it to the WebSocket handshake.

Two facts shaped the second question rather than being decided by it. The JWT travels as the
`access_token` query parameter, not as a cookie, so a browser does not attach it automatically to
a cross-site connection. And the React Native client is not a browser: it sends no `Origin`
header at all.

## Decision

### Both a connection ceiling and a per-connection call limit

- A **concurrent connection ceiling**, per user and per IP. Exceeding it refuses the handshake —
  *usually*. The refusal has two layers and only one of them is authoritative: the middleware reads
  the count and answers `429` before the handshake, and `OnConnectedAsync` takes the slot. A client
  whose `StartAsync` has returned is not yet counted, because `OnConnectedAsync` has not run, so a
  burst of negotiates can pass a read that is a moment stale. Those connections are refused a
  moment later, when the slot is actually claimed, and the client sees a drop rather than a refused
  handshake. **Accepted:** the ceiling bounds the steady state, which is what T-12 is about; making
  the middleware authoritative would mean holding a slot for a handshake that may never complete.
  It is recorded here because `ADR-0030` promised a refused handshake and the implementation
  delivers that only when the count has caught up — observed 2026-10-04, when two `gate` runs on
  one commit disagreed about it.
- A **per-connection invocation limit** on hub methods, enforced inside the filter
  [[ADR-0019-hub-action-auditing]] already installs. One filter, two jobs — not a second filter in
  the pipeline.
- A refused invocation throws `HubException`. The trip is logged the way SPEC-006 logs a REST
  rate-limit trip: the hub and the method, the wait, and nothing that identifies the account.
- The numbers, owner decision 2026-10-04:

  | Limit | Value |
  |---|---|
  | Concurrent connections per user | **5** |
  | Concurrent connections per IP | **20** |
  | Hub invocations per connection | **60 per minute** |

  Five per user is a phone, a tablet and a reconnect in flight, with room to spare: a person
  controlling playback from one device and watching a playlist from another is two, and a dropped
  connection the server has not yet reaped is a third. Twenty per IP is the venue case — a shared
  network at an event, which is precisely where ADR-0041 found the REST limiter partitioning on
  address and refusing 98 per cent of a ten-user step. Sixty invocations a minute is one a second
  sustained, against a client that joins a group and then mostly listens; a vote, a reorder or a
  queue advance is a REST write, and those carry their own limits.

  They are a starting point with a measurement behind the shape rather than the value: S9 has a
  Gatling harness and `docs/load/REPORT.md`, so the first run after this lands says whether 60 is
  generous or tight. Changing one is an amendment here, not a code review argument.

### The handshake is bound to `CORS_ALLOWED_ORIGINS`

- The hub handshake validates `Origin` against the same `CORS_ALLOWED_ORIGINS` list REST uses.
- A request with **no** `Origin` header passes. That is the native client, which cannot send one.
- A request with an `Origin` that is not on the list is refused at the handshake.

## Consequences

- **The connection ceiling and the call limit answer different attacks, which is why both are
  here.** The ceiling stops a client opening connections until the single instance runs out; the
  call limit stops one authorised connection spending the event's write path. Either alone leaves
  the other open.
- Putting the counter in the existing audit filter keeps one place that knows a hub invocation
  happened. A second filter would mean two orderings to reason about and two places to keep in
  step with the REST policies.
- The limiter state is per process, exactly like the REST limiter recorded against O8 in
  [[sdd]] §16. One instance is the ceiling, and the same note applies.
- The origin check earns little against the native client and is not useless: it closes the
  browser path to the hub without adding a key, a list or a second configuration surface to keep
  in step with REST.
- A missing `Origin` passing is a deliberate hole, not an oversight. Any non-browser client can
  omit the header. The control that stands behind it is the JWT, which is checked on every
  handshake and re-checked on reconnect.
- T-12 in [[threat-register]] narrows from "bounded payloads and timeouts" to a stated limit.
  Its row is updated when S5 lands the numbers, not now.
- Closes no issue in [[sdd]] §17. O8 stays open and deliberately so.

## Alternatives rejected

- **Only a connection ceiling.** It caps how many sockets a client holds and nothing else. Vote
  spam arrives over one authorised connection, which is the case the subject grades under V.2.1.
- **Only a per-invocation limit.** It bounds what each connection does and leaves the client free
  to open more of them.
- **No origin check, JWT alone.** Defensible on the facts above — the token is not a cookie, so a
  browser does not send it by itself. Rejected because reusing a list that already exists costs
  nothing, and "the token is not a cookie" is an argument that stops holding the moment someone
  moves the token.
- **A separate allowlist for the hub.** A second `.env` key and a second list to keep in step with
  the REST one. Two lists drift; one list does not.

## Affected

Standards: `signalr-realtime`, `authentication-authorization`. Design: [[sdd]] §9.2, §13.3.
Plan: [[delivery-plan]] S5. Threats: T-12. Related: [[ADR-0004-realtime-signalr]],
[[ADR-0019-hub-action-auditing]].
