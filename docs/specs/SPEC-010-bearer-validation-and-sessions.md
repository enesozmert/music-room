---
id: SPEC-010
title: Authenticate a caller — bearer validation, logout, and the session list
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0007, ADR-0008, ADR-0011, ADR-0012, ADR-0021, ADR-0024, ADR-0028]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, dotnet-csharp, testing]
approved_by: enesozmert # repository owner, 2026-09-28, with the four decisions below
escalation: |
  Gates 1, 3 and 5 answer "yes". Four decisions belong to the repository owner before this spec may be
  implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) what `DELETE /auth/sessions/current` can even revoke, since an access token carries
  `sub`, `iat`, `exp` and `jti` and therefore does not say which refresh-token family it belongs to;
  (2) what `GET /auth/sessions` may list, which today would be a family id and two instants and nothing a
  human recognises — naming the device means storing it, which is a migration; (3) the clock skew the
  bearer handler allows, whose default silently turns a 15-minute token into a 20-minute one; (4) whether
  the audit record takes `user_id` from the validated token for every authenticated command, instead of
  each handler setting it.
---

# Authenticate a caller

## Goal

After this spec the API can tell who is calling. `AddJwtBearer` validates the access token SPEC-005
issues — signature, `iss`, `aud`, `exp` — and three endpoints the contract has specified since day one
start working: `DELETE /api/v1/auth/sessions/current` ends a session, `GET /api/v1/auth/sessions` lists
the caller's own, and `DELETE /api/v1/auth/sessions/{sessionId}` ends a named one. Every one of them is
the first of its kind in this codebase: the first endpoint that requires authentication, the first that
returns `404` rather than `403` for something the caller may not see, and the first whose audit record
can name the account without the handler working it out.

It is the unit SPEC-005 named in its Out of scope — "validation lands with the first authenticated
endpoint, which is `DELETE /auth/sessions/current`" — and it is the prerequisite for slice S3, whose
every row is `self` or `auth`.

## Out of scope

- **Profile, friends and the visibility tiers.** That is S3. This spec builds the gate S3 walks through,
  and deliberately not the first thing behind it.
- **The ownership and licence behaviours** of `docs/SDD.md` §13.2. Three endpoints that each act on the
  caller's own sessions need `sub` and nothing else; a pipeline behaviour for ownership arrives when an
  endpoint acts on something that might not be the caller's.
- **Role or scope claims.** Nothing in the subject needs them, and an unused claim is a claim that will
  be wrong when something finally reads it.
- **An access-token denylist.** T-28 records that a reset does not revoke access tokens already issued,
  and that the 15-minute lifetime is the accepted mitigation. Logout inherits the same limit, and this
  spec states it rather than solving it.
- **Refresh-token sliding expiry, device binding, or "sign out everywhere else".** All are features; the
  contract lists none of them.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — escalation 1 may change what an access token carries, which `docs/SDD.md` §13.1 fixes at "`sub`, `iat`, `exp`, `jti` and nothing else", and escalation 3 is a security parameter no document states. **Five ADR lines are already owed** (`brain/projects/musicroom-backend.md` "Open items"); this spec does not write them |
| 2 | new or upgraded dependency? | no — `Microsoft.AspNetCore.Authentication.JwtBearer` is already pinned and referenced, which is exactly why SPEC-005 chose it over an issuance-only package |
| 3 | schema change beyond this spec, or destructive migration? | **depends on escalation 2** — a session list that names the device needs columns on `refresh_tokens`, which is an additive migration. A list of ids and instants needs none. The spec cannot pick |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — all three rows exist with their status codes. A note may be added about what a session list contains, which is documentation of a decision, not a contract change |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is authentication. A mistake here either accepts a token nobody issued or ends the wrong caller's session |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — decided by the owner, 2026-09-28

| # | Decision |
|---|---|
| 1 | **Option B** — the access token gains a `sid` claim naming the refresh-token family, and `DELETE /auth/sessions/current` revokes that family. `docs/SDD.md` §13.1's claim list is amended to match. |
| 2 | **Store the client context on the family.** `platform`, `device` and `app_version` are persisted when a session starts and copied to every rotated row, so a session list names something a human recognises. Additive migration. |
| 3 | **Thirty seconds of clock skew.** A token is valid for at most 15 minutes 30 seconds, and T-28 is restated with that exact number. |
| 4 | **The client context supplies `user_id`.** `AuditBehaviour` fills it from the validated `sub` for every authenticated command; a handler may still override, because a login has no token yet. |

## Escalation — what a human must decide

### 1. What can `DELETE /auth/sessions/current` revoke (gate 1)

The contract says the request has no body and answers `204`. The caller presents an access token. That
token carries `sub`, `iat`, `exp` and `jti` — **it does not identify a refresh-token family**, so the
handler cannot tell which of the caller's sessions is "current".

| Option | Behaviour | Cost |
|---|---|---|
| A | revoke **every** family the account holds — logout means logout everywhere | correct with the data that exists, one indexed `UPDATE`, and it is what a password reset already does. Against it: a user logging out of a phone also loses their tablet, which is surprising and is not what "current" says |
| B | add a `sid` claim naming the family, and revoke that one | exactly what the contract implies, and `GET /auth/sessions` then has something to match against. Against it: it amends `docs/SDD.md` §13.1's claim list, and a family id in a signed, client-readable token is one more identifier to reason about |
| C | keep the body-less contract but revoke nothing, and let the client discard its tokens | no. Client-side deletion alone is not logout — `authentication-authorization` says so in as many words |

**Recommendation, not a decision:** B. It is the only option that makes the three endpoints coherent
with each other: a list of sessions you cannot individually end, and a "current" you cannot identify,
are the same missing claim seen twice. C is not an option, it is written here to be refused.

### 2. What may `GET /auth/sessions` list (gates 3 and 4)

`refresh_tokens` holds `user_id`, `family_id`, `token_hash`, three instants and the audit columns. A
session list built from that is a family id, when it started and when it expires. Nothing a human
recognises, and the point of the endpoint is for a human to spot a session they do not recognise.

- **List what exists.** Family id, created, expires, and whether it is the current one (needs
  escalation 1 option B). Honest, and thin.
- **Store the client context per family.** `platform`, `device` and `app_version` are already required
  on the request that creates the session, so a `POST /auth/sessions` could persist them on the family's
  first row: "iPhone 15, iOS, 1.2.3, started Tuesday" is a list worth showing. Cost: an additive
  migration, three columns, and the same values now live in two tables.
- **Defer the endpoint.** Ship validation and logout, leave `GET /auth/sessions` unmapped. Then S3 is
  unblocked and the contract row stays unimplemented for another slice.

**Recommendation, not a decision:** store the client context. The columns are small, the values are
already validated at the edge, and an audit row proves what happened while this list is what a *user*
can act on.

### 3. Clock skew (gate 5)

`AddJwtBearer` defaults `TokenValidationParameters.ClockSkew` to **five minutes**. With a 15-minute
access token that is a 33% extension of the window `docs/SDD.md` §13.1 fixed, and T-28 already accepts
"up to 15 minutes" of post-reset validity — silently making it 20 would make that row false.

- **Zero skew.** The window is exactly what was signed. Cost: a client whose clock is a minute fast sees
  a token rejected as not-yet-valid, on a one-instance deployment where both clocks are the same
  machine's — so the real cost is near zero here and non-zero in a distributed future.
- **Thirty seconds.** A token is valid for 15:30 at most. Enough for ordinary clock drift, small enough
  that T-28's "up to 15 minutes" stays approximately true and can be restated precisely.
- **Keep the default.** Nothing to write, and T-28 has to be corrected to 20 minutes.

**Recommendation, not a decision:** thirty seconds, and T-28 restated as "up to 15 minutes and 30
seconds".

### 4. Where the audit record's `user_id` comes from (gate 5)

SPEC-009 has each handler set `user_id` when it learns the account. For an authenticated command the
account is known before the handler runs: it is `sub` on the validated token.

- **The client context supplies it.** `IClientContextAccessor` already reads the request; it can read
  `sub` too, and `AuditBehaviour` fills `user_id` for every authenticated command without a handler
  remembering to. A handler may still override — a login has no token and sets it itself.
- **Leave it to the handlers.** One mechanism, already tested. Cost: seven handlers today and every
  authenticated handler forever must remember, and the one that forgets produces a row that says an
  anonymous caller did something only an authenticated one can do.

**Recommendation, not a decision:** the first. "Who was authenticated" is request context, not a fact a
handler discovers.

## Files

### Discovered during implementation, 2026-09-28

Three deltas, each forced by an analyzer or by the database rather than by a design change. **The owner
accepts or rejects this delta at review.**

| Path | Action | Why |
|---|---|---|
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change, larger than planned | wiring the bearer handler pushed `Main` to 42 coupled types: `error CA1506: 'Main' is coupled with '42' different types from '29' different namespaces`. The registration moved into a `CallerServices` class in the same file, beside `ApiServices` and `Observability` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/RefreshTokenConfiguration.cs` | change, larger than planned | three more columns pushed `Configure` to 51 lines: `error S138 ... greater than the 40 lines authorized`. Split into four mapping methods, as `AuditRecordConfiguration` already is |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/AuditRecordEndpointTests.cs` | change, plus a trim | adding one test tripped `error S104: This file has 301 lines, which is greater than 300 authorized`. A single-use helper folded into the assertion it served rather than moving a test to a suite it does not belong to |

One implementation note. `ListSessionsAsync` folds families in memory, not in SQL: the grouped projection that
picks each family's first row does not translate —
`The LINQ expression ... GroupBy ... could not be translated` — and the window function that would translate is
machinery this repository has no other reason to reach for. The database still narrows to one account's live
tokens and projects away every digest, so the fold is over a handful of rows.


Exact paths, created or changed. An unattended run must not touch anything outside this list.
The table assumes the recommendations — `sid` claim, stored client context, 30-second skew, context-supplied
`user_id`. **A different answer changes it, and it is re-walked against the decisions before implementation
starts.**

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/RefreshToken.cs` | change — **escalation 2**: `Platform`, `Device`, `AppVersion` on the family's rows, so a session list names something a human recognises |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/IAccessTokenIssuer.cs` | change — **escalation 1 option B**: `Issue` takes the family id, which becomes the `sid` claim |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtAccessTokenIssuer.cs` | change — emit `sid` |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/JwtBearerConfiguration.cs` | create — the validation parameters: signature, `iss`, `aud`, lifetime, the skew from escalation 3, and `RequireExpirationTime` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/IClientContextAccessor.cs` | change — **escalation 4**: `ClientContext` gains the authenticated account, null for an anonymous request |
| `MusicRoom/src/MusicRoom/Application/Pipelines/Auditing/AuditBehaviour.cs` | change — take `user_id` from the client context when the handler did not set one |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/HttpClientContextAccessor.cs` | change — read `sub` off the validated principal |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/ClientContextMiddleware.cs` | change — carry the account through `HttpContext.Items` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/ICurrentUserAccessor.cs` | create — what a handler asks for the caller's id and session id; `Application` declares it, `WebAPI` implements it over the principal |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/HttpCurrentUserAccessor.cs` | create — the implementation |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IRefreshTokenRepository.cs` | change — `RevokeFamilyAsync`, `RevokeFamilyOfUserAsync` and a projection for the session list |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/RefreshTokenRepository.cs` | change — implement all three |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/SessionIssuer.cs` | change — persist the client context on the family and pass the family id to the issuer |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/EndSession/EndSessionCommand.cs` | create — logout; auditable, action `auth.logout` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/EndSession/EndSessionCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/EndSession/EndSessionOutcome.cs` | create — ended, or not found, so the endpoint answers `204` or `404` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Queries/ListSessions/ListSessionsQuery.cs` | create — the first query in the codebase; **not** auditable, because reading your own session list is not a mutating action |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Queries/ListSessions/ListSessionsQueryHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Queries/ListSessions/SessionSummary.cs` | create — family id, platform, device, app version, started, expires, and whether it is the caller's current one |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/SessionEndpoints.cs` | create — the three routes, `RequireAuthorization`, and the response records. Its own file: `AuthEndpoints.cs` is at 299 lines of the 300 allowed |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — call `SessionEndpoints.Map` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — `AddAuthentication().AddJwtBearer(...)`, `UseAuthentication()` and `UseAuthorization()` in that order, after the rate limiter and before the client-context middleware |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/RefreshTokenConfiguration.cs` | change — map the three new columns |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_RefreshTokenClientContext.cs` | create — additive; `dotnet ef migrations add` output, reformatted by hand as the four existing migrations were |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_RefreshTokenClientContext.Designer.cs` | create — same command |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/MusicRoomDbContextModelSnapshot.cs` | change — same command |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/JwtAccessTokenIssuerTests.cs` | change — `sid` is present, and the claim set is still free of anything sensitive |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SessionEndpointTests.cs` | create — the three endpoints, the `401`s, the `404` for another caller's session, and logout ending exactly one session |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/BearerValidationTests.cs` | create — an absent, malformed, expired, wrongly-signed and wrong-audience token each answer `401` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/AuditRecordEndpointTests.cs` | change — an authenticated command's record names the account without the handler setting it |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — the helper that builds an authenticated client lives here, beside `TestJwtSettings` |
| `docs/api/ENDPOINTS.md` | change — a design note on what a session list contains and what "current" means |
| `docs/api/openapi.json` | change — regenerated; three routes and their `401`/`404` |
| `docs/SDD.md` | change — §13.1 claim list gains `sid` and the skew; §16 V.1 and V.6 rows |
| `docs/owasp/THREAT-REGISTER.md` | change — T-28 restated with the exact window from escalation 3; a row for "logout does not revoke access tokens" if that is not already covered |
| `brain/migrations/migrations-index.md` | change — the new migration row |
| `docs/SDD.pdf`, `docs/api/ENDPOINTS.pdf`, `docs/owasp/THREAT-REGISTER.pdf` | change — regenerated by `./docs/build-pdf.sh` |
| `docs/specs/SPEC-010-bearer-validation-and-sessions.md` | change — status transitions |

### The Files table walk

The five checks `docs/prompt-library/PROMPTS.md` §2.3 requires, answered against the tree as it is today.

**1. Assembly membership and every reference it implies.** `RefreshToken` is `Domain`. The two new
abstractions, the command, the query and their handlers are `Application`. `JwtBearerConfiguration` is
`Infrastructure`, beside `JwtAccessTokenIssuer`, and it needs `Microsoft.AspNetCore.Authentication.JwtBearer`
— already a `PackageReference` there since SPEC-005, which is the payoff of that decision. The accessors and
the endpoints are `WebAPI`. **No new project and no `.csproj` change**, every reference inward.

`Program.cs` calls `AddJwtBearer` and therefore needs the handler registered from `WebAPI`; the parameters
come from `Infrastructure` as a value. That keeps the web project free of the signing key's shape.

**2. Types that cross an assembly boundary.** `ICurrentUserAccessor`, `SessionSummary`,
`EndSessionOutcome`, the command and the query are `public`. `SessionSummary` is its own file: the endpoint
maps it to a response record and both will be read together. `ClientContext` gains a nullable `UserId`,
which is a public record change with three call sites in this table.

**3. Does anything actually invoke it?** Every route is mapped in this change, `RequireAuthorization` is
what makes the middleware meaningful, and the audit behaviour reads the new context field on every command.
`ICurrentUserAccessor` has exactly two consumers, both handlers in this spec.

**4. CLI, tool manifest, or unpinned package.** `dotnet ef migrations add` only, already available.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Security` holds
**nothing but a `.csproj`** — and this is the first spec where that is worth a second look, because a
current-user accessor is exactly the kind of thing that tree was made for. It stays out: the accessor names
`sid`, which is this application's session model, not a domain-agnostic concept. `Core.Application` holds
only `ValidationBehaviour`; the other two hold nothing.

**What the walk caught.** Three things.

- **`AuthEndpoints.cs` is at 299 lines**, one under the 300 the root `.editorconfig` allows. Any new route
  in that file fails the build with `S104`, which is why `SessionEndpoints.cs` is its own file from the
  start rather than after a heal cycle.
- **`UseAuthentication` must sit after `UseRateLimiter` and before the client-context middleware.** The
  limiter must still refuse a flood before the server verifies a signature, and the client-context
  middleware must see the principal so it can put `sub` into the audit record.
- **The client context on a refresh-token family is written by `SessionIssuer`**, which today receives no
  client context. It gains the accessor already declared in `Application` — the same one the audit
  behaviour reads — so no new plumbing crosses a boundary.

## Steps

Written against the recommendations. **Re-walk against the four decisions before starting.**

1. `JwtBearerConfiguration`: validate signature, issuer, audience and lifetime, require `exp`, set the
   skew from escalation 3, and map nothing else. `Program.cs` wires it, then `UseAuthentication()` and
   `UseAuthorization()`.
2. `sid`: `IAccessTokenIssuer.Issue(userId, familyId, nowUtc)`, emitted as a claim. `SessionIssuer` and
   `RotateSessionCommandHandler` both pass the family they are issuing for — rotation keeps the family, so
   the claim is stable across a rotation, which is what makes "current" meaningful.
3. The three new columns on `refresh_tokens`, written once when the family starts. A rotated row copies
   them, for the same reason it inherits the expiry: it is the same session.
4. `ICurrentUserAccessor` over the principal: `UserId` from `sub`, `SessionId` from `sid`, both null when
   unauthenticated. A handler behind `RequireAuthorization` may treat them as present and must still fail
   loudly rather than silently if they are not.
5. `EndSessionCommand`: revoke the family named by `sid`, or the one named in the route, and answer
   `404` when it is not this caller's — never `403`, per the contract's own rule.
6. `ListSessionsQuery`: the caller's unrevoked, unexpired families, newest first, with the current one
   flagged. A query, not a command, and not auditable: reading your own list is not a mutating action.
7. The audit behaviour takes `user_id` from the context when the handler set none, so every authenticated
   command names its caller.
8. Tests, then the migration, then regenerate `docs/api/openapi.json`.

## Verification

```bash
make agent-check
```

That one target is the whole gate: `dotnet restore`, `dotnet format --verify-no-changes`,
`dotnet build -warnaserror --no-restore`, `dotnet test --no-build`,
`dotnet list package --vulnerable --include-transitive`, `make openapi` with
`git diff --exit-code -- docs/api/openapi.json`, then `./docs/build-pdf.sh` with
`git diff --exit-code -- '*.pdf'`. Docker and pandoc required; a missing tool is an environment result.

| Check | Proves |
|---|---|
| `Sessions_WithoutABearerToken_Returns401` | the gate exists |
| `Sessions_WithAMalformedToken_Returns401` | a garbage token is refused, not a `500` |
| `Sessions_WithATokenSignedByAnotherKey_Returns401` | the signature is verified against the configured key |
| `Sessions_WithAWrongAudienceToken_Returns401` | `aud` is checked, which RFC 9700 asks for |
| `Sessions_WithAnExpiredToken_Returns401` | the lifetime is enforced, at the skew escalation 3 chose |
| `Logout_EndsTheCurrentSessionOnly` | escalation 1 — the other session still rotates |
| `Logout_ThenRotatingTheEndedSession_Returns401` | logout is server-side, not a client discarding tokens |
| `ListSessions_ReturnsTheCallersOwnSessionsWithTheCurrentOneFlagged` | the endpoint is usable by a human |
| `ListSessions_DoesNotReturnAnotherAccountsSessions` | the first `self` gate in the codebase |
| `DeleteSession_ForAnotherAccountsSession_Returns404` | `404` not `403`, per `docs/api/ENDPOINTS.md` |
| `ListSessions_ReturnsNoTokenOrDigest` | §13.5 — a session list must not carry the credential it describes |
| `AuditRecord_ForAnAuthenticatedCommand_NamesTheAccount` | escalation 4 |
| `JwtAccessTokenIssuerTests` (extended) | `sid` present, and still no address, name or role in the payload |
| every existing suite | the seven anonymous endpoints answer exactly as before: adding authentication must not change an anonymous path |

## Done when

- [x] every file in the Files table exists and compiles, with the three deltas above
- [x] every command under Verification passes — `make agent-check` exited 0 on 2026-09-28, 159 tests
- [x] OWASP checklists walked (A01, A02, A04, A07, A09, API1, API2, API5). Nothing new is unfixed; T-28 is
      restated rather than left approximate
- [x] **T-28 restated**: the window is 15 minutes 30 seconds, it now covers logout as well as reset, and the
      handler's five-minute default is named as what would have made it 20 minutes
- [x] `docs/SDD.md` §13.1 lists `sid` and the skew; §16 V.1 says the API can tell who is calling and V.6
      "own data only" moves to partly implemented with the first `self` gate
- [x] `docs/api/ENDPOINTS.md` says a session is a refresh-token family, what the list contains, what
      "current" means, and that an access token outlives a logout
- [x] `brain/migrations/migrations-index.md` carries the `RefreshTokenClientContext` row
- [x] `docs/api/openapi.json` regenerated — three routes, their `401` and the `404` — and the PDFs rebuilt
- [x] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| Ownership and licence behaviours | S3 onward, when an endpoint acts on something that may not be the caller's |
| An access-token denylist | T-28; needs a decision about reading state on every request |
| "Sign out everywhere" as its own endpoint | not in the contract; add a row first |
| Rehash on login below the configured cost | still the follow-up SPEC-005 recorded |
