---
kind: project-card
id: musicroom-backend
status: in-progress
updated: 2026-10-01
---

# Project Card — MusicRoom.Backend

Single source of truth for all service data. Clients are thin remote controls.

## Identity

| Field | Value |
|---|---|
| Path | **`MusicRoom/`** — the only location for backend source code. 13 projects: 5 application, 4 `corePackages`, 4 test. Layout: [[ADR-0011-solution-layout]] |
| Language / runtime | C# 13 / .NET 10.0.401 |
| Framework | ASP.NET Core Web API |
| Dependency set | MediatR, FluentValidation, built-in RateLimiter, built-in OpenAPI, no mapper ([[ADR-0021-backend-dependency-set]]) |
| Observability | Serilog + OpenTelemetry, exporter off by default ([[ADR-0022-observability-stack]]) |
| Catalog cache | `track_refs` table + `IMemoryCache` ([[ADR-0023-catalog-cache]]) |
| Architecture | Clean Architecture + CQRS, modelled on [[reference-narchitecture]] |
| Persistence | EF Core + Npgsql on Supabase (managed PostgreSQL) ([[ADR-0006-datastore]]) |
| Realtime | SignalR over WebSocket ([[ADR-0004-realtime-signalr]]) |
| Music provider | YouTube ([[ADR-0003-music-provider-youtube]]) |
| API style | REST / JSON, OpenAPI documented ([[ADR-0005-api-style-rest-json]]) |
| Tests | xUnit + NSubstitute + Testcontainers ([[ADR-0012-test-stack]]) |
| Password hashing | BCrypt, cost >= 12 ([[ADR-0007-password-hashing-bcrypt]]) |
| Social login | OpenID Connect + PKCE ([[ADR-0008-oidc-social-login]]) |
| Security conformance | OWASP ASVS 5.0.0 — L1 all, L2 for V6/V7/V8/V9/V10/V16 ([[asvs-conformance]]) |
| Code limits | 40-line methods, complexity 10, 15 methods/type, analyzer-enforced ([[ADR-0009-code-quality-gates]]) |
| CI | GitHub Actions, `dev` and `prod` configs, **no deploy** ([[ADR-0015-hosting-and-deployment]]) |
| Runtime | local Docker on a declared machine; DB Supabase Free, Frankfurt ([[ADR-0016-supabase-plan-region]]) |
| Mail | **Brevo**, behind `IMailService`, selected by `MAIL_PROVIDER`; sandbox sender ([[ADR-0013-mail-provider]], SPEC-024) |

## Bounded contexts (planned)

| Context | Owns | Notes |
|---|---|---|
| Identity | user, credentials, email verification, password reset, social links | subject V.1 |
| Profile | public / friends-only / private fields, music preferences | 3 visibility tiers are a hard requirement |
| Event (Track Vote) | event, attendee, track suggestion, vote | public/private + license rules |
| Playlist (Editor) | playlist, collaborator, ordered tracks | realtime multi-user editing |
| Delegation (Control) | device, control grant | per-device licence, delegate to friends |
| Catalog | YouTube track lookup + cache | external SDK boundary |
| Audit | per-action log: platform, device, app version | subject V.6; REST behaviour + SignalR hub filter ([[ADR-0019-hub-action-auditing]]) |
| Playback | playback session, now-playing, queue advance | one session per device; server decides *what*, client reports *when* ([[ADR-0017-playback-session]]) |

**All three services ship** ([[ADR-0010-service-scope]]). If the schedule slips, Control Delegation is dropped first — removing it still leaves two, which satisfies V.2.

## Cross-cutting requirements

- Concurrency control on votes and playlist reordering is called out explicitly in the subject. See [[realtime-architecture]].
- Every mobile action produces a backend log line with platform, device, app version.
- Backend base URL must be configurable on the client (test requirement).
- Brute-force and session-theft mitigation required. See [[auth-and-identity]].
- Load must be measured and justified with named server specs.

## API contract

[[endpoint-inventory]] — written 2026-09-24. This is the interface to the UI workstream; keep
`docs/api/openapi.json` current once code exists.

## Conformance layer

Every line of code must be OWASP-conformant: [[owasp-index]] · [[asvs-conformance]] ·
[[owasp-top10-checklist]] · [[owasp-api-top10-checklist]] · [[threat-register]]

## Standards that apply

Via [[standards-router]]: [[engineering-baseline]] · [[git-workflow]] · [[dotnet-csharp]] ·
[[clean-architecture-cqrs]] · [[ef-core-persistence]] · [[signalr-realtime]] ·
[[rest-api-design]] · [[openapi-documentation]] · [[database-design-migrations]] ·
[[authentication-authorization]] · [[application-security]] · [[secrets-management]] ·
[[youtube-integration]] · [[social-oauth]] · [[testing]] · [[logging-observability]] ·
[[load-testing]] · [[ci-cd-containerization]]

## What exists, as of 2026-10-01

Every spec in `docs/specs/` is `done`, each on its own branch and draft pull request. They are stacked
and unmerged, so `main` lags: read `docs/specs/` for the count and the status, `git log` for what has
landed.

| Slice | Built |
|---|---|
| S0 | solution, `Directory.Packages.props`, compose with a local PostgreSQL, `Makefile`, CI, `/health`, `/health/ready`, generated OpenAPI |
| S1 | registration, email verification and resend, login, JWT + refresh rotation with reuse detection, password reset, credential rate limits, **bearer validation and the session endpoints** |
| S1a | **complete**: the hosted mail sender. `MAIL_PROVIDER` chooses `brevo` or, on a Development host only, the sender that logs the link; a host with neither does not start. Verification and reset messages reach a real mailbox from Brevo's sandbox sender — O17 and T-26 closed |
| S4 | the catalog: one YouTube adapter behind `ICatalogProvider`, two cache levels (`IMemoryCache`, then `track_refs`), a cached query-to-ids mapping, `GET /tracks` with a 30-a-minute per-account limit, and `503` rather than `500` when the quota is spent. **Not yet:** `DATABASE_URL` still points at the local container, not Supabase |
| S2 | mandatory client headers and the audit log, refusal outcomes included — REST only; the hub filter waits for a hub |
| S3 | **complete**: own profile with per-field visibility and music preferences, the friends graph, and reading another user's profile projected per field against the relationship |
| S5 | **complete**: playlists with their three gates, an `xmin` concurrency token and collaborators; tracks with a sparse ordering key, a guarded reorder and the twenty-way parallel move that proves it; `PlaylistHub` at `/hubs/playlists`, authorised on join, audited by ADR-0019's filter, broadcasting after the commit |
| S6 | **complete**: the event aggregate with its three gates, the window, invitations and attendance; suggestions, votes, the vote-ordered queue counted from the rows, venue codes and ADR-0014's presence checks; `EventHub` at `/hubs/events`, sharing one broadcast pipeline and one audit filter with `PlaylistHub` |
| S7 | **complete**: devices registered from the client headers; control delegation to a friend, guarded by the device row's `xmin`, with revoke and release; playback sessions with a server-chosen next track, an idempotent advance, an explicit jump and `NowPlayingChanged`; `ControlHub` telling both sides that control moved |
| S9 | capacity measured — five Gatling scenarios under `tests/load/`, numbers in `docs/load/REPORT.md`: **100 concurrent writers and 200 readers on the declared machine**, the ceiling being PostgreSQL's connection count and not CPU (O22). That run found a live defect — the rate limiter ran before authentication, so the per-account vote limit was a per-address limit ([[ADR-0041-limiter-after-authentication]]). The realtime limits are built too (SPEC-026): 5 connections per account, 20 per address, 60 hub invocations a minute per connection, and the handshake `Origin` checked against `CORS_ALLOWED_ORIGINS`, which code reads for the first time — O21 closed. Remaining in S9: the re-measurement ADR-0030 asks for, and the `asvs-conformance` evidence |


**No counts here on purpose.** `docs/specs/` is the authority for what is done, [[migrations-index]]
for the schema history, and `make agent-check` for the test count — each drifted within a day of being
written down twice.

## Open items

- Remaining open: remote deployment target (O11), SignalR backplane (O8, deliberately deferred),
  fluent assertion library (O12), silent-controller recovery (O13), music preferences consumer (O14),
  OpenTelemetry evidence (O16), self-hosted vault (O18), RLS policies (O20),
  the Npgsql pool against `max_connections` (O22, opened by the load measurement). **O17 closed**
  2026-10-04 with the Brevo sender.
- **Credentials received:** Brevo API key for mail (**spent — S1a is built**), YouTube and Supabase keys (unblocking S4). Facebook/Google OIDC credentials for S8 are partially pending.
- **The decisions taken during S1 to S3 are now recorded as ADRs**, so deleting a spec loses nothing:
  [[ADR-0036-access-token-mechanics]], [[ADR-0037-credential-rate-limiting]],
  [[ADR-0032-refresh-token-lifetime]], [[ADR-0033-audit-write-posture]] and
  [[ADR-0034-profile-storage-and-visibility]]. [[ADR-0036-access-token-mechanics]] and
  [[ADR-0037-credential-rate-limiting]] amend [[ADR-0021-backend-dependency-set]];
  [[ADR-0036-access-token-mechanics]] amends [[sdd]] §13.1's claim list. Accepted ADRs are never
  edited — a later change supersedes one.
- Owner: one backend developer. The React Native client is a parallel workstream owned by the second team member ([[musicroom-mobile]]).
