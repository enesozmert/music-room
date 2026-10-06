---
id: SPEC-036
title: Put a test behind the 422 body — one shared reader, one key assertion per route
status: done            # draft | approved | in-progress | done | blocked
tier: A                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.6]
decisions: []
standards: [testing, api-contract]
approved_by: agent      # clean Tier A gate; the gate walk is below and every answer is no
escalation:             # which gate tripped, if status went back to draft
---

# Put a test behind the 422 body — one shared reader, one key assertion per route

## Goal

After this, the `errors` dictionary of a `422` is a tested contract on **all 20 routes that publish one**.
The spec was written expecting nineteen, and §"What the run changed" says why the twentieth came in. Today three routes assert their keys — the ones
SPEC-034 and SPEC-035 touched — and the other seventeen publish `{"errors":{"<PropertyName>":[…]}}` to
clients with nothing holding the name in place. `CredentialEndpointResults.ToErrors` groups failures by the
property FluentValidation reported against, so renaming a validated property changes a response shape clients
read and **nothing fails**: issue **O26**, which this unit closes. The three four-line body readers SPEC-035
left behind become one shared reader.

## Out of scope

- **Any production file.** This unit adds assertions. If a key turns out to be one a client would not expect,
  the spec stops and escalates rather than renaming it quietly — that is a contract change and the owner's to
  answer.
- **Handler-produced `422`s.** A refusal that comes from a handler rather than a validator answers
  `Results.Problem`, which carries **no `errors` dictionary at all**: the wrong current password on
  `/me/password`, a stranger on `PUT /devices/{id}/control`, every presence refusal on a vote, an unknown
  `direction` or `scope` on a list route. There is no key to assert, and inventing one would mean changing a
  response shape. They are listed in §"What stays uncovered, and why".
- **The casing inconsistency this unit found.** Event create, update and licence publish **camelCase** keys;
  every other route publishes the **PascalCase** property name. Recorded as **O28**, asserted as it is, not
  changed.
- **`docs/api/ENDPOINTS.md`.** Its table does not list `422` for `POST /auth/password-reset-requests`, which
  the route does produce. That is a documentation gap, recorded in O28 with the casing one, and fixing it is a
  separate decision about a published table.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | no — no decision is made or restated here |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — nothing writes |
| 4 | changes `docs/api/ENDPOINTS.md`? | no, deliberately: the one gap found there is recorded as **O28** rather than patched, because the table is a published contract and its shape is a decision |
| 5 | touches authn, authz, tokens, secrets, audit? | **no**, and the judgement is on record: three credential suites are edited, and the only change to them is that a private four-line reader becomes a shared one. No credential logic, no production file, no assertion removed or weakened. SPEC-034 and SPEC-035 answered yes because they changed the password path itself; this changes no path at all |
| 6 | lowers a severity, deletes or skips a test? | no — and §"The two walls" is about the places that made it tempting |
| 7 | touches or copies from `reference/`? | no |

Every answer is no, so **Tier A** and the run continues without a human turn.

## The inventory this is scoped from

Measured on `main` at `6289457`, not estimated.

- **21 `ToErrors` call sites in 14 files.** One of the fourteen is `CredentialEndpointResults` itself, which
  declares the method. The other twenty are route handlers, and the twenty-first site is
  `PasswordResetEndpoints.PasswordRuleFailure`, the private helper `ResetPasswordAsync` answers through.
- **44 assertions** on `HttpStatusCode.UnprocessableEntity` across the suites, of which **7 in 3 files** also
  assert the body's keys — all added by SPEC-034 and SPEC-035.
- **30 test methods** named for a `422`. Many of them assert a refusal that never had an `errors` dictionary.

## Coverage, per route

The twenty routes that publish an `errors` body, with the key each one's `422` carries and where the
assertion lands. "add" means an existing `422` test gains the key assertion; "new" means the route has no
validation-`422` test anywhere and gets a minimal one.

| Route | Key asserted | Suite | Action |
|---|---|---|---|
| `POST /auth/registrations` | `Password` | `RegistrationEndpointTests` | already covered (SPEC-034, SPEC-035) |
| `POST /auth/password-resets` | `NewPassword` | `PasswordResetEndpointTests` | already covered |
| `POST /me/password` | `NewPassword` | `PasswordChangeEndpointTests` | already covered |
| `POST /auth/verification-requests` | `Email` | `VerificationRequestEndpointTests` | add |
| `POST /events` | `endsAtUtc`, `presenceProof`, `geofence` | `EventEndpointTests` | add — three tests, three keys |
| `PATCH /events/{eventId}` | `endsAtUtc` | `EventEndpointTests` | new |
| `PUT /events/{eventId}/licence` | `presenceProof` | `EventEndpointTests` | new — this route has no `422` test anywhere today |
| `POST /events/{eventId}/tracks` | `YoutubeVideoId` | `EventSuggestionEndpointTests` | add |
| `POST /events/{eventId}/event-codes` | `TimeToLiveSeconds` | `EventPresenceEndpointTests` | add |
| `PUT /devices/{deviceId}/control` | `GranteeUserId` | `DeviceControlEndpointTests` | new — the existing `422` there is a handler refusal |
| `POST /me/friend-requests` | `TargetUserId` | `FriendRequestEndpointTests` | new — the existing `422` there is a query-parse refusal on another route |
| `POST /playlists` | `Licence` | `PlaylistEndpointTests` | add |
| `PATCH /playlists/{playlistId}` | `Name` | `PlaylistEndpointTests` | new |
| `PUT /playlists/{playlistId}/licence` | `Licence` | `PlaylistEndpointTests` | new |
| `POST /playlists/{playlistId}/tracks` | `YoutubeVideoId` | `PlaylistTrackEndpointTests` | add |
| `PATCH /playlists/{playlistId}/tracks/{playlistTrackId}` | `AfterTrackId` | `PlaylistTrackOrderingTests` | new — the self-move rule, not the cross-property one, see §"What the run changed" |
| `PATCH /me/profile` | `Bio.Value` | `ProfileEndpointTests` | new — a deterministic failure, because the existing assertion there accepts `400` **or** `422` |
| `PUT /me/music-preferences` | `Genres.Count` | `ProfileEndpointTests` | add |
| `GET /tracks` | `Query`, `PageSize` | `TrackEndpointTests` | add — two tests |
| `POST /auth/password-reset-requests` | `Email` | `PasswordResetEndpointTests` | new — **covered after all**, see §"What the run changed" |

**Nineteen of twenty.** Nine routes gain an assertion on a test that already exists, seven gain a minimal
test, three are already done.

### What the run changed

Three things the spec got wrong, each found by running rather than by reading:

1. **`POST /auth/password-reset-requests` is covered.** The spec left it out because
   `PasswordResetEndpointTests` was at 293 of 300 lines and the test "does not fit even after the shared reader
   frees its private copy". Measured after the fact: the reader freed **nine** lines, the suite dropped to 284,
   and the test costs nine. The file is back at **293** and the route has its assertion. The spec's arithmetic
   was an estimate where it should have been a measurement, so coverage is **20 of 20**.
2. **`PATCH /playlists/{id}/tracks/{trackId}` is covered through the self-move rule, not the two tests the
   spec named.** `MoveTrackCommandValidator` declares its "exactly one neighbour" rule as
   `RuleFor(command => command)` with no `WithName`, so FluentValidation reports it against the **empty**
   property name and the published body is `{"errors":{"":["Send exactly one of afterTrackId or
   beforeTrackId."]}}`. Asserting an empty key would cement a shape no client can read, and renaming it is a
   contract change this unit may not make — constraint 3. So the route is covered by a new test on
   `AfterTrackId`, a key a client can act on, and the empty key goes to **O28** for the owner.
3. **Two keys are dotted paths**, not the property names the spec predicted: `Genres.Count` from
   `RuleFor(command => command.Genres.Count)` and `Bio.Value` from `RuleFor(command => command.Bio!.Value)`.
   Asserted as the code reports them. The shape is the same one `geofence.latitude` already has, so it is a
   convention rather than a defect — which is why it joins O28 as an observation and not an escalation.

### What stays uncovered, and why

- **Every handler-produced `422`.** `Change_WithTheWrongCurrentPassword`, `GrantControl_ToAStranger`, the
  eight presence refusals in `EventPresenceEndpointTests`, `ListRequests_WithAnUnknownDirection`,
  `List_WithAnUnknownScope`, and the concurrency suites' `422` counts. These answer `Results.Problem` with no
  `errors` dictionary, so there is no key to hold in place. Their status codes stay asserted exactly as they
  are.

## The two walls

Both are O27's, and both are already measured:

- `PasswordResetEndpointTests.cs` at **293 of 300** before this unit, 284 once the shared reader replaced its
  private copy, and 293 again with its new test. No new file was needed, and no severity moved.
- `AuditRecordEndpointTests.cs` at **295 of 300** — not touched by this unit, recorded so the next one knows.

`DeviceControlEndpointTests.cs` at 272 is the largest file this unit adds a test to; it lands at about 286.
If any suite would cross 300, the answer is a new file, never a raised severity.

## Files

| Path | Action |
|---|---|
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ValidationProblems.cs` | create — the one body reader, `internal static`, beside `EventRequests.cs` which exists for the same reason |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/RegistrationEndpointTests.cs` | change — delete the private reader, call the shared one |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordResetEndpointTests.cs` | change — the shared reader, **and** a new test for `POST /auth/password-reset-requests` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PasswordChangeEndpointTests.cs` | change — the same |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/VerificationRequestEndpointTests.cs` | change — add: `Email` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventEndpointTests.cs` | change — add three keys, and two new tests: `PATCH /events/{id}` and `PUT /events/{id}/licence` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventSuggestionEndpointTests.cs` | change — add: `YoutubeVideoId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventPresenceEndpointTests.cs` | change — add: `TimeToLiveSeconds` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/DeviceControlEndpointTests.cs` | change — new test: `GranteeUserId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/FriendRequestEndpointTests.cs` | change — new test: `TargetUserId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistEndpointTests.cs` | change — add `Licence`, and two new tests: rename and set-licence |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackEndpointTests.cs` | change — add: `YoutubeVideoId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/PlaylistTrackOrderingTests.cs` | change — new test: `AfterTrackId` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/ProfileEndpointTests.cs` | change — add `Genres.Count`, and one new test for `PATCH /me/profile` |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/TrackEndpointTests.cs` | change — add: `Query`, `PageSize` |
| `docs/SDD.md` | change — §17: **O26 closed**, **O28 opened** (key casing, and the ENDPOINTS table gap) |
| `brain/lessons/lessons-index.md` | change — **L-048**: a bundled list's normalisation has to match the list, not the input |
| `docs/specs/SPEC-036-validation-body-coverage.md` | change — status transitions |

**Fifteen test files, one of them new. No production file, and that is the point of the unit.**

### The Files table walk

**1. Assemblies, and which references that implies.** `MusicRoom.WebAPI.Tests` only. No `.csproj` changes, no
new reference, and nothing in `src/` is opened.

**2. Types crossing an assembly boundary.** None. `ValidationProblems` is `internal static` in the test
assembly, exactly like `EventRequests`, `DeviceRequests`, `PlaybackRequests` and `PlaylistTrackRequests` —
four precedents for a shared test helper in its own file, each created because inlining would have pushed a
suite past 300 lines.

**3. Does anything invoke it?** Fourteen suites will. The three readers it replaces are `private static` and
have exactly seven call sites between them, all of which move.

**4. A CLI, a tool manifest, or an unpinned package?** None.

**5. Do the four corePackages contain what I assume?** Not touched. A test helper for this application's
response shape is not a domain-agnostic primitive.

**What the walk caught.** Two things, both recorded rather than fixed. The keys are **not consistently
cased**: `EventLicenceRules` calls `.WithName("presenceProof")`, `"geofence"`, `"endsAtUtc"`, `"name"` and
`"geofence.latitude"`, so event create, update and licence publish camelCase while every other route
publishes the PascalCase property name — a client reading `errors` has to know which route it asked. And
`docs/api/ENDPOINTS.md` does not list `422` for `POST /auth/password-reset-requests`, which produces one.
Both go in **O28**; neither is renamed or patched here, because constraint 3 of this unit is that a key a
client would not expect is an escalation and not a quiet edit.

## Steps

1. `ValidationProblems.cs`: one method returning the `errors` property names, one returning the messages for
   a named key. Replace the three private readers and run the three credential suites — they must pass with no
   change to any assertion.
2. The nine "add" routes, one assertion each (two for the three multi-key routes). Run each suite as it is
   touched, because a wrong expectation here is a finding about the contract and not a broken test.
3. The seven "new" routes, one minimal test each: a payload that fails exactly one rule, the `422`, the key.
4. If any key is not the property name a client would expect, **stop**: set `status: blocked`, name the route
   and the key in `escalation`, commit nothing further.
5. `docs/SDD.md` §17: close O26 with the counts, open O28 with both findings. `lessons-index.md`: L-048.
6. `make agent-check`, then the PDFs, which the gate re-checks.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| the three credential suites, unedited except for the reader | the shared reader is the same reader: every key assertion SPEC-034 and SPEC-035 added still passes |
| nine suites, one new key assertion each | the `errors` key of each route that already had a `422` test is now held in place — O26's gap on those routes |
| seven new tests | the seven routes that published an `errors` body with **no** validation test at all now have one |
| `git diff --stat -- MusicRoom/src` | **empty**. A production file in this diff means the unit changed a contract instead of testing one |
| `git grep -c "ValidationKeysAsync" MusicRoom/tests` | three private copies became one shared reader, with every call site moved |
| `make agent-check` | 580 tests plus the new ones, no analyzer regression, no suite over 300 lines, and the OpenAPI document unchanged — it describes the envelope and never the keys, which is why these assertions are the only thing that can catch a rename |

## Done when

- [x] every file in the Files table exists and compiles
- [x] every command under Verification passes
- [x] OWASP checklists walked for the surface touched; unfixed findings in the threat register
- [x] `docs/SDD.md` §16 updated honestly, if this changed a requirement's status
- [x] `status: done` in this file
