---
id: SPEC-008
title: Audit every mutating action — required client headers, one record per command, a queryable table
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S2              # which DELIVERY-PLAN slice this belongs to
requirements: [V.6]
decisions: [ADR-0009, ADR-0011, ADR-0012, ADR-0019, ADR-0022, ADR-0028]
standards: [authentication-authorization, application-security, logging-observability, clean-architecture-cqrs, ef-core-persistence, dotnet-csharp, testing]
approved_by: enesozmert # repository owner, 2026-09-27, with the decisions below
escalation: |
  Gates 1, 4 and 5 answer "yes". Four decisions belong to the repository owner before this spec may be
  implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) what happens when the audit write itself fails — fail closed and refuse the request, or
  fail open and keep the effect; (2) whether the audit record is written inside the command's own
  transaction or after it commits, which is the same question about evidence durability from the other
  side; (3) the scope of the header requirement and of the audit records, given that **every mutating
  endpoint that exists today is anonymous** — so `userId` is null on every record this spec can write;
  (4) whether the SignalR hub filter ADR-0019 requires is built now, against no hub, or with the first
  hub in a later slice.
---

# Audit every mutating action

## Goal

After this spec, every mutating request carries `X-Client-Platform`, `X-Client-Device` and
`X-Client-App-Version` or is refused with `400`, and every handled command writes exactly one row to a
queryable `audit_records` table holding the fields `docs/SDD.md` §13.4 lists: `userId`, `action`,
`targetType`, `targetId`, `platform`, `device`, `appVersion`, `occurredAtUtc`, `correlationId` and the
outcome. Subject V.6 requires a backend log line with platform, device and app version for every client
action; this is the slice that makes that true rather than designed.

It lands early on purpose — `docs/DELIVERY-PLAN.md` puts it at S2 because the audit path touches every
mutating endpoint, and added late it means revisiting every controller and every handler.

## Out of scope

- **Reading the audit log.** No endpoint exposes it: `docs/api/ENDPOINTS.md` specifies none, and an
  audit trail readable over the API is an authorisation surface of its own. `psql` is the query tool
  until a spec says otherwise.
- **Retention and archival.** §13.4 says the audit log is kept and the application log is rotated.
  Nothing here deletes a row, and the `BackgroundService` of ADR-0024 must never be pointed at this
  table.
- **Authorisation auditing.** A denied request is an audit-worthy event, and the ownership and licence
  gates that produce denials are S4 and later. The behaviour written here records what it can see today.
- **The security events T-27 still lists.** A failed login and a refresh-token family revocation are
  domain events inside handlers, not commands in the pipeline. They attach to this table once the
  behaviour exists, in their own small spec — that is what narrows T-27 to nothing.
- **Correlation across services.** One service, one correlation id, taken from the trace context
  ADR-0022 already configures. No propagation format is chosen here.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — escalations 1 and 2. "The audit log is evidence" (§13.4) does not say whether evidence outranks availability, and nothing in the repository states whether a record shares the command's transaction. Both answers are inherited by every later slice, so they belong in an ADR. **ADR-0021 still owes two Consequences lines** (SPEC-005's JwtBearer, SPEC-006's in-process backoff), and SPEC-007's one-repository-two-aggregates decision still wants its own ADR |
| 2 | new or upgraded dependency? | no — `Microsoft.AspNetCore.Http.Abstractions` is in the framework; `IHttpContextAccessor` needs `AddHttpContextAccessor`, not a package |
| 3 | schema change beyond this spec, or destructive migration? | no — one new table, `audit_records`, defined in this spec; additive, no column dropped, no data moved |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes, by deletion** — line 34 currently reads "**Not enforced yet**: the middleware that validates them arrives with the audit log in slice S2, and until then a mutation without them succeeds." This spec makes that note false. No status code and no field changes; the row already lists `400` |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it *is* the audit log, it reads a client-supplied header on every mutation, and §13.5's never-logged list applies to every field it writes |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — decided by the owner, 2026-09-27

| # | Decision |
|---|---|
| 1 | **Fail closed.** A command whose audit record cannot be written does not stand. |
| 2 | **Inside the command's transaction** — chosen on the tie-break below, not on the first answer. Effect and record commit together, so neither exists without the other. |
| 3 | **Option A** — headers required and records written on every mutating request, anonymous included. `userId` is null on every row until the authenticated slices land, and §16 says so rather than hiding it. |
| 4 | **With the first hub.** S2 covers REST; ADR-0019's hub filter arrives with the hub that would invoke it. |

**Tie-break, same day.** The first pair of answers was "fail closed" plus "after the handler returns",
which this spec had already called incoherent: the mutation would commit, the audit insert would fail,
and the caller would get `500` for an effect that stands. Put to the owner as three options, the answer
was **fail closed with the record inside the transaction** — the reading that keeps decision 1 literally
true. That is the expensive option, and it is the one being built:

- it needs the unit-of-work mechanism SPEC-007 escalation 1 deliberately deferred, so this spec grows an
  `IUnitOfWork` and a transaction-owning behaviour;
- the four existing repository methods that open their own transaction must **enlist** in an ambient one
  instead of beginning a second, because EF Core refuses a nested `BeginTransactionAsync`. Those two
  files are in the discovered list below;
- SPEC-007's note that "the next multi-aggregate write reconsiders an explicit unit of work" is now
  answered: this is that write, and the answer is yes.

**Known limit of what the outcome column can say today.** The behaviour writes a record when the handler
returns without throwing; a command refused by validation writes none, and a command that throws rolls
back with its record. So `outcome` is constant for now, and "login refused" versus "login accepted" is a
*handler-level* event — the T-27 follow-up spec, not this one. `targetId` is null for all seven existing
commands for the same reason: the account id is resolved inside the handler, and no command can name it.

## Escalation — what a human must decide

### 1. What happens when the audit write fails (gate 1)

`docs/SDD.md` §13.4 says the audit log is evidence and subject V.6 grades its existence. It does not
say what the API answers when the record cannot be written — a database hiccup, a constraint violation,
a full disk.

| Option | Behaviour | Cost |
|---|---|---|
| Fail closed | the request answers `500` and the effect is rolled back if it can be | no action goes unrecorded, which is what "evidence" means. Against it: an audit-table problem becomes a total outage, and a defence demo dies on a table nobody was looking at |
| Fail open | the effect stands, the failure is logged at error level and a counter is raised | the service keeps working. Against it: an action happened with no record, which is precisely the gap V.6 asks about, and nothing forces anyone to notice |

**Recommendation, not a decision:** fail closed for **mutating commands**, because a mutation whose
record is missing is indistinguishable from one that never happened, and it is one sentence to defend.
Whichever is chosen, the threat register carries the residual.

### 2. Is the record inside the command's transaction (gate 1)

The same question from the durability side. A pipeline behaviour wraps the handler, so by default the
handler's transaction has already committed when the behaviour writes.

- **Inside the command's transaction** — the effect and its record commit together, so neither exists
  without the other. It needs the transaction boundary to be visible to the behaviour, which is the
  unit-of-work question SPEC-007 deferred: option A there put a transaction inside one repository
  method, invisible from outside.
- **After the handler returns** — trivially implementable today, and a crash in the gap loses the
  record for an effect that stands. With "fail closed" this also means a `500` after a successful
  mutation, which is the worst answer of the four combinations to explain.

The two escalations interact, which is why they are one ADR: `fail closed` + `after the handler` is
incoherent, and `inside the transaction` needs a mechanism this codebase has deliberately not built yet.

### 3. Scope: every mutating endpoint today is anonymous (gate 5)

This is the awkward one. The endpoints that exist are registration, email verification, verification
resend, login, refresh rotation, password reset request and password reset. **Not one of them is
authenticated**, because the bearer middleware lands with the first authenticated endpoint, which is
logout. So:

- every record this spec can write has `userId = null`;
- requiring the three headers means requiring them from an anonymous caller who has not signed in yet,
  including on the login request itself.

Three ways:

| Option | Scope | Cost |
|---|---|---|
| A | headers required and records written on **every** mutating request, anonymous included | §10.7 says "every mutating request", and a credential endpoint is exactly where a forensic trail matters. `userId` is nullable and the record identifies the actor by `platform`/`device`/`correlationId` instead. Against it: `userId` is null on 100% of rows until the authenticated slices land, so the column looks broken at defence and needs explaining |
| B | headers required everywhere, records written only where a `userId` exists | the table stays meaningful. Against it: today that is **zero** endpoints, so the behaviour ships proving nothing — the dead-registration smell every spec since SPEC-005 has avoided |
| C | headers and records on every mutating request, **except** the credential endpoints | keeps enumeration surface and header friction off the anonymous flows. Against it: it exempts the endpoints an attacker actually touches, and it contradicts §10.7 |

**Recommendation, not a decision:** A, with the null explained in `docs/SDD.md` §16 rather than hidden,
and one test that asserts a record is written for an anonymous mutation. It is the only option where
the code is exercised by something real today.

### 4. The hub filter, with no hub (gate 1)

ADR-0019 is accepted and says hub actions carry the same record through a hub filter, with client
context captured at the handshake. **No hub exists** — SignalR arrives with the realtime slices.

- **Build it now**, against no hub: it is a registration nothing invokes, which is the exact smell
  SPEC-005's Out of scope named and refused.
- **Build it with the first hub**, and record here that S2 covered REST only. `docs/SDD.md` §16's row
  must then say "REST implemented, hub designed" rather than claiming both.

**Recommendation, not a decision:** the second. The delivery plan's reason for S2-early is that every
*later* slice inherits the behaviour; a hub filter written now would be inherited by nothing.

## Files

### Discovered during implementation, 2026-09-27

Four files beyond the table, all forced by the tie-break decision (record inside the transaction), plus
two visibility changes. No design decision was taken here. **The owner accepts or rejects this delta at
review.**

| Path | Action | Why |
|---|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUnitOfWork.cs` | create | the transaction boundary the behaviour needs, which SPEC-007 escalation 1 deferred. `BeginAsync` returns whether *this* caller owns the transaction |
| `MusicRoom/src/MusicRoom/Persistence/UnitOfWork.cs` | create | the EF Core implementation, ambient-aware |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change | its four transactional methods opened their own transaction; EF Core refuses a nested `BeginTransactionAsync`, so they now enlist and commit only what they opened |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/RefreshTokenRepository.cs` | change | same, for `TryRotateAsync` |
| `AuditBehaviour<,>` and `ClientContextMiddleware` | visibility | both are `public` rather than `internal`, matching `ValidationBehaviour<,>`: the test projects assert the behaviour's fail-closed path and read the header names from one place instead of repeating string literals in three suites |

Two Steps were implemented differently, both noted in the escalation section above: `outcome` is a
constant and `targetId` is null for all seven commands, because refusal outcomes and resolved account ids
are handler-level facts the pipeline cannot see. The T-27 follow-up spec is where they land.

One row of the table above needed **nothing**: `HealthEndpointTests` boots the host but calls no mutating
endpoint, so the header rule never applies to it.


Exact paths, created or changed. An unattended run must not touch anything outside this list.
The table assumes the recommendations; a different decision changes it, and it is re-walked against the
answers before implementation starts.

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/AuditRecord.cs` | create — the §13.4 fields, `UserId` nullable |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/IClientContextAccessor.cs` | create — what the pipeline may learn about the caller's client, plus the `ClientContext` record beside it |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/IAuditSink.cs` | create — one member, `WriteAsync(AuditRecord, CancellationToken)`; `Application` declares it, `Persistence` implements it (ADR-0028) |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/IAuditableCommand.cs` | create — the opt-in a command implements to name its `action`, `targetType` and `targetId`. A marker beats reflection over command type names: a renamed class must not silently rewrite history |
| `MusicRoom/src/MusicRoom/Application/Pipelines/Auditing/AuditBehaviour.cs` | create — the `IPipelineBehavior<,>` that writes one record per handled `IAuditableCommand` |
| `MusicRoom/src/MusicRoom/Application/ApplicationServiceRegistration.cs` | change — add the open behaviour after `ValidationBehaviour<,>`, so a command rejected by validation writes no record |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommand.cs` | change — implement `IAuditableCommand` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailCommand.cs` | change — same |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestEmailVerification/RequestEmailVerificationCommand.cs` | change — same |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommand.cs` | change — same |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RotateSession/RotateSessionCommand.cs` | change — same |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestPasswordReset/RequestPasswordResetCommand.cs` | change — same |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommand.cs` | change — same. **No command's `targetId` may be an address or a token**: the identifier is the account id where one is known and null otherwise (§13.5) |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/AuditRecordConfiguration.cs` | create — table `audit_records`, snake_case columns, `ix_audit_records_user_id`, `ix_audit_records_occurred_at_utc`, `timestamp with time zone`, **no** foreign key to `users` (a record must outlive the account it describes) |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — add the `DbSet` |
| `MusicRoom/src/MusicRoom/Persistence/Auditing/AuditSink.cs` | create — the EF Core implementation |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — register the sink |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_AuditRecords.cs` | create — `dotnet ef migrations add` output, reformatted by hand for the analyzers as the three existing migrations were |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_AuditRecords.Designer.cs` | create — same command |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/MusicRoomDbContextModelSnapshot.cs` | change — same command |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/ClientContextMiddleware.cs` | create — reads and bounds the three headers, `400` on a mutating request without them, attaches the values to the request scope |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/HttpClientContextAccessor.cs` | create — the `IClientContextAccessor` the pipeline resolves, over `IHttpContextAccessor` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — `AddHttpContextAccessor`, register the accessor, and place the middleware **after** `UseRateLimiter` so a limited request is refused before anything reads its headers |
| `MusicRoom/tests/MusicRoom.Application.Tests/Pipelines/AuditBehaviourTests.cs` | create — one record per handled command, the fields it copies, and what it does when the sink throws (escalation 1) |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ClientContextEndpointTests.cs` | create — the `400`, the happy path, the bounds, and that `GET /health` is unaffected |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/AuditRecordEndpointTests.cs` | create — a mutation writes exactly one row with the header values, asserted against the database |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — every mutating call in the suite needs the three headers; the helper that adds them lives here beside `TestJwtSettings` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SessionRotationEndpointTests.cs` | change — same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | change — same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EmailVerificationEndpointTests.cs` | change — same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/VerificationRequestEndpointTests.cs` | change — same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordResetEndpointTests.cs` | change — same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/CredentialRateLimitingTests.cs` | change — same, and its per-IP tests must keep tripping the limiter rather than the new `400` |
| `docs/api/ENDPOINTS.md` | change — delete the "Not enforced yet" note on line 34 and state what the middleware does |
| `docs/api/openapi.json` | change — regenerated by `make openapi`; every mutating route gains `400` |
| `docs/SDD.md` | change — §16 V.6 "log platform, device, app version per action" row: REST implemented, hub still designed (escalation 4) |
| `docs/owasp/THREAT-REGISTER.md` | change — **T-27 narrowed to the handler-level security events only**, plus the residual from escalations 1 and 2 |
| `brain/migrations/migrations-index.md` | change — the new migration row |
| `docs/SDD.pdf`, `docs/api/ENDPOINTS.pdf`, `docs/owasp/THREAT-REGISTER.pdf` | change — regenerated by `./docs/build-pdf.sh`, never hand-edited; `make agent-check` diffs them |
| `docs/specs/SPEC-008-audit-and-client-context.md` | change — status transitions |

### The Files table walk

The five checks `docs/prompt-library/PROMPTS.md` §2.3 requires, answered against the tree as it is
today.

**1. Assembly membership and every reference it implies.** `AuditRecord` goes in `Domain`, which
references nothing. The three abstractions and the behaviour go in `Application`; its `MediatR`
reference already exists, and `IPipelineBehavior<,>` comes from there. The sink goes in `Persistence`,
which already references `Application` and `Domain`. The middleware and the accessor go in `WebAPI`,
which already references all three. **No new project and no `.csproj` change**, and every reference
points inward, matching `docs/SDD.md` §7.1.

`WebAPI` implementing an `Application` abstraction is new for this repository — `IPasswordHasher`,
`IMailService`, `IAccessTokenIssuer` and `ICredentialAttemptLimiter` are all `Infrastructure`. It is
deliberate and it is not an outward reference: `HttpContext` is an ASP.NET concept, and moving the
accessor to `Infrastructure` would make that project reference the web stack to read three strings.

**2. Types that cross an assembly boundary.** `AuditRecord`, `IAuditSink`, `IClientContextAccessor`,
`ClientContext` and `IAuditableCommand` all cross one, so each is `public`. `ClientContext` shares
`IClientContextAccessor.cs` and the marker gets its own file, because seven commands implement it and a
reader will look for it by name. `AuditBehaviour<,>` is `internal sealed` with a public constructor,
registered as an open generic exactly as `ValidationBehaviour<,>` is. The middleware and the accessor
are `internal` to `WebAPI`.

**3. Does anything actually invoke it?** The behaviour is registered as an open behaviour, so it runs
for every command; seven existing commands opt in through the marker in this same change, and
`AuditRecordEndpointTests` proves a row lands. The middleware is placed in the pipeline in this change.
The one thing with no caller is the hub filter, which is why escalation 4 exists and why it is not in
the table.

**4. CLI, tool manifest, or unpinned package.** `dotnet ef migrations add` only, already available:
`Microsoft.EntityFrameworkCore.Design` is pinned with `PrivateAssets="all"` on `Persistence` and
`WebAPI`, and SPEC-007 produced `20260927193149_PasswordResetTokens` this way. `IHttpContextAccessor`
needs `builder.Services.AddHttpContextAccessor()` and no package.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Application` holds
exactly one file, `Pipelines/Validation/ValidationBehaviour.cs` — so the *pattern* for a behaviour lives
there, and the audit behaviour deliberately does **not**: it names `ClientContext` and `IAuditSink`,
which are this application's concepts, not domain-agnostic ones. `Core.Security`,
`Core.CrossCuttingConcerns` and `Core.Persistence` hold nothing but a `.csproj`, so there is no audit
type, clock or context holder to reuse.

**What the walk caught, and it is the expensive one.** Requiring the headers breaks **every** existing
WebAPI test suite — seven files, every mutating call in each. That is not incidental: those suites are
the evidence for four `docs/SDD.md` §16 rows, including three parallel concurrency tests, so the change
must be a header helper applied to every request, never a relaxation of the rule under test. The rule
itself is also what `CredentialRateLimitingTests` must not trip first: a request missing headers is a
`400`, and that suite asserts `429`, so its requests carry headers and the limiter still fires.

Second, smaller: `Program.cs` middleware order is load-bearing. `UseRateLimiter` must stay first, or an
unlimited flood of header-less requests would each get a `400` — cheap, but still a request the limiter
was supposed to refuse.

## Steps

Written against the recommendations. **Re-walk against the four decisions before starting.**

1. `AuditRecord` in `Domain`: `UserId` (nullable), `Action`, `TargetType`, `TargetId` (nullable),
   `Platform`, `Device`, `AppVersion`, `OccurredAtUtc`, `CorrelationId`, `Outcome`. Strings are bounded
   at the column, because every one of them except the outcome arrives from a client.
2. `ClientContext` and `IClientContextAccessor`, then `ClientContextMiddleware`: read the three headers,
   reject a mutating request that lacks any of them with a bodiless `400`, and bound each value —
   64 characters is the column and the limit. A `GET` is never rejected; `/health` and `/health/ready`
   are never rejected, because a probe is not a client action.
3. `HttpClientContextAccessor` over `IHttpContextAccessor`, registered in `Program.cs` together with
   `AddHttpContextAccessor()`. The middleware goes after `UseRateLimiter` and before `MapAuthEndpoints`.
4. `IAuditableCommand`: `AuditAction`, `AuditTargetType`, and a method that returns the target id once
   the handler knows it — for registration that is the new account, which does not exist when the
   command is created. Where the id cannot be known, null, and the record still names the action.
5. `IAuditSink` and `AuditSink`: one insert, no transaction of its own (escalation 2 decides whether it
   joins one).
6. `AuditBehaviour<,>`: after validation in the registration order, so a `422` writes nothing. It reads
   the client context, calls the handler, then writes one record carrying the outcome. Escalation 1
   decides what happens when the write throws.
7. Opt the seven existing commands in. Names are stable strings — `"auth.register"`,
   `"auth.verify-email"`, `"auth.request-verification"`, `"auth.login"`, `"auth.rotate-session"`,
   `"auth.request-password-reset"`, `"auth.reset-password"` — not `nameof`, so a class rename cannot
   rewrite history.
8. The header helper in the test suites, then the new tests, then the migration, then regenerate
   `docs/api/openapi.json`.

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
| `Mutation_WithoutClientHeaders_Returns400` | §10.7 step 2 — the requirement is enforced, not documented |
| `Mutation_WithOneHeaderMissing_Returns400` | all three are required, not any one of them |
| `Mutation_WithAnOverlongHeaderValue_Returns400` | a client-supplied string cannot make the service store a megabyte |
| `Health_WithoutClientHeaders_Returns200` | a probe is not a client action |
| `Get_WithoutClientHeaders_IsUnaffected` | the rule is about mutations |
| `Mutation_WithHeaders_WritesExactlyOneAuditRecord` | V.6 — one row per action, asserted against the database |
| `AuditRecord_CarriesThePlatformDeviceAndAppVersion` | V.6 names those three fields specifically |
| `AuditRecord_ForAnAnonymousMutation_HasNoUserIdAndStillNamesTheAction` | escalation 3 option A, made explicit rather than surprising |
| `AuditRecord_ForARejectedCommand_IsNotWritten` | behaviour order: validation refuses before the record exists |
| `AuditRecord_CarriesNoAddressTokenOrPassword` | §13.5 — the never-logged list applies to this table too |
| `AuditBehaviourTests` — one write per command, the copied fields, and the sink-failure path | escalation 1's decision, asserted rather than assumed |
| `CredentialRateLimitingTests` (existing) | a limited request still answers `429`, not `400`: middleware order holds |
| `LoginEndpointTests`, `SessionRotationEndpointTests`, `RegistrationEndpointTests`, `EmailVerificationEndpointTests`, `VerificationRequestEndpointTests`, `PasswordResetEndpointTests` (existing) | all four §16 rows they back still hold, including the three parallel concurrency tests |
| `SchemaConventionTests` (existing, unchanged) | `audit_records` obeys the naming conventions |

## Done when

- [x] every file in the Files table exists and compiles, plus the four discovered above
- [x] every command under Verification passes — `make agent-check` exited 0 on 2026-09-27, 128 tests
- [x] OWASP checklists walked for the surface touched (A01, A04, A09, API2, API4, API8)
- [x] **T-27 narrowed** again — actions are now recorded for every handled command; what remains is the
      refusal *outcome* and the handler-level family revocation, with `AuditRecordEndpointTests` and
      `AuditBehaviourTests` as the evidence for what is covered
- [x] the residual is written down as **T-29**: fail closed means a fault confined to the audit table
      stops every mutating endpoint, accepted knowingly. A crash between effect and record is no longer
      possible, which is what the tie-break bought
- [x] `docs/api/ENDPOINTS.md` now says the headers are enforced, and names the `400`
- [x] `docs/SDD.md` §16 V.6 row says REST implemented and hub designed
- [x] `brain/migrations/migrations-index.md` carries the `audit_records` row, marked one-way in any
      environment that has served a real request
- [x] `docs/api/openapi.json` regenerated — every mutating route now documents its `400` — and
      `docs/SDD.pdf`, `docs/api/ENDPOINTS.pdf` plus `docs/owasp/THREAT-REGISTER.pdf` rebuilt by
      `./docs/build-pdf.sh`, so both drift guards pass
- [x] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| The SignalR hub filter ADR-0019 requires | escalation 4 — with the first hub |
| Failed login, token reuse and family revocation as audit rows | their own small spec, once this behaviour exists; it is what closes T-27 |
| Authorisation denials as audit rows | with the ownership and licence gates, S4 onward |
| An endpoint to read the audit log | none specified; it is an authorisation surface of its own |
