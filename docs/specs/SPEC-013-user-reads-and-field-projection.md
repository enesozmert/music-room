---
id: SPEC-013
title: Reading another user — per-field projection against the relationship, and a bounded search
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S3              # which DELIVERY-PLAN slice this belongs to
requirements: [V.1, V.6]
decisions: [ADR-0011, ADR-0012, ADR-0028, ADR-0033, ADR-0034]
standards: [authentication-authorization, application-security, rest-api-design, clean-architecture-cqrs, ef-core-persistence, dotnet-csharp, testing]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gates 1 and 5 answer "yes". Ships under the owner's blanket approval of 2026-09-29, with the three
  decisions below taken as the recommendations this spec would otherwise have escalated.
---

# Reading another user

## Goal

After this spec, `GET /api/v1/users/{userId}` returns a profile **projected per field** against the
requester's relationship to its owner — self, friend or stranger — and `GET /api/v1/users` searches
accounts returning public fields only, paged. The full row never leaves the server for a client to
filter, which is OWASP API3, and the three tiers subject V.1 requires finally mean three different
answers.

Third and last unit of slice S3.

## Out of scope

- **Anything that is not a profile read.** No follow, no block, no presence.
- **Search ranking.** A prefix match on the display name, ordered by that name. Relevance scoring is a
  product decision nobody has taken.
- **Reading a profile while unauthenticated.** Both rows are `auth`: the contract says so, and an
  anonymous reader would make the `Public` tier a scraping surface.

## Decisions taken under the blanket approval, 2026-09-29

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **The projection happens in the query**, field by field, against a relationship resolved once per request. | A field the reader may not see is never materialised, so it cannot leak through a serialiser, a log or a debugger. Rejected: reading the row and filtering in C# — one forgotten branch is a leak, and the row exists in memory either way |
| 2 | **An unknown user and a user who hid everything both answer `200` with a sparse body**, not `404`. | `404` would answer "does this account exist" for anyone who can type an id, and the contract reserves `404` for a resource hidden *from this caller*. A stranger's view of a fully private profile is a display name and nothing else — which is exactly what a stranger's view should be |
| 3 | **Search matches the display name only**, prefix, case-insensitive, and never a profile field. | `displayName` is the one always-public field (ADR-0034). Searching `realName` would let a stranger confirm a friends-only value by probing for it, which is a leak with a search box in front of it |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes** — decision 2 settles what a hidden profile answers, which ADR-0034 left to this unit |
| 2 | new or upgraded dependency? | no |
| 3 | schema change beyond this spec, or destructive migration? | no — reads only |
| 4 | changes `docs/api/ENDPOINTS.md`? | no — both rows exist. Design notes record the projection and the search field |
| 5 | touches authn, authz, tokens, secrets, audit? | **yes** — this *is* the visibility control. A mistake here shows a private field to a stranger |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

## Files

### Discovered during implementation, 2026-09-28

Two deltas, both found by tests rather than by reading:

| What | Why |
|---|---|
| `SearchByDisplayNameAsync` uses `EF.Functions.ILike`, not `StartsWith(..., OrdinalIgnoreCase)` | `The LINQ expression ... StartsWith(comparisonType: OrdinalIgnoreCase)' could not be translated`. Doing the comparison in memory would have meant reading every account to answer a search box |
| The `ILIKE` pattern is escaped | a consequence of the first: `%` and `_` are `ILIKE` syntax, so an unescaped query of `%` would match every account. `EscapeLikePattern` handles the three characters, and a test searches a literal value to prove the prefix is a literal |
| `UserEndpoints` is `public` rather than `internal` | the page cap is a contract fact a test asserts, so it reads the constant instead of repeating the number |

| Path | Action |
|---|---|
| `MusicRoom/src/MusicRoom/Application/Features/Profiles/Queries/GetUserProfile/*.cs` | create — query, handler, `VisibleProfile`, and the `ProfileRelationship` enum |
| `MusicRoom/src/MusicRoom/Application/Features/Profiles/Queries/SearchUsers/*.cs` | create — query, handler, `UserSearchResult` |
| `MusicRoom/src/MusicRoom/Application/Abstractions/Persistence/IProfileRepository.cs` | change — two read members |
| `MusicRoom/src/MusicRoom/Persistence/Repositories/ProfileRepository.cs` | change — implement both, projecting in SQL |
| `MusicRoom/src/MusicRoom/WebAPI/Endpoints/UserEndpoints.cs` | create — the two routes under `/api/v1/users` |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — map the group |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/UserProfileEndpointTests.cs` | create |
| `docs/api/ENDPOINTS.md` | change — design notes |
| `docs/api/openapi.json`, `docs/SDD.md`, `docs/SDD.pdf`, `docs/api/ENDPOINTS.pdf` | change |
| `docs/specs/SPEC-013-user-reads-and-field-projection.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Queries and handlers in `Application`, the repository change in `Persistence`, endpoints
in `WebAPI`. No new project, no `.csproj` change, every reference inward.
**2. Boundary types.** Both queries, `VisibleProfile`, `UserSearchResult` and `ProfileRelationship` are
`public`; handlers `internal sealed`.
**3. Invoked?** Two routes, two repository members, one consumer each.
**4. CLI or package.** None — no migration, because nothing is stored.
**5. corePackages.** Unchanged and near-empty; a visibility projection is not domain-agnostic.

**What the walk caught.** `IProfileRepository` gains two members and already has three, and
`ProfileRepository` is the file that will hold the projection — it is at 190 lines, so the projection goes
in its own private method from the start. `Program.cs` maps a fourth endpoint group through an extension,
keeping `Main`'s coupling flat.

## Steps

1. `ProfileRelationship` — `Self`, `Friend`, `Stranger` — resolved once per read: the caller's own id, then
   `AreFriendsAsync`, else stranger.
2. `VisibleProfile`: display name always, and each tiered field present only when the relationship clears
   its tier. A hidden field is **absent**, not null with a flag — "null" is what an empty field looks like,
   and a reader must not be able to tell an empty field from a hidden one.
3. The projection in SQL: each field's value is selected as `visibility <= permitted ? value : null`, with
   the permitted level derived from the relationship, so a hidden value never reaches the process.
4. `GET /users/{userId}` maps it, `200` always for an account that exists; a caller asking for an id that
   does not exist gets the same sparse shape, so existence is not confirmed either way.
5. `GET /users`: prefix match on display name, case-insensitive, paged with a capped size, ordered by name.
   Public fields only — which, since only `displayName` is always public, means the name and the id.
6. Tests, then regenerate the document.

## Verification

```bash
make agent-check
```

| Check | Proves |
|---|---|
| `GetUser_AsSelf_ReturnsEveryField` | the owner sees everything |
| `GetUser_AsFriend_ReturnsPublicAndFriendsFields` | the middle tier exists and differs from both others |
| `GetUser_AsStranger_ReturnsOnlyPublicFields` | the tier is enforced server-side |
| `GetUser_AsStranger_OmitsAFriendsOnlyFieldEntirely` | decision 1: absent, not null |
| `GetUser_AfterBefriending_RevealsTheFriendsFields` | the graph is what the tier consults |
| `GetUser_AfterRemovingAFriend_HidesThemAgain` | it is a live check, not a snapshot |
| `GetUser_ForAnUnknownId_Returns200WithNoFields` | decision 2 — existence is not confirmed |
| `GetUser_WithoutAToken_Returns401` | both rows are `auth` |
| `SearchUsers_MatchesADisplayNamePrefix` | the search works |
| `SearchUsers_DoesNotMatchAProfileField` | decision 3 — no probing a hidden value through search |
| `SearchUsers_CapsThePageSize` | OWASP API4 |
| `GetUser_ReturnsNoEmailOrHash` | §13.5 |

## Done when

- [x] every file in the Files table exists and compiles, with the deltas above
- [x] `make agent-check` passes — exit 0 on 2026-09-28, 209 tests
- [x] `docs/SDD.md` §16's V.1 tier row says the projection is built and all three tiers differ; the V.6 "own
      data only" row says the per-field gate is enforced server-side
- [x] `docs/api/ENDPOINTS.md` records the projection, why this row has no `404`, and that search reads the
      display name only with `%` and `_` escaped
- [x] OWASP checklists walked (A01, A03, A04, API1, API3, API4). Nothing new unfixed — API3 is what this unit
      exists to satisfy, and it is satisfied in the query rather than in a filter
- [x] `status: done` in this file
