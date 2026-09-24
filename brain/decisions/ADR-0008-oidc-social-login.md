---
kind: adr
id: ADR-0008
status: Accepted
date: 2026-09-23
---

# ADR-0008 — OpenID Connect for Google and Facebook sign-in

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
