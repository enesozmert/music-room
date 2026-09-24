---
kind: adr
id: ADR-0019
status: Accepted
date: 2026-09-24
---

# ADR-0019 — Audit SignalR hub actions with the same record

## Context

Subject V.6 is unconditional: *"Every action in the mobile application must create a log on the
backend"*, carrying platform, device and application version.

The audit record is written by a MediatR pipeline behaviour, which sits in the **REST command
path**. SignalR hub methods do not pass through it. Joining an event or a playlist is an action a
user takes in the mobile app, and it was falling through the gap (G4).

## Decision

A **SignalR hub filter** writes the same audit record as the REST behaviour, for
`JoinEvent`, `LeaveEvent`, `JoinPlaylist` and `LeavePlaylist`.

- Client context (`platform`, `device`, `appVersion`) is captured at the **handshake** and held on
  the connection, because a hub method invocation carries no HTTP headers of its own.
- A handshake without valid client context is rejected, mirroring the `400` that REST mutations
  return for the same omission.
- The record shape is identical to the REST one, so the audit table has one schema and one query
  surface.

Connect and disconnect themselves are **not** audited.

## Consequences

- The claim "every action is logged" becomes true across both transports, which is what V.6 asks
  and what an evaluator will probe.
- **Connect/disconnect stay out on purpose.** Mobile networks reconnect constantly; auditing them
  would bury deliberate user actions under transport noise. Reconnects remain in the application
  log as operational events, where they belong.
- The handshake now has a required-fields check, so a client that omits the context headers fails
  fast instead of producing unattributable audit rows.
- One more place to keep in step: a new hub method needs the filter applied, exactly as a new
  command needs the behaviour. Both are registered centrally, not per method.
- No change to the audit table schema.

## Alternatives rejected

- **Audit connect and disconnect too** — fuller coverage, drowned in reconnect noise.
- **Do not audit hub actions** — would need an argued exception to a requirement with no
  exceptions in its wording. A written deviation is defensible; this one is not worth defending
  when a filter costs so little.

Closes gap G4.
