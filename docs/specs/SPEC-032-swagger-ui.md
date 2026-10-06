---
id: SPEC-032
title: Swagger UI over the document AddOpenApi() already serves, Development only
status: approved        # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S3a              # which DELIVERY-PLAN slice this belongs to
requirements: [V.4]
decisions: [ADR-0035, ADR-0005, ADR-0021]
standards: [openapi-documentation, application-security, rest-api-design, testing]
approved_by: enesozmert # name, or "agent" for a clean Tier A gate
escalation: |
  **Gate 2 tripped: yes.** This unit adds `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3 to
  `MusicRoom.WebAPI`, and `docs/prompt-library/PROMPTS.md` §2.8 forbids an agent adding a dependency on its
  own. The mechanical answer is therefore yes and this stops here, even though the substance is already
  settled: [[ADR-0035-swagger-ui-for-the-openapi-document]] names the package **and** the version, and records
  the owner's requirement of 2026-10-01 — *"Swashbuckle, current version"*. Nothing here chooses anything the
  ADR left open.

  The other six gates answer no. No ADR is written or contradicted; no migration; no change to
  `docs/api/ENDPOINTS.md`, because a Development-only documentation page is not part of the API contract and
  ADR-0035 does not put it there; nothing touches authentication, authorisation, tokens, secrets or the audit
  log; nothing is lowered, deleted or skipped; and nothing comes from `reference/`.

  **One thing the walk found that the ADR assumes and the code does not have.** ADR-0035 says the UI is
  registered *"inside the existing `app.Environment.IsDevelopment()` branch"*. There is no such branch in the
  request pipeline: the only `IsDevelopment()` call is `builder.Environment.IsDevelopment()` at
  `Program.cs:322`, passed as a flag into `InfrastructureSetup`. This unit therefore **creates** the branch it
  was told to register inside. That is one `if` in a file the Files table already lists, and it is named here
  rather than discovered at review.

  **Owner's answer, 2026-10-06: approved.** Gate 2 is a formality here and the walk is right that the substance
  was settled on 2026-10-01: the package is `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3, the UI package alone,
  never the metapackage. Gate 1 also becomes a human-answered **yes**, see answer 2.

  Four answers, binding on the implementation:

  1. **The version is pinned in `MusicRoom/Directory.Packages.props` and the reference carries no `Version`
     attribute.** Central Package Management is on; a `Version` on the reference is NU1008 and a pin anywhere
     else is a version the vulnerable-package audit cannot see.
  2. **Create the Development branch, and amend ADR-0035 in the same unit.** The ADR says the UI goes "inside
     the existing `app.Environment.IsDevelopment()` branch" and the walk found there is no such branch — the
     only `IsDevelopment()` call is `builder.Environment.IsDevelopment()` at `Program.cs:322`, passed into
     `InfrastructureSetup`. A record that asserts something the code does not have is a record that will
     mislead the next reader, so add `brain/decisions/ADR-0035-swagger-ui-for-the-openapi-document.md` to the
     Files table and write one amendment line: the branch did not exist and SPEC-032 created it. That makes
     gate 1 a yes, answered here.
  3. **`app.MapOpenApi()` stays outside the branch.** Only `UseSwaggerUI` goes inside it. The raw document
     answers in every environment today and must keep doing so — that is exactly what ADR-0035 preserved, and
     the third test asserts it on both hosts.
  4. **The Production test host cannot use `TestMailSettings`.** It sets `MAIL_PROVIDER=logging`, which a
     non-Development host refuses by design — T-26, and `MailProviderStartupTests.Startup_OnProductionWithTheLoggingProvider_Throws` proves it. A Production host that boots a full pipeline therefore needs
     `MAIL_PROVIDER=brevo` with a **placeholder** key and sender; no real credential goes in a test, and no
     test may issue a request that sends mail, because no suite reaches a provider or spends its quota (T-19).
     `MailProviderStartupTests` sidesteps this by resolving services instead of serving requests; this unit
     cannot, because it fetches two paths over HTTP.

  **BLOCKED 2026-10-06: the Files table cannot hold this unit, and the two rules that say so contradict each
  other's remedy.**

  `MusicRoom/src/MusicRoom/WebAPI/Program.cs` is the only source file the table authorises, and on `main` it
  sits at **exactly 300 code lines against S104's cap of 300**. The unit needs four more — the
  `if (app.Environment.IsDevelopment())` the owner's second answer asked me to create, the `UseSwaggerUI` call
  inside it, and the braces. The build then fails twice:

  ```
  error S104: This file has 304 lines, which is greater than 300 authorized. Split it into smaller files.
  error CA1506: 'Main', '22' farklı ad alanından '42' farklı türe bağlı. ... '41' düzeyinin altına düşürün.
  ```

  S104's remedy is *"split it into smaller files"*. CA1506's is to move the registration out of `Main` into its
  own class — which is how `ApiServices`, `RealtimeServices`, `Observability` and `InfrastructureSetup` already
  exist. Both remedies need a **new file under `WebAPI`**, and the Files table authorises none. Putting the new
  class inside `Program.cs` satisfies CA1506 and makes S104 worse, because it adds lines to a file already over
  the cap.

  Fix cycles attempted:

  1. Moved the `OpenApiDocumentPath` constant out of the wrong class and inlined the path at the call site, to
     be sure neither error was an artefact of my own slip. Remeasured: still 304 lines and still 42 coupled
     types, so both are structural rather than incidental.
  2. and 3. not attempted, because every remaining fix is one the run forbids: widening the Files table with a
     new `WebAPI` file, extracting existing code out of `Program.cs` into a new file (the same widening),
     lowering S104 or CA1506 in `SonarLint.xml` / `.editorconfig`, or suppressing either with a pragma.

  **The owner's decision, not mine.** Three resolutions, each a one-line change to this spec's Files table:

  - add `MusicRoom/src/MusicRoom/WebAPI/InteractiveDocumentation.cs` — a static class holding the document path
    and a `UseOnDevelopmentOnly(WebApplication)` method. This is the shape the four existing pipeline helpers
    use, it fixes CA1506 by construction, and it leaves `Program.cs` one line **shorter** than it is on `main`
    if the call replaces the comment block it displaces;
  - or add that file **and** move one existing helper class out of `Program.cs` into it, which buys S104
    headroom for the next unit as well — `Program.cs` has been at or near the cap for three units running;
  - or decide that S104's 300 is the wrong number for a composition root and raise it in `SonarLint.xml`, which
    is a standards change and therefore an ADR, not a spec decision.

  Nothing is pushed. The branch `feat/spec-032-swagger-ui` carries the package pin, the package reference and
  the pipeline edit as a `wip(spec-032)` commit, so the next cycle resumes from a clean tree.
---

# Swagger UI

## Goal

After this spec a developer running the local stack can open a page that renders the OpenAPI document the API
already publishes, and nothing changes on any other host. `AddOpenApi()` stays the only document generator,
`GET /openapi/v1.json` keeps answering in every environment as it does today, and `docs/api/openapi.json` is
byte-identical before and after — which is the test that no second generator arrived.

## Out of scope

- **`AddSwaggerGen` and the `Swashbuckle.AspNetCore` metapackage.** ADR-0035's central decision: one API, one
  document, one generator. A second generator means two documents that drift and a CI drift check that can
  only watch one of them.
- **Documenting the UI in `docs/api/ENDPOINTS.md`.** It is a page for two developers and a defence
  walkthrough, not a contract a client codes against.
- **Any change to what the document says.** The UI is a reader. A wrong endpoint in the page is a wrong
  endpoint in the document, which is a wrong attribute in the code — ADR-0035's own wording.
- **Authentication on the page.** It is Development-only, which is the control; an auth flow on a page that
  does not exist outside Development would be protecting nothing.

## Decisions taken under ADR-0035

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **`Swashbuckle.AspNetCore.SwaggerUI` 10.2.3, that package alone.** | ADR-0035, binding. It is the middleware that serves the embedded swagger-ui and nothing else; the metapackage brings `SwaggerGen`, a second document generator. `net10.0` is a target of 10.2.3 and its dependency group for it is empty, so nothing arrives behind it |
| 2 | **Pointed at the existing document**: `options.SwaggerEndpoint("/openapi/v1.json", "MusicRoom v1")`. | The document is already generated, already served and already drift-checked. Pointing the UI at it is what keeps one producer |
| 3 | **Registered inside a Development-only branch, which this unit creates**, because the pipeline has none today. | ADR-0035: an interactive, unauthenticated catalogue of every endpoint is an attack-surface map, and `docs/owasp/API-TOP10-CHECKLIST.md` API8 calls a documentation UI in production the textbook misconfiguration |
| 4 | **A test proves a non-Development host answers `404` on the UI path**, and that the raw document still answers there. | Decision 3 is only true if something holds it true. Without the second half the test would also pass if the document had stopped being served, which is the opposite of what ADR-0035 preserved |
| 5 | **The version is pinned in `Directory.Packages.props`; the csproj carries no `Version` attribute.** | Central Package Management is on for this solution (`ManagePackageVersionsCentrally`), so a `Version` on the reference is an error, and a pin outside the props file is invisible to the audit |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes, answered by the owner** — it implements [[ADR-0035-swagger-ui-for-the-openapi-document]] exactly, and amends one sentence of it: the Development branch the ADR calls existing does not exist, and this unit creates it (binding answer 2) |
| 2 | new or upgraded dependency? | **yes** — `Swashbuckle.AspNetCore.SwaggerUI` 10.2.3 on `MusicRoom.WebAPI`. Chosen and versioned by ADR-0035 and by the owner on 2026-10-01, but an agent may not add one on its own (PROMPTS §2.8) |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — a Development-only page is not part of the contract, and ADR-0035 does not add it there |
| 5 | touches authn, authz, tokens, secrets, audit? | no — it adds no authentication path, reads no secret and writes no audit row. The Development-only registration is what keeps an unauthenticated page off every other host |
| 6 | lowers a severity, deletes or skips a test? | no — it adds one |
| 7 | touches or copies from `reference/`? | no |

Gates 1 and 2 are "yes", so this spec stopped at `draft` with the escalation filled in and no code
written. The owner answered both on 2026-10-06 — see the escalation block, whose four answers are
binding — and the status is now `approved`.

## Files

| Path | Action |
|---|---|
| `MusicRoom/Directory.Packages.props` | change — `<PackageVersion Include="Swashbuckle.AspNetCore.SwaggerUI" Version="10.2.3" />`, the only place a version appears |
| `MusicRoom/src/MusicRoom/WebAPI/MusicRoom.WebAPI.csproj` | change — `<PackageReference Include="Swashbuckle.AspNetCore.SwaggerUI" />`, **no** `Version` attribute |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — a Development-only branch around `app.UseSwaggerUI(...)`, beside `app.MapOpenApi()` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SwaggerUiEndpointTests.cs` | create — the four answers: the page on Development, `404` elsewhere, and the document still served in both |
| `docs/SDD.md` | change — §9.1: the document has a renderer, Development only, one generator still |
| `docs/DELIVERY-PLAN.md` | change — S3a |
| `brain/projects/musicroom-backend.md` | change — S3a |
| `brain/decisions/ADR-0035-swagger-ui-for-the-openapi-document.md` | change — one amendment line: the
Development branch it calls existing did not exist, and SPEC-032 created it (binding answer 2) |
| `docs/specs/SPEC-032-swagger-ui.md` | change — status transitions |

### The Files table walk

**1. Assemblies, and every reference that implies.** `MusicRoom.WebAPI` only — it is the single
`Microsoft.NET.Sdk.Web` project and the only one with a request pipeline. The package reference lands on
`MusicRoom.WebAPI.csproj`; **no other `.csproj` changes**, because no other assembly names anything from the
package. `MusicRoom.WebAPI.Tests` already references `MusicRoom.WebAPI` and reaches the UI over HTTP through
`WebApplicationFactory`, so it needs no reference of its own.

**2. Types crossing an assembly boundary.** None. This unit declares no type at all: `UseSwaggerUI` is an
extension method on `IApplicationBuilder` from the package, called once. Nothing becomes public and no new
file appears under `src/`.

**3. Does anything invoke it?** Yes — `app.UseSwaggerUI(...)` in the pipeline. Worth stating because the
failure mode here is exactly a registration nothing calls: a `PackageReference` with no `UseSwaggerUI` compiles,
ships 2 MB of embedded assets and serves nothing, and only a test that fetches the page would notice.

**4. A CLI, a tool manifest, or an unpinned package?** One unpinned package, which is gate 2. No CLI and no
entry in `.config/dotnet-tools.json`.

**5. Do the four corePackages contain what I assume?** They are assumed to contain **nothing** relevant, and
that is checked rather than guessed: `Core.Application` holds 1 source file (the validation behaviour),
`Core.CrossCuttingConcerns`, `Core.Persistence` and `Core.Security` hold **0** each. None is touched.

**The four answers this unit can go wrong on:**

- **Central Package Management.** The solution sets `ManagePackageVersionsCentrally`, so the version lives in
  `MusicRoom/Directory.Packages.props` and the reference in `MusicRoom.WebAPI.csproj` carries no `Version`
  attribute. Both files are in the table above. A `Version` on the reference is build error NU1008, and a pin
  anywhere else is a version the vulnerable-package audit cannot see.
- **Development only, proven in both directions.** The middleware goes inside a branch this unit creates, and
  `SwaggerUiEndpointTests` asserts four things: on a Development host the UI path answers `200`, on a
  Production host it answers `404`, and `GET /openapi/v1.json` answers `200` on **both**. The last one is what
  stops the test passing for the wrong reason — a host that had stopped serving the document would otherwise
  look like a correctly hidden UI.
- **`docs/api/openapi.json` must not change.** `make agent-check` regenerates the document and fails on any
  diff. A diff here would mean a second generator described the API, which is the one outcome ADR-0035 exists
  to prevent — and it would be a reason to stop, not to commit the new document.
- **What `make agent-check` proves about a new package.** `dotnet list package --vulnerable
  --include-transitive` fails the gate on a known advisory, so the new reference and anything behind it are
  audited rather than trusted; ADR-0035 says 10.2.3's `net10.0` dependency group is empty, and this is the
  check that holds that claim. The PDF guards prove the committed PDFs match their Markdown, which matters
  because this unit edits `docs/SDD.md` and `docs/DELIVERY-PLAN.md`.

## Steps

1. Pin the version in `Directory.Packages.props`; reference the package, without a version, in
   `MusicRoom.WebAPI.csproj`.
2. Wrap `app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "MusicRoom v1"))` in a
   Development-only branch next to `app.MapOpenApi()`, and replace the comment at `Program.cs:90` that says a
   UI "needs a UI package no ADR has chosen yet" — an ADR has now chosen one.
3. `SwaggerUiEndpointTests`: the UI on Development, `404` on Production, the document on both.
4. `make agent-check`, with the OpenAPI drift guard as the load-bearing check.
5. `docs/SDD.md` §9.1, the delivery plan, the project card.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `SwaggerUiEndpointTests.Ui_OnADevelopmentHost_Returns200Async` | the page is actually served, so the package is wired and not merely referenced |
| `…Ui_OnAProductionHost_Returns404Async` | the Development-only registration holds: no endpoint map is published on a host that is not ours |
| `…Document_OnBothHosts_Returns200Async` | the raw document keeps being served everywhere, so the `404` above is a hidden UI and not a broken document |
| `git diff --exit-code -- docs/api/openapi.json`, inside `make agent-check` | **no second generator.** The committed document is unchanged, which is ADR-0035's central constraint |
| `dotnet list package --vulnerable --include-transitive`, inside `make agent-check` | the new package and everything behind it carry no known advisory — and that 10.2.3 brings nothing behind it on `net10.0`, as the ADR claims |
| `dotnet build -warnaserror` | Central Package Management is satisfied: no `Version` on the reference (NU1008) and no unpinned package (NU1604) |
| the existing 537 tests | the pipeline change is additive — nothing else answers differently |
| the PDF guards, inside `make agent-check` | `docs/SDD.pdf` and `docs/DELIVERY-PLAN.pdf` match the Markdown this unit edits |

## Done when

- [ ] every file in the Files table exists and compiles, and nothing outside it is touched
- [ ] the UI answers on Development and `404`s elsewhere, with tests for both
- [ ] `docs/api/openapi.json` is byte-identical to its state before this unit
- [ ] `make agent-check` passes
- [ ] `status: done` in this file
