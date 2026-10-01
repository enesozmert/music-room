---
kind: adr
id: ADR-0032
status: Accepted
date: 2026-09-28
---

# ADR-0032 — Refresh-token families live 30 days, absolute

## Context

`authentication-authorization` fixes the access token at 15 minutes, the email verification token at 24
hours and the reset token at 1 hour. For refresh tokens it says only that they rotate, are stored
hashed and are revocable — **no lifetime**. [[ADR-0024-expiry-and-cleanup]] lists refresh tokens among
the things that expire and gives no number either. [[sdd]] §13.1 described the session model without
one.

So SPEC-005 could not issue a token without inventing a number, and a number invented by an agent is
the thing [[AGENTS]] §4 rule 5 exists to prevent.

## Decision

Owner decision of 2026-09-27: **30 days, absolute.**

Absolute, not sliding: every rotation issues a replacement whose expiry is **inherited from the
family's first token**. A family therefore dies 30 days after the sign-in that created it, however many
times it has been rotated in between.

The number is now stated in [[sdd]] §13.1 as well, because a spec is disposable and this is a fact the
defence will be asked about.

## Consequences

- A stolen refresh-token family cannot be kept alive indefinitely by rotating it, which is the property
  that made absolute worth choosing over sliding.
- A user who signs in and uses the app daily is signed out after 30 days and signs in again. That is the
  cost, and it is the ordinary behaviour of most applications.
- Expiry is enforced **at the point of use** — the rotation predicate includes `expires_at_utc > @now` —
  so a stopped cleanup service cannot turn a stale family into a live one
  ([[ADR-0024-expiry-and-cleanup]] mechanism 1).
- The `BackgroundService` sweep of expired rows is still hygiene and still unbuilt; it now has three
  token tables to sweep.
- `SessionIssuer.RefreshTokenLifetimeDays` is the one place the number appears in code.

## Alternatives rejected

- **Sliding expiry.** A session in daily use would never end, which is convenient and means a leaked
  family that the thief keeps rotating never ends either.
- **Seven days absolute.** Tighter, and it makes a user re-enter a password roughly four times a month
  on a phone — friction that pushes people towards weaker passwords.
- **No expiry, revocation only.** Then the only thing standing between a leaked token and permanent
  access is a user noticing. The theft detector in [[sdd]] §13.1 catches reuse, not quiet possession.
