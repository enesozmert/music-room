---
id: SPEC-012
title: The friends graph — requests, acceptance, and the symmetric friendship the Friends tier needs
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S3              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1]
decisions: [ADR-0011, ADR-0012, ADR-0028, ADR-0033, ADR-0034]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, database-design-migrations, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1, 3 and 5 answer "yes", so this is Tier B. It ships under the owner's blanket approval of
  2026-09-29, with the four decisions below taken as the recommendations this spec would otherwise have
  escalated. They are recorded here in full, and the one that is ADR-shaped — the shape of a friendship
  row — is recorded in ADR-0034's successor if it is ever revisited.
---

# The friends graph

## Goal

After this spec, `Friends` stops being a tier that means nothing. A user can ask another to be friends,
accept, decline, cancel, list requests in either direction, list friends, and remove one — the six rows
`docs/api/ENDPOINTS.md` §2 already specifies. A friendship is **symmetric**: if A is a friend of B then B
is a friend of A, with no way for the two answers to disagree.

It is the second unit of slice S3 and the prerequisite for the per-field projection of
`GET /users/{userId}`, which is the third.

## Out of scope

- **Reading another user's profile.** The next unit. This spec builds the relationship that projection
  consults, and deliberately not the projection.
- **Blocking a user.** No contract row, and it is a different relationship with its own semantics — a
  block must survive a friendship being removed, which a friendship row cannot express.
- **Friend suggestions, mutual-friend counts, or any graph traversal beyond one hop.** Nothing in the
  subject needs them.
- **Notifying anybody.** A friend request reaching a mailbox or a push notification is O17's problem and
  a decision nobody has taken.

## Decisions taken under the blanket approval, 2026-09-29

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **One row per friendship, with a canonical ordered pair.** `friendships(user_a_id, user_b_id)` where `user_a_id < user_b_id`, unique on the pair. | Symmetry becomes a property of the data rather than of two rows a bug could desynchronise. Rejected: two mirrored rows per friendship — easier list queries, and one deleted row away from A being B's friend while B is not A's |
| 2 | **A pending request in either direction blocks a new one**, answered `409`. | Two people asking each other at once is the case that produces a duplicate friendship, and the database is where it must be refused. Rejected: auto-accepting a crossing request, which is friendly and surprising — a user would become a friend of somebody without accepting anything |
| 3 | **Acceptance is arbitrated by a conditional `UPDATE`** on `status = 'pending'`, and the friendship insert shares its transaction. | The same arbitration every token spend in this codebase uses, and ADR-0033 already gives the transaction. Twenty parallel accepts of one request must create exactly one friendship |
| 4 | **Removing a friend deletes the row and leaves no request behind**, so the pair can befriend again later. | A tombstone would need a reason to exist; nothing reads one. Rejected: keeping a `removed` status, which makes "can these two be friends again" a query instead of a fact |

`404`, never `403`, for a request or a friendship that is not the caller's — `docs/api/ENDPOINTS.md` says
a resource the caller may not see returns `404`, and a distinct answer would confirm it exists.

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — decision 1 is a storage shape later reads inherit, of the same kind ADR-0034 records for profiles. Under the blanket approval it ships recorded here; if the shape is ever revisited it needs an ADR that supersedes ADR-0034's neighbours |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — two new tables, defined here, additive, no backfill: nobody has friends yet |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — all six rows exist with their status codes. A design note records what `409` means on each route |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — every route is `self` or `auth`, and the graph it writes is what the `Friends` tier will trust |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

### Discovered during implementation, 2026-09-28

One delta, analyzer-forced: `FriendEndpointTests.cs` reached
`error S104: This file has 336 lines, which is greater than 300 authorized`, so the suite is two files —
`FriendRequestEndpointTests` (sending, declining, withdrawing, listing) and `FriendshipEndpointTests`
(acceptance, symmetry, removal, the parallel accept). Each owns its container, as every other endpoint suite
here does.

One implementation note: `TrySendRequestAsync` deletes any **answered** request row for the same direction
before inserting, because the unique index would otherwise let a declined request block the pair forever. The
checks above it have already established that the pair may try again.

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Domain/Entities/FriendRequest.cs` | create — `SenderId`, `RecipientId`, `Status`, plus the `FriendRequestStatus` enum |
| `MusicRoom/src/MusicRoom/Domain/Entities/Friendship.cs` | create — the canonical ordered pair |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IFriendshipRepository.cs` | create — six members, one per contract row, plus the outcome types beside them |
| `MusicRoom/src/MusicRoom/Application/Features/Friends/Commands/SendFriendRequest/*.cs` | create — command, handler, validator, outcome |
| `MusicRoom/src/MusicRoom/Application/Features/Friends/Commands/RespondToFriendRequest/*.cs` | create — accept or decline, one command |
| `MusicRoom/src/MusicRoom/Application/Features/Friends/Commands/CancelFriendRequest/*.cs` | create — the sender withdraws |
| `MusicRoom/src/MusicRoom/Application/Features/Friends/Commands/RemoveFriend/*.cs` | create |
| `MusicRoom/src/MusicRoom/Application/Features/Friends/Queries/ListFriends/*.cs` | create — query, handler, `FriendSummary` |
| `MusicRoom/src/MusicRoom/Application/Features/Friends/Queries/ListFriendRequests/*.cs` | create — query with a direction, handler, `FriendRequestSummary` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/FriendRequestConfiguration.cs` | create — `ux_friend_requests_sender_id_recipient_id`, status as `smallint` |
| `MusicRoom/src/MusicRoom/Persistence/EntityConfigurations/FriendshipConfiguration.cs` | create — `ux_friendships_user_a_id_user_b_id` |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/FriendshipRepository.cs` | create |
| `MusicRoom/src/MusicRoom/Persistence/Contexts/MusicRoomDbContext.cs` | change — two `DbSet`s |
| `MusicRoom/src/MusicRoom/Persistence/PersistenceServiceRegistration.cs` | change — register the repository |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/<timestamp>_FriendsGraph.cs` + `.Designer.cs` | create — reformatted by hand, as the seven before it were |
| `MusicRoom/src/MusicRoom/Persistence/Migrations/MusicRoomDbContextModelSnapshot.cs` | change |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/FriendEndpoints.cs` | create — the six routes, `RequireAuthorization`, request and response records |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the group |
| `MusicRoom/tests/MusicRoom.Application.Tests/Features/Friends/SendFriendRequestCommandValidatorTests.cs` | create |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/FriendEndpointTests.cs` | create |
| `docs/api/ENDPOINTS.md` | change — design notes on `409` and on symmetry |
| `docs/api/openapi.json` | change — regenerated |
| `docs/SDD.md` | change — §8 gains both tables; §16's tier row says `Friends` is now evaluable |
| `brain/migrations/migrations-index.md` | change — the new row |
| `docs/SDD.pdf`, `docs/api/ENDPOINTS.pdf` | change — regenerated |
| `docs/specs/SPEC-012-friends-graph.md` | change — status transitions |

### The Files table walk

**1. Assembly membership.** Entities in `Domain`; the abstraction, commands, queries and handlers in
`Application`; configurations and repository in `Persistence`; endpoints in `WebAPI`. **No new project,
no `.csproj` change**, every reference inward.

**2. Types crossing a boundary.** The two entities, both enums, `IFriendshipRepository`, the four
commands, two queries and the two summaries are `public`. Handlers stay `internal sealed`.

**3. Does anything invoke it?** Six routes, six repository members, one consumer each.

**4. CLI or unpinned package.** `dotnet ef migrations add` only.

**5. corePackages.** Unchanged and still near-empty: `Core.Application` holds `ValidationBehaviour`, the
other three hold a `.csproj`. A friendship is not domain-agnostic.

**What the walk caught.** `Program.cs` maps a third endpoint group, and CA1506 caps `Main` at 41 coupled
types — the group is mapped through an extension method so the coupling lands in the endpoint file.
`FriendEndpoints.cs` will hold six routes and their records, so it starts close to the 300-line limit:
the response records go in the same file only while it fits, and the file is split before a route is
added to it, not after `S104` says so.

## Steps

1. `FriendRequestStatus` (`Pending`, `Accepted`, `Declined`), `FriendRequest`, `Friendship` with the
   canonical pair, then both configurations and the migration.
2. `IFriendshipRepository`: send, respond, cancel, remove, list friends, list requests. Each takes the
   caller's id from the handler, never from the request.
3. Sending: refuse self-friendship, an existing friendship, and a pending request **in either
   direction** — all `409` except an unknown target, which is `404`.
4. Responding: one conditional `UPDATE` on `status = 'pending' AND recipient_id = @caller`; on accept,
   insert the friendship in the same transaction, with the pair ordered.
5. Cancelling: the same `UPDATE` keyed on `sender_id = @caller`, so a recipient cannot cancel.
6. Removing: delete the row for the ordered pair where the caller is either side.
7. Listing: friends as the other side of each row, requests filtered by direction, both paged with a
   capped page size — an unbounded list is OWASP API4.
8. Endpoints, tests, then regenerate `docs/api/openapi.json`.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `SendRequest_ToAnotherUser_Returns201` | the flow starts |
| `SendRequest_ToSelf_Returns409` | a user cannot befriend themselves |
| `SendRequest_Twice_Returns409` | the unique index holds |
| `SendRequest_WhenTheOtherDirectionIsPending_Returns409` | decision 2 |
| `SendRequest_ToAnUnknownUser_Returns404` | an unknown target is not a conflict |
| `Accept_MakesTheFriendshipVisibleToBothSides` | symmetry, from both callers' lists |
| `Accept_TwentyParallelAccepts_ProduceExactlyOneFriendship` | AGENTS.md's parallel rule, asserting final database state |
| `Accept_ARequestThatIsNotMine_Returns404` | `404`, not `403` |
| `Decline_LeavesNoFriendship` | declining is not accepting |
| `Cancel_BySender_Returns204` and `Cancel_ByRecipient_Returns404` | only the sender withdraws |
| `RemoveFriend_RemovesItForBothSides` | symmetry again, and decision 4 |
| `RemoveFriend_ThenSendAgain_Returns201` | no tombstone blocks a second try |
| `ListRequests_Incoming_AndOutgoing_AreDisjoint` | the direction filter means something |
| `ListFriends_DoesNotReturnAnotherAccountsFriends` | the `self` gate |
| `FriendRequest_WritesAnAuditRecordNamingTheCaller` | ADR-0033 on a new surface |
| `SchemaConventionTests` (existing) | both tables obey the naming rules |

## Done when

- [x] every file in the Files table exists and compiles, plus the split above
- [x] `make agent-check` passes — exit 0 on 2026-09-28, 198 tests
- [x] `docs/SDD.md` §8 documents both tables and §16 says the graph is built and `Friends` is evaluable, with
      the projection itself still designed
- [x] `docs/api/ENDPOINTS.md` records what `409` means per route, who may act on a request, and that a
      friendship is symmetric and one row
- [x] `brain/migrations/migrations-index.md` carries the row
- [x] OWASP checklists walked (A01, A04, API1, API3, API4). Nothing new unfixed: every route is `self` or
      `auth`, a resource that is not the caller's answers `404`, and both listings are paged with a capped size
- [x] `status: done` in this file
