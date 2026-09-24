---
aliases: [owasp-index, owasp]
kind: security-conformance-index
updated: 2026-09-23
---

# OWASP Conformance Layer

**Every line of code written in this project must be OWASP-conformant.** This folder defines what
that sentence means concretely, so it is checkable rather than aspirational.

Decision that established it: [[ADR-0009-code-quality-gates]].

## What is here

| File | Purpose |
|---|---|
| [[asvs-conformance]] | the conformance target: which ASVS chapters, at which level, and why |
| [[owasp-top10-checklist]] | OWASP Top 10 mapped to this system's design elements |
| [[owasp-api-top10-checklist]] | API Security Top 10 mapped to this system's endpoints and hubs |
| [[threat-register]] | subject V.6 residual-risk register — threat, mitigation, what remains |
| [[owasp-cheatsheets]] | the cheat sheets that apply here, and what each one is for |
| `upstream/` | verbatim OWASP artifacts, CC BY-SA 4.0 — see [[owasp-provenance]] |

## How this layer relates to the others

| Layer | Role |
|---|---|
| `docs/owasp/` | **what secure means** — the external conformance target and its evidence |
| `docs/technical-guide/40-security/` | **how we implement it** — enforceable project rules |
| `docs/SDD.md` §13 | **how the system is designed** to satisfy it |
| `brain/decisions/` | **why** each security choice was made |
| `MusicRoom/` | the code that must conform |

No duplication: a rule lives in the standard, the conformance target lives here, the design lives
in the SDD. This folder never restates a standard.

## The rule, stated once

1. **ASVS Level 1 is mandatory** for every applicable chapter.
2. **ASVS Level 2 is required** for Authentication (V6), Session Management (V7),
   Authorization (V8), Self-contained Tokens (V9), OAuth and OIDC (V10), and
   Security Logging and Error Handling (V16) — the areas subject V.6 grades directly.
3. **Every pull request** is reviewed against [[owasp-top10-checklist]] and
   [[owasp-api-top10-checklist]].
4. **Anything not fixed goes in [[threat-register]]** with its residual risk and who accepted it.
   Subject V.6 asks for the remaining dangers to be identified and described — an unwritten risk
   is an oversight, a written one is an engineering decision.

## How to use it

| Situation | Do this |
|---|---|
| Writing an auth, session or token feature | read the ASVS V6/V7/V9/V10 requirements for it before coding |
| Adding an endpoint | walk [[owasp-api-top10-checklist]] — API1 (object-level auth) first |
| Reviewing a PR | [[owasp-top10-checklist]] plus the standard for the area touched |
| Finding something you cannot fix | add it to [[threat-register]], do not stay silent |
| Preparing for defence | [[threat-register]] plus [[asvs-conformance]] §Scope is the security story |

Prompt §5 in [[prompt-library]] runs a review against this layer.

## Related

[[application-security]] · [[authentication-authorization]] · [[secrets-management]] ·
[[sdd]] · [[subject-requirements]] · [[technical-guide-index]]
