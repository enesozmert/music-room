---
kind: adr
id: ADR-0018
status: Accepted
date: 2026-09-24
---

# ADR-0018 — Gatling for load testing

## Context

Subject V.7 requires the concurrent-user figure to be **measured**, and names ab, Gatling, Siege,
Tsung and JMeter as examples. No tool had been chosen (gap G3).

The choice is constrained by something the subject does not spell out: this system's scaling risk
is **SignalR connection fan-out**, not REST throughput. A tool that cannot hold WebSocket
connections cannot measure the part most likely to fall over.

## Decision

**Gatling**, run from its Docker image, for both REST and WebSocket scenarios.

Simulations are committed under `tests/load/` and are part of the repository, so the numbers in
`docs/load/REPORT.md` are reproducible.

## Consequences

- Gatling is **named in the subject** and supports WebSocket natively. One tool covers both
  measurement surfaces, so the report has one methodology instead of two.
- Scenarios from [[load-testing]] map to simulations: authenticated queue read, concurrent vote
  casting, concurrent playlist reorder, SignalR group fan-out, login burst.
- REST throughput and WebSocket connection count are still reported **separately**. They are
  different ceilings and one tool measuring both does not make them one number.
- Cost: a Scala/Java DSL the team does not already know. Mitigated by running the official Docker
  image and keeping simulations small — this is measurement code, not production code.
- Gatling reports percentiles by default, which [[load-testing]] requires. Reporting a mean would
  have been the easy mistake with a simpler tool.

## Alternatives rejected

- **k6** — friendlier JavaScript scenarios and good WebSocket support, but not in the subject's
  example list. Defensible under "tools such as", yet it spends credibility for no capability gain.
- **ab + k6** — ab is the simplest named tool but cannot touch WebSocket, so it needs a second
  tool and a second methodology in the report.
- **JMeter** — named in the subject, but WebSocket needs a plugin and headless CI runs with
  committed results are more work.

Closes gap G3.
