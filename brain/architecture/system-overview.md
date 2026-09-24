---
kind: architecture
updated: 2026-09-23
---

# System Overview

Status: **target design, nothing implemented**.

```
 React Native app  ──REST/JSON──►  ASP.NET Core Web API  ──►  EF Core  ──►  Supabase PostgreSQL
   (out of scope)  ──WebSocket──►  SignalR hubs           ──►  YouTube Data API v3
                                        │
                                        └──►  Audit log store (platform/device/app version)
```

## Layering (Clean Architecture, 5 projects)

All five projects live under `MusicRoom/`. Nothing is written outside it.

| Layer | Holds | Depends on |
|---|---|---|
| Domain | entities, value objects, domain rules, enums | nothing |
| Application | CQRS commands/queries, handlers, validators, pipeline behaviours, abstractions | Domain |
| Persistence | `DbContext`, configurations, repositories, migrations | Application, Domain |
| Infrastructure | YouTube client, mail, token/OTP services, external adapters | Application, Domain |
| WebAPI | controllers, SignalR hubs, DI composition, middleware, OpenAPI | all of the above |

Dependency rule: arrows point inward only. WebAPI never talks to `DbContext` directly.
Rationale: [[ADR-0001-backend-dotnet-narchitecture]].

## Request path

`Controller → MediatR Send → pipeline (validation, auth, logging, transaction) → handler → repository → DbContext`

## Realtime path

`Hub method → MediatR Send (same handler as REST) → handler → hub broadcast to group`

Rule: realtime never gets its own business logic. Both transports call the same handler.
Detail: [[realtime-architecture]].

## Related

[[domain-model]] · [[auth-and-identity]] · [[musicroom-backend]] · [[standards-router]]
