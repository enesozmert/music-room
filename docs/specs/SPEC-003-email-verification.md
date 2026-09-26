---
id: SPEC-003
title: Verify an email address — IMailService, a Development-only logging sender, and POST /auth/email-verifications
status: approved       # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6, V.8]
decisions: [ADR-0013, ADR-0024, ADR-0026, ADR-0028, ADR-0011, ADR-0012, ADR-0021]
standards: [authentication-authorization, application-security, clean-architecture-cqrs, dotnet-csharp, ef-core-persistence, testing, openapi-documentation, logging-observability, secrets-management]
approved_by: enesozmert   # Tier B, owner approval 2026-09-26
escalation: |
  Tier B. Gate 5 answers "yes", so docs/prompt-library/PROMPTS.md §2.7 stops the run here. The
  unattended cycle wrote this spec and stopped: no branch, no code edit, no commit.

  Gate 5 — authentication and tokens. This spec carries the verification token end to end. It
  makes the token leave the process for the first time (SPEC-002 generated one, stored its hash
  and deliberately let the plaintext die in the method), it adds the endpoint that spends it, and
  it decides how a spent, unknown or expired token is answered. Three judgements need a human:

  1. **The token is written to the application log, on purpose.** The Development-only
     `LoggingMailService` is the whole point of the owner's 2026-09-24 decision on O17 — the
     developer reads the verification link out of `docker compose logs`. It is still a credential
     in a log, which docs/owasp/TOP10-CHECKLIST.md A09 and the logging standard both name. The
     mitigation proposed here is mechanical, not a promise: `AddInfrastructureServices` registers
     the logging sender only when the host environment is Development, and **throws at start-up**
     in any other environment rather than silently falling back. A THREAT-REGISTER row (T-26,
     drafted in "Security" below) records the residual risk. Needed from the owner: acceptance of
     that row and of the start-up failure as the guard.
  2. **Status codes for a bad token.** docs/api/ENDPOINTS.md §1 lists `204`, `400`, `410 expired`.
     This spec maps unknown, malformed and already-consumed all to `400`, and only a genuinely
     expired token to `410`. A distinct answer for "already consumed" would tell an anonymous
     caller that a token it does not hold once existed. The contract is implemented, not changed
     — but the collapse of three cases into one status is a security judgement, not a typo.
  3. **No rate limit on the new endpoint.** `/auth/email-verifications` is a guess-a-token
     surface. The registered "fixed" policy is still attached to nothing (SPEC-001) and rate
     limiting is deferred as T-25. This spec does not lift that deferral; it widens the surface
     T-25 already covers. The owner should confirm T-25 is updated to name this endpoint rather
     than a new limiter being built here.

  Gates 1, 2, 3, 4, 6 and 7 are all "no", and the reasoning for each is in the Gate table below.
  In particular no package is added: `ILogger<T>` comes from the `net10.0` shared framework, the
  same way `IServiceCollection` already does in `MusicRoom.Infrastructure`, which today declares
  no package reference for it.

  ---

  OWNER DECISIONS, 2026-09-26 — all three answered. This spec is approved.

  1. Verification token in the Development log: ACCEPTED, with the mechanical guard as proposed.
     `AddInfrastructureServices` registers `LoggingMailService` only when the host environment is
     Development and **throws at start-up** in any other environment. A silent fallback would be
     the dangerous version of this; a start-up failure is loud and cannot be missed. Record the
     residual risk as T-26. The whole point of the O17 split is that a developer can read the
     verification link out of `docker compose logs` and exercise the flow end to end before a mail
     provider exists — that value is real, and so is the risk, so both are written down.

  2. Status codes: `400` for unknown, malformed and already-consumed; `410` only for a token that
     genuinely expired. A distinct answer for "already consumed" would tell an anonymous caller
     that a token it does not hold once existed, which is the same family of leak as V.6's "a user
     reaches their own data and no one else's". The contract in ENDPOINTS.md is implemented as
     written, not changed; collapsing the three cases is the security reading of it.

  3. Rate limiting: do NOT build a limiter here. Widen T-25 to name
     `POST /api/v1/auth/email-verifications` alongside registration. The control is shared with
     login, reset and resend, and building it per endpoint means building it four times. T-25
     stays unmitigated and must close before the defence — V.6 grades it, and this spec makes the
     surface it covers larger, which the row now says.

---

# Verify an email address

## Goal

After this spec, a registered account can prove it owns its address. Registration delivers a
verification link through `IMailService`; in Development that sender writes the link to the
structured log, which is what the owner's O17 decision asks for until the hosted provider exists.
`POST /api/v1/auth/email-verifications` spends the token exactly once, sets `is_email_verified`,
and answers `204`, `400` or `410`. SDD §10.1 steps 4, 6 and 7 stop being design-only.

## Out of scope

- `POST /auth/verification-requests` — resending a verification mail. Its own spec; it needs a
  throttle of its own and it must not become an enumeration oracle.
- The hosted provider adapter (Resend or Brevo). That is O17 / slice S1a, and it needs a key that
  does not exist yet.
- Login, JWT issuance, refresh rotation, password reset. Later specs in S1.
- Rate limiting. Deferred as T-25; see escalation note 3.
- Enforcing "an unverified account cannot use the services". There is nothing to gate yet — no
  authenticated endpoint exists. The check lands with login.
- Any background sweep of expired tokens. ADR-0024: expiry is checked at the point of use, which
  is what this spec does. A sweep is hygiene and belongs with the cleanup spec.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0013 already puts mail behind `IMailService` in `Application`, implemented in `Infrastructure`; the Development-only logging implementation is the owner's recorded O17 decision of 2026-09-24, and ADR-0024 already says expiry is checked at the point of use |
| 2 | new or upgraded dependency? | no — `Directory.Packages.props` is untouched. `ILogger<T>` and `IHostEnvironment` resolve from the `net10.0` shared framework; `MusicRoom.Infrastructure` already uses `IServiceCollection` with no package reference for it |
| 3 | schema change beyond this spec, or destructive migration? | no — `users.is_email_verified`, `email_verification_tokens.consumed_at_utc` and `expires_at_utc` all exist in `20260924195820_InitialIdentity`. No migration is created |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — `POST /auth/email-verifications` is already in the contract with `204`, `400`, `410`. This implements the row; the file is not edited |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — see `escalation` |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Files

Exact paths, created or changed. An unattended run must not touch anything outside this list.

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Messaging/IMailService.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | change |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailOutcome.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/LoggingMailService.cs` | create |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/VerifyEmailCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/LoggingMailServiceTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EmailVerificationEndpointTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RecordingMailService.cs` | create |
| `docs/api/openapi.json` | change — regenerated by `make openapi`, never hand-edited |
| `docs/owasp/THREAT-REGISTER.md` | change — add T-26, amend T-25 to name the new endpoint |
| `docs/SDD.md` | change — §16 only, the two rows named under "Close out" |
| `docs/specs/SPEC-003-email-verification.md` | change — status and the "Done when" boxes |

### The Files table, walked (PROMPTS.md §2.3, lesson L-029)

**1. Assembly and reference direction.** Nothing in this table needs a new `ProjectReference`, and
nothing points outward in the SDD §7.1 graph.

| New type | Assembly | Who consumes it | Reference already exists? |
|---|---|---|---|
| `IMailService` | `MusicRoom.Application` | `RegisterCommandHandler` (same assembly); `LoggingMailService` in `MusicRoom.Infrastructure` | yes — `Infrastructure -> Application` |
| `VerifyEmailCommand`, `VerifyEmailOutcome` | `MusicRoom.Application` | `AuthEndpoints` in `MusicRoom.WebAPI` | yes — `WebAPI -> Application` |
| `VerifyEmailCommandHandler`, `VerifyEmailCommandValidator` | `MusicRoom.Application` | MediatR and FluentValidation, by assembly scan | n/a |
| `LoggingMailService` | `MusicRoom.Infrastructure` | `InfrastructureServiceRegistration`, same assembly | n/a |
| `RecordingMailService` | `MusicRoom.WebAPI.Tests` | `EmailVerificationEndpointTests`, same assembly; implements the `Application` interface, reached transitively through `WebAPI` | yes |

`IUserRepository` keeps its place: the interface in `Application`, the EF Core implementation in
`Persistence`. `Application` still never learns about EF Core.

**2. Types that cross an assembly boundary.** `IMailService`, `VerifyEmailCommand` and
`VerifyEmailOutcome` cross one, so all three are `public` and each gets its own file.
`VerifyEmailOutcome` is a separate file rather than a nested enum because `AuthEndpoints` switches
on it. `VerifyEmailCommandHandler` and `LoggingMailService` stay `internal sealed` — both are
activated by the container, so both need a **public constructor** even while the type is internal
(lesson L-035).

**3. Does anything invoke it?**

- `IMailService` — called by `RegisterCommandHandler` after the insert wins, and by nothing else.
  It is the only reason this spec touches the register handler at all.
- `VerifyEmailCommandHandler` — reached by `ISender.Send` from the new endpoint; registered by the
  existing `RegisterServicesFromAssembly` scan in `ApplicationServiceRegistration`.
- `VerifyEmailCommandValidator` — registered by the existing `AddValidatorsFromAssembly`, and run
  by the already-wired `ValidationBehaviour<,>`. A validator with no behaviour is L-029's example
  of a file that compiles and does nothing; the behaviour exists, so this one runs.
- `LoggingMailService` — registered in `AddInfrastructureServices`, resolved as `IMailService`.
- `TryConsumeEmailVerificationTokenAsync` — the new repository method has exactly one caller, the
  verify handler.
- `RecordingMailService` — replaces `IMailService` in the test host, and is the only way the
  integration test can learn the plaintext token, because nothing else ever returns it.

**4. CLI, tool manifest, or an unpinned package?** None. No `dotnet ef` run, because there is no
migration. No new tool.

**5. Do the four `corePackages` contain what this assumes?** They do not, and this spec assumes
nothing from them. `Core.Security` and `Core.CrossCuttingConcerns` are empty; `Core.Persistence` is
empty; `Core.Application` holds `ValidationBehaviour` only. A mail abstraction naming a
verification link is product vocabulary and belongs in `MusicRoom.Application`, not in a
cross-cutting package — ADR-0011, and the same reasoning that kept `Core.Mailing` out of the
solution (ADR-0013).

## Steps

1. **`IMailService`.** One method:
   `Task SendEmailVerificationAsync(string emailAddress, string token, CancellationToken cancellationToken)`.
   It takes the **plaintext** token, because only the sender may ever see it. The interface does
   not name a provider, a template id or a transport — ADR-0013 keeps the provider to one class.

2. **`RegisterCommandHandler`.** Split token issuance so the plaintext survives the call:
   `IssueVerificationToken` returns the entity **and** the plaintext (a private record or a tuple;
   whichever keeps `Handle` under 40 lines). Then:
   - store the hash exactly as today — SHA-256, hex, 24-hour window, `ConsumedAtUtc` null;
   - send the mail **only when `TryRegisterAsync` returned true**. A duplicate address must send
     nothing: a mail to an existing account saying "you just registered" is an enumeration oracle
     with a delivery receipt;
   - send **after** the transaction commits, never inside it;
   - catch a send failure, log it at warning, and still answer `202`. ADR-0013: a failed send must
     not roll back registration. There is a second reason here — letting the exception escape would
     turn a fresh address into a `500` while a duplicate stayed `202`, which is the enumeration
     oracle the whole flow is built to avoid. The `catch` gets that sentence as its comment, because
     a bare swallow is a review finding.

3. **`IUserRepository`.** Add
   `Task<VerifyEmailOutcome> TryConsumeEmailVerificationTokenAsync(string tokenHash, DateTime nowUtc, CancellationToken cancellationToken)`.
   The outcome type lives in the feature folder; the interface refers to it. Document on the
   interface that the hash is supplied by the caller and the plaintext never reaches this layer.

4. **`UserRepository`.** Implement it as a **conditional update**, inside one transaction:
   - `UPDATE email_verification_tokens SET consumed_at_utc = @now WHERE token_hash = @hash AND consumed_at_utc IS NULL AND expires_at_utc > @now`
     expressed with `ExecuteUpdateAsync`. One row affected means this caller won the token.
   - Zero rows affected: read the row by hash to tell the three losing cases apart — absent or
     already consumed gives `Invalid`, present and expired gives `Expired`. The read happens only
     on the failure path, so the happy path stays a single statement.
   - On a win, set `IsEmailVerified` on the owning user and save. Already verified is not an error:
     the update is idempotent on that column.
   - Commit. The token spend and the flag must not be separable — a crash between them would leave
     a spent token and an unverified account, which no later request can repair.

   This is a concurrency hotspot, so the definition of done requires a **parallel** test, listed
   under Verification. The guarantee is enforced by the `consumed_at_utc IS NULL` predicate in the
   database, not by a read-then-write check in C# — the same reasoning as `ux_users_email`.

5. **`VerifyEmailCommand` / `Outcome` / `Handler` / `Validator`.**
   - `VerifyEmailCommand(string Token) : IRequest<VerifyEmailOutcome>`.
   - `VerifyEmailOutcome` — `Verified`, `Invalid`, `Expired`.
   - The handler hashes the submitted token with SHA-256, hex-encodes it, and calls the repository.
     It never logs the token, and it never logs the hash either: a hash of a live credential is
     still a lookup key.
   - The validator: token not empty, and bounded in length. The bound is what the generator
     produces — 32 bytes, base64url-encoded — so a caller cannot force a large allocation before
     any hashing happens.

6. **`LoggingMailService`.** `internal sealed`, public constructor, takes `ILogger<LoggingMailService>`.
   Logs one structured event at information with the address and the verification link, and a
   comment stating in one line that this exists because O17 has no provider yet, that it is
   registered in Development only, and that T-26 records the residual risk. No template engine; the
   link is built from a configured base URL passed in by the composition root, so nothing in this
   class hard-codes `localhost`.

7. **`InfrastructureServiceRegistration`.** Take the extra arguments the composition root already
   holds — `bool isDevelopment` and the verification link base URL — and:
   - register `LoggingMailService` as `IMailService` when `isDevelopment`;
   - otherwise **throw** `InvalidOperationException` naming O17 and slice S1a. A non-Development
     host must fail at start-up, loudly, rather than boot with a sender that writes credentials to
     a production log or with no sender at all. The thrown message names the decision, not the fix,
     so nobody is tempted to re-register the logging sender to make it start.

8. **`AuthEndpoints`.** Map `POST /api/v1/auth/email-verifications`, `AllowAnonymous`:
   - `204` on `Verified`; `410` on `Expired`; `400` on `Invalid`;
   - a `ValidationException` maps to `400` here, not `422`. The contract row lists `400` for this
     endpoint and `422` is not among its failures; a malformed token is malformed input, and the
     registration endpoint's `422` covers a different, richer payload;
   - `.Produces(...)` for each of `204`, `400`, `410` so the OpenAPI document states them;
   - the summary says what the endpoint does, never what a failure distinguishes.
   - Keep `MapAuthEndpoints` under the 40-line limit: the new registration goes in its own private
     method if the group builder grows past it.

9. **`Program.cs`.** Pass the environment flag and the link base URL into
   `AddInfrastructureServices`. The base URL is read the way `DATABASE_URL` is — from
   configuration, with no silent default that points at a machine nobody intended.

## Verification

```bash
make agent-check
```

That target is the gate: `dotnet restore`, `dotnet format --verify-no-changes`,
`dotnet build -warnaserror`, `dotnet test`, `dotnet list package --vulnerable --include-transitive`,
`make openapi`, and `git diff --exit-code -- docs/api/openapi.json`. Docker must be running: three
of the tests below start a real PostgreSQL container, and ADR-0012 rejects the in-memory provider
precisely because it would not enforce the predicate under test.

| Check | Proves |
|---|---|
| `Post_WithTheIssuedToken_Returns204AndVerifiesTheAccountAsync` | V.1 — the happy path end to end: register, capture the token from the mail sender, spend it, `is_email_verified` is true |
| `Post_ReplayingASpentToken_Returns400AndLeavesTheAccountVerifiedAsync` | single use — the replay is refused and nothing regresses |
| `Post_WithAnUnknownToken_Returns400Async` | an unknown token is answered exactly like a spent one |
| `Post_WithAnExpiredToken_Returns410Async` | ADR-0024 — expiry is checked at the point of use, with no sweeper running |
| `Post_TwentyConcurrentVerificationsOfOneToken_VerifyExactlyOnceAsync` | the concurrency case AGENTS.md §5 requires: parallel requests, assertion on final database state, exactly one `204` and one `consumed_at_utc` |
| `Post_ADuplicateRegistration_SendsNoSecondMailAsync` | SDD §10.1 step 5 — the mailbox is not an enumeration oracle either |
| `SendEmailVerificationAsync_WritesTheLinkOnce` (Infrastructure) | the Development sender behaves; one event, containing the link |
| `Validate_AnEmptyToken_Fails` and the length bound (Application) | input is rejected before anything is hashed or queried |
| `dotnet build -warnaserror` | the analyzer limits hold: no method past 40 lines, complexity 10, or 15 methods per type |
| `git diff --exit-code -- docs/api/openapi.json` | V.4 — the committed contract document matches the code in the same change |

## Security

Walked before implementation; findings and their disposition:

| Item | Disposition |
|---|---|
| A09 / logging standard — a credential in a log | **accepted, recorded.** New row **T-26**: *Development-only `LoggingMailService` writes the email verification link, token included, to the application log. Mitigation: registered only when the host environment is Development; any other environment fails at start-up rather than falling back. Residual risk: anyone with access to a developer's container logs can verify an account created on that machine. Removed when O17 lands the hosted provider (slice S1a). Accepted by: <owner>* |
| API4 / unrestricted resource consumption — no rate limit on a token-guessing endpoint | **deferred, already tracked.** T-25 is amended to name `/auth/email-verifications` alongside the registration endpoint. 256 bits of entropy is what makes the guess impractical in the meantime; that is a property of the token, not a substitute for a limiter |
| A01 / API1 — object-level authorisation | not applicable. The endpoint is anonymous by contract, and the token *is* the authorisation. There is no identifier in the request to confuse for one |
| A07 — account enumeration | covered on both paths: no mail on a duplicate registration, and unknown, malformed and spent tokens share one status code |
| ASVS V6 — single use and expiry | enforced by the `consumed_at_utc IS NULL AND expires_at_utc > @now` predicate, in the database, under concurrency, with a parallel test as the evidence |
| Secrets | none read, none added. No `.env` key is introduced; the link base URL is configuration, not a credential |

## Close out

- `docs/SDD.md` §16, row *V.1 — email verification + forgot password*: from `designed` to
  **partly implemented**, naming what exists — verification implemented, forgot-password still
  designed. The V.8 per-layer-tests row gets its count updated.
- `docs/owasp/THREAT-REGISTER.md`: T-26 added, T-25 amended.
- `brain/migrations/migrations-index.md`: **no row.** No migration is created by this spec.
- `brain/lessons/lessons-index.md`: only if something durable is learned. Nothing is expected.
- One commit on `feat/spec-003-email-verification`, then a draft pull request. Not before
  `RESULT: pass`.

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] every command under Verification passes
- [ ] OWASP checklists walked for the surface touched; unfixed findings in the threat register
- [ ] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [ ] `status: done` in this file
