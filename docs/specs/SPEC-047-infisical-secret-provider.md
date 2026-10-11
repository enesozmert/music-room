---
id: SPEC-047
title: The vault is where secrets come from — InfisicalSecretProvider
status: done            # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1b              # which DELIVERY-PLAN slice this belongs to
requirements: [V.8]
decisions: [ADR-0029, ADR-0026, ADR-0028]
standards: [secrets-management, ci-cd-containerization, testing]
approved_by: enesozmert # approved 2026-10-11, the second half of S1b
escalation: |
  **Gate 5 is a yes: this is where every secret in the application comes from.** Gates 1, 2, 3, 4, 6 and 7
  are no — ADR-0029 already specified this provider by name, no package (the HTTP client and `System.Text.Json`
  are already referenced), no schema, no contract row, nothing lowered, nothing from `reference/`.

  **Three decisions the owner should see stated, because each one is a way this could go quietly wrong.**

  1. **A vault that cannot be reached stops the host.** The alternative — fall back to `.env` and carry on —
     is the failure this whole decision exists to prevent: a vault outage would silently return the system to
     reading API keys out of a plain file, and nothing in a log would say the posture had changed. So
     `SECRET_PROVIDER=infisical` means the vault answers or the application does not start.
  2. **The vault wins, and `.env` is the floor underneath it.** A name the vault holds comes from the vault;
     a name it does not hold falls through to the environment. That is not a hedge — it is forced:
     `INFISICAL_CLIENT_ID` and `INFISICAL_CLIENT_SECRET` *are* the bootstrap credentials, so they cannot
     themselves live in the vault, and ADR-0029 said this part out loud before it was built — *"a vault does
     not remove the local secret file; it shrinks it."* What the layering buys is that moving a secret into
     the vault is a vault operation, not a code change.
  3. **Secrets are fetched once, at start-up.** One login, one fetch, an in-memory dictionary, nothing per
     request. **Rotation therefore needs a restart**, which is the honest cost and is cheap for a container.
     A background refresh would mean a secret changing under a running request, and a cache with a lifetime
     would mean a vault outage becoming an outage minutes later instead of at boot, where it is diagnosable.

  **`SECRET_PROVIDER` is unset in CI, deliberately.** ADR-0029: *"CI does not use the vault... a pipeline
  that needs a running vault to build is a pipeline that breaks for the wrong reasons."* The gate keeps
  reading repository secrets through the environment, and nothing in this unit changes that.
---

# The vault is where secrets come from

## Goal

`SECRET_PROVIDER=infisical` makes the application read its configuration from the self-hosted Infisical that
SPEC-042 put in this compose stack, authenticating with the machine identity the owner created there. After
this, S1b is complete: the abstraction (SPEC-030), the vault (SPEC-042) and the provider that reads from it.

## Out of scope

- **Moving any particular secret into the vault.** That is a vault operation, which is the point of the
  layering: nothing in this repository has to change when a value moves.
- **Rotation without a restart.** Stated as a cost rather than solved. A container restart is the rotation
  procedure.
- **The vault in CI.** ADR-0029 already decided against it.
- **A second vault.** `ISecretProvider` has two implementations and that is the number ADR-0029 named.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it implements ADR-0029 |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Decisions

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **`SECRET_PROVIDER` selects: `env` (default) or `infisical`.** An unknown value stops start-up naming both. | The same shape `MAIL_PROVIDER` already has, so there is one way to select an adapter in this codebase. A silent default to `env` on a typo would be a vault nobody noticed was not being used |
| 2 | **An unreachable or unauthenticated vault stops start-up.** | A fallback would silently restore the posture the vault exists to remove. The message names `SECRET_PROVIDER`, `INFISICAL_SITE_URL` and what failed — never a value |
| 3 | **Vault first, environment second.** | Forced by the bootstrap credentials, and it makes moving a secret into the vault a vault operation. A vault-only provider could not read its own client secret |
| 4 | **One login and one fetch, at start-up, into memory.** | No network per read, and a vault outage is a boot failure rather than a request failure. Rotation needs a restart, which is written in `.env.example` beside the variable |
| 5 | **The HTTP calls live in their own class.** `InfisicalVaultClient` logs in and fetches; `InfisicalSecretProvider` answers reads. | Two responsibilities, and the provider's tests then need no network at all — they are given a client |
| 6 | **Blank reads as absent, as it does in `EnvSecretProvider`.** | A secret present in the vault with an empty value has configured nothing, and the rule that decides that already exists in one place |
| 7 | **Nothing here logs a value, and no exception message carries one.** | `secrets-management` forbids printing a secret, including while debugging. The provider logs how many names it loaded, never which values |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Infrastructure/Configuration/InfisicalVaultClient.cs` | create — Universal Auth login, then the raw-secrets fetch |
| `MusicRoom/src/MusicRoom/Infrastructure/Configuration/InfisicalSecretProvider.cs` | create — the vault's answers, with the environment underneath |
| `MusicRoom/src/MusicRoom/Infrastructure/Configuration/SecretProviderFactory.cs` | create — selects, and memoises so the composition root's repeated reads cost one login |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — `EnvironmentConfiguration.For` goes through the factory |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — the registered provider is the same instance the root used |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Configuration/VaultTransport.cs` | create — a vault that answers from memory |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Configuration/InfisicalSecretProviderTests.cs` | create — the layering, the refusals, and that no message carries a value |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Configuration/SecretProviderFactoryTests.cs` | create — the selection, including the typo |
| `.env.example` | change — `SECRET_PROVIDER`, and the rotation note |
| `docs/SDD.md` | change — §14, §16, and O18 closes |
| `docs/DELIVERY-PLAN.md` | change — S1b is built |
| `docs/specs/SPEC-047-infisical-secret-provider.md` | change — status transitions |

## Verification

| Check | Proves |
|---|---|
| a name the vault holds | comes from the vault, not from the environment |
| a name only the environment holds | falls through, which is what makes the bootstrap credentials possible |
| a blank vault value | reads as absent, the same rule `EnvSecretProvider` applies |
| one provider, many reads | one login and one fetch — asserted by counting the requests the transport saw |
| an unreachable vault | start-up fails, naming `SECRET_PROVIDER` and `INFISICAL_SITE_URL` |
| rejected credentials | start-up fails, and the message contains neither the client secret nor any secret value |
| `SECRET_PROVIDER` absent, `env`, `ENV`, and a typo | the environment provider, the environment provider, the environment provider, and a refusal naming both valid values |
| `make e2e` | the stack still boots with `SECRET_PROVIDER` unset, so the default path is untouched |

**A live run against the running vault is an owner step, and it has not passed yet.** Booted with
`SECRET_PROVIDER=infisical` against the Infisical at `localhost:8090`, the host refused at start-up exactly as
designed, carrying the vault's own reason:

```
SECRET_PROVIDER is `infisical`, ... the vault refused the machine identity with 404
("No identity with specified client ID was found"); check INFISICAL_CLIENT_ID and
INFISICAL_CLIENT_SECRET, and that the identity exists in this vault
```

That is the mechanism working and the credentials not matching: the machine identity named by
`INFISICAL_CLIENT_ID` is not an identity of **this** vault instance, so there is nothing for the client secret
to authenticate against. The remaining steps are the owner's, in the Infisical UI — create (or re-create) the
machine identity, issue it a client secret, **add it to the project** with a read role, and put the three
values in `.env`. The transport, the login, the fetch, the layering and every refusal are proven against a
stub vault, which is the half an agent can prove; a live login needs a credential only the owner can make.

**It also produced the first finding.** The original refusal named the two variables and not the vault's own
sentence, which left "that identity does not exist here" and "that secret is wrong" indistinguishable. The
message now carries the vault's `message` member, bounded, and a test asserts both that it is there and that
the client secret is not.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes — 681 tests, 0 failed, on 2026-10-11
- [x] `make e2e` still passes, proving the default path is unchanged
- [x] S1b has no remaining half, and O18 says so
- [x] `status: done` in this file
- [ ] **owner step:** a machine identity of the running vault, added to the project, with its three
      values in `.env` — then `SECRET_PROVIDER=infisical` boots instead of refusing
