---
aliases: [knowledge-map]
kind: map-of-content
updated: 2026-09-23
---

# Knowledge Map

How every note in this vault relates. Use the graph view alongside this page.

## The retrieval chain

```
MEMORY  ->  project-registry  ->  project card  ->  architecture / ADR / standard  ->  source code
```

Each arrow is a deliberate narrowing. Never skip a step by searching the whole vault.

## Clusters

### Entry points

- [[HOME]] — humans
- [[MEMORY]] — agents
- [[user-manual]] — how the repository is operated
- [[prompt-library]] — prompts, autonomous recipes, per-tool extras
  ([[prompt-library-tr]], Türkçe okuma kopyası)
- [[specs]] — disposable work orders an agent can execute unattended
- [[agent-runner]] — the unattended driver and what keeps it safe
- [[knowledge-map]] — this page
- [[obsidian-setup]] — how the vault is configured

### Components

- [[project-registry]]
  - [[musicroom-backend]] — the active work
  - [[musicroom-mobile]] — deferred
  - [[reference-narchitecture]] — read-only study material

### Requirements and constraints

- [[subject-requirements]] — distilled from the 42 subject PDF, authoritative

### Planning

- [[delivery-plan]] — phase order, decision queue, vertical slices, risk register.
  Sequences work; decides nothing. Status lives in [[sdd]] §16, not here.

### Contract

- [[endpoint-inventory]] — the REST and hub surface, written before the code. The interface
  between the backend and the UI workstream.

### Design

- [[sdd]] — the consolidated design document: viewpoints, sequences, traceability, open issues.
  Consolidates the architecture notes below; it is not the source of truth for any of them.

### Architecture

- [[system-overview]] — the whole picture
  - [[domain-model]] — aggregates and invariants
  - [[realtime-architecture]] — hubs, concurrency, race conditions
  - [[auth-and-identity]] — identity, authorisation, the audit log

### Decisions

- [[decisions-index]]
  - [[ADR-0001-backend-dotnet-narchitecture]]
  - [[ADR-0002-mobile-react-native]]
  - [[ADR-0003-music-provider-youtube]]
  - [[ADR-0004-realtime-signalr]]
  - [[ADR-0005-api-style-rest-json]]
  - [[ADR-0006-datastore]] — still open

### Security conformance (separate layer, linked not copied)

- [[owasp-index]] — what OWASP-conformant means here
  - [[asvs-conformance]] — the committed ASVS 5.0.0 scope
  - [[owasp-top10-checklist]] · [[owasp-api-top10-checklist]] — per-PR review
  - [[threat-register]] — subject V.6 residual risk
  - [[owasp-cheatsheets]] · [[owasp-provenance]]

### Standards (separate layer, linked not copied)

- [[standards-router]] — the Brain's pointer
- [[technical-guide-index]] — the library itself, in `docs/technical-guide/`

### Operational history

- [[migrations-index]] — schema transitions, one row per migration; it is the authority, so nothing else counts them
- [[lessons-index]] — durable gotchas only

## Which layer owns what

| Question | Owner |
|---|---|
| "What is required of us?" | [[subject-requirements]] |
| "Why did we choose X?" | an ADR |
| "How is the system shaped?" | an architecture note |
| "What rule must my code follow?" | a standard in `docs/technical-guide/` |
| "What does secure mean here?" | [[owasp-index]] |
| "What did we decide not to defend against?" | [[threat-register]] |
| "What does the code do right now?" | the code |
| "How does the whole design hang together?" | [[sdd]] |
| "Is requirement X covered?" | [[sdd]] §16 |
| "What do we build next?" | [[delivery-plan]] |
| "How do I work in this repo?" | [[user-manual]] |
| "How do I ask an agent for this?" | [[prompt-library]] |
| "What may an agent do without me?" | [[specs]] gate, [[agent-runner]] |

If two layers disagree, the order of authority is: subject → ADR → standard → note → code comment.

## Maintenance rules

- A new component gets a row in [[project-registry]] and its own card. Nothing else.
- A new decision gets an ADR and a row in [[decisions-index]]. Do not edit an accepted ADR;
  supersede it.
- A new rule goes into an existing standard where one fits. A new standard folder is a last resort.
- Never add a note purely to make the graph look connected.
