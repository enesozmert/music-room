---
kind: adr
id: ADR-0002
status: Accepted
date: 2026-09-23
---

# ADR-0002 — React Native client, built after the backend

## Context

The subject requires a mobile app on Android or iOS. The current team scope is the backend.

## Decision

The client will be React Native (iOS + Android from one codebase). It is **not** built yet.
The backend is designed against this consumer today so the API contract does not need rework later.

## Consequences

- Transport choices must have a usable JavaScript client — this constrains [[ADR-0004-realtime-signalr]].
- Backend base URL is a client-side setting, so the API must not assume a fixed origin; CORS and
  absolute-URL generation are configured, not hardcoded.
- Expo vs bare CLI is `UNKNOWN - requires decision`.
- Social login runs the provider flow on the device and exchanges the provider token at our API.

## Alternatives rejected

- **Native Swift** — Swift 6.3 is installed, but iOS-only and no Android story.
- **Flutter** — SDK not installed on the dev machine, no team experience cited.

Card: [[musicroom-mobile]].
