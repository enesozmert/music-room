---
standard: engineering-baseline
aliases: [engineering-baseline]
title: Engineering Baseline
version: 1.1.0
status: active
updated: 2026-09-23
applies-to: [backend, mobile, tooling]
---

# Engineering Baseline

## Scope

Rules that hold everywhere in `music-room`, independent of language. Every other standard
assumes this one.

## Official requirements

From `docs/subject/en.subject tr.pdf` (v6), sections IV and V.8 — these are graded, not negotiable:

- No third-party library **source** may be committed to the repository.
- Dependencies MUST install automatically from a fresh clone through a Makefile or an
  equivalent documented mechanism.
- All credentials, API keys and environment variables MUST live in a local `.env` that git
  ignores. Publicly stored credentials cause direct project failure.
- Per-layer tests and continuous integration are required.
- The chosen SDK must not perform the project's own work.

## Project policy

1. **OWASP conformance is not optional.** Every line of code written in this project must be
   OWASP-conformant. Concretely: ASVS 5.0.0 Level 1 for every applicable chapter, Level 2 for
   V6, V7, V8, V9, V10 and V16; both OWASP Top 10 checklists walked on every pull request; every
   unfixed finding recorded in the threat register. The conformance layer is `docs/owasp/`;
   the decision is `brain/decisions/ADR-0009-code-quality-gates.md`.
2. **One command to run.** `make up` MUST bring the backend and its database up from a clean
   clone with no manual steps beyond copying `.env.example` to `.env`.
3. **Documented decisions.** A technology choice that a peer evaluator could question needs an
   ADR in `brain/decisions/`. No ADR means the choice is undefended.
4. **Microsoft conventions, no house dialect.** Every C# identifier follows Microsoft's naming
   and coding conventions, and the size and complexity limits in the `dotnet-csharp` standard are
   enforced by analyzers with `TreatWarningsAsErrors`. A violation fails the build, not the review.
5. **English in the codebase.** Identifiers, comments, commit messages, API fields and logs are
   English. Discussion may be Turkish; artifacts are not.
6. **UTC everywhere.** Store and transmit instants as UTC ISO-8601. Local time is a presentation concern.
7. **UTF-8, LF line endings** in every text file.
8. **No dead code.** Commented-out blocks and unused files are deleted, not parked. Git holds history.
9. **No generated artifacts in git.** `bin/`, `obj/`, `node_modules/`, build output, coverage reports.
10. **AI disclosure.** The subject asks for transparency about AI-assisted work. Note significant
   AI-generated components in the pull request description; every author must be able to defend
   the code they submit.

## Checklist

- [ ] Fresh clone + `cp .env.example .env` + `make up` works
- [ ] `.env` is git-ignored and `.env.example` lists every key with placeholder values
- [ ] No vendored library source in the tree
- [ ] Every non-obvious technology choice has an ADR
- [ ] `.editorconfig` present at the repository root and honoured by CI
- [ ] Both OWASP checklists walked on the pull request

## References

- Project subject, `docs/subject/en.subject tr.pdf` sections IV, V.8
- Twelve-Factor App — https://12factor.net/
- OWASP conformance layer — `docs/owasp/README.md`

## Related

`../git-workflow/STANDARD.md` · `../../40-security/secrets-management/STANDARD.md` ·
`../../70-delivery/ci-cd-containerization/STANDARD.md` · [[MEMORY]]
