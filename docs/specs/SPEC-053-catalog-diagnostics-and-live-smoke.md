---
id: SPEC-053
title: A 503 that says why, and a smoke test the deployment can answer
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.4, V.7, V.8]
decisions: [ADR-0003, ADR-0023, ADR-0046]
standards: [application-security, testing, ci-cd-containerization]
approved_by: enesozmert # 2026-10-17, after the live catalog answered 503 and nothing said why
escalation: |
  **Gate 4 is a yes** — `/health` gains a member. Gates 1, 2, 3, 5, 6 and 7 are no.

  **This unit exists because of a defect that was not in the code.** `YOUTUBE_API_KEY` was never set on
  the live deployment, so every catalog request answered `503`. The response was correct. What was
  missing is that **nothing could tell the operator which of three things had happened**:

  1. no key is configured, so the provider was never called;
  2. the provider was called and could not be reached;
  3. the provider refused because the daily quota is spent.

  **A fourth was found while implementing this unit, by running the smoke it adds:** the provider
  answers `400` for a key that is malformed, expired, or restricted to referrers or addresses the host
  is not among. The development stack's key was in exactly that state while the live one worked, and
  the only trace was an `Information` line saying the call answered 400. It is now its own warning,
  because "the key is wrong" and "the quota is spent" ask for completely different actions.

  All four are one `CatalogResult.Unavailable()` and one `503`. **To a caller that is right** — which of
  them it is describes this deployment's configuration, and a caller is not owed that. To the person
  running the service it is useless, and the owner found it by asking rather than by reading a log.

  **Two things this unit deliberately does not do.**

  **It does not make the catalog part of readiness.** Signing in, profiles, playlists, events, votes,
  devices and playback do not touch YouTube; only search does. A readiness probe that fails when a third
  party is unwell takes this service out of rotation for somebody else's outage. The catalog's state is
  reported, and reporting is not the same as failing.

  **It does not call YouTube from a health check.** A `search.list` costs **100 of the 10,000 daily quota
  units** (ADR-0023). A probe that called the provider would spend the quota it exists to watch, and on a
  platform that wakes a sleeping container on every request it would do so unpredictably. The check
  reports whether a key is **configured**; whether the provider answers is what a real search finds out,
  and that path now says so in the log.

  **The live smoke is thin on purpose.** The 717 tests in `make agent-check` prove the rules against a
  real engine; repeating them in shell against production would duplicate the weaker half and leave
  accounts, playlists and events behind in a database that is not disposable — and send real mail on
  every run. What a deployment smoke proves is **configuration**: HTTPS, the issuer, the published keys,
  the database, the schema, the third parties. It uses one account that is created once and reused.
---

# A 503 that says why, and a smoke test the deployment can answer

## Goal

When the catalog answers `503`, the log says which of the three causes it was. `/health` reports whether
a catalog provider is configured without calling it and without affecting readiness. `make smoke` runs a
short, repeatable check against any deployment and names what is wrong when something is.

## Out of scope

- **Making the response say more.** `503` and its ProblemDetails stay exactly as they are: which cause
  applied is the deployment's business, not the caller's.
- **Calling the provider from a health check.** The quota reasoning is in the escalation.
- **Full product flows against production.** They belong in the suite and in `make e2e`, against a
  disposable database. Growing `make e2e` to cover playlists, events and devices is its own unit.
- **Alerting.** A log line an operator can find is this unit; something that pages somebody is not.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — `/health` gains a `catalog` member |
| 5 | touches authn, authz, tokens, secrets, audit? | no — it reports whether a key exists, never its value |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The four causes are distinguished in the log, not in the response.** | A caller learning that a key is absent learns something about this deployment's configuration. An operator reading the log is the audience |
| 2 | **A start-up line states whether the catalog has a provider.** | The cheapest possible answer to "why is search dead": it is in the first screen of logs after a deploy, before anybody has to reproduce anything |
| 3 | **`/health` gains `catalog`, reporting configuration only.** | A check that called YouTube would spend 100 quota units per probe — the quota it is watching |
| 4 | **The catalog never makes readiness unhealthy.** | Only search touches it. `Degraded` says so without taking the service out of rotation for a third party |
| 5 | **The smoke test is thin and read-mostly, with one reusable account.** | A full product run against production leaves rows nothing deletes and sends real mail. The suite already proves the rules |
| 6 | **It takes a base URL**, so the same script checks the composed stack and the deployment. | A check that only runs against production is one nobody runs before deploying |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Infrastructure/Catalog/YouTubeCatalogProvider.cs` | change — each failure logs which one it was |
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/CatalogConfigurationCheck.cs` | create — configuration, never a call |
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/CatalogStartupNotice.cs` | create — the start-up line |
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/HealthRegistration.cs` | create — moved out of `Program.cs`, which was at the 300-line wall, and registers both |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — passes whether a provider is configured |
| `scripts/smoke.sh` | create — the deployment check |
| `Makefile` | change — `smoke`, and `smoke-live` |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Catalog/YouTubeCatalogProviderTests.cs` | change — the log says which cause |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HealthEndpointTests.cs` | change — catalog is reported and readiness survives it |
| `scripts/e2e-oidc.sh` | unchanged — the product flow stays there, against a disposable stack |
| `docs/api/ENDPOINTS.md` | change — the health response |
| `docs/deploy/RENDER.md` | change — the variable that was missing, and how to see it |
| `docs/SDD.md` | change — §14, §16 |
| `docs/specs/SPEC-053-catalog-diagnostics-and-live-smoke.md` | change — status transitions |

## Two deviations from the plan above

**`CatalogOptions` gained nothing.** The plan said "a reason, not just a boolean". `HasProvider` is
enough: the four causes are told apart at the point each one happens, where the status code is in hand,
and a reason carried on the options record would only restate the boolean a few lines later.

**The start-up notice lives in `WebAPI`, not in `CatalogRegistration`.** An `IHostedService` in
`Infrastructure` would mean a new package reference there — gate 2 is a no — so the notice sits beside
the health check that answers the same question, registered in the same method so the two cannot drift.

## Verification

| Check | Proves |
|---|---|
| no key configured | start-up logs it; `/health` says `not configured`; `/health/ready` is still healthy |
| a key configured, provider unreachable | the log distinguishes it from "not configured" |
| a `403 quotaExceeded` | the log says quota, not "unreachable" |
| the `503` response body | unchanged in all three cases |
| `make smoke` against the composed stack | passes |
| `make smoke-live` | passes against the deployment, and names what is wrong when it does not |
| running the smoke twice | no new account, no mail, same result |
| a key the provider rejects | the log says the key, not the quota — found live on the development stack |

## Done when

- [x] four causes are distinguishable in the log and identical in the response
- [x] `/health/ready` reports the catalog without calling it and without failing readiness
- [x] `make smoke` passes against the composed stack; `make smoke-live` passes against the deployment
      once it carries this code, and already names a missing `catalog` member on the version before it
- [x] `make agent-check` passes
- [x] `status: done` in this file
