---
kind: adr
id: ADR-0010
status: Accepted
date: 2026-09-24
---

# ADR-0010 — Ship all three services

## Context

Subject V.2 requires **at least two** of Music Track Vote, Music Playlist Editor and
Music Control Delegation. Issue O1 blocked the endpoint inventory, the schema and the work split.

## Decision

Build **all three** services.

## Consequences

- The endpoint inventory, schema and slice plan cover three contexts instead of two.
  [[sdd]] §5.2 is no longer conditional.
- **The location+time licence mode is now mandatory.** It belongs to Track Vote (V.2.1) and was
  the only `partial` row in [[sdd]] §16. It is decided in [[ADR-0014-location-time-licence]].
- Two of the three concurrency hotspots in [[sdd]] §12.1 are now in scope simultaneously —
  vote races and playlist reorder races — plus the device control state machine. All three need
  parallel tests, not sequential ones.
- More surface to defend. Subject VI is explicit: the bonus is graded only if the mandatory part
  is **flawless**. Three half-finished services score worse than two complete ones.
  **If the schedule slips, drop Control Delegation first** — it is the smallest and the only one
  whose removal still leaves two services, which satisfies V.2.
- Slice plan grows from S5-S6 to S5-S6-S7 in [[delivery-plan]].

## Alternatives rejected

- **Playlist Editor + Control Delegation** — lowest risk, avoided geofencing entirely.
- **Track Vote + Playlist Editor** — both concurrency hotspots without the device state machine.
- **Track Vote + Control Delegation** — would have dropped realtime collaborative editing.

Closes issue O1.
