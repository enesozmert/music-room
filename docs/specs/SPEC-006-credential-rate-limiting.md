---
id: SPEC-006
title: Rate limit the credential endpoints — per IP and per submitted account, 429 with Retry-After
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.6]
decisions: [ADR-0009, ADR-0011, ADR-0012, ADR-0021]
standards: [authentication-authorization, application-security, rest-api-design, dotnet-csharp, testing]
approved_by: enesozmert # repository owner, 2026-09-27, with the four decisions below
escalation: |
  Gates 1, 3 and 5 answer "yes". Four decisions belong to the repository owner before this spec may
  be implemented; they are stated in full under "Escalation — what a human must decide".
  In short: (1) the numeric limits, which no document states; (2) how a per-account partition reads
  the submitted address, since the limiter runs before model binding; (3) whether progressive backoff
  on repeated login failure is in-process state or a table, which decides whether gate 3 needs a
  migration and whether ADR-0021's "state is held in process" line still holds; (4) whether the
  rate-limit trip is logged as a security event here or left to T-27 and slice S2.
---

# Rate limit the credential endpoints

## Goal

After this spec, no anonymous caller can drive a credential endpoint in a loop. The five endpoints
that exist — `POST /api/v1/auth/registrations`, `/auth/email-verifications`,
`/auth/verification-requests`, `/auth/sessions` and `/auth/tokens` — each answer `429` with a
`Retry-After` header once a caller passes its limit, keyed on the client IP **and** on the account
the request names, because either key alone is trivially bypassed
(`docs/technical-guide/40-security/application-security/STANDARD.md`). This closes **T-25**, the
threat that has been deferred three times and that subject V.6 grades, and it is the precondition
slice S1a placed on the hosted mail provider of issue O17: a real sender behind an unlimited
`/auth/verification-requests` is the T-19 quota loop.

This is the fourth unit of slice S1 and the one `docs/DELIVERY-PLAN.md` puts before any further
endpoint.

## Out of scope

- **Vote casting.** `docs/SDD.md` §13.3 names it in the same sentence as the credential endpoints,
  but no vote endpoint exists yet. It gets the same policy when slice S6 builds it, and the policy
  is written here so that slice attaches rather than invents.
- **Password reset endpoints.** `POST /auth/password-reset-requests` and `/auth/password-resets` are
  a later S1 spec. The policy they will need is one of the two this spec registers; the routes are
  not mapped yet and a policy attached to nothing is the dead-registration smell SPEC-005 avoided.
- **A distributed limiter.** State is in process, which is correct for the single instance
  `ADR-0015` deploys and wrong for several. Multi-instance limiting shares the fate of the SignalR
  backplane, issue O8, and is mentioned in the threat register rather than solved here.
- **Global rate limiting.** `application-security` asks for a global limit under the per-endpoint
  ones. The global limiter touches every future endpoint including the hub handshake, so it is its
  own unit; this spec covers the credential surface T-25 names.
- **Account lockout.** A limiter refuses a request; a lockout disables an account, which needs a
  column, an unlock path and a defence answer about denial of service by a third party. Progressive
  backoff, if escalation 3 approves it, slows a caller down and never locks an account out.
- **Audit records.** The audit behaviour is slice S2. Whether a `429` is logged as a security event
  before then is escalation 4.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — `ADR-0021` chose the built-in middleware and states "state is held **in process**". Progressive backoff on repeated login failure, which `authentication-authorization` makes mandatory, is not a built-in primitive: a fixed window, sliding window, token bucket and concurrency limiter are. Whichever mechanism escalation 3 picks must be recorded in `ADR-0021` Consequences, exactly as the MediatR licence verification is. **Before that, ADR-0021 still owes the line SPEC-005 left it** — the `Microsoft.AspNetCore.Authentication.JwtBearer` package is pinned and referenced but absent from the ADR's dependency set |
| 2 | new or upgraded dependency? | no — `Microsoft.AspNetCore.RateLimiting` ships in the framework and `Program.cs` already calls `AddRateLimiter` and `UseRateLimiter` |
| 3 | schema change beyond this spec, or destructive migration? | **no, as decided** — escalation 3 chose in-process state, so there is no table and no migration. The answer would have been yes for the table option |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — every identity row in §1 already lists `429`, and the "Every row here is rate limited per account **and** per IP" note is already written. This spec makes five existing rows true |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it stands in front of every credential endpoint, it partitions on a submitted email address, and a mistake here either locks real users out or leaves T-25 open while claiming it closed |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Escalation — decided by the owner, 2026-09-27

| # | Decision |
|---|---|
| 1 | **The proposed table, as written.** The five endpoint rows below are the approved numbers. |
| 2 | **Option B** — the middleware limits per IP; the per-account limit is enforced inside the handler, behind an abstraction `Application` declares. Untrusted JSON never reaches the limiter. |
| 3 | **In process.** Failure counts and the next permitted instant live in the process, not in a table, so gate 3 stays "no" and nothing is persisted. A restart forgives, and the count is per instance — both stated at defence. |
| 4 | **Log the trip here**, as a Serilog warning carrying the partition kind and the window and nothing that identifies the account. |

Two implementation notes, both inside these decisions rather than new ones, and both for the owner to
confirm at review:

- **The in-process store is a `ConcurrentDictionary` with time-based pruning, not `IMemoryCache`.**
  `Microsoft.Extensions.Caching.Memory` is not in `MusicRoom.Infrastructure`'s dependency graph, and
  adding it is gate 2, which this spec answers "no". The property escalation 3 asked for — in-process,
  self-expiring, no migration — is unchanged.
- **The approved table fixes permits and windows; it names no backoff ceiling.** The penalty therefore
  derives from the approved per-account window: the first violation waits one window, each further
  consecutive violation doubles it, capped at eight windows. Both the base and the cap are
  configuration, and the shipped defaults are exactly the approved numbers.

## Escalation — what a human must decide

### 1. The numbers (gate 5)

Nowhere in the repository is a permit count or a window written down. `docs/SDD.md` §13.3 and
`authentication-authorization` both say "rate limits keyed on account **and** IP" and stop. The
`"fixed"` policy sitting unattached in `Program.cs` uses 100 permits per minute per IP, which was
never a decision about credentials — it was a placeholder that proved the middleware was wired.

A starting proposal, to be confirmed or replaced. Every number is a guess until the owner signs it:

| Endpoint | Per IP | Per named account |
|---|---|---|
| `POST /auth/sessions` | 20 / 5 min | 5 / 5 min |
| `POST /auth/tokens` | 60 / 5 min | — (the request names no account; the token is the key, and hashing it to partition is escalation 2) |
| `POST /auth/registrations` | 10 / hour | 3 / hour |
| `POST /auth/verification-requests` | 10 / hour | 3 / hour |
| `POST /auth/email-verifications` | 30 / 5 min | — (the request names no account) |

The defence question is "why these numbers", so the answer must be a sentence per row, not a table
someone generated. A number too low breaks a real user on a shared NAT at a 42 campus; too high and
the limiter is decoration.

### 2. How a per-account partition reads the submitted address (gates 1 and 5)

`UseRateLimiter` runs **before** endpoint execution and therefore before model binding, so the
partition function has an `HttpContext` and no bound request object. The address this spec must
partition on is in the JSON body. Three ways, with the cost of each:

| Option | Mechanism | Cost |
|---|---|---|
| A | `httpContext.Request.EnableBuffering()` in the partition, read the body, parse the one field, rewind | the limiter parses untrusted JSON on every anonymous request — a parse cost and an allocation the limiter exists to bound. Needs a hard size cap before the read, and the parse must never throw into the middleware |
| B | limit per IP in the middleware, and enforce the per-account limit **inside** the handler, through an abstraction `Application` declares | the body is already bound and validated, so no double parse; the per-account counter lives with the code that knows which account was named. Against it: two mechanisms to explain at defence, and a request refused in the handler has already cost a database read |
| C | per-IP only, and record the missing per-account half in the threat register | smallest change, and it does not satisfy `authentication-authorization`, which says "account **and** IP, because either alone is trivially bypassed". A caller with a cheap IP pool defeats it |

**Recommendation, not a decision:** B. It keeps untrusted JSON out of the middleware and puts the
account key where the account is known. C is what this spec must not quietly become.

### 3. Progressive backoff: in-process or a table (gates 1 and 3)

`authentication-authorization` requires "progressive backoff on repeated login failure", which no
built-in limiter primitive expresses: a window limiter treats the eleventh request like the first
regardless of whether the previous ten were wrong passwords.

- **In process** — an `IMemoryCache` entry per account key holding a failure count and the instant
  the next attempt is permitted. Nothing persists, so a restart forgives every attacker, and the
  count is per instance. No migration, no schema change, gate 3 stays "no".
- **A table** — `login_attempt_counters`, keyed on the account, holding the count and the next
  permitted instant. Survives a restart, is visible to an operator, and is one more row written on
  the failure path of an anonymous endpoint — which is itself a write-amplification target for the
  very loop this spec exists to stop. Gate 3 becomes yes and this spec gains a migration.
- **Defer it** — ship windows now, and record the missing backoff as a threat-register row rather
  than claim V.6 is satisfied. Honest, and it leaves a graded requirement open for a fourth time.

The owner picks one. Whichever it is, `ADR-0021`'s "state is held in process" sentence is either
confirmed or superseded, and that edit is the owner's: no agent writes an ADR.

### 4. Is a rate-limit trip a logged security event (gate 5)

`application-security` says of a `429`: "Log the event; do not silently drop."
`docs/owasp/TOP10-CHECKLIST.md` A09 lists the rate-limit trip among the events that must be logged.
**T-27**, opened by SPEC-005, records that no credential refusal is logged at all today and points
at the slice S2 audit behaviour.

Two acceptable answers:

- **Log the trip here**, as a Serilog warning carrying the partition kind, the window and nothing
  that identifies the account — no address, no token, no digest. It is a handful of lines in
  `OnRejected` and it makes the limiter observable on defence day.
- **Leave it to S2**, keep T-27 as the record, and accept that the limiter fires invisibly until the
  audit behaviour lands.

## Files

### Discovered during implementation, 2026-09-27

Two files changed that the table below does not list, and one listed file did not need changing. Both
are mechanical consequences of listed rows; no design decision was taken here. **The owner accepts or
rejects this delta at review.**

| Path | Action | Why |
|---|---|---|
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/LoggingMailServiceTests.cs` | change | it calls `AddInfrastructureServices`, whose five parameters this spec collapsed into one `InfrastructureOptions` record to stay under S107. Two call sites; nothing the suite asserts moved |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HealthEndpointTests.cs` | **not changed** | listed nowhere and needed nothing: it boots the host but calls no credential endpoint, so no policy applies to it |

One deviation from the Steps, with the same property: the in-process store is a
`ConcurrentDictionary` pruned as it is used, not `IMemoryCache`, because
`Microsoft.Extensions.Caching.Memory` is absent from `MusicRoom.Infrastructure`'s dependency graph and
adding it is gate 2. Recorded in the escalation section above.


Exact paths, created or changed. An unattended run must not touch anything outside this list.
**The rows marked "escalation 2 option B" and "escalation 3" exist only if those options are chosen;
the table is re-walked against the decisions before implementation starts.**

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CredentialRateLimitOptions.cs` | create — the permit counts and windows from escalation 1, one record, read from configuration with no silent default |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CredentialRateLimiterPolicies.cs` | create — the named policies, their partition functions, and the `OnRejected` that writes `Retry-After` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — register the policies in place of the placeholder `"fixed"`, and read the options |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — `.RequireRateLimiting(...)` on all five routes and `.Produces(StatusCodes.Status429TooManyRequests)` so the document states it |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/ICredentialAttemptLimiter.cs` | create — **escalation 2 option B only.** The abstraction the handlers ask "may this account try again"; `Application` declares it, `Infrastructure` implements it (ADR-0028) |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/InMemoryCredentialAttemptLimiter.cs` | create — **escalation 2 option B only**, and the in-process half of escalation 3 |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — **escalation 2 option B only.** Register the limiter. Note: the signature is at five parameters after SPEC-005, which `SonarLint.xml` S107 caps, so this change collapses the parameter list into one options record rather than adding a sixth |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommandHandler.cs` | change — **escalation 2 option B only.** Ask the limiter before verifying, report the failure after |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | change — **escalation 2 option B only**, same shape |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestEmailVerification/RequestEmailVerificationCommandHandler.cs` | change — **escalation 2 option B only**, same shape |
| `MusicRoom/src/MusicRoom/Persistence/**` and a new migration | create — **escalation 3 "a table" only.** Exact paths written once that option is chosen, never before: an unattended run must not improvise a schema |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/CredentialRateLimitingTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/CredentialAttemptLimiterTests.cs` | create — **escalation 2 option B only** |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — its six tests share one IP and one address and will start tripping the new limits; the fixture raises the configured permits for the suites that are not about limiting |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SessionRotationEndpointTests.cs` | change — same, and its twenty-way parallel rotation test is exactly what a per-IP limit refuses |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | change — same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EmailVerificationEndpointTests.cs` | change — same, including its own twenty-way parallel test |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/VerificationRequestEndpointTests.cs` | change — same |
| `.env.example` | change — the permit counts and windows as explicit values, since the options record takes no silent default |
| `docs/api/openapi.json` | change — regenerated by `make openapi`; the five routes gain a `429` |
| `docs/SDD.md` | change — §16 V.6 brute-force row, which SPEC-005 left saying brute-force protection is absent |
| `docs/owasp/THREAT-REGISTER.md` | change — **T-25 closes**, with the test names as its Verified column, and T-01 gains the same evidence. If escalation 3 defers backoff, a new row records that gap instead of leaving it silent |
| `docs/SDD.pdf`, `docs/owasp/THREAT-REGISTER.pdf` | change — regenerated by `./docs/build-pdf.sh`, never hand-edited; `make agent-check` diffs them |
| `docs/specs/SPEC-006-credential-rate-limiting.md` | change — status transitions |

### The Files table walk

The five checks `docs/prompt-library/PROMPTS.md` §2.3 requires, answered against the tree as it is
today.

**1. Assembly membership and every reference it implies.** The policies and their options are
`WebAPI` types: the middleware is an ASP.NET concern and nothing below the composition root should
know a permit count. `WebAPI` already references `Application`, `Infrastructure` and `Persistence`,
so no `.csproj` changes. Under escalation 2 option B the abstraction goes in `Application` and its
implementation in `Infrastructure`, which already references `Application` — again no `.csproj`
change, and every reference points inward, matching `docs/SDD.md` §7.1. **No new project, and
nothing points outward.** `WebAPI` has no `RateLimiting/` folder yet; `Endpoints/` and
`HealthChecks/` are the precedent for one folder per concern.

**2. Types that cross an assembly boundary.** `CredentialRateLimitOptions` and
`CredentialRateLimiterPolicies` are consumed only by `Program.cs` in the same assembly, so they are
`internal`, as `AuthEndpoints` and `EnvironmentConfiguration` are. `ICredentialAttemptLimiter`
crosses from `Application` to `Infrastructure` and to the handlers, so it is `public` in its own
file, and its implementation is `internal sealed` with a public constructor, the shape
`BCryptPasswordHasher` and `JwtAccessTokenIssuer` already use.

**3. Does anything actually invoke it?** Every policy is attached to a route in the same change —
that is the whole point of this spec, and it is the mistake `Program.cs` currently documents: the
`"fixed"` policy has been registered and unattached since S0. Under option B, each handler that is
given the limiter calls it; no handler receives an abstraction it does not use.

**4. CLI, tool manifest, or unpinned package.** None. `Microsoft.AspNetCore.RateLimiting` is part of
the framework and is already imported in `Program.cs`. `dotnet ef migrations add` is available if
escalation 3 chooses a table.

**5. What the four corePackages actually contain.** Checked, not assumed. `Core.Security` holds
**nothing but a `.csproj`**; `Core.Application` holds only
`Pipelines/Validation/ValidationBehaviour.cs`; `Core.CrossCuttingConcerns` and `Core.Persistence`
hold only their `.csproj`. There is no limiter, no clock and no counter to reuse, and nothing in
this spec is written into them: a type that names a credential endpoint is not domain-agnostic.

**What the walk caught, and it is the expensive one.** Five existing test suites will break the
moment a limit is attached. `LoginEndpointTests` signs in repeatedly from one client;
`EmailVerificationEndpointTests` and `SessionRotationEndpointTests` each fire **twenty parallel
requests at one endpoint** to prove a race, which is precisely the traffic a per-IP limit refuses.
Those two tests are the evidence for two concurrency claims in `docs/SDD.md` §16 and must keep
proving them: the fix is a test-host configuration that raises the permits for suites that are not
about limiting, never `[Fact(Skip = ...)]` and never a weakened assertion. That is also why
`.env.example` and an options record appear in the table at all — a limit hard-coded in
`Program.cs` cannot be raised by a test host.

## Steps

Written against the recommendations. **Re-walk this list against the four decisions before starting;
where a decision differs, the step changes, not the design.**

1. `CredentialRateLimitOptions`: one record carrying the permit count and window per policy, read
   from configuration in `Program.cs` the way the JWT settings are — no default, missing values fail
   start-up loudly. The record is what lets a test host raise the numbers.
2. `CredentialRateLimiterPolicies`: two named policies, `"credential-ip"` and, under option B,
   whatever the handler-side limiter needs. The IP partition uses
   `httpContext.Connection.RemoteIpAddress`, falling back to a single `"unknown"` bucket exactly as
   the placeholder policy does today — an absent IP must share one small bucket, not get its own.
3. `OnRejected`: set `429`, write `Retry-After` in seconds from the limiter's
   `RetryAfterMetadata`, and return **no body** on the credential endpoints. A ProblemDetails body
   naming which partition tripped tells a caller which key to vary.
4. Attach the policies in `AuthEndpoints` and add `.Produces(StatusCodes.Status429TooManyRequests)`
   to each of the five routes, so `docs/api/openapi.json` states what the contract already promises.
5. Under escalation 2 option B: `ICredentialAttemptLimiter` with two members — one that asks whether
   an account key may attempt now, one that records a failure — and an in-memory implementation
   whose entries expire on their own, so a quiet account leaves no state behind.
6. Under escalation 3 "in process": the same implementation holds the failure count and the next
   permitted instant, and the delay grows per consecutive failure up to a stated ceiling. The ceiling
   is a number the owner approves in escalation 1.
7. Raise the configured permits in the five existing test suites through the test host, and assert in
   each that the behaviour they were written for is unchanged.
8. Close **T-25** in `docs/owasp/THREAT-REGISTER.md` by pointing at the new test names, and update
   `docs/SDD.md` §16 V.6. If any part of the control is deferred, the row says so instead of
   claiming the threat closed.

## Verification

```bash
make agent-check
```

That one target is the whole gate. `Makefile:60` runs, in order: `dotnet restore`,
`dotnet format --verify-no-changes`, `dotnet build -warnaserror --no-restore`,
`dotnet test --no-build`, `dotnet list package --vulnerable --include-transitive`, `make openapi`
followed by `git diff --exit-code -- docs/api/openapi.json`, then `./docs/build-pdf.sh` followed by
`git diff --exit-code -- '*.pdf'`. It needs Docker and pandoc; a missing tool is an environment
result, not a failure of the code.

| Check | Proves |
|---|---|
| `Login_BeyondThePerIpLimit_Returns429WithRetryAfter` | V.6 — the limiter fires, and the response carries the header `rest-api-design` requires |
| `Login_BeyondThePerAccountLimit_Returns429ForThatAddressOnly` | account **and** IP: a second address from the same client is still served |
| `Login_FromASecondIp_IsNotRefusedByAnotherClientsCounter` | the IP partition is per client, not global |
| `Registrations_BeyondTheLimit_Returns429` | T-25's row creation loop is bounded |
| `VerificationRequests_BeyondTheLimit_Returns429` | T-19: the mail loop is bounded **before** O17 lands a real sender |
| `EmailVerifications_BeyondTheLimit_Returns429` | the token-guessing loop is bounded |
| `Tokens_BeyondTheLimit_Returns429` | the refresh-token guessing loop is bounded |
| `Refused_Request_BodyIsEmpty` | a `429` says nothing about which partition tripped |
| `CredentialAttemptLimiterTests` — count, escalating delay, expiry | escalation 3's backoff, if it is built here |
| `LoginEndpointTests`, `SessionRotationEndpointTests`, `EmailVerificationEndpointTests`, `RegistrationEndpointTests`, `VerificationRequestEndpointTests` (existing) | the five suites still prove what they proved before, including both twenty-way parallel races |

A `429` test that shares its counter with another test proves nothing. Each limiting test uses its
own partition key — its own address, and a distinct `X-Forwarded-For` or test client where the IP
partition is what is under test — so the order the suite runs in cannot change the result.

## Done when

- [x] every file in the Files table exists and compiles, minus the one row that needed nothing and plus
      the one discovered above
- [x] every command under Verification passes — `make agent-check` exited 0 on 2026-09-27
- [x] OWASP checklists walked for the surface touched (A02, A07, A09, API2, API4). A07 and API2 are now
      satisfied on a single instance; A09 remains partly open and T-27 was **narrowed**, not closed:
      the rate-limit trip is logged, an individual failed login and a family revocation are not
- [x] **T-25 closed** in `docs/owasp/THREAT-REGISTER.md`, with both new test suites in its Verified
      column and the in-process residual (O8, and a restart forgiving every caller) written down.
      T-01 gained the same evidence
- [x] `docs/SDD.md` §16 V.6 now reads `implemented`, and §13.3 carries the approved numbers — the spec
      is disposable, so the decision needed a home in the SDD
- [x] `docs/api/openapi.json` regenerated in the same change, and `docs/SDD.pdf` plus
      `docs/owasp/THREAT-REGISTER.pdf` rebuilt by `./docs/build-pdf.sh`, so both drift guards pass
- [x] `status: done` in this file

## Follow-ups this spec deliberately leaves open

| Item | Where it belongs |
|---|---|
| The same policy on the password-reset endpoints | the password-reset spec, which maps those routes |
| The same policy on vote casting | slice S6, which creates the vote endpoint |
| A global rate limit under the per-endpoint ones | its own spec; it touches the hub handshake too |
| Distributed limiter state across instances | issue O8, with the SignalR backplane |
| Logging the trip as an audit record | slice S2, T-27 — unless escalation 4 pulls the Serilog line forward |
