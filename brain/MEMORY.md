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
Backend is the current work scope. Repository is **greenfield** — one commit,
no application source code yet.

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
| How this repo is operated | [[user-manual]] |
| Reusable prompts, autonomous recipes | [[prompt-library]] |
| Shared contract for any AI agent | `AGENTS.md` |
| Work orders an agent can execute | [[specs]] |

## Hard facts (safe to trust without re-reading)

- Workspace root: `/Users/eozmert/ecole42/music-room`
- Git: single repo, branch `main`, 1 commit (`1b76d0c Initial commit`).
- **`MusicRoom/` holds backend source code, `MusicRoomApp/` holds the React Native client**
  ([[ADR-0020-monorepo-layout]]). Both currently empty. An agent stays inside one root.
- **`reference/` is read-only study material.** Nested, unrelated git repo. Never edit it, never
  build it, never reference it from a project, never copy a file out of it. Read it to learn a
  pattern; then write the code yourself under `MusicRoom/`.
- Authoritative requirement source: `docs/subject/en.subject tr.pdf` (Turkish, v6) — distilled into [[subject-requirements]].
- Toolchain present: dotnet 10.0.401, node v24.21.0, npm 11.19.0, Swift 6.3.3, Docker 29.8.0, pandoc, tectonic. **Absent**: flutter, psql, java.

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
