# Music Room

A collaborative music platform: shared playlists, event-based track voting, and playback control
delegated between friends — backend first, with every decision written down before the code that
implements it.

École 42 subject. .NET 10, PostgreSQL, SignalR. This README describes **what exists on `main`**, not
what is planned; the planned work is in [`docs/DELIVERY-PLAN.md`](docs/DELIVERY-PLAN.md).

**It is live**: [`music-room-api-regw.onrender.com`](https://music-room-api-regw.onrender.com/health),
and it is an OpenID Provider — [its discovery
document](https://music-room-api-regw.onrender.com/.well-known/openid-configuration) and [its
keys](https://music-room-api-regw.onrender.com/.well-known/jwks.json) are public, so a token it issues
can be verified by something that has never spoken to it.

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
| **Mail** — a hosted provider behind `IMailService`; a verification message carries a link **and** a six-character code, in English or Turkish, from one black-on-white layout at WCAG AA | Brevo | SPEC-024, SPEC-052, ADR-0013 |
| **Sign in with Google** — one discovery-driven validation core, the authorization code exchanged by this API with the client secret, an identity bound to exactly one account | `/api/v1/auth/sessions/social`, `/api/v1/auth/social-links` | SPEC-041, ADR-0008 |
| **This API is an OpenID Provider** — discovery document, JWKS, authorization endpoint with PKCE `S256`, token endpoint, RS256 ID token, UserInfo, revocation | `/.well-known/*`, `/oauth2/*` | SPEC-043 to SPEC-045, ADR-0031 |
| **A secret vault** — `SECRET_PROVIDER=infisical` reads configuration from a self-hosted Infisical, with `.env` as the floor beneath it and no fallback when the vault is unreachable | `ISecretProvider` | SPEC-030, SPEC-042, SPEC-047, ADR-0029 |
| **A live deployment** — Render, Frankfurt, HTTPS, against Supabase, with the whole identity surface driven end to end over real HTTP | [music-room-api-regw.onrender.com](https://music-room-api-regw.onrender.com/health) | SPEC-046, SPEC-051, ADR-0046 |

**79 of the 82 endpoints** in [`docs/api/ENDPOINTS.md`](docs/api/ENDPOINTS.md) are implemented. The
three that are not are Facebook's half of social sign-in.

## What is deliberately not here yet

- **The mobile client** (`MusicRoomApp/`): sequenced after the backend by owner decision, and the
  reason that root is empty. The contract it will read is published and drift-checked.
- **Facebook sign-in**: the second acquisition against the same validation core. Google works; the
  shape Facebook needs is decided (ADR-0008) and the credentials are not created.
- **Row-level security in PostgreSQL** (O20): defence in depth behind gates that are already enforced
  in every query.
- **A capacity figure for the deployment**: subject V.7 is answered on the declared machine, and a
  free-tier container that sleeps after fifteen minutes is a URL rather than a measurement platform.

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

**It also runs live**, at [`music-room-api-regw.onrender.com`](https://music-room-api-regw.onrender.com/health)
— Render, Frankfurt, HTTPS, against Supabase. The free tier sleeps after fifteen minutes, so the first
request wakes it and takes about a minute; `/` is a `404` because this is a JSON API with no pages.
`docs/deploy/RENDER.md` is the runbook, `render.yaml` is the stack.

| Command | What it does |
|---|---|
| `make up` / `make down` | start and stop the full stack |
| `make migrate` | apply EF Core migrations — reads `DATABASE_URL` and nothing else |
| `make test` | the full test suite |
| `make agent-check` | the local gate: format, build with warnings as errors, tests, vulnerable-package audit, OpenAPI drift, PDF drift |
| `make openapi` | regenerate `docs/api/openapi.json` from the code |
| `make load SCENARIO=vote-storm` | run one Gatling scenario against the stack |
| `make e2e` | drive the whole identity surface against the composed stack over real HTTP — 47 assertions |
| `make vault` | start the self-hosted Infisical, which `make up` deliberately does not |
| `make oidc-key` | print an RS256 key pair for `OIDC_SIGNING_KEY`; nothing generates one for itself |
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
- **Sessions**: a 15-minute **RS256** access token and an opaque refresh token. One key pair signs
  every token this API issues, its `kid` names it, and the public half is published at
  `/.well-known/jwks.json` — so a token can be verified by something that has never spoken to this
  API. Rotation is single-use and reuse revokes the whole family; a family dies 30 days after its
  first token, absolutely.
- **Verification**: a message carries a 256-bit link token **and** a six-character code from a
  32-symbol alphabet with no character a person misreads. Five wrong codes spend the row, so the
  sixth attempt is refused even when it is right — the budget, not the length, is what makes a short
  code safe. Both last fifteen minutes. The password reset deliberately has no code.
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
- **Secrets**: read through one abstraction — the environment, or a self-hosted Infisical when
  `SECRET_PROVIDER=infisical`. The vault outranks `.env` and `.env` is the floor beneath it, because
  the credentials that open a vault cannot live inside it. **A vault that cannot be reached stops the
  host**: a fallback would silently return the system to reading keys out of a file. No secret is in
  source, and `.env` is refused by a write-boundary hook as well as ignored by git.
- **Behind a proxy**: the deployment forwards `X-Forwarded-Proto` and `X-Forwarded-For`. Without it
  the issuer advertised over HTTPS says `http`, which a conforming client refuses — and, more
  quietly, the per-IP limiter sees the proxy's address and partitions the whole internet into one
  bucket. Both were found by driving the live deployment, not by reading it.

**OWASP ASVS**: 142 requirements committed, **70 met** with a named artifact, 5 partial, the rest not
met. That assessment is dated **2026-10-04** and has not been re-run since — the OpenID Provider
surface and the live deployment landed after it, so the V10 federation chapter and the TLS
requirements are recorded as unmet by a pass that predates the code that answers them. Claiming them
without a pass would be the kind of number this project does not publish; re-running it is its own
unit. Outcomes per requirement are in
[`docs/owasp/ASVS-CONFORMANCE.md`](docs/owasp/ASVS-CONFORMANCE.md); accepted residual risks are in
[`docs/owasp/THREAT-REGISTER.md`](docs/owasp/THREAT-REGISTER.md).

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

717 test cases across four projects, 20 migrations, 13 projects in the solution.

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
| Slices merged | **13 of 13** |
| Endpoints implemented | 79 of 82 |
| Specs done | 50 of 52 |
| Decisions landed | 46 ADRs |
| Tests | 717, 0 failing |
| Live | [HTTPS, Frankfurt, since 2026-10-11](https://music-room-api-regw.onrender.com/health) |

**The backend is done.** Every slice in the plan is merged, including the one that carried both
OpenID Connect roles and the one that put a secret vault in production. What remains is the client —
`MusicRoomApp/`, sequenced after the backend by owner decision — and Facebook's half of social
sign-in.

Two things the records are honest about rather than quiet about: the ASVS pass predates the provider
work and has not been re-run, and the capacity figure belongs to the declared machine rather than to
the free-tier deployment.

`make progress` prints these numbers from the records at any commit.
