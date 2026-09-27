---
kind: adr
id: ADR-0031
status: Accepted
date: 2026-09-27
---

# ADR-0031 — The MusicRoom API becomes an OpenID Provider at S8

## Context

[[ADR-0008-oidc-social-login]] made the API an OpenID Connect **relying party**: Google and
Facebook authenticate the user, the API validates an ID token, and then the API issues its own
credentials. What it issues was never OIDC. `authentication-authorization` specifies a 15-minute
JWT carrying `sub`, `iat`, `exp` and `jti`, signed with "a strong asymmetric or HMAC key from
`.env`", plus an opaque refresh token rotated on every use. That is a sound bearer-token design
and it is not OpenID Connect: there is no discovery document, no JWKS endpoint, no authorization
endpoint, no ID token, no UserInfo endpoint, and the signing algorithm is not pinned.

Owner requirement, 2026-09-27: **the project is to be OpenID Connect conformant end to end, not
only on the relying-party side.** The two halves of authentication should speak one protocol.

S1 has already shipped the current surface. SPEC-005 landed sign-in and refresh rotation,
SPEC-006 the credential rate limits, SPEC-007 password reset. All three are `done`. Any change
here is rework of finished slices, which is what makes *when* part of this decision rather than a
detail of it.

## Decision

### The API becomes an OpenID Provider

The API implements the provider role of OpenID Connect Core 1.0:

| Surface | What |
|---|---|
| Discovery | `/.well-known/openid-configuration`, describing everything below |
| JWKS | a public key set, so a token can be verified without asking the API |
| Authorization endpoint | authorization code flow, **PKCE `S256` required** |
| Token endpoint | code exchange, and refresh |
| ID token | RS256, carrying `iss`, `sub`, `aud`, `exp`, `iat`, `auth_time`, `nonce` |
| UserInfo endpoint | the claims the granted scopes allow, and nothing else |

- **Signing is RS256 with an asymmetric key.** The HMAC option in
  `authentication-authorization` goes away: a shared secret cannot be published in a JWKS, and a
  JWKS is what makes the provider verifiable by anything other than itself.
- **Access token and ID token are different things and stay different.** The ID token says who
  authenticated; the access token authorises an API call. Neither is accepted in the other's
  place.
- **Refresh tokens keep the behaviour S1 already built.** Opaque, stored as a SHA-256 digest,
  rotated on every use, reuse revokes the family. OIDC does not ask for less, and RFC 9700 asks
  for exactly this. Rotation is not discarded to satisfy a protocol; it is the protocol's own
  recommendation.
- **The mobile client is a public client.** It authenticates through the authorization endpoint
  with PKCE and holds no client secret. A direct username-and-password grant is not offered:
  the resource owner password credentials grant is removed by OAuth 2.1 and deprecated by RFC
  9700.

### It lands at S8, not now

- S1's current sign-in surface stays as it is until S8.
- S8 already owns social login, so both halves of OIDC land in one slice, reviewed together.
- The migration is part of S8's spec: the endpoints S1 shipped are replaced, not kept alongside.

## Consequences

- **S8 grows from "add two providers" to "become a provider".** It is the largest single slice in
  the plan after the concurrency work, and it now sits after S7 with no slack behind it. That cost
  is the price of the requirement and is recorded here rather than discovered in S8.
- **Key management becomes a real concern.** An RS256 key pair has to be generated, stored, served
  through the JWKS, and rotated with an overlap window so tokens signed by the outgoing key keep
  verifying. `EnvSecretProvider` holds it until [[ADR-0029-containerised-dev-and-secret-vault]]
  lands the vault at S1b, after which the private key belongs there. New open issue.
- **Three finished specs get rewritten.** SPEC-005, SPEC-006 and SPEC-007 built endpoints that the
  authorization and token endpoints replace. Their tests, their threat-register rows and their
  section 16 claims move with them. Nothing is deleted before its replacement passes.
- **The rate limits survive the move.** SPEC-006's per-IP policies and per-account counters attach
  to the token and authorization endpoints, which is where credential guessing arrives once the
  old endpoints are gone.
- **"Conformant" is a claim with a test suite behind it.** The OpenID Foundation publishes a
  certification suite. Implementing the surface above is not the same as passing it, and the
  project does not claim certification. Running the basic OP profile against a local instance is
  the honest evidence, and it belongs in S9 next to the other conformance evidence.
- The audit behaviour, the client-context headers and the ownership gates are unaffected: they sit
  on the resource side, and the resource side keeps taking a bearer access token.

## Alternatives rejected

- **Keep the plain JWT and call the project "OIDC" because social login uses it.** The relying
  party half would be conformant and the provider half would not exist. The owner asked for both.
- **Do it now, before S2.** It would rewrite three `done` specs while the graded concurrency
  slices S5 to S7 are still unwritten, and those are what subject V.2 measures. Rejected on
  sequencing, not on merit.
- **Align the token shape only** — RS256, OIDC claim names, a published JWKS — without an
  authorization endpoint or UserInfo. Much cheaper, and not a provider: nothing could discover
  it, and no standard client could drive it.
- **Adopt an off-the-shelf identity server.** It would deliver conformance fastest and it would
  answer the subject's design question for us; the project's own rule is that the team decides and
  defends its design, and [[ADR-0007-password-hashing-bcrypt]] already rejected the Identity stack
  on the same grounds.

## Affected

Standards: `authentication-authorization`, `social-oauth`. Design: [[sdd]] §9.1, §10.2, §13.1.
Plan: [[delivery-plan]] S8. Contract: [[endpoint-inventory]].
Related: [[ADR-0008-oidc-social-login]], [[ADR-0029-containerised-dev-and-secret-vault]].
