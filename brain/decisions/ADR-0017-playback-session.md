---
kind: adr
id: ADR-0017
status: Accepted
date: 2026-09-24
---

# ADR-0017 — PlaybackSession aggregate and client-driven queue advance

## Context

Two gaps surfaced when the subject requirements were mapped onto the design:

1. **V.2.1** says a highly-voted track *"moves up and plays earlier"*. The ordering existed
   ([[endpoint-inventory]] §4), but nothing modelled **playback progressing** from one track to
   the next.
2. **V.2.2** says the control licence is *per device*, and control is delegated to friends. But
   what the delegate actually controls was undefined — there was no link between `Device` and the
   `Event` or `Playlist` being played.

Both are the same missing concept: **who is playing what, right now, on which device.**

## Decision

### PlaybackSession

A new aggregate in the Playback context:

```
PlaybackSession
  id
  deviceId            the device doing the playing  (V.2.2: licence is per device)
  sourceType          Event | Playlist
  sourceId
  nowPlayingId        the current suggestion or playlist track
  startedAtUtc
  controllerUserId    who may issue playback commands right now
  concurrencyToken    xmin
```

- A device has at most one active session.
- `ControlGrant` changes `controllerUserId` on the session. That is what "delegating music
  control" means concretely.
- The session is the single place playback state lives. `Event` and `Playlist` stay pure
  collaboration aggregates and know nothing about playback.

### Queue advance: the client reports, the server decides

```
1. Controller device detects the track ended
2. POST /api/v1/playback-sessions/{id}/advances  { expectedNowPlayingId }
3. Server verifies the caller is controllerUserId
4. Server picks the next item:
     Event    -> highest vote count, ties broken by earliest suggestion
     Playlist -> next ordering key
5. Server writes nowPlayingId under the concurrency token and commits
6. ONLY THEN broadcasts NowPlayingChanged
```

`expectedNowPlayingId` makes the call **idempotent**: a duplicate advance from a flaky network
finds the expectation already stale and returns the current state instead of skipping a track.

## Consequences

- **The server remains the arbiter of *what* plays; the client is the authority on *when*.**
  The backend cannot observe audio, and a scheduler based on `durationSeconds` drifts on pause,
  buffering and network stalls. This split matches subject V.5: the app is a remote control, the
  backend holds the truth.
- **A fourth concurrency hotspot.** Two clients advancing at once is now possible.
  `expectedNowPlayingId` plus the concurrency token resolves it; the loser gets the current state,
  never a double skip. This joins the three hotspots in [[sdd]] §12 and needs a parallel test.
- **New failure mode: a silent controller.** If the controller device goes offline mid-track,
  nothing advances. Accepted for now: any participant with control can take over
  (`PUT /devices/{id}/control`), and the owner can always revoke. A server-side watchdog is not
  built — it would reintroduce the drift problem this decision avoids.
- `Device`, `Event` and `Playlist` gain no playback fields. The new aggregate absorbs them.
- Adds roughly six endpoints and one SignalR event. See [[endpoint-inventory]] §7.
- Closes the "what does control actually control" question that a peer evaluator will ask.

## Alternatives rejected

- **Now-playing on `Event`, control on `Device`, no shared concept** — less modelling, but leaves
  Delegation only loosely connected to the other two services and the defence question unanswered.
- **`ControlGrant { deviceId, eventId }`** — narrow and clear, but excludes playlist playback and
  contradicts the subject's per-device wording.
- **Server-side timer driven by `durationSeconds`** — no client dependency, but drifts against
  real playback and needs a background scheduler.
- **Manual advance only** — least work, but "live music chain" in V.2.1 is then barely met.

Closes gaps G1 and G2.
