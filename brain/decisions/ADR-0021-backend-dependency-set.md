---
kind: adr
id: ADR-0021
status: Accepted
date: 2026-09-24
---

# ADR-0021 — Backend dependency set

## Context

[[ADR-0001-backend-dotnet-narchitecture]] fixed the architecture but not the packages. Four
choices were still open (T2, T4, T5, T6), and two of the obvious candidates changed licence terms,
which makes "everyone uses it" a poor reason on its own.

## Decision

| Concern | Choice |
|---|---|
| CQRS dispatch and pipeline behaviours | **MediatR** |
| Request validation | **FluentValidation** |
| Entity to DTO mapping | **none — project with `Select` in the query** |
| Rate limiting | **`Microsoft.AspNetCore.RateLimiting`**, the built-in middleware |
| OpenAPI document | **`Microsoft.AspNetCore.OpenApi`**, the built-in generator |

Every version is pinned once in `Directory.Packages.props`.

## Consequences

- **MediatR moved to a commercial licence** (LuckyPennySoftware) with a free tier for small-scale
  use. A student project falls inside it. **Verify the current terms before adding the package**
  and record the result here — an assumption about licensing is not a licence. If the terms stop
  fitting, the escape is a hand-written dispatcher of roughly 150 lines, because the pipeline
  behaviour pattern is ours, not MediatR's.
- **No mapping library.** `ef-core-persistence` already requires projecting to DTOs inside the
  query rather than loading the entity graph and mapping afterwards, so a mapper would only
  duplicate what `Select` does — while generating wider SQL. This also sidesteps AutoMapper's
  licence change. Cost: hand-written projections are repetitive.
- **Built-in rate limiting** keeps the count of dependencies down and partitions cleanly by
  account and by IP, which is what `authentication-authorization` requires. It stores state
  **in process**: correct for one instance, and it would not be shared across instances. O8 is
  already open for the same reason, so this adds no new constraint.
- **Built-in OpenAPI** is the .NET 9+ official generator: no dependency, and the document can be
  emitted to `docs/api/openapi.json` at build time, which subject V.4 wants readable without
  running the service. Interactive UI is a separate, Development-only concern.
- The reference repository uses Swashbuckle. **Do not copy that choice**; it is net6-era, and the
  built-in generator did not exist then. See [[reference-narchitecture]].

## Alternatives rejected

- **Wolverine** instead of MediatR — permissive licence and faster, but diverges from the
  nArchitecture pattern the team studied, with no compensating benefit here.
- **Hand-written dispatcher from the start** — zero licence risk and a strong defence answer, but
  it is infrastructure the team would have to test instead of building graded features. Kept as
  the fallback, not the default.
- **AutoMapper or Mapperly** — a mapper for projections the query should be doing anyway.
- **AspNetCoreRateLimit** — richer configuration, overlapping with built-in middleware.
- **Swashbuckle or NSwag** — NSwag's TypeScript client generation is genuinely attractive for a
  two-person split, but it is a heavier configuration surface and the UI developer can generate a
  client from `openapi.json` with any tool they prefer.

Closes T2, T4, T5, T6.
