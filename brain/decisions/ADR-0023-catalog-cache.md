---
kind: adr
id: ADR-0023
status: Accepted
date: 2026-09-24
---

# ADR-0023 — Two-level catalog cache

## Context

[[ADR-0003-music-provider-youtube]] treats the Data API v3 quota as a hard constraint, not an
optimisation: `search.list` costs roughly two orders of magnitude more than a `videos.list` by id.
Where cached `TrackRef` data lives was open (T3).

## Decision

Two levels:

| Level | Store | Lifetime |
|---|---|---|
| L1 | `IMemoryCache` in process | short, hot lookups |
| L2 | `track_refs` table in PostgreSQL | persistent, the real cache |

Lookup order: L1, then L2, then YouTube. A provider response populates both.

## Consequences

- **A restart does not reset the quota exposure.** That is the whole point: an in-memory-only
  cache burns quota again after every container restart, and restarts happen most often right
  before a demo.
- The `track_refs` table is needed regardless. Playlist and event rows reference tracks, and
  [[domain-model]] already stores a `TrackRef` value object rather than provider payloads. The
  cache and the reference data are the same rows.
- L1 makes the common case free of a database round trip, which matters because the database is
  in Frankfurt ([[ADR-0016-supabase-plan-region]]) and every hop costs real latency.
- Two levels means two invalidation paths. Kept simple deliberately: entries are refreshed by age,
  never invalidated by hand. Track metadata barely changes, and a stale thumbnail is not a defect
  worth complexity.
- L1 is per instance and not shared. Consistent with O8 staying open; it changes nothing.
- **Only normalised `TrackRef` is cached** — video id, title, channel, duration, thumbnail URL.
  Never the raw provider payload, never media.
- Search results cache the **query to id list** mapping, so a repeated search does not re-issue
  `search.list`.

## Alternatives rejected

- **`IMemoryCache` only** — simplest, and wrong for the failure mode that matters: quota burned
  again on every restart.
- **Database only** — one source of truth, but a Frankfurt round trip on every metadata read.
- **Redis** — a real cache tier, but O8 deliberately defers Redis. Introducing it for caching
  would reopen that decision for a problem two cheaper levels already solve.

Closes T3.
