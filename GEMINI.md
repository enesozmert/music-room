# GEMINI.md — music-room

**Read `AGENTS.md` first.** It is the shared contract for every AI agent in this repository:
retrieval path, write targets, authority order, non-negotiable rules, definition of done, and
when to escalate instead of guessing.

This file holds only Gemini-specific notes.

## Context budget

Start at `brain/MEMORY.md` and follow its router. Do not read the repository breadth-first — the
Brain exists precisely so that four small files replace a full scan. `reference/` and
`docs/owasp/upstream/` are large and must not be loaded unless a question genuinely needs them.

## Behaviour

- State the file list you intend to change **before** changing anything.
- One logical change per run. Do not bundle unrelated fixes.
- If a rule in `docs/technical-guide/` conflicts with your default style, the rule wins.
- Escalate on the triggers in `AGENTS.md` §6 rather than choosing for the team.

## Prompts

`docs/prompt-library/PROMPTS.md` — Part 1 is agent-agnostic, Part 2 covers autonomous runs,
Part 3 has per-tool snippets. Turkish reading aid: `PROMPTS.tr.md`.
