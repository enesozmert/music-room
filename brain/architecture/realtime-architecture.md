---
kind: architecture
updated: 2026-09-23
---

# Realtime & Concurrency

The subject grades race-condition handling twice (voting, playlist reorder). Treat it as a
first-class design problem, not an implementation detail.

## Transport

SignalR hubs over WebSocket, fallback to Server-Sent Events / long polling.
Rationale and rejected options: [[ADR-0004-realtime-signalr]].

Planned hubs:

| Hub | Group key | Server → client events |
|---|---|---|
| `EventHub` | `event:{eventId}` | `TrackSuggested`, `VoteChanged`, `QueueReordered`, `NowPlayingChanged` |
| `PlaylistHub` | `playlist:{playlistId}` | `TrackAdded`, `TrackRemoved`, `TrackMoved`, `CollaboratorChanged` |
| `ControlHub` | `user:{userId}` | `ControlGranted`, `ControlRevoked`, `ControlTakenOver` |

`NowPlayingChanged` is broadcast on the hub owning the playback source — `EventHub` or `PlaylistHub`.

## Concurrency strategy

| Hotspot | Mechanism |
|---|---|
| Vote double-submit | unique constraint `(EventId, TrackId, UserId)`; insert-or-ignore semantics; vote count derived, never incremented in memory |
| Vote count read | recompute in the same transaction, or a DB-side aggregate — never read-modify-write from the app |
| Playlist reorder | fractional / sparse ordering keys (gap of 1000) + optimistic concurrency token on the playlist row; on conflict, reload and reapply |
| Control takeover | single-row state machine on `Device` with a concurrency token; last writer must lose, not silently win |
| Queue advance | `expectedNowPlayingId` in the request plus the session's concurrency token; a stale expectation returns the current state instead of skipping a second track ([[ADR-0017-playback-session]]) |

Rule: **the database, not the hub, is the arbiter.** A hub broadcast happens only after the
transaction commits. Otherwise clients see state the server later rolls back.

## Authorisation at the hub

Hub connections carry the same JWT as REST. Group membership is checked server-side against
the event/playlist visibility and licence rules on every join — never trusted from the client.

## Location and time licence

Decided in [[ADR-0014-location-time-licence]]: server-side geofence check **plus** an optional
short-lived event code shown at the venue. The licence policy selects which proofs are required.
Time windows use server time; a client clock proves nothing. Residual risk: T-21 and T-17 in
[[threat-register]].

## Location and time licence

Decided in [[ADR-0014-location-time-licence]]: server-side geofence check **plus** an optional
short-lived event code shown at the venue. The licence policy selects which proofs are required.
Time windows use server time; a client clock proves nothing. Residual risk: T-17 and T-21 in
[[threat-register]].

## Open

- Scale-out backplane (Redis) - deliberately open (issue O8). Not needed for a single-instance
  defence, and "we did not need it, and here is why" beats an unused dependency.

## Related

[[system-overview]] · [[domain-model]] · [[ADR-0004-realtime-signalr]]
