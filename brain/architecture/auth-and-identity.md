---
kind: architecture
updated: 2026-09-23
---

# Auth, Identity & Audit

## Authentication

| Flow | Design |
|---|---|
| Email + password | **BCrypt**, cost >= 12, input 12-64 chars and <= 72 bytes ([[ADR-0007-password-hashing-bcrypt]]); email verification required before service access |
| Forgot password | single-use, short-TTL, hashed reset token; invalidate all sessions on reset |
| Social login | **OpenID Connect**, authorization code + PKCE ([[ADR-0008-oidc-social-login]]); server validates the **ID token** against JWKS, never an access token, never the client's word |
| Account linking | an existing authenticated user attaches a provider identity; one provider identity maps to at most one user |

Session model: short-lived JWT access token + rotating refresh token, refresh tokens revocable
server-side. Rotation is what makes session theft detectable.

## Authorisation

Two independent gates, both required:

1. **Ownership** — an authenticated user reads and writes only their own data (subject V.6).
2. **Visibility + licence** — event/playlist access resolved from `Visibility` and `LicenceMode`,
   evaluated server-side on every request and every hub join.

Profile reads are filtered per field by `ProfileFieldVisibility` relative to the requester's
relationship (self / friend / stranger). Never return a full profile row and filter on the client.

## Conformance

ASVS 5.0.0 Level 2 applies to V6 Authentication, V7 Session Management, V8 Authorization,
V9 Self-contained Tokens and V10 OAuth and OIDC. Scope and rationale: [[asvs-conformance]].
Residual risks: [[threat-register]].

## Abuse resistance (subject V.6)

- Rate limit login, registration, password reset, and verification resend — per IP **and** per account.
- Progressive lockout / backoff on repeated login failure.
- Generic error text on login and password reset (no account enumeration).
- Refresh-token rotation with reuse detection.
- Remaining accepted risks get written down in [[threat-register]], not hidden — that is part of
  the grade.

## Mandatory audit log

Every client action produces a backend log entry containing **platform, device, app version**.

Design: clients send `X-Client-Platform`, `X-Client-Device`, `X-Client-App-Version` headers;
middleware validates and attaches them to the request scope; every handled command writes an
audit record. Missing headers are a 400 for mutating endpoints, not a silent default.

SignalR hub joins and leaves are audited by a hub filter writing the same record; client context
is captured at the handshake ([[ADR-0019-hub-action-auditing]]).

Never log credentials, tokens, full request bodies, or voter coordinates. See standard
`application-security`.

## Related

[[system-overview]] · [[subject-requirements]] · [[standards-router]] · [[owasp-index]] ·
[[ADR-0007-password-hashing-bcrypt]] · [[ADR-0008-oidc-social-login]]
