---
kind: adr
id: ADR-0011
status: Accepted
date: 2026-09-24
---

# ADR-0011 — Solution layout mirrors nArchitecture

## Context

Issue O2 blocked scaffolding. The team studied `reference/nArchitecture`
([[reference-narchitecture]]) and wants the same shape so the reference stays readable as a guide.

## Decision

Lay out `MusicRoom/` with the same structure as the reference repository:

```
MusicRoom/
  MusicRoom.sln
  src/
    corePackages/          cross-cutting packages, reused by the application projects
      Core.Application/      MediatR pipeline behaviours, request/response base types
      Core.CrossCuttingConcerns/  exceptions, logging abstractions
      Core.Persistence/      generic repository, paging, dynamic query
      Core.Security/         JWT, hashing, token services
    MusicRoom/
      Domain/
      Application/
      Persistence/
      Infrastructure/
      WebAPI/
  tests/
    <Project>.Tests/
```

## Consequences

- The reference repo stays a usable map: a pattern found there sits at the same relative path here.
- **The shape is copied, never the files.** Not one file moves from `reference/` into `MusicRoom/`.
  That tree is net6 with stale package versions, and subject IV.1 forbids committing third-party
  source. Read the pattern, write our own. See [[reference-narchitecture]].
- `corePackages` for a single application is an extra layer the reference carries because it is a
  teaching template with two demo projects. Accepted for familiarity, with one rule: **a type goes
  into `corePackages` only when it is genuinely domain-agnostic.** Anything that mentions a
  playlist, an event or a vote belongs in `MusicRoom/Domain` or `MusicRoom/Application`.
- The reference has no Elasticsearch or SQL Server equivalent here. `Core.ElasticSearch` and
  `Core.Mailing` are **not** reproduced; mail lives in `Infrastructure`
  ([[ADR-0013-mail-provider]]).
- `tests/` sits beside `src/`, which the reference does not show in detail. One test project per
  production project, named `<Project>.Tests` ([[ADR-0012-test-stack]]).
- The dependency rule from [[clean-architecture-cqrs]] is unchanged and still enforced: arrows
  point inward, `Domain` references nothing.

## Alternatives rejected

- **`src/` + `tests/` with no `corePackages`** — fewer layers, but the reference stops lining up.
- **Flat `MusicRoom/{Domain,Application,...}`** — leaves no obvious home for the solution file and tests.

Closes issue O2.
