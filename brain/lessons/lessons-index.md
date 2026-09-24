---
kind: lessons-index
updated: 2026-09-23
---

# Lessons

Durable, hard-won facts. Not a log, not a diary, not debug output.
A lesson earns a line here only if it would otherwise be rediscovered the expensive way.

| # | Lesson | Origin |
|---|---|---|
| L-001 | `reference/nArchitecture` is a nested git repo with its own history. Running git commands from the workspace root will not see it, and it must never be committed into this repo's tree as source. | workspace scan, 2026-09-23 |
| L-002 | YouTube Data API v3 bills `search.list` at roughly 100x a `videos.list` call in quota units. Search-heavy track pickers exhaust a default daily quota fast. Cache aggressively. | [[ADR-0003-music-provider-youtube]] |
| L-003 | Publicly committed credentials are an automatic project failure in this subject, not a deduction. `.env` is git-ignored from the first commit, not retrofitted. | [[subject-requirements]] |
| L-005 | BCrypt truncates its input at **72 bytes**. Two different long passwords then hash identically. Enforce 12-64 characters and a 72-byte UTF-8 check in validation — do not rely on the library to complain. | [[ADR-0007-password-hashing-bcrypt]] |
| L-006 | An OAuth 2.0 **access token** says what the bearer may do, not who they are, and may have been minted for another app. Identity comes from a validated OIDC **ID token** with `aud` and `nonce` checked. | [[ADR-0008-oidc-social-login]] |
| L-007 | Facebook is not uniformly OIDC: Limited Login issues an ID token, classic Facebook Login does not. Google is full OIDC. The integration must branch — assuming symmetry produces a broken or insecure path. | [[social-oauth]] |
| L-008 | Microsoft's CA analyzers cannot express method **length** or method **count**. `SonarAnalyzer.CSharp` supplies S138, S1448 and S104. A limit written only in a document is a suggestion. | [[ADR-0009-code-quality-gates]] |
| L-009 | ASVS is CC BY-SA 4.0. Verbatim redistribution needs attribution and the licence file alongside it; adapted text carries share-alike. Reference requirement ids instead of copying requirement text. | [[owasp-provenance]] |
| L-010 | Vercel cannot host this API. It is serverless functions plus static hosting: ASP.NET Core needs a long-running process and SignalR holds persistent WebSocket connections. .NET is not a first-class Vercel runtime either. Docker-image hosts (Fly.io, Railway, Render, Azure Container Apps) are the viable class. | [[ADR-0015-hosting-and-deployment]] |
| L-011 | Supabase free projects **pause** after a period of inactivity. A paused project on defence day is a dead demo. Touch it beforehand and keep the local PostgreSQL container as a fallback — same engine, same migrations. | [[ADR-0016-supabase-plan-region]] |
| L-012 | Client-reported GPS is evidence, not proof. Server-side geofencing can only check what the client claims. A venue-issued short-lived code raises the cost far more than any coordinate validation can. | [[ADR-0014-location-time-licence]] |
| L-013 | "Who is playing what right now" is its own aggregate. Hanging now-playing off `Event` and control off `Device` leaves delegation unconnected to the thing being played, and a peer evaluator will ask what a delegate actually controls. | [[ADR-0017-playback-session]] |
| L-014 | A backend cannot observe audio. Advancing a queue on a server-side `durationSeconds` timer drifts on pause and buffering; the client reports *when*, the server decides *what*. Make the advance call idempotent on the expected current track or a retry silently skips a song. | [[ADR-0017-playback-session]] |
| L-015 | SignalR hub methods bypass the MediatR pipeline, so a REST-only audit behaviour silently misses every hub action. A hub filter is needed, and client context must be captured at the handshake because a hub invocation carries no HTTP headers. | [[ADR-0019-hub-action-auditing]] |
| L-016 | `ab` cannot measure WebSocket. For a SignalR system the load tool choice is constrained by the realtime surface, not by REST throughput. Gatling is both named in the subject and WebSocket-capable. | [[ADR-0018-load-testing-tool]] |
| L-017 | MediatR and AutoMapper moved to a commercial licence (LuckyPennySoftware) with a free tier for small-scale use. "Everyone uses it" stopped being a sufficient reason to add a .NET package — check the terms and record the check. | [[ADR-0021-backend-dependency-set]] |
| L-018 | An in-memory-only catalog cache re-burns the YouTube quota after every container restart, and restarts cluster right before a demo. Persist the cache in the database and keep memory as L1. | [[ADR-0023-catalog-cache]] |
| L-019 | Never let a scheduler be the only thing enforcing expiry. Check the lifetime at the point of use; a background sweep is hygiene, not security. A stopped timer must not turn a stale token into a valid one. | [[ADR-0024-expiry-and-cleanup]] |
| L-020 | Submodules are a submission risk at 42: a stale pointer or a forgotten `--recursive` means the evaluator sees missing code. One repository, one clone, one submission. | [[ADR-0020-monorepo-layout]] |
| L-021 | An unattended agent needs a git protocol, not just a prohibition. Branch and commit per spec gives per-unit rollback; "never commit" turns five cycles into one unreviewable pile. A clean tree is a precondition, not a nicety. | [[ADR-0025-agent-git-protocol]] |
| L-022 | Name configuration after the role, not the vendor: `DATABASE_URL`, not `SUPABASE_DB_URL`. A variable named after a provider couples the application to it for no reason. | [[ADR-0026-environment-bring-up]] |
| L-004 | `reference/` is read-only. It is there to learn the structure from, not to copy from. Backend code is written under `MusicRoom/` only. Copying a file across drags net6-era packages in and breaks subject IV.1 (no third-party source in the repo). | team decision, 2026-09-23 |

## How to add one

Append a row. Keep it to one sentence of fact plus where it came from.
Link to the ADR or architecture note that carries the detail.
