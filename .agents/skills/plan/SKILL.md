---
name: plan
description: Produce a file list for a change and stop for approval — no code this turn. Argument: <what you want built>
---

# plan


Task: **$ARGUMENTS**.

Before writing any code:

1. Read `brain/projects/musicroom-backend.md` and the relevant `brain/architecture/` note.
2. Read the applicable standards via `brain/standards-router.md`.
3. Read `docs/api/ENDPOINTS.md` for the contract, and `docs/owasp/API-TOP10-CHECKLIST.md`.
4. List every file you will create or change, with its path under `MusicRoom/`.

Then stop and wait for my approval. Write no code in this turn.

Constraints:

- The branch for this work is cut from `origin/main`, never from another open branch. `git fetch
  origin` first. Stacking is what cost five renumbered records and six hand-resolved conflicts
  between 2026-09-27 and 2026-10-03 — `AGENTS.md` §5b has the measurement.
- All backend code under `MusicRoom/`; client code under `MusicRoomApp/`. `reference/` is read-only.
- One command or query per use case; handlers stay thin.
- No secret in source; configuration from `.env`.
- Size limits: 40-line methods, complexity 10, 5 parameters, 15 methods per type. Severities in
  `.editorconfig`, thresholds in `MusicRoom/SonarLint.xml`.

Source: `docs/prompt-library/PROMPTS.md` prompt 2.
