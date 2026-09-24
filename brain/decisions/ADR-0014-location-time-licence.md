---
kind: adr
id: ADR-0014
status: Accepted
date: 2026-09-24
---

# ADR-0014 — Location and time licence: geofence plus optional event code

## Context

Subject V.2.1 requires a licence mode where *"with the right licence, only people present at a
given place during a given time window can vote"*. [[ADR-0010-service-scope]] put Track Vote in
scope, so this is now mandatory. It was the only `partial` row in [[sdd]] §16. Issue O7.

## Decision

Two independent proofs of presence, selectable per event in its licence policy:

| Proof | How |
|---|---|
| **Geofence** | the client sends coordinates with the vote; the server checks them against the event's centre, radius and time window |
| **Event code** | the owner generates a short-lived code shown at the venue; the client sends it; the server verifies and consumes it |

`LicenceMode.LocationAndTimeRestricted` carries a policy object specifying which proofs are
required: geofence only, code only, or both.

All evaluation is **server-side**. The client never decides whether it is allowed to vote — it
supplies evidence, and the server rules on it.

## Consequences

- **Client-reported GPS is spoofable.** A determined user can lie about their coordinates. This is
  not fixable without infrastructure this project will not build. It is recorded as **T-17** in
  [[threat-register]] with its residual risk, and stated plainly at defence. Subject V.6 says the
  API is not expected to be unbreakable; it does expect the remaining danger to be described.
- The event code raises the cost of remote voting meaningfully, because the code exists only at
  the venue and expires. It is the stronger of the two proofs, but it satisfies "present at a
  place" indirectly, which is why geofence remains available.
- Time-window checks use server time, never client time. A client clock is evidence of nothing.
- The event stores centre coordinates, radius in metres, and a UTC window. Coordinate precision is
  kept coarse — this is location data about real people, and [[application-security]] data
  minimisation applies.
- A submitted coordinate is used for the check and **not** stored on the vote row. Storing every
  voter's position would be a privacy liability with no requirement behind it.
- Codes are short-lived, single-use per user, and rate limited like any other credential-shaped
  input.

## Alternatives rejected

- **Geofence only** — simplest, but the spoofing residual risk is the whole story with no
  mitigation available.
- **Code only** — resistant to GPS spoofing, but meets the subject's "at a given place" wording
  indirectly, and an evaluator may push on that.
- **iBeacon** — suggested in subject VI.2 as a bonus. Needs hardware and mobile work; the bonus is
  graded only if the mandatory part is flawless, so it is not a foundation to build on.

Closes issue O7. Makes [[sdd]] §16 V.2.1 licence row `designed` rather than `partial`.
