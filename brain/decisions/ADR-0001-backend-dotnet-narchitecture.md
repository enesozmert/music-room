---
kind: adr
id: ADR-0001
status: Accepted
date: 2026-09-23
---

# ADR-0001 — .NET 10 backend with Clean Architecture + CQRS

## Context

The subject leaves the backend stack free but demands justification. The team already
vendored `reference/nArchitecture`, a Clean Architecture teaching repo, and dotnet 10.0.401
is installed locally. The system needs REST, realtime, OAuth, and a relational model.

## Decision

Build the backend in C# on .NET 10, ASP.NET Core Web API, structured as five projects
(Domain, Application, Persistence, Infrastructure, WebAPI) with MediatR-style CQRS and
pipeline behaviours, following the layer split of nArchitecture.

## Consequences

- Existing team familiarity with the reference layout shortens the scaffolding phase.
- SignalR comes from the same framework, so realtime needs no second runtime ([[ADR-0004-realtime-signalr]]).
- Five projects for a school project is real overhead; it is accepted because the subject
  grades defended architecture, not minimal line count.
- nArchitecture targets net6 with stale package versions. Copy the **shape**, never the
  version numbers or the Elasticsearch/SQL-Server specifics. See [[reference-narchitecture]].

## Alternatives rejected

- **Node.js + NestJS** — comparable fit, no team advantage here.
- **Firebase** — removes the backend engineering the subject exists to teach.

Standards: `dotnet-csharp`, `clean-architecture-cqrs`.
