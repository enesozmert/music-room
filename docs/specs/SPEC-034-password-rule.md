---
id: SPEC-034
title: One password rule, called three times — the unification SPEC-028 was credited with
status: in-progress     # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0007, ADR-0028, ADR-0042]
standards: [authentication-authorization, application-security, clean-architecture-cqrs, testing]
approved_by: enesozmert # gate 5 answered by the owner on 2026-10-06
escalation: |
  **Gate 5 tripped: yes, and I am stopping on it deliberately rather than waving it through.**

  The unit is behaviour-preserving and small, so the temptation is to call gate 5 a no and take Tier A. Two
  reasons not to, and the second is the one that matters:

  1. **Precedent.** SPEC-030 was exactly this shape — a behaviour-preserving refactor that routed every secret
     read through one abstraction — and it answered gate 5 yes for the reason *"it is the secrets path itself"*.
     This is the password policy itself. Answering differently for the same kind of change would make the gate a
     matter of mood.
  2. **A refactor here can change the response body without changing the policy.** FluentValidation's failure
     carries the **property name**, and `CredentialEndpointResults.ToErrors` groups by it, so the `422` body is
     `{"errors":{"Password":[…]}}` on register and `{"errors":{"NewPassword":[…]}}` on reset and change — this
     spec measured that: register validates `Password`, reset and change validate `NewPassword`. A shared rule
     written as a validator for a fixed property, rather than as a rule applied to whichever property a caller
     selects, would silently rename a key clients read. That is a contract change hiding inside a refactor, and
     it is precisely what a human gate is for.

  Everything else is a no. No ADR is written or contradicted: [[ADR-0042-breached-and-context-password-screening]]
  was corrected on #82 to say the unification comes first, and this is that unit. No dependency, no migration, no
  change to `docs/api/ENDPOINTS.md`, nothing lowered, nothing from `reference/`.

  **What this unit deliberately does not do:** screen anything. The breached set and the context word list are
  SPEC-035, landing into the one rule this unit creates. Scoping them out is the owner's answer 1 of 2026-10-06,
  and it is what lets the existing suites be the whole proof here.

  **Owner's answer, 2026-10-06: approved, and you were right to stop.** Both reasons hold. The precedent one
  keeps the gate from being a matter of mood, and the second found a real trap: a refactor that renames a key in
  a `422` body is a contract change whatever its intent. That is worth a human turn.

  Four answers, binding:

  1. **The shared rule is applied to a property the caller selects, never to a fixed one.** An extension on
     `IRuleBuilder<T, string>` that the three validators call on `Password`, `NewPassword` and `NewPassword`
     respectively. A rule class bound to one property name would rename a key clients read, which is the thing
     your escalation caught.
  2. **Prove the body keys rather than reasoning about them.** Assert the `422` key on all three routes —
     `Password` on register, `NewPassword` on reset and change — and the message text with it. If no assertion
     covers a key today, add one: that is new evidence, not a changed test, and this unit is exactly where it
     belongs.
  3. **No existing test changes.** If one has to, stop and tell me which and why. A behaviour-preserving unit
     whose proof needed editing was not behaviour-preserving.
  4. **One home for the numbers, and no forwarding constants left behind.**
     `ResetPasswordCommandValidator.PasswordMaximumUtf8Bytes` and `ChangePasswordCommandValidator.PasswordMaximumUtf8Bytes`
     are `public`, and `RegisterCommandValidator.PasswordMaximumUtf8Bytes` is `internal`. Only
     `ResetPasswordCommandValidatorTests` reads any of them, at line 92. Move the three numbers to the shared
     rule, update that one reference in the same unit, and delete the three declarations — a `public const`
     kept as a forwarder is the duplication this unit exists to remove.

  SPEC-035 follows with the screens, into the rule this one creates.
---

# One password rule

## Goal

After this spec the password policy is stated **once** and called from the three paths that choose a password.
Nothing about what the system accepts or refuses changes, and the existing register, reset and change suites are
the evidence for that: they pass untouched, including the `422` bodies with the property names they already
assert.

This is the unification SPEC-028 is credited with and did not perform. It matters now because SPEC-035 adds a
breached-password screen, and a security rule added to three places is a rule that will be in two of them after
the next path is written.

## Out of scope

- **Any screening.** No breached set, no context word list. SPEC-035, into the rule this creates.
- **Changing the policy.** 12 characters, 64 characters, 72 UTF-8 bytes, no composition rules, and reset's
  "must differ from the current one" stay exactly as they are, including their messages.
- **Renaming a validated property.** `Password` on register, `NewPassword` on reset and change — the `422` body
  carries those keys and a refactor must not touch them.
- **The login path.** It validates no new password and is not in the Files table.

## Decisions taken under ADR-0042 and ADR-0028

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A rule-builder extension, not a validator class.** `PasswordRules.ChosenPassword(this IRuleBuilder<T, string> rule)`, applied to whichever property each command exposes. | A shared `AbstractValidator<string>` would have to be invoked with a property name of its own choosing, which renames the key in the `422` body. An extension on the rule builder keeps `Password` on register and `NewPassword` on reset and change, because the caller still selects the property |
| 2 | **It lives in `Application/Features/Auth`, beside the commands that use it**, and is `internal`. | All three callers are in that assembly. Nothing outside it needs the rule, and ADR-0028's point is that a type goes where it is used, not in a shared bucket. `Core.Security` is empty and stays empty |
| 3 | **The messages move verbatim.** Every `WithMessage` string is copied, not reworded. | A reworded message is a response-body change, and `RegistrationEndpointTests` and the reset and change suites assert on `422` bodies. The diff should be readable as "three copies became one" and nothing else |
| 4 | **The three constants stop being three.** `PasswordMaximumUtf8Bytes` is declared once, on the rule, and the validators that exposed it as a `public const` keep a reference rather than a copy. | Two of them are `public const int PasswordMaximumUtf8Bytes = 72` on separate classes today. A compile-time constant duplicated across a security boundary is how two limits drift by one and nobody notices |
| 5 | **Three assertions are added, because the ones this unit relies on do not exist.** The `422` body key on each of the three routes, with the message text. | This spec first claimed the existing suites would catch a renamed key. They would not: **37 assertions on `HttpStatusCode.UnprocessableEntity` across the suites and zero on the body's `errors` keys**, measured from both sides. So the refactor's only real safety net is written here — and these are the **first assertions of their kind in the repository** |
| 6 | **The repository-wide half of that gap is an issue, not this unit's work.** `CredentialEndpointResults.ToErrors` shapes 21 call sites in 14 endpoint files, and the committed `docs/api/openapi.json` publishes `HttpValidationProblemDetails` on 22 operations — all of it untested. | A unit that fixes a repository-wide gap while refactoring a validator is two changes in one pull request, and the second is the one nobody asked for. The row carries the numbers so it gets fixed rather than rediscovered |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0042 as corrected on #82 names this unit and says it comes first |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — and it must not change the `422` bodies either, which is decision 1's whole point |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the password policy, on three credential routes. Behaviour-preserving, and the escalation says why that is not enough to make it a no |
| 6 | lowers a severity, deletes or skips a test? | no — and no test changes. If one has to, that is a finding, not a chore |
| 7 | touches or copies from `reference/`? | no |

Gate 5 is "yes", so this stopped at `draft` with the escalation filled in and no code written. The owner
answered it on 2026-10-06 — four binding answers in the escalation block — and the status is now `approved`.

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Features/Auth/PasswordRules.cs` | create — the policy, once: length, UTF-8 ceiling, messages verbatim |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandValidator.cs` | change — call the rule on `Password`; drop the local copy |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandValidator.cs` | change — call it on `NewPassword`; keep `TokenLength` where it is |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommandValidator.cs` | change — call it on `NewPassword`; keep the "must differ" rule, which is this path's alone |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/ResetPasswordCommandValidatorTests.cs` | change — line 92 reads a constant answer 4 deletes; the reference moves to the shared rule |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | change — **add** the `422` key assertion: `Password` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordResetEndpointTests.cs` | change — **add** the `422` key assertion: `NewPassword` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordChangeEndpointTests.cs` | change — **add** the `422` key assertion: `NewPassword` |
| `docs/SDD.md` | change — §13.1's password-policy row, and §16 gains the issue the body-key gap deserves |
| `docs/specs/SPEC-034-password-rule.md` | change — status transitions |

Four test files, and decision 5 is amended by the owner's answers of 2026-10-06: three of them **gain** an
assertion that does not exist anywhere in the repository today, and the fourth has one reference moved. No
existing assertion is weakened or rewritten.

### The Files table walk

**1. Assemblies, and which references that implies.** `MusicRoom.Application` only. The rule and all three
callers are in it, so **no `.csproj` changes** and no new reference anywhere. `Infrastructure` and `WebAPI` are
untouched — which is also why this unit cannot accidentally reach the embedded set SPEC-035 will add.

**2. Types crossing an assembly boundary.** None. `PasswordRules` is `internal`: every caller is in the same
assembly, and nothing outside needs it. The two `public const int PasswordMaximumUtf8Bytes` declarations that
exist today are *not* part of any contract — grep says they are referenced only inside their own validators — so
collapsing them to one is safe, and decision 4 says why it matters.

**3. Does anything invoke it?** Three validators, all of them `public` and already registered by
`AddValidatorsFromAssembly`. SPEC-025's lesson applies in reverse here: an **internal** validator is never
registered, so the rule must be called *from* the existing public validators rather than becoming a validator of
its own.

**4. A CLI, a tool manifest, or an unpinned package?** None.

**5. Do the four corePackages contain what I assume?** Checked rather than assumed: `Core.Application` holds one
file, and `Core.CrossCuttingConcerns`, `Core.Persistence` and `Core.Security` hold **none**. `Core.Security` is
the tempting home for a password rule and it is empty — nothing goes in it, because the rule is a FluentValidation
extension over this application's commands, not a domain-agnostic primitive.

**What the walk caught, and it is the reason gate 5 is a yes.** The three validators do not validate the same
property: register validates `Password`, reset and change validate `NewPassword`. `CredentialEndpointResults.ToErrors`
groups failures **by property name**, so those names are keys in the `422` body a client reads. A shared rule
written the obvious way — one `AbstractValidator` for a password string — would rename them. Decision 1 is what
prevents it, and the existing suites are what would catch it.

## Steps

1. `PasswordRules.ChosenPassword`: the length rules, the UTF-8 ceiling, and the messages copied verbatim.
2. `RegisterCommandValidator` calls it on `Password`; the reset and change validators call it on `NewPassword`.
3. The duplicated `PasswordMaximumUtf8Bytes` constants become one, referenced where they were declared.
4. `make agent-check` — the existing suites are the proof, and any test that has to change is reported as a
   finding rather than edited to fit.
5. `docs/SDD.md` §13.1, then close out.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `RegistrationEndpointTests` — **new** key assertion | register answers `422` with `errors` keyed on **`Password`**, and the message is the one the policy states. The first assertion of its kind in this repository |
| `PasswordResetEndpointTests` — **new** key assertion | the reset path's key is **`NewPassword`** |
| `PasswordChangeEndpointTests` — **new** key assertion | the change path's key is **`NewPassword`**, and its own "must differ from the current one" rule still fires |
| every other assertion in those three suites, unchanged | the policy itself did not move: what was accepted is accepted, what was refused is refused |
| the whole suite, 540 tests | nothing else moved: no handler, no endpoint, no message |
| `git diff --stat` | four files, one created and three shrunk. A diff that touches a handler or an endpoint means the unit went further than its scope |
| `make agent-check` | no analyzer regression, and the OpenAPI document unchanged. **A renamed validated property would not show there** — `HttpValidationProblemDetails` describes the envelope and not the keys — which is exactly why the three new assertions above exist |

## Done when

- [ ] `PasswordRules` exists and all three validators call it
- [ ] no duplicate password constant remains in the three validators
- [ ] every message is byte-identical to the one it replaced
- [ ] `make agent-check` passes; no existing assertion is weakened or rewritten, and the one moved constant reference is reported
- [ ] the three `422` key assertions exist and are named as the first of their kind
- [ ] the body-key gap is opened as an issue in `docs/SDD.md` §16 with its numbers
- [ ] `status: done` in this file
