---
kind: adr
id: ADR-0039
status: Accepted
date: 2026-10-01
---

# ADR-0039 — A queue broadcast carries the authoritative value, not a row version

## Context

[[signalr-realtime]] and [[sdd]] §9.2 require that *every payload carries the aggregate id and a
monotonic `version`, so a client that missed a message can detect the gap and resynchronise over REST*.
`PlaylistHub` satisfies that literally ([[ADR-0035-realtime-broadcast-posture]]): a playlist row carries
`xmin`, every track write touches that row, and the version in the payload is the same token the REST
`ETag` carries.

`EventHub` cannot copy it. Two facts get in the way.

- **An event's `xmin` is already spoken for.** `PATCH /events/{eventId}` is guarded by `If-Match` against
  it. Touching the event row on every vote would invalidate the owner's token continuously — the owner
  could not rename an event during the evening the event is actually running, which is the one evening it
  matters.
- **There is no vote counter to version.** [[SPEC-018]] decided deliberately that no column holds a count,
  because a stored count is a second copy of a fact two writers can disagree about. Adding a
  `queue_version` column would reintroduce exactly the thing that decision removed, one level up.

## Decision

An `EventHub` payload carries the **authoritative value of the thing that changed**, plus the instant it
changed, and no row version.

| Event | Payload | Why it is enough |
|---|---|---|
| `TrackSuggested` | event id, suggestion id, video id, instant | An add is idempotent by id: applying it twice is applying it once |
| `VoteChanged` | event id, suggestion id, **the suggestion's new vote count**, instant | State, not a delta. A client that missed three messages and receives `votes: 7` is correct; a client that received `+1` three times out of order is not |
| `QueueReordered` | event id, the removed suggestion id, instant | A removal is idempotent by id, and the ordering it implies is derived from counts the client already holds |

Clients **re-read the queue on connect and on reconnect**, which is the reconcile point a version would
otherwise provide. `signalr-realtime`'s "treat every event as a hint to reconcile" rule is unchanged and is
what makes this work.

## Consequences

- For `VoteChanged` this is **stronger** than a version: the payload is the value, so a missed message is
  self-healing the next time that suggestion is voted on, and out-of-order delivery converges instead of
  drifting.
- **The real loss is a missed removal.** A client that misses a `QueueReordered` keeps a track in its list
  until it re-reads. Bounded by the reconnect re-read, and visible rather than silent: the track simply
  does not disappear. Accepted — the alternative costs the owner's `If-Match` or a counter column.
- This is a **written deviation from `signalr-realtime`'s payload rule**, scoped to `EventHub`.
  `PlaylistHub` keeps the literal version, so the rule still holds wherever a parent row exists to carry
  one. The standard's intent — a client can always converge — is met by a different mechanism.
- A future `NowPlayingChanged` belongs to the `PlaybackSession` of [[ADR-0017-playback-session]], which has
  a row and therefore a token of its own. It should carry a version in the literal sense, and nothing here
  prevents that.

## Alternatives rejected

- **Touch the event row on every queue write**, as the playlist does for tracks. Correct by the standard's
  letter, and it makes the metadata `PATCH` unusable during a live event.
- **A `queue_version` counter column on `events`.** Reintroduces the shared mutable count SPEC-018 removed,
  and two writers incrementing it is the same lost update one level up.
- **A PostgreSQL sequence per event.** A monotonic number nothing else needs, a second object per event to
  create and drop, and a gap in it still tells a client only to do what it already does — re-read.
- **Send the whole ordered queue on every change.** Correct and simple, and it sends a page per vote to
  every listener on a mobile network. `signalr-realtime` says keep payloads small.

Extends [[ADR-0035-realtime-broadcast-posture]] · implements part of [[ADR-0004-realtime-signalr]].
