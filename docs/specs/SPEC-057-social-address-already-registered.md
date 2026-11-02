---
id: SPEC-057
title: A social sign-in onto an address that already has a password says so
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S4               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.4]
decisions: [ADR-0008, ADR-0045]
standards: [social-oauth, authentication-authorization, application-security]
approved_by: enesozmert # 2026-11-03, from the front-end team's report
escalation: |
  **Gates 4 and 5 are yes.** The refusal set on `POST /auth/sessions/social` gains a member, and the
  whole subject is a sign-in path.

  **The behaviour is right and the answer is useless.** Somebody registered with an address and a
  password, then pressed "sign in with Google" on the same address. The provider authenticated them,
  no link exists for that `sub`, and creating an account fails because `ux_users_email` already holds
  the address. The handler refuses — correctly: linking on a matching address is the account-takeover
  path ADR-0008 names, and the person must sign in and link deliberately. But the refusal is the same
  bodiless `401` that a bad code, a `redirect_uri` mismatch and a failed PKCE check produce, so the
  client cannot tell the person what to do, and what they experience is a button that does nothing.

  **Saying this one plainly is not an enumeration oracle, and that is the whole argument.** The rule
  it would normally break is that an anonymous caller must not learn which addresses hold an account.
  Here the caller is not making a claim about an address — **the provider has already verified that
  they control it**. `email_verified` is true, or this path is not reached (that is
  `SOCIAL.EMAIL_UNVERIFIED`, and it stays). Telling the owner of an address that the address has an
  account tells them nothing they could not learn by asking for a password reset on it.

  **Nothing else moves.** Every other refusal on this route stays one bodiless `401`: a provider that
  refused the code, a token that failed a check, a verifier that did not match. In those the caller
  has proved nothing, so the response may say nothing.
---

# A social sign-in onto an address that already has a password says so

## Goal

A person whose address is registered with a password, signing in with a provider that has verified
that address, gets an answer their client can act on: sign in with the password, then link the
provider from account settings. One stable code, one sentence, and no change to any other refusal.

## Out of scope

- **Linking automatically.** That is the takeover path. ADR-0008 decided it and this does not reopen it.
- **Changing `POST /auth/social-links`.** The link route already exists, requires a bearer token and
  answers `201`; it is what the new message points at.
- **Saying anything on the other refusals.** A caller who has proved nothing is told nothing.
- **A client-side flow.** The "connect Google" screen is the front end's, and this unit is what lets it
  know when to show it.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0008's refusal to match on an address is kept; only the wording of the refusal changes |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — a refusal code and a `422` on the social sign-in route |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is a sign-in path, and the audit outcome stays `refused` |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A distinct outcome, `AddressAlreadyRegistered`, answered `422 SOCIAL.ADDRESS_HAS_ACCOUNT`.** | `401` is "we will not say"; this one we will. A code rather than prose, because a client switches on it and translates — ADR-0045 |
| 2 | **Only when the provider verified the address.** | That verification is what makes it safe to speak. Unverified is `SOCIAL.EMAIL_UNVERIFIED` and reaches this point never |
| 3 | **The message names the next action, not the account.** | "Sign in with your password, then link this provider" is actionable. It does not say when the account was created, how it signs in, or anything else about it |
| 4 | **`422`, not `409`.** | The route's refusals are already `422` with a code (`PROVIDER.UNKNOWN`, `SOCIAL.EMAIL_UNVERIFIED`); a third shape for a third refusal would be a contract a client has to special-case |
| 5 | **Every other refusal stays a bodiless `401`.** | A bad code, a mismatched redirect URI and a failed PKCE check prove nothing about the caller, so they are told nothing |
| 6 | **The audit record stays `refused`, and now names the account.** | A refused takeover attempt on a known address is exactly the row an operator wants later — SPEC-009 |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/SignInWithSocial/SignInWithSocialResult.cs` | change — the outcome |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/SignInWithSocial/SignInWithSocialCommandHandler.cs` | change — tells the two refusals apart |
| `MusicRoom/src/MusicRoom/Application/Validation/ValidationCodes.cs` | change — `SOCIAL.ADDRESS_HAS_ACCOUNT` |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/SocialAuthEndpoints.cs` | change — the `422` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SocialAuthEndpointTests.cs` | change — the new refusal, and the others unchanged |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json` | change — the refusal table |
| `docs/SDD.md` | change — §13.1, §16 |
| `docs/specs/SPEC-057-social-address-already-registered.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| password account, then social sign-in on the same verified address | `422` with `SOCIAL.ADDRESS_HAS_ACCOUNT`, and no session issued |
| the same person signs in with the password and links the provider | `201`, and the provider then signs them in — the path the message names |
| a provider that has not verified the address | still `SOCIAL.EMAIL_UNVERIFIED`, unchanged |
| a refused code, a mismatched verifier | still a bodiless `401`, unchanged |
| the `422` body | names no account, no display name and no timestamp |
| an address with no account at all | still registers and signs in, unchanged |

## Done when

- [x] the refusal is distinguishable and the other refusals are not
- [x] `docs/api/ENDPOINTS.md` lists the code
- [x] `make agent-check` passes
- [x] `status: done` in this file
