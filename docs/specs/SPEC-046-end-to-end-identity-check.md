---
id: SPEC-046
title: The end-to-end identity check — the composed stack, driven as a client drives it
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0026, ADR-0031, ADR-0032]
standards: [testing, authentication-authorization, ci-cd-containerization]
approved_by: enesozmert # asked for on 2026-10-11: "taşınmadan önce uçtan uca end-to-end test yapmamız lazım"
escalation: |
  **Gate 5 is a yes: this drives the credential path with real credentials.** Gates 1, 2, 3, 4, 6 and 7 are
  no — no ADR moves, no package, no schema, no contract row, nothing lowered, nothing from `reference/`.

  **The owner asked for this before the client moves**, and the reason it is worth a unit of its own is that
  the 664 tests in `make test` do not make the claim he wants checked. They run against
  `WebApplicationFactory`: an in-process `TestServer`, no Kestrel, no port, no container, and configuration
  supplied by the test rather than by a deployment. They prove the **rules**. What nothing proved is that the
  **composed stack** serves those rules — that the image builds, that the middleware order survives a real
  pipeline, that the configuration a container actually receives is enough to boot, and that a client talking
  HTTP to `localhost:8080` can complete the whole flow.

  Three things the overlay changes, each written down rather than assumed:

  1. **The logging mail sender**, so a smoke run emails nobody. The real sender has SPEC-024's tests; a check
     that posted to Brevo on every run would bounce mail at `.invalid` addresses and spend the project's
     sender reputation to prove nothing new. That sender is refused outside Development by the start-up guard,
     which is exactly the guard that keeps this honest.
  2. **A client row of its own** (`musicroom-e2e`), because the authorization code flow needs somebody to
     issue a code to, and that belongs to this check rather than to the owner's `.env`.
  3. **A throwaway RS256 key**, generated per run by `make e2e` and gone when it ends. A fixed key in a
     committed file is a private key in a public repository; a key in `.env` would make a smoke test a
     prerequisite for the owner's own stack.

  **Nothing the script prints is a credential.** Every assertion is a comparison, and the values compared are
  statuses, claim names, an address and a scope. A smoke log that carried a working token would be a token in
  a terminal buffer, a CI artifact and a screenshot.
---

# The end-to-end identity check — the composed stack, driven as a client drives it

## Goal

`make e2e` starts the real stack, applies the migrations and drives the whole identity surface over HTTP:
register, read the verification token out of the log, verify, sign in, authorize with PKCE, exchange the code,
read the ID token's header against the published JWKS, read UserInfo under two different grants, rotate,
revoke, and confirm what each refusal refuses. After this, "the provider surface works" is a command anybody
can run, not a claim resting on in-process tests.

## Out of scope

- **Replacing the suite.** These are different claims: `make test` proves the rules, this proves the
  deployment serves them. Neither substitutes for the other, and this script asserts nothing the suite does
  not already assert more precisely.
- **The ID token's signature.** The script checks that the `kid` in the header is one the JWKS publishes —
  that this stack signs with the key this stack serves. Verifying the signature itself belongs where it is:
  `IdTokenIssuerTests` and `OpenIdMetadataEndpointTests`, which do it with a real verifier.
- **Running in CI.** It needs Docker, a built image and two minutes; the gate already runs the suite. Wiring
  it into a workflow is a separate decision, and it is recorded as **O31** rather than done quietly here.
- **The mail path.** Covered by SPEC-024's tests, and deliberately substituted here.
- **The mobile client.** This is the backend's half of the readiness the owner asked for before the client
  moves to the code flow.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A compose overlay, not a second compose file.** `docker-compose.yml` plus `docker-compose.e2e.yml`. | The stack under test stays the stack that ships. A separate file would drift, and a check that drives a different stack proves nothing about this one |
| 2 | **The verification token is read from the application log**, not from the database. | The database holds a digest, so reading it there would mean reimplementing the token. The log is where the Development sender puts it, so this exercises the real path registration takes |
| 3 | **A step the rest depends on stops the run.** | A failed sign-in otherwise turns twenty later assertions into messages about an empty string, which buries the one finding that mattered |
| 4 | **Shell, `curl` and `python3`.** | No new dependency, nothing to install, and the thing under test is HTTP. A second test project in C# would reach for `WebApplicationFactory` and stop being an end-to-end check |
| 5 | **It is not wired into the gate.** | It needs Docker and a built image. Opened as **O31** instead of being added to `agent-check`, where it would double the gate's runtime for a claim the gate does not make |

## Files

| Path | Action |
|---|---|
| `docker-compose.e2e.yml` | create — the three differences, each with its reason |
| `scripts/e2e-oidc.sh` | create — 47 assertions, no credential printed |
| `Makefile` | change — `e2e` and `e2e-down` |
| `docs/SDD.md` | change — §16's provider row cites this as evidence, and O31 |
| `docs/specs/SPEC-046-end-to-end-identity-check.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| `make e2e` from a cold stack | the image builds, the container boots with the configuration it is given, and the migrations apply |
| every URL in the discovery document | a client's assumption about that document holds against a deployment, not only against a test host |
| the ID token's `kid` | the key this stack signs with is the key it publishes — the one thing in-process tests cannot confirm about a deployment |
| UserInfo under two grants | the scope rule survives real configuration, including the first-party session null case |
| revoke, then rotate | revocation reaches the database the running API is actually connected to |
| nothing in the output | no token, code, key or password is printed |

## Done when

- [x] `make e2e` passes from a cold stack
- [x] every assertion names the behaviour it checks, not a status code alone
- [x] no credential appears in the output
- [x] `status: done` in this file
