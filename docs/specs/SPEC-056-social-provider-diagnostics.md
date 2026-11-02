---
id: SPEC-056
title: A social sign-in that says it is not configured, before a client finds out
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.4, V.8]
decisions: [ADR-0008, ADR-0046]
standards: [social-oauth, logging-observability, application-security]
approved_by: enesozmert # 2026-11-03, after the front-end team hit PROVIDER.UNKNOWN on the live API
escalation: |
  **Gate 4 is a yes** — `/health/ready` gains a member. Gates 1, 2, 3, 5, 6 and 7 are no.

  **Third time, same shape.** `YOUTUBE_API_KEY` was never set on the live service, so every search
  answered `503` and nothing said why (SPEC-053). `ASPNETCORE_FORWARDEDHEADERS_ENABLED` was never set,
  so every token carried an `http://` issuer and nothing said why (#106). Now the four `GOOGLE_*`
  variables were never set, so every social sign-in answered `422 PROVIDER.UNKNOWN` — and the way that
  was discovered is that **the front-end team reported it as a bug in the API**.

  It is not a bug. `SocialProviderConfiguration.Read` requires all four variables together and returns
  an empty set when any is missing, deliberately: a provider with half a row is worse than no provider,
  and refusing to boot would make Google credentials a requirement for running the project at all.
  What is missing is that a host could not be *asked* whether it had a provider. The only way to find
  out was to attempt a sign-in and read the refusal — which is how an API tells a stranger something
  about its configuration, and therefore the one audience that must not be the first to learn it.

  **This applies SPEC-053's answer to the same class of defect**, with the same two rules. The probe
  reports **configuration only** — it never calls Google's discovery endpoint, because a readiness
  probe that reaches a third party on every call is an outage waiting to be imported. And it is
  `Degraded`, never `Unhealthy`: password sign-in, the whole product surface and the OIDC provider
  work without a social provider, so a host with none is a host that serves.
---

# A social sign-in that says it is not configured, before a client finds out

## Goal

`/health/ready` reports whether this host has a social provider, without calling one and without
failing readiness. A start-up line says the same thing once, naming the four variables when there is
none. A client never has to be the instrument that discovers a missing variable.

## Out of scope

- **Making `PROVIDER.UNKNOWN` say more.** It is the right answer to the caller: the set of providers
  an API supports is published, and which variables a deployment forgot is nobody else's business.
- **Calling the provider's discovery document from a probe.** The reasoning is the escalation's.
- **Refusing to boot without a provider.** ADR-0008's amendment allows a host with no social provider,
  and SPEC-041 built it that way on purpose.
- **A second provider.** Facebook is four more variables and no code change; it is not this unit.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — `/health/ready` gains a `social` member |
| 5 | touches authn, authz, tokens, secrets, audit? | **it reports on them** — whether a provider is configured, never a client id, never a secret, never a fragment of one |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The probe reports configuration, never a call.** | A readiness probe that fetched Google's discovery document would import a third party's outage, and on a tier that wakes a sleeping instance per request it would fetch it at a rate nobody chose |
| 2 | **`Degraded`, never `Unhealthy`.** | Passwords, profiles, playlists, events, devices and the OIDC provider all work without Google. A host with no social provider serves |
| 3 | **The member names the providers it has, not their credentials.** | `google` is public — it is in the request body a client sends. A client id is not, and neither is the first six characters of one |
| 4 | **A start-up line names the four variables when there is none.** | The cheapest possible answer to "why does social sign-in refuse": it is in the first screen of logs after a deploy, before anybody reproduces anything |
| 5 | **One registration method for both optional dependencies.** | `catalog` and `social` answer the same kind of question, and a second place to register a probe is a second place for the two to drift |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/SocialConfigurationCheck.cs` | create — configuration, never a call |
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/SocialStartupNotice.cs` | create — the start-up line |
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/HealthRegistration.cs` | change — registers both |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — passes the provider names |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HealthEndpointTests.cs` | change — reported, never fatal, and no credential in the body |
| `docs/api/ENDPOINTS.md` | change — the readiness body |
| `docs/deploy/RENDER.md` | change — the four variables, and the finding they produce |
| `docs/SDD.md` | change — §14.2.1, §16 |
| `docs/specs/SPEC-056-social-provider-diagnostics.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| no `GOOGLE_*` configured | start-up logs it naming the four variables; `social` reports `Degraded`; `/health/ready` is still `200` |
| all four configured | `social` reports `Healthy` and the body names `google` |
| the readiness body, with a provider configured | contains no client id and no part of one |
| `make smoke-live` | names a missing social provider the way it names a missing catalog key |

## Done when

- [x] `/health/ready` reports the social provider without calling one and without failing readiness
- [x] the start-up line names the four variables when there is no provider
- [x] `make agent-check` passes
- [x] `status: done` in this file
