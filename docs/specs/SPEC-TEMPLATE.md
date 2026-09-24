---
id: SPEC-000
title: <one line, imperative>
status: draft          # draft | approved | in-progress | done | blocked
tier: A                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S0              # which DELIVERY-PLAN slice this belongs to
requirements: []       # subject refs, e.g. [V.1.5, V.6.1]
decisions: []          # ADRs this implements, e.g. [ADR-0007, ADR-0013]
standards: []          # standards that apply, e.g. [authentication-authorization, testing]
approved_by:           # name, or "agent" for a clean Tier A gate
escalation:            # which gate tripped, if status went back to draft
---

# <title>

## Goal

One paragraph. What will be true after this that is not true now.

## Out of scope

What this deliberately does not do. Prevents drift during an unattended run.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | no |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Files

Exact paths, created or changed. An unattended run must not touch anything outside this list.

| Path | Action |
|---|---|
| `MusicRoom/src/...` | create |

## Steps

1. …
2. …

## Verification

Commands that must pass, and what each one proves. Include the layer tests the `testing` standard
requires for what is touched.

Until the root `Makefile` exists (slice S0 creates it), use raw commands:

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
```

After S0:

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `<test name>` | `<requirement>` |

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] every command under Verification passes
- [ ] OWASP checklists walked for the surface touched; unfixed findings in the threat register
- [ ] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [ ] `status: done` in this file
