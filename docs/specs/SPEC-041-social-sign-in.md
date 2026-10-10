---
id: SPEC-041
title: Sign in with Google — one validation core, the first of two acquisitions
status: draft           # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0008, ADR-0031, ADR-0045, ADR-0026]
standards: [authentication-authorization, social-oauth, application-security, testing]
approved_by:            # name, or "agent" for a clean Tier A gate
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

Three yes answers, so Tier B, and gate 1 is the one that blocks: **the owner picks A or B before the Files
table can be written.** Everything below assumes B and is marked where A would differ.

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

## Open questions for the owner

1. **A or B**, which decides the request shape and whether `GOOGLE_CLIENT_SECRET` is used at all.
2. **Does a social sign-in create an account?** Subject V.1 asks for registration with a social account, so the
   answer is presumably yes — but then an account exists with no password, and `POST /me/password` requires a
   current one. The spec proposes: a social-only account has no password, `POST /me/password` answers `422`
   with a code saying so, and setting a first password is the reset flow, which already proves the address.
3. **`nonce` needs somewhere to live** for the seconds between the authorization request and the callback.
   The spec proposes the same table as the social link, single-use, with a short expiry — a second Redis-shaped
   dependency for a sixty-second value would be the wrong trade at this size.

## Done when

- [ ] the owner has answered A or B, and questions 2 and 3
- [ ] the Files table is written against that answer
- [ ] status returns to `approved` and implementation begins
