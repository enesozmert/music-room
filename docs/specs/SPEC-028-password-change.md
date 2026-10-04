---
id: SPEC-028
title: Changing a password while signed in — the endpoint ASVS asked for and this API never had
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0007, ADR-0037]
standards: [authentication-authorization, application-security, rest-api-design, testing]
approved_by: enesozmert # blanket approval of outstanding specs, 2026-09-29
escalation: |
  **Gate 4 and gate 5 both answer yes.** This adds a route to `docs/api/ENDPOINTS.md` and it is a credential
  endpoint: it reads a password, writes a password hash, and revokes sessions.

  Closes **T-31**, which SPEC-027's ASVS pass opened on 2026-10-04: ASVS V6.2.2 and V6.2.3 are committed at
  L2 and there was no way for a signed-in user to replace their password. The reset flow was the only path,
  which means a user who suspects their password is known needs a mail round trip, and cannot act at all while
  a provider outage lasts.

  **One decision worth the owner's eye is the status code for a wrong current password**: this spec answers
  `422`, not `401`. Reasoning in decision 3 — a `401` would tell a client its *token* is finished and send it
  into a refresh loop over a password typo.
---

# Changing a password

## Goal

After this spec a signed-in user can replace their own password by presenting the current one, every other
session they hold is revoked, and the attempt is audited and rate-limited like every other credential
operation. ASVS V6.2.2 and V6.2.3 move from not met to met, V7.4.3 gains its second mechanism, and V7.5.1 —
re-authentication before changing a sensitive attribute — is met for the one sensitive attribute this API lets
a user change.

## Out of scope

- **Changing the email address.** It is not in the contract, and it would need its own verification round
  trip. V7.5.1 is met for what exists rather than claimed for what does not.
- **Breached-password screening on the new password.** That is T-32 and its own unit; this endpoint applies
  exactly the policy registration applies, which is the point — one password policy, one validator.
- **A second factor.** T-31 is about the missing endpoint. MFA is V6.3.3 and remains not met.
- **Revoking the caller's own session.** Decision 4 keeps it alive deliberately.

## Decisions taken under the blanket approval

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **`POST /api/v1/me/password`**, body `currentPassword` and `newPassword`, answering `204`. | `POST` because it is an action and not a representation: a `PUT` of the same body twice would fail the second time, because the current password has changed. Under `/me`, where everything scoped to the caller lives |
| 2 | **The current password is verified by the handler, with the same hasher that stores it**, and the new hash is written in one transaction with the revocation of the caller's other refresh families. | Same posture as the reset path (SPEC-007): a change that replaced the hash and failed to revoke would leave a thief holding a live session behind a password its owner had already changed |
| 3 | **A wrong current password is `422`, not `401`.** | The caller's token is valid and the request is authenticated; what failed is a semantic precondition. A `401` tells a client its session is over, which sends a well-behaved client into a refresh-then-retry loop over a typo. There is also nothing to hide: the caller already knows which account this is, so a distinct message costs no enumeration and saves a support question. Rejected: `401`, and a single indistinguishable `422` for both failure modes |
| 4 | **Every other session is revoked; the caller's own stays alive.** | Literally ASVS V7.4.3 — *terminate all other active sessions* — and it is the behaviour a user expects: changing a password should not log you out of the device you changed it on. The session is identified by the `sid` claim, which is what SPEC-010 made the refresh family's name |
| 5 | **The route carries a per-IP policy and the per-account attempt limiter**, and a wrong current password charges an attempt while a correct one clears the history. | It is a password-guessing surface: a stolen access token plus this route would otherwise be an offline-speed oracle for the password itself. Login's rule, for login's reason — a correct password is not abuse |
| 6 | **The attempt is audited as `account.password-change`**, with `completed`, `refused` or `throttled`. | Subject V.6 wants every action recorded; a password change is the one a user is most likely to ask about afterwards |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — ADR-0007 fixes the hashing and ADR-0037 the limiter posture; this follows both |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration. The new hash overwrites the old column and the refresh rows are revoked, not deleted |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — one route in §1, with its statuses and the revocation it performs |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes, all of them** — it is a credential endpoint |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommand.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommandHandler.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommandValidator.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — a credential read by id, and the change write |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — the write path, one transaction |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AccountPasswordEndpoints.cs` | create — the route and its statuses |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CredentialRateLimiterPolicies.cs` | change — the policy |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/CredentialRateLimitOptions.cs` | change — its number |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the route |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordChangeEndpointTests.cs` | create |
| `.env.example` | change — the new limit's variables |
| `docs/api/ENDPOINTS.md` | change — §1 |
| `docs/SDD.md` | change — §13.1, §13.3's table, §13.4's action list, §16's V.1 and V.6 rows |
| `docs/owasp/ASVS-CONFORMANCE.md` | change — V6.2.2, V6.2.3, V7.4.3 and V7.5.1 |
| `docs/owasp/THREAT-REGISTER.md` | change — **closes T-31** |
| `docs/DELIVERY-PLAN.md` · `brain/projects/musicroom-backend.md` | change |
| `docs/specs/SPEC-028-password-change.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** One command in `Application`, two methods on the repository contract and its
implementation, one endpoint file and one policy in `WebAPI`.

**2. Boundary types.** The command and its outcome are `public`; the handler and the endpoint class are not.

**3. Invoked?** `POST /api/v1/me/password`, authenticated.

**4. CLI or package.** None.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **The route needs the `sid` claim, not just the account.** Keeping the caller's session alive means the write
  path has to know which family to spare, and that name is the `sid` SPEC-010 put in the token.
- **The current password must be hashed-compared, never read back.** The handler asks the hasher to verify
  against the stored hash; nothing returns a hash to a caller and nothing logs either password.
- **Its own endpoint file.** `AuthEndpoints.cs` is near the file-length gate and this route is about the
  account rather than about a session, so it goes beside the profile routes under `/me`.

## Steps

1. The command, its validator — the same password rules registration applies — and the outcome enum.
2. `FindCredentialByIdAsync` and `TryChangePasswordAsync` on the repository, the second in one transaction.
3. The handler: verify, charge or clear the attempt limiter, write, audit.
4. The endpoint, its per-IP policy, and the mapping.
5. Tests.
6. `make agent-check`, then the documents — including closing T-31 and flipping four ASVS rows.

## What the build added to this plan

**The register row this unit closes is not on `main` yet.** T-31 was opened by SPEC-027, which is still in
review, so this branch — cut from `origin/main`, as the rule requires — has no row to close and no ASVS row to
flip. Writing the row here in closed form would collide with SPEC-027's version of the same row on merge. So
this unit leaves both documents alone and says why: whichever of the two lands second marks T-31 closed and
turns V6.2.2, V6.2.3, V7.4.3 and V7.5.1 to met. `docs/SDD.md` names the row rather than claiming it.

**The limiter key is the account id, not an address.** Every existing use of `ICredentialAttemptLimiter` keys
on the submitted email, because those endpoints are anonymous and that is all they know. This route is
authenticated and the body carries no address, so the key is `account:{userId}` — the same limiter, a different
partition, and the interface's own remark already allowed for it: the key "is a partition key and nothing
else".

**A no-op change is refused.** Sending the current password as the new one would revoke every other session for
no benefit, so the validator refuses it. That is a surprising amount of damage for a request that changes
nothing.

**The endpoint needed its own `429` helper.** `CredentialEndpointResults.Throttled` takes the
`CredentialThrottle` the anonymous credential handlers return; this command carries its wait on its own result
record, so the response is built here. Same shape to a client, which is what the contract promises.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `PasswordChangeEndpointTests` — the happy path | `204`, the new password signs in, and the old one no longer does |
| — the other sessions | a second session's refresh token is refused afterwards, and the caller's own still rotates |
| — a wrong current password | `422`, the password is unchanged, and the old one still signs in |
| — a weak new password | `422` from the same rules registration uses |
| — the audit rows | `completed` on success and `refused` on a wrong current password, action `account.password-change` |
| — the limiter | repeated wrong attempts are answered `429` with `Retry-After`, and a correct one clears the history |
| — no token | `401` |
| `make agent-check` | the OpenAPI document gains the route and is committed with it |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] **T-31 is closed, and the four ASVS rows say met** — done by SPEC-027, which landed second and owed it
- [x] `docs/api/ENDPOINTS.md` carries the route
- [x] `status: done` in this file
