---
kind: adr
id: ADR-0008
status: Accepted
date: 2026-09-23
---

# ADR-0008 — OpenID Connect for Google and Facebook sign-in

> **Amended 2026-09-27.** Owner requirement: **one relying-party implementation, driven by
> configuration, not one integration per provider.** A provider is a row of configuration — its
> discovery URL, client id, and how its ID token arrives — and the validation core is written
> once. The branch this ADR already predicted is now bounded: it lives in **acquisition only**,
> never in validation. See "One core, two acquisitions" below, where the two providers' published
> discovery documents are recorded as read on 2026-09-27.
>
> What the API issues after validation is no longer a plain JWT:
> [[ADR-0031-musicroom-as-openid-provider]] makes the API an OpenID Provider at S8.

## Context

Subject V.1 requires registering with a social account and linking one to an existing account.
Subject V.5 requires social authentication in the mobile app. The question is not *whether* to
support Google and Facebook, but *which protocol shape* to integrate with: plain OAuth 2.0 plus a
provider-specific profile endpoint, a provider SDK, or OpenID Connect.

OAuth 2.0 is an **authorisation** protocol. Using an OAuth access token as proof of *identity* is
the classic mistake, because an access token says what the bearer may do, not who they are, and
it may have been minted for a different application.

## Decision

Use **OpenID Connect** — authentication on top of OAuth 2.0 — with the **authorization code flow
plus PKCE**. Identity is established from a validated **ID token**, never from an access token and
never from a profile object sent by the client.

Server-side validation of every ID token, in this order:

1. Signature verified against the provider's JWKS, fetched from its discovery document and cached
   with respect to key rotation.
2. `iss` matches the expected issuer exactly.
3. `aud` matches our client id. An ID token minted for another application is rejected — this is
   the check that stops token-substitution.
4. `exp` and `iat` within tolerance.
5. `nonce` matches the one the server generated for this authentication.
6. `email_verified` is `true` before an email is used to match an existing account.

`state` and `nonce` are generated server-side, single-use, and verified. Redirect URIs are
registered exactly and allow-listed.

## One core, two acquisitions

Both providers publish an OpenID Connect discovery document. They are not equally capable, and the
difference is in the record because it decides the shape of the code. Read 2026-09-27:

| | Google | Facebook |
|---|---|---|
| Discovery | `https://accounts.google.com/.well-known/openid-configuration` | `https://www.facebook.com/.well-known/openid-configuration` |
| `issuer` | `https://accounts.google.com` | `https://www.facebook.com` |
| `jwks_uri` | `https://www.googleapis.com/oauth2/v3/certs` | `https://www.facebook.com/.well-known/oauth/openid/jwks/` |
| `token_endpoint` | `https://oauth2.googleapis.com/token` | **absent** |
| `response_types_supported` | includes `code` | `id_token`, `token id_token` only |
| `code_challenge_methods_supported` | `plain`, `S256` | **absent** |
| `userinfo_endpoint` | present | **absent** |
| `id_token_signing_alg_values_supported` | `RS256` | `RS256` |

So the authorization code flow with PKCE, which this ADR chose, is available at Google and **not
available at Facebook**: Facebook publishes no token endpoint and no `code` response type. Its
Limited Login hands the ID token to the app directly instead.

The decision that follows:

- **Validation is one implementation, shared, and driven by the discovery document.** Fetch the
  document, cache the JWKS with respect to key rotation, then run the six checks above. Nothing in
  that path names a provider. Adding a third provider is a configuration row.
- **Acquisition branches, and only there.** Google: authorization code plus PKCE `S256` through
  the token endpoint. Facebook: Limited Login, which yields an ID token without a code exchange.
  Both hand the same thing — a signed ID token — to the same validator.
- **`aud` and `nonce` carry the weight on the Facebook path**, because there is no code exchange
  binding the response to this client. `nonce` is generated server-side, single-use, and verified,
  exactly as on the Google path.
- Facebook's classic OAuth 2.0 plus Graph API path is **not** used. It is the one route that would
  put a non-OIDC integration in the codebase.
- Subject V.1 asks for "Facebook **or** Google", so shipping only Google was available and was not
  chosen. Both ship, and the non-uniformity above is the price.

## Consequences

- **Google is a full OpenID Connect provider.** Discovery document at
  `https://accounts.google.com/.well-known/openid-configuration`; ID token validation is the
  documented path.
- **Facebook is not uniformly OIDC.** Classic Facebook Login is OAuth 2.0 plus the Graph API, while
  Facebook's *Limited Login* issues an OIDC ID token. The integration must therefore branch:
  OIDC ID token validation where Facebook issues one, and server-side token inspection against
  Facebook's own endpoint where it does not. Pretending the two providers are identical would
  produce a broken or insecure implementation, so the difference is recorded here rather than
  discovered during integration.
- The client secret stays on the server. A mobile app is a **public client** and cannot keep one,
  which is why PKCE is mandatory rather than optional.
- PKCE prevents authorization-code interception on the device. RFC 9700 deprecates the implicit
  flow, so it is not used.
- Provider identity is stored as `(provider, providerUserId)` and maps to at most one local user.
  Email is used for matching **only** when the provider asserts it verified; unverified-email
  matching is an account-takeover path.
- We issue our **own** tokens after validation. The provider authenticates; our API authorises.
  A provider token is never accepted as an API credential.
- Scopes are minimal: `openid`, `profile`, `email`. No contacts, no publishing, nothing "for later".
- A user must always keep at least one usable sign-in method; unlinking the last one is refused
  unless a password is set.

## Alternatives rejected

- **Plain OAuth 2.0 + provider profile endpoint** — works, but invites the access-token-as-identity
  mistake and needs a bespoke validation path per provider.
- **Provider mobile SDKs as the source of truth** — the client would assert its own identity.
  Trusting a client-supplied profile is the vulnerability, not the shortcut.
- **ASP.NET Core Identity external login providers** — brings the Identity stack we already
  rejected in [[ADR-0007-password-hashing-bcrypt]].

## Affected

Standards: `social-oauth` (v1.1.0), `authentication-authorization` (v1.2.0).
Design: [[sdd]] §10.2, §13.1.
Open issue: none. Provider-specific client registration is configuration, not a design decision.
