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
  **Withdrawn by amendment 2, 2026-10-11** — once the authorization endpoint required a bearer token,
  those endpoints became its precondition rather than its predecessor.

## Amendment, 2026-10-11 — this is a first-party provider

SPEC-044 asked a question this decision had not answered: `GET /oauth2/authorize` is the endpoint a
**person** interacts with, and this API has no login page, no cookie session and no HTML. A standard
provider answers an unauthenticated authorization request with a sign-in page; this one has nowhere
to send anybody.

**Owner's answer, 2026-10-11: option A — the authorization endpoint requires a bearer access
token.** The client signs in through the credential endpoints and then drives the code flow with the
token it already holds. The contract's auth column for that row changes from `anon` to `auth`.

What that buys: this stays a JSON API, and it grows no second authentication path — the thing most
likely to drift from the first and quietly weaken it.

What it costs, stated here rather than discovered later:

- **No third party can federate with this provider.** Somebody who is not already a MusicRoom user
  cannot complete an authorization request, because there is no interface in which they could
  become one. The surface is conformant; the deployment is first-party.
- **The token endpoint requires the mandatory client headers**, like every other mutating request
  (SDD §10.7). A standards client that sends only RFC 6749's form fields is refused with `400`. That
  is the same trade: our client sends them already, a stranger's does not.
- **Making this a general-purpose provider is a user interface, not an endpoint.** A consent screen
  and an interactive sign-in page are what would be missing, and neither is in the subject.

Two smaller decisions SPEC-044 took under the same answer, recorded so they are not re-litigated:
**clients are configuration rather than a table** — nobody can self-register, so a registry is a
row read the way the social provider rows are — and **the access token stays HMAC in that unit**,
because changing the signature of a token `AddJwtBearer` already validates, in the unit that
introduces a new flow, is two migrations in one diff. The RS256 move goes with the removal of the
old endpoints.

## Amendment 2, 2026-10-11 — the credential endpoints are not removed

This decision said, under *"It lands at S8, not now"*:

> - The migration is part of S8's spec: the endpoints S1 shipped are **replaced, not kept alongside**.

**That sentence is withdrawn.** It was written on 2026-09-27, before amendment 1 gave the authorization
endpoint its auth column. Once `GET /oauth2/authorize` requires a bearer access token, the credential
endpoints are its **precondition**: something has to authenticate a person before a code can be issued for
them, and in a first-party provider that something is `POST /auth/sessions` or `POST /auth/sessions/social`.
Removing them would leave a flow whose entry condition cannot be met.

**Owner's answer, 2026-10-11: both surfaces stay, permanently**, and what each is for is published in
`docs/api/ENDPOINTS.md` rather than left to a reader to infer. They are two front doors onto one session —
the same refresh-token family, the same rotation, the same family revocation (ADR-0032), the same rate limits
and the same audit records.

What this costs, stated rather than discovered: **the project does not get to claim it migrated.** The
honest sentence at defence is the longer one — *the API implements the OpenID provider role, our client
authenticates through the credential endpoints, and the two coexist because a first-party provider has no
sign-in surface of its own.*

Two consequences follow:

- **SPEC-041's migration plan loses its phases 3 and 4.** They are marked not applicable in that spec, with
  this reasoning, rather than deleted — a plan that stopped being correct is a finding worth keeping.
- **The access token's move to RS256 is decoupled.** It was scheduled with phase 4 only because phase 4 was
  going to rewrite the same code; nothing about a signature depends on removing an endpoint. It is issue O32.

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
