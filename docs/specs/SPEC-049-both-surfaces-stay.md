---
id: SPEC-049
title: Both authentication surfaces stay, and the reason is written down
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.4]
decisions: [ADR-0031, ADR-0032, ADR-0008]
standards: [authentication-authorization, rest-api-design]
approved_by: enesozmert # answered 2026-10-11, asked before any client work began
escalation: |
  **Gates 1 and 4 are yes: an accepted ADR loses one of its sentences, and the contract loses a paragraph.**
  Gates 2, 3, 6 and 7 are no. Gate 5 is a yes in the sense that the subject is the credential path, though no
  code changes in this unit.

  **The contradiction, found before writing a line of the client.** Two documents say incompatible things:

  - `docs/api/ENDPOINTS.md`: *"**These replace rather than join** `POST /auth/sessions`, `POST
    /auth/sessions/social` and `POST /auth/tokens`."*
  - ADR-0031: *"the endpoints S1 shipped are **replaced, not kept alongside**."*
  - And SPEC-044's owner answer **A**: `GET /oauth2/authorize` **requires a bearer access token**.

  Under A the credential endpoints are the **precondition** of the authorization endpoint: a client signs in
  with a password or a provider, and only then can it drive the code flow. Removing them would leave the
  provider surface with no way for anybody to become authenticated in the first place — a flow whose entry
  condition cannot be met. The plan that said "replace" was written before the answer that made replacement
  impossible, and SPEC-041's phases 3 and 4 inherited it.

  **Owner's answer, 2026-10-11: both surfaces stay, and the reason is written down.** Three options were put;
  the second was rejected again and the third was never on the table:

  | | **A — both stay (taken)** | **B — authorize renders a login page** | **C — defer the decision** |
  |---|---|---|---|
  | What the client uses | the credential endpoints, as it always would have | HTML served by this API | unknown, which is the problem |
  | What the provider surface is for | conformance, ID tokens, and a client that wants the standard flow | a general-purpose provider | unclear |
  | What this API grows | nothing | HTML views, a cookie session, CSRF, a password form, a second authentication path | nothing, and a contradiction stays published |
  | Cost | two documents lose a sentence each | the "JSON API" decision in SDD §9.1 is reversed, outside the subject | the client is written against endpoints the documents say are going away |

  **What this does not do is quietly keep both and say nothing.** The honest claim at defence is specific:
  *the API implements the OpenID provider role, our client authenticates through the credential endpoints,
  and the two coexist because a first-party provider has no sign-in surface of its own.* That sentence is
  worth more than a tidier contract, and it is now in the ADR rather than in somebody's memory.
---

# Both authentication surfaces stay

## Goal

The published documents stop contradicting the code. `ENDPOINTS.md` says the two surfaces coexist and why,
ADR-0031 carries the amendment, and SPEC-041's phases 3 and 4 are closed as not applicable rather than left
scheduled. After this, the client can be written against a contract that will not be withdrawn under it.

## Out of scope

- **Moving the access token to RS256.** Independent of removal, and its own unit — it was only ever bundled
  with phase 4 because phase 4 was going to rewrite the same code. Recorded as **O32**.
- **Removing anything.** Nothing is deleted here; the point of the unit is that nothing will be.
- **The client itself.** Next, and now unblocked.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0031 gains amendment 2, which withdraws one of its sentences |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** in subject, no in code — this unit changes no code |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why |
|---|---|---|
| 1 | **The credential endpoints and the provider surface both stay, permanently.** | Under SPEC-044's answer A the first is the precondition of the second. "Replace" was written before that answer existed |
| 2 | **ADR-0031 keeps its sentence visible and marks it withdrawn**, rather than being edited in place. | An accepted decision is a record of what was decided and when. Silently editing it would hide that the project learned something; the amendment is the evidence that it did |
| 3 | **SPEC-041's phases 3 and 4 are closed as not applicable**, with the reason, rather than deleted. | A plan that stops being correct is a finding. Deleting the rows would leave the next reader wondering whether removal was forgotten |
| 4 | **`ENDPOINTS.md` states what each surface is for**, not merely that both exist. | Two ways to sign in, with no stated reason, reads as indecision. With the reason it reads as a trade-off, which is what it is |
| 5 | **The RS256 access token becomes O32**, scheduled on its own. | It was coupled to phase 4 by accident of sequencing. Nothing about the signature depends on removing an endpoint |

## Files

| Path | Action |
|---|---|
| `docs/api/ENDPOINTS.md` | change — the "replace rather than join" paragraph becomes the coexistence rule, with its reason |
| `brain/decisions/ADR-0031-musicroom-as-openid-provider.md` | change — amendment 2 withdraws the removal sentence |
| `docs/specs/SPEC-041-social-sign-in.md` | change — phases 3 and 4 marked not applicable, with the reason |
| `docs/SDD.md` | change — §16's provider row stops promising removal, and O32 opens |
| `docs/DELIVERY-PLAN.md` | change — S8's row says both surfaces ship |
| `docs/specs/SPEC-049-both-surfaces-stay.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| `grep -rn "replace rather than join" docs/` | no document still promises a removal that cannot happen — the only hits left are this spec quoting the old text |
| the ADR's Decision section | the withdrawn sentence is still there, marked withdrawn and pointing at amendment 2, rather than edited away |
| SPEC-041's migration table | phases 3 and 4 say why they will not run |
| `make agent-check` | documentation-only: the suite and the generated document are unchanged |

## Done when

- [x] no document promises the credential endpoints will be removed
- [x] ADR-0031 carries amendment 2, with the withdrawn sentence quoted
- [x] O32 exists for the RS256 access token
- [x] `make agent-check` passes — 681 tests, 0 failed, on 2026-10-11
- [x] `status: done` in this file
