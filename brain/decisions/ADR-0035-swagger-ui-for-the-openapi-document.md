---
kind: adr
id: ADR-0035
status: Accepted
date: 2026-10-01
---

# ADR-0035 — Swagger UI over the existing OpenAPI document

## Context

The API publishes its OpenAPI document and nothing renders it. `Program.cs` calls `AddOpenApi()`
and `MapOpenApi()` — the framework's own generator — so `GET /openapi/v1.json` returns the
document in every environment, and `docs/api/openapi.json` is the committed copy that CI
drift-checks against the code. There is no interactive page: `/swagger`, `/swagger/index.html` and
`/scalar/v1` all answer `404` on the running container, verified 2026-10-01.

That was deliberate rather than forgotten. The comment at `Program.cs:72` says so: *"the raw
document is served in every environment. An interactive UI is a separate, Development-only
concern, and it needs a UI package no ADR has chosen yet."* Subject V.4 asks for "REST, JSON,
reference documentation", and the committed document plus [[endpoint-inventory]] already satisfy
it. A UI is for the people building and demonstrating the thing.

Owner requirement, 2026-10-01: **Swashbuckle, current version.**

## Decision

### The UI package only, not the generator

Reference **`Swashbuckle.AspNetCore.SwaggerUI` 10.2.3** — the middleware that serves the embedded
swagger-ui and nothing else. Not the `Swashbuckle.AspNetCore` metapackage, which also brings
`SwaggerGen`, a second document generator.

Point it at the document that already exists:

```csharp
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "MusicRoom v1"));
```

**One document, one generator.** `AddOpenApi()` stays as the only producer. Adding `SwaggerGen`
would mean two generators describing the same API, two documents that drift from each other, and a
CI drift check that can only watch one of them — the committed `docs/api/openapi.json` would stop
being the contract the moment the two disagreed.

### Development only

The UI is registered inside the existing `app.Environment.IsDevelopment()` branch. The raw document
keeps being served everywhere, as it is today.

- An interactive, unauthenticated catalogue of every endpoint is an attack-surface map. It is also
  the one page an attacker would like most on a host that is not ours.
- Nothing is lost: the audience for the UI is the two developers and the defence walkthrough, and
  both run the local stack ([[ADR-0015-hosting-and-deployment]]).
- `docs/owasp/API-TOP10-CHECKLIST.md` API8 is about security misconfiguration, and a documentation
  UI shipped to production is the textbook example.

### Where it lands

Slice **S3a** in [[delivery-plan]], after S3 and before S4. It is small, it has no dependency on
anything later, and it is most useful while S4 to S7 are being built by hand against the API.

`net10.0` is a target of 10.2.3, and the package's dependency group for it is empty — nothing
comes in behind it.

## Consequences

- **A dependency is added, so this ADR exists.** `brain/decisions/ADR-0021-backend-dependency-set`
  fixes the set, and `docs/prompt-library/PROMPTS.md` §2.8 forbids an agent adding one. That rule
  is why a UI did not simply appear in a spec three weeks ago.
- The pinned version goes in `MusicRoom/Directory.Packages.props` like every other, and
  `dotnet list package --vulnerable` already runs in the gate and now covers it.
- **The UI is a reader, not a source.** It renders whatever `/openapi/v1.json` says. A wrong
  endpoint in the UI is a wrong endpoint in the document, which is a wrong attribute in the code —
  the fix is never in the UI configuration.
- Swashbuckle at 10.x carries `Newtonsoft.Json` in some of its packages; `SwaggerUI` 10.2.3 does
  not, and this ADR is the reason only that package is referenced. If a future need pulls in
  `SwaggerGen`, the dependency question reopens and so does this decision.
- The defence gains a page to drive. Showing a reviewer a live endpoint list is cheaper than
  narrating `curl`.
- Production behaviour does not change. Nothing new is reachable on a non-Development host.

## Alternatives rejected

- **The `Swashbuckle.AspNetCore` metapackage with `AddSwaggerGen`.** The familiar setup, and it
  puts a second generator in the process. Two documents for one API, and the committed one is the
  contract the client developer reads.
- **Scalar (`Scalar.AspNetCore`).** Built for exactly this pairing with `MapOpenApi`, and a lighter
  dependency. Rejected by the owner, who asked for Swashbuckle.
- **No UI, paste the JSON into editor.swagger.io.** What happens today. It works and it costs a
  manual step every time, which is why it kept not happening.
- **The UI in every environment.** One line shorter and it publishes a map of the whole API on any
  host the project is ever deployed to.

## Affected

Standards: `openapi-documentation`. Design: [[sdd]] §9.1, §13.
Plan: [[delivery-plan]] S3a. Related: [[ADR-0005-api-style-rest-json]],
[[ADR-0021-backend-dependency-set]], [[ADR-0015-hosting-and-deployment]].
