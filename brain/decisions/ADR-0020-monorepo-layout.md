---
kind: adr
id: ADR-0020
status: Accepted
date: 2026-09-24
---

# ADR-0020 — Backend and client in one repository

## Context

Subject VII: the assignment is submitted in the git repository, and **only the work in that
repository is evaluated at defence**. The team is two people: one backend, one UI
([[ADR-0002-mobile-react-native]]). Where the React Native client lives was never decided (T1).

## Decision

One repository, two application roots:

```
music-room/
  MusicRoom/       backend  (.NET 10)   -- owner: backend developer
  MusicRoomApp/    client   (React Native) -- owner: UI developer
  brain/ docs/ .agent/                  -- shared knowledge layer
  reference/                            -- read only
```

## Consequences

- **One clone is the whole submission.** An evaluator sees everything without being told to fetch
  anything else. This is the reason for the decision; the rest is secondary.
- The write-target rule in `AGENTS.md` changes from one path to two, **with ownership**: an agent
  working on the backend writes under `MusicRoom/` only; an agent working on the client writes
  under `MusicRoomApp/` only. Neither crosses. `reference/` stays read-only for both.
- CI grows a second job. The .NET job and the React Native job run independently and neither
  blocks the other's iteration speed.
- `.gitignore` carries both ecosystems: `bin/`, `obj/` and `node_modules/`.
- The API contract in [[endpoint-inventory]] and the generated `docs/api/openapi.json` are the
  interface between the two roots. They are shared, versioned together, and changed in one commit.
- Risk: a noisy client commit history mixed into backend history. Accepted — the
  `git-workflow` branch naming already prefixes by scope, and one submission beats a tidy log.

## Alternatives rejected

- **Separate repository linked as a submodule** — lets the UI developer work independently, but a
  stale submodule pointer on defence day means missing code, and evaluators forget `--recursive`.
- **Separate, unlinked repository** — most flexible to develop, worst to submit. Subject VII says
  the repository is what is evaluated, so the client might simply not be graded.

Closes T1.
