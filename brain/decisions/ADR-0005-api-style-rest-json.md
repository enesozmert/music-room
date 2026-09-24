---
kind: adr
id: ADR-0005
status: Accepted
date: 2026-09-23
---

# ADR-0005 — REST + JSON, documented with OpenAPI

## Context

Subject V.4 asks for a REST-supporting API with JSON, justified, and reference documentation
covering methods, inputs and outputs — naming Swagger as an example.

## Decision

Resource-oriented REST over HTTPS, JSON request and response bodies, OpenAPI 3 document
generated from the code and served as interactive docs. Realtime deltas go over SignalR
([[ADR-0004-realtime-signalr]]), not over REST polling.

## Consequences

- One documented contract for the mobile app and for peer evaluators.
- Errors use a single machine-readable shape (RFC 9457 `application/problem+json`).
- Breaking changes need an explicit versioning story; path versioning (`/api/v1`) is the default.
- REST is a poor fit for high-frequency deltas — that is precisely why realtime is a separate channel.

## Alternatives rejected

- **GraphQL** — flexible, but adds query-cost and auth-per-field problems to a graded security requirement.
- **gRPC** — weak browser story, and peer evaluators expect a readable HTTP API.

Standards: `rest-api-design`, `openapi-documentation`.
