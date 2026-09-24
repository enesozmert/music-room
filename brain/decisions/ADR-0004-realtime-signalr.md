---
kind: adr
id: ADR-0004
status: Accepted
date: 2026-09-23
---

# ADR-0004 — SignalR over WebSocket for realtime

## Context

Track Vote and Playlist Editor are both real-time multi-user features. The client is
React Native ([[ADR-0002-mobile-react-native]]), the backend is ASP.NET Core ([[ADR-0001-backend-dotnet-narchitecture]]).

## Decision

Use SignalR hubs, WebSocket as the primary transport, with SignalR's built-in fallbacks.
The official `@microsoft/signalr` JavaScript client serves React Native.

## Consequences

- One runtime, one auth mechanism: hub connections carry the same JWT as REST.
- Hubs are transport only. Every hub method delegates to the same CQRS handler the REST
  endpoint uses, so business rules cannot diverge. See [[realtime-architecture]].
- Broadcast happens after commit, never before.
- Scaling past one instance needs a Redis backplane — deferred, `UNKNOWN - requires decision`.

## Alternatives rejected

- **Raw WebSockets** — reimplements reconnect, groups, and fallback by hand.
- **Polling** — fails the "real-time" wording and wastes YouTube quota and battery.

Standard: `signalr-realtime`.
