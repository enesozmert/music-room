---
kind: brain-router
scope: /Users/eozmert/ecole42/music-room
updated: 2026-09-23
---

# MEMORY — AI Router

Entry point for agents. Do **not** scan the workspace. Follow the retrieval path.

```
MEMORY -> Project Registry -> Project Card -> Architecture / ADR / Standard -> Source Code
```

## One-paragraph context

`music-room` is an École 42 group project: a collaborative music platform
(Music Track Vote, Music Control Delegation, Music Playlist Editor).
Backend is the current work scope and is **well past the skeleton**: identity, credential rate
limits, the audit log, bearer authentication, profiles with per-field visibility, the friends graph and
playlists all exist under
`MusicRoom/`. **Do not trust a count written here.** `docs/specs/` holds one file per unit of work
with its status, [[delivery-plan]] holds the slice order and each slice's `State`,
[[migrations-index]] holds the schema history, and `docs/progress/PROGRESS.json` holds the counts,
recounted by `.agent/progress.sh` on every merge that could move them; each is the authority for
its own question. `MusicRoomApp/` is still empty by design — the client workstream starts after S9.

## Router

| Need | Go to |
|---|---|
| What components exist | [[project-registry]] |
| A component's facts | `brain/projects/*.md` |
| How the system fits together | [[system-overview]] |
| Why a technology was chosen | [[decisions-index]] |
| What the subject *requires* | [[subject-requirements]] |
| Coding / security / API rules | [[standards-router]] |
| Security conformance target and checklists | [[owasp-index]] |
| Schema + data migrations | [[migrations-index]] |
| Past mistakes, gotchas | [[lessons-index]] |
| Human reading view | [[HOME]] |
| Consolidated design view | [[sdd]] |
| The API contract | [[endpoint-inventory]] |
| What to build next, in what order | [[delivery-plan]] |
| How far it has got, counted | `docs/progress/PROGRESS.json` — written by `.agent/progress.sh`, refreshed by the `progress` workflow on every push to `main`. The board is https://claude.ai/artifact/4fLzR9r23v7syYnYhUUoS7, republished in place by `/progress` |
| How this repo is operated | [[user-manual]] |
| Reusable prompts, autonomous recipes | [[prompt-library]] |
| Shared contract for any AI agent | `AGENTS.md` |
| Work orders an agent can execute | [[specs]] |

## Hard facts (safe to trust without re-reading)

- Workspace root: `/Users/eozmert/ecole42/music-room`
- Git: single repo. `main` carries branch protection with `enforce_admins: true`. Work lands as one
  branch and one draft pull request per spec ([[ADR-0025-agent-git-protocol]],
  [[ADR-0027-agent-push-and-pr]]). Several may be stacked and unmerged at once, and then `main` lags
  the spec statuses in `docs/specs/` — it did for eight specs until 2026-09-28. **Check `git log`
  rather than trusting a commit id or a "merged" claim written here.**
- A stacked pull request is retargeted to `main` **before** its base merges. GitHub closes a pull
  request whose base branch is deleted instead of retargeting it, and a closed one cannot be
  reopened once that branch is gone — #22 was lost that way and had to be reopened as #31.
- **`MusicRoom/` holds backend source code, `MusicRoomApp/` holds the React Native client**
  ([[ADR-0020-monorepo-layout]]). `MusicRoom/` holds 13 projects — 5 application, 4 `corePackages`,
  4 test — and `MusicRoomApp/` is empty until the client workstream starts. An agent stays inside one
  root.
- **`reference/` is read-only study material.** Nested, unrelated git repo. Never edit it, never
  build it, never reference it from a project, never copy a file out of it. Read it to learn a
  pattern; then write the code yourself under `MusicRoom/`.
- Authoritative requirement source: `docs/subject/en.subject tr.pdf` (Turkish, v6) — distilled into [[subject-requirements]].
- Toolchain present: dotnet 10.0.401, node v24.21.0, npm 11.19.0, Swift 6.3.3, Docker 29.8.0, pandoc, tectonic. **Absent**: flutter, psql, java.

## Two columns that are data, not notes

`.agent/progress.sh` counts everything it can and reads two things it cannot count:

- the `State` column of the slice table in [[delivery-plan]] — `merged`, `in progress`, `blocked`,
  `not started`
- the `Lands in` column of [[decisions-index]] — `landed`, `landed, <slice>`, or the slice id

A stale value in either one becomes a wrong number on the progress board, which the `progress`
workflow regenerates on every push to `main`. Update the column in the same change that makes it
true, the way a migration row goes in with its migration.

## Rules for agents

1. Backend code goes under `MusicRoom/`, client code under `MusicRoomApp/`. `reference/` is never
   a write target and never a dependency.
2. Read the Project Card before reading source code.
3. `UNKNOWN - requires targeted inspection` means: do not guess, inspect on demand, then update the card.
4. Do not copy standard text into the Brain. Link to `docs/technical-guide/`.
   Same for `docs/owasp/` — link, never restate.
4b. **All code written here must be OWASP-conformant.** ASVS 5.0.0 L1 everywhere applicable,
   L2 for V6/V7/V8/V9/V10/V16. Unfixed findings go to [[threat-register]], never left silent.
5. Never store secrets, `.env` values, tokens, logs, or transcripts here.
6. Update the Brain when a decision changes, not when code changes.
