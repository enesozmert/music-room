---
kind: project-card
id: musicroom-mobile
status: parallel-workstream
updated: 2026-09-23
---

# Project Card — MusicRoom.Mobile

React Native client at `MusicRoomApp/`. **Built in parallel by the second team member**, in this
same repository so that one clone is the whole submission (subject VII).

This card exists so the API contract is designed against a real consumer, and so the two
workstreams agree on the contract rather than discovering it late.

## Identity

| Field | Value |
|---|---|
| Path | `MusicRoomApp/` — same repository, separate root ([[ADR-0020-monorepo-layout]]). Not created yet |
| Owner | the UI developer (team of two: one backend, one UI) |
| Stack | React Native (Expo vs bare CLI: `UNKNOWN - requires decision`) |
| Targets | iOS + Android |
| State / data layer | `UNKNOWN - requires decision` |
| Auth | email+password, plus Google or Facebook social login (subject V.5, mandatory) |

## Contract obligations it places on the backend

- Configurable backend base URL (no hardcoded host).
- Social login callback/token exchange endpoints.
- Every client action reports platform, device model, app version — these belong in request headers, not the body. See [[auth-and-identity]].
- Realtime transport must work from React Native. SignalR has a JS client; that is the reason the transport choice is not .NET-only. See [[ADR-0004-realtime-signalr]].

## Standards that apply

Via [[standards-router]]: [[react-native-client]] (deferred) · [[rest-api-design]] · [[social-oauth]] · [[secrets-management]]
