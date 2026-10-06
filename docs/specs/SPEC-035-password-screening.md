---
id: SPEC-035
title: Screen a chosen password against a bundled breached set and the caller's own context words
status: in-progress     # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0007, ADR-0028, ADR-0042]
standards: [authentication-authorization, application-security, clean-architecture-cqrs, testing]
approved_by: enesozmert # gates 1, 4 and 5 answered by the owner on 2026-10-06
escalation: |
  **Three gates are yes: 1, 4 and 5. The unit stops here and waits.**

  Gate 5 is a yes for the same reason SPEC-030 and SPEC-034 were: this is the password path itself. That one was
  expected. Gates 1 and 4 are yes because walking the files found three things ADR-0042 assumes that the code
  does not do, and one published behaviour that changes. Each is stated with the measurement behind it.

  **1. Gate 1 — ADR-0042's "two homes" paragraph is wrong about both non-register paths, so it needs an
  amendment.** It says the context screen works because reset and change "read the context words from the row
  their handler already loads". Measured:

  - `ResetPasswordCommandHandler` loads **nothing**. It hashes the new password and calls
    `TryResetPasswordAsync(tokenHash, newPasswordHash, nowUtc, ct)`, which spends the token, replaces the hash
    and revokes every refresh family in one transaction. There is no read before the write, and no repository
    method that finds an account by a reset-token digest.
  - `ChangePasswordCommandHandler` does load a row, and that row carries no context. `UserCredential` is
    `(Guid UserId, string PasswordHash, bool IsEmailVerified)` — no address, no display name.
  - The access token carries neither. `JwtAccessTokenIssuer` says so in a comment and means it: the payload is
    `sub`, `sid` and `jti`, "no address, no display name". So `ICurrentUserAccessor` cannot supply the words
    either.

  The screen is still buildable on both paths, and §"The two homes, measured" below says how. But the ADR's
  stated reason for putting the context screen in the handlers is not the reason that holds, and a decision
  record whose rationale is counterfactual is worse than one that is silent. It needs the same kind of amendment
  #82 already carries.

  **2. Gate 4 — a `422` is answered where a `204` used to be, and `docs/api/ENDPOINTS.md` documents the `422`
  semantics of these routes.** No status code is added: `422` is already listed on `/auth/registrations`,
  `/auth/password-resets` and `/me/password`. What changes is that a password those routes accept today is
  refused tomorrow, and that file already carries a note for exactly this class of fact — line 114, "`POST
  /me/password` answers `422` for a wrong current password, not `401`". One note line belongs beside it.

  **3. Gate 5 — authentication, and three specific traps in it.**

  - **The refusal must be a returned value, never an exception.** SPEC-009 learned this the expensive way: the
    audit behaviour is fail-closed and shares the handler's transaction, so a throw from inside a handler rolls
    the audit row back with it, which is why the limiter's refusal stopped being an exception. A handler-side
    context refusal that threw `ValidationException` would answer `422` correctly and leave no record that it
    happened.
  - **A refused reset must not spend the token.** The screen runs before `TryResetPasswordAsync`, so a caller
    whose passphrase was refused can retry on the same link. Screening after the write is not a worse
    message — it is a password already changed.
  - **Nothing may echo the password.** Both screens have a natural failure message that quotes the offending
    input, and either would put a password in a log, a proxy and a crash report at once. The messages name
    the screen and nothing else, and the proof is an assertion on the absence rather than a review of the code.

  **4. Two analyzer ceilings are already touching, and they chose the design rather than the reverse.**

  - `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` is at **293 code lines of S104's 300**.
    The reset path's pre-read is one more query method, roughly twelve code lines with its projection and its
    documentation, which puts that file over the cap. This is SPEC-032's situation again, and the remedy that
    does not touch a severity is a **new file**: a second, small repository beside it. That is an owner call,
    and issue **O27** records the file for the next unit that needs a method there.
  - `ChangePasswordCommandHandler`'s constructor takes **five dependencies**, which is S107's limit — proven
    live in S0 at six. A sixth for a context reader would not compile under `-warnaserror`. The change path
    therefore reads its context words from the row it **already** loads, by widening `UserCredential`, which
    adds no parameter anywhere.

  **Owner's answers, 2026-10-06: approved, all three gates.** The walk earned its turn. One of my own records
  was wrong again and you measured it rather than working around it.

  Six answers, binding:

  1. **Amend ADR-0042, and use this text rather than paraphrasing it.** Add to the record, as its third
     amendment line: *"Corrected 2026-10-06 by SPEC-035's measurement. The 'two homes' paragraph said reset and
     change read the context words from the row their handler already loads. Neither does. `ResetPasswordCommandHandler`
     reads nothing — `TryResetPasswordAsync` spends the token and replaces the hash in one transaction, with no
     read before the write — and `ChangePasswordCommandHandler`'s `UserCredential` carries only `UserId`,
     `PasswordHash` and `IsEmailVerified`. The access token carries neither value either. The conclusion stands
     and the reason is replaced: the context screen belongs where the account can be reached, which means a
     pre-read on the reset path and two more projected columns on the change path."*
  2. **`PasswordContextRepository` is approved, as a new file.** `UserRepository` at 293 of 300 is the same wall
     SPEC-032 hit, and the answer is the same: a new file, never a raised cap. Keep it to the one query it needs.
     O27 records the file for the next unit, with the line count, exactly as O24 did for `Program.cs`.
  3. **Widen `UserCredential` rather than adding a sixth constructor parameter.** S107's limit is five and S0
     proved it fires at six; two more projected columns cost nothing at the call site. Put the reason in the
     record's own documentation, as your Files table already says.
  4. **Context words are screened on all three paths, including reset.** Skipping them there would leave
     V6.1.2 and V6.2.11 half-met on the path a person uses precisely when they are improvising a new password.
     That is why answer 2 authorises the file.
  5. **Your three traps are the acceptance criteria, not notes.** A refusal is a returned value and never an
     exception, because the audit behaviour is fail-closed and a throw takes its row with it — SPEC-009 paid
     for that lesson. A refused reset leaves the token spendable, and there is a test for it in your Files
     table; keep it. And nothing echoes the password anywhere, proven by an assertion on the absence.
  6. **The reach sentence is not optional.** 46,146 entries from 999,999 is the honest number and it goes beside
     V6.2.4 and V6.2.12 in `docs/owasp/ASVS-CONFORMANCE.md`, with the filter's reason: a 12-character minimum
     makes every shorter entry unreachable, so the retained set is the only part of a common-password list this
     system could ever accept. Pin the source by the commit you already recorded — a list fetched from a moving
     branch is not a measurement.

  **5. What the gate walk did *not* find, stated because it is the cheap half of this unit.** No existing test
  breaks. Every password literal in the test suites was checked against the bundled set and against the context
  screen, with all 383 `@example.com` local parts and both display names the suites use:

  | Test password | Normalised | In the bundled set | Caught by its own context words |
  |---|---|---|---|
  | `correct horse battery` | `correcthorsebattery` | no | no |
  | `a brand new passphrase` | `abrandnewpassphrase` | no | no |
  | `a-replacement-password` | `areplacementpassword` | no | no |
  | `the attackers passphrase` | `theattackerspassphrase` | no | no — its account is `replayed-reset@example.com` |

  The fourth row is the one worth naming: `attacker` appears as a local part elsewhere in the suites, so a
  naive scan flags it. It is in `SessionEndpointTests`, on a sign-in that chooses no password, while the
  passphrase is in `PasswordResetEndpointTests` on `replayed-reset@example.com`. Different requests, no
  collision. Login is not screened, which is also what makes that safe — ADR-0042.

  **Three questions for the owner, and I have a recommendation on each.**

  1. **The source.** SecLists' `xato-net-10-million-passwords-1000000.txt`, filtered, gives **46,146** entries —
     ADR-0042's cap is 100,000, so it fits with room. The figures and the honest limits are in §"The bundled
     set, measured". Recommend yes.
  2. **The new Persistence file for the reset pre-read**, because `UserRepository.cs` has seven lines of headroom
     and the alternatives are a suppression or a severity. Recommend yes, as a new file rather than a split of
     the existing one: a split touches a 447-line file that fourteen methods and every auth suite depend on, for
     a unit that is about screening.
  3. **Widening `UserCredential` with `Email` and `DisplayName`.** Login receives both and ignores both — it
     already knows the address it was given. Recommend yes; the alternative is a sixth constructor dependency
     that does not compile.
---

# Screen a chosen password against a bundled breached set and the caller's own context words

## Goal

After this, a password this system accepts is one that is not in a bundled set of 46,146 known breached
passwords and does not contain the caller's own words — the project name, their address local part or their
display name. Today both `musicroom2026` and `i love you forever` are accepted on all three paths that choose a
password. ASVS **V6.1.2**, **V6.2.4**, **V6.2.11** and **V6.2.12** move from *not met* to *met*, and **T-32**
closes with its residual named. Neither screen makes an outbound request, which is ADR-0042's whole decision.

## Out of scope

- **Login.** A password already on an account must still sign in. ADR-0042 decides this, and nothing in ASVS
  asks otherwise here.
- **Have I Been Pwned, or any other remote set.** Rejected in ADR-0042 with three written reasons. The
  abstraction this unit introduces is the seam if that is ever revisited.
- **Rehashing or expiring existing passwords.** An account whose password is in the set keeps working. That is
  the residual T-32 carries when it closes.
- **A password-strength meter, composition rules, or a client-side check.** The policy stays "12 characters, no
  composition rules" — ADR-0007.
- **Refreshing the bundled file on a schedule.** It is a data change with no code change, which is the shape
  ADR-0042 asked for, and it is nobody's unit until the file is stale.

## The bundled set, measured

Facts rather than intentions, because ADR-0042 requires the source and both counts to be recorded here.

| Fact | Value |
|---|---|
| Source | SecLists, `Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt` |
| Commit | `c205c36a445bff37f8e58a9ec829105cd4975c58`, 2025-05-08 |
| URL | `https://raw.githubusercontent.com/danielmiessler/SecLists/c205c36a445bff37f8e58a9ec829105cd4975c58/Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt` |
| Licence | MIT, confirmed from the repository's own licence metadata |
| `sha256` of the source | `424a3e03a17df0a2bc2b3ca749d81b04e79d59cb7aeec8876a5a3f308d0caf51` |
| Raw entries, non-empty lines | **999,999** |
| Retained, 12 to 64 characters | **46,296** (4.6%) |
| Retained after lowercasing and de-duplicating | **46,146** — the bundled count |
| Of those, letters only, the passphrase shape | **14,700** |
| Bundled file size | 674,833 bytes; 326,477 compressed, which is what the assembly carries |
| `sha256` of the bundled file | `e1e5d002f1b7144d4826ff33ac03fa25ce5e1bb214ffde9c82da2245190716fb` |

**Why this list and not the obvious one.** The path ADR-0042's register row implied,
`10-million-password-list-top-1000000.txt`, returns **404** on `master` — SecLists reorganised the directory in
the commit above. Three candidates were measured at the filter the policy forces:

| Candidate | Raw | Retained at ≥ 12 characters |
|---|---|---|
| `xato-net-10-million-passwords-1000000.txt` | 999,999 | **46,296** |
| `Pwdb_top-1000000.txt` | 1,000,000 | 31,116 |
| `100k-most-used-passwords-NCSC.txt` | 99,839 | 1,212 |

The first retains the most and stays under the 100,000 cap, so it is the one.

**What the reach actually is, stated plainly.** 95.4% of a million-entry list is unreachable here, because the
12-character minimum already refuses it — `123456` and `password` can never be a password on this system, so
bundling them buys nothing. What remains is 46,146 entries of the length an attacker's dictionary would have to
reach for. That sentence, with the two counts, goes next to **V6.2.4** and **V6.2.12** in
`docs/owasp/ASVS-CONFORMANCE.md`: the requirement asks that a chosen password be checked against a breached
set, and it is met with a snapshot that is smaller than the best available and does not learn about tomorrow's
breach.

**The passphrase question, answered with the file rather than with hope.** ADR-0042 asked to prefer a source
that includes passphrases. Measured in the retained set:

- Present: `iloveyouforever`, `thisismypassword`, `passwordpassword`, `qwertyuiopasdfgh`.
- Absent: `correcthorsebatterystaple`, `letmeinplease`, `correcthorsebattery`.

So the shape is covered and the single most famous example of it is not. **No entry in the set contains a
space** — every passphrase in it is written as one run of letters. A user types `i love you forever`, so the
breached screen must compare the separator-stripped form as well as the submitted one, or the set would miss
every passphrase in it. That is decision 2 below, and it is the difference between a screen that works and a
screen that passes its own tests.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0042's "two homes" rationale is counterfactual on both non-register paths |
| 2 | new or upgraded dependency? | no — the set is a text file compiled in as a resource. Data, not a package: no `PackageReference`, nothing for `dotnet list package --vulnerable` to see, no transitive anything |
| 3 | schema change beyond this spec, or destructive migration? | no — the address and display name columns already exist, and nothing is written |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — one note line: a password accepted today can be refused with `422` tomorrow |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — it is the password path, and the refusal has to leave an audit row without rolling it back |
| 6 | lowers a severity, deletes or skips a test? | no — and §4 of the escalation is about the two ceilings that made that tempting |
| 7 | touches or copies from `reference/`? | no |

Any "yes" means Tier B. Set `status: draft`, fill `escalation:`, stop.

## Decisions this spec proposes

1. **`IBreachedPasswordSet` in `MusicRoom.Application.Abstractions.Security`, one method, `bool Contains(string
   password)`.** The Application layer names the abstraction and never learns that the answer comes from an
   embedded file — ADR-0028. `BundledBreachedPasswordSet` in `MusicRoom.Infrastructure.Security` reads the
   resource once in its constructor into a `HashSet<string>` with `StringComparer.OrdinalIgnoreCase` and is
   registered as a **singleton**: one load at start-up, one hash lookup per password choice, nothing per
   request. A missing or empty resource throws at construction, so a deployment that lost the file fails to
   boot rather than booting with no screen — ADR-0042.
2. **The breached screen checks two forms of the candidate: as submitted, and lowercased with separators
   stripped.** The set contains no spaces, and a passphrase is the shape that gets past a length rule, so a
   single literal comparison would be a screen with a 14,700-entry hole in it.
3. **The breached screen lives in `PasswordRules.ChosenPassword`**, the one rule SPEC-034 created, so all three
   paths get it from one place. The rule takes the set as a parameter, and the three validators take it as a
   constructor dependency. These become the **first validators in this repository with a dependency**;
   `AddValidatorsFromAssembly` activates them through the container, and all three are already `public`, which
   SPEC-025's lesson says is the thing that must stay true.
4. **The context screen's logic lives once, in `PasswordContext` in `MusicRoom.Application.Features.Auth`**,
   beside `PasswordRules`. It holds the fixed words — `musicroom`, `music room`, `musicroomapp` — the
   normalisation, and the containment test. Three callers, one rule.
5. **Normalisation, named exactly.** Lowercase with the invariant culture, then remove space, tab, `.`, `_` and
   `-`. Applied to the candidate password and to every context word before comparison. A context word shorter
   than **four characters** after normalisation is dropped, so a display name of `Al` refuses nothing.
   Containment, not equality: `musicroom2026` is the case ADR-0042 named.
6. **Register screens context in its validator**, because `RegisterCommand` carries the address and the display
   name. A cross-property rule keyed on `Password`, which is the key that route's `422` already uses.
7. **Change screens context in its handler**, from a **widened `UserCredential`** that gains `Email` and
   `DisplayName`. No new dependency, because its constructor is at S107's limit. Login receives the two fields
   and ignores them.
8. **Reset screens context in its handler, from a new read before the write.** `IPasswordContextReader` in
   `MusicRoom.Application.Abstractions.Security`, one method taking the token digest and returning the address
   and display name or null, implemented in a **new** Persistence file because `UserRepository.cs` has seven
   code lines of headroom. A null answer — unknown, spent or expired token — screens against the fixed words
   only and leaves the status code to the write path, so a replayed link still answers `400`.
9. **Both handler refusals are returned values that map to `422` with an `errors` body keyed `NewPassword`**,
   through `Results.ValidationProblem`. `ChangePasswordOutcome` and `ResetPasswordOutcome` each gain one
   member. The audit outcome is `Refused`, and it commits, which is only true because the refusal is not an
   exception — SPEC-009.
10. **Two messages, neither quoting the input.** "Password is on a list of known breached passwords." and
    "Password must not contain your name, address or the application's name." The second never says which word
    matched: naming it would confirm a display name or an address to whoever submitted the request.

## The two homes, measured

ADR-0042's shape holds; its stated reason does not. What is true:

| Path | Chooses a password where | Carries the context words | So the context screen sits |
|---|---|---|---|
| register | `RegisterCommand` | **yes** — `Email` and `DisplayName` are on the command | in `RegisterCommandValidator` |
| change | `ChangePasswordCommand` | no — and the token has neither, and the constructor is full | in the handler, off the row it already reads, widened |
| reset | `ResetPasswordCommand` | no — and the handler reads nothing at all today | in the handler, off a new read taken **before** the write |

The breached screen needs only the password, so it sits in the shared rule for all three and needs none of this.

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/IBreachedPasswordSet.cs` | create — one method, `Contains` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Security/IPasswordContextReader.cs` | create — the reset path's pre-read, and the record it returns |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/PasswordContext.cs` | create — fixed words, normalisation, the four-character floor, containment |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/PasswordRules.cs` | change — the breached screen joins the rule; the rule takes the set |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/Register/RegisterCommandValidator.cs` | change — take the set; add the cross-property context rule on `Password` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandValidator.cs` | change — take the set |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommandValidator.cs` | change — take the set |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordCommandHandler.cs` | change — read the context, screen, then write |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ResetPassword/ResetPasswordOutcome.cs` | change — one member, `RefusedByContext` |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommand.cs` | change — one `ChangePasswordOutcome` member and its factory |
| `MusicRoom/src/MusicRoom/Application/Features/Auth/Commands/ChangePassword/ChangePasswordCommandHandler.cs` | change — screen after the current password is verified, before the hash is written |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IUserRepository.cs` | change — `UserCredential` gains `Email` and `DisplayName`, with the reason in the record's own documentation |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/UserRepository.cs` | change — **two projection lines only**. 293 code lines of 300 (issue O27) |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/PasswordContextRepository.cs` | create — the reset-token pre-read. A new file because of the line above |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — register it |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/BundledBreachedPasswordSet.cs` | create — loads the resource once, case-insensitive set, throws if absent |
| `MusicRoom/src/MusicRoom/Infrastructure/Security/breached-passwords.txt` | create — 46,146 entries, lowercase, LF, one per line |
| `MusicRoom/src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj` | change — one `EmbeddedResource` item. The project's first |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — register the set as a singleton |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/PasswordResetEndpoints.cs` | change — map the new outcome to `422` with the `errors` key |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/AccountPasswordEndpoints.cs` | change — the same |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Security/BundledBreachedPasswordSetTests.cs` | create — the resource loads, the count is 46,146, four known entries are found, one known absentee is not |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/PasswordContextTests.cs` | create — containment, the four-character floor, separator stripping, case |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/RegisterCommandValidatorTests.cs` | change — the constructor takes the set; add the two screens' cases |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/ResetPasswordCommandValidatorTests.cs` | change — the same constructor change |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | change — add the breached and context refusals, keyed `Password` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordResetEndpointTests.cs` | change — add both refusals keyed `NewPassword`, **and** that a refused reset leaves the token spendable |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordChangeEndpointTests.cs` | change — add both refusals keyed `NewPassword`, and the audit row for a screened refusal |
| `docs/api/ENDPOINTS.md` | change — one note line beside the existing `422` notes |
| `docs/owasp/ASVS-CONFORMANCE.md` | change — V6.1.2, V6.2.4, V6.2.11, V6.2.12 to **met**, with the reach sentence beside V6.2.4 and V6.2.12 |
| `docs/owasp/THREAT-REGISTER.md` | change — T-32 closed, with the existing-accounts residual named |
| `docs/SDD.md` | change — §13.1's password policy, §16 for the four requirements, §17 gains **O27** |
| `docs/specs/SPEC-035-password-screening.md` | change — status transitions |
| `brain/decisions/ADR-0042-breached-and-context-password-screening.md` | change — the amendment gate 1 names. **Owner's, not mine, unless they say otherwise** |

### The Files table walk

**1. Assemblies, and which references that implies.** Four: `Application` (abstractions, the rule, the context
type, three validators, two handlers, two outcomes), `Persistence` (one widened projection, one new repository,
one registration), `Infrastructure` (the set, the resource, one registration), `WebAPI` (two endpoint mappings).
Every reference needed already exists — `Infrastructure` and `Persistence` both reference `Application`, and
`WebAPI` references all three. **No new project reference.** The one `.csproj` edit is an `EmbeddedResource`
item, not a `PackageReference`, which is why gate 2 is a no.

**2. Types crossing an assembly boundary.** Three, and each is `public` for a reason that is not "it was
easier": `IBreachedPasswordSet` and `IPasswordContextReader` are implemented in `Infrastructure` and
`Persistence` respectively, so `internal` would not compile; `UserCredential` already crosses. `PasswordContext`
stays **internal** — only the validators and handlers beside it use it. `PasswordRules.ChosenPassword` stays
internal for the same reason, and `PasswordRules`' constants stay `public` because
`ResetPasswordCommandValidatorTests` reads one.

**3. Does anything invoke it?** The three validators are `public` and registered by `AddValidatorsFromAssembly`,
which **skips internal types** — SPEC-025's lesson, and the reason none of them may become internal here. The
two handlers are reached by MediatR. The set and the reader are resolved by the container, so both registrations
are load-bearing: a missing one is a start-up failure, not a silent pass, which is the behaviour to want.

**4. A CLI, a tool manifest, or an unpinned package?** None. Central Package Management is untouched —
`Directory.Packages.props` gains nothing, so NU1008 cannot fire.

**5. Do the four corePackages contain what I assume?** `Core.Security` is empty, and it is again the tempting
home. Nothing goes in it: the breached set is this application's bundled data and the context words include this
application's name.

**What the walk caught.** Three things, all in the escalation: the reset handler reads nothing, so the ADR's
reason for the handler home does not apply; `UserRepository.cs` is at 293 of 300 code lines, so the pre-read
needs a new file; and `ChangePasswordCommandHandler` is at five constructor parameters, so the change path reads
its context from the row it already has rather than from a sixth dependency. Also this, which is the pleasant
one: **no existing test breaks**, and the escalation's table is how that was established rather than assumed.

## Steps

1. Take the source at the pinned commit, filter to 12 to 64 characters, lowercase, de-duplicate, sort, write
   with LF. Confirm 46,146 lines and the recorded `sha256` before committing the file.
2. `IBreachedPasswordSet`, then `BundledBreachedPasswordSet` with the `EmbeddedResource` item and the singleton
   registration. Prove the load in `MusicRoom.Infrastructure.Tests` before anything calls it.
3. `PasswordContext`: normalisation, the four-character floor, containment. Prove it in
   `MusicRoom.Application.Tests` before anything calls it.
4. The breached screen into `PasswordRules.ChosenPassword`; the three validators take the set. Run the three
   validator suites: every existing case must still pass with no edit beyond the constructor.
5. Register's context rule in its validator. Then change: widen `UserCredential`, screen in the handler, add the
   outcome, map the endpoint. Then reset: the new reader, the pre-read, screen, outcome, endpoint.
6. Assert the absence — no response body, no log line and no audit row contains the submitted password, on all
   three paths.
7. Documentation last, and only what the code supports: ASVS, the threat register, ENDPOINTS, SDD §13.1, §16 and
   §17's O27.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `BundledBreachedPasswordSetTests` — count and membership | the resource is embedded and loads: 46,146 entries, `iloveyouforever` and `thisismypassword` found, `correcthorsebattery` absent. Also the record of the set's reach, in a form that fails if the file is swapped |
| `BundledBreachedPasswordSetTests` — missing resource | a deployment without the file fails at construction rather than booting with no screen — ADR-0042 |
| `PasswordContextTests` | containment not equality, the four-character floor, separators stripped, case ignored. `musicroom2026` refused, `Al` refusing nothing |
| `RegisterCommandValidatorTests` + `RegistrationEndpointTests` | register refuses both screens with `422` keyed **`Password`** — V6.2.4, V6.2.11 |
| `PasswordChangeEndpointTests` | change refuses both with `422` keyed **`NewPassword`**, writes no new hash, and leaves an audit row — the row is what proves the refusal is a value and not an exception |
| `PasswordResetEndpointTests` — refused, then retried on the same link | a screened refusal does **not** spend the token. The reset is still possible with a better passphrase |
| `PasswordResetEndpointTests` — replay, unchanged | an unknown, spent or expired token still answers `400`, and the pre-read returning nothing changed no status code |
| every existing assertion in the auth suites, unedited | the policy did not move: the four passwords the suites use are still accepted, by measurement rather than by hope |
| an assertion on the absence of the submitted password in body, logs and audit row | decision 10, and `docs/owasp/API-TOP10-CHECKLIST.md`'s never-logged list |
| `make agent-check` | S104 on `UserRepository.cs` still passes with the projection widened, S107 on the change handler still passes, and the OpenAPI document is unchanged — no status code was added |

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] every command under Verification passes
- [ ] OWASP checklists walked for the surface touched; unfixed findings in the threat register
- [ ] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [ ] `status: done` in this file
