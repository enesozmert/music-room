# Music Room

A collaborative music platform: shared playlists, event-based track voting, and playback control
delegated between friends — backend first, with every decision written down before the code that
implements it.

École 42 subject. .NET 10, PostgreSQL, SignalR. This README describes **what exists on `main`**, not
what is planned; the planned work is in [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md).

---

## What works today

| Capability | Where | Evidence |
|---|---|---|
| **Identity** — registration, email verification, sign-in, refresh-token rotation with family revocation, password reset, authenticated password change | `/api/v1/auth`, `/api/v1/me/password` | SPEC-005 to SPEC-007, SPEC-028 |
| **Password screening** — a chosen password is refused if it is in a bundled breached set or contains the caller's own context words | the one shared password rule | SPEC-034, SPEC-035, ADR-0042 |
| **Profiles and the friend graph** — per-field visibility tiers enforced in the query, friend requests, friendships | `/api/v1/me`, `/api/v1/users` | SPEC-013, ADR-0034 |
| **Playlists** — three access gates, collaborators, sparse track ordering, guarded reorder under `xmin` concurrency | `/api/v1/playlists` | SPEC-014 to SPEC-017 |
| **Events and voting** — the event aggregate with its window, invitations, attendance, suggestions, vote-ordered queue, venue codes, location and time licence checks | `/api/v1/events` | SPEC-018, SPEC-019, ADR-0014 |
| **Devices and control delegation** — register a device from its client headers, delegate control to a friend, revoke, release, with takeover notices | `/api/v1/devices` | SPEC-020 to SPEC-022 |
| **Playback** — sessions with a server-chosen next track, an idempotent advance, an explicit jump | `/api/v1/playback-sessions` | SPEC-021, ADR-0017 |
| **Catalog** — a YouTube adapter behind one abstraction, two cache levels, a per-account search limit, `503` on a spent quota | `/api/v1/tracks` | SPEC-025, ADR-0003, ADR-0023 |
| **Realtime** — three hubs, authorised on join, audited by one filter, broadcasting after the commit | `/hubs/playlists`, `/hubs/events`, `/hubs/control` | SPEC-016, SPEC-019, SPEC-022, ADR-0038 |
| **Audit log** — every mutating action and every credential refusal, fail-closed in the same transaction | `audit_records` | SPEC-008, SPEC-009, ADR-0019, ADR-0033 |
| **Interactive API documentation** — Swagger UI over the document the framework already generates, Development hosts only | `/swagger/index.html` | SPEC-032, ADR-0035 |
| **Mail** — a hosted provider behind `IMailService`, with a logging sender for local work that is refused anywhere else | Brevo | SPEC-024, ADR-0013 |

**69 of 82 endpoints** in [`docs/api/ENDPOINTS.md`](docs/api/ENDPOINTS.md) are implemented. The
remaining 13 are the OpenID Connect surface of slice S8.

## What is deliberately not here yet

- **OpenID Connect, both roles** (slice S8): Google and Facebook sign-in through one
  discovery-driven validation core, and this API becoming an OpenID Provider in its own right.
  ADR-0008 and ADR-0031 decide the shape; the code is not written.
- **The secret vault** (slice S1b): `ISecretProvider` and its environment implementation exist, so a
  vault-backed second implementation is a registration rather than a rewrite. Infisical itself needs
  a machine identity a person creates.
- **The mobile client** (`MusicRoomApp/`): deliberately sequenced after the backend, by owner
  decision. Nothing in it is written.
- **A deployment**: no host, so no TLS, no WSS, and nothing to ship logs to. Issue O11.

Every gap above is an issue row in [`docs/SDD.md`](docs/SDD.md) §17, with what it blocks and what it
would cost.

---

## Running it

Needs Docker and the .NET 10 SDK. Nothing else: the host stays empty on purpose (ADR-0026).

```bash
cp .env.example .env     # then fill it in; it is never committed
make up                  # build and start the stack
make migrate             # apply the migrations to the database DATABASE_URL names
```

The API answers on `http://localhost:8080`. On a Development host the interactive documentation is at
`http://localhost:8080/swagger/index.html`; the raw OpenAPI document is at `/openapi/v1.json` in every
environment.

| Command | What it does |
|---|---|
| `make up` / `make down` | start and stop the full stack |
| `make migrate` | apply EF Core migrations — reads `DATABASE_URL` and nothing else |
| `make test` | the full test suite |
| `make agent-check` | the local gate: format, build with warnings as errors, tests, vulnerable-package audit, OpenAPI drift, PDF drift |
| `make openapi` | regenerate `docs/api/openapi.json` from the code |
| `make load SCENARIO=vote-storm` | run one Gatling scenario against the stack |
| `make progress` | count how far the backend has got |

`make agent-check` is what CI runs as its `gate` job, so a green local run means a green pull request.

---

## How it is built

**Clean Architecture with CQRS.** Five projects under `MusicRoom/src/MusicRoom/`:

```
Domain          entities and invariants, no dependencies
Application     one command or query per use case, MediatR handlers, FluentValidation validators,
                and the abstractions the outer layers implement
Infrastructure  mail, catalog, security, configuration — the adapters
Persistence     EF Core, Npgsql, repositories, migrations
WebAPI          minimal API endpoints, middleware, SignalR hubs, the composition root
```

Dependencies point inward. A handler names an abstraction; the composition root decides which
implementation satisfies it. Tests live beside each layer in `MusicRoom/tests/`.

**The stack**, with every version pinned centrally in `Directory.Packages.props`:

| | |
|---|---|
| Runtime | .NET 10 (`net10.0`) |
| Mediation and validation | MediatR 14.2.0, FluentValidation 12.1.1 |
| Data | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, PostgreSQL 18.2 |
| Auth | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12, BCrypt.Net-Next 4.2.0 |
| Realtime | ASP.NET Core SignalR |
| Observability | Serilog 10.0.0, OpenTelemetry 1.19.x (exporter off by default) |
| Documentation | Microsoft.AspNetCore.OpenApi 10.0.12, Swashbuckle.AspNetCore.SwaggerUI 10.2.3 |
| Tests | xUnit v3 4.0.1, NSubstitute 6.2.0, Testcontainers 4.15.0 |
| Analyzers | SonarAnalyzer.CSharp 10.34.0, Microsoft.CodeAnalysis.NetAnalyzers 10.0.401 |

---

## Security posture

Each control is implemented and tested, not aspirational.

- **Passwords**: BCrypt at work factor 12, a 12-character minimum with no composition rules, and a
  64-character maximum. A chosen password is screened against a bundled breached set and against the
  caller's own context words. Login is deliberately not screened.
- **Sessions**: a 15-minute access token and an opaque refresh token. Rotation is single-use and
  reuse revokes the whole family; a family dies 30 days after its first token, absolutely.
- **Rate limiting**: per-IP and per-account policies on every credential endpoint, with progressive
  backoff. The limiter runs **after** authentication, because a per-account policy that runs before
  it silently becomes per-address — a defect a load measurement found (ADR-0041).
- **Realtime**: a connection ceiling of 12 per account and 60 per address, 60 hub invocations per
  connection per minute, and a handshake `Origin` checked against `CORS_ALLOWED_ORIGINS`. The numbers
  are measured rather than guessed (ADR-0030, SPEC-033).
- **Audit**: every mutating action and every credential refusal writes a row in the same transaction
  as the action it describes. It fails closed, deliberately.
- **Client context**: a mutating request without `X-Client-Platform`, `X-Client-Device` and
  `X-Client-App-Version` is refused with a bodiless `400`, hubs included.
- **Secrets**: read through one abstraction, from the environment that `docker compose` fills from
  `.env`. No secret is in source, and `.env` is refused by a write-boundary hook as well as ignored
  by git.

**OWASP ASVS**: 142 requirements committed, **70 met** with a named artifact, 5 partial, 43 not met —
29 of those are the V10 federation chapter waiting on S8 and 3 are TLS waiting on a deployment.
Outcomes per requirement are in [`docs/owasp/ASVS-CONFORMANCE.md`](docs/owasp/ASVS-CONFORMANCE.md);
accepted residual risks are in [`docs/owasp/THREAT-REGISTER.md`](docs/owasp/THREAT-REGISTER.md).

---

## Capacity, measured

On the declared machine, one instance supports **100 concurrent users performing contended writes and
200 reading or listening**. The write path sets the limit: `reorder` at 100 concurrent collaborators
held 1 624 requests/s with no failures and a p99 of 86 ms, while 200 crossed the 1% failure rule.

The bottleneck is PostgreSQL's connection count, not CPU — named rather than assumed, and tracked as
issue O22. Five Gatling scenarios, the command, the raw numbers and the reproduction steps are in
[`docs/load/REPORT.md`](docs/load/REPORT.md).

That measurement found two live defects that no functional test could: the rate limiter partitioning
on address instead of account, and a realtime limit nobody had built yet.

---

## Tests

547 test cases across four projects, 15 migrations, 13 projects in the solution.

- **Domain and Application**: unit tests with NSubstitute for the abstractions.
- **Persistence**: schema-convention tests over the built model, plus mapping tests against a real
  engine.
- **WebAPI**: end-to-end through `WebApplicationFactory` against a **real PostgreSQL in a container**
  (Testcontainers), because a credential read and a refresh-token insert are what those endpoints are
  made of.
- **Realtime**: a real SignalR client against the test host, never a mocked hub context.

Two rules the suites follow, both learned the expensive way and recorded as lessons:

1. **A broadcast test never proves absence by waiting.** It fences on an observable server-side
   event instead, because `StartAsync` returns before the server has joined the connection to its
   groups.
2. **A limit is proven by mutation.** A test that would pass with the limit removed proves nothing,
   so the ones that matter were run against a deliberately broken implementation first.

---

## How this repository is organised

The records are the point. Code says what it does; these say **why**, and they are written before the
code rather than after.

| Path | What is in it |
|---|---|
| [`AGENTS.md`](AGENTS.md) | the shared contract: retrieval path, write targets, authority order, definition of done |
| [`brain/`](brain/) | the knowledge layer — 42 ADRs in `decisions/`, lessons in `lessons/`, project cards, standards router |
| [`docs/SDD.md`](docs/SDD.md) | the consolidated design: architecture, data model, security, §16 requirement status, §17 open issues |
| [`docs/specs/`](docs/specs/) | one spec per unit of work, each with a seven-question gate, an exact file list and its verification commands |
| [`docs/api/ENDPOINTS.md`](docs/api/ENDPOINTS.md) | the API contract; `openapi.json` beside it is generated from the code and drift-checked in CI |
| [`docs/owasp/`](docs/owasp/) | ASVS conformance, the API Top 10 checklist, the threat register |
| [`docs/load/REPORT.md`](docs/load/REPORT.md) | the capacity measurement |
| [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md) | the thirteen slices and their state |
| [`reference/`](reference/) | read-only study material. Nothing is copied from it |

**Decision records are numbered and immutable.** When one turns out to be wrong about the code, it
gains an amendment line rather than a quiet edit — ADR-0030's connection ceiling and ADR-0042's two
homes were both corrected that way, with the measurement that forced the change.

**Specs carry a gate.** Seven questions — new ADR, new dependency, schema change, contract change,
credential path, lowered severity, `reference/` — and any "yes" stops the unit for a human. That is
why the ceiling moved from 5 to 12 after a measurement instead of during a code review.

---

## Working on it

Every unit of work follows the same path: a status query first, then an ADR if a decision is needed,
then a spec with its gate answered honestly, then the code, then the records. The prompts that drive
it are in [`docs/prompt-library/PROMPTS.md`](docs/prompt-library/PROMPTS.md) — canonical, with Turkish
translations beside them — and nine of them are wrapped as slash commands in `.claude/commands/`.

Pull requests are not drafts and carry auto-merge: CI's `gate` job is the reviewer, and it runs the
same `make agent-check` a local run does. One open unit at a time, never a stack — a measured lesson,
after one stack cost nine hand-retargeted pull requests.

---

## Status

| | |
|---|---|
| Slices merged | 10 of 13 |
| Requirements built | 19 of 35, with 8 partly implemented |
| Endpoints implemented | 69 of 82 |
| ASVS requirements met | 70 of 142 committed |
| Specs done | 35 |
| Decisions landed | 27 of 42 |

The critical path is slice S8. It carries both OpenID Connect roles, it rewrites the sign-in surface
three earlier specs shipped, and the 29 unmet V10 requirements are waiting on it.

`make progress` prints these numbers from the records at any commit.
