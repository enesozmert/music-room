---
aliases: [specs, spec-index]
kind: spec-index
updated: 2026-09-24
---

# Specs

A **spec** is a small, disposable work order for one unit of work: what changes, where, and how it
is verified. It is the unit an agent can execute unattended.

Specs are **not** decisions. A decision is an ADR. A spec implements decisions that already exist.

## Lifecycle

```
draft  ->  approved  ->  in-progress  ->  done
                    \->  blocked
```

Status lives in the spec's frontmatter. An agent moves `approved -> in-progress -> done`.
Who may move `draft -> approved` depends on the tier below.

## Two tiers

| Tier | What it covers | Who approves |
|---|---|---|
| **A — routine** | implementation entirely inside existing ADRs, standards and the endpoint contract | the agent may self-approve **only if every gate answer is "no"** |
| **B — gated** | anything else | a human, explicitly |

### The gate — answer all seven before self-approving

1. Does this need a new ADR, or contradict an accepted one?
2. Does this add or upgrade a dependency?
3. Does this change the schema beyond what this spec states, or run a destructive migration?
4. Does this change the public contract in `docs/api/ENDPOINTS.md`?
5. Does this touch authentication, authorisation, tokens, secrets, or the audit log?
6. Does this lower an analyzer severity, delete a test, or skip a test?
7. Does this change anything under `reference/`, or copy from it?

**Any "yes" makes it Tier B.** Set `status: draft`, write `escalation:` explaining which gate
tripped, and stop. Do not proceed on an assumption.

## Naming

`SPEC-<nnn>-<kebab-slug>.md`, numbered sequentially. Start from `SPEC-TEMPLATE.md`.

## Retention

A spec is disposable. Once `done` and merged, it may be deleted — the durable record is the ADR,
the standard, the SDD and the code. Keep a spec only while it is useful.

Specs are **not** part of the Brain. The Brain holds decisions and structure, not work orders.

## Related

[[delivery-plan]] · [[sdd]] · [[decisions-index]] · [[prompt-library]] · `../../AGENTS.md`
