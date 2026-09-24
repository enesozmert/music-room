---
kind: adr
id: ADR-0007
status: Accepted
date: 2026-09-23
---

# ADR-0007 — BCrypt for password hashing

## Context

Subject V.1 requires email+password registration and V.6 requires protecting users against
malicious behaviour. Passwords must be stored with a slow, salted, adaptive hash. The earlier
draft of [[auth-and-identity]] left the algorithm as "Argon2id or the ASP.NET Core Identity
hasher"; the team has now chosen.

## Decision

Hash passwords with **BCrypt**, via the `BCrypt.Net-Next` NuGet package.

Parameters and rules:

| Item | Value |
|---|---|
| Work factor (cost) | **12 minimum**, stored inside the hash |
| Salt | generated per password by the library — never reused, never hand-rolled |
| Accepted password length | 12 to 64 characters |
| Hard input limit | 72 **bytes** UTF-8 — longer input is rejected at validation, not silently truncated |
| Rehash | on successful login, if the stored hash's work factor is below the current setting, rehash and store |
| Storage | the full BCrypt string (algorithm, cost, salt, digest) in one column |

## Consequences

- **BCrypt silently truncates input at 72 bytes.** Two different long passwords can therefore
  produce the same hash. This is the one real footgun of the algorithm, so the limit is enforced
  in validation: 64 characters maximum, and a UTF-8 byte-length check rejects anything over 72
  bytes. NIST SP 800-63B asks verifiers to accept at least 64 characters, so this limit satisfies
  the requirement and avoids the truncation entirely.
- The work factor is a tuning parameter, not a constant of nature. It is configuration, measured
  on the target host: high enough to cost real time, low enough not to become a
  denial-of-service vector on the login endpoint. Rate limiting on login remains mandatory.
- BCrypt is **memory-cheap** compared with Argon2id, so it resists GPU cracking less well. Accepted
  for this project: it is a mature, widely reviewed, correctly implemented default, and the
  threat model here is a school project, not a credential warehouse. This trade-off is stated
  rather than hidden, because an evaluator may ask.
- Upgrading later is possible without a mass reset: the stored hash is self-describing, so a
  future algorithm change can rehash on next successful login.
- Never log, return, or include a password or hash in any response, trace or error.

## Alternatives rejected

- **Argon2id** — stronger against GPU attacks and the current OWASP first choice, but the team has
  no operational experience tuning its three parameters, and a badly tuned Argon2id is worse than
  a correctly configured BCrypt.
- **ASP.NET Core Identity's `PasswordHasher`** (PBKDF2) — would pull in the whole Identity stack,
  which conflicts with the hand-rolled CQRS identity design in [[auth-and-identity]].
- **PBKDF2 by hand** — weakest of the three and easiest to misconfigure.

## Affected

Standards: `authentication-authorization` (v1.2.0).
Design: [[sdd]] §13.1, [[auth-and-identity]].
