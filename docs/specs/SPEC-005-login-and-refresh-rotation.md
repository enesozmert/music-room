---
id: SPEC-005
title: Sign in and rotate a session — RefreshToken aggregate, JWT access token issuance, POST /auth/sessions and POST /auth/tokens
status: draft          # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0007, ADR-0011, ADR-0012, ADR-0021, ADR-0024, ADR-0028]
standards: [authentication-authorization, application-security, clean-architecture-cqrs, ef-core-persistence, dotnet-csharp, testing]
approved_by:           # name, or "agent" for a clean Tier A gate
escalation: |
  Gates 1, 2 and 5 answer "yes". Three decisions belong to the repository owner before this spec
  may be implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) how an HS256 access token is produced, which is a new package and therefore an
  amendment to ADR-0021; (2) the refresh-token lifetime, which no ADR or standard currently states;
  (3) whether login and rotation ship without rate limiting under the existing T-25 deferral, when
  authentication-authorization calls rate limiting on credential endpoints mandatory.
---

# Sign in and rotate a session

## Goal

After this spec, a verified account can exchange its email and password for a session, and can keep
that session alive without re-entering the password. `POST /api/v1/auth/sessions` answers `200` with
a token pair — a 15-minute JWT access token and an opaque refresh token — or a single generic `401`
for every failure. `POST /api/v1/auth/tokens` spends a refresh token exactly once and returns a
rotated pair; presenting an already-rotated token revokes the whole token family and answers `401`,
which is the theft detection `docs/SDD.md` §13.1 requires. Refresh tokens are stored only as
SHA-256 digests, so a leaked database hands nobody a working session.

This is the third unit of slice S1. Registration (SPEC-002) and email verification (SPEC-003) are
done; login and refresh rotation are the next items in the `docs/DELIVERY-PLAN.md` S1 row.

## Out of scope

- **Validating** an access token. `AddJwtBearer`, `UseAuthentication` and `UseAuthorization` are not
  in this spec, because no endpoint requires authentication yet. A registration nothing invokes is
  a file that compiles and does nothing. Validation lands with the first authenticated endpoint,
  which is `DELETE /auth/sessions/current` (logout).
- Logout, `GET /auth/sessions`, and `DELETE /auth/sessions/{sessionId}`. All three need the
  authentication middleware above.
- `POST /auth/sessions/social` — slice S8, ADR-0008.
- `POST /auth/password-resets` and resending a verification mail. Both are later S1 specs, and the
  reset flow revokes refresh-token families, which is why it comes after this spec rather than with it.
- Rate limiting. The `"fixed"` policy registered in `Program.cs` is attached to no endpoint and stays
  that way here; the deferral is recorded as T-25 in `docs/owasp/THREAT-REGISTER.md`. See gate 5.
- The `BackgroundService` sweep of expired refresh tokens (ADR-0024 mechanism 2). Expiry is enforced
  at the point of use in this spec, which is the mechanism security depends on; the sweep is hygiene
  and gets its own spec.
- Rehash on login when the stored BCrypt cost is below the current setting. The standard requires it;
  it needs a second write path on `IUserRepository` and is a separable unit. Recorded below as a
  follow-up, not silently dropped.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0021 fixes the dependency set and names no JWT library. Whichever option in the escalation is chosen, ADR-0021's Consequences section must record it, exactly as it records the MediatR licence verification |
| 2 | new or upgraded dependency? | **yes** — producing a signed JWT needs a token library that is not in `Directory.Packages.props`. The candidates and the zero-dependency alternative are in the escalation |
| 3 | schema change beyond this spec, or destructive migration? | no — one new table, `refresh_tokens`, defined in this spec; additive, no column dropped, no data moved |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — `POST /auth/sessions` and `POST /auth/tokens` are already specified there, including their status codes and the rotation note. This spec implements two existing rows |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — this is the session mechanism. It verifies a password, issues an access token, persists refresh-token state, and reads a signing key from configuration |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — what a human must decide

### 1. How the access token is produced (gates 1 and 2)

`docs/SDD.md` §13.1 fixes the session model: a 15-minute JWT signed with a key from `.env`.
`authentication-authorization` allows "a strong asymmetric or HMAC key". It does not say which
library produces it, and ADR-0021 does not list one. Three options, with the cost of each:

| Option | Package | Cost |
|---|---|---|
| A | `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler`) | one Microsoft-owned runtime package, the modern replacement for `System.IdentityModel.Tokens.Jwt`. Issuance only; validation later reuses the same types |
| B | `Microsoft.AspNetCore.Authentication.JwtBearer` now | the package validation will need anyway, so ADR-0021 is amended once instead of twice. But it registers an authentication handler nothing in this spec uses, which is the dead-registration smell this spec's Out of scope avoids |
| C | no package — hand-write HS256 | `HMACSHA256` plus `Base64Url` from the BCL, roughly 30 lines behind `IAccessTokenIssuer`. Zero dependency, and the precedent exists: ADR-0021 already names a hand-written dispatcher as the MediatR fallback. Against it: hand-rolled token code is what `application-security` warns about, even though the warning is aimed at hand-rolled *validation* |

**Recommendation, not a decision:** A. It keeps this spec's surface honest (issuance only, no idle
handler), and B's saving is one ADR edit. C is defensible and cheap, and a defence panel asking
"why did you write your own JWT encoder" is a worse conversation than "why this package".

### 2. The refresh-token lifetime

Nowhere stated. `authentication-authorization` fixes the access token at 15 minutes, verification at
24 hours and reset at 1 hour, and says only that refresh tokens rotate and are revocable.
This spec assumes **30 days absolute, with no sliding extension**: each rotation issues a token whose
expiry is inherited from the family's original expiry, so a stolen family cannot be kept alive
indefinitely by rotating it. Confirm the number and the absolute-versus-sliding choice, or supply
different ones. If the answer changes the model, it is a one-line change to the issuing handler.

### 3. Rate limiting on the credential endpoints

`authentication-authorization` calls rate limiting on login mandatory, per IP **and** per account,
with progressive backoff and `429` + `Retry-After`. `docs/api/ENDPOINTS.md` lists `429` on both
endpoints in this spec. Today no endpoint is attached to the rate limiter, and the gap is accepted as
T-25. Login is the endpoint T-25 costs the most: without it, this spec ships a password-guessing
oracle that answers in constant time.

Two acceptable answers, and this spec cannot pick either:

- **Ship as written** and keep T-25 open, with the register row updated to say login is now live.
  Then rate limiting becomes the next S1 spec, before any further endpoint.
- **Widen this spec** to attach a per-IP and per-account partition to both endpoints. It is the
  correct order of work and makes the spec noticeably bigger.

## Files

Exact paths, created or changed. An unattended run must not touch anything outside this list.

| Path | Action |
|---|---|
| `MusicRoom/Directory.Packages.props` | change — pin the token package chosen in escalation 1 (no change if C) |
| `MusicRoom/src/MusicRoom/Domain/Entities/RefreshToken.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/IAccessTokenIssuer.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/OpaqueTokenHash.cs` | create — the shared digest, moved out of the VerifyEmail folder |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IRefreshTokenRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — add the credential read path |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/AuthTokenPair.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RotateSession/RotateSessionCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RotateSession/RotateSessionCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RotateSession/RotateSessionCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailCommandHandler.cs` | change — call `OpaqueTokenHash`, drop the local `VerificationTokenHash` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | change — call `OpaqueTokenHash` |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtAccessTokenIssuer.cs` | create |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtOptions.cs` | create |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — register the issuer |
| `MusicRoom/src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj` | change — reference the token package (no change if C) |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/RefreshTokenConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/RefreshTokenRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — implement the credential read path |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — add the `DbSet` |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — register the repository |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_RefreshTokens.cs` | create — `dotnet ef migrations add` output |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_RefreshTokens.Designer.cs` | create — same command |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/MusicRoomDbContextModelSnapshot.cs` | change — same command |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — map both routes, add the request and response records |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — read the JWT settings, pass them to `AddInfrastructureServices` |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/LoginCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/RotateSessionCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/JwtAccessTokenIssuerTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SessionRotationEndpointTests.cs` | create |
| `.env.example` | change — add the JWT settings as empty placeholders |
| `docs/api/openapi.json` | change — regenerated by the build |
| `docs/SDD.md` | change — §16 rows for V.1 and V.8 |
| `docs/owasp/THREAT-REGISTER.md` | change — T-25 now covers a live login endpoint |
| `brain/migrations/migrations-index.md` | change — the new migration row |
| `docs/specs/SPEC-005-login-and-refresh-rotation.md` | change — status transitions |

### The Files table walk

The five checks `docs/prompt-library/PROMPTS.md` §2.3 requires, answered against the tree as it is
today, not as it is remembered.

**1. Assembly membership and every reference it implies.** `RefreshToken` goes in `Domain`, which
references nothing; `Application` and `Persistence` both already reach `Domain`
(`Persistence -> Application -> Domain`), so no `.csproj` changes for it. The two abstractions go in
`Application`, whose `MediatR` and `FluentValidation` references already exist. `JwtAccessTokenIssuer`
goes in `Infrastructure`, next to `BCryptPasswordHasher`; `Infrastructure` already references
`Application`, so the only `.csproj` edit in the whole spec is the token package on
`MusicRoom.Infrastructure.csproj` plus its `PackageVersion` in `Directory.Packages.props`. Every
reference points inward, matching `docs/SDD.md` §7.1. **No new project, and nothing points outward.**

**2. Types that cross an assembly boundary.** `IAccessTokenIssuer`, `IRefreshTokenRepository`,
`OpaqueTokenHash`, `AuthTokenPair`, `LoginCommand` and `RotateSessionCommand` all cross one, so each
is `public` and each gets its own file. `OpaqueTokenHash` is the reason two SPEC-003 files appear in
the table as changes: the digest algorithm currently lives as `internal static class
VerificationTokenHash` inside the VerifyEmail folder, and refresh tokens need the identical
algorithm. Copying it would create exactly the bug its own remark warns about — "a copy in each
feature folder is a bug waiting for one of them to be changed" — so it moves to
`Abstractions/Security/`, keeps its remarks, and the two existing callers are updated. The handlers
stay `internal sealed`, activated by MediatR through a public constructor, as SPEC-002 and SPEC-003
do it.

**3. Does anything actually invoke it?** `AuthEndpoints` sends both commands; both handlers call
`IUserRepository` or `IRefreshTokenRepository` and, for login, `IAccessTokenIssuer`;
`ValidationBehaviour<,>` is already an open behaviour in `ApplicationServiceRegistration`, so both
validators run without any registration of their own. `JwtAccessTokenIssuer` is registered in
`InfrastructureServiceRegistration` and resolved by the login handler. The one thing with no caller
is JWT *validation*, which is why it is in Out of scope rather than in this table.

**4. CLI, tool manifest, or unpinned package.** `dotnet ef migrations add` is already available:
`Microsoft.EntityFrameworkCore.Design` is pinned and referenced with `PrivateAssets="all"` by both
`Persistence` and `WebAPI`, and SPEC-002 produced `20260924195820_InitialIdentity` this way. No tool
manifest is needed. The token package is the gate 2 trip.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Security` holds
**nothing but a `.csproj`** — its own comment says it stays empty until a domain-agnostic type
exists. `Core.Application` holds one file, `Pipelines/Validation/ValidationBehaviour.cs`.
`Core.CrossCuttingConcerns` and `Core.Persistence` hold only their `.csproj`. So there is no token
helper, no clock and no result type to reuse there, and nothing in this spec is written into
`Core.Security`: a type that names a refresh token is not domain-agnostic.

**One thing the walk caught.** `AddInfrastructureServices` today takes four parameters
(`this services`, `bcryptWorkFactor`, `isDevelopment`, `verificationLinkBaseUrl`). The JWT settings
are three values — signing key, issuer, audience — and passing them separately would make seven
parameters, which `SonarLint.xml` fails with `S107`, proven live in S0 at six. They therefore travel
as one `JwtOptions` record, keeping the signature at five parameters. That is at the limit, and the
next setting this method needs will require collapsing the whole list into one options record.

## Steps

1. `RefreshToken : Entity` in `Domain`: `UserId`, `FamilyId`, `TokenHash`, `ExpiresAtUtc`,
   `ConsumedAtUtc`, `RevokedAtUtc`. `FamilyId` is what makes reuse detection a single indexed
   `UPDATE` rather than a walk along a chain of parent pointers.
2. `OpaqueTokenHash.Of` — move the hex SHA-256 out of `VerifyEmailCommandHandler` unchanged, byte for
   byte, and point `RegisterCommandHandler` and `VerifyEmailCommandHandler` at it. Do this first and
   on its own, so the migration and the new handlers are not mixed into a move.
3. `RefreshTokenConfiguration`: table `refresh_tokens`, snake_case columns, `token_hash`
   `char(64)` fixed length with `ux_refresh_tokens_token_hash`, `ix_refresh_tokens_family_id`,
   `ix_refresh_tokens_user_id`, `timestamp with time zone` on all four instants, and a cascade
   foreign key to `users` with no navigation property — the same shape as
   `EmailVerificationTokenConfiguration`, so `SchemaConventionTests` passes unchanged.
4. `IUserRepository.FindCredentialByEmailAsync(string email, CancellationToken)` returning a small
   record (`Id`, `PasswordHash`, `IsEmailVerified`) rather than the `User` entity: the login path has
   no reason to hold a tracked aggregate, and a narrow projection cannot accidentally be logged whole.
5. `IRefreshTokenRepository` with two members: `IssueAsync(RefreshToken, CancellationToken)`, and
   `TryRotateAsync(string tokenHash, RefreshToken replacement, DateTime nowUtc, CancellationToken)`
   returning a rotation outcome. Rotation is arbitrated **in the database**, by a conditional update
   on `consumed_at_utc IS NULL AND revoked_at_utc IS NULL AND expires_at_utc > @now`, the same way
   `ux_users_email` arbitrates registration and `consumed_at_utc IS NULL` arbitrates verification.
   No read-then-write in C#.
6. `TryRotateAsync` on a token whose row exists but is already consumed: revoke the family with one
   `UPDATE ... WHERE family_id = @family AND revoked_at_utc IS NULL`, then report reuse. Both
   statements share one transaction.
7. `LoginCommandHandler`: look up the credential, `IPasswordHasher.Verify`, require
   `IsEmailVerified`, then issue the pair. **Every** failure — unknown address, wrong password,
   unverified account — returns the same outcome, and the handler runs `Verify` against a fixed dummy
   hash when no row was found, so a missing account and a wrong password cost the same time. An
   endpoint that answers faster for an unknown address is an enumeration oracle with a stopwatch.
8. `JwtAccessTokenIssuer`: HS256, 15-minute lifetime, claims `sub`, `iat`, `exp`, `jti` and nothing
   else. No email, no display name — a JWT is signed, not secret.
9. `RotateSessionCommandHandler`: hash the presented token, call `TryRotateAsync`, and on success
   issue a new access token alongside the replacement refresh token. The replacement inherits the
   family's `ExpiresAtUtc` (escalation 2).
10. `AuthEndpoints`: `POST /sessions` and `POST /tokens`, both `AllowAnonymous`, both `200` on
    success and a bodiless `401` for every failure. A `ValidationException` becomes `401` here, not
    `422`: the contract rows list `401` and `429` only, and a validation message describing why a
    refresh token was rejected is a hint about its shape.
11. `Program.cs`: read `JWT_SIGNING_KEY`, `JWT_ISSUER` and `JWT_AUDIENCE` the way `DATABASE_URL` is
    read — straight from configuration, no default, missing or short key fails start-up loudly. The
    key length floor is 32 bytes. Never log the key, never include it in the failure message.
12. `.env.example`: the three names with empty values and a comment saying the key must be a random
    32-byte value, generated per environment. No example value that anyone could copy into production.
13. Tests, then the migration, then regenerate `docs/api/openapi.json` via the build.

## Verification

```bash
make agent-check
```

`make agent-check` runs `dotnet format --verify-no-changes`, `dotnet build -warnaserror`,
`dotnet test` and `dotnet list package --vulnerable --include-transitive`. The API integration tests
need Docker: `MusicRoom.WebAPI.Tests` is Testcontainers-backed against a real PostgreSQL, because
`ADR-0012` rejects the in-memory provider for exactly the index-arbitration claims below.

| Check | Proves |
|---|---|
| `Login_WithCorrectPassword_ReturnsTokenPair` | V.1 — a verified account can sign in |
| `Login_WithWrongPassword_Returns401` | V.6 — the password is actually verified |
| `Login_WithUnknownAddress_ReturnsTheSameBodyAndStatusAsAWrongPassword` | V.6 — no enumeration oracle |
| `Login_WithUnverifiedAccount_Returns401` | SDD §11.1 — an unverified account cannot use the services |
| `Rotate_WithValidRefreshToken_ReturnsRotatedPairAndTheOldTokenStopsWorking` | single use, per the contract's rotation note |
| `Rotate_WithAlreadyRotatedToken_Returns401AndRevokesTheFamily` | SDD §13.1 theft detection |
| `Rotate_WithExpiredToken_Returns401` | ADR-0024 — expiry enforced at the point of use, not by the sweep |
| `Rotate_TwentyParallelRotationsOfOneToken_ProduceExactlyOneSuccess` | the AGENTS.md parallel-test rule, asserting final database state: one `consumed_at_utc`, one replacement row |
| `JwtAccessTokenIssuerTests` — claims, 15-minute expiry, signature over a known key | SDD §13.1, and that the token carries nothing sensitive |
| `LoginCommandValidatorTests`, `RotateSessionCommandValidatorTests` | the validation layer the testing standard requires |
| `SchemaConventionTests` (existing, unchanged) | `refresh_tokens` obeys the naming conventions |

A sequential test proves nothing about a race. The rotation test must run its twenty requests in
parallel and then read the database.

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] every command under Verification passes
- [ ] OWASP checklists walked for the surface touched; unfixed findings in the threat register
      (T-25 rate limiting is the known one and its row is updated, not left as written)
- [ ] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [ ] `brain/migrations/migrations-index.md` carries the `refresh_tokens` row
- [ ] `docs/api/openapi.json` regenerated in the same change
- [ ] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| JWT bearer validation, `UseAuthentication`, `UseAuthorization` | the logout spec, which is the first endpoint that needs them |
| Rehash on login when the stored BCrypt cost is below the configured one | its own small spec; needs a password-hash update path on `IUserRepository` |
| Rate limiting per IP and per account on the credential endpoints | escalation 3; T-25 |
| The expired-token `BackgroundService` sweep | ADR-0024 mechanism 2 |
