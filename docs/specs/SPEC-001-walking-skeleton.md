---
id: SPEC-001
title: Stand up the backend walking skeleton — solution, packages, compose, Makefile, CI, health, OpenAPI
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S0              # which DELIVERY-PLAN slice this belongs to
requirements: [IV.1, V.4, V.8]
decisions: [ADR-0011, ADR-0021, ADR-0026, ADR-0015, ADR-0012, ADR-0009, ADR-0020, ADR-0022]
standards: [ci-cd-containerization, dotnet-csharp, clean-architecture-cqrs, openapi-documentation, testing, secrets-management, logging-observability]
approved_by: Claude Opus 5, recording the repository owner's explicit Tier B approval on 2026-09-24
escalation: |
  Approval resolved, implementation complete. Gate 2 (dependency) and gate 5 (secrets) were both
  "yes"; the repository owner reviewed both and approved on 2026-09-24, including the one package
  beyond the ADR-0021 list (Microsoft.Extensions.ApiDescription.Server).

  One follow-up is owed to a decision, and it is not this spec's to make. ADR-0009 says
  ".editorconfig at the repository root is the single authority. Severities live there." That is
  now only half true: SonarAnalyzer reads severities from .editorconfig but rule PARAMETERS only
  from a SonarLint.xml AdditionalFile, so the thresholds moved to MusicRoom/SonarLint.xml. Until
  they did, every `sonar.csharp.*` line in .editorconfig was inert and S138 fired at its own
  default of 80 lines rather than the 40 the ADR states. ADR-0009's Enforcement section needs one
  sentence naming the split. Owner's call, not the agent's.
---

# Stand up the backend walking skeleton

## Goal

`MusicRoom/` is empty today. After this spec, a fresh clone of the repository reaches a running,
containerised API in three documented commands — `cp .env.example .env`, fill values, `make up` —
and that API answers `GET /health` and `GET /health/ready` against a local PostgreSQL container.
The solution has the layout of [[ADR-0011-solution-layout]], every package version is pinned once
in `Directory.Packages.props` per [[ADR-0021-backend-dependency-set]], the analyzer limits of
[[ADR-0009-code-quality-gates]] fail the build when violated, GitHub Actions runs the full gate on
every pull request in a `dev` and a `prod` configuration per [[ADR-0015-hosting-and-deployment]],
and `docs/api/openapi.json` is generated from the code rather than written by hand.

Nothing about the product works yet. What works is the machine that will carry every later slice,
built while there is nothing to break — [[delivery-plan]] Phase 4, slice S0, and Phase 5.

## Out of scope

- **Any domain type, entity, command, query or handler.** The nine production projects exist as
  compiling shells. `Domain`, `Application`, `Persistence`, `Infrastructure` and the four
  `corePackages` projects contain no `.cs` file at the end of this spec except where the health
  check needs one. Filling them is S1 onward.
- **No `DbContext`, no EF Core migration, no table.** The readiness check opens a connection and
  closes it. The first migration belongs to S1 — see [[database-design-migrations]].
- **No authentication, no JWT, no BCrypt, no user.** Both health endpoints are anonymous, which is
  what `docs/api/ENDPOINTS.md` already states for them.
- **No MediatR pipeline behaviour, no FluentValidation validator, no Serilog enricher.** Their
  packages are pinned, and `Program.cs` registers the MediatR assembly scan and Serilog so the
  wiring is proven, but no behaviour or validator is written.
- **One test project only** — `MusicRoom.WebAPI.Tests`. [[ADR-0012-test-stack]] requires one test
  project per production project; the remaining eight arrive with the slice that gives their
  project something to test, rather than as eight empty projects that make `dotnet test` look
  busier than it is.
- **No Testcontainers test.** The API smoke test uses `WebApplicationFactory` with the readiness
  check stubbed. The first container-backed test arrives with the first persistence code, in S1.
- **No registry push and no deployment.** [[ADR-0015-hosting-and-deployment]] wants the image built
  in CI so it is proven buildable, and pushed so a future deploy ADR has something to deploy. This
  spec builds it. Pushing needs a registry and a token that do not exist yet, and that decision is
  the deferred deployment ADR's, not this spec's.
- **No Supabase.** S0 to S3 run on the local container ([[ADR-0026-environment-bring-up]]).
- **No OpenTelemetry exporter configuration beyond the off-by-default switch** already named in
  `.env.example` ([[ADR-0022-observability-stack]]).
- **No client work.** `MusicRoomApp/` is the UI developer's root ([[ADR-0020-monorepo-layout]]).

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **no** — but read the note |
| 2 | new or upgraded dependency? | **yes** |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

### Why each answer

**1 — no, with one note.** Every element of this spec is already decided: layout by ADR-0011,
packages by ADR-0021, local database by ADR-0026, CI shape by ADR-0015, test stack by ADR-0012,
analyzer limits by ADR-0009. Nothing here contradicts an accepted ADR. The one ADR change S0 owed
— the MediatR licence verification that ADR-0021 demanded be recorded before the package is added,
issue O15 — **was made on 2026-09-24, before this spec was written**, and its outcome is positive:
the free Community License covers a 42 coursework project with no revenue, no outside capital and
no production deployment, so MediatR stays and the hand-written dispatcher fallback is not needed.
The record, its criteria and the two conditions that would invalidate it are in ADR-0021's
Consequences. No file in the Files table below changes an ADR.

**2 — yes, unavoidably.** This is the slice where every backend package is introduced. The set
itself is fixed by ADR-0021 and needs no new decision, but pinning a version is still adding a
dependency, and two items need a human eye:

- `Microsoft.Extensions.ApiDescription.Server` is **not on the ADR-0021 list**. ADR-0021 says the
  built-in generator "can be emitted to `docs/api/openapi.json` at build time"; that build-time
  emission is what this Microsoft-owned, build-only package provides. The alternative is to start
  the API in CI and `curl /openapi/v1.json`, which works but makes a documentation artifact depend
  on a running container. The spec takes the package. **A reviewer may reject it and take the curl
  path instead**; the rest of the spec is unaffected either way.
- `SonarAnalyzer.CSharp` and `Microsoft.CodeAnalysis.NetAnalyzers` are named by ADR-0009 but were
  never pinned, because no `.csproj` existed to pin them in.

**3 — no.** No `DbContext`, no entity, no migration, no destructive operation. The compose
container starts with an empty database and that is the correct end state for S0.

**4 — no.** `GET /health` and `GET /health/ready` are already in `docs/api/ENDPOINTS.md` (lines
288–289) with their status codes and `anon` authorisation. This spec implements that contract
rather than changing it. `docs/api/openapi.json` is created for the first time, which the
`openapi-documentation` standard requires; that file is generated output, not the contract document
the gate asks about.

**5 — yes, on secrets only.** No authentication, authorisation, token or audit code is written —
those are S1 and S2. But this spec creates the path a real credential travels: `.env` to
`docker compose` to `DATABASE_URL` in the container, and the GitHub Actions configuration that must
never print it. Three specific risks a reviewer should check rather than assume:

- no default password and no fallback connection string in `appsettings.json`, in
  `docker-compose.yml`, or in `Program.cs` — a missing `DATABASE_URL` must fail loudly at startup;
- `.dockerignore` must exclude `.env` so no credential reaches an image layer
  ([[ci-cd-containerization]], [[secrets-management]]);
- `/health/ready` must report only reachable or not reachable. No server version, no host, no
  connection string, no exception text.

**6 — no.** No severity is lowered anywhere, no `#pragma warning disable` is introduced, no test is
skipped. The point of S0 is that the analyzers start working.

**7 — no.** `reference/nArchitecture` is read for its shape, which ADR-0011 already did. Not one
file is copied out, no project there is referenced or built, and no version is taken from it — it
is net6 with stale packages. The directory is git-ignored and stays untouched.

## Files

Exact paths, created or changed. An unattended run must not touch anything outside this list.

### Repository root

| Path | Action |
|---|---|
| `Makefile` | create |
| `docker-compose.yml` | create |
| `.github/workflows/backend-dev.yml` | create |
| `.github/workflows/backend-prod.yml` | create |
| `docs/api/openapi.json` | create — generated, committed |
| `docs/SDD.md` | change — §16 rows for IV.1, V.4, V.8 only |
| `brain/project-registry.md` | change — backend status line, `planned, not scaffolded` to scaffolded |
| `docs/specs/SPEC-001-walking-skeleton.md` | change — `status`, `approved_by` |

`.editorconfig`, `.env.example` and `.gitignore` already exist and are correct for S0. They are
deliberately **not** in this table.

### Solution and build configuration

| Path | Action |
|---|---|
| `MusicRoom/MusicRoom.sln` | create |
| `MusicRoom/Directory.Build.props` | create |
| `MusicRoom/Directory.Packages.props` | create |
| `MusicRoom/Dockerfile` | create — multi-stage, build context is `MusicRoom/` |
| `MusicRoom/.dockerignore` | create |

### Cross-cutting packages — ADR-0011

| Path | Action |
|---|---|
| `MusicRoom/src/corePackages/Core.Application/Core.Application.csproj` | create |
| `MusicRoom/src/corePackages/Core.CrossCuttingConcerns/Core.CrossCuttingConcerns.csproj` | create |
| `MusicRoom/src/corePackages/Core.Persistence/Core.Persistence.csproj` | create |
| `MusicRoom/src/corePackages/Core.Security/Core.Security.csproj` | create |

Four compiling, empty projects. A type enters `corePackages` only when it is genuinely
domain-agnostic — ADR-0011 — and nothing qualifies yet.

### Application projects — ADR-0011

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/MusicRoom.Domain.csproj` | create |
| `MusicRoom/src/MusicRoom/Application/MusicRoom.Application.csproj` | create |
| `MusicRoom/src/MusicRoom/Persistence/MusicRoom.Persistence.csproj` | create |
| `MusicRoom/src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj` | create |
| `MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj` | create |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | create |
| `MusicRoom/src/MusicRoom/WebAPI/appsettings.json` | create — no secret, no connection string |
| `MusicRoom/src/MusicRoom/WebAPI/appsettings.Development.json` | create — OpenAPI UI on |
| `MusicRoom/src/MusicRoom/WebAPI/Properties/launchSettings.json` | create |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/HealthEndpoints.cs` | create |
| `MusicRoom/src/MusicRoom/WebAPI/HealthChecks/DatabaseReadinessCheck.cs` | create |

Folder names mirror the reference tree, per ADR-0011. **Project file names do not**: the reference
uses bare `Domain.csproj` because it carries two demo projects under one solution, and a bare
`Application` assembly name in a real product is a collision waiting to happen. Assemblies are
`MusicRoom.*`. This is the only deliberate divergence in the layout, and it changes no path.

### Tests — ADR-0012

| Path | Action |
|---|---|
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/MusicRoom.WebAPI.Tests.csproj` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HealthEndpointTests.cs` | create |

## Files — amendments made during the run

Six paths outside the table above were touched. Each is recorded here rather than folded in
silently, because the table is the contract an unattended run is judged against.

| Path | Action | Why it could not be avoided |
|---|---|---|
| `global.json` | create | The .NET 10 SDK removed the VSTest path. xUnit v3 runs on Microsoft.Testing.Platform, and `dotnet test` only opts into it through `{"test": {"runner": "Microsoft.Testing.Platform"}}` in this file. Without it no test runs at all. It also pins the SDK, which IV.1 wants anyway |
| `MusicRoom/SonarLint.xml` | create | The only place SonarAnalyzer reads its numeric thresholds from. Chosen by the owner over amending ADR-0009's numbers |
| `.editorconfig` | change | The `sonar.csharp.*` keys were proven inert. Left in place they read as enforcement that is not happening, which is worse than no line at all. Replaced with a pointer to SonarLint.xml; no severity changed |
| `.env.example` | change | `docker compose` cannot configure the `db` container from `DATABASE_URL`; it needs `POSTGRES_USER`, `POSTGRES_PASSWORD` and `POSTGRES_DB`. Without these three lines the IV.1 path — clone, `cp .env.example .env`, fill, `make up` — does not work, which is the requirement this slice exists to satisfy |
| `docs/owasp/THREAT-REGISTER.md` | change | The Done-when list requires unfixed findings to land here before the commit. The table above omitted the file |
| `MusicRoom/Dockerfile` | (in table) | Copies `SonarLint.xml` into the build stage; without it the image build fails with `CS2001: Source file '/src/SonarLint.xml' could not be found` |

Three packages named in Steps step 3 were not pinned after all: `Microsoft.NET.Test.Sdk` and
`xunit.runner.visualstudio` are the VSTest bridge the .NET 10 SDK removed, and xUnit v3 carries its
own runner. `BCrypt.Net-Next` and `Testcontainers` remain unpinned until S1, as planned.

## Deviations from the Steps, and why

- **Health endpoints are minimal APIs, not `MapHealthChecks`.** A health-check middleware route
  carries no API description, so `openapi.json` came out with `"paths": { }` — the V.4 check would
  have passed on an empty document. They now run `HealthCheckService` directly and appear in the
  document with summaries and both status codes.
- **OpenAPI generation is behind `-p:GenerateOpenApiDocument=true`, off by default.** The
  generator boots the real host, and the host refuses to start without `DATABASE_URL`. Making
  generation the default would make every `dotnet build` depend on a variable it does not need.
  `make openapi` and the CI step pass a placeholder connection string; nothing connects, because
  only a request to `/health/ready` opens a socket.
- **No top-level statements.** `.editorconfig` requires file-scoped namespaces and a named
  namespace for every type; both conflict with types declared after top-level statements.
  `MusicRoom.WebAPI.Program.Main` is written out.
- **Test names carry an `Async` suffix** — `Get_Health_ReturnsOkAsync`. The `.editorconfig` async
  naming rule is an error-severity build gate; the rest of each name is the testing standard's
  `Method_Scenario_ExpectedResult`.
- **No interactive OpenAPI UI.** .NET 10 ships no built-in UI and no UI package is on the ADR-0021
  list. The raw document is served in every environment; the UI needs a decision first.
- **`dotnet publish` in the Dockerfile keeps its restore.** With `--no-restore` it failed with
  `NETSDK1064: Package Microsoft.AspNetCore.OpenApi, version 10.0.12 was not found`.

## Steps

1. **Create the solution skeleton.** `MusicRoom.sln`, then the nine production projects and the one
   test project at the exact paths in the Files table. Target `net10.0` everywhere.
2. **Write `Directory.Build.props`.** `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`,
   `GenerateDocumentationFile` — the last one so `openapi-documentation` gets its descriptions from
   the source. Reference `SonarAnalyzer.CSharp` and `Microsoft.CodeAnalysis.NetAnalyzers` here, as
   `PrivateAssets="all"`, so the ADR-0009 limits apply to every project without being repeated.
   Relax the length and method-count limits for `tests/**` only, as ADR-0012 allows; the security
   rules stay.
3. **Write `Directory.Packages.props`.** `ManagePackageVersionsCentrally`, and one `PackageVersion`
   per item in the table below. Resolve the current stable version of each at pin time and record
   what was pinned in the commit body. **Do not copy a version from `reference/`.**

   | Package | Why | Source |
   |---|---|---|
   | `MediatR` | CQRS dispatch | ADR-0021, licence verified 2026-09-24 |
   | `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` | validation | ADR-0021 |
   | `Microsoft.AspNetCore.OpenApi` | OpenAPI document | ADR-0021 |
   | `Microsoft.Extensions.ApiDescription.Server` | build-time emission to `docs/api/openapi.json` | **beyond ADR-0021 — gate 2** |
   | `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL provider; brings `Npgsql`, used by the readiness check | ADR-0021, ADR-0026 |
   | `Serilog.AspNetCore` | structured JSON to stdout | ADR-0022 |
   | `OpenTelemetry.Extensions.Hosting` + the ASP.NET Core and OTLP exporter packages | traces, exporter off by default | ADR-0022 |
   | `SonarAnalyzer.CSharp`, `Microsoft.CodeAnalysis.NetAnalyzers` | the size and complexity limits | ADR-0009 |
   | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `Microsoft.AspNetCore.Mvc.Testing` | test stack | ADR-0012 |

   `BCrypt.Net-Next` and `Testcontainers` are on the ADR-0021 list but have no consumer until S1.
   Pin them when the code that uses them lands, not before — an unused pinned version is a version
   nobody checks.
4. **Enforce the dependency direction.** `ProjectReference` edges only: `Application` to `Domain`;
   `Persistence` to `Application`; `Infrastructure` to `Application`; `WebAPI` to `Application`,
   `Persistence`, `Infrastructure`. `Domain` references nothing — [[clean-architecture-cqrs]].
5. **Write `Program.cs`.** Serilog first so startup failures are structured. Read `DATABASE_URL`
   from the environment and **throw at startup if it is missing or empty** — no fallback, no
   default. Register the MediatR assembly scan, FluentValidation, rate limiting
   (`Microsoft.AspNetCore.RateLimiting`, built in), OpenTelemetry with the exporter gated on
   `OTEL_EXPORTER_ENABLED`, health checks, and the OpenAPI document. Map the interactive OpenAPI UI
   in `Development` only. Keep it under the 40-line method limit by grouping registrations into
   extension methods.
6. **Write the health endpoints.** `GET /health` returns `200` with a minimal body and touches no
   dependency — it answers "is the process alive". `GET /health/ready` runs
   `DatabaseReadinessCheck`, which opens an `NpgsqlConnection` with a short timeout, closes it, and
   maps success to `200` and failure to `503`. The body names the check and its state and **nothing
   else** — no exception text, no host, no server version. Both are anonymous and both sit outside
   `/api/v1`, exactly as `docs/api/ENDPOINTS.md` has them.
7. **Write `docker-compose.yml`.** Two services. `db`: PostgreSQL pinned by tag, never `latest`, a
   named volume, a `pg_isready` healthcheck, credentials from `.env`. `api`: built from
   `MusicRoom/Dockerfile`, `depends_on` the database being healthy, `DATABASE_URL` passed through
   from `.env`, port published. No secret literal anywhere in the file.
8. **Write `MusicRoom/Dockerfile`.** Multi-stage: SDK image builds and publishes, runtime image
   ships. Pin both base images by tag. Run as a non-root user. The runtime image contains neither
   the SDK nor the source. `.dockerignore` excludes `bin`, `obj`, `.git`, `.env`, `node_modules`
   and `reference`.
9. **Write the `Makefile`.** Exactly the targets `ci-cd-containerization` documents: `up`, `down`,
   `migrate`, `test`, `lint`, `load`. Add `agent-check` — the full local gate, the command every
   later spec's Verification section calls — and `openapi`. `migrate` and `load` have nothing to do
   yet; each must **print what it is waiting on and exit 0**, not fail and not silently pass.
10. **Write the two workflows.** `backend-dev.yml` on pull requests and on pushes to any branch but
    `main`: `dotnet restore`, `dotnet format --verify-no-changes`, `dotnet build -warnaserror`,
    `dotnet test`, `dotnet list package --vulnerable --include-transitive` failing on high
    severity, and OpenAPI generation — in that order, all must pass.
    `backend-prod.yml` on pushes to `main`: the same gate in `Release`, plus `docker build` of the
    compose image to prove it builds. **No push, no deploy** — ADR-0015. The two differ in
    configuration only, never in code path. Paths-filter both to the backend so a client-only
    commit does not run them ([[ADR-0020-monorepo-layout]]).
11. **Generate and commit `docs/api/openapi.json`.** Build-time emission from step 3's package. The
    document at this point describes two health endpoints, and that is correct: it is generated from
    the code, and the code has two endpoints.
12. **Write the smoke test.** `HealthEndpointTests` via `WebApplicationFactory`:
    `/health` returns `200`; `/health/ready` returns `503` when the readiness check is substituted
    with a failing one, and `200` when it succeeds. `NSubstitute` provides the substitute — no real
    database, no container. Test names follow `Method_Scenario_ExpectedResult`.
13. **Walk the OWASP checklists** for the surface actually touched: configuration and secret
    handling, error-response shape, and information disclosure through the readiness body. Write
    every unfixed finding into [[threat-register]] with its residual risk **before the commit**.
14. **Update the two status records honestly.** `docs/SDD.md` §16: IV.1 and V.8 move from designed
    to implemented for the reproducible-clone and CI rows only; V.4's row notes that
    `openapi.json` is now generated and committed, and that it currently documents two endpoints.
    `brain/project-registry.md`: the backend line stops saying `planned, not scaffolded`.
    Do not touch any other row.

## Verification

Commands that must pass, and what each one proves. Include the layer tests the `testing` standard
requires for what is touched.

The root `Makefile` does not exist when this spec starts — this spec creates it. So the gate runs as
raw commands, from `MusicRoom/`:

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
dotnet list package --vulnerable --include-transitive
```

Then, from the repository root, the part no `dotnet` command can prove:

```bash
cp .env.example .env    # then fill DATABASE_URL and the database password
docker compose up -d --build
curl -fsS localhost:8080/health
curl -fsS localhost:8080/health/ready
docker compose down
```

Once step 9 has landed, the same gate is `make agent-check`, and every later spec uses that form.

| Check | Proves |
|---|---|
| `dotnet restore` | every pinned version in `Directory.Packages.props` resolves; central package management is wired |
| `dotnet format --verify-no-changes` | the root `.editorconfig` is the single formatting authority, and CI step 2 will pass |
| `dotnet build -warnaserror` | `TreatWarningsAsErrors` is on and the ADR-0009 analyzers are actually restored and running — an unreferenced analyzer is a limit that exists only in a document |
| `dotnet test` | API integration layer per [[testing]]: routing and status codes for both health endpoints, `WebApplicationFactory` boots the real `Program.cs` |
| `HealthEndpointTests.Get_Health_ReturnsOk` | IV.1 — the application starts and serves, liveness per `ENDPOINTS.md` |
| `HealthEndpointTests.GetHealthReady_WhenDatabaseUnreachable_Returns503` | readiness fails closed rather than reporting ready while the database is down |
| `HealthEndpointTests.GetHealthReady_WhenDatabaseReachable_Returns200` | the check's success path is wired, not merely present |
| `dotnet list package --vulnerable --include-transitive` | no high-severity advisory enters the project with its first packages |
| `docker compose up -d --build` from a fresh clone | **IV.1** — clone, `cp .env.example .env`, fill, `make up`, with no extra manual step. This is the graded requirement, and it is the one check the `dotnet` commands cannot make |
| `curl -fsS localhost:8080/health/ready` returning `200` | the API reaches the local PostgreSQL container over compose networking — ADR-0026's S0-to-S3 database |
| `docs/api/openapi.json` regenerates with no diff after a clean build | **V.4** — the document is generated from the code and committed, not hand-maintained |
| `backend-dev.yml` and `backend-prod.yml` green on the pull request | **V.8** — continuous integration exists before the first real feature branch |
| unset `DATABASE_URL`, then `docker compose up` | startup fails loudly instead of falling back to a default connection string — gate 5 |
| `docker history` on the built image shows no `.env` and no credential | no secret in any image layer — gate 5, [[secrets-management]] |

Two things are deliberately **not** verified here, because they have nothing to act on yet:
`make migrate` (no migration until S1) and `make load` (no simulation until S9). Each prints what it
is waiting on and exits 0.

## Verification — results, 2026-09-24

| Check | Result |
|---|---|
| `dotnet restore` | 10 projects, clean |
| `dotnet format --verify-no-changes` | clean |
| `dotnet build -warnaserror` | 0 errors, 0 warnings |
| `dotnet test --solution` | 4 of 4 passed |
| `dotnet list package --vulnerable --include-transitive` | no advisory in any of the 10 projects |
| `make agent-check` | exit 0 |
| `make up` from `.env.example` alone | db healthy, api started |
| `curl /health` | `{"status":"Healthy"}`, HTTP 200 |
| `curl /health/ready` | `{"status":"Healthy","database":"Healthy"}`, HTTP 200 |
| no `DATABASE_URL` | `Unhandled exception. System.InvalidOperationException: DATABASE_URL is not set.` — fails at start-up, no fallback |
| `.env` or credential in an image layer | none; the container runs as `uid=1654(app)` |
| `docs/api/openapi.json` regenerated | no diff; both health endpoints documented |
| 47-line method | `error S138: This method 'TooLong' has 47 lines, which is greater than the 40 lines authorized` |
| six-parameter method | `error S107: Method has 6 parameters, which is greater than the 5 authorized` |

`dotnet test` takes the MTP form, `dotnet test --solution <path>`. The bare-path form is VSTest
syntax and the .NET 10 SDK rejects it.

## Done when

- [x] every file in the Files table exists and compiles
- [x] every command under Verification passes
- [x] OWASP checklists walked for the surface touched; unfixed findings in the threat register —
      T-24 added (anonymous, unthrottled operational endpoints); T-09, T-15 and T-16 gained their
      first evidence
- [x] `docs/SDD.md` §16 updated honestly — IV.1, V.4 and two V.8 rows; V.8 per-layer tests says
      **partly implemented**, because only the API integration layer exists
- [x] `status: done` in this file

Additionally, because this is S0 and later specs inherit all of it:

- [x] a fresh clone reaches a served `/health` in exactly `cp .env.example .env` → fill → `make up`
- [x] the ADR-0009 limits are proven live — and were **not** when this spec started: the thresholds
      lived in `.editorconfig`, where SonarAnalyzer never read them. Moved to
      `MusicRoom/SonarLint.xml`, then proven with a 47-line method and a six-parameter method
- [x] `make agent-check` is the single command every later spec's Verification section can call
- [x] one branch, one commit, no push — [[ADR-0025-agent-git-protocol]]

## Related

[[delivery-plan]] Phase 4 slice S0 and Phase 5 · [[specs]] · [[endpoint-inventory]] ·
[[ADR-0011-solution-layout]] · [[ADR-0021-backend-dependency-set]] ·
[[ADR-0026-environment-bring-up]] · [[ci-cd-containerization]]
