---
id: SPEC-018
title: Suggestions, votes, the vote-ordered queue, event codes and the presence checks
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S6              # which DELIVERY-PLAN slice this belongs to
requirements: [V.2.1, V.6]
decisions: [ADR-0012, ADR-0014, ADR-0024, ADR-0028, ADR-0031, ADR-0033]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29 with the seven decisions
  below. Gate 1 is answered by ADR-0014, already accepted: this spec implements the geofence and the event
  code it decided, and decides nothing new about either. The load-bearing decisions are 2 — no vote counter
  anywhere, the count is derived — and 5, that a coordinate is used for one comparison and never written.
---

# The contended write path: suggestions, votes, the queue, and proving you are there

## Goal

After this spec subject V.2.1's **contended path** exists and is proven under concurrency. Anybody the
licence admits may suggest a track and vote for one; the queue is ordered by vote count; and on a
`LocationAndTimeRestricted` event a vote must carry the presence ADR-0014 requires — coordinates inside the
geofence, a code from the venue, or both. Six more rows of `docs/api/ENDPOINTS.md` §4.

Second unit of slice S6. SPEC-017 built the aggregate and its gates; this is the write those gates exist for,
and the one subject V.2.1 grades for race conditions.

## Out of scope

- **`PATCH /events/{eventId}/now-playing`.** It needs the `PlaybackSession` of ADR-0017, which is slice S7.
  The queue this spec builds is what that pointer will walk.
- **`EventHub`.** Third unit. The broadcast machinery already exists and is proven — ADR-0035's queue and
  behaviour, built in SPEC-016 — so the hub is a small unit rather than a new mechanism.
- **Resolving a video id to a title or duration.** Slice S4's catalog, blocked on a YouTube key. A suggestion
  stores the id the client sent.
- **iBeacon.** Subject VI.2's bonus, rejected in ADR-0014: hardware and mobile work, graded only if the
  mandatory part is flawless.

## Decisions taken under the blanket approval, 2026-10-01

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A vote is `PUT` and idempotent**: `201` the first time, `200` after, and a concurrent duplicate is also `200`. The arbiter is `ux_event_votes_event_id_suggestion_id_user_id`. | The contract's own rule. A double submit from a flaky mobile network must be harmless, and `POST` would create a second row and make the race worse. The duplicate is detected **by the database**, never by a read-then-write check |
| 2 | **There is no vote counter.** The queue counts the vote rows in SQL, every read. | A counter is a second copy of a fact two writers can disagree about, and incrementing one is the lost-update bug subject V.2.1 asks about. Rejected: a `vote_count` column with a trigger (a second place to be wrong, and invisible to the test that matters), and recomputing into a column after each write (the same bug with extra steps). The cost is a `GROUP BY` on a table indexed for exactly it — SDD §12.2's "recomputed from the source rows inside the transaction" |
| 3 | **A suggestion is unique per event and video**, `409` otherwise. | A second row for the same video would split its votes between two queue entries, which is worse than refusing. This is the opposite of a playlist, where a duplicate track is legitimate (SPEC-015 decision 5): there, two entries play twice; here, two entries fragment a ranking |
| 4 | **An event code is credential-shaped and is stored only as a SHA-256 digest**, with a TTL between 60 and 3600 seconds, single-use **per account**, and the generation route is rate limited per account. | The same treatment refresh tokens and reset tokens get, for the same reason: a stolen code votes. Single-use per account rather than globally, because one code is shown to a whole venue and must work for everybody in it exactly once. Rejected: a plaintext code column, and a global single use (the first person through the door would consume it) |
| 5 | **A submitted coordinate is compared and discarded.** It is never written to any row and never logged. | ADR-0014 and `application-security` data minimisation. Storing every voter's position would be a privacy liability with no requirement behind it, and a log line is a row with worse retention. The check is a great-circle distance against the event's own centre, in server code, with server time |
| 6 | **Presence is required only for the licence that says so, and is checked at the moment of the write.** A missing or wrong proof is `422`; a code that is expired, unknown or already spent by this account is `422` with the same message. | The server rules on evidence (ADR-0014). One message for the three code failures: telling a caller *which* way their code failed is telling them whether a code exists, which is the enumeration answer the credential endpoints refuse to give |
| 7 | **Removing a suggestion is the owner's or the suggester's**, and it takes its votes with it. | The contract's `owner or suggester`. Cascading the votes is the only honest reading: a vote is a vote *for that suggestion*, and keeping orphaned rows would leave a count that counts nothing |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes, and already recorded** — ADR-0014 decided the geofence and the event code; this spec implements both. ADR-0024's expiry discipline and ADR-0031's limiter posture are followed, not changed |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — four new tables, additive, no backfill |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — six existing rows. Design notes gain the `422` shape of a failed proof and the "no counter" rule |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — a new credential-shaped token (the event code), the `licence` gate's enforcement, a rate limit, and location data about real people |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/EventSuggestion.cs` | create |
| `MusicRoom/src/MusicRoom/Domain/Entities/EventVote.cs` | create |
| `MusicRoom/src/MusicRoom/Domain/Entities/EventCode.cs` | create — digest, expiry, who issued it |
| `MusicRoom/src/MusicRoom/Domain/Entities/EventCodeRedemption.cs` | create — one per code per account |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IEventQueueRepository.cs` | create — suggestions, votes, the queue read |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IEventCodeRepository.cs` | create — issue, and spend once per account |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Presence/Geodesy.cs` | create — the great-circle distance, a pure function |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Presence/PresenceVerifier.cs` | create — rules on the evidence |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Presence/EventCodeGenerator.cs` | create — the unambiguous alphabet |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/SuggestTrack/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/RemoveSuggestion/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/CastVote/*` | create — the contended write |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/WithdrawVote/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Commands/IssueEventCode/*` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Events/Queries/GetQueue/*` | create — vote-ordered, paged |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IEventRepository.cs`, `EventContracts.cs` | change — `FindPresenceAsync` and `EventPresence` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/EventQueueConfiguration.cs` | create — suggestions and votes |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/EventCodeConfiguration.cs` | create — codes and redemptions |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/EventQueueRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/EventCodeRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — four `DbSet`s |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_EventQueue.cs` + `.Designer.cs` + snapshot | create/change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventQueueEndpoints.cs` | create — suggest, remove, the queue |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventVoteEndpoints.cs` | create — the two vote routes and the code route |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/EventChildRoutes.cs` | change — map them |
| `MusicRoom/src/MusicRoom/WebAPI/RateLimiting/EventRateLimiterPolicies.cs` | create — per account, for the vote and the code |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — register them |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Events/GeodesyTests.cs` | create — the distance, including the antimeridian |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Events/QueueCommandValidatorTests.cs` | create — the video id, the TTL bounds and the generator |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventQueueRequests.cs` | create — the requests the four suites share |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventSuggestionEndpointTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventVoteEndpointTests.cs` | create — the licence gate and idempotence |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventPresenceEndpointTests.cs` | create — the geofence and the code |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventVoteConcurrencyTests.cs` | create — **the parallel vote** |
| `docs/api/ENDPOINTS.md`, `docs/api/openapi.json`, `docs/SDD.md`, PDFs | change |
| `docs/owasp/THREAT-REGISTER.md` | change — T-17 gains its evidence and its residual risk is restated |
| `brain/migrations/migrations-index.md` | change |
| `brain/projects/musicroom-backend.md` | change |
| `docs/specs/SPEC-018-suggestions-votes-and-presence.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Entities in `Domain`; the two abstractions, the presence verifier and the features in
`Application`; configurations and repositories in `Persistence`; endpoints and the limiter in `WebAPI`. The
great-circle distance is a pure function and lives beside the verifier that uses it.

**2. Boundary types.** The entities, both repository abstractions, the commands, the query, the proof record
and the verifier are `public`; handlers, configurations and repositories `internal sealed`.

**3. Invoked?** Six routes, two repositories, one verifier.

**4. CLI or package.** `dotnet ef migrations add` only.

**5. corePackages.** Unchanged. A great-circle distance is domain-agnostic and still stays out: one caller
does not justify a shared helper, and `Core.*` holds a `.csproj` and nothing else.

**What the walk caught.** Four things.
- **The count must not be stored.** SDD §12.2 says the count is recomputed from the source rows inside the
  transaction; a column would be a second copy of the fact, and the parallel test would pass while the
  production path lost updates. This is decision 2, and it shapes the queue query.
- **The vote needs the `licence` gate *and* the presence check**, and `EventAccess.MayVote` deliberately
  answers only the first half (SPEC-017's walk said so). The verifier is the second half, and the handler is
  the only place that has both.
- **A code is a credential.** It therefore gets the treatment `authentication-authorization` already
  prescribes: generated from `RandomNumberGenerator`, stored as a digest, bounded TTL, single use, rate
  limited, and never logged.
- **`EventChildRoutes` already exists** from SPEC-017, so the two new endpoint classes cost `EventEndpoints`
  nothing against S1200.

## Steps

1. The four entities and their configurations: `ux_event_suggestions_event_id_youtube_video_id`,
   `ux_event_votes_event_id_suggestion_id_user_id`, `ux_event_codes_code_hash`,
   `ux_event_code_redemptions_event_code_id_user_id`, and one additive migration.
2. `IEventQueueRepository`: suggest (`409` from the index), remove (owner or suggester, votes cascade),
   `TryCastVoteAsync` (insert, duplicate is success), withdraw, and the queue read that counts votes in SQL.
3. `IEventCodeRepository`: issue a digest with an expiry, and `TrySpendAsync` — valid, unexpired, not yet
   spent by this account — arbitrated by the redemption table's unique index rather than by a read.
4. `Geodesy.DistanceInMetres`: the great-circle distance, correct across the antimeridian and at the poles.
5. `PresenceVerifier`: given an event's policy and a submitted proof, answer whether the caller is present.
   Coordinates are compared and dropped; the code is spent; nothing is logged.
6. The vote handler: the `licence` gate from SPEC-017, then the verifier, then the guarded insert.
7. Endpoints, with a per-account rate limit on the vote and on code issuance, and `422` for a failed proof.
8. Tests, including **twenty parallel votes from twenty accounts on one suggestion** and **twenty parallel
   votes from one account**, then the documents.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `Suggest_ThenReadQueue_ReturnsIt` | the suggestion round-trips |
| `Suggest_TheSameVideoTwice_Returns409` | decision 3, arbitrated by the index |
| `Suggest_WithAMalformedVideoId_Returns422` | the id is validated, not trusted |
| `Suggest_WithoutTheLicence_Returns404` | the `licence` gate from SPEC-017 holds on a new surface |
| `RemoveSuggestion_AsTheSuggester_Succeeds` · `AsTheOwner_Succeeds` · `AsAnotherVoter_Returns404` | decision 7, all three cases |
| `RemoveSuggestion_TakesItsVotesWithIt` | no orphaned votes, no count that counts nothing |
| `Queue_IsOrderedByVoteCount` | the ranking, derived and not stored |
| `Queue_ReportsWhetherTheCallerVoted` | the read a client needs to render a toggle |
| `Queue_AsAStranger_Returns404` | the `member` gate on the queue |
| `Vote_FirstTime_Returns201_ThenAgainReturns200` | decision 1, idempotence |
| `Vote_ThenWithdraw_RemovesItFromTheCount` · `Withdraw_WithoutVoting_Returns404` | the withdraw path |
| `Vote_IsAttributedToTheTokenAndNotTheRequest` | the voter is the token |
| `Vote_ForASuggestionInAnotherEvent_Returns404` | a suggestion belongs to one event |
| `Vote_WithoutTheLicence_Returns404` | the gate, on the vote |
| `Vote_WritesAnAuditRecord` | ADR-0033 on the contended path |
| `Vote_OnARestrictedEventWithNoProof_Returns422` | decision 6 |
| `Vote_OutsideTheGeofence_Returns422` · `Vote_InsideTheGeofence_Succeeds` | ADR-0014's geofence, both directions |
| `Vote_OutsideTheWindow_Returns422` | server time, not the client's |
| `Vote_WithAValidEventCode_Succeeds` · `WithAnUnknownCode` · `WithAnExpiredCode` · `ReusingACodeFromTheSameAccount` | ADR-0014's code, and the TTL and single use that make it evidence |
| `Vote_WithTheSameCodeFromAnotherAccount_Succeeds` | one code, a whole venue |
| `Vote_WithBothRequired_AndOnlyACode_Returns422` | `Both` means both |
| `IssueEventCode_AsANonOwner_Returns404` · `WithATooLongTtl_Returns422` | codes are the owner's, and bounded |
| `Vote_TwentyAccountsOnOneSuggestion_CountsTwenty` | **the lost-update bug subject V.2.1 grades, absent because nothing increments** |
| `Vote_TwentyParallelVotesFromOneAccount_WriteOneRow` | one `201`, nineteen `200`, one row |
| `Suggest_TwentyParallelSuggestionsOfOneVideo_AddOne` | one suggestion, nineteen `409`s |
| `Vote_TwentyParallelVotesWithOneCode_SpendItOnce` | single use per account, under concurrency |
| `GeodesyTests` (7 cases) | the distance: the same point, a degree of latitude, the antimeridian, a pole, and the radius boundary |
| `QueueCommandValidatorTests` (10 cases) | the video id, the TTL bounds, and that two generated codes differ |
| `SchemaConventionTests` (existing) | the four new tables obey the naming rules |

## Discovered during implementation

- **A refusal must not look like a conflict.** The first draft of the suggestion path reused the duplicate
  outcome for "the licence does not admit you", which would have answered `409` where the contract says `404` —
  and `409` on a private event is an existence oracle. `SuggestionWriteOutcome` gained `NotPermitted`.
- **The gate and the proof are two reads, deliberately.** `FindAccessAsync` answers what the row decides and
  `FindPresenceAsync` answers what the client must show. Merging them would have put a geofence into the answer
  of every `member` check, including the ones that have no business seeing it.
- **`MayVote` only checks the window for the restricted licence.** An `Everyone` event is votable before it
  starts, which is correct: a queue is built in advance and the window is the *licence's* constraint, not the
  event's own opening hours. The window test therefore uses a restricted event.
- **The generated code's alphabet is public** so the test can assert it. A code is transcribed by a human in a
  dark room, and `I`, `L`, `O`, `U`, `0` and `1` are left out on purpose.
- **Two analyzer-driven splits in the migration.** `CreateIndexes` went over the 40-line method gate and became
  one method per concern, which reads better anyway: the queue's indexes carry the vote guarantees and the
  code's carry the single use.

## Done when

- [x] every file in the Files table exists and compiles
- [x] `make agent-check` passes
- [x] `docs/SDD.md` §16's V.2.1 queue, licence and race rows say what is proven rather than designed
- [x] `docs/api/ENDPOINTS.md` §4 records the `422` shape and the derived count
- [x] `docs/owasp/THREAT-REGISTER.md` T-17 carries its evidence and its restated residual risk
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] `status: done` in this file
