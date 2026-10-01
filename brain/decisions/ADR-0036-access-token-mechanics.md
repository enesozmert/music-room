---
kind: adr
id: ADR-0036
status: Accepted
date: 2026-09-28
---

# ADR-0036 — Access token mechanics: JwtBearer, a `sid` claim, and 30 seconds of skew

## Context

[[ADR-0021-backend-dependency-set]] fixed the dependency set and named no JWT library, and
[[sdd]] §13.1 fixed the session model at "a 15-minute JWT access token" carrying
"`sub`, `iat`, `exp`, `jti` and nothing else". Three questions were left for the slice that built it,
and all three were answered as owner decisions inside specs — which are disposable
([[specs]] "Retention"), so they are recorded here instead.

1. **Which library issues the token.** SPEC-005 put three options to the owner: a Microsoft
   issuance-only package, the bearer package that validation would later need anyway, or hand-written
   HS256.
2. **What the token carries.** SPEC-010 found that `DELETE /auth/sessions/current` cannot work as
   specified: the contract says the request has no body, and a token carrying only `sub` does not say
   *which* refresh-token family the caller is using. A session list you cannot act on and a "current"
   you cannot identify are the same missing claim seen twice.
3. **How much clock drift validation tolerates.** `AddJwtBearer` defaults
   `TokenValidationParameters.ClockSkew` to five minutes, which turns a 15-minute token into a
   20-minute one and would silently falsify T-28's stated window.

## Decision

Owner decisions of 2026-09-27 and 2026-09-28.

### `Microsoft.AspNetCore.Authentication.JwtBearer` issues and validates

One package for both halves, so the dependency set is amended once rather than twice. It is pinned in
`MusicRoom/Directory.Packages.props` and referenced only by `MusicRoom.Infrastructure`;
`MusicRoom.Application` declares `IAccessTokenIssuer` and never learns which library signs
([[ADR-0028-domain-owns-its-base-types]]).

This **amends the dependency set in [[ADR-0021-backend-dependency-set]]**, which that ADR anticipated
but did not name.

### The token carries `sid`, naming the refresh-token family

Claims are `sub`, `sid`, `iat`, `exp` and `jti`. `sid` is the family id, stable across a rotation
because a rotation continues the same session. It **amends [[sdd]] §13.1's "nothing else"**, and §13.1
now states the claim list including it.

Logout revokes the family named by `sid`, so signing out of one device leaves the others signed in.
The alternative — revoking every family — was rejected because the contract says "current".

### Validation allows 30 seconds of clock skew

A token is therefore accepted for at most 15 minutes and 30 seconds. `JwtBearerConfiguration` sets
issuer, audience, signature, lifetime and `RequireExpirationTime` explicitly rather than relying on
defaults, because an authentication configuration that relies on defaults is one upgrade away from
being wrong in silence.

## Consequences

- **T-28 is stated precisely**: neither a password reset nor a logout revokes an access token already
  issued, and the exposure cannot exceed 15 minutes 30 seconds. That row names this ADR's number for
  the skew.
- `sid` is one more identifier in a signed, client-readable token. It is opaque and grants nothing on
  its own; the family it names is useless without a refresh token that belongs to it.
- Zero skew was rejected as brittle for a distributed future; five minutes as dishonest about the
  window. Thirty seconds is a number this project can defend in one sentence.
- Validation lands where SPEC-005 said it would — with the first authenticated endpoint — so no idle
  authentication handler was ever registered.
- A hand-written HS256 encoder is no longer the fallback it was in SPEC-005's escalation. If this
  package is ever dropped, `IAccessTokenIssuer` is the only seam that changes.

## Alternatives rejected

- **`Microsoft.IdentityModel.JsonWebTokens` for issuance only.** Smaller surface at the time, and it
  would have meant amending the dependency set twice — once for issuance, once for validation.
- **Hand-written HS256.** Zero dependency, and the precedent existed in ADR-0021's hand-written
  dispatcher fallback. Rejected: "why did you write your own JWT encoder" is a worse defence
  conversation than "why this package".
- **Revoke every family on logout.** Correct with the data that existed before `sid`, and surprising:
  a user logging out of a phone would lose their tablet.
- **A body on `DELETE /auth/sessions/current` carrying the refresh token.** Contradicts the contract,
  and it would put a live credential in a request that does not need one.
