---
id: SPEC-025
title: The catalog — YouTube behind one adapter, and two cache levels in front of it
status: in-progress    # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4              # which DELIVERY-PLAN slice this belongs to
requirements: [III, V.2.1, V.2.3]
decisions: [ADR-0003, ADR-0023, ADR-0028]
standards: [youtube-integration, clean-architecture-cqrs, ef-core-persistence, secrets-management, testing]
approved_by: enesozmert # plan approved 2026-10-04, with the two open questions answered by "next"
escalation: |
  **Gate 2:** `Microsoft.Extensions.Http.Resilience` is added to `MusicRoom.Infrastructure`, and it ships.
  `youtube-integration` requires a timeout, retry on `5xx` and `429` only, and a **circuit breaker** so a
  provider outage does not exhaust the thread pool; the package is the first-party, Polly-backed way to get all
  three. The alternative was hand-rolling a breaker in a `DelegatingHandler`, which means writing and testing
  one ourselves. Owner chose the package, 2026-10-04, consistent with the `Microsoft.Extensions.Http` call made
  for SPEC-024 the same day.

  **Gate 5:** the unit holds the YouTube Data API key. It is read from `.env`, lives on the typed client, and is
  never logged, never returned and never named in a failure message.

  **One number taken rather than escalated:** `GET /tracks` carries a per-account limit of **30 a minute**. It
  is the only route that can reach `search.list`, which costs roughly a hundred times a `videos.list`. Thirty a
  minute is far above a human typing against a debounced client and far below a script, which is the reasoning
  SPEC-018 used for the vote limit. Owner accepted it with the plan.
---

# The catalog

## Goal

After this spec subject III is implemented rather than designed: `GET /api/v1/tracks` searches YouTube and
`GET /api/v1/tracks/{youtubeVideoId}` resolves one video, both behind one adapter, both in front of two cache
levels, and neither reachable without a bearer token. The quota becomes a measurable budget instead of a hope —
every provider call logs its unit cost, and `403 quotaExceeded` surfaces as `503` with its own problem type.

The contract for both routes is **already written** in `docs/api/ENDPOINTS.md` §3, so this unit implements it
and changes no contract.

## Out of scope

- **Pointing `DATABASE_URL` at Supabase.** The delivery plan's S4 row mentions it. It changes the connection
  ceiling issue O22 describes, and `load-testing` requires a re-measurement after a change like that, so it is
  its own unit with its own Gatling run.
- **Resolving metadata inside a write path.** Decision 4 explains why: a provider outage must not stop an event.
- **The client's debounce.** `youtube-integration` requires it; `MusicRoomApp/` is empty and the client
  workstream starts after S9. The server-side half — the rate limit and both caches — is here.
- **Playback.** The client plays through the YouTube player SDK. The backend already decides what and when
  (SPEC-021) and never touches media.

## Decisions taken under the approval of 2026-10-04

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **One adapter.** `ICatalogProvider` in `Application`, `YouTubeCatalogProvider` in `Infrastructure`, over a typed `HttpClient` carrying the key. | `youtube-integration`: all provider access behind one adapter, and nothing above `Infrastructure` learns that the catalog is YouTube — ADR-0028. The key stays server-side, which the subject makes a failing condition if broken |
| 2 | **Two cache levels, and the handlers see neither.** `ICatalog` is what a handler depends on; `CachedCatalog` implements it as L1 `IMemoryCache`, then the `track_refs` table, then the provider, populating both on the way back. | ADR-0023's lookup order, in one place. Putting it in the handlers would mean two copies of the quota rule and two chances to forget it |
| 3 | **The search mapping is cached in the database too**, as `track_search_cache(query_hash, video_ids, fetched_at_utc)`. | ADR-0023 requires the query-to-ids mapping to be cached so a repeated search does not re-issue `search.list`, and an in-memory-only mapping burns the quota again after every restart — the exact failure the ADR rejects for `TrackRef`. Restarts cluster right before a demo. Rejected: L1 only, which is that failure |
| 4 | **A write path never calls YouTube.** Suggesting or adding a track keeps the shape-only validation it has today; metadata is resolved on read from the cache. | `youtube-integration`: *"if YouTube is unavailable the feature degrades: queue and votes still work on cached `TrackRef` data."* Validating a video id against the provider would put an outage, and a quota ceiling, inside the one path a room full of people is using. Rejected: resolve-on-write, which trades a party for a tidier row |
| 5 | **`quotaExceeded` is `503` with its own problem type**, and a provider `5xx` is the same `503`. Neither is a `500`. | The contract says so, and the distinction a caller needs is "the catalog is unavailable, your request was fine", not a stack trace. A `500` would also claim the fault is ours |
| 6 | **Every provider call logs its unit cost** — 100 for `search.list`, 1 for `videos.list`. | `youtube-integration` asks for quota-relevant call counts to be logged so the budget is measurable *before* it is exhausted. Without the cost in the line, counting calls does not tell anyone how much of the day is left |
| 7 | **Entries refresh by age and nothing is invalidated by hand.** A `TrackRef` is re-fetched after 30 days, a search mapping after 6 hours. | ADR-0023's rule. Track metadata barely changes and a stale thumbnail is not a defect; a search mapping is the volatile half, because new uploads should appear the same day |
| 8 | **`GET /tracks` carries a per-account limit of 30 a minute.** `GET /tracks/{id}` does not. | Search is the only expensive call. The id route prefers the cache and costs one unit when it misses, so limiting it would buy nothing and break a legitimate page that resolves twenty rows |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0003 chose YouTube and ADR-0023 chose the two levels. This implements both |
| 2 | new or upgraded dependency? | **yes, and it ships** — `Microsoft.Extensions.Http.Resilience` for the timeout, the retry and the circuit breaker `youtube-integration` requires. Owner chose it over hand-rolling |
| 3 | schema change beyond this spec, or destructive migration? | **a schema change, additive** — two new tables, `track_refs` and `track_search_cache`. No existing table is touched, so nothing migrates and nothing is dropped |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — §3 already specifies both routes, their statuses and the `TrackRef` shape. This unit makes the document true |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes, secrets** — the unit holds the Data API key. Both routes also require a bearer token. Neither is auditable: reading the catalog changes nothing |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/TrackRef.cs` | create — the cached reference: video id, title, channel, duration, thumbnail |
| `MusicRoom/src/MusicRoom/Domain/Entities/TrackSearchCache.cs` | create — one row per searched query |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Catalog/ICatalogProvider.cs` | create — what YouTube is, to this application |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Catalog/ICatalog.cs` | create — what a handler depends on, caching included |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Catalog/CatalogContracts.cs` | create — `TrackMetadata`, `CatalogPage`, and the unavailable outcome |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/ITrackRefRepository.cs` | create — the L2 read and write |
| `MusicRoom/src/MusicRoom/Application/Features/Catalog/Queries/SearchTracks/SearchTracksQuery.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Catalog/Queries/SearchTracks/SearchTracksQueryHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Catalog/Queries/SearchTracks/SearchTracksQueryValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Catalog/Queries/GetTrack/GetTrackQuery.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Catalog/Queries/GetTrack/GetTrackQueryHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Infrastructure/Catalog/YouTubeCatalogProvider.cs` | create — the two provider calls, and nothing else |
| `MusicRoom/src/MusicRoom/Infrastructure/Catalog/YouTubeResponses.cs` | create — the provider's wire shapes |
| `MusicRoom/src/MusicRoom/Infrastructure/Catalog/CachedCatalog.cs` | create — L1, L2, provider, in that order |
| `MusicRoom/src/MusicRoom/Infrastructure/Catalog/CatalogOptions.cs` | create — key, lifetimes, timeout |
| `MusicRoom/src/MusicRoom/Infrastructure/Catalog/CatalogRegistration.cs` | create — the typed client, the resilience pipeline, the decorator |
| `MusicRoom/src/MusicRoom/Persistence/Configurations/TrackRefConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Configurations/TrackSearchCacheConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/TrackRefRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<stamp>_Catalog.cs` | create — two additive tables |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — two sets |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — a catalog repository group |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — delegates to `CatalogRegistration` |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/TrackEndpoints.cs` | create — the two routes of ENDPOINTS §3 |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CatalogRateLimiterPolicies.cs` | create — 30 a minute per account, search only |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — the policy, the routes, and the catalog options |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Catalog/YouTubeCatalogProviderTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Catalog/CachedCatalogTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/TrackEndpointTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/CatalogRequests.cs` | create — the helper the endpoint suite shares |
| `MusicRoom/Directory.Packages.props` | change — the resilience package |
| `MusicRoom/src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj` | change — the same |
| `.env.example` | change — `YOUTUBE_API_KEY` documented, and the two lifetimes |
| `docs/SDD.md` | change — §9.3, §16's `III` row, and the catalog's own note |
| `docs/owasp/THREAT-REGISTER.md` | change — T-10 gains its mitigation and its evidence |
| `docs/DELIVERY-PLAN.md` | change — S4 |
| `brain/projects/musicroom-backend.md` | change — S4 |
| `docs/specs/SPEC-025-youtube-catalog.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Two domain entities, three `Application` abstractions and two query features, five
`Infrastructure` types, two configurations and a repository in `Persistence`, two routes and a policy in
`WebAPI`. No new project.

**2. Boundary types.** `ICatalog`, `ICatalogProvider` and the contract records are `public`, because
`Application` declares them and `Infrastructure` implements them. Every implementation is `internal`.

**3. Invoked?** `GET /api/v1/tracks` and `GET /api/v1/tracks/{youtubeVideoId}`, both authenticated.

**4. CLI or package.** `Microsoft.Extensions.Http.Resilience` — gate 2.

**5. corePackages.** Unchanged.

**What the walk caught.** Four things.
- **The cache is not a handler's business.** `ICatalog` exists so the quota rule has one home; without it both
  handlers would each decide when to call YouTube, which is how a quota gets spent twice.
- **A `TrackRef` row is both the cache and the reference data.** ADR-0023 says so, and it is why the table is
  needed even if the provider were never called again: playlist and event rows already carry video ids with no
  metadata beside them.
- **The write paths stay untouched**, which is decision 4 and the reason this unit adds no risk to S5 to S7.
- **`Main` is at CA1506's limit**, so the routes and the policy are mapped from a helper, the way SPEC-024's
  `InfrastructureSetup` and SPEC-021's playback routes already are.

## Steps

1. The two entities, their configurations, the context sets and one additive migration.
2. `ICatalogProvider`, `ICatalog`, the contracts, and `ITrackRefRepository`.
3. `TrackRefRepository`: read a reference by id, read a search mapping, upsert both.
4. `YouTubeCatalogProvider`: `search.list` and `videos.list`, the unit cost logged, `403 quotaExceeded` and a
   provider `5xx` mapped to one unavailable outcome.
5. `CachedCatalog`: L1, L2, provider, populating both; and `CatalogRegistration` with the typed client, the
   resilience pipeline and the options.
6. The two queries and their handlers, then `TrackEndpoints` and the rate-limit policy.
7. Tests: the provider against a stubbed transport, the cache's lookup order and its quota behaviour, and the
   two routes end to end.
8. `make agent-check`, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `YouTubeCatalogProviderTests` — the request | `search.list` and `videos.list` are called with the key, the id list and the documented parts |
| — `403 quotaExceeded` | becomes the unavailable outcome, not an exception that reaches a caller as `500` |
| — a provider `5xx` | the same outcome, and the retry is bounded |
| — the log | the unit cost is in the line, and the key is in no line |
| `CachedCatalogTests` — the order | L1 hit calls neither L2 nor the provider; L2 hit calls the provider never; a miss calls the provider once and populates both |
| — a repeated search | re-issues no `search.list`, which is the quota rule ADR-0023 exists for |
| — an aged entry | is re-fetched, and a fresh one is not |
| `TrackEndpointTests` | `200` with the `TrackRef` shape ENDPOINTS §3 states, `404` for a video the provider does not know, `503` when the catalog is unavailable, `401` without a token, and `422` for a malformed query or video id |
| — the limit | the thirty-first search in a minute is `429` with `Retry-After`, and another account's search is not |
| `SchemaConventionTests` | the two tables follow the naming rules, which is what it is for |
| `make agent-check` | the drift guards: a new endpoint means `docs/api/openapi.json` changes, and the regenerated document is committed with it |

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] `make agent-check` passes
- [ ] `docs/SDD.md` §16's `III` row says implemented, and T-10 carries its evidence
- [ ] the quota cost of a search is visible in the log
- [ ] `status: done` in this file
