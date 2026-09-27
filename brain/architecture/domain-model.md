---
kind: architecture
updated: 2026-09-28
---

# Domain Model (target)

Sketch, not a schema. For the aggregates that have shipped — `User`, `EmailVerificationToken`, `RefreshToken`, `PasswordResetToken`, `AuditRecord` — the migrations and their `IEntityTypeConfiguration` files are authoritative on field-level detail, not this note ([[migrations-index]]). For the rest it is still `UNKNOWN - requires decision` until the migration that creates them.

## Aggregates

| Aggregate | Root | Contains | Invariant that matters |
|---|---|---|---|
| User | `User` | credentials, email-verification token, social links, devices | email unique; unverified email cannot use services |
| Profile | `Profile` | per-field visibility tier, music preferences | every profile field carries one of `Public / Friends / Private` |
| Event | `Event` | attendees, suggested tracks, votes, licence policy | one vote per user per track; ranking derives from vote count |
| Playlist | `Playlist` | collaborators, ordered `PlaylistTrack` | position ordering must stay total and gap-tolerant |
| Device | `Device` | control grants | a control grant belongs to exactly one device |
| PlaybackSession | `PlaybackSession` | now-playing pointer, controller | at most one active session per device; the controller is what a control grant changes ([[ADR-0017-playback-session]]) |
| TrackRef | value object | YouTube video id, title, duration, thumbnail | never store YouTube audio/video content, only references |

## Shared enums

- `Visibility`: `Public | Private`
- `LicenceMode`: `Everyone | InvitedOnly | LocationAndTimeRestricted`
- `ProfileFieldVisibility`: `Public | Friends | Private`
- `Platform`: `iOS | Android | Web | Other`

## Concurrency hotspots

1. Two users vote on the same track at the same instant.
2. Two users reorder the same playlist simultaneously.
3. Two devices claim music control at once.
4. Two clients advance the same playback session at once.

These are graded explicitly. Strategy: [[realtime-architecture]].

## Related

[[system-overview]] · [[subject-requirements]] · [[musicroom-backend]]
