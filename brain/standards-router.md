---
kind: standards-router
updated: 2026-09-23
---

# Standards Router

The Brain does **not** contain standards. It points at them.
Authoritative text lives in `docs/technical-guide/`. Markdown is the source of truth; the
sibling PDF is a distribution artifact.

Index: [[technical-guide-index]] · Manifest: `docs/technical-guide/manifest.json`

| Topic | Standard | Applies to |
|---|---|---|
| Engineering baseline | [[engineering-baseline]] | everything |
| Git & branching | [[git-workflow]] | everything |
| C# / .NET style | [[dotnet-csharp]] | backend |
| Clean Architecture + CQRS | [[clean-architecture-cqrs]] | backend |
| EF Core persistence | [[ef-core-persistence]] | backend |
| SignalR realtime | [[signalr-realtime]] | backend |
| REST API design | [[rest-api-design]] | backend, mobile |
| OpenAPI documentation | [[openapi-documentation]] | backend |
| Database design & migrations | [[database-design-migrations]] | backend |
| AuthN / AuthZ | [[authentication-authorization]] | backend, mobile |
| Application security | [[application-security]] | everything |
| **OWASP conformance target** | [[owasp-index]] — separate layer, not a standard | everything |
| Secrets management | [[secrets-management]] | everything |
| YouTube integration | [[youtube-integration]] | backend, mobile |
| Social OAuth | [[social-oauth]] | backend, mobile |
| Testing | [[testing]] | everything |
| Logging & observability | [[logging-observability]] | backend |
| Load testing | [[load-testing]] | backend |
| CI/CD & containers | [[ci-cd-containerization]] | everything |
| React Native client | [[react-native-client]] | mobile (deferred) |

## Conformance layer

`docs/owasp/` states *what secure means* (target, checklists, threat register, the vendored
standard). `docs/technical-guide/40-security/` states *how we build it*. Neither restates the other.

[[owasp-index]] · [[asvs-conformance]] · [[owasp-top10-checklist]] ·
[[owasp-api-top10-checklist]] · [[threat-register]] · [[owasp-cheatsheets]]

## Reading order for a new contributor

`engineering-baseline` → `git-workflow` → `clean-architecture-cqrs` → `rest-api-design`
→ `secrets-management` → the standard for the task at hand.

Related: [[MEMORY]] · [[project-registry]] · [[musicroom-backend]]
