---
id: SPEC-044
title: The authorization and token endpoints — code plus PKCE, and an ID token
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0031, ADR-0032, ADR-0043, ADR-0045]
standards: [authentication-authorization, application-security, rest-api-design, testing]
approved_by: enesozmert # gates 1, 4 and 5 answered on 2026-10-11
escalation: |
  **Gate 1 is a yes, and it is a contradiction the owner has to settle before any code — the same shape
  SPEC-041 found, in the other half of S8.**

  `docs/api/ENDPOINTS.md` publishes `GET /oauth2/authorize` with auth **`anon`**, answering `302` to the
  redirect URI with a code. That is the standard shape, and it assumes the authorization endpoint can
  **authenticate a person**: in every conforming provider, an anonymous caller arriving there is shown a login
  page, signs in, and is redirected back.

  **This API has no login page, no cookie session and no HTML.** It is a JSON API by design — SDD §9.1,
  and `docs/api/ENDPOINTS.md` §10 lists "media streaming or proxying" and server-rendered anything among what
  is deliberately absent. An anonymous `GET /oauth2/authorize` therefore has nobody to issue a code for.

  Three ways out, and only one is small:

  | | **A — authorize requires a bearer token** | **B — authorize renders a login page** | **C — token endpoint only** |
  |---|---|---|---|
  | What the app does | signs in with the S1 surface, then calls authorize with its access token | opens a browser, sees HTML served by this API, signs in there | no authorization endpoint at all |
  | Contract change | `anon` → `auth` on one row | none | the row is removed |
  | What this API grows | nothing | HTML views, a cookie session, CSRF, a password form, and a second place that authenticates | nothing |
  | Conformance | a **first-party** provider: our own app can use the code flow, a third party cannot | a general-purpose provider | not a provider in any useful sense |
  | Honest claim at defence | "the provider surface exists and our client drives it" | "the provider surface exists" | "we have a token endpoint" |

  **A is the recommendation.** It keeps this a JSON API, it needs no second authentication path — the thing
  most likely to diverge from the first and quietly weaken it — and it is truthful: nobody but our own client
  was ever going to drive this provider. The cost is that it is not a provider a third party could federate
  with, and that sentence belongs in ADR-0031 as an amendment rather than being discovered by a reader of the
  discovery document.

  **B is rejected unless the owner wants it.** Subject VI credits extra work only when the mandatory part is
  flawless; building a login UI inside an API that deliberately has none, to serve a provider role that is
  already beyond the subject, is the definition of the wrong order.

  Gates 4 and 5 are also yes: the contract row changes under A, and this is the credential path. Gates 2, 3,
  6 and 7 answer no — no package, one additive table for authorization codes, nothing lowered or skipped,
  nothing from `reference/`.

  **Owner's answers, 2026-10-11: A, and both proposals as written.**

  1. **A.** `GET /oauth2/authorize` requires a bearer token; the contract's auth column changes from `anon` to
     `auth`. This API stays a JSON API and grows no second authentication path — the thing most likely to
     diverge from the first and quietly weaken it. What it costs is stated rather than discovered: **this is a
     first-party provider**. Our own client can drive the code flow; a third party cannot, because there is
     nowhere for a person who is not already signed in to sign in. ADR-0031 gains that sentence as an
     amendment in this unit.
  2. **Clients are configuration, not a table.** One row for the mobile client, read the way the social
     providers are. A registry nobody can self-serve into is a list of one, and a list of one in a table is a
     migration plus a seed plus an admin surface that does not exist.
  3. **The access token stays HMAC here.** Changing the signature of a token `AddJwtBearer` already validates,
     in the unit that introduces a new flow, is two migrations in one diff. It moves in phase 4, with the old
     endpoints.

  **Two more answers the owner should give, because they shape the Files table:**

  1. **Which clients exist?** A code flow needs a `client_id` and a registered `redirect_uri` to compare
     against — an unregistered redirect URI is the classic open redirect. The spec proposes **configuration,
     not a table**: one row for the mobile client, read like the social providers are, because a client
     registry nobody can self-serve into is a list of one.
  2. **Does the access token become RS256 now, or stay HMAC until the old endpoints go?** ADR-0031 keeps the
     ID token and the access token separate but says nothing about when the access token's signature moves.
     The spec proposes **it stays HMAC in this unit**: changing the signature of a token that `AddJwtBearer`
     already validates, in the same unit that introduces a new flow, is two migrations in one diff. It moves
     in phase 4, when the old endpoints go.
---

# Authorization and token

## Goal

After this spec a client can run the authorization code flow with PKCE against this API and receive an ID
token signed with the key SPEC-043 published. The two endpoints the discovery document does not yet advertise
start answering, and the document grows to name them — in the same unit, because
`OpenIdMetadataEndpointTests.EveryAdvertisedUrl_AnswersAsync` makes any other order fail.

## Out of scope

- **UserInfo and revoke.** The last third, their own spec.
- **Removing anything from S1's surface.** Phase 2 of SPEC-041's migration plan: both surfaces live. Phase 4
  removes the old one, after a client has moved.
- **Consent.** A first-party client asking its own backend for a token has nobody to consent to.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — the contract says the authorization endpoint is anonymous and this API cannot authenticate an anonymous caller. See `escalation` |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — one additive table for authorization codes |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes**, under answer A: one auth column |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the credential path |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions this spec takes under answer A

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **`GET /oauth2/authorize` requires a bearer access token** and answers `302` to a **registered** redirect URI with a code. | A redirect URI compared against a registered list is the difference between a provider and an open redirect. An unregistered URI is refused with `400` **without redirecting** — redirecting an error to an unverified URI is the vulnerability itself |
| 2 | **`code_challenge_method=S256` is required; `plain` is refused.** | Already published in the contract, and the reason PKCE exists: `plain` is a challenge an interceptor can replay |
| 3 | **The code is single-use, 60 seconds, and bound to four things**: the client, the redirect URI, the challenge and the account. | Single use arbitrated by the database — `consumed_at_utc IS NULL` in the update's predicate — rather than by a read-then-write check, the same posture the verification token uses |
| 4 | **The ID token is RS256, signed with SPEC-043's key**, and carries `iss`, `sub`, `aud`, `exp`, `iat`, `auth_time` and `nonce` when one was sent. | ADR-0031's list exactly. `nonce` is echoed rather than invented: a client sent it to bind this token to its own request |
| 5 | **`grant_type=refresh_token` reuses S1's rotation**, rather than growing a second refresh mechanism. | ADR-0032's family revocation is the session-theft detector. Two refresh paths would mean one of them eventually stops revoking |
| 6 | **The discovery document grows in this unit**, naming both endpoints and the grants it now supports. | `EveryAdvertisedUrl_AnswersAsync` makes any other order fail, which is what that test is for |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/AuthorizationCode.cs` | create — the code, its binding and its single-use stamp |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IAuthorizationCodeRepository.cs` | create — the interface and its one return record |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Identity/IOpenIdClientRegistry.cs` | create — a client, and the exact redirect-URI match |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/IIdTokenIssuer.cs` | create — the claim set as a record, because five parameters is the limit |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/PkceChallenge.cs` | create — one definition of `S256`, compared in constant time |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/AuthorizeClient/` | create — command, handler, result |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ExchangeAuthorizationCode/` | create — command, handler, result |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/AuthorizationCodeConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/AuthorizationCodeRepository.cs` | create — issue, and spend once |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — one registration |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/20261011133236_AuthorizationCodes.cs` | create — one additive table |
| `MusicRoom/src/MusicRoom/Infrastructure/Identity/OpenIdClientOptions.cs` | create — the registry, read from two variables |
| `MusicRoom/src/MusicRoom/Infrastructure/Identity/IdTokenIssuer.cs` | create — RS256, ADR-0031's claim list |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/RsaSigningKeyProvider.cs` | change — the key carries its own `kid`, so the handler writes the header |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — two registrations |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/OAuth2Endpoints.cs` | create — authorize |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/OAuth2TokenEndpoint.cs` | create — token, both grants, RFC 6749 error bodies |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/OpenIdMetadataEndpoints.cs` | change — the document names both endpoints, and maps them |
| `MusicRoom/src/MusicRoom/Application/Validation/ValidationCodes.cs` | change — `CLIENT.UNKNOWN`, `REDIRECT_URI.UNREGISTERED` |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Identity/IdTokenIssuerTests.cs` | create — the claims, the echoed nonce, the signature and the `kid` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2Driver.cs` | create — one definition of how a client drives this flow |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2FlowTests.cs` | create — the flow end to end, and the refresh grant |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2RefusalTests.cs` | create — each refusal on its own |
| `brain/decisions/ADR-0031-musicroom-as-openid-provider.md` | change — the first-party amendment, answer 1 |
| `docs/api/ENDPOINTS.md` | change — the auth column, and four notes saying why |
| `docs/api/openapi.json` | change — regenerated |
| `brain/migrations/migrations-index.md` | change — the migration's row |
| `.env.example` | change — the client's two variables |
| `docs/SDD.md` | change — §13.1, §16 |
| `docs/specs/SPEC-044-authorization-and-token.md` | change — status transitions |

**Not changed, against the first draft of this table.** `SocialProviderConfiguration.cs` stays as it
is: the registry reads its two variables through `ISecretProvider` the way `RsaSigningKeyProvider`
reads its key, so nothing had to grow `InfrastructureOptions`, which is already past the argument
limit it answers with one record.

## Verification

| Check | Proves |
|---|---|
| the flow, end to end | authorize issues a code and token exchanges it for an access token, an ID token and a refresh token |
| a code spent twice | the second attempt is `invalid_grant`, arbitrated by the database rather than by a read |
| a wrong `code_verifier` | refused — the whole point of PKCE, and a test that only exercised the happy path would miss it |
| `code_challenge_method=plain` | refused at the authorization endpoint |
| an unregistered `redirect_uri` | `400` **without** redirecting, so an error is never sent to a URI nobody registered |
| an expired code | refused after 60 seconds |
| the ID token | carries ADR-0031's claims, echoes the `nonce`, and verifies against the published JWKS |
| `EveryAdvertisedUrl_AnswersAsync` | still passes with two more endpoints in the document |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes — 652 tests, 0 failed, on 2026-10-11
- [x] each refusal is tested on its own, so removing one check fails exactly one test
- [x] ADR-0031 says this is a first-party provider
- [x] `status: done` in this file
