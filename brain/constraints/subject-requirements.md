---
kind: constraints
source: docs/subject/en.subject tr.pdf (v6, Turkish)
updated: 2026-09-23
---

# Subject Requirements (distilled)

Authoritative. If the Brain and this file disagree, the PDF wins. Re-read the PDF only to
resolve a dispute, not for routine work.

## Mandatory — User (V.1)

- Account creation on first launch: email+password **or** social (Facebook / Google).
- Once registered, a user can link a social account.
- Profile fields in **three visibility tiers**: public, friends-only, private. Plus music preferences.
- Email+password signup requires **email verification** and a **forgot-password** flow.

## Mandatory — Services (V.2) — ship at least 2 of 3

| Service | Core rule |
|---|---|
| Music Track Vote | Live queue; anyone suggests/votes; high-vote tracks move up. Public by default; private = invitees only. Licence modes: everyone / invitees only / **people at a given place within a given time window**. Handle vote race conditions. |
| Music Control Delegation | Licence system **per device** bound to the account. Owner delegates playback control to friends. |
| Music Playlist Editor | Real-time multi-user playlist editing. Public by default; private = invitees only. Licence: everyone edits, or invited-with-licence only. Handle reorder race conditions. |

## Mandatory — Server (V.3)

All service data lives in the backend. Backend is the single source of truth. Any stack allowed.

## Mandatory — API (V.4)

- REST-oriented (alternatives allowed if justified).
- JSON exchange format (alternatives allowed if justified).
- Reference documentation of methods, inputs, outputs — Swagger/OpenAPI named as an example.

## Mandatory — Mobile (V.5)

- Apps are "remote controls" only. Backend address **configurable in the app**.
- Social network authentication (Facebook or Google) on mobile.

## Mandatory — Security (V.6)

- Authenticated user reaches own data only, never another user's.
- Anticipate brute force and session hijacking; implement protections; document remaining threats.
- **Every mobile action creates a backend log entry** with: platform, device, app version.

## Mandatory — Load (V.7)

Measure and justify concurrent-user capacity with a named tool
(ab, Gatling, Siege, Tsung, JMeter) and declare server specs (CPU, RAM, cloud/on-prem).

## Mandatory — Process (V.8)

Agility, per-layer tests, continuous integration.
**All credentials / API keys / env vars in a local `.env`, git-ignored.
Publicly stored credentials = automatic project failure.**

## General constraints (IV)

- No third-party library source committed to the repo.
- Dependencies must install automatically from a fresh clone via a Makefile or equivalent.
- The chosen SDK must not do the work for you.

## Bonus — only graded if the mandatory part is flawless

Multi-platform/responsive web, IoT (iBeacon proximity), free vs paid subscription tiers, offline mode with sync.

## Grading

Git repository only. Defence on repository contents. Exact folder/file names matter.
