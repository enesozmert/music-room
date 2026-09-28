---
id: SPEC-009
title: Record why an action was refused — outcomes, the acting account, and the security events T-27 still lists
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S2              # which DELIVERY-PLAN slice this belongs to
requirements: [V.6]
decisions: [ADR-0009, ADR-0011, ADR-0012, ADR-0019, ADR-0022, ADR-0028]
standards: [authentication-authorization, application-security, logging-observability, clean-architecture-cqrs, ef-core-persistence, dotnet-csharp, testing]
approved_by: enesozmert # repository owner, 2026-09-28, with the four decisions below
escalation: |
  Gates 1 and 5 answer "yes". Four decisions belong to the repository owner before this spec may be
  implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) how a handler tells the pipeline what happened, since the outcome is known inside the
  handler and the record is written outside it; (2) what to do about a refusal that throws — the
  per-account throttle raises `CredentialAttemptThrottledException`, and SPEC-008's fail-closed
  behaviour rolls its record back with it, so the loudest signal in the system currently leaves no row;
  (3) the rate-limit trip, which happens in middleware before the client headers are read, so a record
  for it has no platform, device or app version to carry; (4) whether a refused attempt may write a row
  per attempt, which makes `audit_records` the write-amplification target the rate limiter bounds.
---

# Record why an action was refused

## Goal

After this spec, the audit log answers "what happened" and not only "something happened".
A failed login, a rejected refresh token, a detected refresh-token **family revocation** and a spent or
expired reset token each leave a row whose `outcome` says so; a successful action names the account it
acted for, so `user_id` stops being null on every row. That is what closes **T-27**, open since SPEC-005
and narrowed twice, and it is the last piece of the A09 event list in `docs/owasp/TOP10-CHECKLIST.md`
that this codebase can satisfy before authorisation exists.

## Out of scope

- **Authorisation denials.** The ownership and licence gates are S4 onward; their denials attach to the
  same `outcome` vocabulary once they exist.
- **The SignalR hub filter.** Still ADR-0019's, still waiting for a hub — SPEC-008 escalation 4.
- **Reading or alerting on the log.** No endpoint, no dashboard, no threshold. A row that exists can be
  queried with `psql`; turning rows into alerts needs a decision about where alerts go.
- **Retention.** Nothing here deletes a row, and the ADR-0024 sweep must still never be pointed at this
  table.
- **Sampling.** If escalation 4 says a refusal writes one row per attempt, it writes one row per
  attempt. Any sampling scheme is its own decision, and a sampled audit log is a weaker claim at defence.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — escalation 2 reopens SPEC-008's fail-closed decision at its edge: a refusal that throws must leave a record, and SPEC-008's behaviour rolls one back. Whatever resolves that is an amendment to the audit ADR the owner still owes, and **the four ADR lines listed in `brain/projects/musicroom-backend.md` are still unwritten** |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **no new table, but one column changes**: `audit_records.outcome` gains values, which is data and not schema. If escalation 1 chooses a typed outcome the column stays `varchar(64)`; nothing is dropped, narrowed or moved |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — no route, status code or field changes. A refused request answers exactly as it does today |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the audit log, and it writes a row for every failed credential attempt. §13.5's never-logged list is the constraint that matters: a refusal row must name the action and the account id, never the address, the token or the digest |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — decided by the owner, 2026-09-28

| # | Decision |
|---|---|
| 1 | **Option A** — a scoped `IAuditContext` the handler writes and the behaviour reads. It is the only mechanism that covers a `null` answer, a `Task` and a value type at once, and it carries `user_id` and `target_id` too. |
| 2 | **Option B** — throttling stops being an exception. `CredentialAttemptThrottledException` is deleted; the limiter's refusal becomes a value the handlers return and the endpoints map. Observable behaviour must not change by one byte. |
| 3 | **Serilog only** for the per-IP rate-limit trip. The application log is the right place for something that never reached the application, and every `audit_records` row keeps carrying the three fields subject V.6 names. |
| 4 | **Accept one row per refused attempt**, with the volume bound — 5 per account per 5 minutes, 20 per IP per 5 minutes — stated in the threat register. |

## Escalation — what a human must decide

### 1. How a handler tells the pipeline what happened (gate 1)

The outcome is known inside the handler — `LoginCommandHandler` returns null for a refusal,
`RotateSessionCommandHandler` sees `RefreshTokenRotationStatus.Reused`, `TryResetPasswordAsync` returns
`ResetPasswordOutcome.Expired`. The record is built outside, in `AuditBehaviour`. Something has to carry
the fact across.

| Option | Mechanism | Cost |
|---|---|---|
| A | a scoped, mutable `IAuditContext` the handler writes to and the behaviour reads | one small abstraction, works for every handler and every response shape, and it also carries `user_id` and `target_id`. Against it: mutable request-scoped state, which is exactly the kind of thing that is hard to reason about when two commands share a scope |
| B | typed responses — the behaviour inspects the response for a known interface such as `IAuditableOutcome` | no mutable state, and the outcome travels with the value it describes. Against it: three of the seven commands answer `Task` or a nullable record, so it means changing response types the endpoints already map, and a `null` answer cannot implement an interface |
| C | MediatR notifications — the handler publishes an event, a handler for it writes the row | the pattern scales to the handler-level events that are not commands at all. Against it: the notification is published inside the transaction and handled synchronously or not at all, so the atomicity SPEC-008 bought has to be re-established by hand |

**Recommendation, not a decision:** A. It is the only one of the three that covers a `null` answer, a
`Task`, and a value type with one mechanism, and the mutable state is confined to one request scope with
one writer.

### 2. The refusal that throws (gate 1)

`ICredentialAttemptLimiter` refusals raise `CredentialAttemptThrottledException` from inside the handler.
`AuditBehaviour` catches nothing: it rolls the transaction back and rethrows, so **the row that would
record a throttled credential attempt is rolled back with it**. Today that is invisible in the table, and
it is the single most interesting row the audit log could hold.

| Option | Mechanism | Cost |
|---|---|---|
| A | the behaviour catches the throttle, writes a `throttled` row, commits, and rethrows | the record lands and the caller still gets `429`. It means committing a transaction on a path that also throws, so the behaviour has two commit sites and the "fail closed" story gains an exception |
| B | throttling stops being an exception: the limiter's refusal becomes a normal outcome the handlers return, and the endpoints map it | no exception on a control-flow path, and the record is written on the ordinary success path of the behaviour. It changes three handlers and both endpoint classes that SPEC-006 and SPEC-007 just wrote |
| C | a second write on its own transaction, after the rollback | leaves the effect rolled back and the record written, with no shared transaction. Against it: a second connection per refused request, and it reintroduces exactly the gap SPEC-008's tie-break closed — a crash between the two loses the row |

**Recommendation, not a decision:** B. An exception for a refusal the system expects is the thing that
made this hard in the first place, and the `429` mapping moves from a `catch` to a `switch`.

### 3. The rate-limit trip (gate 5)

The per-IP limiter refuses in middleware, **before** `ClientContextMiddleware` runs — SPEC-006 put
`UseRateLimiter` first deliberately, so a flood is refused before anything reads its headers. A row for
that trip therefore has no platform, no device and no app version, and those columns are `NOT NULL`.

- **Stay Serilog-only.** SPEC-006 already logs the trip with the endpoint and the wait. T-27's row can
  close naming that as the evidence, and the audit table keeps its promise that every row carries the
  three fields subject V.6 names.
- **Write a row with placeholders** — `platform = "unknown"`. It makes the trip queryable in the same
  place as everything else, and it puts a lie in three columns.
- **Swap the middleware order** so the headers are parsed first. Then a header-less flood costs a `400`
  instead of a `429`, which is the outcome SPEC-006 explicitly avoided, and its test asserts otherwise.

**Recommendation, not a decision:** the first. The application log is the right place for something that
never reached the application.

### 4. One row per refused attempt (gate 5)

With this spec, a password-guessing loop writes one `audit_records` row per attempt until the limiter
stops it: 5 per account per 5 minutes on the per-account counter, 20 per IP per 5 minutes on the per-IP
policy (§13.3). That is bounded, and it is also the audit table becoming the write-amplification target
the limiter exists to bound.

- **Accept it.** The numbers are small, the rows are narrow, and an unrecorded guessing run is worse than
  a large table. The register row says what the volume is bounded by.
- **Record only the first refusal per key per window.** Cheaper, and it turns a count into a guess: "how
  many attempts" stops being answerable, which is the question a forensic reader asks first.

**Recommendation, not a decision:** accept, with the bound stated in the threat register.

## Files

### Discovered during implementation, 2026-09-28

Two files beyond the table, both forced by an analyzer rather than by a design change. No decision was
taken here. **The owner accepts or rejects this delta at review.**

| Path | Action | Why |
|---|---|---|
| `MusicRoom/src/MusicRoom/Application/Features/Auth/SessionIssuer.cs` | create | injecting `IAuditContext` pushed `LoginCommandHandler` to six constructor parameters: `error S107: Constructor has 6 parameters, which is greater than the 5 authorized`. The refresh-token write path and the access-token issuer moved behind one collaborator, which is also where slice S8's social sign-in will issue its pair. `SessionIssuer.RefreshTokenLifetimeDays` now holds the 30-day constant |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/CredentialMailDispatcher.cs` | create | the same limit on `RegisterCommandHandler`. The three handlers that each had their own copy of "send, and swallow a failure so an existing account cannot be told from an unknown address" now share one, and each is back to five parameters or fewer |

Both are registered in `ApplicationServiceRegistration`, which the table already lists as a change.

`docs/api/openapi.json` came back **unchanged**, which is the check that matters most here: deleting
`CredentialAttemptThrottledException` rewired three handlers and three endpoint methods without moving one
byte of the contract.


Exact paths, created or changed. An unattended run must not touch anything outside this list.
The table assumes the recommendations — option A for escalation 1, option B for escalation 2, Serilog-only
for escalation 3, accept for escalation 4. **A different answer changes it, and it is re-walked against
the decisions before implementation starts.**

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/IAuditContext.cs` | create — what a handler contributes: the outcome, the acting account, the target id. Plus the `AuditOutcome` values beside it, as a type rather than loose strings |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/AuditContext.cs` | create — the scoped implementation. In `Application` rather than `Infrastructure` because it holds no external resource: it is request-scoped state, and the layer that defines the contract is the one that can keep it honest |
| `MusicRoom/src/MusicRoom/Application/ApplicationServiceRegistration.cs` | change — register `IAuditContext` scoped |
| `MusicRoom/src/MusicRoom/Application/Pipelines/Auditing/AuditBehaviour.cs` | change — read the context when building the record; default to `completed` when a handler said nothing, so silence is still a valid, true record |
| `MusicRoom/src/MusicRoom/Domain/Entities/AuditRecord.cs` | change — the `Outcome` remark stops saying the value is constant |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/ICredentialAttemptLimiter.cs` | change — **escalation 2 option B**: delete `CredentialAttemptThrottledException`; the decision it carried becomes a value the handlers return |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommandHandler.cs` | change — set `refused` on a failed verification, `throttled` when the limiter refuses, and on success set the outcome plus the account id |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginResult.cs` | create — **escalation 2 option B**: the pair plus the refusal reason the endpoint maps, so a throttle is a value and not an exception |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RotateSession/RotateSessionCommandHandler.cs` | change — `reuse-detected` when the family was revoked, `refused` for unknown, expired and revoked, and the account id on success |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | change — `throttled` as a value, and the new account's id on success. A duplicate address stays `completed`: it is not a refusal, and saying otherwise would make the table an enumeration oracle |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestEmailVerification/RequestEmailVerificationCommandHandler.cs` | change — same shape |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestPasswordReset/RequestPasswordResetCommandHandler.cs` | change — same shape |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandHandler.cs` | change — `refused` for an invalid token, `expired` for a spent window, and the account id on success |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/VerifyEmailCommandHandler.cs` | change — `refused` and `expired` from the existing `VerifyEmailOutcome` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — `TryResetPasswordAsync` must report **which account** it reset, so the record can name it; `FindCredentialByEmailAsync` already answers the id |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IRefreshTokenRepository.cs` | change — `RefreshTokenRotation` already carries `UserId`; it must also carry it on the `Reused` path, so a revocation names the account whose family died |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — return the account id from the reset path |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/RefreshTokenRepository.cs` | change — carry the account id on `Reused` |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — **escalation 2 option B**: map the refusal reason to `401` or `429` in a `switch` instead of a `catch`; `Retry-After` still comes from the limiter's wait |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PasswordResetEndpoints.cs` | change — same |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/CredentialEndpointResults.cs` | change — `Throttled` takes the wait rather than the exception |
| `MusicRoom/tests/MusicRoom.Application.Tests/Pipelines/AuditBehaviourTests.cs` | change — the outcome a handler set reaches the record, and silence still yields `completed` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/AuditRecordEndpointTests.cs` | change — a refused login, a reused refresh token, an expired reset and a successful login each land the right `outcome`, and success names `user_id` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/CredentialRateLimitingTests.cs` | change — a throttled attempt now leaves a `throttled` row, and the response is still `429` with `Retry-After` |
| `docs/SDD.md` | change — §13.4 gains the outcome vocabulary; §16 V.6 audit row says outcomes are recorded and `user_id` is populated on authenticated-by-credential paths |
| `docs/owasp/THREAT-REGISTER.md` | change — **T-27 closes**, with the new test names in its Verified column; T-29's row notes that a refusal now also writes a row, and the volume bound from escalation 4 is stated |
| `docs/SDD.pdf`, `docs/owasp/THREAT-REGISTER.pdf` | change — regenerated by `./docs/build-pdf.sh`, never hand-edited |
| `docs/specs/SPEC-009-audit-refusal-outcomes.md` | change — status transitions |

### The Files table walk

The five checks `docs/prompt-library/PROMPTS.md` §2.3 requires, answered against the tree as it is today.

**1. Assembly membership and every reference it implies.** `IAuditContext` and `AuditContext` go in
`Application`, beside the abstractions SPEC-008 created; the handlers and the behaviour are already there.
The repository changes are `Persistence`, the endpoint changes `WebAPI`. `AuditRecord` is `Domain`.
**No new project, no `.csproj` change**, every reference inward — `docs/SDD.md` §7.1 holds.

`AuditContext` living in `Application` rather than `Infrastructure` is deliberate and is the one
arguable placement: it holds no external resource, only request-scoped state, and the alternative is a
project reference that buys nothing. ADR-0028's rule is about *external* concerns.

**2. Types that cross an assembly boundary.** `IAuditContext`, `AuditContext`, `AuditOutcome` and
`LoginResult` cross one, so each is `public`. `AuditOutcome` is a type and not loose strings, because the
same value is written by seven handlers and asserted by two test suites — a typo in one of nine places
would be a row nobody finds. `CredentialAttemptThrottledException` is **deleted**, which is a public type
removal inside one solution with every caller in this table.

**3. Does anything actually invoke it?** Every handler this table changes writes to the context, and the
behaviour reads it on every command. No member is added that nothing calls. The deleted exception's three
`catch` sites are all in this table.

**4. CLI, tool manifest, or unpinned package.** None. **No migration**: `outcome` is already
`varchar(64)` and this spec only writes different values into it. That is the one thing worth stating
twice, because "the audit log learns a new outcome" sounds like a schema change and is not.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Application` holds one
file, `Pipelines/Validation/ValidationBehaviour.cs`. `Core.Security`, `Core.CrossCuttingConcerns` and
`Core.Persistence` hold nothing but a `.csproj`. There is no context holder or outcome type to reuse, and
nothing here belongs in them: an audit outcome that names a credential refusal is not domain-agnostic.

**What the walk caught.** Deleting `CredentialAttemptThrottledException` is the largest edit in the spec
and it is invisible from the goal: three handlers throw it, three endpoint methods catch it, and
`CredentialEndpointResults.Throttled` takes it as a parameter. Option B in escalation 2 is what makes the
audit path simple; the price is a change to code SPEC-006 and SPEC-007 wrote days ago, and it must not
change one observable byte — `429`, `Retry-After`, empty body, same per-IP and per-account behaviour.
`CredentialRateLimitingTests` is the guard for that, and it must pass **unchanged** except for the new
row assertions.

## Steps

Written against the recommendations. **Re-walk against the four decisions before starting.**

1. `AuditOutcome` and `IAuditContext`, then `AuditContext`, registered scoped. Members: `SetOutcome`,
   `SetUserId`, `SetTargetId`, and read-only properties for the behaviour. Last writer wins, which is
   what a handler that refuses after a partial read needs.
2. `AuditBehaviour` reads the context after the handler returns: outcome, user id and target id, with
   `completed` and null as the defaults. A handler that says nothing still produces a true record.
3. Escalation 2 option B: introduce `LoginResult` (pair plus refusal reason), delete
   `CredentialAttemptThrottledException`, and change the three handlers that threw it to return a value.
   Then the endpoints map the reason: `null` pair with reason `throttled` to `429` plus `Retry-After`,
   everything else to the status it answers today.
4. Every handler sets its outcome: `completed`, `refused`, `throttled`, `expired`, `reuse-detected`.
   Success paths also set the account id — the login handler has it from `UserCredential`, the rotation
   handler from `RefreshTokenRotation`, the reset handler from the value `TryResetPasswordAsync` now
   returns.
5. `RefreshTokenRotation` carries the account id on `Reused` as well, so the row that records a family
   revocation names whose family it was.
6. Registration keeps `completed` for a duplicate address. That is deliberate and it is the one place the
   audit log is **less** informative on purpose: a `refused` row for a duplicate would make the table an
   account-enumeration oracle for anyone who can read it.
7. Tests, then `docs/SDD.md` §13.4 and §16, then close T-27.

## Verification

```bash
make agent-check
```

That one target is the whole gate: `dotnet restore`, `dotnet format --verify-no-changes`,
`dotnet build -warnaserror --no-restore`, `dotnet test --no-build`,
`dotnet list package --vulnerable --include-transitive`, `make openapi` with
`git diff --exit-code -- docs/api/openapi.json`, then `./docs/build-pdf.sh` with
`git diff --exit-code -- '*.pdf'`. It needs Docker and pandoc; a missing tool is an environment result,
not a failure of the code. **`docs/api/openapi.json` must come back unchanged**: this spec changes no
route, no status code and no field.

| Check | Proves |
|---|---|
| `Login_WithWrongPassword_WritesARefusedAuditRecord` | A09 — a failed login is now visible in the table, not only in a log line |
| `Login_WithCorrectPassword_WritesARecordNamingTheAccount` | `user_id` is populated where the code knows it |
| `Login_BeyondThePerAccountLimit_WritesAThrottledAuditRecordAnd429` | escalation 2 — the loudest signal in the system leaves a row, and the response is unchanged |
| `Rotate_WithAlreadyRotatedToken_WritesAReuseDetectedRecordNamingTheAccount` | SDD §13.1 theft detection is auditable, which is what a forensic reader needs first |
| `Rotate_WithAnUnknownToken_WritesARefusedRecord` | a rejected token is recorded without naming an account that may not exist |
| `Reset_WithAnExpiredToken_WritesAnExpiredRecord` | the third outcome value, from the existing enum |
| `Register_ForADuplicateAddress_WritesACompletedRecord` | step 6 — the audit log does not become an enumeration oracle |
| `AuditRecord_ForARefusedAttempt_CarriesNoAddressTokenOrDigest` | §13.5 on the new paths, asserted over every row the suite wrote |
| `AuditBehaviourTests` — the outcome a handler set, and `completed` when it set nothing | escalation 1's mechanism, including the silent default |
| `CredentialRateLimitingTests` (existing, extended) | `429`, `Retry-After` and the empty body are byte-for-byte what SPEC-006 shipped, with the exception gone |
| every other existing suite | the seven endpoints answer exactly as before; this spec adds rows, not behaviour |

## Done when

- [x] every file in the Files table exists and compiles, plus the two discovered above
- [x] every command under Verification passes — `make agent-check` exited 0 on 2026-09-28, 142 tests — and
      `docs/api/openapi.json` came back unchanged
- [x] **T-27 closed**, with `AuditRecordEndpointTests`, `CredentialRateLimitingTests` and
      `AuditBehaviourTests` in its Verified column, and the residual named: the per-IP trip stays
      Serilog-only, authorisation denials arrive with the gates, nothing alerts yet
- [x] T-29's row now says a refusal writes a row too, bounded by the §13.3 limits — 5 per account per 5
      minutes, 20 per IP per 5 minutes
- [x] `docs/SDD.md` §13.4 lists the five outcome values and says where `user_id` is and is not set; §16 V.6
      says outcomes are recorded
- [x] OWASP checklists walked (A01, A04, A07, A09, API2, API4). Nothing new is unfixed: A09's remaining
      gaps are the two the T-27 row names as residual
- [x] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| Authorisation denials as `refused` rows | with the ownership and licence gates, S4 onward |
| The SignalR hub filter | ADR-0019, with the first hub |
| Alerting on a burst of `throttled` rows | needs a decision about where alerts go; the rows are the prerequisite |
| An endpoint to read the audit log | none specified; it is an authorisation surface of its own |
