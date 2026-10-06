---
kind: adr
id: ADR-0045
status: Accepted
date: 2026-10-06
---

# ADR-0045 — Every refusal carries a stable code, and the text stays English

## Context

The API answers a refused request with RFC 9457 `ProblemDetails`: a `422` carries an `errors`
dictionary of field names to messages, and SPEC-037 makes those field names the ones the client sent.
The **messages** are English sentences written inline in validators — *"Password must be at least 12
characters."*

A mobile client cannot show that to a Turkish-speaking user. It can translate nothing, because a
sentence is not an identifier: the server may reword it for clarity and every client that pattern-matched
on the old wording breaks silently. The client needs something it can switch on.

Two ways to get there, and this record rejects one of them.

**Server-side localisation** — `AddLocalization`, `IStringLocalizer`, `.resx` per culture, a
`RequestLocalization` middleware reading `Accept-Language`. None of it exists today: zero hits for
every one of those names. It would mean the server owns the translations, ships a resource file per
language, and answers in whatever the header asked for.

**Codes, translated by the client.** The server names the rule; the client owns the words.

## Decision

### The server sends codes; the client owns the language

A refusal carries a **stable code**. The response text stays English and is for a developer reading a
log or a `curl`, never for an end user. The mobile client maps a code to its own string table.

Three reasons this way round:

1. **The client knows the context the server does not.** The same `PASSWORD.TOO_SHORT` is phrased
   differently in a registration form and in a settings screen, and only the screen knows which it is.
2. **Translations ship with the thing that renders them.** A new language is a client release, not a
   server release — and for a client store release that is the cheaper of the two anyway.
3. **`Accept-Language` is a negotiation the server would then have to honour everywhere**, including in
   logs, mail and audit records, each with its own correct answer. The audit log must stay in one
   language to be searchable; mail already has its own templates. Localising only the API's error body
   while those stay English is the inconsistency, not the fix.

Server-side localisation is therefore **rejected**, and the three names above stay absent on purpose.

### The code is an extension member, and `errors` is untouched

The body keeps its standard shape and gains one extension member, which RFC 9457 explicitly allows:

```json
{
  "type": "...", "title": "...", "status": 422,
  "errors": { "newPassword": ["Password must be at least 12 characters."] },
  "codes":  { "newPassword": ["PASSWORD.TOO_SHORT"] }
}
```

- `errors` stays `IDictionary<string, string[]>`, which is what `HttpValidationProblemDetails` and the
  committed `openapi.json` already describe. A reader that knows nothing about codes keeps working.
- `codes` carries the same keys in the same order, so a client reads one and renders the other.
- **Both are produced by one function from one `ValidationFailure` list**, so they cannot drift. Two
  dictionaries built in two places would disagree within a month.

Replacing `errors` with a list of objects was considered and rejected: it abandons the standard type,
changes a schema every client library already understands, and buys nothing a sibling member does not.

### A handler refusal carries a code too, in `code`

Not every `422` comes from a validator. A wrong current password, a stranger on a control grant and the
presence refusals are handler outcomes that answer `Results.Problem` with **no `errors` dictionary at
all** — SPEC-036 enumerated them. Those carry a single extension member:

```json
{ "status": 422, "title": "Password not changed", "code": "PASSWORD.CURRENT_INCORRECT" }
```

Same vocabulary, same stability guarantee. A client that handles codes handles both shapes.

### The vocabulary

`AREA.RULE`, uppercase, dot-separated, ASCII:

| Code | Means |
|---|---|
| `EMAIL.REQUIRED` | the field was absent or blank |
| `EMAIL.INVALID` | the field is not an address |
| `PASSWORD.TOO_SHORT` | below the 12-character minimum |
| `PASSWORD.BREACHED` | found in the bundled set — SPEC-035 |
| `PASSWORD.CONTEXT_WORD` | contains the caller's own words — SPEC-035 |
| `PASSWORD.CURRENT_INCORRECT` | the handler refused the current password — SPEC-028 |

The implementing spec writes the full table. Three rules bind it:

1. **A code is a contract.** Once published it is never reworded, never recased and never reused for a
   different meaning. Retiring one means adding its replacement and keeping both until the client
   releases.
2. **The codes live in one file**, as constants, not as string literals at each rule. A literal typed
   twice is two codes.
3. **No framework default ever ships.** FluentValidation fills `ErrorCode` with `NotEmptyValidator`,
   `MinimumLengthValidator` and the like when nothing sets it. Those are implementation names, not a
   contract, and a test fails if one reaches a response.

### The mechanism is FluentValidation's own

`.WithErrorCode(ValidationCodes.PasswordTooShort)` on each rule; the behaviour already carries
`ValidationFailure.ErrorCode` through to the endpoint, so nothing new travels. The constants file is the
only new type on the validator side.

## Consequences

- The `422` body gains a member. The committed `openapi.json` changes, because the extension is
  documented rather than undeclared — an undocumented member is one no generated client can see.
- Every rule in 25 validators gains a code, and a test asserts that no response carries a framework
  default. That test is the one that keeps this honest as validators are added.
- The client can localise without the server knowing any language. `Accept-Language` stays unread, and
  `docs/api/ENDPOINTS.md` §10 says so rather than leaving a reader to wonder.
- This is a second contract change to the same body after SPEC-037's key renaming. Both are taken now,
  before `MusicRoomApp` exists, for the same reason: the cost today is a test update.
- Codes make the audit trail better by accident — a refusal reason that is a stable token is one a
  query can group by, which free text never was.

## Related

[[ADR-0043-validation-error-key-naming]] · [[ADR-0005-api-conventions]] · [[endpoint-inventory]] ·
[[rest-api-design]]
