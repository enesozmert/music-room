# AGENTS.md — music-room

Shared contract for **any** AI coding agent working in this repository: Claude Code, Gemini CLI,
Cursor, Copilot, Aider, Codex, or a future one. Tool-specific notes live in the files listed at
the bottom; everything here applies to all of them.

## 1. Read this before doing anything

Start at `brain/MEMORY.md` and follow its router. **Do not scan the workspace.**

```
MEMORY -> brain/project-registry.md -> brain/projects/<card>.md -> architecture / ADR / standard -> source code
```

The Brain exists so you never re-derive context. Four small files beat a full-repo scan.

## 2. Where code goes

One repository, two application roots, one owner each — `brain/decisions/ADR-0020-monorepo-layout.md`.

| Path | Rule |
|---|---|
| `MusicRoom/` | **Backend source code.** Layout: `brain/decisions/ADR-0011-solution-layout.md` |
| `MusicRoomApp/` | **React Native client source code.** |
| `reference/` | **Read only.** Study material. Never edit, build, run git in, `ProjectReference`, or copy a file out of it |
| everything else | knowledge layer. Change it only when a decision or a status changes |

**Stay in your root.** An agent working on the backend writes under `MusicRoom/` only; an agent
working on the client writes under `MusicRoomApp/` only. Neither crosses into the other. The
contract between them is `docs/api/ENDPOINTS.md` and the generated `docs/api/openapi.json`.

Read a pattern in `reference/`, then write your own. That tree is net6 with stale packages, and
the project subject forbids committing third-party source.

## 3. Authority order

```
42 subject PDF -> ADR -> standard -> SDD -> Brain note -> code comment
```

- Requirements: `brain/constraints/subject-requirements.md`
- Decisions: `brain/decisions/` (indexed in `decisions-index.md`)
- Enforceable rules: `docs/technical-guide/` (19 standards)
- Security conformance: `docs/owasp/`
- Consolidated design: `docs/SDD.md`
- API contract: `docs/api/ENDPOINTS.md`
- What to build next: `docs/DELIVERY-PLAN.md`

## 4. Non-negotiable rules

1. **OWASP conformance.** ASVS 5.0.0 L1 everywhere applicable, L2 for V6/V7/V8/V9/V10/V16.
   Walk `docs/owasp/TOP10-CHECKLIST.md` and `API-TOP10-CHECKLIST.md`. Any finding you do not fix
   goes to `docs/owasp/THREAT-REGISTER.md` before the change is done.
2. **Size limits are analyzer-enforced** from the root `.editorconfig`: 40-line methods,
   complexity 10, 5 parameters, nesting 3, 15 methods per type, 300-line files.
   **Never lower a severity to make a build pass.** Fix the code or raise it as a decision.
3. **No secrets.** Never read, print, log, or commit a credential. Configuration comes from `.env`,
   which is git-ignored. A committed credential fails the whole project.
4. **No fabricated status.** Never mark a row `done` in `docs/SDD.md` §16 without code that exists.
   `UNKNOWN - requires decision` is a valid answer; a plausible guess is not.
5. **Decisions belong to the humans.** You may draft an ADR; you may not choose the architecture.
   The subject grades whether the team can defend its own choices.
6. **English in artifacts.** Identifiers, comments, commit messages, API fields, logs, docs.
7. **A numbered record is claimed on `origin/main`, not in your branch.** This covers every id the
   repository allocates: an ADR id, an `O` issue in `docs/SDD.md` §17, a `T` row in
   `docs/owasp/THREAT-REGISTER.md`, and a spec number. Before you write one, run

   ```
   git ls-tree --name-only origin/main brain/decisions/
   ```

   and take the first number after the highest one you see. For an `O` or `T` row, read the table on
   `origin/main` the same way.

   Two branches that each pick "the next number" pick the same one, and git merges both without a
   word when the filenames differ: the repository ends up with two files carrying one `id`, and
   whichever index edit lands second silently drops the other record's row. It happened five times
   between 2026-09-27 and 2026-10-03 — ADR-0030, ADR-0031, ADR-0035, ADR-0036 twice, and issue
   `O19` — and each fix is a file rename, two frontmatter lines and every reference across up to
   sixteen documents. Checking costs one command.

   The deeper fix is rule 8: a branch cut from `origin/main` reads the current numbers.

## 5. Definition of done for any change

- [ ] Code under your own root (`MusicRoom/` or `MusicRoomApp/`), following the standards that apply
- [ ] Builds clean with `TreatWarningsAsErrors`; no analyzer suppression without a justifying comment
- [ ] Tests required by `docs/technical-guide/60-quality/testing/STANDARD.md` for the layers touched
- [ ] If a concurrency hotspot was touched: a **parallel** test asserting final database state
- [ ] Negative authorisation cases tested — REST **and** hub
- [ ] OpenAPI regenerated; `docs/api/openapi.json` updated in the same change
- [ ] Migration recorded in `brain/migrations/migrations-index.md`
- [ ] `docs/SDD.md` §16 status updated honestly

## 5b. Git, for unattended runs

`brain/decisions/ADR-0025-agent-git-protocol.md`:

`brain/decisions/ADR-0025-agent-git-protocol.md` and
`brain/decisions/ADR-0027-agent-push-and-pr.md`:

- One branch per spec: `feat/spec-<nnn>-<slug>`, **cut from `origin/main`** after the spec is
  approved — never from the previous spec's branch. Run `git fetch origin` first.
- **Do not stack.** If the previous spec's pull request has not merged, either wait for it or cut
  from `origin/main` anyway and accept that the two touch the same central files. What is forbidden
  is basing a branch on another open one, which is what builds a stack.
- One commit per spec, only after verification passes. Conventional Commits; name the spec id in
  the body.
- **After `RESULT: pass` only**: push that branch and open a **draft** pull request with
  `gh pr create --draft`. One pull request per spec.
- **Never** push to `main` or any branch other than the spec's own. Never `--force`, never
  `reset --hard`, never `rebase`, never delete a remote branch or tag.
- **Never** mark a pull request ready for review, merge it directly, or approve it.
- **Auto-merge is allowed and expected** — `gh pr merge --auto --merge` on the spec's own pull
  request. Repository auto-merge is on, so GitHub merges it once the required `gate` check passes
  and never before ([[ADR-0027-agent-push-and-pr]], amended 2026-10-03). `gate` is therefore the
  whole of review: a defect it does not catch reaches `main` unread. Run `/review-diff` yourself.
- A blocked or environment result pushes nothing. The work stays local and the diff is the evidence.
- Refuse to start a run on a dirty working tree.

`main` carries branch protection with `enforce_admins: true`, so a direct push is refused
mechanically, not by convention.

### Why not to stack, measured

Twenty-one specs reached `main` through two stacks, on 2026-09-28 and 2026-10-01 to 03. The second
one cost, in order: nine pull requests retargeted by hand, because GitHub **closes** a child when
its base branch is deleted rather than retargeting it, and a closed one cannot be reopened once
that branch is gone (#22 was lost that way); a landing script whose ordering had to be rewritten
twice, since the base chain erases itself on retargeting and commit ancestry breaks when any
branch in the middle is rewritten; six conflict resolutions, every one of them in
`brain/decisions/decisions-index.md` or `docs/SDD.md`, the two files every spec touches; five
renumbered records; and a flaky test that each branch inherited and that failed `gate` twice
before it was fixed.

A branch cut from `origin/main` after the previous one merged has none of those. The whole of
`.agent/land-stack.sh` exists to clean up after a stack, and the point of this rule is that it
should be an emergency tool rather than a daily one.

## 6. Escalate instead of guessing

Stop and ask a human when the task would require any of these:

- a new or changed ADR, or contradicting an accepted one
- a new dependency
- a schema change beyond what the current spec covers, or any destructive migration
- a change to the public API contract in `docs/api/ENDPOINTS.md`
- anything touching authentication, authorisation, tokens, or secrets
- lowering an analyzer severity, deleting a test, or marking a test skipped

## 7. Tool-specific entry points

| Tool | File |
|---|---|
| Claude Code | `CLAUDE.md` |
| Gemini CLI | `GEMINI.md` |
| Cursor | `.cursor/rules/` (if present) |
| Anything else | this file |

All of them point back here. Keep the shared contract in one place.

## 8. Prompts

Reusable prompts, autonomous-run recipes and agent-specific snippets:
`docs/prompt-library/PROMPTS.md` (Turkish reading aid: `PROMPTS.tr.md`).
