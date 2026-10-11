---
id: SPEC-052
title: A six-character code beside the link, and mail that reads like the product
status: approved        # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1a              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.8]
decisions: [ADR-0013, ADR-0028, ADR-0045]
standards: [authentication-authorization, application-security, rest-api-design, testing]
approved_by: enesozmert # asked for on 2026-10-11, with the live deployment answering
escalation: |
  **Gates 3, 4 and 5 are yes.** One additive migration, two contract rows, and the whole unit is the
  credential path. Gates 1, 2, 6 and 7 are no — no ADR moves, no package (the templates are embedded
  resources and the substitution is `string.Replace`, not a templating engine, which would need ADR-0021
  amended), nothing lowered, nothing from `reference/`.

  **The security question this unit has to answer out loud: a six-character code is a weak secret.**
  Today's verification token is 256 bits and unguessable, so nothing counts attempts against it. A code a
  person reads off a screen cannot be that, and the honest numbers are:

  | Alphabet | Codes | Chance per guess | With 5 attempts |
  |---|---|---|---|
  | `A-Z0-9`, 6 chars | 2,176,782,336 | 1 in 2.2 billion | 1 in 435 million |
  | **32 symbols** (`A-Z` without `I`,`O`; `2-9`), 6 chars | 1,073,741,824 | 1 in 1.07 billion | 1 in 215 million |

  The smaller alphabet is the one taken, and the reason is not cryptographic: `I`/`1` and `O`/`0` are the
  two pairs a person transcribes wrongly, and a code nobody can read produces resends, which is a real
  availability cost against a difference of one bit per character.

  **What makes it safe is not the entropy; it is the budget.** Five wrong codes burn the record, so an
  attacker gets five guesses per issued code and then has to cause a new mail — through an endpoint
  SPEC-006 already rate-limits per IP and per account. Without the budget a six-character code is a
  liability; with it, it is what every bank and every provider uses.

  **The link keeps its own 256-bit token.** The link and the code are two credentials on one record, not
  one credential in two places. Making the link carry the code would quietly reduce the link to six
  characters, which is the kind of change nobody notices until it is the only one that mattered.

  **The window shortens from 24 hours to 15 minutes**, which is the owner's ask and is right for a code.
  The cost is real and belongs here: a mail delayed past fifteen minutes forces a resend, and resend is
  rate-limited. A code that lives a day is a code sitting in an unlocked inbox for a day.

  **The password reset keeps its link and gets no code.** A six-character code that *replaces a password*
  is a weak credential for the strongest action in the system. The reset mail gets the new design and the
  new languages; it does not get a code.

  **Language comes from the request, not from a stored preference.** `Accept-Language` decides, `en` is the
  default, and `tr` is the other. A stored preference is a settings screen, a migration and a second place
  for the answer to be wrong; a header is what the client already sends and what the person is reading the
  app in at that moment.
---

# A six-character code beside the link, and mail that reads like the product

## Goal

A person verifying an address can click a link **or** type a six-character code, in Turkish or in English,
from a message that looks like it came from a product rather than from a test harness. A new account gets
one welcome message, once. Every message is black on white, legible at a glance, and readable by a screen
reader.

## Out of scope

- **A code for the password reset.** Link only — the escalation says why.
- **A stored language preference.** The request decides; a preference is a settings screen this project
  does not have.
- **A marketing list, unsubscribe handling or a second welcome.** One message, once, transactional.
- **A templating engine.** `{{placeholder}}` substitution over an embedded resource. A dependency would
  need ADR-0021 amended, and three messages do not justify it.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **yes** — additive columns, declared below |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — the verification request body, and the shorter window |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **One record carries two credentials**: a 256-bit link token and a six-character code, each hashed, both single-use. | The link keeps the strength it has today. Reusing the code in the link would silently reduce the link to six characters |
| 2 | **The alphabet is 32 symbols** — `A-Z` without `I` and `O`, digits `2-9`. | The two pairs a person mistypes are removed. One bit per character buys less than a code somebody can read aloud |
| 3 | **Five attempts per record, then it is spent.** | What makes a short code safe. The sixth attempt is refused even if correct, and the person asks for a new mail |
| 4 | **Fifteen minutes, for both the link and the code.** | The owner's ask, and right for something typed from an inbox. The cost — a delayed mail needs a resend — is the trade, and resend is rate-limited |
| 5 | **A wrong code and an unknown code answer identically**, as a spent one does. | Otherwise the endpoint says which addresses have a verification outstanding |
| 6 | **`Accept-Language` picks the language; `en` is the default and `tr` the alternative.** | The header the client already sends, read once in middleware and carried on `ClientContext` beside the platform and the device |
| 7 | **Templates are embedded HTML and text, one pair per message per language.** | Reviewable in the same diff as the code that sends them — the reason SPEC-024 refused a vendor dashboard template. Text beside HTML because a mail client that refuses HTML must still be able to verify |
| 8 | **Black on white, WCAG AA, and a dark-mode variant.** | Contrast 21:1 either way, a 16px floor, a 44px touch target, `lang` on the document, a visible focus style, and the code never conveyed by colour alone — it is in text a screen reader reads out |
| 9 | **The welcome message is sent once, on the first sign-in after verification**, stamped on the account. | "After the user signs in", without a message on every sign-in. The stamp is what makes once mean once |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/EmailVerificationToken.cs` | change — the code's digest and the attempt count |
| `MusicRoom/src/MusicRoom/Domain/Entities/User.cs` | change — when the welcome message was sent |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/VerificationTokenIssuer.cs` | change — mints both credentials, fifteen minutes |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/VerificationCode.cs` | create — the alphabet, the length, the generator |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Messaging/IMailService.cs` | change — one request record per message, carrying the language |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Auditing/IClientContextAccessor.cs` | change — the language the caller asked for |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — spend a code, and stamp the welcome |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/VerifyEmail/` | change — a code is accepted as well as a token |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/SessionIssuer.cs` | change — **every** way in sends the welcome, not only the password one |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/` | change — two configurations |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.Verification.cs` | create — a partial, because `UserRepository.cs` is at the file-size cap (O27) |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/` | create — additive columns |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/Templates/` | create — **one** layout, HTML and text; the words are per language in a copy book, not twelve files |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/MailCopyBook.cs` | create — three messages × two languages |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/WelcomeMailSender.cs` | create — stamp, read, send |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/MailTemplateRenderer.cs` | create — load, substitute, no engine |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/CredentialMailTemplates.cs` | change — the copy moves into the templates |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/BrevoMailService.cs` | change — the new requests |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/LoggingMailService.cs` | change — the same |
| `MusicRoom/src/MusicRoom/WebAPI/Middleware/ClientContextMiddleware.cs` | change — reads `Accept-Language` |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AuthEndpoints.cs` | change — the verification body |
| `MusicRoom/tests/` | create and change — the generator, the budget, the renderer, the language, the endpoint, the welcome |
| `docs/api/ENDPOINTS.md` | change — the body and the window |
| `docs/SDD.md` | change — §10.1, §16 |
| `docs/specs/SPEC-052-verification-code-and-mail.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| the generated code | six characters, 32-symbol alphabet, no `I`, `O`, `0` or `1` |
| a correct code | verifies the address, once |
| a wrong code, five times | the sixth attempt is refused **even when correct** |
| a code after fifteen minutes | refused |
| an unknown, a wrong and a spent code | one answer, so the endpoint names no address |
| the link | still works, and still carries a 256-bit token |
| `Accept-Language: tr` | a Turkish message; anything else, English |
| every template | no `{{placeholder}}` survives rendering, and the text part carries the code |
| the first sign-in | one welcome message; the second sign-in, none |
| a live run to `ozmerte@gmail.com` | the real thing, in both languages, read in a real client |

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] `make agent-check` passes
- [ ] a live verification against the deployment, by code and by link
- [ ] the three messages have been read in a real mail client, in both languages
- [ ] `status: done` in this file
