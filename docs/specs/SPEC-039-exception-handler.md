---
id: SPEC-039
title: An unexpected exception answers ProblemDetails, and says nothing about itself
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.4, V.6]
decisions: [ADR-0045, ADR-0005, ADR-0022]
standards: [rest-api-design, application-security, logging-monitoring, testing]
approved_by: enesozmert # gates 4 and 5 answered on 2026-10-09, before the spec was written
escalation: |
  **Gates 4 and 5 are yes, and the owner answered both when O29 was opened.**

  Gate 4: `docs/api/ENDPOINTS.md` §Conventions describes what a `500` carries, which it has never said because
  a `500` has never carried anything.

  Gate 5: an exception handler sits in front of every route, the credential ones included, and the whole point
  of it is **not** disclosing what it caught. A handler that leaked a stack trace or an Npgsql message naming a
  column would be an information-disclosure defect introduced by a reliability fix.

  Gates 1, 2, 3, 6 and 7 answer no. [[ADR-0045-stable-error-codes]] already decided the vocabulary and this
  reuses it; no package — `UseExceptionHandler` and `IExceptionHandler` are the framework's; no migration;
  nothing lowered, deleted or skipped; nothing from `reference/`.
---

# One API, one error envelope

## Goal

Issue **O29**: `services.AddProblemDetails()` is registered and `UseExceptionHandler` is never called, so the
service produces nothing. A validation failure answers a proper `422` with `errors` and `codes`; an unexpected
exception answers a bare `500` with no body in Production and a developer page in Development. A client needs
two parsers and the second one reads nothing.

After this spec every refusal and every failure answers `ProblemDetails`, and the `500` carries a code from the
vocabulary SPEC-038 established rather than inventing a second one.

## Out of scope

- **Changing what any endpoint answers deliberately.** `400`, `401`, `403`, `404`, `409`, `410`, `412`, `422`
  and `429` are decisions other specs made. This unit is only about the failure nobody decided.
- **Reporting the exception to anything.** Where logs go is T-33, sequenced behind a deployment. This writes
  the log line; it does not ship it.
- **Retries, circuit breakers or degraded modes.** A failure answers honestly and stops.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — the code comes from [[ADR-0045-stable-error-codes]]'s vocabulary |
| 2 | new or upgraded dependency? | no — `IExceptionHandler` and `UseExceptionHandler` are the framework's |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — §Conventions gains what a `500` carries |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it sits in front of every route, and its job is to disclose nothing |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Gates 4 and 5 are yes, so Tier B. Both were answered by the owner on 2026-10-09 — see `escalation`.

## Decisions this spec takes

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **`UseExceptionHandler` with a written handler**, not the parameterless overload. | The default writes the exception's own message into `detail` in some configurations, and an Npgsql message names a column and a constraint. A reliability fix must not become an information-disclosure one |
| 2 | **The response says `INTERNAL.UNEXPECTED` and nothing else.** No message, no type, no trace, no identifiers. | Everything a caller could use to diagnose is something an attacker can use to map the system. The person who needs the detail is reading the log, not the response |
| 3 | **The log line carries the exception, at `Error`, with the trace identifier.** | A failure nobody can diagnose is worse than a leaky one. The trace identifier is what joins the log line to the request — and it is in the log rather than in the body, because a body is public |
| 4 | **First in the pipeline, before `UseSerilogRequestLogging`.** | A handler that runs after the request logger cannot shape the response of an exception thrown by the logger's own pipeline position. First is the only position that covers everything behind it |
| 5 | **Its own file, `UnexpectedExceptionHandler.cs`.** | `Program.cs` is at S104's 300-line cap — issue O24 — and this is the third unit in a row to need a new file for that reason rather than raise a severity |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/WebAPI/UnexpectedExceptionHandler.cs` | create — the handler: log the exception, answer `ProblemDetails` with the code and nothing else |
| `MusicRoom/src/MusicRoom/Application/Validation/ValidationCodes.cs` | change — `INTERNAL.UNEXPECTED` joins the vocabulary |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — register the handler and call `UseExceptionHandler` first |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/UnexpectedExceptionTests.cs` | create — the body carries the code and no exception text; the status is `500` |
| `docs/api/ENDPOINTS.md` | change — §Conventions: what a `500` carries |
| `docs/SDD.md` | change — O29 closed, §13 the disclosure rule |
| `docs/specs/SPEC-039-exception-handler.md` | change — status transitions |

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `UnexpectedExceptionTests` — the status | a route that throws answers `500` rather than a connection reset |
| — the body | it is `ProblemDetails` carrying `code: INTERNAL.UNEXPECTED`, so one parser reads every failure |
| — what it does not carry | the exception's message and type appear nowhere in the response, asserted by absence |
| — the log | the same request leaves an `Error` line that **does** carry the exception, so the detail exists where it belongs |
| the 600 tests already passing | no deliberate status changed: this covers the failure nobody decided, not the ones everybody did |
| `make agent-check` | the regenerated document matches, since `ProblemDetails` already declares `code` from SPEC-038 |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] no deliberate status or body changed
- [x] O29 is closed with what replaced it
- [x] **the composition root is the same length as before.** Two new lines would have put `Program.cs` at 302
  against S104's 300 — O24's wall for the third unit running. `ErrorHandling` took the request logger with it,
  so the root calls two helpers where it called two framework methods
- [x] `status: done` in this file
