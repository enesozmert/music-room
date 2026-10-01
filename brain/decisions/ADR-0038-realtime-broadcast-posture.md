---
kind: adr
id: ADR-0038
status: Accepted
date: 2026-10-01
---

# ADR-0038 — A broadcast is queued by the handler and sent after the commit, by a pipeline behaviour

## Context

[[signalr-realtime]] and [[sdd]] §9.2 both state the rule: *broadcast after the database transaction
commits, never from inside it*. [[ADR-0033]] then made every auditable command run inside a transaction
that `AuditBehaviour` opens and commits. A handler therefore has **no post-commit moment of its own** —
when it returns, its transaction is still open.

SPEC-016 is the first unit where this matters: `PlaylistHub` must tell the other editors of a playlist
that a track moved, and the move is an auditable command.

Three places could send the message.

- **The handler**, straight after the repository call. Inside the transaction, so a rollback leaves the
  clients told about a change that never happened.
- **The endpoint**, after `sender.Send` returns. Correct in time, but every route repeats the plumbing,
  and the hub then only hears about changes that arrived over REST.
- **A pipeline behaviour** wrapping the audit behaviour. Correct in time by construction, written once.

## Decision

A handler **states** what happened; a behaviour **sends** it.

- `IPlaylistBroadcastQueue` is request-scoped. A handler enqueues a typed payload after a successful
  write and nothing else.
- `BroadcastBehaviour` is registered **between** `ValidationBehaviour` and `AuditBehaviour`, so it wraps
  the transaction rather than running inside it. When the inner pipeline returns, the transaction has
  committed; the behaviour then drains the queue through `IPlaylistBroadcaster`.
- If the inner pipeline throws, the queue is discarded. Nothing is sent for an action that rolled back.
- A failure *while sending* is logged and swallowed. The database is the arbiter; a notification that
  cannot be delivered must not undo a committed action.
- Every payload carries the aggregate id and the playlist's new `xmin` as `version`, which is the same
  token the REST `ETag` carries, so a client that missed a message can detect the gap and resynchronise
  over REST.

## Consequences

- The post-commit rule is structural rather than a thing each handler remembers. A new broadcast is an
  `Enqueue` call; the ordering guarantee comes free.
- `Application` stays transport-free: it depends on `IPlaylistBroadcaster`, and `WebAPI` implements it
  over `IHubContext<PlaylistHub>`.
- Behaviour order is now load-bearing in two directions, and is documented where it is registered:
  validation outside everything, broadcast outside the transaction, audit owning the transaction.
- **A broadcast can be lost.** A process that dies between the commit and the send has a committed
  change nobody was told about. Accepted: every client treats an event as a hint to reconcile and
  carries the version to detect a gap, which is already the rule in [[signalr-realtime]]. A durable
  outbox would make delivery reliable and is out of proportion to a single-instance defence; it is the
  same ground on which O8 leaves the scale-out backplane open.
- Broadcasts from a path that is not a MediatR command — a background job, say — would need their own
  flush point. None exists today.

## Alternatives rejected

- **Send from the handler.** Announces uncommitted state; a rollback cannot be unsent.
- **Send from the endpoint.** Correct but repeated per route, and it puts transport sequencing into the
  one layer that is supposed to be a thin adapter.
- **A durable outbox table drained by a worker.** Reliable delivery, at the cost of a table, a worker,
  and a second place where ordering can go wrong. Revisit only if lost notifications are observed.
- **Broadcast inside the transaction and compensate on rollback.** There is no compensating message for
  "ignore what I just told you"; clients would have to distrust every event.

Implements the realtime half of [[ADR-0004-realtime-signalr]] and sits beside [[ADR-0019-hub-action-auditing]].
