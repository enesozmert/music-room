---
id: SPEC-050
title: The access token is RS256 — closing O32
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0031, ADR-0028]
standards: [authentication-authorization, application-security, testing]
approved_by: enesozmert # approved 2026-10-11, before taking the backend live
escalation: |
  **Gate 5 is a yes, and this one changes the signature of the credential every endpoint accepts.** Gates 1,
  2, 3, 4, 6 and 7 are no — ADR-0031 already pinned RS256 for what this API signs, no package, no schema, no
  contract row (the shape of the token is not published; its claims are), nothing lowered, nothing copied.

  **What is being corrected is a half-finished decision.** ADR-0031 says *"Signing is RS256 with an
  asymmetric key. The HMAC option in `authentication-authorization` goes away."* SPEC-043 built the key pair
  and the JWKS; SPEC-044 signed the **ID token** with it. The **access token** stayed HS256, deliberately and
  for one unit — *"changing the signature of a token `AddJwtBearer` already validates, in the unit that
  introduces a new flow, is two migrations in one diff."* SDD §13.1 has said "15-minute **RS256** JWT" the
  whole time, which has been aspirational rather than true. This makes it true, and O32 closes.

  **The one decision worth stating: no HMAC overlap.** The honest options were to keep accepting HS256 for a
  window, or to stop accepting it at once.

  | | **keep accepting HS256 for a window** | **stop at once (taken)** |
  |---|---|---|
  | Access tokens in flight at deploy | survive | are refused, once |
  | What a client does | nothing | gets one `401`, rotates its refresh token, carries on |
  | What the system keeps | a shared secret that still mints valid tokens, plus a knob to remember to turn off | nothing |
  | Risk | the knob is never turned off, which is how an overlap becomes a permanent second key | a few `401`s inside one 15-minute window |

  **Refresh tokens are unaffected**, which is what makes the second column cheap: they are opaque, stored as
  digests, and validated against the database rather than by a signature. The blast radius of this deploy is
  one refused request per active client, and the client already handles a `401` by rotating — that path is
  tested by `SessionRotationEndpointTests`.

  **`JWT_SIGNING_KEY` stops existing.** Nothing signs with it and nothing validates against it, so keeping it
  as a start-up requirement would be a secret the project asks for and never uses — the kind of configuration
  nobody can audit. It is removed from `.env.example`, from the Makefile's document build and from the test
  host.
---

# The access token is RS256

## Goal

Every token this API issues is RS256, signed with the key pair SPEC-043 published and verifiable against
`/.well-known/jwks.json` by anything that has never spoken to this API. After this, ADR-0031's *"signing is
RS256"* is a fact rather than a plan, and the shared HMAC secret is gone from the project.

## Out of scope

- **Key rotation mechanics.** SPEC-043 built the two slots and the overlap; this unit only starts using them
  for a second kind of token.
- **The ID token.** Already RS256, SPEC-044.
- **The refresh token.** Opaque by design, and not a signed thing at all — ADR-0032.
- **Taking the backend live.** Next, and the reason this goes first: a deployment is a bad moment to discover
  that the access token's signature was still the one the design said it would not be.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it completes ADR-0031 |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no — two HMAC-specific tests are replaced by their RS256 equivalents |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **`JwtAccessTokenIssuer` signs with `ISigningKeyProvider.Current`, RS256.** | ADR-0031. The `kid` header comes with it, because SPEC-044 put the thumbprint on the key itself |
| 2 | **Validation accepts every key in `ISigningKeyProvider.Verification`.** | The current key and, during a rotation overlap, the outgoing one — which is what SPEC-043 built the second slot for. One list, so a rotation needs no change here |
| 3 | **No HMAC is accepted, not even for a window.** | An overlap would keep a shared secret that still mints valid tokens, plus a knob to remember to turn off. The cost is one `401` per active client inside one 15-minute window, and refresh tokens are unaffected |
| 4 | **`JWT_SIGNING_KEY` is removed everywhere.** | Nothing signs with it or validates against it. A secret a project asks for and never uses is configuration nobody can audit |
| 5 | **`JwtOptions` keeps issuer, audience and lifetime**, and loses the key and its floor. | Those three are still configuration. The key is not: it comes from the provider, which has its own floor (2048 bits) and its own refusal |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtOptions.cs` | change — three settings, no key |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtAccessTokenIssuer.cs` | change — RS256 from the key provider |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtBearerConfiguration.cs` | change — the published keys, not a shared secret |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — the guard stops reading `JWT_SIGNING_KEY`; the bearer handler is given the key provider |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/JwtAccessTokenIssuerTests.cs` | change — RS256, the `kid`, and a token from another key refused |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/BearerValidationTests.cs` | change — forges with the host's own RSA key |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — the test host stops setting a key nothing reads |
| `.env.example` | change — `JWT_SIGNING_KEY` removed, with a line saying where the signing key lives now |
| `Makefile` | change — the document build drops the placeholder |
| `docs/SDD.md` | change — §13.1 and §16 stop hedging, O32 closes |
| `docs/owasp/ASVS-CONFORMANCE.md` | change — V9.1.2 and V9.1.3's evidence is the RS256 key and the JWKS |
| `docs/technical-guide/40-security/secrets-management/STANDARD.md` | change — the key table names the RSA pair |
| the four Infrastructure test suites that build an `InfrastructureOptions` | change — one fewer argument |
| `docs/specs/SPEC-050-rs256-access-token.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| an issued access token | `alg` is `RS256` and `kid` is one the JWKS publishes |
| validation of that token | accepted by the running host |
| a token signed by another RSA key | refused, so "signed by something" is not enough |
| a token signed with the old HMAC secret | refused — the overlap that is deliberately absent |
| a rotation overlap | a token signed by the previous key still validates, because `Verification` carries both |
| `grep -rn JWT_SIGNING_KEY` | no code, workflow, Makefile or example asks for it |
| `make e2e` | sign in, call an authenticated route, rotate, revoke — against the composed stack, with the new signature |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes — 684 tests, 0 failed, on 2026-10-11
- [x] `make e2e` passes — 47 checks against the composed stack, with the new signature
- [x] O32 is in the closed table
- [x] `status: done` in this file
