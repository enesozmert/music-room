---
kind: adr
id: ADR-0037
status: Accepted
date: 2026-09-28
---

# ADR-0037 — Credential rate limiting: per-IP middleware, per-account counter, refusal as a value

## Context

Threat **T-25** — anonymous credential endpoints with no rate limit — was deferred three times across
SPEC-002, SPEC-003 and SPEC-004, because the control is shared between endpoints that did not all
exist yet. Subject V.6 grades it. [[ADR-0021-backend-dependency-set]] chose the built-in
`Microsoft.AspNetCore.RateLimiting` middleware and stated that "state is held **in process**", without
saying how progressive backoff — which `authentication-authorization` makes mandatory — would work,
since no built-in limiter primitive expresses it.

Two further problems surfaced while building it:

1. The middleware runs **before model binding**, so a per-account partition cannot see the address in
   the request body without parsing untrusted JSON in the component whose job is to bound work.
2. Once SPEC-008 made the audit record commit inside the command's transaction, the throttle's
   `CredentialAttemptThrottledException` **rolled its own audit row back** — the loudest event in the
   system left no trace.

## Decision

Owner decisions of 2026-09-27 and 2026-09-28.

### Two halves, in two places

- **Per IP: the built-in middleware**, one named policy per credential endpoint, so a registration
  loop cannot spend the sign-in allowance. `429` with `Retry-After` and an **empty body**: a message
  naming the partition that tripped tells a caller which key to vary.
- **Per account: `ICredentialAttemptLimiter`**, enforced inside the handler, because the address a
  request names is known only after binding. Login charges a *failed* attempt and clears the counter on
  success; registration and verification resend charge **every** attempt, because the cost is in the
  attempt rather than in its outcome.

### Progressive backoff in process, without `IMemoryCache`

A `ConcurrentDictionary` pruned as it is used: a fixed number of chargeable attempts per window, then a
penalty of one window doubling per further consecutive violation to a ceiling of eight.
`Microsoft.Extensions.Caching.Memory` is not in `MusicRoom.Infrastructure`'s dependency graph and
adding it would have been a new dependency for a dictionary. This **confirms** ADR-0021's "state is
held in process" and names the mechanism.

### The approved numbers live in [[sdd]] §13.3

Because a spec is disposable and the numbers are what a defence panel asks about.

### A refusal is a returned value, never an exception

`CredentialAttemptThrottledException` was deleted. Handlers return the refusal and endpoints map it, so
the audit record for a throttled attempt commits on the pipeline's ordinary path. This **amends**
[[ADR-0033-audit-write-posture]], which is where fail-closed auditing is recorded.

### The per-IP trip stays in the application log

A record for it would have nothing true to put in `platform`, `device` or `app_version`: the trip
happens before the client-context middleware reads them, and the limiter must stay first so a flood is
refused before the server verifies anything.

## Consequences

- **T-25 is closed**, with tests as its evidence, and T-01 gained the same evidence.
- Limiter state is per process, so several instances would each hold their own, and a restart forgives
  every caller. Recorded on T-25 as a residual; it shares the fate of the SignalR backplane (issue O8).
- A refused attempt writes one audit row per attempt, bounded by these same limits — 5 per account per
  5 minutes on the credential counter. Recorded on T-29.
- Middleware order is load-bearing and now documented in `Program.cs`: rate limiter, authentication,
  client context.
- Every credential endpoint that arrives later — password reset did, social sign-in will — attaches an
  existing policy instead of inventing one.

## Alternatives rejected

- **Parse the body in the partition function.** One mechanism instead of two, at the cost of parsing
  untrusted JSON in the limiter and needing a hard size cap before the read.
- **Per-IP only.** Smallest change, and `authentication-authorization` refuses it in as many words:
  either key alone is trivially bypassed.
- **A `login_attempt_counters` table.** Survives a restart and is visible to an operator. Rejected: one
  more write on the failure path of an anonymous endpoint is write amplification for the very loop this
  control exists to stop.
- **Catching the throttle in the audit behaviour and committing there.** Keeps the exception, and gives
  the behaviour two commit sites and the fail-closed story an exception.
