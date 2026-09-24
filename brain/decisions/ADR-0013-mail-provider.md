---
kind: adr
id: ADR-0013
status: Accepted
date: 2026-09-24
---

# ADR-0013 — Transactional mail through a hosted provider

## Context

Subject V.1 requires email verification on signup and a forgot-password flow. Neither ships
without a way to send mail. Issue O6.

## Decision

Send transactional mail through a **hosted provider on its free tier — Resend or Brevo**, behind
an `IMailService` abstraction declared in `Application` and implemented in `Infrastructure`.

Configuration lives in `.env`: `MAIL_PROVIDER`, `MAIL_API_KEY`, `MAIL_FROM`.

## Consequences

- Real delivery, so the verification and reset flows can be demonstrated at defence rather than
  described.
- Free-tier daily and monthly send limits are far above what a school project needs, but they are
  **limits**: a test loop that registers accounts in a tight cycle can exhaust a daily quota. Rate
  limiting on registration and on verification resend already exists for security reasons
  ([[authentication-authorization]]) and doubles as quota protection.
- The API key is server-side only, in `.env`, never in a client bundle
  ([[secrets-management]]).
- A provider requires a verified sender domain or a sandbox sender address. Using the provider's
  sandbox sender is acceptable for the defence; note it rather than pretending it is production.
- The abstraction means the provider is one class, so switching costs little. `Core.Mailing` from
  the reference repository is **not** reproduced ([[ADR-0011-solution-layout]]).
- Bounce and delivery-failure handling is out of scope. A failed send must not roll back
  registration; it is retried or surfaced as "resend verification".
- Mail content carries a single-use, hashed, short-TTL token and no other personal data.

## Alternatives rejected

- **Gmail SMTP with an app password** — quick, but Google throttles and blocks aggressively, the
  password is tied to a personal account, and it is fragile in front of evaluators.
- **Mailpit/MailHog only** — excellent for development, but nothing leaves the machine, so the
  requirement cannot be demonstrated end to end. It stays available as the local development
  option; the provider is used where real delivery matters.

Closes issue O6.
