---
kind: adr
id: ADR-0042
status: Accepted
date: 2026-10-06
---

# ADR-0042 — Breached and context-word password screening, without a network call

## Context

SPEC-027's ASVS pass opened **T-32**: nothing screens a chosen password against breached or
context-specific words, so `correct horse battery` and `musicroom2026` are both accepted today. Four
committed L2 requirements are unmet because of it — **V6.1.2** (a context-specific word list exists),
**V6.2.4** (a new password is checked against a breached set), **V6.2.11** (the context list is used) and
**V6.2.12** (a breached set is consulted).

What exists already: a 12-character minimum with no composition rules, a 64-character and 72-byte
maximum, BCrypt at cost 12, and the per-account and per-IP limiters that [[ADR-0041-limiter-after-authentication]]
put after authentication. Those bound guessing **against this server**. They do nothing about an attacker
who has the digests, which is the exposure T-32 names: offline guessing, where a password on every list
falls immediately whatever the cost factor is.

The register row named the usual shape and left the choice to the owner: a k-anonymity range query against
Have I Been Pwned, which is a network call on the registration path.

## Decision

### Two screens, both local, no outbound request

| Screen | Closes | Shape |
|---|---|---|
| **Context words** | V6.1.2, V6.2.11 | a fixed project list plus the values the request already carries — the address local part and the display name |
| **Breached set** | V6.2.4, V6.2.12 | a set of common passwords **bundled with the application**, loaded once |

Neither screen makes a request to anything. That is the decision, and the rest follows from it.

### Have I Been Pwned is rejected, with the reason written down

Three reasons, in the order they weighed:

1. **It puts a third party on the registration path.** A provider that is slow makes registration slow; a
   provider that is down makes registration fail, or makes the screen silently pass — and a security control
   that disappears when a vendor has an incident is a control nobody can rely on. The mail provider already
   taught this: SPEC-024 had to decide explicitly that a failed send does not roll a registration back.
2. **The defence may run without internet.** A control that cannot be demonstrated is hard to defend, and
   this project is graded in a room.
3. **k-anonymity is good, not free.** Sending the first five hex characters of a password's SHA-1 leaks
   little, but it is still a password-derived value leaving the system on every registration, and the
   subject's own posture — secrets never leave, every outbound call is a decision — makes that a cost rather
   than a non-event.

**What is given up is stated rather than glossed:** a bundled set is a snapshot, it is smaller than HIBP's
hundreds of millions, and it does not learn about tomorrow's breach. The requirement asks that a chosen
password be checked against a breached set; it is met with a weaker set than the best available, and that
sentence belongs in `docs/owasp/ASVS-CONFORMANCE.md` next to the outcome rather than only here.

### The bundled set

- **Filtered to the passwords this system could actually accept.** The policy requires 12 characters, so every
  shorter entry in a common-password list is unreachable — `123456` and `password` can never be a password here.
  The bundled file carries only entries of 12 characters or more, and the spec that adds it records the source,
  the raw count **and** the retained count. If the retained count is a few thousand rather than a hundred
  thousand, that is a fact about the screen's reach and it goes in `docs/owasp/ASVS-CONFORMANCE.md` beside the
  outcome. Prefer a source that includes passphrases, because `correct horse battery` is the shape that gets
  past a length rule.
- **At most 100 000 entries** after filtering, one per line, lowercase, LF.
- **An embedded resource** in `MusicRoom.Infrastructure`, not a file read from disk at start-up: a file is a
  deployment step that can be forgotten, and a missing screen that boots is worse than a boot that refuses.
- **Loaded once into a case-insensitive set** behind an abstraction the Application layer names. One hash
  lookup per password choice; nothing per request.
- **The source and the entry count are recorded** in the spec that adds it, so a later reader can tell a
  curated list from a file somebody pasted.

### The context list

- A fixed part: `musicroom`, `music room`, `musicroomapp`, and the host name once a deployment exists.
- A per-request part: the **address local part** and the **display name**, which the register command already
  carries and the change and reset paths can read from the account they already load.
- A password is refused when, lowercased and with separators removed, it **contains** any context word of
  four characters or more. Containment rather than equality, because `musicroom2026` is the case the
  register row named.

### Where it applies, and what it answers

- **Every path that chooses a password**: register, password reset, and the authenticated change SPEC-028
  added.

> **Corrected 2026-10-06, before this record landed.** The sentence here said SPEC-028 had established
> "one password policy, one validator" and that this extends it. SPEC-033's successor measured the code and
> that is not the state of it: `RegisterCommandValidator`, `ResetPasswordCommandValidator` and
> `ChangePasswordCommandValidator` each declare the 12-character minimum, the 64-character maximum and the
> 72-byte ceiling separately. SPEC-028 applied the same policy; it did not share it. So the unification is
> **its own unit and lands first** — behaviour-preserving, provable by the suites that already exist — and the
> screens go into the shared rule afterwards. Adding a screen to three validators would have been the third
> copy of a rule that is already duplicated twice.

> **Corrected 2026-10-06 by SPEC-035's measurement.** The "two homes" paragraph said reset and change read the
> context words from the row their handler already loads. Neither does. `ResetPasswordCommandHandler` reads
> nothing — `TryResetPasswordAsync` spends the token and replaces the hash in one transaction, with no read
> before the write — and `ChangePasswordCommandHandler`'s `UserCredential` carries only `UserId`, `PasswordHash`
> and `IsEmailVerified`. The access token carries neither value either. The conclusion stands and the reason is
> replaced: the context screen belongs where the account can be reached, which means a pre-read on the reset path
> and two more projected columns on the change path.

- **Two homes, because the two screens need different things.** The breached screen is pure — a password and a
  set — and belongs in the shared rule every path calls. The context screen needs the account: only register
  carries the address and display name in its command, while reset carries a token and change carries two
  passwords, so those paths read the context words from the row their handler already loads. A validator
  cannot reach that row, and SPEC-028's wrong-current-password answer shows a handler can already answer
  `422`.
- The refusal is **`422`**, the answer those paths already give an unacceptable password, with a message that
  says which screen refused and **never echoes the password** or any part of it.
- Login is **not** screened. A password already on an account must still be able to sign in; forcing a
  rotation at the login path is a different decision with a different failure mode, and nothing in ASVS asks
  for it here.

## Consequences

- Four unmet L2 requirements become met, and T-32 closes.
- The application carries about a megabyte of embedded data, and the set is in the repository, which is a
  thing a reviewer can inspect — unlike a remote service's answer.
- The screen is only as current as the bundled file. Refreshing it is a one-line data change and no code
  change, which is the shape to prefer.
- An existing account with a breached password keeps working. That is deliberate (login is not screened) and
  it is a residual worth naming in the register when T-32 closes.
- If a future deployment makes an outbound call cheap and the owner changes their mind, the abstraction is
  already the seam: a second implementation, not a rewrite.

## Related

[[ADR-0007-password-hashing]] · [[ADR-0041-limiter-after-authentication]] · [[threat-register]] ·
[[asvs-conformance]] · [[secrets-management]]
