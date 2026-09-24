---
kind: adr
id: ADR-0015
status: Accepted
date: 2026-09-24
---

# ADR-0015 — Local Docker for the defence, CI without deployment

## Context

Subject V.7 requires the concurrent-user figure to be measured on a machine whose specification is
**declared** — cloud or on-premise, both acceptable. Subject V.8 requires continuous integration.
Issue O4 asked where the API runs.

Vercel was considered for publishing. It does not host this API: Vercel is serverless functions
plus static hosting, ASP.NET Core needs a long-running process, and SignalR holds persistent
WebSocket connections that the serverless model cannot keep open. .NET is also not a first-class
Vercel runtime.

## Decision

1. **Runtime for development and defence: local Docker** via `docker compose`, on a machine whose
   CPU, RAM, disk and network are declared in `docs/load/REPORT.md`.
2. **CI: GitHub Actions, with separate `dev` and `prod` configurations.** The pipeline builds,
   formats, tests, scans dependencies, generates the OpenAPI document, and builds the container
   image. It **does not deploy**.
3. **A remote deployment target is deferred** to a later ADR. Nothing in the system may assume a
   fixed origin, so deferring costs nothing.

## Consequences

- Zero hosting cost, zero deploy risk during the graded window. Subject V.7 is satisfied by
  declaring the machine, not by renting one.
- The capacity figure belongs to the declared laptop, and the report must say so. The database is
  off-premise on Supabase ([[ADR-0016-supabase-plan-region]]), so network latency to Frankfurt is
  part of the measured numbers — an on-premise API with an off-premise database is a hybrid, and
  the report states it rather than glossing over it.
- `dev` and `prod` pipeline configurations differ in configuration only, never in code path.
  `Development` enables detailed errors and the interactive OpenAPI UI; `prod` does not.
- The image is built in CI so that it is proven buildable, and pushed to a registry so a future
  deploy ADR has something to deploy. Building an image that is never run would be theatre; this
  one is the same image `docker compose` runs locally.
- **No deployment means no live demo URL.** The defence runs on the local stack. If a live URL is
  wanted later, the nearest viable targets are Fly.io, Railway, Render or Azure Container Apps —
  all of which accept a Docker image and support WebSocket. That is the deferred ADR.
- Vercel remains usable for a static surface — a landing page, or a React Native Web build owned
  by the UI developer. It is never a target for the API.

## Alternatives rejected

- **VPS (Hetzner, DigitalOcean)** — a fixed, defensible machine and a 24/7 demo, at a monthly cost
  and a deployment workload the schedule does not need yet.
- **Fly.io / Railway / Render free tier** — easy deploys, but variable CPU allocation and cold
  starts make "declare the server specification" harder to answer honestly.
- **Vercel for the API** — not technically possible, as above.

Closes issue O4. Opens a new issue: remote deployment target, deferred.
