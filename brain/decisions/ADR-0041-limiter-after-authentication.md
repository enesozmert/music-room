---
kind: adr
id: ADR-0041
status: Accepted
date: 2026-10-03
---

# ADR-0041 — The rate limiter runs after authentication

## Context

SPEC-006 put `app.UseRateLimiter()` first in the pipeline, ahead of `UseAuthentication()`, and wrote the
reason in a comment: a flood should be refused before the server verifies a signature. Every policy that
specification registered is partitioned by the remote address, so nothing it needed was missing at that
point in the pipeline.

SPEC-018 then added two policies that are partitioned by the account —
`EventRateLimiterPolicies.Votes` and `.Codes` — and keyed them on the validated `sub`, with the remote
address as a fallback for a request that carries no identity. Its own remarks say why:
*"a room full of people voting over one venue's network must not share a bucket."*

That is exactly what happened. The limiter middleware runs before `UseAuthentication()`, so
`HttpContext.User` holds no claims when the partition key is computed, every authenticated vote took the
fallback, and the per-account allowance became one bucket per address.

Nothing caught it. The functional suites raise the credential limits and never cast sixty votes, and a
single-account test cannot tell a per-account bucket from a per-address one, because for one account on
one address the two are the same bucket. SPEC-023's load measurement is what surfaced it: two hundred
accounts voting from one address, and 98% of the votes refused at every step, including the step with ten
users.

## Decision

The pipeline order becomes: `UseAuthentication()`, then `UseRateLimiter()`, then `UseAuthorization()`,
then `ClientContextMiddleware`.

- The limiter runs **after** authentication, so a policy may partition on the validated `sub`.
- It still runs **before** authorisation, any handler and any database work, so a flood is refused before
  it costs anything that matters.
- It still runs **before** `ClientContextMiddleware`, so a request the limiter refuses is answered `429`
  and not `400` for a missing client header — the SPEC-008 reason, unchanged.

A regression test is part of the decision, because the defect was invisible to every test that existed:
`EventVoteRateLimitingTests` votes past the allowance with one account and then votes once with a second
account from the same address. The second vote must succeed.

## Consequences

- A flood now pays bearer validation before the limiter sees it. That cost is one HMAC verification
  against an in-memory key, and only for a request that actually presents a token: the credential
  endpoints are anonymous, so the flood SPEC-006 was written against still reaches the limiter having
  spent nothing. The exchange is a signature verification for a limiter that partitions on the identity
  it was specified to partition on.
- The per-IP credential policies are unaffected: their key is the connection's address, which is
  available at either position.
- `CredentialRateLimitingTests` still passes unchanged. Its test-only client-address filter is installed
  as an `IStartupFilter`, which wraps the whole application pipeline, so it stays ahead of the limiter
  wherever the limiter sits.
- The comment in `Program.cs` that said the limiter must be first is replaced rather than deleted: the
  order is load-bearing in both directions, and the next reader needs to know which constraint holds it.
- Opens no issue in [[sdd]] §17. O8 — limiter state is per process — is untouched and still open.

## Alternatives rejected

- **Read the `sub` inside the partition key.** The limiter would parse and validate the token itself,
  duplicating what the bearer handler does and creating a second place where a token is trusted. The
  handler's own remark is that it trusts nothing beyond what authentication already verified; this would
  make that false.
- **Two limiter stages, one before authentication for the per-IP policies and one after for the
  per-account ones.** `UseRateLimiter` applies the endpoint's own policy, so a second call means every
  policy is consulted twice and every permit spent twice. Two stages would need two sets of policies to
  stay in step, which is the drift [[ADR-0030-hub-rate-limits-and-origin]] rejected a second allowlist to
  avoid.
- **Partition the vote policies by address and accept it.** It is the behaviour that shipped, and it is
  wrong on the exact case the feature exists for: an event is a room full of people on one network.
- **Leave the order and raise the vote allowance.** It makes the symptom rarer and keeps the defect: the
  venue still shares one bucket, and now a single abusive account has a larger one.

## Affected

Standards: `application-security`, `authentication-authorization`. Design: [[sdd]] §13.3.
Specs: SPEC-006, SPEC-018, SPEC-023. Related: [[ADR-0037-credential-rate-limiting]],
[[ADR-0030-hub-rate-limits-and-origin]].
