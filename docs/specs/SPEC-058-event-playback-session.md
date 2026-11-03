---
id: SPEC-058
title: A guest can ask an event what it is playing, and from which second
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.1, V.3, V.4, V.5]
decisions: [ADR-0017, ADR-0036]
standards: [signalr-realtime, api-design, testing]
approved_by: enesozmert # 2026-11-03, from the mobile team's jam-sync proposal
escalation: |
  **Gates 3 and 4 are yes** — one additive column on `playback_sessions`, and two new members plus a
  route in `docs/api/ENDPOINTS.md`.

  **Is this in the subject?** No, and it is worth saying plainly. V.2.1 asks for a voting chain among
  people *gathered in one place*; nothing there requires a remote guest's phone to play the same
  second as the host's. V.5 says the mobile apps are a "remote control" for the backend and V.3 makes
  the backend the keeper of the truth, so synchronised playback is **consistent** with the subject
  rather than demanded by it. It is a product extra the owner asked for on 2026-11-03, and this spec
  records that so a reader later does not mistake it for a requirement.

  **What this unit actually fixes is a hole the mobile team found.** A guest who opens an event has no
  way to learn which session is playing: `GET /playback-sessions/{id}` needs an id they do not have,
  `GET /devices/{id}/playback-session` is the device owner's and the controller's, and the only other
  source is the next `NowPlayingChanged` — so a guest who arrives mid-track waits for the next one.
  One read closes that, and it is a read they are already entitled to: whoever may see the event may
  see what it is playing, which is the rule `GET /playback-sessions/{id}` already applies.

  **`nowPlayingStartedAtUtc` is the server's stamp, not the client's.** V.3 says the backend is the
  representative of the truth; a start time computed on a phone is the one number in this feature that
  must not be. It is written in the same statement that moves `now_playing_id`, so the two cannot
  disagree, and the same value travels in `NowPlayingChanged` so a listener and a late joiner compute
  from one number.

  **`serverNowUtc` costs nothing and removes the stated limitation.** The mobile proposal ends by
  noting that device-clock skew lands directly in the offset and offers `serverNowUtc` as the fix. It
  is one field on a response the client already reads; adding it now is cheaper than the conversation
  about whether to add it later.

  **What this unit does not do is the position reporting** — `PUT /playback-sessions/{id}/position`,
  its table and its broadcast. That is SPEC-059, and it is separate because it brings a write every
  fifteen seconds per playing device, which touches the V.7 load figure and the V.6 audit rule in ways
  a read does not.
---

# A guest can ask an event what it is playing, and from which second

## Goal

`GET /api/v1/events/{eventId}/playback-session` answers the live session of an event to anybody who
may see the event. The answer says when the current track started, by the server's clock, and what
the server's clock reads now.

## Out of scope

- **Position reporting.** SPEC-059.
- **A playlist equivalent.** A playlist's listeners already hold the session id from the hub they
  joined, and nobody has asked. One route, for the case that exists.
- **Changing who may read a session.** The rule is the one `GET /playback-sessions/{id}` applies.
- **Correcting clock skew for the client.** `serverNowUtc` hands them the number; what they do with
  it is the client's.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0017 keeps the session shape, ADR-0036 keeps the broadcast rule |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **yes** — `now_playing_started_at_utc`, additive and nullable |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — a route and two response members |
| 5 | touches authn, authz, tokens, secrets, audit? | it **reuses** an authorisation rule, and adds none |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The stamp is written in the same statement that moves `now_playing_id`.** | Two statements could disagree, and the disagreement would be a track that started at a time it did not |
| 2 | **Null until something plays.** | A session whose queue is exhausted has no current track, and `started_at_utc` of the session is a different fact. Rows written before this migration are null, which is honest: nobody stamped them |
| 3 | **The same value travels in `NowPlayingChanged`.** | A listener and a guest who arrives later must compute from one number. The event already carried `occurredAtUtc` — the commit instant — and that is the same moment, but a listener should not have to know that |
| 4 | **`serverNowUtc` on the detail, computed per response.** | The client's offset is only as good as its clock. One field removes the 2.5-second tolerance the mobile team wrote down as a known limit |
| 5 | **The newest live session wins when an event somehow has two.** | At most one live session per device, not per event: two devices can legitimately play one event. The newest is the one a guest arriving now should follow |
| 6 | **A caller who may not see the event gets `404`, not `403`.** | The route says nothing about whether that event exists — the rule every event route already follows |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/PlaybackSession.cs` | change — the stamp |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PlaybackSessionConfiguration.cs` | change — the column |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/*` | create — one additive migration |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaybackSessionRepository.cs` | change — stamp it, project it, and find by event |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/PlaybackContracts.cs` | change — two members |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPlaybackSessionRepository.cs` | change — the event read |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Queries/GetEventSession/*` | create — the query and its handler |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/PlaybackBroadcasts.cs` | change — the stamp on the event |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Commands/**` | change — carry the stamp into the broadcast |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventPlaybackEndpoints.cs` | create — the route |
| `MusicRoom/tests/**` | change — the route, the stamp, the broadcast |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json` | change — §7 |
| `docs/SDD.md` | change — §9.2, §16 |

## Verification

| Check | Proves |
|---|---|
| a guest who may see a public event reads it | `200`, the session, the ETag |
| a stranger to a private event reads it | `404`, identical to an event with no session |
| an event with no live session | `404` |
| two devices playing one event | the newest live session answers |
| the stamp after a start, an advance and a jump | equals the moment of the move, and changes with each |
| the stamp on a session whose queue ended | the session is ended and the route answers `404` |
| `serverNowUtc` | within a second of the response's own time |
| `NowPlayingChanged` | carries the same stamp the detail reports |

## Done when

- [x] the route answers for a guest and refuses a stranger
- [x] the stamp is written by every move and read by both the detail and the broadcast
- [x] `make agent-check` passes
- [x] `status: done` in this file
