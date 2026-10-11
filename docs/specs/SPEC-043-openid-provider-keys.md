---
id: SPEC-043
title: The provider's identity — an RS256 key pair, a discovery document and a JWKS
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S8               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0031, ADR-0029, ADR-0005]
standards: [authentication-authorization, application-security, secrets-management, testing]
approved_by: enesozmert # gate 5 answered when ADR-0031 was accepted; see the escalation
escalation: |
  **Gate 5 is yes and was answered when ADR-0031 was accepted.** This unit creates the key that signs every
  identity assertion this API will ever make. It reads a private key from configuration, publishes the public
  half, and nothing else — but a signing key is not a Tier A change at any size.

  Gates 1, 2, 3, 4, 6 and 7 answer no. [[ADR-0031-musicroom-as-openid-provider]] decided the surface, the
  algorithm and the rotation posture, and this implements the first third of it. No package — RSA and the JWT
  handler are already here. No migration: the key lives in configuration, not in a table. No change to
  `docs/api/ENDPOINTS.md`: the two routes are **already published** there and this is what makes them real.
  Nothing lowered, deleted or skipped; nothing from `reference/`.

  **Why this is a unit on its own.** ADR-0031's surface is six endpoints and the whole of issue **O19** — key
  generation, storage, JWKS exposure and rotation with an overlap window. Shipping it in one spec would put an
  authorization endpoint, a token endpoint, UserInfo and a key-rotation scheme in one diff, and the first three
  cannot be reviewed until the key they sign with exists. So: this unit is the key and what publishes it, and
  the flows follow against something already provable.
---

# The provider's identity

## Goal

After this spec the API has a signing identity and says so where a standard client looks:
`GET /.well-known/openid-configuration` describes what it will offer, and `GET /.well-known/jwks.json`
publishes the public half of an RS256 key, so a token this API issues can be verified by something that has
never spoken to it.

That last clause is the whole reason ADR-0031 removed the HMAC option: *"a shared secret cannot be published
in a JWKS, and a JWKS is what makes the provider verifiable by anything other than itself."*

## Out of scope

- **The flows.** `/oauth2/authorize`, `/oauth2/token`, `/oauth2/userinfo` and `/oauth2/revoke` are the next
  specs. The discovery document advertises them, and advertising a route that answers `404` is a defect this
  spec must not ship — see decision 4.
- **Signing the current access token with RS256.** S1's access token stays HMAC until the token endpoint
  exists; changing the signature of a token that is already issued and already validated is phase 4 of
  SPEC-041's migration plan, not phase 2's first unit.
- **Moving the private key into the vault.** ADR-0031 says it plainly: *"`EnvSecretProvider` holds it until
  ADR-0029 lands the vault at S1b, after which the private key belongs there."* The vault's compose profile
  landed in SPEC-042; its provider has not. This unit reads the key through `ISecretProvider`, which is the
  seam that makes the move a configuration change.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it implements [[ADR-0031-musicroom-as-openid-provider]] |
| 2 | new or upgraded dependency? | no — `RSA`, `JsonWebKey` and the token handler are already referenced |
| 3 | schema change beyond this spec, or destructive migration? | no — a key is configuration, not a row |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — both routes are already published there |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the signing key |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Gate 5 is a yes, so Tier B, answered when ADR-0031 was accepted.

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The key is PEM in configuration**, read through `ISecretProvider` as `OIDC_SIGNING_KEY`. | A file path would be a deployment step that can be forgotten; a generated-at-start-up key would change on every restart and invalidate every token the previous process signed. The seam is `ISecretProvider`, so the vault move is a configuration change — ADR-0031's own sentence |
| 2 | **A second, optional `OIDC_SIGNING_KEY_PREVIOUS`**, published in the JWKS and never used to sign. | This is the overlap window O19 asks for, and it is the whole of rotation: the new key signs, both keys verify, and the old one leaves when nothing it signed is still alive. Without the second slot, rotating a key invalidates every token in flight |
| 3 | **`kid` is the SHA-256 thumbprint of the public key**, not a name somebody chooses. | Two deployments cannot disagree about it, and a rotated key cannot accidentally reuse the identifier of the one it replaced |
| 4 | **The discovery document advertises only what answers.** | Until the flows land, `authorization_endpoint` and the rest are absent from the document rather than present and broken. A client that reads a discovery document is entitled to assume every URL in it exists |
| 5 | **No key is generated here, ever** — a missing key is a refusal at start-up with the command to make one. | A system that generates its own signing key on first run has a key nobody backed up and nobody can rotate. `make oidc-key` prints one; the owner puts it in `.env` |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Infrastructure/Security/ISigningKeyProvider.cs` | create — the abstraction, **in Infrastructure, not Application**: see "What the walk caught" |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/RsaSigningKeyProvider.cs` | create — reads the PEM pair, computes the `kid`, refuses a missing or malformed key |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — register it as a singleton |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/OpenIdMetadataEndpoints.cs` | create — the two `.well-known` routes |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the group, one line |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/RsaSigningKeyProviderTests.cs` | create — the key loads, the `kid` is the thumbprint, both keys are offered, a bad key refuses |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/OpenIdMetadataEndpointTests.cs` | create — the document is well-formed, the JWKS verifies a token this API signed, and the document advertises nothing that answers `404` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — `TestJwtSettings` gives every test host a key, because a missing one now stops the host |
| `Makefile` | change — `oidc-key`, which prints a pair and writes nothing |
| `.env.example` | change — the two key variables |
| `docs/SDD.md` | change — §13.1, and O19's row |
| `docs/specs/SPEC-043-openid-provider-keys.md` | create |

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `RsaSigningKeyProviderTests` — the pair loads | a PEM private key in configuration becomes a usable RS256 credential |
| — the `kid` | it is the public key's SHA-256 thumbprint, so two hosts with the same key agree and a rotated key cannot reuse an identifier |
| — the overlap | with a previous key configured, **both** are offered for verification and only the current one signs |
| — a missing or malformed key | start-up refuses, naming the variable and the command that makes one, rather than generating a key nobody backed up |
| `OpenIdMetadataEndpointTests` — the document | `issuer`, `jwks_uri` and the algorithm list are present and correct, and `response_types_supported` is what this API can actually do today |
| — **every URL the document advertises answers** | the rule decision 4 states, asserted rather than promised: the test reads the document and calls each endpoint it names |
| — the JWKS | a token signed by this API validates against the published key set with no other input |
| the 621 tests already passing | nothing about S1's tokens changed: the HMAC access token is untouched until the token endpoint exists |

## What the walk caught

**Three things, each found by running something rather than by reading it.**

**The abstraction was in the wrong layer.** The first draft put `ISigningKeyProvider` beside `IPasswordHasher`
in `Application`, which was wrong twice: `Application` references no security library, so the interface would
have dragged `Microsoft.IdentityModel.Tokens` into the layer that is supposed to name no technology — and
nothing in `Application` signs anything. Both consumers, the JWKS route and the token endpoint to come, are
outside it. Moved to `Infrastructure/Security`.

**A PEM in an environment variable is one line.** `.env`, `docker compose` and a hosting platform's secret
field all carry one line cleanly and a multi-line value badly, so `make oidc-key` prints `\n` — and two
literal characters are not a line break. `ImportFromPem` answers an `ArgumentException` that names nothing
useful. The escape is undone in the provider, with a test that loads a one-line key.

**"Refuses at start-up" was not true as written.** A lazily-resolved singleton throws on first use, so a host
with no key looked healthy until somebody fetched the JWKS — the worst moment to find out. The provider is now
resolved at map time, which made every test host need a key: `TestJwtSettings` generates one per process,
rather than a fixed key in source, because a private key in a repository is a private key.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes — 633 tests, 0 failed
- [x] a token this API signs verifies against the published JWKS, in a test that fetches both and is given
  nothing else from inside the process
- [x] the discovery document advertises no URL that answers `404`, asserted by walking the document
- [x] **no private material is published**, asserted twice: once on the provider's set and once on the bytes
  the route serves, because a serializer that reached past the provider would pass the first and fail the
  second
- [x] `status: done` in this file
