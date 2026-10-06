---
id: SPEC-031
title: Prove that a blank origin list refuses every origin, with a status code
status: done            # draft | approved | in-progress | done | blocked
tier: A                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.6]
decisions: [ADR-0030, ADR-0029]
standards: [testing, application-security]
approved_by: enesozmert # name, or "agent" for a clean Tier A gate
escalation:             # none: every gate question answers no
---

# Prove that a blank origin list refuses every origin

## Goal

After this spec `CORS_ALLOWED_ORIGINS=` is proven, by a test, to admit no browser origin, and proven with the
status code rather than with an exception. This is the first of the three binding answers SPEC-030 was approved
on, and SPEC-030 could not carry it: `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubLimitTests.cs` is not one of
that spec's ten authorised paths, and widening a Files table mid-run is the scope creep the gate exists to
stop. So the proof becomes its own unit, against SPEC-026's file, which is where the origin check lives.

Two things were missing, not one:

1. **No test configured the list blank.** All three origin tests in the suite run with a populated list. The
   code is right — `ReadOrigins` splits `Read(...) ?? string.Empty` with `RemoveEmptyEntries`, so a blank value
   yields no origins — but the rule held by construction, and nothing would catch a later change that made
   blank permissive. SPEC-030 made this sharper, not weaker: `EnvSecretProvider` now treats blank as absent
   everywhere, and this is the one read where absent must not mean "allow anything".
2. **No test in the repository asserts the status code.** Every origin test asserts
   `Assert.ThrowsAnyAsync<Exception>` on `HubConnection.StartAsync`, which proves a refusal and not which
   refusal. `403` is "not you, ever" and `429` is "not now" (SPEC-026 decision 2); a test that accepts any
   exception would pass if one silently became the other.

## Out of scope

- **Changing the origin check, the limits or any message.** No production file is touched. If a production
  file needs a change, this spec is wrong and stops.
- **Rewriting the three existing origin tests.** They prove what they prove. Converting them to status-code
  assertions is a separate cleanup and would make this diff a refactor.
- **The remaining two binding answers.** Guard messages unchanged and a failure that never echoes a value are
  already covered by SPEC-030's own suites, which assert them directly.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **no** — it evidences [[ADR-0030-hub-rate-limits-and-origin]] |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **no** — it adds a test over an existing control and changes no production file. The control itself was decided in ADR-0030 and built in SPEC-026 |
| 6 | lowers a severity, deletes or skips a test? | **no** — it adds two and removes none |
| 7 | touches or copies from `reference/`? | no |

Every answer is no, so Tier A.

## Files

| Path | Action |
|---|---|
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubLimitTests.cs` | change — two tests and one helper that issues the negotiate directly |
| `docs/specs/SPEC-030-secret-provider-abstraction.md` | change — the binding answer points here instead of claiming its own proof |
| `docs/specs/SPEC-031-blank-origin-list-evidence.md` | create |

## Steps

1. `NegotiateAsync`: issue `POST /hubs/playlists/negotiate` with the token and the three client values in the
   query string, exactly as `HubClients` builds them, plus an `Origin` header.
2. `Negotiate_WithABlankOriginList_Answers403Async`: blank list, an origin a populated list would admit,
   assert `403`.
3. `Negotiate_FromAListedOrigin_IsAcceptedAsync`: the control. Without it the first test would pass for a
   missing token or a missing client context, which are `401` and `400` — but a negotiate that always refused
   would still look like a working origin check to a reader.
4. Point SPEC-030's binding answer at this spec.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Negotiate_WithABlankOriginList_Answers403Async` | `CORS_ALLOWED_ORIGINS=` admits nothing, and the refusal is `403` and not some other failure |
| `Negotiate_FromAListedOrigin_IsAcceptedAsync` | the same request with the origin listed is accepted, so the first test fails for the origin and not for the handshake |
| the three existing origin tests | unchanged and still passing: the client-level behaviour is not what moved |
| `make agent-check` | no analyzer regression |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] no production file changed — `git diff --stat MusicRoom/src` is empty
- [x] **the test bites.** Mutation: `AllowsOrigin` given `|| AllowedOrigins.Count == 0`, which is exactly the
  "blank means permissive" reading the rule forbids. `Negotiate_WithABlankOriginList_Answers403Async` failed,
  8 of 9 passed, and the mutation was reverted. A test that holds by construction would have stayed green
- [x] SPEC-030's binding answer names this spec as its evidence
- [x] `status: done` in this file
