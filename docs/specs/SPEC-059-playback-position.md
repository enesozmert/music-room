---
id: SPEC-059
title: The playing device reports where it is, and everybody else seeks to it
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.1, V.3, V.4, V.6, V.7]
decisions: [ADR-0017, ADR-0036, ADR-0040]
standards: [signalr-realtime, api-design, logging-observability, load-testing]
approved_by: enesozmert # 2026-11-03, the second half of the mobile team's jam-sync proposal
escalation: |
  **Gates 3, 4 and 5 are yes.** A new table, a new route and a new hub event, and the audit rule has
  to be decided rather than inherited.

  **Not in the subject, and that is already recorded.** SPEC-058's escalation says why: V.2.1 asks for
  a voting chain among people in one room, V.5 makes the apps a remote control and V.3 makes the
  backend the keeper of the truth. Synchronised playback is consistent with all three and demanded by
  none. This is the half of the owner's 2026-11-03 request that costs something, so the costs are
  decided here rather than discovered later.

  **Three costs, and each one is a decision below.**

  **It writes every fifteen seconds per playing device.** That is the first write in this system whose
  rate is set by a timer rather than by a person. It must not land on `playback_sessions`: that row's
  `xmin` is the `If-Match` of advance and jump, so a position written to it would invalidate every
  controller's ETag four times a minute and turn a working client into a `412` generator. It gets its
  own table, one row per session, and the session row is never touched.

  **It must not be audited.** Subject V.6 says every action in the mobile app leaves a log entry in
  the backend, and every command in this system does — `IAuditableCommand`, SDD §10.7. A position ping
  is not an action a person took; it is a timer telling the server what the speaker is doing, and
  auditing it would write four rows a minute per session, burying the records V.6 exists to produce in
  telemetry nobody will ever read. **The command deliberately does not implement `IAuditableCommand`**,
  which is the mechanism that marker was built for: *"a command that should not be audited must be
  able to say so by omission rather than by an exclusion list nobody maintains."* A **deliberate**
  omission, written down here, is not the same as forgetting one.

  **It changes what the capacity figure measures.** `docs/load/REPORT.md` was measured without this
  traffic. The honest answer is not to re-measure in this unit — the figure is tied to a declared
  machine and a declared scenario — but to say in the report that the measured scenarios contain no
  position reporting, and to size the limit so that the ceiling is arithmetic rather than a guess:
  one write per second per session, which is four times what the client sends and bounded per session
  rather than per account.
---

# The playing device reports where it is, and everybody else seeks to it

## Goal

`PUT /api/v1/playback-sessions/{sessionId}/position` takes `positionMs` and `paused` from the device
that is playing, stamps the server's own instant on it, and broadcasts it. A guest reading the
session gets the three values and computes where to seek. The session row, its version and its ETag
are untouched.

## Out of scope

- **Making the server authoritative over the position.** It is the device's report, stamped by the
  server. Nothing in this unit decides what a client should hear; it carries a number.
- **Re-measuring V.7.** The report gains a sentence about what its scenarios do not contain. A new
  measurement is its own unit on the declared machine.
- **Smoothing, drift correction or seeking policy.** The client's.
- **A `GET` of its own.** The position travels on the session detail and in the broadcast. A third
  place to read it would be a third thing to keep consistent.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0017 keeps the session shape; this adds a table beside it |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **yes** — one new table, `playback_positions` |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — a route, three response members and a hub event |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — the authorisation is jump's, and the audit rule is decided by omission |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **Its own table, one row per session, upserted.** | `playback_sessions.xmin` is the `If-Match` of advance and jump. A position on that row would bump the version four times a minute and answer every controller `412`. Rejected: a column on the session, which is the obvious design and the broken one |
| 2 | **`positionAtUtc` is the server's clock, never the client's.** | V.3 makes the backend the keeper of the truth, and two phones with two clocks reporting "now" would disagree about the same instant |
| 3 | **The write is not audited** — the command does not implement `IAuditableCommand`. | V.6 asks for a record of what a person did. A timer is not a person, and four rows a minute per session would bury the records V.6 exists to produce. The marker exists precisely so an omission can say this |
| 4 | **Authorisation is jump's: the device's owner or its current controller.** | Reporting where the music is, is something only the thing playing it can honestly do. A guest who could write it could make every other guest seek to a lie |
| 5 | **One write per second per session, then `429`.** | Four times what the client sends, bounded per **session** rather than per account — one party's speaker must not spend the allowance of a person running two rooms. The mobile side says it may be dropped silently; a `429` with `Retry-After` is the same thing said out loud |
| 6 | **A position for a track that is no longer playing is discarded.** | The report carries the `nowPlayingId` it belongs to. A ping that was in flight while the track advanced is stale by arrival, and applying it would make every listener seek backwards into the previous track |
| 7 | **The stamp is cleared when `now_playing_id` changes.** | Position belongs to a track. Carrying it across an advance would be the same backwards seek, one track later |
| 8 | **`404` for an ended session and for a caller who may not command it.** | The same answer the jump route gives, for the same reason: a caller learns nothing about sessions they do not control |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/PlaybackPosition.cs` | create — the row |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PlaybackPositionConfiguration.cs` | create — the table |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/*` | create — one new table |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PlaybackSessionRepository.cs` | change — upsert, clear on move, project into the detail |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/PlaybackContracts.cs` | change — three members |
| `MusicRoom/src/MusicRoom/Application/Features/Playback/Commands/ReportPosition/*` | create — the command, **without** `IAuditableCommand` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Realtime/PlaybackBroadcasts.cs` | change — `PlaybackPositionChanged` |
| `MusicRoom/src/MusicRoom/WebAPI/Realtime/RealtimeBroadcaster.cs` | change — its hub route and event name |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/PlaybackRateLimiterPolicies.cs` | create — one write a second, per session |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PlaybackEndpoints.cs` | change — the route |
| `MusicRoom/tests/**` | change — the write, the gate, the staleness, the limit, the broadcast |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json` | change — §7 and §9 |
| `docs/load/REPORT.md` | change — what the measured scenarios do not contain |
| `docs/SDD.md` | change — §9.2, §10.7, §16 |

## Verification

| Check | Proves |
|---|---|
| the controller reports a position | `204`, and the session detail carries the three values |
| a guest reports a position | `404`, and nothing is written |
| a position for a stale `nowPlayingId` | `204` and **nothing written** — a late ping does not drag listeners backwards |
| an advance, then a read | the position is gone, not carried into the new track |
| two reports in one second | the second is `429` |
| the session's `ETag` across ten reports | unchanged — the controller's `If-Match` still works |
| the audit table after fifty reports | no new rows |
| `PlaybackPositionChanged` | reaches the event group with the stamped instant |

## Done when

- [x] the route writes, broadcasts, and leaves the session's version alone
- [x] a stale or unauthorised report changes nothing
- [x] no audit row is written for a position
- [x] `docs/load/REPORT.md` says what its scenarios do not contain
- [x] `make agent-check` passes
- [x] `status: done` in this file
