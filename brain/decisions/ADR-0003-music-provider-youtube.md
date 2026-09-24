---
kind: adr
id: ADR-0003
status: Accepted
date: 2026-09-23
---

# ADR-0003 — YouTube as the music provider

## Context

The services need a track catalogue and playback. The subject warns: *the SDK must not do your
work for you* — the collaboration, voting and licence logic stays ours.

## Decision

Use YouTube: **Data API v3** for search and metadata, and the YouTube player SDK on the client
for playback.

## Consequences

- Only a `TrackRef` (video id, title, duration, thumbnail) is persisted. We never store or
  proxy media. See [[domain-model]].
- Data API v3 enforces a **daily quota in units**, and `search.list` is the expensive call.
  Caching search results and preferring `videos.list` by id is a hard requirement, not an optimisation.
- The API key is server-side only and lives in `.env`. A key shipped in the client is both a
  quota leak and, per the subject, a failing condition.
- Playback happens on the client; the backend orchestrates *what* and *when*, never the audio stream.
- YouTube terms of service restrict download and background playback. Treat this as a product
  constraint to state at defence.

## Alternatives rejected

- **Deezer** — the common 42 choice, but full playback needs a paid account.
- **Spotify** — playback requires Premium for every tester.

Standard: `youtube-integration`.
