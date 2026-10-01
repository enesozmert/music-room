---
aliases: [HOME]
kind: human-entry-point
updated: 2026-09-23
---

# music-room — Home

Human entry point. Agents start at [[MEMORY]] instead.

> **École 42 group project.** A collaborative music platform: live track voting, real-time
> playlist editing, and delegated music control. Current work scope: **backend**.

## Four layers, one information architecture

| Layer | Lives in | Read it for |
|---|---|---|
| AI retrieval | `brain/` → [[MEMORY]] | fast, structured context for an agent |
| Human navigation | this vault → **HOME**, [[knowledge-map]] | browsing, graph view, backlinks |
| Authoritative docs | `docs/technical-guide/` → [[technical-guide-index]] | the rules you must follow when writing code |
| Security conformance | `docs/owasp/` → [[owasp-index]] | what "OWASP-conformant" means here, concretely |
| Implementation | `MusicRoom/` | the truth about what the code actually does |

They are **views over the same files**, not copies. Editing a note in Obsidian edits the Brain.

## First time here?

Read [[user-manual]] once — directory map, workflows, maintenance rules, glossary.
Working with an agent? [[prompt-library]] has the prompts this structure is designed around —
Part 1 agent-agnostic, Part 2 unattended runs, Part 3 per-tool extras. The shared contract every
agent reads is `AGENTS.md`.

## Start here

### I want to understand the project

1. [[subject-requirements]] — what the 42 subject actually demands
2. [[sdd]] — the consolidated design, with a full requirements traceability table
3. [[system-overview]] — the short version of the same picture
4. [[project-registry]] — what components exist
5. [[decisions-index]] — why the stack is what it is

### I want to know what to build next

1. [[delivery-plan]] — phases, the decision queue, the vertical slices, the definition of done
2. [[sdd]] §17 — the open decisions those phases close

### I am about to write backend code

1. [[musicroom-backend]] — the project card
2. [[clean-architecture-cqrs]] — where the code goes
2b. [[owasp-index]] — every line of code here must be OWASP-conformant
3. [[endpoint-inventory]] — the contract you are implementing
4. [[rest-api-design]] or [[signalr-realtime]] — depending on the transport
4. [[secrets-management]] — before you touch any key

### I am reviewing a pull request

1. [[git-workflow]] — process rules
2. the standard for the area changed — [[technical-guide-index]]
3. [[testing]] — what must be proven

### I am preparing the defence

1. [[subject-requirements]] — the checklist you are graded on
2. [[sdd]] §16 — requirement-by-requirement traceability, and §17 for what is still open
3. [[decisions-index]] — every choice, with its rejected alternatives
3. [[load-testing]] — the capacity number and how it was measured
4. [[threat-register]] and [[asvs-conformance]] — the security story: what we defend, what we
   accepted, and why the scope is what it is

## Current state — 2026-09-28

- **Built so far:** the walking skeleton; the whole identity surface — registration, email verification
  and resend, login, JWT plus refresh rotation with reuse detection, password reset, credential rate
  limits, bearer validation and the session endpoints; the audit log with its mandatory client headers
  and its refusal outcomes; and own-profile editing with per-field visibility plus music preferences.
  Work lands as one draft pull request per spec and they are stacked and unmerged, so `main` lags —
  **`docs/specs/` has the statuses and `make agent-check` has the test count; this page counts neither.**
- Next: the friends graph, which is what makes the `Friends` visibility tier mean something, then
  reading another user's profile with per-field projection. Slice S1a is still blocked on O17, a mail
  provider account that does not exist yet.
- **All 29 architectural decisions are closed**, and four decisions taken during S1 and S2 are still
  waiting for an ADR — listed in [[musicroom-backend]] "Open items". Scope: all three services.
- **The API contract is written** — [[endpoint-inventory]] — and `docs/api/openapi.json` is generated
  from the code: nine paths, two health and seven identity.
- **Write backend code under `MusicRoom/`. `reference/` is read-only** — study material only,
  never edited and never copied from. See [[reference-narchitecture]].
- Stack decided: .NET 10 backend, Supabase (managed PostgreSQL) datastore, React Native client (later), YouTube provider, SignalR realtime, BCrypt password hashing, OIDC social login.
- Security: OWASP ASVS 5.0.0 is the conformance target — [[owasp-index]]. Code-size limits are analyzer-enforced via `.editorconfig`.
- Still open on purpose: SignalR backplane (O8), concrete schema (O9, the first migration settles
  it), `Makefile`/compose (O10, slice S0), remote deployment target (O11).
- Team of two: one backend, one UI. The API contract is the interface between them.
- Mobile client is out of scope for now — [[musicroom-mobile]].

## Conventions in this vault

- `brain/` is small on purpose. Facts, decisions and pointers — never copied source, never logs.
- `docs/technical-guide/` holds the standards. The Brain links to them and never restates them.
- `UNKNOWN - requires targeted inspection` is a valid, deliberate answer. Do not replace it with a guess.
- `reference/` and `docs/subject/` are excluded from search and graph — see [[obsidian-setup]].

## Related

[[MEMORY]] · [[sdd]] · [[delivery-plan]] · [[owasp-index]] · [[endpoint-inventory]] · [[user-manual]] · [[prompt-library]] ([[prompt-library-tr|TR]]) · [[specs]] · [[agent-runner]] · [[knowledge-map]] · [[technical-guide-index]] · [[obsidian-setup]]
