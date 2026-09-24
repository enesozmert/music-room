---
kind: adr
id: ADR-0009
status: Accepted
date: 2026-09-23
---

# ADR-0009 — OWASP conformance and enforced code-size limits

## Context

Subject V.6 requires implementing protections and documenting residual threats; V.8 requires
quality and continuous integration. Both are graded by a human reading the code, so "we tried to
be secure" and "we tried to keep methods small" are not answers. A conformance target and machine
enforcement are.

Two separate things are being fixed here:

1. **Security conformance** — which external standard the code is measured against.
2. **Code size and shape** — limits that a build can check, so review argues about design rather
   than about line counts.

## Decision

### Security conformance target

**OWASP ASVS Level 1 is mandatory for every applicable chapter; Level 2 is required for the
identity and audit chapters** — ASVS 5.0.0 V6 Authentication, V7 Session Management,
V8 Authorization, V9 Self-contained Tokens, V10 OAuth and OIDC, V16 Security Logging and Error
Handling. These are the areas subject V.6 grades directly.

That is 142 of the 345 requirements in ASVS 5.0.0. The per-chapter scope, with the reason for each
inclusion and exclusion, is [[asvs-conformance]]. The standard itself is vendored verbatim under
`docs/owasp/upstream/asvs-5.0.0/` (CC BY-SA 4.0) — see [[owasp-provenance]].

Additionally:

- **OWASP Top 10** and **OWASP API Security Top 10** are the review checklist for every pull request.
- A finding that cannot be fixed is written into [[threat-register]] with its residual risk
  **before the pull request merges**. Silence is not acceptance; subject V.6 asks for the
  remaining dangers to be described.

### .NET naming conformance

Every identifier follows **Microsoft's C# naming and coding conventions** — no house dialect.
`IDE1006` (naming rule violation) is escalated to an error, so a deviation fails the build rather
than the review.

### Size and complexity limits

| Limit | Value | Applies to |
|---|---|---|
| Method body length | **40 lines** hard, 20 target | every method |
| Cyclomatic complexity | **10** | every method |
| Parameters | **5** | every method, excluding `CancellationToken` |
| Nesting depth | **3** | every method |
| Methods per type | **15** | every type; a CQRS handler has exactly one public method |
| File length | **300 lines** | every `.cs` file |
| Class coupling | **30** | every type |

Constructors, DI registration methods and generated code are exempt from the method-count limit
only; they are not exempt from the rest.

### Enforcement

- `.editorconfig` at the repository root is the single authority. Severities live there, not in
  individual project files.
- Microsoft's `Microsoft.CodeAnalysis.NetAnalyzers` covers complexity (CA1502), maintainability
  (CA1505) and class coupling (CA1506).
- Line-count and method-count limits are **not** expressible in the Microsoft analyzers.
  `SonarAnalyzer.CSharp` supplies them: S138 (method lines), S1541 (complexity), S107 (parameters),
  S134 (nesting), S1448 (methods per type), S104 (file lines), S1200 (coupling).
- `TreatWarningsAsErrors` is on. CI runs `dotnet format --verify-no-changes` and
  `dotnet build -warnaserror`.

## Consequences

- A build fails on a violation. That is the point: a limit that only appears in a document is a
  suggestion, and suggestions lose to deadlines.
- `SonarAnalyzer.CSharp` is an added NuGet dependency. It is a build-time analyzer, ships no
  runtime code, and is downloaded by restore — consistent with subject IV.1, which forbids
  committing library **source**, not using packages.
- The limits will occasionally be wrong for a specific method. The escape hatch is an explicit,
  justified `#pragma warning disable` with a comment naming the reason — visible in review,
  countable in the codebase. A silent global severity downgrade is not an option.
- These limits push handlers toward the thin shape [[clean-architecture-cqrs]] already requires.
  A handler that cannot fit in 40 lines is usually doing a second job.
- ASVS Level 2 for six chapters, rather than Level 2 everywhere, is a deliberate scope choice: it
  concentrates effort where subject V.6 is graded and avoids claiming 183 verified requirements
  that nobody on this team has verified.
- A new OWASP conformance layer exists at `docs/owasp/` — target, checklists, threat register,
  vendored standard. It states *what secure means*; the security standards state *how we build it*.

## Alternatives rejected

- **No numeric limits, review judgement only** — produces inconsistent review and endless
  line-count arguments.
- **Microsoft analyzers alone** — cannot express method length or method count, which are the two
  limits explicitly requested.
- **Full ASVS Level 2 across all chapters** — an unverifiable claim for a team this size; a
  conformance claim you cannot demonstrate is worse than a narrower one you can.
- **SonarCloud as a hosted gate** — extra account, extra CI coupling, no benefit over the local
  analyzer for this project's size.

## Affected

Standards: `dotnet-csharp` (v1.2.0), `application-security` (v1.1.0),
`engineering-baseline` (v1.1.0), `ci-cd-containerization` (v1.2.0).
Conformance layer: [[owasp-index]], [[asvs-conformance]], [[threat-register]].
Root config: `.editorconfig`.
