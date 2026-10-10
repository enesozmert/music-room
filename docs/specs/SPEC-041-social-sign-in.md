---
id: SPEC-041
title: Sign in with Google — one validation core, the first of two acquisitions
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0008, ADR-0031, ADR-0045, ADR-0026]
standards: [authentication-authorization, social-oauth, application-security, testing]
approved_by: enesozmert # gates 1, 4 and 5 answered on 2026-10-10
escalation: |
  **Gates 1, 4 and 5 are yes, and gate 1 is a contradiction the owner has to settle before any code.**

  **The contract and the decision disagree about what crosses the wire on the Google path.**
  `docs/api/ENDPOINTS.md` §1 publishes `POST /auth/sessions/social` with a body of `provider` and **`idToken`**.
  [[ADR-0008-oidc-social-login]] says the Google acquisition is *"authorization code plus PKCE `S256` through
  the token endpoint"* and that *"the client secret stays on the server"*. Those are two different designs and
  only one of them can be built:

  | | **A — the app sends an ID token** | **B — the app sends a code** |
  |---|---|---|
  | Request | `{provider, idToken}`, as published today | `{provider, code, codeVerifier, redirectUri}` |
  | Who exchanges the code | the app, through Google's mobile SDK | **this API**, with `GOOGLE_CLIENT_SECRET` |
  | What `GOOGLE_CLIENT_SECRET` is for | nothing — a public client holds none | the exchange |
  | What proves the flow ran in our app | `aud` and `nonce` | the exchange itself, plus `aud` and `nonce` |
  | Accepted `aud` | the **Android and iOS** client ids | the **Web** client id |
  | Facebook | identical shape | still A — Limited Login publishes no token endpoint |

  **B is the stronger of the two and the one this spec proposes.** Under A, anything holding a Google ID token
  minted for our mobile client ids can present it; `aud` and `nonce` are the only things standing there, and
  `nonce` only binds a token to a login this API started. Under B the code is exchanged by this API with a
  secret the app never sees, so possession of a valid code **and** the verifier that matches the challenge is
  evidence the flow ran where we started it. ADR-0008's own sentence — the secret stays on the server — only
  means something under B.

  **What B costs:** `docs/api/ENDPOINTS.md` changes, which is gate 4, and the owner has already created a
  **Web** OAuth client, which is the right kind for B. Under A that client would be the wrong kind and two
  more — Android and iOS — would be needed before anything could be tested.

  **What A buys:** the mobile SDKs make it one call, and the published contract already describes it.

  Gate 5 is a yes however this is answered: it is the sign-in path, it reads a provider secret, and it mints
  this API's own tokens.

  Gates 2, 3, 6 and 7 answer no. No package — the ID token is validated against a JWKS fetched over the
  `HttpClient` the catalog adapter already uses, and the JWT handler is the one `AddJwtBearer` already brings.
  A migration is needed for the social-link table and it is this spec's own, not beyond it. Nothing lowered,
  deleted or skipped. Nothing from `reference/`.

  **Facebook is deliberately not in this unit.** Its Limited Login yields an ID token with no code exchange,
  so it is the second acquisition against the same core, and it cannot be tested at all until the mobile client
  exists — the ADR's own measurement says Facebook publishes no `token_endpoint` and no `code` response type.
  Shipping the core with one acquisition proves the core; adding the second then proves the core was general.

  **Owner's answers, 2026-10-10: B, and the two proposals as written. Nothing beyond the subject.**

  1. **B.** The app sends `code` and `codeVerifier`; this API exchanges them at Google's token endpoint with
     `GOOGLE_CLIENT_SECRET`. `docs/api/ENDPOINTS.md` changes to match, which is gate 4 and is answered here.
     ADR-0008's sentence about the secret staying on the server now describes something that happens.
  2. **A social sign-in creates an account, and that account has no password.** `password_hash` becomes
     nullable — a sentinel string would be a landmine the first time something compared against it. Password
     sign-in for such an account answers the same generic `401` every wrong credential answers, because telling
     a caller *"this address exists but has no password"* is an enumeration oracle. `POST /me/password`
     answers `422` with `CURRENT_PASSWORD.REQUIRED`'s sibling, and setting a first password is the reset flow,
     which already proves the address.
  3. **The `nonce` lives in the database**, single-use, with a short expiry, beside the authentication it
     belongs to. A second dependency shaped like Redis for a sixty-second value is the wrong trade at this
     size, and ADR-0023 already refused that shape for a bigger reason.

  **Scope is the subject and nothing else.** V.1 asks for two things — registering with a social account and
  linking one to an existing account — and this unit builds those two and the four routes
  `docs/api/ENDPOINTS.md` already publishes for them. No provider half, no Facebook, no endpoint the contract
  does not already name.
---

# Sign in with Google

## Goal

After this spec a person can sign in with Google, and an existing account can link a Google identity. The
validation core names no provider: a provider is a configuration row — its discovery URL, its client id, and
how its ID token arrives — and adding Facebook later is a row plus an acquisition, not a second integration.

What the API issues after validation is what it issues today: its own access token and opaque refresh token.
The provider authenticates; this API authorises.

## Out of scope

- **The OpenID Provider half.** `/.well-known/openid-configuration`, JWKS, `/oauth2/authorize`,
  `/oauth2/token` and UserInfo are [[ADR-0031-musicroom-as-openid-provider]]'s surface, they need the RS256
  key pair that issue **O19** tracks, and that key belongs in the vault S1b has not built. Its own spec.
- **Facebook.** The second acquisition, named in the escalation.
- **Removing anything.** `POST /auth/sessions` and the rest of S1's surface stay exactly as they are. ADR-0031
  replaces them, and that replacement is the provider half's migration, not this one's.

## The migration plan ADR-0031 requires

ADR-0031 says *"the migration is part of S8's spec: the endpoints S1 shipped are replaced, not kept
alongside"*. S8 is two specs, so the plan is stated here in full and executed by the second:

| Phase | What happens | When it is safe |
|---|---|---|
| 1 — **this spec** | the social routes are **added**. Nothing is removed; password sign-in is untouched | always: it is additive |
| 2 — the provider spec | `/oauth2/authorize` and `/oauth2/token` are added beside `POST /auth/sessions` and `POST /auth/tokens`, both surfaces live | when the provider's own tests pass |
| 3 — the provider spec | the mobile client moves to the authorization endpoint | when a client exists to move. Until then phase 3 does not start, and the old endpoints are the only ones anything uses |
| 4 — the provider spec | `POST /auth/sessions`, `POST /auth/sessions/social` and `POST /auth/tokens` are removed, with their tests and their §16 rows | only after phase 3, and never in the same pull request as phase 2 |

**Nothing is deleted before its replacement passes**, which is the ADR's rule and the reason the phases are
numbered rather than described.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — the contract and ADR-0008 disagree about the Google request shape. See `escalation` |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — one additive table, `social_links`, and no column is dropped |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — under B the social route's body changes from `idToken` to a code and a verifier |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the sign-in path |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Three yes answers, so Tier B. All three were answered by the owner on 2026-10-10 — B, and the two proposals
as written. The Files table below is written against that answer.

## What the core does, once, for every provider

1. Fetch the provider's discovery document and cache it; fetch the JWKS from it and cache that with respect to
   key rotation.
2. **Exchange** — Google only, and only under B: `code` plus `code_verifier` at the provider's
   `token_endpoint`, with the client id and secret. The response carries the ID token.
3. Validate the ID token, in this order and failing closed at each step: signature against the JWKS; `iss`
   exactly the expected issuer; `aud` exactly our client id; `exp` and `iat` within tolerance; `nonce` matches
   the single-use value this API generated; `email_verified` true before an email is used to match.
4. Resolve the identity: `(provider, providerUserId)` maps to at most one local account. An unverified email
   never matches an existing account — that is the account-takeover path ADR-0008 names.
5. Issue this API's own tokens, exactly as password sign-in does.

Step 3 is the part that names no provider. Steps 2 and 4 are where a provider row is read.

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/SocialLink.cs` | create — `(provider, providerUserId)` to one account, unique on the pair |
| `MusicRoom/src/MusicRoom/Domain/Entities/SocialAuthentication.cs` | create — the single-use `nonce` and its expiry, answer 3 |
| `MusicRoom/src/MusicRoom/Domain/Entities/User.cs` | change — `PasswordHash` becomes nullable, answer 2 |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Identity/ISocialIdentityProvider.cs` | create — the core's seam: a code and a verifier in, a validated identity out |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/ISocialLinkRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/SignInWithSocial/` | create — command, handler, validator |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/LinkSocialAccount/` | create — command, handler, validator |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/UnlinkSocialAccount/` | create — command, handler, validator |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Queries/ListSocialLinks/` | create — query and handler |
| `MusicRoom/src/MusicRoom/Infrastructure/Identity/OpenIdConnectIdentityProvider.cs` | create — **the core**: discovery, JWKS with rotation, the exchange, the six checks. Names no provider |
| `MusicRoom/src/MusicRoom/Infrastructure/Identity/SocialProviderOptions.cs` | create — a provider is a configuration row |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — register the core and the Google row |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/SocialLinkRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — two `DbSet`s and their configuration |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/` | create — one additive migration: two tables, one column made nullable |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/SocialAuthEndpoints.cs` | create — the four routes the contract already publishes |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — map the new group |
| `MusicRoom/src/MusicRoom/Application/Validation/ValidationCodes.cs` | change — the codes these routes answer with |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/SocialSignInTests.cs` | create — the handler against a substituted provider |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Identity/OpenIdConnectIdentityProviderTests.cs` | create — the six checks, each failing closed |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SocialAuthEndpointTests.cs` | create — the four routes end to end |
| `docs/api/ENDPOINTS.md` | change — the social route's body, answer 1 |
| `docs/api/openapi.json` | change — regenerated |
| `brain/migrations/migrations-index.md` | change — the migration's row |
| `docs/SDD.md` | change — §10.2, §16's two V.1 rows |
| `docs/specs/SPEC-041-social-sign-in.md` | change — status transitions |

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `OpenIdConnectIdentityProviderTests` — six tests, one per check | a bad signature, a wrong `iss`, a wrong `aud`, an expired token, a replayed `nonce` and an unverified email are each refused **on their own**, so no check is carrying another |
| — the same core with two configuration rows | the core names no provider: the second row is Facebook's and it runs the same code |
| `SocialSignInTests` | an unknown `(provider, providerUserId)` creates an account with no password; a known one signs in; an unverified email never matches an existing account |
| `SocialAuthEndpointTests` | the four routes answer what the contract says, and a social-only account's `POST /me/password` answers `422` rather than `401` |
| the 600 tests already passing | password sign-in is untouched: this unit adds, it removes nothing |
| `make agent-check` | the regenerated document matches, and the migration applies |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes — 621 tests, 0 failed
- [x] **a provider is a configuration row**, proven by `TwoRows_RunTheSameCoreAsync`: the second row differs
  only in its name and nothing in the path behaves differently for it
- [x] **each of the six checks refuses on its own.** Every forged token is valid in every respect except the
  one its test is about, so removing a check fails exactly one test. Proven by mutation:
  `ValidateAudience = false` failed `ATokenForAnotherAudience_IsRefusedAsync` and nothing else
- [x] nothing from S1's surface was removed — phase 1 of the migration plan is additive
- [x] `status: done` in this file
