---
kind: adr
id: ADR-0022
status: Accepted
date: 2026-09-24
---

# ADR-0022 — Serilog with OpenTelemetry

## Context

`logging-observability` separates the **audit log** (a database table, subject V.6, mandatory)
from the **application log** (diagnostics). The audit side is settled. The diagnostic side had no
implementation choice (T7).

Subject V.7 also requires the load report to **name the bottleneck with evidence**. Evidence is
easier to produce with traces than with reasoning.

## Decision

**Serilog** for application logging, with **OpenTelemetry** for traces and metrics.

- Serilog writes structured JSON to stdout; the container runtime collects it.
- Correlation id is attached by an enricher, so every log line and every error payload carries the
  same `traceId`.
- OpenTelemetry instruments ASP.NET Core, HttpClient and EF Core.
- The exporter is configuration-driven and **off by default**; it is switched on for a load run.

## Consequences

- **This is more machinery than a single-instance school project needs**, and that was said
  plainly when the choice was made. It earns its place on one condition: the traces are actually
  used during the Gatling run to find the bottleneck that subject V.7 requires naming. If the load
  report ends up saying "the bottleneck is the database" with no trace behind it, this stack was
  decoration.
- Concrete payoff to aim for: an EF Core span showing query time against a `HttpClient` span
  showing YouTube latency against total request time, on the same trace. That answers "where does
  the time go" with data.
- Serilog's enrichers make the correlation id trivial, which `logging-observability` requires in
  every request and every error response.
- The exporter being off by default keeps ordinary development and the defence demo free of a
  collector dependency. A collector that must be running to start the app would be a liability on
  defence day.
- Two logging paths must not blur: **nothing on the never-logged list reaches either sink** —
  passwords, tokens, API keys, `Authorization` headers, voter coordinates.
- The audit table is untouched by this decision. It is evidence, not diagnostics.

## Alternatives rejected

- **Built-in `ILogger` with a JSON console** — zero dependencies and sufficient for logs alone,
  but produces no traces, so the V.7 bottleneck claim would rest on inference.
- **Serilog without OpenTelemetry** — good logs, same missing evidence.

Closes T7.
