---
id: SPEC-037
title: A validation error key is the field the client sent, resolved once and held by a property test
status: in-progress     # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.6]
decisions: [ADR-0005, ADR-0043]
standards: [testing, api-contract, clean-architecture-cqrs]
approved_by: enesozmert # gates 1, 4 and 5 answered on 2026-10-06; ADR-0043 amended on #87
escalation: |
  **Three gates are yes: 1, 4 and 5. Gates 4 and 5 were expected. Gate 1 is the one to read.**

  ADR-0043's decision holds — the key is the field the client sent, configured once, never empty — and
  implementing it found **four things the record states that the code does not do**. Each is measured, each
  has a recommendation, and none of them is a reason to change the decision.

  **1. `WithName` does not set the key. It sets the message's display name.** The ADR says "the three existing
  `WithName` calls in `EventLicenceRules` become redundant". Measured: that file has **nine**, and two more
  live in `IssueEventCodeCommandValidator` (`ttlSeconds`) and `GrantControlCommandValidator`
  (`granteeUserId`) — eleven in validator code, against 70-odd `RouteHandlerBuilder.WithName` calls in
  endpoints that have nothing to do with this.

  The eleven do not behave alike, and SPEC-036's passing assertions are the proof:

  | Rule shape | Example | Key published today |
  |---|---|---|
  | member access **with** `WithName` | `RuleFor(c => c.TimeToLiveSeconds).WithName("ttlSeconds")` | **`TimeToLiveSeconds`** — the name is ignored for the key |
  | member access **with** `WithName` | `RuleFor(c => c.GranteeUserId).WithName("granteeUserId")` | **`GranteeUserId`** — the same |
  | whole-command rule with `WithName` | `RuleFor(c => licence(c)).WithName("presenceProof")` | **`presenceProof`** — here the name *is* the key |

  So the camelCase keys that exist today come from rules whose expression is **not** a member access, where
  FluentValidation has no property path and falls back to the overridden name. Removing those nine calls would
  not normalise them — it would publish **nine empty keys**, which is the defect this unit exists to remove.

  **Recommendation:** the primitive that sets a key is `OverridePropertyName`, not `WithName`. The resolver
  handles every member-access rule; `OverridePropertyName` handles each whole-command rule, and `WithName`
  stays only where a message needs a different word from the key.

  **2. A global camelCase resolver cannot produce the wire name on two routes, because the property and the
  field are spelled differently.** Measured against `docs/api/openapi.json`:

  | Route | Validated property | Resolver would give | The client actually sent |
  |---|---|---|---|
  | `GET /tracks` | `Query` | `query` | **`q`** |
  | `POST /events/{eventId}/event-codes` | `TimeToLiveSeconds` | `timeToLiveSeconds` | **`ttlSeconds`** (`EventCodeRequest.TtlSeconds`) |

  Both are endpoints whose request record names a field differently from the command it builds. The ADR's
  mechanism is right for the eighteen routes where the two agree and silently wrong for these two.

  **Recommendation:** resolver for the general case, `OverridePropertyName` for these two, and **the property
  test is what makes that safe** — a future mismatch fails a test rather than waiting for a client. Which is
  the ADR's own argument for the test, now with the two cases that prove it necessary.

  **3. `genres.count` is not a field the client sent.** The ADR offers it as an example of a kept dotted path.
  But `PUT /me/music-preferences` publishes `genres` as an **array of strings**: there is no `count` on the
  wire, so that key fails the ADR's own criterion — "a client should be able to take a key from `errors` and
  look it up in the body it just sent" — and it fails the property test the ADR asks for. The rule is
  `RuleFor(command => command.Genres.Count)`, so `Count` is a CLR member leaking into a published contract.
  `bio.value` is **not** the same case: `ProfileFieldRequestOfstring` really does carry `value`, so that path
  resolves.

  **Recommendation:** `OverridePropertyName("genres")` and `("artists")` on the two count rules, and strike
  `genres.count` from the ADR's example line. The alternative — weakening the property test to check only a
  key's first segment — would pass `genres.count` and also pass the next `Something.Count` nobody noticed,
  which is what the ADR asked the test not to do.

  **4. The one line cannot go in the composition root.** `MusicRoom/src/MusicRoom/WebAPI/Program.cs` is at
  **exactly 300 code lines**, S104's limit — issue **O24**, open since SPEC-032. One more line fails the build
  with `-warnaserror`, and the remedy O24 names is a refactor nobody has authorised.

  **Recommendation:** set it in `ApplicationServiceRegistration.AddApplicationServices`, which is the method
  that already registers every validator, is called by the composition root exactly once, and is at 35 code
  lines. Still "once, globally", which is the decision's substance; a different file from the one its prose
  names.

  **What the counts actually are, since the ADR and the unit brief both say seventeen.** Measured from the
  assertions SPEC-036 left: **26 key assertions over 17 distinct keys**. Three of those keys are already
  camelCase — `presenceProof`, `geofence`, `endsAtUtc`, five assertions — so **14 distinct keys change across
  21 assertions**. A reviewer should see 21 edited expectations in the diff, not 17.

  **Gate 4 is a yes** and the brief answers it in advance: `docs/api/ENDPOINTS.md` gains the key convention in
  §Conventions, and the missing `422` on `POST /auth/password-reset-requests` — which `PasswordResetEndpointTests`
  already proves — is corrected in the same unit. I am taking that as the owner's answer because the unit brief
  states it as a decision, not as a suggestion; if it was not, say so and I will split it out.

  **Gate 5 is a yes** because three of the fourteen keys are on credential routes: `Password` on register,
  `NewPassword` on reset and change, `Email` on the two mail routes. No credential logic changes — only the
  name a `422` body puts on a field — but a published key on an authentication path is not something to
  renumber on a Tier A run.

  **Owner's answers, 2026-10-06: approved, all three gates.** Your four corrections are confirmed from here
  and ADR-0043 carries them as an amendment on #87 — read the amended record, not the first version.

  Four answers, binding:

  1. **Gate 1 is answered by the amendment, not by this spec.** `WithName` sets a message's display name and
     `OverridePropertyName` sets the key; the resolver covers member-access rules and whole-command rules take
     the override. The nine `WithName` calls in `EventLicenceRules` stay — removing them publishes nine empty
     keys, which your measurement caught and my first draft would have caused.
  2. **`q` and `ttlSeconds` take `OverridePropertyName`, and `genres.count` is a defect rather than a
     convention.** `MusicPreferencesRequest` publishes `genres` and `artists` with no `count`, so that key
     fails this ADR's own criterion; it takes `OverridePropertyName("genres")`, `artists` the same, and
     `bio.value` stays because `ProfileFieldRequestOfstring` really carries `value`. The property test is not
     weakened to first-segment matching — that would pass the next `Something.Count` nobody noticed.
  3. **Run the Application suite first, before touching any expectation.** `ValidatorOptions.Global` is
     process-wide static and a validator built with `new` sees the framework default, which the property test
     cannot see. Whatever that suite reports is the measurement. A divergence is fixed by making the suite use
     the same configuration, never by weakening an assertion.
  4. **Gate 4 was a decision, not a suggestion.** `docs/api/ENDPOINTS.md` §Conventions gains the key rule and
     `POST /auth/password-reset-requests` gains the `422` it already produces, both in this unit.

  The count to prove in the diff is **21 edited expectations over 14 distinct keys**, which is the corrected
  arithmetic, not the seventeen my first draft asserted.

  **One residue in the amended record, not blocking.** Its Consequences list still opens with *"Seventeen keys
  change from PascalCase to camelCase"*, which the amendment above it corrects to fourteen across 21
  assertions. The amendment is the figure this spec implements; the bullet is a leftover for whoever next
  touches that file.
---

# A validation error key is the field the client sent, resolved once and held by a property test

## Goal

After this, every key in a `422`'s `errors` dictionary is a field name the client could look up in the body or
query string it just sent: camelCase, dotted where the field is nested, and **never empty**. One line in one
place produces it for every validator, including validators written after this. A property test asserts the
rule against `docs/api/openapi.json` rather than against a list of expected strings, so the next route cannot
quietly break it. Issue **O28** closes.

## Out of scope

- **The envelope.** `HttpValidationProblemDetails`, the status codes, the messages. Only the keys move.
- **`docs/api/openapi.json`.** The envelope is unchanged, so the committed document is unchanged and the drift
  guard stays green — which is exactly why it could never have caught this.
- **Handler-produced `422`s.** They answer `Results.Problem` with no `errors` dictionary. Nothing to key.
- **Renaming any request field.** `q` stays `q`; `ttlSeconds` stays `ttlSeconds`. The error key moves to meet
  the field, never the other way.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — ADR-0043's mechanism needs four corrections, each measured in `escalation` |
| 2 | new or upgraded dependency? | no — `ValidatorOptions.Global` is FluentValidation's own API |
| 3 | schema change beyond this spec, or destructive migration? | no |
| 4 | changes `docs/api/ENDPOINTS.md`? | **yes** — §Conventions gains the key rule, and one route's status list gains the `422` it already produces |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — `Password`, `NewPassword` and `Email` are keys on credential routes |
| 6 | lowers a severity, deletes or skips a test? | no — 21 expectations change value; none is removed or loosened |
| 7 | touches or copies from `reference/`? | no |

## Decisions this spec proposes

1. **`ValidatorOptions.Global.PropertyNameResolver`** returns the member path with **each segment**
   camelCased, so `Bio.Value` becomes `bio.value` and `NewPassword` becomes `newPassword`. It returns null for
   a rule with no member, leaving such rules to name themselves.
2. **`OverridePropertyName` is the primitive for a key**, and it goes on exactly the rules that need it: the
   nine whole-command rules in `EventLicenceRules`, `MoveTrackCommandValidator`'s "exactly one neighbour" rule
   (`afterTrackId`), the two wire-name mismatches (`q`, `ttlSeconds`) and the two count rules (`genres`,
   `artists`). `WithName` stays only where a message reads better with another word.
3. **The resolver is set in `ApplicationServiceRegistration.AddApplicationServices`**, once, because the
   composition root is at S104's cap (escalation 4).
4. **The property test walks the document.** For each route it covers: resolve the request schema — following
   `$ref` and the `oneOf: [null, …]` wrapper the generator emits for a nullable body — and assert that every
   key in `errors` resolves segment by segment to a field of that schema. For a query-string route it checks
   the `parameters` list instead. The document is found by walking up from `AppContext.BaseDirectory` to the
   repository root, so the test needs no new build item.
5. **One route per shape**, not all twenty: a JSON body with flat fields, a body with a nested object, a
   nullable body, and a query string. The twenty routes keep their own key assertions from SPEC-036; this test
   is what catches a route nobody thought to add.

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/ApplicationServiceRegistration.cs` | change — set the resolver, once |
| `MusicRoom/src/MusicRoom/Application/Validation/ValidationKeyNaming.cs` | create — the resolver itself, so the registration stays one line |
| `MusicRoom/src/MusicRoom/Application/Features/Events/EventLicenceRules.cs` | change — nine `WithName` become `OverridePropertyName`; the keys they publish do not move |
| `MusicRoom/src/MusicRoom/Application/Features/Playlists/Commands/MoveTrack/MoveTrackCommandValidator.cs` | change — the empty key becomes `afterTrackId` |
| `MusicRoom/src/MusicRoom/Application/Features/Catalog/Queries/SearchTracks/SearchTracksQueryValidator.cs` | change — `Query` keys on `q` |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/IssueEventCode/IssueEventCodeCommandValidator.cs` | change — keys on `ttlSeconds` |
| `MusicRoom/src/MusicRoom/Application/Features/Profiles/Commands/ReplaceMusicPreferences/ReplaceMusicPreferencesCommandValidator.cs` | change — the two count rules key on `genres` and `artists` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ValidationKeyContractTests.cs` | create — the property test, one route per shape |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ValidationProblems.cs` | change — the document lookup helper, beside the reader SPEC-036 added |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | change — `password` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordResetEndpointTests.cs` | change — `newPassword`, `email` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordChangeEndpointTests.cs` | change — `newPassword` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/VerificationRequestEndpointTests.cs` | change — `email` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventSuggestionEndpointTests.cs` | change — `youtubeVideoId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventPresenceEndpointTests.cs` | change — `ttlSeconds` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/DeviceControlEndpointTests.cs` | change — `granteeUserId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/FriendRequestEndpointTests.cs` | change — `targetUserId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistEndpointTests.cs` | change — `licence`, `name` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackEndpointTests.cs` | change — `youtubeVideoId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackOrderingTests.cs` | change — `afterTrackId`, and the empty-key remark goes with it |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ProfileEndpointTests.cs` | change — `genres`, `bio.value` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/TrackEndpointTests.cs` | change — `q`, `pageSize` |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/ResetPasswordCommandValidatorTests.cs` | change — any `PropertyName` expectation the resolver moves |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Auth/RegisterCommandValidatorTests.cs` | change — the same |
| `docs/api/ENDPOINTS.md` | change — §Conventions gains the key rule; `POST /auth/password-reset-requests` gains `422` |
| `brain/decisions/ADR-0043-validation-error-key-naming.md` | change — the amendment the four escalation points need. **Owner's, unless they say otherwise** |
| `brain/decisions/decisions-index.md` | change — ADR-0043's row from "not built" to built, beside the ADR itself |
| `docs/SDD.md` | change — §17: **O28 closed**; §13 or the API viewpoint gains the key convention |
| `docs/specs/SPEC-037-validation-key-naming.md` | change — status transitions |

**Seven production files, thirteen test files touched and one created, five documents.** The Application tests
are in the table because a validator test that asserts a `PropertyName` reads what the resolver now produces —
unmeasured until the resolver exists, so the table says "any", and if none needs it those two rows go unused.

### The Files table walk

**1. Assemblies.** `MusicRoom.Application` for the resolver and six validators, `MusicRoom.WebAPI.Tests` and
`MusicRoom.Application.Tests` for the expectations. **`WebAPI` is not touched** — no endpoint, no `Program.cs`,
which is what keeps O24's wall out of this unit. No `.csproj` changes.

**2. Types crossing an assembly boundary.** One: `ValidationKeyNaming`, `public static` because
`ApplicationServiceRegistration` is in the same assembly but the test project asserts the resolver's output
directly on a few paths. If that direct test proves unnecessary once the property test exists, it becomes
`internal`.

**3. Does anything invoke it?** `ValidatorOptions.Global` is process-wide state, set before the first
validation. Every test host that builds the application through `WebApplicationFactory` runs
`AddApplicationServices`, so the resolver is in place for every suite. A unit test that constructs a validator
with `new` and never calls the registration would see the **default** resolver — which is precisely why the two
Application-test rows are in the table, and the first thing to check when implementing.

**4. A CLI, a tool manifest, or an unpinned package?** None.

**5. Do the four corePackages contain what I assume?** `Core.Application` holds the validation behaviour that
throws `ValidationException`; it is not touched, because the key is decided where the rule is declared, not
where the failure is turned into a response.

**What the walk caught.** The process-wide static in point 3. `ValidatorOptions.Global` is not injected and not
scoped, so a validator exercised without `AddApplicationServices` keys differently from the same validator
behind an endpoint. The property test runs through the application, so it cannot catch that divergence — the
Application-layer validator tests are what would, and they are the reason this unit reads their expectations
rather than assuming them.

## Steps

1. `ValidationKeyNaming` with the resolver, and one line in `AddApplicationServices`.
2. Run the whole `MusicRoom.Application.Tests` suite **before touching any expectation**: whatever fails there
   is the measurement of point 3 above.
3. The six validators' explicit names, in one pass: nine `OverridePropertyName` in `EventLicenceRules`,
   `afterTrackId`, `q`, `ttlSeconds`, `genres`, `artists`.
4. The 21 WebAPI expectations. The diff is the evidence the contract moved on purpose.
5. The property test, which must **fail** if any `OverridePropertyName` from step 3 is removed. Prove that by
   removing one and seeing it fail before putting it back.
6. Documents: ENDPOINTS §Conventions and the `422` row, SDD §17's O28, the ADR amendment, the index row.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `ValidationKeyContractTests` | every key in a `422` is a field in that route's published request schema — for a flat body, a nested object, a nullable body and a query string. The test a new route cannot pass by accident |
| the 21 changed expectations | the keys moved to the wire names, and nothing else about those responses changed |
| `PlaylistTrackOrderingTests.MoveTrack_WithNoNeighbour` and `_WithBothNeighbours` | the two rules that published `""` now publish `afterTrackId` — the defect O28 named |
| `TrackEndpointTests` and `EventPresenceEndpointTests` | the two wire-name mismatches key on `q` and `ttlSeconds`, not on the camelCased property name |
| `ProfileEndpointTests` | `genres` rather than `genres.count`, and `bio.value` unchanged in shape because that path really exists |
| `MusicRoom.Application.Tests`, unchanged where possible | the resolver did not move a key that a validator test asserts for its own reasons; any row that did change is named in the commit |
| `git diff -- docs/api/openapi.json` | **empty**. The envelope is untouched, so the drift guard says nothing — the reason the property test exists |
| `make agent-check` | 589 tests plus the new one, no analyzer regression, no file over 300 lines |

## Done when

- [ ] every file in the Files table exists and compiles
- [ ] every command under Verification passes
- [ ] OWASP checklists walked for the surface touched; unfixed findings in the threat register
- [ ] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [ ] `status: done` in this file
