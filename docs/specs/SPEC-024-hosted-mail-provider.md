---
id: SPEC-024
title: Mail that actually leaves the machine — the Brevo sender behind IMailService
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S1a             # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0013, ADR-0028, ADR-0029]
standards: [secrets-management, application-security, authentication-authorization, testing, logging-observability]
approved_by: enesozmert # this plan, approved 2026-10-04
escalation: |
  Three gates answer yes. **Gate 2:** `Microsoft.Extensions.Http` is added to `MusicRoom.Infrastructure`, and
  unlike SPEC-023's Maven dependencies this one ships. It is first-party and it is what gives the sender a
  typed client with a bounded timeout; the alternative, registering the client from `WebAPI`, keeps the
  dependency set untouched and moves the adapter's registration out of the layer that owns the adapter. Owner
  chose the package, 2026-10-04. **Gate 5:** this is the component that handles the plaintext verification and
  reset tokens, so the whole unit is inside the token surface. **Gate 1 is a no with a note:** ADR-0013 already
  decided provider, abstraction and configuration keys, and picking Brevo over Resend is a choice it offers.

  One thing the owner settled rather than the spec: `MAIL_FROM` is Brevo's **sandbox sender**, not a verified
  domain. ADR-0013 permits that for the defence provided it is said out loud instead of being presented as
  production, so it is said here, in `.env.example`, and in `docs/SDD.md` §10.1.
---

# The hosted mail sender

## Goal

After this spec a registration on a non-Development host sends a real message to a real mailbox, and the
verification and password-reset flows can be demonstrated end to end rather than read out of
`docker compose logs`. `IMailService` gains a second implementation, `BrevoMailService`; `MAIL_PROVIDER`
chooses between it and the Development logger; and the start-up guard that refuses to boot outside
Development becomes a guard that refuses to boot **without a sender**, which is what closes issue O17 and
threat T-26.

Precondition met: T-25 closed with SPEC-006 on 2026-09-27, so every endpoint whose success path is one mail
send is rate-limited per IP and, where it names an account, per account. That was the owner's condition for
this slice and it is why a registration loop cannot spend the provider's quota (T-19).

## Out of scope

- **Bounce, complaint and delivery-status handling.** ADR-0013 put it out of scope. A send failure is
  swallowed by `CredentialMailDispatcher` and the user's recovery is the resend endpoint, which exists.
- **A verified sender domain.** The sandbox sender is the owner's choice, recorded above. Verifying a domain
  is DNS work outside this repository.
- **Retry or a queue.** One attempt per request, bounded by a timeout. Reasoning in decision 4.
- **Provider templates.** Subjects and bodies are composed in code, so the repository is the source of truth
  for what the message says.
- **Changing any endpoint.** `/auth/registrations`, `/auth/verification-requests` and
  `/auth/password-reset-requests` already answer `202` and say nothing about delivery — `docs/api/ENDPOINTS.md`
  is unchanged by design, because a caller must not be able to tell from a response whether a mailbox exists.

## Decisions taken under the approval of 2026-10-04

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **Brevo**, through `POST https://api.brevo.com/v3/smtp/email` with the `api-key` header. | It is the key that arrived. ADR-0013 offers Resend or Brevo and the abstraction makes the choice one class, so the other remains a one-file change |
| 2 | **`MAIL_PROVIDER` selects the implementation.** `brevo` → `BrevoMailService`. `logging` → `LoggingMailService`, and only on a Development host. Anything else, including an empty value, fails start-up | The old guard asked "is this Development?" and the new one asks "is there a sender?", which is the question that was always meant. A typo in `MAIL_PROVIDER` must not silently downgrade a production host to the sender that writes credentials into a log — that is T-26, and this is the mechanism that closes it |
| 3 | **`brevo` requires `MAIL_API_KEY` and `MAIL_FROM` at start-up**, checked before the container is built, with a message that names the missing variable and never its value. | A sender that starts without a key fails on the first registration, which is the worst moment to find out: the account is written and the mail is lost. Rejected: defaulting the from-address, which would send from an address nobody chose |
| 4 | **One attempt, 10-second timeout, no retry inside the sender.** | The resend endpoint *is* the retry path and it is rate-limited; a retry loop multiplies quota spend against T-19 and does it unbounded by any limiter, because the retry happens after the limiter has already let the request through. `CredentialMailDispatcher` already swallows the failure, so a dead provider costs a log line and not a `500` — which is also what keeps the `202` from becoming an enumeration oracle |
| 5 | **Subjects and bodies are composed in code, as HTML and plain text**, from the token and a configured link base. | `VERIFICATION_LINK_BASE_URL` was already read by `Program.cs` and was missing from `.env.example`; the reset flow had no link base at all, so this spec adds `PASSWORD_RESET_LINK_BASE_URL`. A provider template id would put the words in a vendor dashboard where no review sees them |
| 6 | **The provider's response is untrusted input — API10.** Only the status code and `messageId` are read; the body is never echoed to a caller and never logged whole; redirects are not followed. | The checklist's own rule for Google, Facebook and YouTube applies to a fourth outbound call. A provider that returns an error document must not be able to put its content into our logs or our responses |
| 7 | **No test sends a real message.** Unit tests drive a stubbed `HttpMessageHandler`; the one live send is a manual step, run by the owner with the real key, recorded in Verification | A test that calls Brevo spends quota on every CI run (T-19) and fails when the network does. Rejected: an integration test behind an environment flag, which is a test that never runs and therefore proves nothing |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **no, with a note** — ADR-0013 decided the provider, the abstraction and the three configuration keys. Choosing Brevo is a choice it offers; the sandbox sender is a consequence it anticipated |
| 2 | new or upgraded dependency? | **yes, and it ships** — `Microsoft.Extensions.Http` on `MusicRoom.Infrastructure`, for a typed client with a bounded timeout. First-party. Owner chose it over registering the client from `WebAPI` |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration. Nothing about tokens changes; the digest storage SPEC-003 and SPEC-007 built is untouched |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — and deliberately so. A caller must not learn from a response whether a message was sent |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes, all but audit** — this component receives the plaintext verification and reset tokens and holds the provider API key. It logs neither |
| 6 | lowers a severity, deletes or skips a test? | no — it adds three suites and keeps `LoggingMailServiceTests`, because the Development sender stays |
| 7 | touches or copies from `reference/`? | no — `Core.Mailing` from the reference repository is not reproduced, per ADR-0013 |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/MailOptions.cs` | create — provider, key, sender, two link bases, and the validation that makes a bad combination fail at start-up |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/CredentialMailTemplates.cs` | create — the two subjects and bodies, and the link composition |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/BrevoMailService.cs` | create — the provider call, and nothing else |
| `MusicRoom/src/MusicRoom/Infrastructure/Messaging/MailServiceRegistration.cs` | create — provider selection, the typed client, the start-up guard |
| `MusicRoom/src/MusicRoom/Infrastructure/InfrastructureServiceRegistration.cs` | change — delegates mail; `InfrastructureOptions` carries `MailOptions` instead of two loose values |
| `MusicRoom/src/MusicRoom/Infrastructure/MusicRoom.Infrastructure.csproj` | change — `Microsoft.Extensions.Http` |
| `MusicRoom/Directory.Packages.props` | change — its `PackageVersion` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — one `ReadMailOptions`, and `InfrastructureSetup` so `Main` stays under CA1506's 41 coupled types |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/LoginEndpointTests.cs` | change — `TestMailSettings`, because a host now needs a sender to start |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/*.cs` | change — 41 call sites gain `TestMailSettings.Apply(webHost)` beside `TestJwtSettings.Apply` |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/LoggingMailServiceTests.cs` | change — the new options shape, and the guard's new rule |
| `Makefile` | change — the OpenAPI generator boots the host, so it needs `MAIL_PROVIDER` too |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/StubHttpMessageHandler.cs` | create — the handler the sender's tests drive |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/BrevoMailServiceTests.cs` | create — the request, the headers, the link, the secret-free log, the failure |
| `MusicRoom/tests/MusicRoom.Infrastructure.Tests/Messaging/MailOptionsTests.cs` | create — which combinations are allowed to start |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/MailProviderStartupTests.cs` | create — which implementation a host ends up with |
| `.env.example` | change — `MAIL_PROVIDER` values, the sandbox-sender note, the two link bases |
| `docs/SDD.md` | change — §10.1, §13.1, §16's V.1 rows, §17 closes O17 |
| `docs/owasp/THREAT-REGISTER.md` | change — closes T-26, T-19 gains its real mitigation |
| `docs/owasp/API-TOP10-CHECKLIST.md` | change — API10 names the mail provider |
| `docs/DELIVERY-PLAN.md` | change — S1a, and the stale S3 and S9 states |
| `brain/projects/musicroom-backend.md` | change — S1a, O17 out of the open list |
| `docs/specs/SPEC-024-hosted-mail-provider.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Four new files in `Infrastructure/Messaging`, one reader in `WebAPI`. `Application` does not
change at all, which is the test of ADR-0028: the layer that asks for mail to be sent cannot tell that a
provider arrived.

**2. Boundary types.** `MailOptions` is `public`, because the composition root builds it. `BrevoMailService` is
`internal`, like `LoggingMailService` — nothing outside the layer may name it.

**3. Invoked?** By `CredentialMailDispatcher`, through `IMailService`, from the three handlers that already
call it. No new call site.

**4. CLI or package.** `Microsoft.Extensions.Http`, gate 2.

**5. corePackages.** Unchanged.

**What the walk caught.** Four things.
- **The guard had to change meaning, not disappear.** `AddInfrastructureServices` currently throws outside
  Development. Deleting that line would let a host with a typo'd `MAIL_PROVIDER` boot with no sender; inverting
  it to "a sender must be registered" keeps the mechanical protection T-26 relies on.
- **`VERIFICATION_LINK_BASE_URL` is read by the application and absent from `.env.example`.** Found while
  reading, fixed here: a variable the host reads and the example file omits is a variable nobody sets.
- **The reset mail needs its own link base.** `LoggingMailService` logs the bare reset token, so the gap was
  invisible until a real message had to contain a clickable link.
- **Four source files rather than one.** `InfrastructureServiceRegistration` is at the S107 argument limit and
  `Program.cs` is at CA1506's coupling limit, so neither can absorb provider selection.

## What the build added to this plan

Three things the plan did not foresee, each written where it is enforced rather than only here.

- **`Main` went over CA1506 again.** Adding `MailOptions` to `InfrastructureOptions` pushed the composition
  root to 42 coupled types, so the wiring moved into `InfrastructureSetup`, the same way `CallerServices` and
  `RealtimeServices` were extracted before it.
- **Every test host had to be given a sender.** Making a sender a start-up requirement broke 312 WebAPI tests
  at once, which is the guard working: the suites now call `TestMailSettings.Apply` beside
  `TestJwtSettings.Apply`, explicitly at each of 41 call sites rather than hidden inside the JWT helper.
  `MailProviderStartupTests` is the one suite that deliberately does not, because the refusal is its subject.
- **`make openapi` boots the host**, so the document generator needed `MAIL_PROVIDER=logging` as well. The
  regenerated document is byte-identical, which is the check that this unit changed no endpoint.

One test-only trap worth recording: the first version of the sender's tests replaced the transport with
`ConfigureHttpClientDefaults`, which applies *before* a named client's own configuration and therefore lost to
it — the suite reached `api.brevo.com` and was answered `401`. Fixed by configuring the named client. A test
that silently makes a real call is worse than a failing one.

## Steps

1. `MailOptions` with its validation, and `MailOptionsTests` for the combinations that may and may not start.
2. `CredentialMailTemplates`: subject, HTML and text for both messages, link composed from the base and the
   URL-escaped token.
3. `BrevoMailService` over a typed `HttpClient`: one `POST`, status checked, `messageId` logged, body never.
4. `MailServiceRegistration`: select on `MAIL_PROVIDER`, register the typed client with its 10-second timeout
   and no automatic redirects, throw when nothing valid is configured.
5. Wire it: `InfrastructureOptions` carries `MailOptions`, `Program.cs` reads it, `.env.example` documents it.
6. Tests: the sender against a stub, and the start-up selection against a test host.
7. `make agent-check`, then the one manual live send.
8. Documents: SDD §10.1/§13.1/§16/§17, the threat register, the API checklist, the delivery plan, the project
   card.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `BrevoMailServiceTests` — the request | `POST` to `/v3/smtp/email`, `api-key` header present, sender from `MAIL_FROM`, recipient from the call |
| — the link | the body carries the configured base and the URL-escaped token, and the token appears nowhere else |
| — the log | no log event contains the API key or the token (decision 6, `secrets-management`) |
| — a non-2xx | throws, so `CredentialMailDispatcher` swallows it and registration still answers `202` |
| — the provider's error body | is not in the exception message and not in any log event |
| `MailOptionsTests` | `brevo` without a key, or without `MAIL_FROM`, is refused; `logging` outside Development is refused; an unknown value is refused |
| `MailProviderStartupTests` | a Development host with `MAIL_PROVIDER=logging` resolves `LoggingMailService`; any host with `brevo` and its two values resolves `BrevoMailService`; a Production host with an empty `MAIL_PROVIDER` fails to start |
| `LoggingMailServiceTests` | unchanged and still passing — the Development path is not collateral |
| **manual, once, by the owner — OUTSTANDING** | `MAIL_PROVIDER=brevo` with the real key and `MAIL_FROM` filled, a registration, and the message arriving in a real mailbox from the sandbox sender. No automated test spends quota (decision 7), so this is the only evidence that the provider call itself works end to end. Everything else in this table passes |

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [ ] **a real message arrives, sent by the owner's manual run** — the one open item
- [x] T-26 is closed in `docs/owasp/THREAT-REGISTER.md` and O17 in `docs/SDD.md` §17
- [x] `docs/DELIVERY-PLAN.md` S1a is `merged`, and the stale S3 and S9 states are correct
- [x] `status: done` in this file
