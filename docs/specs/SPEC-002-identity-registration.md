---
id: SPEC-002
title: Register an account — User aggregate, first EF Core migration, BCrypt hashing, POST /auth/registrations
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6, V.8]
decisions: [ADR-0007, ADR-0011, ADR-0012, ADR-0013, ADR-0021, ADR-0026, ADR-0006]
standards: [authentication-authorization, application-security, database-design-migrations, dotnet-csharp, clean-architecture-cqrs, testing, openapi-documentation, logging-observability]
approved_by: enesozmert   # Tier B, owner approval 2026-09-24
escalation: |
  Tier B. Three gate answers are "yes" and one stop condition in
  docs/prompt-library/PROMPTS.md §2.7 is already met. The unattended run wrote this spec and
  stopped without creating a branch, editing code, or committing.

  Gate 2 — dependency. Two packages must be pinned in MusicRoom/Directory.Packages.props:
  BCrypt.Net-Next (password hashing, ADR-0007) and Testcontainers.PostgreSql (the first
  persistence test, ADR-0012). Both are named on the ADR-0021 list and were deliberately left
  unpinned in SPEC-001 with the comment "Pin each when the code that uses it lands." That is this
  spec. The decision is therefore already made; what is missing is the human act of adding a
  dependency, which AGENTS.md §6 and the §2.8 guardrail block reserve for a human in every case.
  Needed from the owner: approval of the two package ids and their versions.

  Gate 5 — authentication and secrets. This is the first code in the repository that stores a
  credential. It creates the users table, the password_hash column and the BCrypt work factor.
  Nothing here can be routine by construction.

  Stop condition — a credential the slice needs does not exist yet. ADR-0026 lists "Mail provider
  API key — needed by S1" and states that a run reaching a slice with an absent credential stops
  correctly. The mail provider chosen in ADR-0013 has no account and no API key yet. This spec is
  written so that registration is verifiable without one — step 4 of SDD §10.1, sending the
  verification mail, is out of scope and lands in the next spec once the key exists — but the
  owner should create the key before approving, because SPEC-003 cannot start without it.

  Open question for the owner, which the agent may not answer: SDD §10.1 step 5 requires a 202
  response whether or not the address is already registered, so that the endpoint is not an
  account-enumeration oracle. Without mail delivery there is no channel that tells the real user
  what happened. Two readings are defensible — ship the honest 202 now and let the mailbox become
  meaningful in SPEC-003, or hold the whole endpoint until mail works. This spec assumes the
  first. Say so if you want the second.

  ---

  OWNER DECISIONS, 2026-09-24 — all four answered, this spec is approved.

  1. Gate 2, packages: APPROVED — three, not two. Pin BCrypt.Net-Next and Testcontainers.PostgreSql,
     and add Microsoft.EntityFrameworkCore.Design, which a second cycle correctly pointed out is
     required by `dotnet ef migrations add` and is named by no ADR. It is Microsoft-owned,
     design-time only, ships no runtime code, and carries the same justification SPEC-001 gave
     Microsoft.Extensions.ApiDescription.Server. Reference it from Persistence with
     PrivateAssets="all" so it does not flow to consumers.

     Pin BCrypt.Net-Next and Testcontainers.PostgreSql. The agent
     chooses the current stable version of each; CI's `dotnet list package --vulnerable
     --include-transitive` is the gate on a bad one. Testcontainers is not deferred — ADR-0012
     rejects the in-memory provider precisely because it does not enforce a unique index, which is
     what the ux_users_email test has to prove.

  2. Gate 5, authentication surface: APPROVED as written. users(Email, PasswordHash, DisplayName,
     IsEmailVerified) and email_verification_tokens(token hash, expiry, consumed-at), work factor
     from configuration with a floor of 12, ux_users_email as the invariant. Rate limiting stays
     out of scope on the spec's own reasoning: it is shared with login, reset and resend, and
     building it for one endpoint means building it twice.

  3. Open question, 202 without mail: SHIP THE 202 NOW. The endpoint learns the correct behaviour
     from the start and the mailbox becomes meaningful in SPEC-003. There are no real users in
     between. Building an enumeration oracle and removing it later would contradict SDD §10.1 and
     OWASP A07, and would be hard to defend.

  4. Mail provider: SPLIT. SPEC-003 defines IMailService and ships a Development-only logging
     implementation, so the verification flow is testable end to end immediately. The real
     provider adapter is its own small spec at the end of S1, when the account and API key exist.
     Tracked as issue O17. V.1 still requires real delivery before the defence — the logging
     implementation is a staging post, not the destination.

  ---

  BLOCKED AT STAGE 3, 2026-09-24 — the spec cannot be implemented as written.

  The owner's four decisions above are not in question and nothing below reopens them. The run
  resumed the approved spec, read AGENTS.md, ADR-0011, ADR-0007, the clean-architecture-cqrs,
  ef-core-persistence, database-design-migrations, testing and authentication-authorization
  standards, SDD §7.1, §8.2, §8.3, §10.1, §16 and the endpoint contract, and then stopped before
  the first edit. No branch was created, no source file was written, no commit was made. The only
  change on disk is this status transition and this note.

  Two defects, both of them stop conditions in docs/prompt-library/PROMPTS.md §2.7. Each needs a
  human answer; neither is an agent's to decide.

  DEFECT 1 — the Files table places the common-column base where Domain cannot reach it.

  Row: `MusicRoom/src/corePackages/Core.Persistence/Repositories/Entity.cs`, "create — `id`,
  `created_at_utc`, `updated_at_utc` base, per SDD §8.3". Step 2 repeats it.

  For `User` and `EmailVerificationToken` to carry those columns from that base, they must inherit
  it, which means `MusicRoom.Domain.csproj` must reference `Core.Persistence.csproj`. Three
  accepted sources forbid that reference, in the words of each:

  - ADR-0011 Consequences: "The dependency rule from clean-architecture-cqrs is unchanged and
    still enforced: arrows point inward, `Domain` references nothing." The same ADR scopes
    corePackages to "cross-cutting packages, reused by the application projects" — Domain is not
    one of them.
  - clean-architecture-cqrs v1.2.0: "`Domain` MUST NOT reference any other project or any
    infrastructure package", and the project table gives Domain "may reference: nothing".
  - SDD §7.1: "`Domain` references no other project and no infrastructure package. A reference
    pointing outward is a build-breaking defect, not a style issue."

  This is where the shape copied from `reference/nArchitecture` and the dependency rule disagree.
  nArchitecture does have `Core.Persistence/Repositories/Entity.cs` and its domain entities do
  inherit it, so the file path in this spec is faithful to the reference; ADR-0011 copied the
  layout while explicitly declining that one edge. Gate question 1 was answered "no" on the
  reading that this spec implements ADR-0011 as written. It does not.

  Three resolutions, all of them the owner's to pick:

  (a) Keep ADR-0011 as it stands. Drop the `Entity.cs` row and declare `Id`, `CreatedAtUtc` and
      `UpdatedAtUtc` on each Domain entity. Cheapest now; every later aggregate repeats three
      properties, and the timestamp-stamping logic has no shared type to hang on.
  (b) Keep ADR-0011 as it stands and put the base inside `Domain` itself — Domain still references
      nothing, and one shared abstract type serves every aggregate. Costs a Files-table row that
      the spec does not have and moves a domain-agnostic type out of corePackages, which ADR-0011
      says is where domain-agnostic types belong.
  (c) Amend ADR-0011 to permit `Domain -> Core.Persistence` for persistence-agnostic base types
      only, and update clean-architecture-cqrs and SDD §7.1 to match. This is the nArchitecture
      shape, and it is a real weakening of the rule the project intends to defend at evaluation.

  The agent may draft the ADR for (c). It may not choose between (a), (b) and (c) — AGENTS.md §5
  rule 5, and §6 first bullet: escalate when the task would require "a new or changed ADR, or
  contradicting an accepted one".

  DEFECT 2 — five project files must change and none of them is in the Files table.

  Step 1 says "Reference them only from the projects that use them", and the owner's decision 1
  says to reference Microsoft.EntityFrameworkCore.Design "from Persistence with
  PrivateAssets="all"". Both instruct edits to `.csproj` files that the Files table does not list,
  and §2.7 makes "a file outside the spec's Files table would change" a stop condition on its own.

  | Project file | Why it must change |
  |---|---|
  | `MusicRoom/src/corePackages/Core.Security/Core.Security.csproj` | add the `BCrypt.Net-Next` PackageReference for `BCryptPasswordHasher` |
  | `MusicRoom/src/MusicRoom/Application/MusicRoom.Application.csproj` | `RegisterCommandHandler` consumes `IPasswordHasher`, which the spec places in `Core.Security`; Application references only Domain, Core.Application and Core.CrossCuttingConcerns today |
  | `MusicRoom/src/MusicRoom/Persistence/MusicRoom.Persistence.csproj` | add `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets="all"`, per the owner's decision 1 |
  | `MusicRoom/tests/MusicRoom.WebAPI.Tests/MusicRoom.WebAPI.Tests.csproj` | `RegistrationEndpointTests` is Testcontainers-backed and asserts on the row, so it needs the `Testcontainers.PostgreSql` PackageReference and a reference to Persistence |
  | `MusicRoom/src/corePackages/Core.Persistence/Core.Persistence.csproj` | only if resolution (c) above is taken and `Entity.cs` lands here |

  The Application row is not bookkeeping. Putting `IPasswordHasher` in `Core.Security` and calling
  it from a handler creates `Application -> Core.Security`, a dependency edge no ADR, standard or
  SDD section has yet drawn. ADR-0011 lists Core.Security as "JWT, hashing, token services", so the
  edge is plainly intended, but it is the first one of its kind and it should be written down
  rather than introduced by an unattended run.

  WHAT IS NEEDED TO UNBLOCK

  1. Pick (a), (b) or (c) for the common-column base. If (c), the ADR change lands first.
  2. Confirm `Application -> Core.Security` is the intended edge for `IPasswordHasher`, or name
     the project the interface should live in instead.
  3. Add the four or five project files above to the Files table.
  4. Set status back to `approved`. Stage 1 will resume this spec on the next cycle with no
     second spec written and no second gate run.

  Nothing else in the spec is in doubt. The package set, the two tables, the work-factor floor,
  the 202 behaviour, the test list and the parallel `ux_users_email` test all stand as approved.

  ---

  BLOCK RESOLVED, 2026-09-24 — ADR-0028. The first implementation attempt stopped before its first
  edit and was right to. Both defects were in this spec and in the documents behind it, not in the
  reasoning that found them.

  Defect 1, the common-column base. `Core.Persistence/Repositories/Entity.cs` would have forced
  `MusicRoom.Domain` to reference a corePackage, which ADR-0011, clean-architecture-cqrs and
  SDD §7.1 each forbid in the same words. RESOLVED: the base moves to
  `MusicRoom/src/MusicRoom/Domain/Common/Entity.cs`. Identity and creation time are domain facts.
  Accepted cost: the generic repository in Core.Persistence is generic over `where TEntity : class`
  and cannot constrain on the base, because a corePackage stays domain-agnostic.

  Defect 2, the undrawn edge. `IPasswordHasher` in `Core.Security` required `Application ->
  Core.Security`, an edge no ADR or SDD section had drawn. RESOLVED: the interface is declared in
  `MusicRoom.Application`, the BCrypt implementation lives in `MusicRoom.Infrastructure`. This is
  what clean-architecture-cqrs already required, so no new edge exists. `Core.Security` keeps JWT
  and token helpers for Infrastructure and WebAPI. A side benefit: the BCrypt choice no longer
  leaks upward, so ADR-0007 can be superseded by touching Infrastructure alone.

  Defect 2 also named five project files that had to change and were not listed. All five are in
  the Files table now.

  This spec is approved again. Stage 1 resumes it; no second spec, no second gate.

  ---

  BLOCKED AT STAGE 3 AGAIN, 2026-09-24 — the write path still has no abstraction to write through.

  ADR-0028 is not in question and nothing below reopens it. Its two resolutions hold: the common
  column base is `Domain/Common/Entity.cs`, and `IPasswordHasher` is declared in `Application` with
  the BCrypt implementation in `Infrastructure`. The run resumed the approved spec, read AGENTS.md,
  ADR-0028, ADR-0007, ADR-0011, ADR-0026, the clean-architecture-cqrs, ef-core-persistence,
  database-design-migrations, testing, authentication-authorization, dotnet-csharp and
  openapi-documentation standards, SDD §7.1, §8.2, §8.3, §10.1, §16, `.editorconfig`, the Makefile,
  `docker-compose.yml` and every existing file under `MusicRoom/`. It then stopped before the first
  source edit. Branch `feat/spec-002-identity-registration` exists; no source file was written, no
  commit was made, nothing was pushed. The only change on disk is this status transition and this
  note.

  Two defects. Both are stop conditions in `docs/prompt-library/PROMPTS.md` §2.7. Neither is the
  agent's to decide.

  DEFECT 3 — `RegisterCommandHandler` cannot reach the database, because no persistence abstraction
  appears anywhere in the Files table.

  Step 6 says the handler "hashes, attempts the insert, and treats a unique-violation as success".
  The Files table gives the handler exactly one injectable abstraction,
  `Application/Abstractions/Security/IPasswordHasher.cs`. There is no repository interface, no unit
  of work and no `DbContext` abstraction in the table, and none exists in the repository today:
  all four `corePackages` projects contain a `.csproj` and no `.cs` file at all.

  The handler cannot take `MusicRoomDbContext` instead. `Application` MUST NOT reference EF Core
  (clean-architecture-cqrs v1.2.0, hard rules; SDD §7.1), and `MusicRoomDbContext` sits in
  `Persistence`, which is outward from `Application`.

  The interface also cannot be folded into a file the table already lists. It has to be `public`
  for `Persistence` to implement it across the assembly boundary, and dotnet-csharp v1.3.0 states
  "File name matches the single public type it contains. One public type per file." Declaring it
  beside `RegisterCommandHandler` trades a Files-table violation for a standards violation.

  Where it belongs is already settled and needs no new decision — ef-core-persistence v1.1.0:
  "Interfaces in `Application`, implementations in `Persistence`." What is missing is only the
  Files-table rows:

  | Path | Action |
  |---|---|
  | `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | create — the write path the handler consumes |
  | `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | create — the EF Core implementation, which is also where the unique-violation is caught |

  ef-core-persistence also asks for "a generic base repository" with entity-specific repositories
  on top. For a single aggregate the two rows above are enough, and ADR-0028 already accepted that
  a generic base in `Core.Persistence` can only constrain on `where TEntity : class`. Adding that
  base now would be a third project and a fourth row; the recommendation is to defer it until a
  second aggregate exists, and to say so in the spec so the next reader does not re-derive it.

  DEFECT 4 — the readiness-check row asks `WebAPI` to touch `DbContext`, which a hard rule forbids.

  Row: `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/DatabaseReadinessCheck.cs`, "change — read
  through the `DbContext` now that one exists". Step 8 repeats it: "move the readiness check onto
  the `DbContext`".

  `DatabaseReadinessCheck` is a `WebAPI` type. Three accepted sources forbid what the row asks, in
  the same words each time:

  - clean-architecture-cqrs v1.2.0, hard rules: "`WebAPI` MUST NOT touch `DbContext` directly."
  - The same standard's project table gives `WebAPI` "controllers, SignalR hubs, middleware, DI
    registration, OpenAPI" — no data access.
  - SDD §7.1: "`WebAPI` never touches `DbContext` directly. A reference pointing outward is a
    build-breaking defect, not a style issue."

  This is the same shape as ADR-0028's defect 2: a layering edge the spec introduces in passing and
  no document has drawn. Gate question 1 was answered "no" on the reading that this spec implements
  the accepted layering. On this row it does not.

  Three resolutions, all of them the owner's to pick:

  (a) Drop the row. `DatabaseReadinessCheck` keeps opening an `NpgsqlConnection` and stays exactly
      as SPEC-001 wrote it. Readiness then proves the socket, not the schema — which is what a
      readiness probe is for, and what T-24 in the threat register already describes. Cheapest,
      and no layering question arises.
  (b) Declare a readiness abstraction in `Application` and implement it in `Persistence`, the same
      shape ADR-0028 chose for `IPasswordHasher`. Readiness then proves the schema is reachable
      through the mapped context. Costs two more Files-table rows and one more `Application`
      abstraction whose only consumer is a health check.
  (c) Use `AddDbContextCheck` from `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`.
      Rejected as an agent suggestion rather than escalated: it is a new dependency (gate 2), and
      registering it still names the `DbContext` type inside `WebAPI`, so it does not avoid the
      layering question it appears to solve.

  The recommendation is (a), with one sentence in the spec saying why readiness deliberately checks
  the connection and not the schema. It is the only resolution that adds nothing.

  NOT BLOCKING, BUT THE FILES TABLE SHOULD SAY SO

  1. `MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj` is listed as "change — reference
     `Persistence` and `Infrastructure` for composition". It already references both, from SPEC-001.
     The row is a no-op; leaving it in place invites a reader to look for a diff that will not exist.
  2. `BCryptPasswordHasher` needs a work factor "from configuration with a floor of 12". The run
     intends to read it with a default of 12 in code, so that
     `MusicRoom/src/MusicRoom/WebAPI/appsettings.json` — which the table does not list — needs no
     change. Confirm that reading, or add the appsettings row.
  3. Nothing registers `BCryptPasswordHasher`. The table lists `ApplicationServiceRegistration.cs`
     and `PersistenceServiceRegistration.cs` but no `InfrastructureServiceRegistration.cs`, so the
     run intends to register the concrete type from `Program.cs`, which is the composition root and
     may legitimately do it. Confirm that, or add the row for symmetry with the other two layers.
  4. `Application/Behaviours/ValidationBehaviour.cs` is not in the table, so `RegisterCommandValidator`
     would never run: FluentValidation validators do not execute themselves, and
     clean-architecture-cqrs puts validation at position 3 of the MediatR pipeline. The run intends
     to validate inside `AuthEndpoints` and return `400`/`422` there, which keeps the diff smaller
     and is defensible while exactly one command exists. Confirm that, or add the behaviour row —
     but note that the endpoint-level choice must be revisited the moment a second command lands,
     and the standard's "a cross-cutting concern that appears in more than two handlers belongs in a
     behaviour" is the trigger.
  5. `make migrate` and `dotnet ef migrations add` both need the `dotnet-ef` CLI. It is referenced by
     ADR-0026 and the ef-core-persistence standard but is not installed on this host and the
     repository has no `.config/dotnet-tools.json`. A local tool manifest is the reproducible answer
     and would be a new file outside the table; a documented global install is the alternative and
     costs nothing in the repository. Owner's call which one, because a manifest is also a pinned
     tool version, which is close enough to a dependency to deserve the gate.
  6. `make agent-check` runs `dotnet format --verify-no-changes` across the solution, and
     EF-generated migration files routinely fail it. Expect one formatting pass over
     `Persistence/Migrations/` inside the self-heal budget. Recorded here so it is not mistaken for
     a code defect on the next cycle.

  WHAT IS NEEDED TO UNBLOCK

  1. Add the two rows in defect 3 to the Files table. No decision is required; the placement is
     already fixed by ef-core-persistence and ADR-0028.
  2. Pick (a), (b) or (c) for the readiness check in defect 4, and correct the row and step 8 to
     match. If (b), the two extra rows land with it.
  3. Answer the five confirmations above, or add the corresponding rows.
  4. Set status back to `approved`. Stage 1 will resume this spec on the next cycle with no second
     spec written and no second gate run.

  Nothing else in the spec is in doubt. The package set, the two tables, the work-factor floor, the
  202 behaviour, the test list and the parallel `ux_users_email` test all stand as approved.

  ---

  BLOCK 2 RESOLVED, 2026-09-24. The second implementation attempt stopped before its first source
  edit and was right again. Both defects were in this spec.

  Defect 3, the handler had nothing to write through. Step 6 inserts a row, but the Files table
  gave the handler only `IPasswordHasher`. It cannot take `MusicRoomDbContext`, because
  `Application` must not reference EF Core. ef-core-persistence already settles the placement, so
  no decision was needed, only the missing rows: `IUserRepository` in Application,
  `UserRepository` in Persistence.

  Defect 4, the readiness row broke layering. Changing `DatabaseReadinessCheck.cs` to read through
  the `DbContext` would put `WebAPI` in touch with EF Core, which clean-architecture-cqrs and
  SDD §7.1 forbid in the same words as ADR-0028 defect 2. RESOLVED by dropping the row: the
  existing `NpgsqlConnection` probe stays. Readiness answers "can I reach the database", not
  "is the schema current" — a migration check belongs to startup, not to a liveness surface.

  Five confirmations, all answered:
  - The `MusicRoom.WebAPI.csproj` row was a no-op; it already references Persistence and
    Infrastructure. Removed.
  - Work factor lives in `appsettings.json` as `Security:BCryptWorkFactor`, and the hasher
    enforces a floor of 12 in code. Configuration can raise it, never lower it below the ADR-0007
    minimum.
  - `BCryptPasswordHasher` is registered in a new `InfrastructureServiceRegistration.cs`, matching
    the pattern the spec already uses for Persistence and Application.
  - **No `ValidationBehaviour` exists**, so the validator in step 6 would never have run. All four
    corePackages are empty of `.cs` files today. The behaviour is now a listed file in
    `Core.Application`, and `Core.Application.csproj` gains MediatR and FluentValidation.
    This is the first pipeline behaviour in the project; the other four from
    clean-architecture-cqrs arrive with the slices that need them.
  - `dotnet-ef` is absent. APPROVED: `.config/dotnet-tools.json` pins it and `make migrate` runs
    `dotnet tool restore` first. A build that depends on a CLI installed by hand on one laptop
    fails subject IV.1 on a clean clone.

  ---

  BLOCKED AT STAGE 3, THIRD TIME, 2026-09-24 — the migration cannot be generated from the projects
  the Files table allows. One `.csproj` line is missing and it is not this run's to add.

  Nothing above is reopened. ADR-0028 holds, the owner's four decisions hold, and the two earlier
  block resolutions hold. Unlike the first two blocks, this run did not stop before its first edit:
  every other row of the Files table is implemented, and the solution builds clean with
  `-warnaserror` and zero warnings across all ten projects. The work is on branch
  `feat/spec-002-identity-registration`, uncommitted and unpushed, which is what §2.8 requires of a
  blocked result.

  DEFECT 5 — `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` on the **startup** project,
  and the Files table lets it exist only on `Persistence`, privately.

  The owner's decision 1 is explicit: reference the package "from Persistence with
  `PrivateAssets="all"` so it does not flow to consumers". That was done. `PrivateAssets="all"` is
  what makes the design-time package absent from every consumer's dependency graph — including the
  startup project the EF tools require it on. Both orderings were tried, and both fail:

  | Command | Failure |
  |---|---|
  | `--startup-project` = `WebAPI` | `Your startup project 'MusicRoom.WebAPI' doesn't reference Microsoft.EntityFrameworkCore.Design.` |
  | `--startup-project` = `Persistence` | `Unable to resolve service for type 'DbContextOptions<MusicRoomDbContext>'` — a class library has no host, so EF has no options to build the context from |

  The second failure is not a missing package: `Persistence` does reference `Design`. It is the
  absence of an `IDesignTimeDbContextFactory<MusicRoomDbContext>`, which the Files table does not
  list either.

  So three Files-table rows cannot all hold at once:

  - `MusicRoom/src/MusicRoom/Persistence/Migrations/` — "create — the initial migration, EF-generated"
  - `MusicRoom.Persistence.csproj` — "reference `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets="all"`"
  - `_(removed)_` — `MusicRoom.WebAPI.csproj` is **not** in the table, so it may not change

  Two resolutions. Both are the owner's, because both are dependency placement, which AGENTS.md §6
  reserves for a human in every case.

  (a) **Add `Microsoft.EntityFrameworkCore.Design` to `MusicRoom.WebAPI.csproj` with
      `PrivateAssets="all"`, and add that file to the Files table.** This is the arrangement
      Microsoft documents. It adds no code and no runtime asset: `PrivateAssets="all"` still keeps
      the package out of the published output, so the owner's intent — a design-time package that
      never ships — is preserved. The package would then be declared in two projects. Recommended.

  (b) **Add `MusicRoom/src/MusicRoom/Persistence/DesignTimeDbContextFactory.cs` to the Files table**
      and run `dotnet ef` with `Persistence` as its own startup project. `WebAPI` stays untouched
      and the design-time package stays in exactly one project. Costs a production-assembly type
      that exists only for tooling, and a second place that knows the name `DATABASE_URL`, which
      ADR-0026 currently keeps in one.

  Rejected without escalating: dropping `PrivateAssets="all"` so the package flows to `WebAPI`
  transitively. It contradicts the owner's decision 1 in its own words and would put a design-time
  package into the API's shipped dependency graph — the opposite of what that decision asked for.

  WHAT WAS BUILT, AND WHAT IS NOT YET PROVEN

  Implemented and compiling: the package pins, `.config/dotnet-tools.json`, `Domain/Common/Entity`,
  `User`, `EmailVerificationToken`, `IPasswordHasher`, `IUserRepository`, `ValidationBehaviour`,
  `BCryptPasswordHasher`, `InfrastructureServiceRegistration`, `MusicRoomDbContext`, both entity
  configurations, `UserRepository`, `PersistenceServiceRegistration`, `ApplicationServiceRegistration`,
  `RegisterCommand` / handler / validator, `AuthEndpoints`, the `Program.cs` wiring, and all three
  test projects with every test the Verification table names.

  Proven green on this branch: `dotnet format --verify-no-changes` over the whole solution, the
  five `RegisterCommandValidatorTests` cases, and the three `BCryptPasswordHasherTests` cases.
  Neither test project needs a database.

  Not proven, because they all depend on the migration that could not be generated: `make migrate`,
  and the four `RegistrationEndpointTests` cases, which call `Database.MigrateAsync()` against the
  Testcontainers engine. `make agent-check` was therefore not run to completion either. No row of
  `docs/SDD.md` §16 was moved, no migration was recorded, and no threat-register row was added:
  none of them has code behind it yet.

  TWO FINDINGS WORTH KEEPING, NEITHER BLOCKING

  1. **`IDE1006` treats a `private const` as a field**, so `private const int Foo` fails the build
     with "missing prefix '_'". The repository's existing code never hit this because every
     constant it declares is either a method-local (`Program.cs`, `DatabaseReadinessCheck.cs`) or
     `internal` (`HealthEndpointTests.cs`). This run converted its constants to method-locals and
     to `internal` to match, rather than touching a severity. It is worth a
     `brain/lessons/lessons-index.md` row: it will bite every future spec that declares a constant,
     and the fix is a convention, not a suppression.
  2. **`Microsoft.EntityFrameworkCore.Design` is pinned at 10.0.4, not at the newest 10.0.12.**
     `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 brings EF Core 10.0.4, and a Design package
     ahead of it fails the build with `MSB3277` assembly-version conflicts. The pin tracks the
     provider, not the calendar. `.config/dotnet-tools.json` pins `dotnet-ef` at the same 10.0.4 for
     the same reason.

  One more item the Files table should settle when it is next edited: the table lists no
  `appsettings.json` row, and the second block's resolution said the work factor "lives in
  `appsettings.json` as `Security:BCryptWorkFactor`". This run read that exact key from
  configuration with a code-side default and floor of `12`, and left `appsettings.json` unchanged,
  so no file outside the table was touched. The behaviour is identical whether or not the key is
  present. Confirm that reading, or add the `appsettings.json` row.

  WHAT IS NEEDED TO UNBLOCK

  1. Pick (a) or (b) for the design-time package, and add the corresponding file to the Files table.
  2. Confirm the `appsettings.json` reading above, or add the row.
  3. Set status back to `approved`. Stage 1 resumes this spec on the next cycle with no second spec
     written and no second gate run. The branch already holds the implementation; the next run
     generates the migration, runs `make agent-check` and `make migrate`, and closes out.

  ---

  BLOCK 3 RESOLVED, 2026-09-24 — defect 5, dependency placement.

  `dotnet ef` resolves `Microsoft.EntityFrameworkCore.Design` from the **startup** project, not
  from the project holding the `DbContext`. Owner decision 1 put it on `Persistence` with
  `PrivateAssets="all"`, which is exactly what prevents it flowing to `WebAPI`. Both orderings
  failed, as reported:

    startup = WebAPI       -> doesn't reference Microsoft.EntityFrameworkCore.Design
    startup = Persistence  -> Unable to resolve service for type DbContextOptions<MusicRoomDbContext>

  RESOLVED, option (a): add the package to `MusicRoom.WebAPI.csproj`, also with
  `PrivateAssets="all"`. It is design-time only and ships no runtime code, so it appears on both
  projects and flows out of neither. Option (b), an `IDesignTimeDbContextFactory`, was rejected:
  it is a second construction path for the `DbContext` that exists only to satisfy a CLI, and it
  drifts from `Program.cs` the moment configuration changes.

  Two findings the cycle asked to be recorded, now lessons L-031 and L-032: `IDE1006` covers
  `private const`, and the EF Design version must track the Npgsql provider's EF Core version
  rather than the newest published one.

  ---

  STOPPED ON THE ENVIRONMENT, 2026-09-24 — `.env` does not exist on this host, so `make migrate`
  cannot run. Nothing is wrong with the code or with this spec.

  Block 3's resolution held. `Microsoft.EntityFrameworkCore.Design` now sits on
  `MusicRoom.WebAPI.csproj` as well, with `PrivateAssets="all"`, and `dotnet ef migrations add`
  succeeded from the `WebAPI` startup project on the first attempt after that one line. The
  migration `20260924195820_InitialIdentity` exists, creates `users` and
  `email_verification_tokens`, and carries `ux_users_email`,
  `ux_email_verification_tokens_token_hash` and `ix_email_verification_tokens_user_id`.

  WHAT IS PROVEN GREEN ON THIS BRANCH

  `make agent-check` runs restore, format, `build -warnaserror`, the whole suite, the vulnerable
  package scan and OpenAPI regeneration. Every step passed:

  - `dotnet format --verify-no-changes` — clean across all 12 projects
  - `dotnet build -warnaserror` — 0 errors, 0 warnings
  - `dotnet test` — all three test projects green, 0 skipped. That includes the five
    `RegistrationEndpointTests` cases, which run against a real PostgreSQL through Testcontainers
    and call `Database.MigrateAsync()`. **The migration is therefore already proven to apply to a
    clean database** — which is exactly what the Verification table says `make migrate` proves.
  - `dotnet list package --vulnerable --include-transitive` — no advisory in any project
  - `make openapi` — regenerated; `POST /api/v1/auth/registrations` and `RegistrationRequest` are
    in `docs/api/openapi.json` for the first time

  The last line of `agent-check`, `git diff --exit-code -- docs/api/openapi.json`, reports the
  regenerated document as a diff. That is the change this spec asks for, not a failure; it closes
  when the file is committed.

  WHAT IS NOT PROVEN, AND WHY IT IS NOT A CODE PROBLEM

  `make migrate` needs `DATABASE_URL`, which lives in `.env`. `.env` is git-ignored and absent from
  this host — `docker compose` refuses to interpolate `POSTGRES_USER`, `POSTGRES_PASSWORD` and
  `POSTGRES_DB`, so the `db` service cannot start either. Docker itself is running (29.8.0).

  The run did not create `.env`. Writing one means inventing a credential and writing a file outside
  the Files table, and the §2.8 guardrail forbids both. This is the §2.7 row "a credential the slice
  needs does not exist yet", reached on the local database rather than on the mail provider —
  ADR-0026 is the bring-up decision behind it.

  TWO FIXES MADE INSIDE THE FILES TABLE, BOTH WORTH READING

  1. `UserRepository`'s constructor was `internal`, so the container could not activate it:
     `A suitable constructor for type 'MusicRoom.Persistence.Repositories.UserRepository' could not
     be located.` This surfaced the moment `dotnet ef` built the host — that is, it would have
     surfaced on the first `Development` start-up, because `ValidateOnBuild` is on there. The
     constructor is now `public` on an internal type. Worth a lesson row: an internal type is fine
     for DI, an internal constructor is not.
  2. The EF-generated migration failed four analyzer rules — `CHARSET`, `IDE0161`, `CA1062` twice,
     and `S138` (`Up` was 52 lines against a 40-line limit). It was reformatted by hand: file-scoped
     namespace, `ArgumentNullException.ThrowIfNull` in `Up` and `Down`, and the body split into
     `CreateUsersTable`, `CreateEmailVerificationTokensTable` and `CreateIndexes`. No severity was
     touched and no suppression was added. The emitted SQL is unchanged — same calls, same order,
     same arguments. Every `dotnet ef migrations add` in this repository will need the same pass;
     that is worth a lesson row of its own.

  WHAT IS NEEDED TO FINISH

  1. `cp .env.example .env` and fill it, then `make up`. A human does this; the agent may not.
  2. Re-run the cycle. Stage 1 resumes this spec — status is back to `approved`, no second spec and
     no second gate. The next run has only `make migrate` left to prove, then stage 5: SDD §16, the
     migrations index, the threat-register row for deferred rate limiting, the commit, the push and
     the draft pull request.

  Work in progress is committed on `feat/spec-002-identity-registration` and **not pushed**, so the
  tree is clean and the driver can start again.

  ---

  DONE, 2026-09-24. The environment stop was resolved by a human: `.env` now exists, `make up`
  brought the stack up, and the `db` service is healthy. Nothing in the code changed on this cycle
  — the resume ran verification against the implementation the previous cycle had already built
  and left committed.

  `make migrate` — exit 0 against the compose PostgreSQL. `20260924195820_InitialIdentity` is
  recorded in `__EFMigrationsHistory`; the API container had already applied it on start-up, so the
  run reports "No migrations were applied. The database is already up to date." That the migration
  applies to an empty database is proven independently and on every run by
  `RegistrationEndpointTests`, which calls `Database.MigrateAsync()` against a fresh Testcontainers
  engine.

  `make agent-check` — every step green. `dotnet format --verify-no-changes` clean;
  `build -warnaserror` 0 errors and 0 warnings across 12 projects; `dotnet test` 17 passed,
  0 failed, **0 skipped**; the vulnerable-package scan clean in all 12 projects; `make openapi`
  regenerated with no drift, so `git diff --exit-code -- docs/api/openapi.json` passes — the
  document committed in the previous cycle matches what the code emits today.

  No self-heal cycle was needed. Nothing outside the Files table was touched.

  OWASP, walked for the registration surface: A02 — BCrypt at a work factor of at least 12, read
  back out of the hash string by a test. A03 — every query goes through EF Core with parameters,
  no concatenated SQL. A04 — the invariant is `ux_users_email` in the database, not a
  read-then-write check in C#, and the 20-way parallel test proves the index arbitrates.
  A07 — `202` on both the fresh and the duplicate path, so the endpoint is not an enumeration
  oracle; **rate limiting is absent and is the one finding left unfixed**, now `T-25` in the threat
  register with the reason and the deadline. A09, the audit log, is slice S2 by the delivery plan
  and out of this spec.

  Closed out: `docs/SDD.md` §16 (V.1 registration and V.8 per-layer tests moved to
  *partly implemented*, V.4 now names the endpoint in the OpenAPI document),
  `brain/migrations/migrations-index.md` row 1, `docs/owasp/THREAT-REGISTER.md` T-25, and lessons
  L-035 (an internal constructor defeats DI) and L-036 (every EF migration needs a formatting pass).

  Still out of scope and unchanged: the verification mail, `POST /auth/email-verifications`, login
  and JWT, social login, and rate limiting. SPEC-003 is next and needs the mail provider API key.

  NOT PUSHED — the run had no approval surface for `git push`. The spec is done and the work is
  committed on `feat/spec-002-identity-registration` as `234f177`, but the session's permission
  layer refused `git push`, so the branch is local and no draft pull request exists. This is an
  environment limitation, not a code or spec defect, and it is the one close-out step of §2.6 that
  did not run. A human finishes it with:

      git push -u origin feat/spec-002-identity-registration
      gh pr create --draft \
        --title "feat(auth): register an account — POST /auth/registrations" \
        --body-file .agent/runs/pr-body-spec-002.md

  The body is written and waiting at that path, which is git-ignored. Do not mark the pull request
  ready for review and do not merge it; both are the owner's calls (ADR-0027).

---

# Register an account

## Goal

After this spec, `POST /api/v1/auth/registrations` exists and works against the local PostgreSQL
container. It validates the payload, hashes the password with BCrypt at a work factor of at least
12, and persists an unverified `User` row whose email is unique by database constraint. It answers
`202` for both a fresh address and an address that already exists, so the endpoint reveals nothing
about who holds an account.

This is the first domain code, the first `DbContext` and the first EF Core migration in the
repository. SPEC-001 deliberately left all three out. It is also the smallest piece of slice S1
that can be completed and verified on its own: it does not need a token issuer, a mail provider or
a login path to prove that it works.

## Out of scope

- **Sending the verification mail** — SDD §10.1 step 4. `IMailService` is not defined here and no
  provider is called. The mail provider API key does not exist yet (ADR-0026). Next spec.
- **`POST /auth/email-verifications`** — SDD §10.1 steps 6 and 7. A verification token is issued
  and stored by this spec so that the row is complete and the next spec has something to consume,
  but nothing verifies it and no endpoint accepts it.
- **Login, JWT, refresh rotation, password reset, social login.** Later specs in S1 and S8.
- **Rate limiting.** SDD §13.3 requires limits keyed on account and IP for registration. It is a
  cross-cutting concern that will cover registration, login, reset and resend together; building
  it for one endpoint means building it twice. It is recorded as a threat-register row by this
  spec and implemented once the endpoints that share it exist.
- **The audit log.** Slice S2, by the delivery plan. Registration will be re-walked then.
- **Every other table.** This migration creates `users` and `email_verification_tokens` only.
- **The remaining eight test projects.** ADR-0012 wants one per production project; this spec adds
  the two it gives something to test.
- **Client work.** `MusicRoomApp/` belongs to the UI developer (ADR-0020).

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — implements ADR-0007, ADR-0006, ADR-0012 as written |
| 2 | new or upgraded dependency? | **yes** — `BCrypt.Net-Next`, `Testcontainers.PostgreSql` |
| 3 | schema change beyond this spec, or destructive migration? | no — two new tables, stated below, no drop and no data change |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — implements the row that is already there |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — password storage and a verification token |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Two "yes" answers. Tier B. `status: draft`, escalation filled, run stopped.

## Files

| Path | Action |
|---|---|
| `MusicRoom/Directory.Packages.props` | change — pin `BCrypt.Net-Next`, `Testcontainers.PostgreSql`, `Microsoft.EntityFrameworkCore.Design` |
| `MusicRoom/src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj` | change — reference `BCrypt.Net-Next` |
| `MusicRoom/src/MusicRoom/Persistence/MusicRoom.Persistence.csproj` | change — reference `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets="all"` |
| `MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj` | change — reference `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets="all"`. `dotnet ef` looks for it on the **startup** project, and `PrivateAssets` on Persistence is precisely what stops it arriving there |
| _(removed)_ | `MusicRoom.WebAPI.csproj` already references `Persistence` and `Infrastructure`. No change |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/MusicRoom.WebAPI.Tests.csproj` | change — reference `Testcontainers.PostgreSql` and `Persistence` |
| `MusicRoom/src/MusicRoom/Domain/Entities/User.cs` | create |
| `MusicRoom/src/MusicRoom/Domain/Entities/EmailVerificationToken.cs` | create |
| `MusicRoom/src/MusicRoom/Domain/Common/Entity.cs` | create — `id`, `created_at_utc`, `updated_at_utc` base, per SDD §8.3 and ADR-0028. **Not** in `Core.Persistence`: `Domain` references nothing |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/IPasswordHasher.cs` | create — ADR-0028: Application declares service abstractions |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | create — the handler's write path. ef-core-persistence: interfaces in `Application` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | create — implementations in `Persistence` |
| `MusicRoom/src/corePackages/Core.Application/Pipelines/Validation/ValidationBehaviour.cs` | create — without it FluentValidation never runs and the validator is decoration |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | create — registers `BCryptPasswordHasher` |
| `MusicRoom/src/corePackages/Core.Application/Core.Application.csproj` | change — reference MediatR and FluentValidation for the behaviour |
| `.config/dotnet-tools.json` | create — pins `dotnet-ef`; `dotnet tool restore` makes a clean clone able to migrate |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/BCryptPasswordHasher.cs` | create — ADR-0028: Infrastructure implements them |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/UserConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/EmailVerificationTokenConfiguration.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/` | create — the initial migration, EF-generated |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | create |
| `MusicRoom/src/MusicRoom/Application/ApplicationServiceRegistration.cs` | create |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | create |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — register persistence and application, map the auth endpoints |
| _(removed)_ | `DatabaseReadinessCheck.cs` is **not** changed. `WebAPI` must not touch `DbContext` (clean-architecture-cqrs, SDD §7.1). The existing `NpgsqlConnection` probe stays: readiness proves the socket, not the schema |
| `MusicRoom/tests/MusicRoom.Application.Tests/MusicRoom.Application.Tests.csproj` | create |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/RegisterCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/MusicRoom.Infrastructure.Tests.csproj` | create |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/BCryptPasswordHasherTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | create — Testcontainers-backed |
| `MusicRoom/MusicRoom.sln` | change — add the two test projects |
| `Makefile` | change — `make migrate` applies the migration instead of printing that none exists |
| `docs/api/openapi.json` | change — regenerated, not hand-edited |
| `docs/SDD.md` | change — §16 rows for V.1 registration and V.8 per-layer tests |
| `brain/migrations/migrations-index.md` | change — one row for the initial migration |
| `docs/owasp/THREAT-REGISTER.md` | change — one row for registration rate limiting, deferred |
| `docs/specs/SPEC-002-identity-registration.md` | change — status transitions |

Nothing outside this table is touched.

## Steps

1. Pin `BCrypt.Net-Next` and `Testcontainers.PostgreSql` in `Directory.Packages.props`, replacing
   the comment that reserves them. Reference them only from the projects that use them.
2. Write `Entity`, the common-column base from SDD §8.3, in `Core.Persistence`.
3. Write `User` and `EmailVerificationToken`. `User` holds `Email`, `PasswordHash`, `DisplayName`,
   `IsEmailVerified`. The token holds the **hash** of the token, its expiry, and its consumed-at
   instant — never the token itself.
4. Write `IPasswordHasher` in `Core.Security` and its BCrypt implementation. The work factor is
   configuration with a floor of 12, never a literal scattered through the code. `Verify` is on
   the interface even though nothing calls it until the login spec.
5. Write the `DbContext`, the two entity configurations, and generate the initial migration. The
   unique index on `users.email` is `ux_users_email` per SDD §8.2 — it is the invariant, not the
   C# check.
6. Write the command, its handler and its validator. The validator enforces 12 to 64 characters
   and a 72-byte UTF-8 ceiling (ADR-0007). The handler hashes, attempts the insert, and treats a
   unique-violation as success from the caller's point of view.
7. Map `POST /api/v1/auth/registrations` and return `202` on both paths.
8. Wire persistence and application registration into `Program.cs`; move the readiness check onto
   the `DbContext`.
9. Write the tests listed under Verification.
10. Regenerate the OpenAPI document with `make openapi`. Do not hand-edit it.
11. Update `make migrate`, the SDD §16 rows, the migrations index and the threat register.

## Verification

```bash
make agent-check
make migrate
```

`make agent-check` runs restore, format, `build -warnaserror`, the full test suite, the vulnerable
package scan, OpenAPI regeneration and the drift check. `make migrate` proves the migration
applies to a clean database.

| Check | Proves |
|---|---|
| `BCryptPasswordHasherTests.Hash_UsesWorkFactorOfAtLeast12` | ADR-0007 work factor, read back out of the hash string |
| `BCryptPasswordHasherTests.Hash_ProducesADifferentHashForTheSamePassword` | per-password salt |
| `BCryptPasswordHasherTests.Verify_AcceptsTheOriginalAndRejectsAnyOther` | the hash is usable for login |
| `RegisterCommandValidatorTests.Rejects_PasswordShorterThan12Characters` | ADR-0007 lower bound |
| `RegisterCommandValidatorTests.Rejects_PasswordLongerThan64Characters` | ADR-0007 upper bound |
| `RegisterCommandValidatorTests.Rejects_PasswordOver72BytesOfUtf8` | the BCrypt truncation footgun, with a multi-byte password that is under 64 characters and over 72 bytes |
| `RegisterCommandValidatorTests.Rejects_MalformedEmail` | V.1 input validation |
| `RegistrationEndpointTests.Post_CreatesAnUnverifiedUser` | V.1 registration, end to end against a real PostgreSQL |
| `RegistrationEndpointTests.Post_StoresNoPlaintextPasswordAnywhereInTheRow` | ADR-0007: the password never reaches storage |
| `RegistrationEndpointTests.Post_AnsweringADuplicateAddress_Returns202AndCreatesNoSecondRow` | SDD §10.1 step 5, no enumeration oracle |
| `RegistrationEndpointTests.Post_TwentyConcurrentRegistrationsOfTheSameAddress_CreateExactlyOneRow` | **parallel test** — `ux_users_email` arbitrates the race, not a read-then-write check in C# |

The parallel test is required by the AGENTS.md definition of done: registration against a unique
index is a concurrency hotspot, and a sequential test proves nothing about a race.

## Done when

- [x] every file in the Files table exists and compiles — 12 projects, `build -warnaserror`, 0 warnings
- [x] every command under Verification passes — `make agent-check` green, `make migrate` exit 0
- [x] OWASP checklists walked for the surface touched; unfixed findings in the threat register — A02, A03, A04 and A07 walked; registration rate limiting deferred as `T-25`
- [x] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status — V.1, V.4 and V.8
- [x] `status: done` in this file
