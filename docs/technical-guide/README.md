---
aliases: [technical-guide-index]
kind: standards-index
updated: 2026-09-23
---

# Engineering Standards Library

Authoritative engineering rules for `music-room`. Markdown is the **editable source of truth**;
the sibling `STANDARD.pdf` is a distribution artifact regenerated from it.

Every standard separates two things:

- **Official requirements** — what the 42 subject or an external specification mandates.
- **Project policy** — what this team decided, which could have been decided differently.

A rule with no source is a project policy. A rule with a source is not negotiable by us.

## Index

### 00 — Foundations

| Standard | Covers |
|---|---|
| [engineering-baseline](00-foundations/engineering-baseline/STANDARD.md) | rules that hold everywhere: reproducible clone, `.env`, English, UTC, no vendored source |
| [git-workflow](00-foundations/git-workflow/STANDARD.md) | branches, Conventional Commits, review, leak hygiene |

### 10 — Backend

| Standard | Covers |
|---|---|
| [dotnet-csharp](10-backend/dotnet-csharp/STANDARD.md) | C# style, project files, nullable, async, central package management |
| [clean-architecture-cqrs](10-backend/clean-architecture-cqrs/STANDARD.md) | five-project layout, dependency rule, handlers, pipeline behaviours |
| [ef-core-persistence](10-backend/ef-core-persistence/STANDARD.md) | mapping, querying, optimistic concurrency, repositories |
| [signalr-realtime](10-backend/signalr-realtime/STANDARD.md) | hubs, groups, event contracts, post-commit broadcast |

### 20 — API

| Standard | Covers |
|---|---|
| [rest-api-design](20-api/rest-api-design/STANDARD.md) | URLs, status codes, JSON shape, Problem Details, client headers |
| [openapi-documentation](20-api/openapi-documentation/STANDARD.md) | generated contract, examples, published `openapi.json` |

### 30 — Data

| Standard | Covers |
|---|---|
| [database-design-migrations](30-data/database-design-migrations/STANDARD.md) | naming, constraints that protect invariants, reversible migrations |

### 40 — Security

| Standard | Covers |
|---|---|
| [authentication-authorization](40-security/authentication-authorization/STANDARD.md) | passwords, tokens, verification, two-gate authorisation, rate limits |
| [application-security](40-security/application-security/STANDARD.md) | input validation, headers, data exposure, threat register |
| [secrets-management](40-security/secrets-management/STANDARD.md) | `.env`, `.env.example`, leak response — the automatic-failure rule |

### 50 — Integration

| Standard | Covers |
|---|---|
| [youtube-integration](50-integration/youtube-integration/STANDARD.md) | adapter boundary, quota discipline, key placement, resilience |
| [social-oauth](50-integration/social-oauth/STANDARD.md) | PKCE, server-side token validation, account linking rules |

### 60 — Quality

| Standard | Covers |
|---|---|
| [testing](60-quality/testing/STANDARD.md) | per-layer tests, Testcontainers, the mandatory concurrency cases |
| [logging-observability](60-quality/logging-observability/STANDARD.md) | the mandatory audit log, structured logging, health |
| [load-testing](60-quality/load-testing/STANDARD.md) | scenarios, machine declaration, the capacity report |

### 70 — Delivery

| Standard | Covers |
|---|---|
| [ci-cd-containerization](70-delivery/ci-cd-containerization/STANDARD.md) | `make up`, multi-stage images, the merge gate |

### 80 — Client

| Standard | Covers |
|---|---|
| [react-native-client](80-client/react-native-client/STANDARD.md) | **deferred** — only the constraints it places on the backend today |

## Conventions

- One topic per folder. A topic is never duplicated in a second folder; standards cross-reference.
- Each folder contains `STANDARD.md` and, once generated, `STANDARD.pdf`.
- Edit only the Markdown. Regenerate PDFs with `../build-pdf.sh` (pandoc + tectonic).
- `manifest.json` is the machine-readable inventory. Update it when a standard is added.
- Version each standard with semver in its frontmatter: patch for wording, minor for a new rule,
  major for a rule that invalidates existing code.

## Related

Brain router: `../../brain/standards-router.md` · Manual: `../USER-MANUAL.md` · Prompts: `../prompt-library/PROMPTS.md` · Human entry point: `../../HOME.md`
