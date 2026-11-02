---
id: SPEC-055
title: A six-character code for the password reset, and a mail that stops calling a token one
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.5, V.8]
decisions: [ADR-0024, ADR-0042, ADR-0043, ADR-0045]
standards: [authentication-authorization, application-security, testing]
approved_by: enesozmert # 2026-11-02, from the reset mail he received
escalation: |
  **Gates 3, 4 and 5 are yes.** A column pair on `password_reset_tokens`, a changed request body on
  `POST /auth/password-resets`, and the whole subject is a credential.

  **It starts as a defect, visible in the mail the owner received.** With no reset screen configured —
  which is every deployment until the client exists — `CredentialMailTemplates.Reset` printed the
  43-character **token** inside the code block, under the label *"Your verification code"*, while the
  sentence above it said *"Use the button to choose a new password"* and there was no button. Three
  things wrong in one message: a strong credential presented as a typed code, a label that lies about
  what the value is, and an instruction pointing at an element that is not there.

  **SPEC-052 refused a code here, and this reverses that.** Its reasoning was sound and is worth
  restating: six characters from a 32-symbol alphabet is about 1.07e9, and what makes that safe for
  email verification is the five-attempt budget, not the entropy. Password reset is the strongest
  action in the system — whoever completes it owns the account — so the same code is protecting more.

  **The owner asked for it anyway, on 2026-11-02, having seen the mail.** The reason it is acceptable
  is that the budget is what carries the weight, and it is carried identically here: five wrong codes
  spend the row, the sixth is refused whatever it says, and a new code can only be caused through
  `POST /auth/password-reset-requests`, which is rate-limited per IP and per account (SPEC-006) and
  sends the mail to the account's own address. An attacker gets five guesses out of 1.07e9 per mail
  the **victim** receives. What this unit does **not** do is widen that: no second window, no longer
  life, no separate row.
---

# A six-character code for the password reset, and a mail that stops calling a token one

## Goal

The reset mail carries a six-character code beside the link, in the same shape email verification
already uses. `POST /auth/password-resets` accepts either the token **or** the address and the code.
No message ever presents a 43-character token as something to type.

## Out of scope

- **A different alphabet or length.** `VerificationCode` already defines both, and a second definition
  is a second thing to get wrong.
- **A separate expiry for the code.** One row, one window — the entity's own remark says why.
- **Changing the reset window.** One hour stays, as `authentication-authorization` fixed it.
- **A code for the OIDC flows.** They carry their own single-use codes and are not this.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it reverses a SPEC-052 decision, which is recorded in both specs rather than in an ADR |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **yes** — `code_hash` and `attempts` on `password_reset_tokens`, both additive |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — the reset request body gains `email` and `code` |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is a credential path |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The code lives on the existing row, beside the token's digest.** | The link and the code prove the same thing about the same request. Spending either must spend both, and two rows would be two windows with one eventually outliving the other |
| 2 | **The same five-attempt budget, in the UPDATE's predicate.** | A row at the limit does not match, so the sixth attempt is refused even when the code is right. It is the budget, not the entropy, that makes six characters safe |
| 3 | **Either credential, never both required.** | A link that also demanded a typed code would make the link pointless; a code that demanded the token would make the code pointless |
| 4 | **The code path takes the address too.** | A six-character code is not unique across accounts. The address scopes the lookup, exactly as email verification's code path does |
| 5 | **The mail never prints the token as a code.** | The defect this unit starts from. With no link configured the message carries the code and says so; the token travels in a link or not at all |
| 6 | **One expiry, one hour, unchanged.** | The code is not a weaker credential with a longer life. It shares the row's window |
| 7 | **Wrong code, unknown address, spent row and expired row answer identically.** | `400`, as the token path already answers. A distinct answer would say which addresses hold an account |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/PasswordResetToken.cs` | change — `CodeHash`, `Attempts` |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/*` | create — one additive migration |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/PasswordResetTokenIssuer.cs` | change — mints the code |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Messaging/IMailService.cs` | change — `PasswordResetMail.Code` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/CredentialMailDispatcher.cs` | change — carries it |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RequestPasswordReset/RequestPasswordResetCommandHandler.cs` | change — passes it |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommand.cs` | change — token **or** address and code |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandValidator.cs` | change — one credential required |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandHandler.cs` | change — picks the path |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — the code consumer |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IPasswordContextReader.cs` | change — context by address |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.PasswordReset.cs` | create — the code path, budget included |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PasswordContextRepository.cs` | change — context by address |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/CredentialMailTemplates.cs` | change — the code block, and no token in it |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/MailCopyBook.cs` | change — words that match what is in the message |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PasswordResetEndpoints.cs` | change — the request shape |
| `MusicRoom/tests/**` | change — the code path, the budget, the mail |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json` | change — the request body |
| `docs/SDD.md` | change — §13.1 |
| `docs/specs/SPEC-052-*.md` | change — its decision is reversed, and says so |

## Verification

| Check | Proves |
|---|---|
| the reset mail, rendered with no link configured | carries the six-character code and no 43-character value |
| the reset mail, rendered with a link | carries both the button and the code; the token appears only inside the link |
| `POST /auth/password-resets` with `email` + `code` + `newPassword` | `204`, password replaced, every refresh token revoked |
| the same code a second time | `400` — one row, spent once |
| five wrong codes then the right one | `400`, because the row is spent by the budget |
| a wrong code for an address with no account | `400`, identical to a wrong code for one that exists |
| 20 parallel resets with one code | exactly one `204`, one `consumed_at_utc`, one password change |
| the `422` for a weak new password on the code path | keyed on `newPassword`, as the token path already answers |

## Done when

- [x] the reset mail carries a six-character code and never a token as text
- [x] both credentials work, and either spends the row
- [x] the attempt budget refuses the sixth guess
- [x] `make agent-check` passes — 732 tests, 0 failures
- [x] `status: done` in this file
