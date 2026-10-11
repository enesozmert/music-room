---
id: SPEC-045
title: UserInfo and revocation — the last third of the provider surface
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0031, ADR-0032, ADR-0045]
standards: [authentication-authorization, application-security, rest-api-design, testing]
approved_by: enesozmert # approved 2026-10-11, after SPEC-044 landed
escalation: |
  **Gates 3, 4 and 5 are yes.** One additive, nullable column on `refresh_tokens`; two contract rows gain
  detail they did not have; and the whole unit is the credential path. Gates 1, 2, 6 and 7 are no — ADR-0031
  already names UserInfo, no package, nothing lowered, nothing from `reference/`.

  **The question this unit had to answer: where does a granted scope live?** `docs/api/ENDPOINTS.md` promises
  `GET /oauth2/userinfo` returns "claims the granted scopes allow", and SPEC-044 stored the scope on the
  authorization code and then **dropped it** — the code was spent, a session was issued, and nothing
  remembered what had been asked for. Without an answer, UserInfo either returns everything (the contract
  broken quietly) or returns `sub` only (the endpoint pointless).

  Two candidates:

  | | **A — a `scope` claim in the access token** | **B — a `scope` column on the refresh-token family** |
  |---|---|---|
  | Where UserInfo reads it | the validated token, no database read | one indexed read by the `sid` the token already carries |
  | What changes | `IAccessTokenIssuer.Issue`, its implementation, every caller, and the rotation path | `SessionIssuer`, the family row, the rotation's inheritance |
  | SDD §13.1 | **contradicted** — it says the access token carries `sub`, `sid`, `iat`, `exp`, `jti` "and nothing else", with a stated reason: a JWT is signed, not secret | unchanged |
  | Cost | a documented token shape changes in the unit that reads it | one read per UserInfo call, on a primary-key-adjacent index |

  **B, taken.** The deciding fact is not the read: it is that §13.1's claim list has a reason behind it, and
  this unit has no business rewriting it to save a query that costs a single indexed lookup on a route
  nothing calls in a loop. Scope is **session state**, and the refresh-token family is where this codebase
  already keeps session state — the device that started it, its absolute expiry, its revocation.

  **Null means first-party.** A session from `POST /auth/sessions` or `POST /auth/sessions/social` has no
  granted scope, because nobody asked for one; UserInfo answers such a caller the full claim set, since it is
  the person's own account and `GET /me` already returns strictly more. A session from the code grant carries
  exactly what the authorization request asked for, and UserInfo filters on it.

  **Revocation refuses to lie.** RFC 7009 §2.2 makes an unknown token a `200`, and this implements that: a
  caller learns nothing about which tokens exist. But `token_type_hint=access_token` answers `400
  unsupported_token_type` rather than a cheerful `200`, because a 15-minute HMAC JWT **cannot** be revoked by
  this design and answering `200` would tell a client its access token is dead when it is not. That is
  §2.1's own allowance, and the honest half of it.
---

# UserInfo and revocation — the last third of the provider surface

## Goal

`GET /oauth2/userinfo` and `POST /oauth2/revoke` answer, and the discovery document names them. After this,
the provider surface ADR-0031 specified exists in full: discovery, JWKS, authorization, token, UserInfo,
revocation. A granted scope survives the exchange that granted it, so "the claims the granted scopes allow"
becomes a rule the code enforces rather than a sentence in a contract.

## Out of scope

- **The access token's signature.** Still HS256; it moves to RS256 with the removal of the old endpoints —
  SPEC-044's decision 3, unchanged.
- **Removing `POST /auth/sessions`, `POST /auth/sessions/social` and `POST /auth/tokens`.** Phases 3 and 4 of
  SPEC-041's migration plan.
- **A consent screen.** There is nothing to consent to: a first-party provider's only client is ours
  (ADR-0031's amendment).
- **New scopes.** `openid`, `profile` and `email`, which is what the claim set here can honour. A scope with
  no claim behind it would be a promise.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0031 lists UserInfo |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | **yes** — one additive nullable column, declared below |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — two rows gain their refusals and their scope rule |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The granted scope is a nullable column on `refresh_tokens`**, written when the family is opened and inherited by every rotation. | Scope is session state and this is where session state lives. Rejected: a `scope` claim in the access token, which contradicts SDD §13.1's stated claim list |
| 2 | **Null scope means a first-party session** and answers the full claim set. | Nobody asked for a scope, the caller is the account's own client, and `GET /me` already returns more. A null treated as "nothing granted" would break password sign-in's own client |
| 3 | **UserInfo requires a bearer access token and reads nothing from the request.** | The `sub` it answers comes from the token, never from a parameter — the same gate every `/me` route has |
| 4 | **`openid` → `sub`; `profile` → `name`; `email` → `email` and `email_verified`.** | OpenID Connect Core 1.0 §5.4, and nothing beyond it: a provider that returns more than the scope allows has no scopes |
| 5 | **Revoking a refresh token revokes its whole family.** | A session is a family — ADR-0032. Revoking one row would leave the replacement live, which is a revocation that does not revoke |
| 6 | **An unknown or already-revoked token answers `200`.** | RFC 7009 §2.2. The alternative is an oracle for which tokens exist |
| 7 | **`token_type_hint=access_token` answers `400 unsupported_token_type`.** | A 15-minute HMAC JWT is not revocable here. `200` would tell a client its token is dead while it still opens every route |
| 8 | **The discovery document gains `userinfo_endpoint`, `revocation_endpoint` and `scopes_supported` in this unit.** | `EveryAdvertisedUrl_AnswersAsync` makes any other order fail, which is what that test is for |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/RefreshToken.cs` | change — the granted scope, nullable |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IRefreshTokenRepository.cs` | change — revoke by token, and read a family's scope |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IIdentityClaimsReader.cs` | create — the three claims UserInfo may answer, projected without a password hash |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/SessionIssuer.cs` | change — takes the granted scope |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Queries/GetUserInfo/` | create — query, handler, result |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/RevokeToken/` | create — command, handler, outcome |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Login/LoginCommandHandler.cs` | change — a first-party session has no scope |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/SignInWithSocial/SignInWithSocialCommandHandler.cs` | change — the same |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ExchangeAuthorizationCode/ExchangeAuthorizationCodeCommandHandler.cs` | change — passes what was granted |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/RefreshTokenConfiguration.cs` | change — the column |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/RefreshTokenRepository.cs` | change — inherit scope, revoke by token, read scope |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/IdentityClaimsRepository.cs` | create — its own file, because `UserRepository` is at S104's cap (O27) |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — one registration |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/` | create — one additive nullable column |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/OAuth2UserInfoEndpoints.cs` | create — UserInfo and revocation |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/OpenIdMetadataEndpoints.cs` | change — the document names both, and the scopes |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2UserInfoTests.cs` | create — the scope rule, claim by claim |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2RevocationTests.cs` | create — each refusal and each `200` on its own |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2Driver.cs` | change — the two new routes, and a scope to ask for |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OAuth2Checks.cs` | create — the assertions, split out when the driver passed S104 |
| `docs/api/ENDPOINTS.md` | change — the two rows, and the scope rule |
| `docs/api/openapi.json` | change — regenerated |
| `brain/migrations/migrations-index.md` | change — the migration's row |
| `docs/SDD.md` | change — §13.1, §16 |
| `docs/specs/SPEC-045-userinfo-and-revocation.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| a code-grant session with `openid email` | UserInfo answers `sub`, `email`, `email_verified` and **no** `name` |
| a code-grant session with `openid` only | `sub` and nothing else |
| a password-sign-in session | the full claim set — decision 2, asserted rather than assumed |
| UserInfo with no token | `401` |
| the scope after a rotation | the inherited scope still filters, so rotation does not widen a grant |
| revoking a refresh token | that token and its whole family stop rotating |
| revoking an unknown token | `200`, saying nothing |
| `token_type_hint=access_token` | `400 unsupported_token_type` |
| `EveryAdvertisedUrl_AnswersAsync` | still passes with two more endpoints in the document |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes — 664 tests, 0 failed, on 2026-10-11
- [x] the provider surface ADR-0031 lists exists in full
- [x] `status: done` in this file
