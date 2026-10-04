---
id: SPEC-027
title: ASVS conformance — evidence for what is built, and named gaps for what is not
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9              # which DELIVERY-PLAN slice this belongs to
requirements: [V.6, V.8]
decisions: [ADR-0009]
standards: [application-security, authentication-authorization, logging-observability, testing]
approved_by: enesozmert # blanket approval of outstanding specs, 2026-09-29
escalation: |
  **Gate 6 answers no, but read this anyway.** This unit writes down which ASVS requirements this project
  actually meets, which it does not, and which do not apply. It therefore *lowers* nothing and hides nothing —
  but it does say out loud, in a document meant for a defence, that some committed requirements are unmet. That
  is the point: `docs/owasp/ASVS-CONFORMANCE.md` still says *"No code exists yet, so no requirement is
  verified"*, which was true in September and is now the most out-of-date sentence in the repository.

  **Three gaps are real and get register rows rather than silence:** there is no authenticated
  change-password endpoint (only the reset flow), no breached-password or context-word check at
  registration, and nothing ships or protects the logs. None is a regression — each is a requirement this
  project committed to and has not built.

  **No code changes.** The documentation-class requirements are met by documenting; the code-class ones are
  met or not by code that already exists, and this unit does not write code to move a row.
---

# ASVS evidence

## Goal

After this spec `docs/owasp/ASVS-CONFORMANCE.md` states, for each of the 142 requirements this project
committed to, whether it is **met** with a named artifact, **partial**, **not met**, or **not applicable** with
a reason. The evidence is a test name, a code path, or a configuration file — never a claim. Subject V.6's
"document remaining threats" stops resting on a register alone, and S9's conformance half is done.

The counts in that document were checked against the vendored standard and are correct: 142 committed = 62 L1
across every chapter but V3 and V17, plus 80 L2 across V6, V7, V8, V9, V10 and V16, computed from
`docs/owasp/upstream/asvs-5.0.0/OWASP_Application_Security_Verification_Standard_5.0.0_en.csv`.

## Out of scope

- **Writing code to satisfy a requirement.** Where a requirement is unmet it is recorded unmet. Building the
  authenticated password change, the breached-password check or log shipping are their own units, and pretending
  otherwise in a conformance document is the one failure mode this document exists to avoid.
- **V10, OAuth and OIDC.** Twenty-nine committed requirements whose surface does not exist until S8. They are
  listed as not met with S8 named, not quietly dropped from the count.
- **Re-measuring capacity.** ADR-0030 wants a Gatling run with the hub limits in place; that needs SPEC-026 on
  `main` first, so it is the next unit rather than this one.
- **Raising or lowering the committed level of any chapter.** The commitment is ADR-0009's and the owner's.

## Decisions taken under the blanket approval

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **Four outcomes per requirement: met, partial, not met, not applicable** — and every one of them carries a reason or an artifact. | A two-state table invites "met" for anything arguable. ASVS itself treats not-applicable as a legitimate outcome when the technology is absent, so a row saying "no WYSIWYG editor exists" is more honest than a tick and more useful than a blank |
| 2 | **Evidence is a name**: a test method, a file and what it does, or a configuration file. No row says "the design handles this". | The document's own Verification section already set this rule in September; this unit is the first to be held to it |
| 3 | **Documentation-class requirements are met by documenting them here**, and the document says that is what happened. | Requirements like V6.1.1, V7.1.2, V8.1.1 and V16.1.1 ask that *documentation define* a rule. Writing that definition is the work, not a shortcut around it — but a reader deserves to know which rows were closed by a sentence and which by a test |
| 4 | **A requirement that is unmet gets a register row only when the gap is actionable**; otherwise it is marked not met with the slice that will cover it. | Twenty-nine V10 rows would mean twenty-nine register entries saying "S8 has not happened". The three gaps that are real, orthogonal and buildable today — password change, breached-password screening, log protection — get T-31, T-32 and T-33 |
| 5 | **TLS requirements are not met, and say so.** | V4.4.1 and all of V12 need a deployment that terminates TLS. There is none: issue O11. Claiming TLS because the code would work behind it is exactly the kind of claim a defence punctures |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0009 set the levels and this evidences them |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **it documents them** — no code path changes, and no secret appears |
| 6 | lowers a severity, deletes or skips a test? | no — and it records three requirements as unmet rather than leaving a document that implies otherwise |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `docs/owasp/ASVS-CONFORMANCE.md` | change — the evidence pass: per-chapter tables, a status that is true, and the four outcomes |
| `docs/owasp/THREAT-REGISTER.md` | change — T-31 no authenticated password change, T-32 no breached-password or context-word screening, T-33 logs are neither protected nor shipped |
| `docs/SDD.md` | change — §13.4 gains the log inventory V16.1.1 asks for; §16's V.6 documentation row says what is now evidenced |
| `docs/DELIVERY-PLAN.md` | change — S9's remaining list |
| `brain/projects/musicroom-backend.md` | change — S9 |
| `docs/specs/SPEC-027-asvs-evidence.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** None. No code in this unit.

**2. Boundary types.** None.

**3. Invoked?** Nothing is invoked. The deliverable is read at a defence.

**4. CLI or package.** None.

**5. corePackages.** Unchanged.

**What the walk caught.** Two things.
- **The log inventory belongs in the SDD, not in the conformance document.** V16.1.1 asks for an inventory of
  what is logged at each layer; the conformance document should cite it, not be it, or the next reader has two
  places to look and one of them is a checklist.
- **The three new register rows are gaps, not threats discovered today.** They are written as what they are:
  requirements this project committed to and has not built, with the exposure each one leaves.

## Steps

1. Compute the committed set from the vendored CSV, and check the document's own counts against it.
2. Walk each chapter, assign one of the four outcomes per requirement, and name the artifact.
3. Replace the stale Status section with one that states what is evidenced and what is not.
4. Add the log inventory to `docs/SDD.md` §13.4, and the three rows to the register.
5. Update §16's V.6 documentation row, the delivery plan and the project card.
6. `make agent-check` — the PDFs regenerate, which is the whole build surface this unit touches.

## What the pass itself produced

**The summary table I drafted first was wrong.** I wrote 71 met / 9 partial / 39 not met / 23 not applicable
from memory of the walk, then counted the rows programmatically: **63 / 5 / 50 / 24**. The numbers in the
document are the counted ones, and the lesson is the one this unit is about — a conformance figure that was
not computed is a guess wearing a table.

**The outcome spread is the honest headline.** 63 of 142 met is less than half, and that is what a project
with no deployment, no second factor and no OIDC surface should report: 29 of the 50 unmet are V10, 3 are TLS,
and 11 are V6 rows about factors and password screening that were never built. The N/A count, 24, is mostly
file handling, XML, OTP channels and browser concerns that this system genuinely does not have.

**Two requirements were closed by a document that did not exist before:** the log inventory V16.1.1 asks for,
now in [[sdd]] §13.4, and the dependency remediation timeframe V15.1.1 asks for, stated in the conformance
document and enforced by the gate rather than by a calendar.

**One cross-branch hazard caught in review:** three rows cited SPEC-026's hub limits, which are open as a pull
request and not on `main`. They now say so, because a conformance document that claims a control a reader
cannot find in the code is worse than one that admits a gap.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| the committed counts, recomputed from the CSV | 142 = 62 L1 + 80 L2, so the scope table is arithmetic rather than assertion |
| every row in the evidence tables | carries an outcome **and** either an artifact or a reason |
| `docs/owasp/ASVS-CONFORMANCE.md` Status | no longer claims that no code exists |
| T-31, T-32, T-33 | the three unmet requirements have somewhere to live, with their residual risk stated |
| `docs/SDD.md` §13.4 | the log inventory exists, so V16.1.1 is met by a document rather than by intent |
| `make agent-check` | unchanged behaviour: no code moved, and the PDF guards stay in step |

## Done when

- [x] every committed requirement carries an outcome and an artifact or a reason
- [x] the Status section is true
- [x] T-31 to T-33 exist with their residual risk
- [x] `make agent-check` passes
- [x] `status: done` in this file
