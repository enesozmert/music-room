---
kind: adr
id: ADR-0033
status: Accepted
date: 2026-09-28
---

# ADR-0033 — The audit record commits with the action it describes, and fails closed

## Context

Subject V.6 requires a backend record of every client action with platform, device and app version, and
[[sdd]] §13.4 calls the audit log evidence and says it is kept rather than rotated. It did **not** say
what happens when the record cannot be written, nor whether the record shares the transaction of the
action it describes.

SPEC-008 put both questions to the owner and found the two answers interact: "fail closed" with a
post-commit write would mean the mutation commits, the audit insert fails, and the caller receives
`500` for an effect that stands — the worst of the four combinations to explain at a defence.

Writing the record inside the transaction also needed a boundary visible from outside the repository
doing the work, which [[sdd]] §7.1's layering had no mechanism for: SPEC-007 escalation 1 had
deliberately deferred an explicit unit of work, noting that the next multi-aggregate write would
reconsider it. The audit record was that write.

## Decision

Owner decisions of 2026-09-27, with a tie-break the same day.

### Fail closed

A command whose audit record cannot be written **does not stand**. The transaction is rolled back and
the caller receives `500`.

### The record is written inside the command's transaction

So an effect without a record cannot exist, and a record without an effect cannot either. A crash
between the two is not a state this system has.

### `IUnitOfWork` is the boundary, and it is ambient-aware

`AuditBehaviour` opens a transaction before the handler runs and commits after the record is written.
`IUnitOfWork.BeginAsync` reports whether *this* caller opened the transaction, because EF Core refuses
a nested `BeginTransactionAsync`: the repository methods written by SPEC-003 through SPEC-007 now
**enlist** in an ambient transaction and commit only what they opened themselves.

This answers SPEC-007 escalation 1's open question: one repository method writing two aggregates was
right for that spec, and the explicit unit of work arrived here.

### Scope: every mutating request, anonymous included

`user_id` is null on a row whose caller was not authenticated, and null on a failed login even when the
address matched — naming the account there would make the table an account-existence oracle for anyone
who can read it. A duplicate registration stays `completed` for the same reason.

### A command refused by validation writes nothing

`AuditBehaviour` is registered after `ValidationBehaviour`: a request that never became an action leaves
no record of one.

## Consequences

- **T-29**: a fault confined to `audit_records` stops every mutating endpoint. Accepted knowingly —
  reads, health probes and the OpenAPI document stay up, so the service remains diagnosable while
  refusing to act unrecorded.
- **T-27 closed** once SPEC-009 added the outcome vocabulary on top of this behaviour.
- A refusal that *throws* would roll its own record back, which is why
  [[ADR-0037-credential-rate-limiting]] made a throttle a returned value rather than an exception. Any
  future refusal must follow that rule or it will be invisible.
- The `audit_records` table has **no foreign key to `users`**: a record must outlive the account it
  describes, so deleting an account cannot delete the evidence of what it did.
- Rolling the table back destroys evidence rather than recomputable data. `brain/migrations/migrations-index.md`
  marks it one-way in any environment that has served a real request.
- Every handler that wants to say more than "it happened" does so through `IAuditContext`, which is
  request-scoped and read once, after the handler returns.

## Alternatives rejected

- **Fail open.** The service keeps working and an action happens that nobody can account for, which is
  precisely the gap subject V.6 asks about, with nothing forcing anyone to notice.
- **The record after the handler returns.** Trivially implementable, and it loses the record for an
  effect that stands if the process dies in the gap. With fail closed it also produces `500` after a
  successful mutation.
- **A `TransactionBehaviour` around every command.** The pattern `reference/nArchitecture` uses. It puts
  a transaction around commands that do not need one and hides the boundary from the handler that
  depends on it.
- **Auditing only authenticated actions.** The table would have stayed empty until bearer validation
  existed, so the behaviour would have shipped proving nothing.
