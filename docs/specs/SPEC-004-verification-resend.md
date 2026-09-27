---
id: SPEC-004
title: Resend an email verification link — POST /auth/verification-requests
status: approved       # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6, V.8]
decisions: [ADR-0013, ADR-0024, ADR-0028, ADR-0011, ADR-0012, ADR-0021]
standards: [authentication-authorization, application-security, clean-architecture-cqrs, dotnet-csharp, ef-core-persistence, testing, openapi-documentation, logging-observability]
approved_by: enesozmert   # Tier B, owner approval 2026-09-27
escalation: |
  Tier B. Gate 5 answers "yes", so docs/prompt-library/PROMPTS.md §2.7 stops the run here. The
  unattended cycle wrote this spec and stopped: no branch, no code edit, no commit.

  Gate 5 — authentication and tokens. This spec mints a verification token outside registration
  for the first time. SPEC-002 issued exactly one token per account, at the only moment an account
  can be created; this endpoint lets an anonymous caller ask for another one, repeatedly, for an
  address it names. Three judgements need a human, and none of them is settled by an existing ADR.

  1. **Does a resend supersede the outstanding token?** ADR-0024 decided expiry (checked lazily at
     the point of use, swept later by one background service) but says nothing about a second live
     token for the same account. Two readings are defensible.
     *Supersede*: the handler consumes or deletes any unspent token for that user before inserting
     the new one, so exactly one link is live at a time and the guess surface never grows. Cost: a
     user who clicks the link from the first mail after asking for a second one gets `400`, which
     reads as "the system broke" from the inbox.
     *Keep both*: every issued link works until its own 24h window closes. Cost: N resends means N
     live 256-bit tokens, and a caller who can drive the endpoint can hold the table open.
     This spec assumes **supersede**, and the Steps below are written that way. The choice changes
     what the endpoint means, not how it is coded, so it belongs to the owner.
  2. **Rate limiting, for the third time.** T-25 is already accepted twice — for
     `/auth/registrations` (SPEC-002) and `/auth/email-verifications` (SPEC-003). This endpoint is
     materially worse than either: it is the only unauthenticated surface whose success path is *a
     mail send per request*. While `LoggingMailService` is the only sender the cost is a log line,
     so the risk is bounded to a Development host today — but the moment O17 lands the hosted
     provider in slice S1a, an unauthenticated loop against this path burns the free-tier quota
     directly. That is T-19, which currently names "rate limits on registration and verification
     resend" as its mitigation and has no evidence column. The agent will not decide whether T-25
     stretches to cover this or whether a limiter must land first. Needed from the owner: either
     widen T-25 to name this endpoint and accept it for S1, or say that the limiter is a
     precondition and this spec waits behind it.
  3. **Silence for an unknown or already-verified address.** The contract row answers `202` with no
     failure code but `429`, which forces four different states — unknown address, unverified
     account, already-verified account, mail send failed — to be indistinguishable to the caller.
     This spec sends a mail only for an existing unverified account and returns `202` in every
     case. The consequence the owner should accept explicitly: a user who has already verified and
     asks again gets a `202` and no mail, with no way to tell that from success. That is the same
     trade ENDPOINTS.md §1 already made for registration, applied to a path a *legitimate* user
     reaches by mistake far more often.

  Gates 1, 2, 3, 4, 6 and 7 are all "no"; the reasoning for each is in the Gate table below. In
  particular no package is added and no migration is written: the token generator, the
  `email_verification_tokens` table, `IMailService` and the validation behaviour all exist and are
  reused as they are.

  ---

  OWNER DECISIONS, 2026-09-27 — all three answered. This spec is approved.

  1. SUPERSEDE. A resend consumes any unspent token for that account before inserting the new one,
     so exactly one link is live at a time and the guess surface never grows with the number of
     resends. The cost is real and accepted: a user who asks for a second mail and then clicks the
     link in the first one gets `400`, which reads as breakage from an inbox. It is the smaller
     harm — the alternative lets an anonymous caller hold N live 256-bit tokens open, and with no
     limiter in place N is unbounded.

  2. WIDEN T-25 **and** make the limiter a precondition of S1a. This spec ships now: the only
     sender is `LoggingMailService`, so the cost of a loop today is log lines on a Development
     host, and the start-up guard from SPEC-003 makes that the only place it can run. But the
     moment O17 lands the hosted provider, the same loop burns the free-tier quota — that is T-19,
     and it stops being theoretical. So S1a does not start until the rate limiter exists. The risk
     closes before it becomes real, and S1 is not blocked waiting for a control that needs login
     and reset to exist before it can be built once.

     This is the third deferral of T-25 and the last one. Deferring again would make the deferral
     a habit rather than a decision.

  3. ACCEPT the silence. Unknown address, unverified account, already-verified account and a
     failed send all answer `202`. It is the same trade `ENDPOINTS.md` §1 already made for
     registration, and the only way the endpoint is not an enumeration oracle. The difference is
     acknowledged rather than glossed: a legitimate user reaches this path by mistake far more
     often than they mistype a registration, so more real people will see a `202` and no mail. A
     distinct answer for "already verified" would tell an anonymous caller that an address is
     registered and confirmed, which is exactly the leak the registration endpoint refuses.

---

# Resend an email verification link — POST /auth/verification-requests

## Goal

An account that registered but never verified can ask for a second verification link without
registering again. After this spec, `POST /api/v1/auth/verification-requests` accepts an address,
issues a fresh single-use 24-hour token for that account when — and only when — the account exists
and is unverified, hands the plaintext to `IMailService`, and answers `202` in every case. The last
`UNKNOWN` in the verification flow closes: today a registration whose mail send failed is a dead
account, because `RegisterCommandHandler` swallows the send failure and points at an endpoint that
does not exist yet.

## Out of scope

- Rate limiting. It is T-25's subject and is shared with endpoints that do not exist yet; see
  escalation point 2.
- The hosted mail provider. That is O17 and slice S1a. This endpoint sends through whatever
  `IMailService` is registered, which in Development is `LoggingMailService`.
- Password reset (`/auth/password-reset-requests`, `/auth/password-resets`). Same token shape, a
  different credential and a different blast radius; its own spec.
- Login, JWT and refresh rotation. They need a signing key and a new package, and are the next
  unit after this one.
- Any change to the background cleanup service of ADR-0024. It does not exist yet and this spec
  does not need it: superseded tokens are consumed in place, not left for a sweeper.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0013 (mail behind `IMailService`), ADR-0024 (expiry checked at the point of use) and ADR-0028 (Application owns its abstractions) all hold as written. The supersede question in escalation point 1 is a spec-level judgement inside ADR-0024, not a change to it |
| 2 | new or upgraded dependency? | no — `Base64Url`, `RandomNumberGenerator` and `SHA256` come from the `net10.0` shared framework and are already used by `RegisterCommandHandler` |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration at all. `email_verification_tokens` already has every column this needs, including `consumed_at_utc` for the supersede path |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — the row `POST /auth/verification-requests → 202, 429, anon` is already in §1. This implements it |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it mints a verification token on anonymous demand. See `escalation` |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Files

Walked against SDD §7.1: every path below lives in an assembly that already references what it
needs, so **no `.csproj` changes**. `MusicRoom.WebAPI` references `MusicRoom.Application`;
`MusicRoom.Persistence` references `MusicRoom.Application` and `MusicRoom.Domain`;
`MusicRoom.Application` references `MusicRoom.Domain` and `Core.Application`. Nothing here points
outward.

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Features/Auth/VerificationTokenIssuer.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestEmailVerification/RequestEmailVerificationCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestEmailVerification/RequestEmailVerificationCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestEmailVerification/RequestEmailVerificationCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | change — call the shared issuer instead of its private copy |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — add `TryIssueEmailVerificationTokenAsync` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — implement it |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — map the route, add the request record |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/RequestEmailVerificationCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/VerificationRequestEndpointTests.cs` | create |
| `docs/api/openapi.json` | change — regenerated by the build |
| `docs/SDD.md` | change — §16, the V.1 verification row only |
| `docs/owasp/THREAT-REGISTER.md` | change — T-25 and T-19, per the owner's answer to escalation point 2 |
| `docs/specs/SPEC-004-verification-resend.md` | change — status and the "Done when" checklist |

### The walk, question by question

1. **Assemblies and references.** Four assemblies are touched — Application, Persistence, WebAPI
   and two test projects — and every reference that makes them compile already exists, because
   SPEC-002 and SPEC-003 created exactly this path for registration and verification. No
   `Directory.Packages.props` entry, no `PackageReference`, no `ProjectReference`.
2. **Types crossing a boundary.** `IUserRepository` is public and gains one method, which
   `UserRepository` in Persistence implements — the consumer sees the abstraction, never EF Core.
   `RequestEmailVerificationCommand` is public because MediatR resolves it from WebAPI; its handler
   and validator follow the internal/public shape SPEC-003 established. `VerificationTokenIssuer`
   is `internal static` inside `MusicRoom.Application` and crosses feature folders, not assemblies
   — the same shape as `VerificationTokenHash`, which `Register` and `VerifyEmail` already share.
3. **Does anything invoke it?** `AuthEndpoints.MapVerificationRequests` → `ISender.Send` →
   `RequestEmailVerificationCommandHandler` → `IUserRepository.TryIssueEmailVerificationTokenAsync`
   → `IMailService.SendEmailVerificationAsync`. The validator runs through the existing
   `ValidationBehaviour<,>` open behaviour registered in `ApplicationServiceRegistration`; no new
   registration is needed, because `AddValidatorsFromAssembly` and
   `RegisterServicesFromAssembly` already scan this assembly. Nothing in this table compiles
   without a caller.
4. **CLI, tool manifest, unpinned package?** None. No `dotnet ef` invocation either — there is no
   migration.
5. **corePackages.** `Core.Application` holds `ValidationBehaviour` and is used. `Core.Security`,
   `Core.Persistence` and `Core.CrossCuttingConcerns` are empty today and this spec adds nothing to
   them: a token issuer that only the identity feature uses does not belong in a shared package
   before a second consumer exists.

## Steps

1. Extract the token generator out of `RegisterCommandHandler` into
   `VerificationTokenIssuer.Issue(Guid userId, DateTime nowUtc)`, returning the
   `EmailVerificationToken` row and the plaintext together. Keep the constants and their comments:
   32 random bytes, 24-hour window, base64url encoding, digest via `VerificationTokenHash.Of`.
   Behaviour must not change — this is a move, and `RegistrationEndpointTests` proves it.
2. Point `RegisterCommandHandler` at the shared issuer and delete its private copy.
3. Add `RequestEmailVerificationCommand(string Email)` and its validator: `NotEmpty`,
   `EmailAddress`, and the same maximum length `RegisterCommandValidator` applies to an address. No
   rule message interpolates the submitted value.
4. Add `Task TryIssueEmailVerificationTokenAsync(string email, EmailVerificationToken token,
   DateTime nowUtc, CancellationToken)` to `IUserRepository`, returning whether the token was
   written. Document that the caller passes a lower-cased address and that a `false` return means
   "no unverified account" without saying which of the two reasons applied.
5. Implement it in `UserRepository`, in one transaction:
   a. read the user id for `email` where `is_email_verified` is false; return false when there is
      none;
   b. **supersede** — `ExecuteUpdateAsync` stamps `consumed_at_utc` and `updated_at_utc` on every
      row for that user where `consumed_at_utc IS NULL`, so an earlier link stops working;
   c. insert the new token row;
   d. commit, return true.
   The insert can still lose a race against a concurrent verification that has just flipped
   `is_email_verified`; that is acceptable and produces a live token for a verified account, which
   verifies nothing and expires. Do not add a second read to close it.
6. Handle the command: lower-case and trim the address exactly as registration does, build the
   token through the issuer, call the repository, and send the mail **only** when it returned true.
   Wrap the send in the same `try`/`catch` `RegisterCommandHandler` uses and for the same reason —
   a send failure must not turn one address into a `500` and another into a `202`.
7. Map `POST /api/v1/auth/verification-requests` in `AuthEndpoints`: `AllowAnonymous`,
   `Produces(202)`, `ProducesValidationProblem(422)` for a malformed address, and a description
   that states the `202` is unconditional without explaining what makes a mail arrive.
8. Tests, per step 9 below.
9. Regenerate `docs/api/openapi.json` through the build, update `docs/SDD.md` §16, and apply the
   owner's answer to escalation point 2 in `docs/owasp/THREAT-REGISTER.md`.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `dotnet format --verify-no-changes` | the style gate of ADR-0009 |
| `dotnet build -warnaserror` | no analyzer violation; the extracted issuer keeps every method inside the 40-line and complexity-10 limits |
| `RequestEmailVerificationCommandValidatorTests` | a malformed or oversized address is refused before a database read — application-security |
| `VerificationRequestEndpointTests.UnverifiedAccount_ReceivesASecondLink` | V.1: the resend path works end to end and the new token verifies |
| `VerificationRequestEndpointTests.Resend_InvalidatesThePreviousLink` | escalation point 1 as implemented: the first link answers `410`/`400` after a resend, and only the newest verifies |
| `VerificationRequestEndpointTests.UnknownAddress_Answers202AndSendsNothing` | V.6: the endpoint is not an account-enumeration oracle |
| `VerificationRequestEndpointTests.VerifiedAccount_Answers202AndSendsNothing` | a verified account cannot be mailed on an anonymous caller's demand |
| `RegistrationEndpointTests` and `EmailVerificationEndpointTests`, unchanged and green | step 1 moved the generator without changing its behaviour |
| OpenAPI drift check in `backend-dev.yml` | V.4: the committed contract documents the new endpoint |

`VerificationRequestEndpointTests` follows `EmailVerificationEndpointTests`: a real PostgreSQL
through Testcontainers (ADR-0012), `RecordingMailService` substituted for `IMailService` so the
assertion is "how many messages, carrying which token", not a log scrape.

No parallel test is required. This endpoint has no contended write: the supersede update and the
insert are one transaction, and two concurrent resends for one address produce two serialised
transactions whose loser leaves exactly one live token — the same end state as one request. The
concurrency hotspot in this flow is `TryConsumeEmailVerificationTokenAsync`, which SPEC-003 already
covers with a 20-way parallel test and which this spec does not touch.

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] every command under Verification passes
- [ ] OWASP checklists walked for the surface touched; unfixed findings in the threat register
- [ ] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [ ] `status: done` in this file
