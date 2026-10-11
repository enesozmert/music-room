---
id: SPEC-044
title: The authorization and token endpoints — code plus PKCE, and an ID token
status: draft           # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0031, ADR-0032, ADR-0043, ADR-0045]
standards: [authentication-authorization, application-security, rest-api-design, testing]
approved_by:            # name, or "agent" for a clean Tier A gate
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

## Done when

- [ ] the owner has answered A, B or C, and the two questions in the escalation
- [ ] the Files table is written against that answer
- [ ] status returns to `approved` and implementation begins
