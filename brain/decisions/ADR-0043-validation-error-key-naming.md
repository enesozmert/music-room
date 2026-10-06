---
kind: adr
id: ADR-0043
status: Accepted
date: 2026-10-06
---

# ADR-0043 — A validation error key is the field name the client sent

## Context

SPEC-036 covered the `422` validation body with assertions for the first time and reported the keys
the code actually publishes, rather than the ones anybody expected. Three facts came out of it, each
measured:

1. **Two casings in one envelope.** `POST /events`, `PATCH /events/{id}` and the licence publish
   route answer `endsAtUtc`, `presenceProof` and `geofence`, because `EventLicenceRules` calls
   `WithName`. Every other route answers the CLR property name: `Password`, `NewPassword`, `Email`,
   `Licence`, `Name`, `Query`, `PageSize`, `YoutubeVideoId`, `GranteeUserId`, `TargetUserId`,
   `AfterTrackId`, `TimeToLiveSeconds`.
2. **One key is empty.** `MoveTrackCommandValidator` declares its "exactly one neighbour" rule as
   `RuleFor(command => command)` with no `WithName`, so `PATCH /playlists/{id}/tracks/{trackId}`
   publishes `{"errors":{"":["Send exactly one of afterTrackId or beforeTrackId."]}}`.
3. **The wire is camelCase.** `docs/api/openapi.json` publishes every request schema in camelCase —
   `currentPassword`, `newPassword`, `userId`, `canEdit` — because the host uses the web defaults of
   `System.Text.Json`.

Put together: a client sends `newPassword` and is told its `NewPassword` is wrong. The three
camelCase routes are not the inconsistency; they are the only ones that answer in the language the
request was written in. And an empty key cannot be routed to a field at all, so that message reaches
a user only if the client renders the whole dictionary verbatim.

`docs/api/ENDPOINTS.md` §Conventions documents the envelope and not the keys inside it, and the
OpenAPI drift guard cannot catch any of this: `HttpValidationProblemDetails` describes the shape of
`errors`, never its contents. So nothing failed, for twenty routes, until a test looked.

## Decision

### The key is the JSON name the client sent

A validation error is keyed by the **field name as it appears on the wire** — camelCase, matching the
request schema in `docs/api/openapi.json`. A nested field keeps its dotted path, also in camelCase:
`geofence.latitude`, `genres.count`, `bio.value`.

A client should be able to take a key from `errors` and look it up in the body it just sent. Today it
cannot, for seventeen keys out of twenty.

### It is configured once, not written twenty times

`ValidatorOptions.Global.PropertyNameResolver` is set in the composition root to produce the camelCase
name, so **a validator written tomorrow gets the policy for free**. Twenty `WithName` calls would make
the convention a thing each author has to remember, which is the same failure mode that produced two
casings in the first place. The three existing `WithName` calls in `EventLicenceRules` become
redundant and go.

### No key is ever empty

A rule that validates the command as a whole names the field the message is about. `MoveTrackCommandValidator`'s
message is about `afterTrackId` and `beforeTrackId`; it keys on `afterTrackId`, which is the field a
client would highlight. A rule with genuinely no field — none exists today — would be a `400` about
the request rather than a `422` about a field.

### Now, before a client exists

This is a breaking change to a published contract, and it is taken now for one reason: **there is no
client**. `MusicRoomApp/` is not written, and the owner sequenced it after the backend. The cost of
this change today is a test update; after S8 and the mobile client it is a coordinated release. A
contract defect found before the first consumer is a free fix, and declining it would mean shipping
two casings into the app that has to read them.

### One test holds it

A test asserts, for at least one route per shape, that every key in `errors` is a field name present
in the request schema that route publishes. Not a list of expected strings — a property that a future
route cannot quietly break.

## Consequences

- Seventeen keys change from PascalCase to camelCase. Every one of them is asserted by SPEC-036's
  tests, so the change is visible in a diff rather than discovered by a client.
- `PATCH /playlists/{id}/tracks/{trackId}` stops publishing an empty key.
- `docs/api/ENDPOINTS.md` §Conventions gains the rule, because an envelope documented without its key
  convention is what allowed this to drift.
- `docs/api/openapi.json` does not change: `HttpValidationProblemDetails` describes the envelope, and
  the envelope is untouched. The drift guard will stay green, which is exactly why it could not have
  caught this and why the test above exists instead.
- Issue **O28** closes with this and its implementing spec.
- A future validator cannot reintroduce the split without removing the global resolver, which is one
  line in the composition root rather than a convention in somebody's memory.

## Related

[[ADR-0005-api-conventions]] · [[endpoint-inventory]] · [[rest-api-design]] · [[sdd]]
