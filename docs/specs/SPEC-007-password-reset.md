---
id: SPEC-007
title: Reset a forgotten password — hashed single-use 1h token, and every session revoked on success
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0007, ADR-0011, ADR-0012, ADR-0013, ADR-0024, ADR-0028]
standards: [authentication-authorization, application-security, clean-architecture-cqrs, ef-core-persistence, dotnet-csharp, testing]
approved_by: enesozmert # repository owner, 2026-09-27, with the four decisions below
escalation: |
  Gates 1 and 5 answer "yes". Four decisions belong to the repository owner before this spec may be
  implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) how one transaction spans two aggregates, since a successful reset must consume the
  token, replace the password hash and revoke every refresh-token family together — the mechanism
  chosen here is inherited by every later multi-aggregate write, which makes it ADR-shaped;
  (2) the rate-limit numbers for the two new endpoints, which the SPEC-006 table does not cover;
  (3) whether a reset token may pass through the Development-only logging mail sender, which widens
  T-26 from "verify an account" to "take an account over"; (4) whether an access token issued before
  the reset stays valid for its remaining minutes, or this spec grows a denylist.
---

# Reset a forgotten password

## Goal

After this spec, a user who cannot sign in can recover the account without an operator.
`POST /api/v1/auth/password-reset-requests` answers `202` for any well-formed address — known,
unknown or unverified alike — and issues a single-use, hashed, one-hour token for an account that
exists. `POST /api/v1/auth/password-resets` spends that token exactly once, replaces the BCrypt hash,
and **revokes every refresh-token family the account holds**, so a password change ends every session
that was opened with the old one. This is the last unit of slice S1 and the second half of subject
V.1's "email verification + forgot password".

## Out of scope

- **Notifying the user that the password changed.** A second mail on success is good practice and a
  separate decision about what the Development-only sender may write; the reset itself does not depend
  on it.
- **Email change.** `authentication-authorization` puts it in the same sentence as reset because both
  invalidate sessions, but no endpoint exists for it and the contract does not list one.
- **Rehash on login when the stored cost is below the configured one.** Still the follow-up SPEC-005
  recorded; this spec writes a hash at the current cost, which is the same code path.
- **Logout and session listing.** They need the bearer middleware, which lands with the first
  authenticated endpoint. A reset revokes families without any of that: it is keyed on the token.
- **A breached-password check.** `authentication-authorization` mentions no feed, T-01 records its
  absence, and adding one is a dependency and a decision.
- **The expired-token sweep.** Expiry is enforced at the point of use, which is the mechanism security
  depends on — ADR-0024 mechanism 2 is still its own spec.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — escalation 1. Nothing in the repository says how one request writes two aggregates atomically: SPEC-002 through SPEC-006 each stayed inside one table plus, at most, a column on `users`. The mechanism chosen for "consume the token, replace the hash, revoke every family" is the one every later multi-aggregate write inherits, so it belongs in an ADR. **ADR-0021 also still owes two Consequences lines** — the JwtBearer package from SPEC-005 and the in-process backoff from SPEC-006 |
| 2 | new or upgraded dependency? | no — BCrypt, MediatR, FluentValidation and EF Core are all already pinned and referenced |
| 3 | schema change beyond this spec, or destructive migration? | no — one new table, `password_reset_tokens`, defined in this spec; additive, no column dropped, no data moved. The reset itself updates `users.password_hash` and `refresh_tokens.revoked_at_utc`, both existing columns |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — `POST /auth/password-reset-requests` and `POST /auth/password-resets` are already specified there with their status codes, and the note "`/auth/password-resets` revokes every refresh token for that account on success" is already written. This spec implements two existing rows |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is an account-takeover path by design. A mistake here hands an account to whoever can guess or read a token |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — decided by the owner, 2026-09-27

| # | Decision |
|---|---|
| 1 | **Option A** — `IUserRepository.TryResetPasswordAsync` runs all three statements in one transaction and writes `refresh_tokens` itself. The ADR that records this says the **next** multi-aggregate write reconsiders an explicit unit of work. **The ADR is the owner's to write — no agent writes an ADR.** |
| 2 | **The same numbers as the verification pair** — `/auth/password-reset-requests` 10 per hour per IP plus the existing per-account counter charged on every attempt, `/auth/password-resets` 30 per 5 minutes per IP. Same work, same cost, same numbers. |
| 3 | **Accept, and widen T-26.** The reset token passes through the Development-only logging sender, so the flow is exercisable before O17. The register row now says takeover, not verification. |
| 4 | **Accept the 15-minute window.** An access token minted before the reset stays valid until it expires; the short lifetime is the mitigation SDD §13.1 already chose. The threat register states what a reset does not revoke. |

## Escalation — what a human must decide

### 1. How one transaction spans two aggregates (gate 1)

A successful reset must do three things or none of them:

1. mark the reset token consumed,
2. replace `users.password_hash`,
3. stamp `revoked_at_utc` on every unrevoked row of every refresh-token family the account holds.

If (1) and (2) commit and (3) does not, the user has a new password and the thief keeps a live
session — the exact failure the control exists to prevent. Three ways to make it one transaction:

| Option | Mechanism | Cost |
|---|---|---|
| A | `IUserRepository.TryResetPasswordAsync` runs all three statements in one transaction, writing `refresh_tokens` itself | smallest diff, one round of statements, and the existing shape: `TryConsumeEmailVerificationTokenAsync` already writes two tables in one transaction. Against it: the account write path now knows the session table, so "which repository owns `refresh_tokens`" has two answers |
| B | an explicit `IUnitOfWork` abstraction the handler opens, with both repositories enlisting in it | each repository keeps its own tables, and the handler states the boundary. Against it: a new abstraction on the critical path of every future write, and `DbContext` is already a unit of work — this wraps one in another |
| C | a `TransactionBehaviour` in the MediatR pipeline, so every command runs in a transaction | the pattern `reference/nArchitecture` uses, and it never has to be remembered per handler. Against it: it puts a transaction around commands that do not need one, including reads that flow through the same pipeline, and it hides the boundary from the handler that depends on it |

**Recommendation, not a decision:** A for this spec, and the ADR that records it says explicitly that
the *next* multi-aggregate write reconsiders B. Two tables in one repository method is a known,
reviewable shape here; a pipeline-wide transaction is a decision about every future command.

### 2. The rate-limit numbers for two more endpoints (gate 5)

SPEC-006 approved a table for five endpoints. These two are not in it, and both need to be: the
request endpoint sends mail (T-19) and the reset endpoint accepts a guessable secret.

A starting proposal, on the shape of the approved rows. Every number is a guess until signed:

| Endpoint | Per IP | Per named account |
|---|---|---|
| `POST /auth/password-reset-requests` | 10 / hour | the existing per-account counter, charged on every attempt — each one writes a row and spends mail quota |
| `POST /auth/password-resets` | 30 / 5 min | — the request names no account, only a token |

Those mirror `/auth/verification-requests` and `/auth/email-verifications` exactly, which is the
argument for them: the same work, the same cost, the same numbers.

### 3. May a reset token pass through the logging mail sender (gate 5)

The only `IMailService` today is `LoggingMailService`, which writes the token to the application log
and is refused outside Development by a start-up guard. T-26 records that: anyone who can read a
developer's container logs can **verify** an account created on that machine. A reset token in the
same log is strictly worse — it lets that reader **take the account over**, password included.

Three answers:

- **Accept and widen T-26.** Development hosts only, mechanically enforced, and the value is that the
  whole flow is exercisable before O17 lands a provider. The register row says takeover, not
  verification.
- **Log a reference, not the token.** The sender writes the account and a correlation id, and the
  developer reads the plaintext from nowhere — which means the flow cannot be exercised end to end
  without a real mailbox, and the endpoint ships untested against a real client.
- **Hold the reset endpoints until O17.** Honest, and it leaves a graded V.1 requirement unbuilt while
  a dependency nobody has scheduled is waited on.

### 4. Does a reset kill access tokens too (gate 5)

Revoking refresh families ends every session's ability to *continue*. It does not touch the access
tokens already issued: a JWT is validated by signature and `exp`, so one minted a minute before the
reset stays valid for up to fourteen more. `docs/SDD.md` §13.1 fixes the 15-minute lifetime precisely
so this window is small.

- **Accept the window.** The 15 minutes is the stated mitigation, it is what the design chose, and the
  defence answer is one sentence.
- **Build a denylist.** A `jti` or a per-user "not valid before" instant checked on every authenticated
  request. It needs storage, it needs the bearer middleware that does not exist yet, and it makes
  every request read that state. Beyond this spec, and a decision about the whole authentication path.

Whichever is chosen, the threat register says what a reset does **not** revoke.

## Files

### Discovered during implementation, 2026-09-27

Three files beyond the table, plus the two the walk had already predicted. Every one is a mechanical
consequence of a listed row; no design decision was taken here. **The owner accepts or rejects this
delta at review.**

| Path | Action | Why |
|---|---|---|
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PasswordResetEndpoints.cs` | create | adding two routes to `AuthEndpoints.cs` pushed it to 313 lines and 33 referenced types, which the root .editorconfig refuses: `error S104: This file has 313 lines, which is greater than 300 authorized` and `error S1200`. The reset pair moved to its own class; `AuthEndpoints.cs` is back to 299 lines |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/CredentialEndpointResults.cs` | create | `Throttled` and `ToErrors` were private to `AuthEndpoints` and both classes now answer with them. One copy, because a second copy of "what a throttled request answers" is a divergence waiting to happen |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change | delegates the two routes and calls the extracted helpers |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RecordingMailService.cs` | change | predicted by the walk: it implements `IMailService` and stops compiling when the interface gains a member. It now records reset tokens too |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change | it holds `TestRateLimitSettings`, which needs the two new rate-limit prefixes |


Exact paths, created or changed. An unattended run must not touch anything outside this list.
The rows marked "escalation 1 option A" assume the recommendation; a different option changes them,
and the table is re-walked against the decisions before implementation starts.

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/PasswordResetToken.cs` | create — `UserId`, `TokenHash`, `ExpiresAtUtc`, `ConsumedAtUtc`, the shape `EmailVerificationToken` already has |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/PasswordResetTokenIssuer.cs` | create — 32 random bytes, base64url, hashed with `OpaqueTokenHash`, 1-hour window per ADR-0024 and `authentication-authorization` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — `TryIssuePasswordResetTokenAsync` and, under escalation 1 option A, `TryResetPasswordAsync` plus the outcome type beside it |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Messaging/IMailService.cs` | change — `SendPasswordResetAsync`, documented as carrying a plaintext that is an account-takeover credential |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestPasswordReset/RequestPasswordResetCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestPasswordReset/RequestPasswordResetCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestPasswordReset/RequestPasswordResetCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandValidator.cs` | create — the same 12-64 character and 72-byte UTF-8 rules registration enforces, because the same BCrypt truncation applies |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordOutcome.cs` | create — `Reset`, `Expired`, `Invalid`, mirroring `VerifyEmailOutcome` so the endpoint maps `204`, `410`, `400` |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/LoggingMailService.cs` | change — implement the new member; escalation 3 decides what it may write |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/PasswordResetTokenConfiguration.cs` | create — table `password_reset_tokens`, `char(64)` `token_hash` with `ux_password_reset_tokens_token_hash`, `ix_password_reset_tokens_user_id`, `timestamp with time zone` on every instant, cascade foreign key, no navigation property |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — add the `DbSet` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — implement both new members |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_PasswordResetTokens.cs` | create — `dotnet ef migrations add` output, reformatted by hand for the analyzers as the two existing migrations were |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_PasswordResetTokens.Designer.cs` | create — same command |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/MusicRoomDbContextModelSnapshot.cs` | change — same command |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — map both routes, their request records, `RequireRateLimiting`, and the documented status codes |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CredentialRateLimitOptions.cs` | change — two more windows, from escalation 2 |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CredentialRateLimiterPolicies.cs` | change — two more named policies |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/RequestPasswordResetCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/ResetPasswordCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/LoggingMailServiceTests.cs` | change — assert what the sender writes for a reset, which is escalation 3's decision made testable |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordResetEndpointTests.cs` | create |
| `.env.example` | change — the two new rate-limit override keys, alongside the SPEC-006 block |
| `docs/api/openapi.json` | change — regenerated by `make openapi` |
| `docs/SDD.md` | change — §16 V.1 "email verification + forgot password" row, which still says forgot password is designed only; §13.3 gains the two rate-limit rows |
| `docs/owasp/THREAT-REGISTER.md` | change — T-26 widened per escalation 3, a row for what a reset does **not** revoke per escalation 4, and T-25's endpoint list extended with the two new routes now that they are limited from the start |
| `brain/migrations/migrations-index.md` | change — the new migration row |
| `docs/SDD.pdf`, `docs/owasp/THREAT-REGISTER.pdf` | change — regenerated by `./docs/build-pdf.sh`, never hand-edited; `make agent-check` diffs them |
| `docs/specs/SPEC-007-password-reset.md` | change — status transitions |

### The Files table walk

The five checks `docs/prompt-library/PROMPTS.md` §2.3 requires, answered against the tree as it is
today.

**1. Assembly membership and every reference it implies.** `PasswordResetToken` goes in `Domain`,
which references nothing. The issuer, both commands, their handlers, validators and the outcome go in
`Application`, whose `MediatR` and `FluentValidation` references already exist. The configuration and
the repository change are `Persistence`, the sender change is `Infrastructure`, and the endpoints are
`WebAPI`. Every one of those references already exists and every one points inward, matching
`docs/SDD.md` §7.1. **No new project, no `.csproj` change anywhere in this spec.**

**2. Types that cross an assembly boundary.** `PasswordResetToken`, the two commands, the outcome and
the two new `IUserRepository` members cross one, so each is `public`; the outcome gets its own file
because `VerifyEmailOutcome` has one and the pair should read the same. The handlers stay
`internal sealed` with a public constructor, activated by MediatR. `PasswordResetTokenIssuer` mirrors
`VerificationTokenIssuer`: `internal static`, in `Features/Auth`, called by one handler.
`OpaqueTokenHash` is already `public` in `Abstractions/Security` — SPEC-005 moved it there precisely
so a second token type could share it, and this is that second type. **Nothing is copied.**

**3. Does anything actually invoke it?** `AuthEndpoints` sends both commands; both handlers call
`IUserRepository`, the request handler also calls `IMailService` and the reset handler also calls
`IPasswordHasher` and `ICredentialAttemptLimiter`; `ValidationBehaviour<,>` is already an open
behaviour, so both validators run with no registration of their own. No new registration is needed in
`PersistenceServiceRegistration` or `InfrastructureServiceRegistration`: both new members hang off
interfaces that are already registered.

**4. CLI, tool manifest, or unpinned package.** `dotnet ef migrations add` only, which is available:
`Microsoft.EntityFrameworkCore.Design` is pinned and referenced with `PrivateAssets="all"` by
`Persistence` and `WebAPI`, and SPEC-005 produced `20260927183531_RefreshTokens` this way.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Security` holds
**nothing but a `.csproj`**; `Core.Application` holds only
`Pipelines/Validation/ValidationBehaviour.cs`; `Core.CrossCuttingConcerns` and `Core.Persistence` hold
only their `.csproj`. So there is no transaction helper to reuse there — which matters, because
escalation 1 option C would put one in `Core.Application`, and that is a decision about the shared
layer rather than about this spec.

**What the walk caught.** Two things, both about tests rather than source.
`LoggingMailServiceTests` constructs the sender and asserts what it logged; adding a member to
`IMailService` breaks nothing there, but `RecordingMailService` in `MusicRoom.WebAPI.Tests` **is** an
`IMailService` implementation and will not compile until it implements the new member — and that file
is not in the table above. It is one method, and it is the same mechanical consequence SPEC-005 and
SPEC-006 each hit once; it is listed here rather than discovered later:

| Path | Action |
|---|---|
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RecordingMailService.cs` | change — implement `SendPasswordResetAsync` and record the token, so the endpoint suite can spend it |

The second: the new endpoints are rate limited from their first commit, so `PasswordResetEndpointTests`
must raise the permits through `TestRateLimitSettings.Relax`, which already exists and already covers
every key by prefix — it gains the two new prefixes in the same change as the options record.

## Steps

Written against the recommendations. **Re-walk against the four decisions before starting.**

1. `PasswordResetToken` in `Domain`, then `PasswordResetTokenIssuer` in `Application`: 32 random bytes,
   base64url, digest via `OpaqueTokenHash`, `ExpiresAtUtc = nowUtc.AddHours(1)`. The plaintext leaves
   the issuer once, to reach the sender.
2. `PasswordResetTokenConfiguration` and the migration, in that order, with the same conventions as
   `EmailVerificationTokenConfiguration` so `SchemaConventionTests` passes unchanged.
3. `IUserRepository.TryIssuePasswordResetTokenAsync(email, token, nowUtc, ct)`: resolve the account
   inside the transaction that inserts the row, supersede any outstanding reset token for it exactly as
   SPEC-004 supersedes verification tokens, and return `false` — writing nothing — when no account
   holds the address. The caller answers `202` either way.
4. `IUserRepository.TryResetPasswordAsync(tokenHash, newPasswordHash, nowUtc, ct)` under escalation 1
   option A: one transaction, three statements. A conditional `UPDATE` on
   `consumed_at_utc IS NULL AND expires_at_utc > @now` arbitrates single use; whoever affects the row
   won. Then `users.password_hash`, then `refresh_tokens.revoked_at_utc` for every unrevoked row of
   that user. No read-then-write in C#.
5. `RequestPasswordResetCommandHandler`: charge the per-account limiter on every attempt, issue, then
   send the mail **after** the insert commits, swallowing a send failure the way registration and
   resend do — an unknown address and a failed send must be indistinguishable from success.
6. `ResetPasswordCommandHandler`: hash the new password at the configured cost, then call the write
   path. Return the outcome; never say which of unknown, malformed or spent a token was.
7. `AuthEndpoints`: `POST /password-reset-requests` → `202` always, `422` on a malformed address,
   `429` when limited; `POST /password-resets` → `204`, `410` for an expired token, `400` for unknown,
   malformed and spent alike, `422` when the **new password** breaks the policy, `429` when limited.
   A `422` here is about the new password only, never about the token.
8. Tests, then the migration, then regenerate `docs/api/openapi.json` via the build.

## Verification

```bash
make agent-check
```

That one target is the whole gate: `dotnet restore`, `dotnet format --verify-no-changes`,
`dotnet build -warnaserror --no-restore`, `dotnet test --no-build`,
`dotnet list package --vulnerable --include-transitive`, `make openapi` with
`git diff --exit-code -- docs/api/openapi.json`, then `./docs/build-pdf.sh` with
`git diff --exit-code -- '*.pdf'`. It needs Docker and pandoc; a missing tool is an environment result,
not a failure of the code.

| Check | Proves |
|---|---|
| `Request_ForAKnownAddress_Returns202AndSendsAToken` | V.1 — the flow starts |
| `Request_ForAnUnknownAddress_ReturnsTheSameBodyAndStatusAndSendsNothing` | V.6 — no enumeration oracle |
| `Request_Twice_SupersedesTheFirstToken` | one live reset link at a time, as SPEC-004 decided for verification |
| `Reset_WithTheIssuedToken_Returns204AndTheNewPasswordSignsIn` | V.1 end to end, through `POST /auth/sessions` |
| `Reset_WithTheIssuedToken_RevokesEveryRefreshTokenFamily` | `docs/api/ENDPOINTS.md` §1 and SDD §13.1: a refresh token issued before the reset now answers `401` |
| `Reset_ReplayingASpentToken_Returns400AndLeavesThePasswordAsReset` | single use, and no second takeover from one link |
| `Reset_WithAnExpiredToken_Returns410` | ADR-0024 — the one-hour window, enforced at the point of use |
| `Reset_WithAnUnknownToken_Returns400` | unknown and spent answer alike |
| `Reset_WithAWeakNewPassword_Returns422AndDoesNotSpendTheToken` | the policy applies to the new password, and a rejected attempt does not burn the link |
| `Reset_TwentyParallelResetsOfOneToken_ProduceExactlyOneSuccess` | the AGENTS.md parallel-test rule, asserting final database state: one `consumed_at_utc`, one password hash, every family revoked |
| `Reset_AfterReset_TheOldPasswordIsRefused` | the hash was actually replaced |
| `RequestPasswordResetCommandValidatorTests`, `ResetPasswordCommandValidatorTests` | the validation layer the testing standard requires |
| `LoggingMailServiceTests` — the reset message | escalation 3's decision, asserted rather than assumed |
| `SchemaConventionTests` (existing, unchanged) | `password_reset_tokens` obeys the naming conventions |

A sequential test proves nothing about a race. The parallel reset test must run its twenty requests in
parallel and then read the database.

## Done when

- [x] every file in the Files table exists and compiles, plus the five discovered above
- [x] every command under Verification passes — `make agent-check` exited 0 on 2026-09-27, 105 tests
- [x] OWASP checklists walked for the surface touched (A02, A04, A07, A09, API2, API4). Two register
      changes and one new row: **T-26 widened** from "verify an account" to "take an account over", and
      **T-28** records what a reset does not revoke — an access token issued before it stays valid for
      the remainder of its 15 minutes
- [x] `docs/SDD.md` §16 V.1 now states the reset flow end to end, including what it does not revoke
- [x] `docs/SDD.md` §13.3 carries the two new rate-limit rows
- [x] `brain/migrations/migrations-index.md` carries the `password_reset_tokens` row
- [x] `docs/api/openapi.json` regenerated in the same change, and `docs/SDD.pdf` plus
      `docs/owasp/THREAT-REGISTER.pdf` rebuilt by `./docs/build-pdf.sh`, so both drift guards pass
- [x] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| A "your password changed" notification mail | its own small spec, once a real sender exists (O17) |
| Email change, which invalidates sessions the same way | a later spec; no endpoint is specified yet |
| Rehash on login below the configured cost | the follow-up SPEC-005 recorded |
| An access-token denylist | escalation 4, and only with the bearer middleware |
| The expired-token `BackgroundService` sweep | ADR-0024 mechanism 2 — now three token tables wide |
