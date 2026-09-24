---
kind: adr
id: ADR-0028
status: Accepted
date: 2026-09-24
---

# ADR-0028 — Domain owns its base types; Application owns its service abstractions

## Context

The first implementation attempt on SPEC-002 stopped before writing a line, because the spec
contradicted three accepted sources at once. Two distinct collisions, both inherited from the
shape of `reference/nArchitecture`:

1. **Where the common-column base lives.** SDD §8.3 gives every table `id`, `created_at_utc` and
   `updated_at_utc`. The reference repository puts that base at
   `Core.Persistence/Repositories/Entity.cs` and has its domain entities inherit it. For our
   entities to do the same, `MusicRoom.Domain` would have to reference `Core.Persistence` — which
   [[ADR-0011-solution-layout]], [[clean-architecture-cqrs]] and [[sdd]] §7.1 all forbid in
   identical words: `Domain` references nothing, and an outward reference is a build-breaking
   defect.

2. **Where `IPasswordHasher` lives.** The spec placed it in `Core.Security`, which would require an
   `Application -> Core.Security` edge that no ADR and no SDD section had ever drawn.

[[ADR-0011-solution-layout]] copied the reference layout but said the shape is copied, never the
files. It did not say what happens when the shape and the dependency rule disagree. This does.

## Decision

### The dependency rule wins over the reference layout

When `reference/nArchitecture` and the dependency rule disagree, the dependency rule wins and the
divergence is recorded here. This is the second recorded divergence, after assembly naming in
[[ADR-0011-solution-layout]].

### Base entity types live in `MusicRoom.Domain`

`MusicRoom/src/MusicRoom/Domain/Common/Entity.cs`. Identity and creation time are domain facts,
not storage mechanics — a `User` has an id because the business has users, not because a table has
a primary key.

Consequence, accepted: the generic repository in `Core.Persistence` cannot constrain on that base,
because a `corePackage` must stay domain-agnostic ([[ADR-0011-solution-layout]]). It is generic
over `where TEntity : class`, and the `Persistence` project supplies the specifics. Slightly less
compile-time safety in one generic signature, in exchange for a `Domain` that depends on nothing
and can be read on its own.

### Service abstractions live in `Application`, implementations in `Infrastructure`

`IPasswordHasher` is declared in `MusicRoom.Application`; the BCrypt implementation lives in
`MusicRoom.Infrastructure`. This is what [[clean-architecture-cqrs]] already required —
*"Application: repository and service **interfaces**"* — so no new project edge is created.

`Core.Security` keeps the genuinely domain-agnostic security helpers (JWT signing and validation,
token generation) used by `Infrastructure` and `WebAPI`. It does not hold abstractions that
`Application` consumes.

## Consequences

- No project reference changes direction. The graph in [[sdd]] §7.1 stands exactly as drawn.
- The BCrypt choice stops leaking upward: [[ADR-0007-password-hashing-bcrypt]] can be superseded by
  editing `Infrastructure` alone. Under the rejected arrangement, `Application` would recompile.
- `Core.Persistence` is thinner than the reference's version, and honestly so: the reference is a
  teaching template with two demo domains, which is why its base type sits where it does.
- Three lines of common columns are declared once in `Domain`, not repeated per entity.
- A future `corePackage` that wants to know about entity identity must take it as a type parameter,
  not as a reference to `MusicRoom.Domain`.
- The spec's Files table was wrong in two places and silently incomplete in five more. Both are
  corrected, and the gate question "does this contradict an accepted ADR" now has a real answer to
  check against for every later spec.

## Alternatives rejected

- **No base type; repeat the three columns per entity.** Zero coupling and a clean defence answer,
  but the repetition grows with every aggregate and invites drift in column naming.
- **A new `Core.Domain` package holding the base.** Cleanest layering on paper and it restores the
  generic constraint, but it needs another project and a change to the sentence "Domain references
  nothing" that three documents currently state.
- **Amend [[ADR-0011-solution-layout]] to allow `Domain -> Core.Persistence`.** Matches the
  reference exactly. Rejected: it couples the domain to a persistence package, which is precisely
  what the dependency rule exists to prevent, and it invites the question of why the project claims
  Clean Architecture while violating its one hard rule.
