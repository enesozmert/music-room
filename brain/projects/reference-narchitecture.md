---
kind: project-card
id: reference-narchitecture
status: external-readonly
updated: 2026-09-23
---

# Project Card — nArchitecture (reference)

**Read-only study material. Not a dependency, not a template to copy from.**

| | |
|---|---|
| Purpose | learn the structure — layer split, pipeline behaviours, repository shape |
| Allowed | open files and read them |
| Forbidden | edit, build, run, `git` commands inside it, `ProjectReference` to it, copying any file out of it |
| Write target instead | `MusicRoom/` — see [[musicroom-backend]] |

Reading a pattern here and then writing your own version under `MusicRoom/` is the intended use.
Copying a file across is not: this tree is net6 with stale package versions, and subject IV.1
forbids committing third-party source into the repository.

## Identity

| Field | Value |
|---|---|
| Path | `reference/nArchitecture/` |
| Origin | `https://github.com/engindemirog/nArchitecture.git` |
| Own git repo | yes — nested `.git`, HEAD `fe326b9 Day 5 Implement JWT basics` |
| Target framework | net6.0 (older than our net10.0) |

## Layout worth knowing

```
src/corePackages/
  Core.Application/         Pipelines, Requests   (MediatR behaviours, paging requests)
  Core.CrossCuttingConcerns/ Exceptions, Logging
  Core.ElasticSearch/
  Core.Mailing/             MailKit implementation
  Core.Persistence/         Dynamic, Paging, Repositories  (generic repo + dynamic query)
  Core.Security/            JWT, Hashing, Encryption, EmailAuthenticator, OtpAuthenticator
src/demoProjects/
  templateProject/  Application | Domain | Infrastructure | Persistence | WebAPI
  rentACar/         same five-layer shape
```

## What we learn from it (ideas, re-implemented by hand)

The five-layer split (Domain / Application / Persistence / Infrastructure / WebAPI),
MediatR pipeline behaviours, and the generic repository shape.
See [[ADR-0001-backend-dotnet-narchitecture]] and [[clean-architecture-cqrs]].

## What we take from it (files)

Nothing. Zero files move from `reference/` to `MusicRoom/`.

Specifically not reused: the Elasticsearch package, the SQL Server coupling, any net6 package
version, and the demo domain models (rentACar, templateProject). Our datastore is Supabase
PostgreSQL ([[ADR-0006-datastore]]) and our target is net10.0.

## Detail beyond this card

`UNKNOWN - requires targeted inspection`. Open specific files only when a concrete
pattern question comes up (e.g. "how does the JWT helper sign tokens").
