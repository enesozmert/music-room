---
kind: adr
id: ADR-0012
status: Accepted
date: 2026-09-24
---

# ADR-0012 — xUnit, NSubstitute, Testcontainers

## Context

Subject V.8 requires tests per layer. Issue O3 left the framework assumed rather than decided.

## Decision

| Concern | Tool |
|---|---|
| Test framework | **xUnit** |
| Test doubles | **NSubstitute** |
| Database-backed tests | **Testcontainers for .NET**, real PostgreSQL |
| API integration | `WebApplicationFactory` |
| Assertions | xUnit built-ins; a fluent assertion library may be added later if it earns its place |

## Consequences

- xUnit is the de-facto .NET default, so Microsoft's ASP.NET Core integration-testing
  documentation applies without translation.
- NSubstitute's API is quieter than Moq's at call sites, which matters when handlers are thin and
  tests are many.
- **Testcontainers means Docker must be running to test.** Accepted: Docker is already required
  by [[ci-cd-containerization]], and the alternative — the EF Core in-memory provider — does not
  enforce unique indexes or concurrency tokens, which are exactly the guarantees this project is
  graded on. See [[testing]].
- Container-backed tests are slower. Keep them for persistence, API and realtime layers; domain
  and application unit tests stay in-process and fast.
- Test projects are exempt from the method-count and relaxed on the length limits in
  `.editorconfig`; the security analyzer rules still apply.

## Alternatives rejected

- **Moq** — more widely known, noisier API, and its 2023 telemetry episode left teams wary.
- **NUnit** — richer parameterised testing, thinner ASP.NET Core documentation.
- **EF Core in-memory provider** — cannot prove the constraints that matter here.

Closes issue O3.
