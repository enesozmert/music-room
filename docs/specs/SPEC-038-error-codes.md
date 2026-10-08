---
id: SPEC-038
title: Every refusal carries a stable code, in a sibling dictionary the client reads
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.4, V.6]
decisions: [ADR-0045, ADR-0043, ADR-0005]
standards: [rest-api-design, application-security, testing]
approved_by: enesozmert # gates 4 and 5 answered on 2026-10-07, before the spec was written
escalation: |
  **Gates 4 and 5 are yes, and the owner answered both before this spec existed.**

  Gate 4: the `422` body gains an extension member and `docs/api/ENDPOINTS.md` documents it. That is what
  [[ADR-0045-stable-error-codes]] decided; the shape is in the record and this spec implements it rather than
  choosing it.

  Gate 5: three of the keys are on credential routes — `password` on register, `newPassword` on reset and
  change, `email` on the two mail routes. No credential logic moves. What changes is that a refusal names its
  rule as well as describing it.

  Gates 1, 2, 3, 6 and 7 answer no. ADR-0045 is implemented, not written or contradicted; no package —
  `WithErrorCode` and `ValidationFailure.ErrorCode` are FluentValidation's own; no migration; nothing lowered,
  deleted or skipped; nothing from `reference/`.

  **One thing this spec expects to break, and that is the design working.** `ValidationKeyContractTests` and
  the 26 key assertions SPEC-036 and SPEC-037 pinned are what fail first if the body is reshaped carelessly.
  They are left alone: `errors` keeps its keys, its order and its messages, and `codes` is a sibling.
---

# Stable error codes

## Goal

After this spec a client can switch on a refusal instead of parsing an English sentence. The body keeps the
shape SPEC-037 pinned and gains one sibling:

```json
{
  "status": 422,
  "errors": { "newPassword": ["Password must be at least 12 characters."] },
  "codes":  { "newPassword": ["PASSWORD.TOO_SHORT"] }
}
```

A refusal that carries no `errors` dictionary — the handler outcomes SPEC-036 enumerated — carries a single
`code` member instead, from the same vocabulary.

## Out of scope

- **Server-side localisation.** ADR-0045 rejected it with its reasons. `Accept-Language` stays unread and the
  message text stays English, for a developer reading a log rather than for an end user.
- **Changing any key, message or status.** `errors` is untouched. If a key moves, this unit has gone wrong.
- **The `500` body.** O29's handler is SPEC-039 and comes after this, so that it carries this vocabulary
  rather than inventing a second one.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it implements [[ADR-0045-stable-error-codes]] |
| 2 | new or upgraded dependency? | no — `WithErrorCode` and `ValidationFailure.ErrorCode` are FluentValidation's own API |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — the body gains a documented member |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — the credential routes' refusals gain codes |
| 6 | lowers a severity, deletes or skips a test? | no — it adds; no existing assertion changes value |
| 7 | touches or copies from `reference/`? | no |

Gates 4 and 5 are yes, so Tier B. Both were answered by the owner on 2026-10-07 — see `escalation`.

## Decisions this spec takes under ADR-0045

| # | Decision | Why |
|---|---|---|
| 1 | **One function emits both dictionaries**, from one `ValidationFailure` list, in one pass. | Two builders disagree within a month. The record says so and this is where it is enforced |
| 2 | **`ValidationCodes` is the only vocabulary**, a constants class in `MusicRoom.Application`. | A code typed as a literal at a rule is a second vocabulary nobody can grep |
| 3 | **A framework default is a test failure, not a fallback.** | FluentValidation fills `ErrorCode` with `NotEmptyValidator` and friends when nothing sets it. Those are implementation names; shipping one publishes an internal detail as a contract |
| 4 | **Handler refusals carry `code`, singular.** | They have no field to key on. Same vocabulary, different shape, and a client that handles codes handles both |
| 5 | **`openapi.json` declares the members.** | An undeclared extension is one no generated client can see, which is the same defect as not sending it |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Validation/ValidationCodes.cs` | create — the vocabulary, as constants, with the three binding rules in its documentation |
| `MusicRoom/src/MusicRoom/Application/Features/**/*Validator.cs` | change — `.WithErrorCode(ValidationCodes.X)` on every rule |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/CredentialEndpointResults.cs` | change — one function returns both dictionaries; a second returns the single-code extension |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/*.cs` | change — the `ValidationProblem` call sites pass the extension |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/ValidationCodeCoverageTests.cs` | create — every rule has a code, and no framework default survives |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ValidationCodeContractTests.cs` | create — the body carries `codes` with the same keys as `errors`, and a handler refusal carries `code` |
| `docs/api/ENDPOINTS.md` | change — §Conventions documents both members |
| `docs/api/openapi.json` | change — regenerated, with the members declared |
| `docs/SDD.md` | change — §16 and the issue list |
| `docs/specs/SPEC-038-error-codes.md` | change — status transitions |

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `ValidationCodeCoverageTests` — every rule | no rule reaches a response without a code, which is what keeps this true as validators are added |
| — no framework default | `NotEmptyValidator` and its siblings never ship as a contract |
| `ValidationCodeContractTests` — `codes` mirrors `errors` | same keys, same order, one function, so they cannot drift |
| — a handler refusal | carries `code` rather than an empty `errors` dictionary |
| the 26 key assertions from SPEC-036 and SPEC-037 | unchanged and still passing: `errors` did not move |
| `make agent-check` | the regenerated document matches the code, so the members are declared rather than undeclared |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] no existing key, message or status changed — the 26 key assertions are untouched and still pass
- [x] **a second empty key was found and closed.** `AddTrackCommandValidator`'s whole-command rule had no
  member expression and no override, so it published `{"errors":{"":[…]}}` — the same shape SPEC-037 fixed in
  `MoveTrackCommandValidator` and missed here. It keys on `atHead` now, with a test
- [x] `status: done` in this file
