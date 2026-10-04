---
id: SPEC-030
title: The secret provider abstraction, and the one implementation that exists today
status: approved        # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1b              # which DELIVERY-PLAN slice this belongs to
requirements: [V.8, V.6]
decisions: [ADR-0029, ADR-0026, ADR-0028]
standards: [secrets-management, clean-architecture-cqrs, ci-cd-containerization, testing]
approved_by: enesozmert # name, or "agent" for a clean Tier A gate
escalation: |
  **Gate 5 tripped: yes, and unavoidably.** This unit is the path every secret in the system travels. It
  touches the thirteen configuration reads that today name `JWT_SIGNING_KEY`, `MAIL_API_KEY`,
  `YOUTUBE_API_KEY`, `DATABASE_URL` and the rest directly, and routes all of them through one abstraction.
  Nothing about which secret is read changes, and no value is logged, printed or persisted — but the component
  that hands the application its signing key is not a Tier A change, so this stops here for a human.

  The other six gates answer no: no new ADR (this implements [[ADR-0029-containerised-dev-and-secret-vault]],
  which says in as many words that *"the abstraction lands earlier, because writing it later means touching
  every configuration read"*), no dependency, no migration, no contract change, nothing lowered or skipped,
  and nothing from `reference/`.

  **What this unit deliberately does not do:** add `InfisicalSecretProvider` or the `vault` compose profile.
  That half of S1b needs a self-hosted Infisical with a machine identity, and a machine identity is created by
  a person in a running instance's UI — it cannot be produced or verified by an agent. Splitting it off is also
  what makes this half verifiable on its own.

  **Owner's answer, 2026-10-04: approved.** Gate 5 is a real yes, and the split is right — the vault half needs
  a machine identity a person creates in a running Infisical, so it is its own unit and this one is verifiable
  without it.

  Three answers, binding on the implementation:

  1. **`CORS_ALLOWED_ORIGINS` keeps the meaning it has.** A blank value is an **empty list that allows
     nothing**, not an unset variable — SPEC-026 decision 6, and ADR-0030's handshake check compares an
     `Origin` against that list. The provider's blank-is-absent rule must not swallow it: either that read
     stays outside `Require`, or the provider distinguishes "absent" from "present and empty" and this read
     takes the second. Prove it with a test — a blank value still allows no origin, and the hub handshake
     still answers `403`. `Program.cs` already says why in as many words: *"Unset" reading as "everything"
     is how a development convenience becomes a production hole.*
  2. **No variable is renamed and no guard message changes.** The three start-up refusals keep their exact
     text. A changed message means the unit went further than this spec, and that is a stop.
  3. **A failure names the variable and never the value**, in the provider and in the tests. The tests assert
     the **absence** of the value from the message, which is what `secrets-management` asks for.
---

# The secret provider abstraction

## Goal

After this spec the application reads every secret and every setting through one abstraction,
`ISecretProvider`, with a single implementation — `EnvSecretProvider`, reading the process environment that
`docker compose` fills from `.env`. Behaviour is identical: the same names, the same start-up guards, the same
refusals. What changes is that a second implementation becomes a registration rather than a rewrite, which is
the whole reason ADR-0029 asked for the abstraction before the vault.

Today thirteen reads name their variable directly, spread across `EnvironmentConfiguration` in
`Program.cs`. After this they name it once, in one place, through one interface.

## Out of scope

- **`InfisicalSecretProvider` and the `vault` compose profile.** The rest of S1b, and it needs a machine
  identity a person creates in a running Infisical — see the escalation block.
- **Changing any variable name.** `DATABASE_URL` stays `DATABASE_URL`. ADR-0026 made the names
  vendor-neutral and ADR-0029 forbids a provider-specific name; neither asks for a rename.
- **Changing a start-up guard.** A host that cannot sign a token, reach a database or send mail still refuses
  to boot, with the same message.
- **Caching or refresh semantics.** `EnvSecretProvider` reads a value that cannot change inside a process
  lifetime, so there is nothing to invalidate. A vault-backed provider may need both, and that decision
  belongs with it.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — it implements [[ADR-0029-containerised-dev-and-secret-vault]] |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the secrets path itself: the JWT signing key, the mail API key, the YouTube key and the connection string all arrive through it |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Gate 5 is "yes", so this spec stopped at `draft` with the escalation filled in and no code written. The
owner answered it on 2026-10-04 — see the escalation block, whose three answers are binding — and the
status is now `approved`.

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Configuration/ISecretProvider.cs` | create — the interface: a required read that throws with a named variable, and an optional read that returns null |
| `MusicRoom/src/MusicRoom/Infrastructure/Configuration/EnvSecretProvider.cs` | create — the one implementation, over `IConfiguration`, which already layers the process environment |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — register it, so the composition root resolves the abstraction rather than naming a source |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — `EnvironmentConfiguration`'s thirteen reads go through the provider; the guards and their messages are unchanged |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Configuration/EnvSecretProviderTests.cs` | create — a present value, an absent optional, an absent required naming the variable and not its value, and a blank value treated as absent |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/SecretProviderStartupTests.cs` | create — the host still refuses to boot without `DATABASE_URL`, the JWT settings or a mail sender, with the same messages |
| `docs/SDD.md` | change — §13.5 or §13.3's configuration note: secrets are read through one abstraction, with the vault as the second implementation |
| `docs/DELIVERY-PLAN.md` | change — S1b split into the abstraction (this) and the vault (next) |
| `brain/projects/musicroom-backend.md` | change — S1b |
| `docs/specs/SPEC-030-secret-provider-abstraction.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** The interface in `Application/Abstractions/Configuration`, the implementation in
`Infrastructure/Configuration`, the wiring in `WebAPI`. The same shape as `IMailService`, `IPasswordHasher`
and `ICatalogProvider` — declared where it is needed, implemented where the source lives (ADR-0028).

**2. Boundary types.** `ISecretProvider` is `public`, because the composition root and `Infrastructure` both
name it. `EnvSecretProvider` is `internal`.

**3. Invoked?** At start-up, thirteen times, and never during a request. Nothing is read per request today and
this unit does not change that.

**4. CLI or package.** None.

**5. corePackages.** Unchanged.

**What the walk caught.** Four things, and the fourth is why gate 5 is a real "yes" rather than a formality.
- **`EnvironmentConfiguration` lives in `Program.cs`**, which is at CA1506's coupling limit and has been
  pushed over it by three of the last four units. Routing its reads through one injected abstraction should
  *reduce* what `Main` names, but the file is the one to watch.
- **`IConfiguration` already layers the environment**, so `EnvSecretProvider` is a thin adapter rather than a
  reimplementation. Writing it over `Environment.GetEnvironmentVariable` instead would quietly break every
  test host, which sets values through `UseSetting`.
- **Blank is absent.** Several existing guards treat `""` as missing — `MAIL_PROVIDER=` must fail start-up, not
  select a sender named empty string. That rule belongs in the provider, once, rather than at thirteen call
  sites.
- **A failure message may name the variable and never the value.** `secrets-management` forbids printing a
  secret, including during debugging, and a provider that throws `"JWT_SIGNING_KEY is not set"` is useful while
  one that echoes what it found is a leak. The tests assert the absence, not the presence.

## Steps

1. `ISecretProvider`: `Require(string name)` and `Read(string name)`, with blank treated as absent and the
   required path throwing a message that names the variable.
2. `EnvSecretProvider` over `IConfiguration`, registered in `AddInfrastructureServices`.
3. `EnvironmentConfiguration` takes the provider and its thirteen reads go through it. No guard, message or
   default changes.
4. Tests: the provider's four cases, and the three start-up refusals that must still happen.
5. `make agent-check`, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `EnvSecretProviderTests` — a present value | the adapter reads what `IConfiguration` layers, which is what a test host sets and what compose fills from `.env` |
| — an absent optional | returns null rather than throwing, so `VERIFICATION_LINK_BASE_URL` stays optional |
| — an absent required | throws, and the message names the variable and nothing else |
| — a blank value | is absent, so `MAIL_PROVIDER=` cannot select a sender |
| `SecretProviderStartupTests` | the host still refuses to boot without `DATABASE_URL`, without the JWT settings, and without a mail sender — the same three guards, through the new path |
| the existing 507 tests | behaviour is unchanged: every suite that sets configuration through `UseSetting` still gets what it set |
| `make agent-check` | no analyzer regression, and `Main` stays under CA1506's 41 coupled types |

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] no code outside `Infrastructure/Configuration` names a configuration source
- [ ] `make agent-check` passes
- [ ] the three start-up guards still refuse, with their messages intact
- [ ] `status: done` in this file
