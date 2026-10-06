---
title: Capacity measurement
subtitle: Five Gatling scenarios, and the figure they justify
date: 2026-10-03
---

# Capacity measurement — 2026-10-03

Produced by SPEC-023 against the commit on `feat/spec-023-load-measurement`. Every number here was
measured on the machine declared in section 1. Nothing is extrapolated; where a figure would need an
assumption, the assumption is written next to it.

The simulations are in `tests/load/`, and `make load SCENARIO=<name>` reproduces any row below.

## 1. The machine

| | |
|---|---|
| Host | Apple M5 Pro (`Mac17,9`), 15 cores, 24 GiB RAM |
| Host OS | macOS 26.5.1 (build 25F80) |
| Container runtime | Docker Desktop 4.91.0, Docker Engine 29.8.0 |
| **Docker VM** | **15 CPUs, 7.75 GiB RAM** — the resources the API and PostgreSQL actually had |
| API container | `music-room-api:latest`, `linux/arm64`, .NET 10 `-c Release` publish |
| Database container | `postgres:18.2-alpine`, `linux/arm64`, `max_connections = 100` (default) |
| Load generator | Gatling 3.11.5 in `maven:3-eclipse-temurin-21`, **on the same machine**, `--network host` |

The host and the VM are both stated because they are different numbers and only one of them is the
system under test. The generator shares the machine with the system it measures, which is a declared
limitation of this measurement: section 6 shows the CPU headroom that remained, which is what makes the
figure in section 7 defensible in spite of it.

## 2. The topology

One API instance, one PostgreSQL instance, on one Docker network, started with `make up`:

```
Gatling (container, host network) ──HTTP/1.1──► api:8080 ──TCP──► db:5432
                                 ──WebSocket──►
```

No reverse proxy, no connection pooler, no second instance. This is what ADR-0015 deploys — a single
instance — so it is what was measured. `ASPNETCORE_ENVIRONMENT=Development`, because SPEC-003's start-up
guard refuses any other environment while `IMailService` has only its logging implementation (issue O17);
the image is a Release publish either way, and no scenario sends mail (SPEC-023 decision 5).

## 3. The tool and the command

```bash
make up                                  # the system under test
./tests/load/seed/seed.sh                # 220 verified accounts, one event, one playlist
./tests/load/seed/tokens.sh              # one access token per account, minted off the measured instance
make load SCENARIO=queue-read            # one scenario; also vote-storm, reorder, fan-out, login
./tests/load/analyse.py                  # the per-step table, which the Gatling console does not print
```

Two things about the harness are themselves measurement decisions.

**No scenario signs in.** `POST /auth/sessions` allows twenty attempts per IP per five minutes, and a load
generator is one IP, so a scenario that signed its virtual users in would report the limiter instead of the
endpoint under test. `tokens.sh` mints the tokens against a **second, throwaway instance** of the same image,
started with a raised sign-in limit and its own in-process limiter state, and stopped again. The measured
instance keeps the approved numbers. Both read the same signing key, so one mints what the other accepts.

**Accounts are seeded in SQL, not through the API.** Registration allows ten per IP per hour. The rows are
written with pgcrypto's `crypt(…, gen_salt('bf', 12))`, which produces the `$2a$12$` hashes BCrypt.Net
verifies at the cost the application uses — so every sign-in below pays the real price. Raising a limiter on
the system under measurement was rejected: a capacity figure measured with the controls turned down does not
describe the system that shipped.

## 4. The scenarios

| Scenario | What it loads | Shape |
|---|---|---|
| `queue-read` | `GET /events/{id}/queue` — the hot authenticated read, whose cost is a `GROUP BY` over the vote rows | closed, 10 → 200 concurrent, 20 s per step, 200 ms think time |
| `vote-storm` | `PUT /events/{id}/tracks/{id}/vote` — the contended write of subject V.2.1 | closed, 10 → 200 concurrent voters, one vote per 1.2 s each |
| `reorder` | `GET` then `PATCH` on `/playlists/{id}/tracks` with `If-Match` — the contended write of V.2.3 | closed, 10 → 200 concurrent, 100 ms think time, every move aimed at the head of the list |
| `fan-out` | SignalR `PlaylistHub`: 200 connections in one group while one writer moves a track every 500 ms | open, 50 connections over 20 s then 150 over 40 s, held |
| `login` | `POST /auth/sessions` — the deliberately rate-limited path | open, 5 → 50 attempts/s, then 50/s for 30 s |

`vote-storm` is paced at one vote per 1.2 seconds per voter because one account may cast sixty a minute
(SPEC-018). Above that pace every step reports the limiter refusing, which measures the limiter and not the
write. The vote path's own ceiling is therefore a decided number, not a measured one.

## 5. The numbers

Percentiles are milliseconds, over successful responses. `req/s` is the step's own rate, not the run's mean.

### 5.1 `queue-read` — the hot authenticated read

| concurrent | requests | err % | req/s | p50 | p95 | p99 |
|---|---|---|---|---|---|---|
| 10 | 914 | 0.00 | 45.7 | 7 | 13 | 18 |
| 25 | 2 320 | 0.00 | 116.0 | 9 | 15 | 19 |
| 50 | 4 665 | 0.00 | 233.2 | 8 | 18 | 22 |
| 100 | 9 424 | 0.00 | 471.2 | 6 | 19 | 29 |
| 200 | 19 269 | 0.00 | 963.5 | 2 | 9 | 14 |

963 requests/s at 200 concurrent readers, p99 14 ms, nothing failed. The read is not the ceiling: the think
time, not the server, set the rate at every step.

An earlier run of this same scenario, before the fix in section 6.2, produced two `500`s at the 200-user step
(0.01%) from the same connection exhaustion the reorder scenario reaches reliably. The read touches one
connection briefly, so it reaches that ceiling only marginally.

### 5.2 `vote-storm` — many accounts voting on one event

| concurrent voters | requests | err % | req/s | p50 | p95 | p99 |
|---|---|---|---|---|---|---|
| 10 | 160 | 0.00 | 8.0 | 18 | 86 | 626 |
| 25 | 405 | 0.00 | 20.2 | 11 | 21 | 26 |
| 50 | 829 | 0.00 | 41.5 | 8 | 28 | 30 |
| 100 | 1 641 | 0.00 | 82.0 | 6 | 17 | 22 |
| 200 | 3 302 | 0.00 | 165.1 | 5 | 16 | 25 |

Two hundred concurrent voters at a human pace, zero refusals, p99 25 ms. The 626 ms p99 in the first step is
the vote path's first few requests after a container restart — the next step, at two and a half times the
concurrency, is twenty-five times faster.

The write counts vote rows on every read instead of keeping a counter (SPEC-018), and this is the measurement
that decision is accountable to: the cost did not grow with the number of voters.

### 5.3 `reorder` — several collaborators reordering one playlist

| concurrent | request | requests | err % | req/s | p50 | p95 | p99 |
|---|---|---|---|---|---|---|---|
| 10 | `GET` tracks | 1 713 | 0.00 | 85.7 | 3 | 7 | 12 |
| 10 | `PATCH` move | 1 713 | 0.00 | 85.7 | 8 | 16 | 19 |
| 25 | `GET` tracks | 4 414 | 0.00 | 220.7 | 2 | 4 | 8 |
| 25 | `PATCH` move | 4 414 | 0.00 | 220.7 | 6 | 19 | 29 |
| 50 | `GET` tracks | 8 914 | 0.00 | 445.7 | 2 | 4 | 8 |
| 50 | `PATCH` move | 8 910 | 0.00 | 445.5 | 5 | 22 | 50 |
| 100 | `GET` tracks | 16 235 | 0.00 | 811.8 | 2 | 3 | 7 |
| 100 | `PATCH` move | 16 238 | 0.00 | 811.9 | 16 | 50 | 86 |
| **200** | `GET` tracks | 8 782 | **26.00** | 439.1 | 118 | 220 | 271 |
| **200** | `PATCH` move | 8 069 | **9.10** | 403.4 | 101 | 678 | 766 |

**This is the ceiling.** At 100 concurrent collaborators the pair runs at 1 624 requests/s with nothing
failing. At 200 it breaks the 1% rule by a wide margin, throughput *falls* to 843 requests/s, and p99 on the
move goes from 86 ms to 766 ms. Section 6 says why.

`412` and `422` are expected answers here and are not errors: under this much contention most `If-Match`
tokens are stale by the time they arrive, which is the guard working. Only `5xx` was counted as a failure.

### 5.4 `fan-out` — 200 connections in one group

| window | request | count | err % | p50 | p95 | p99 |
|---|---|---|---|---|---|---|
| 0–20 s | `POST negotiate` | 50 | 0.00 | 4 | 12 | 15 |
| 0–20 s | join completion | 50 | 0.00 | 5 | 7 | 8 |
| 0–20 s | broadcast interval | 445 | 0.00 | 521 | 527 | 534 |
| 20–40 s | `POST negotiate` | 75 | 0.00 | 2 | 4 | 6 |
| 20–40 s | join completion | 75 | 0.00 | 5 | 7 | 8 |
| 20–40 s | broadcast interval | 730 | 0.00 | 520 | 527 | 528 |
| 40–60 s | `POST negotiate` | 75 | 0.00 | 2 | 4 | 5 |
| 40–60 s | join completion | 75 | 0.00 | 5 | 8 | 10 |
| 40–60 s | broadcast interval | 744 | 0.00 | 519 | 527 | 535 |
| 60–80 s | broadcast interval | 81 | 0.00 | 524 | 532 | 533 |

Labelled by elapsed time, not by concurrency: this scenario ramps connections and holds them, so the
listener count grows through the run to 200.

The writer moved a track every 500 ms. The interval between two broadcasts arriving at one listener stayed at
520–535 ms from the first fifty connections to the last — the fan-out kept pace with the writer, and the
handshake cost did not grow as the group filled. 200 concurrent connections in one group is therefore a
measured floor for the realtime path, not its ceiling: this scenario never found one.

### 5.5 `login` — the rate-limited path

| window | requests | err % | req/s | p50 | p95 | p99 |
|---|---|---|---|---|---|---|
| 0–20 s | 389 | 0.00 | 19.4 | 2 | 202 | 214 |
| 20–40 s | 921 | 0.00 | 46.0 | 1 | 2 | 3 |
| 40–60 s | 999 | 0.00 | 50.0 | 1 | 2 | 3 |
| 60–80 s | 16 | 0.00 | 0.8 | 1 | 2 | 2 |

2 325 attempts in 60 seconds: **20 accepted, 2 305 refused with `429`.** Twenty is exactly the per-IP permit
(SPEC-006), so the limiter did precisely what it was configured to do.

No error, no hang, and a refusal costs ~1 ms — the limiter rejects before the password is ever hashed, which
is the point of putting it ahead of the handler. The p95 of 202 ms in the first window is the twenty accepted
attempts paying BCrypt at cost 12, as designed.

`Retry-After` on the refusal is asserted functionally by `CredentialRateLimitingTests`, not here: a load
assertion that only fires on a refused request would pass on a run where nothing was refused.

## 5.6 The hub limits, re-measured — 2026-10-06 (SPEC-033)

[[ADR-0030-hub-rate-limits-and-origin]] set three numbers on 2026-10-04 and said what they were: *"a starting
point with a measurement behind the shape rather than the value"*, with *"the first run after this lands says
whether 60 is generous or tight"*. SPEC-026 landed them; this is that run.

**Machine: the one declared in §1** — Apple M5 Pro, 15 cores, 24 GiB, Docker VM 15 CPUs and 7.75 GiB, one API
container and one PostgreSQL container, generator on the same machine. Every figure below was observed there,
on the second run; the first was discarded as a warm-up and produced the same three counts.

`make load SCENARIO=hub-limits` — `HubLimitProbeSimulation`, three probes, one per number.

### What a realistic client is, before anything is measured

ADR-0030's own words: *"a client that joins a group and then mostly listens"*. So: **one connection per hub it
needs**, a join on entering a screen, a leave on leaving it, and otherwise listening. The three hubs are three
separate connections — `/hubs/playlists`, `/hubs/events`, `/hubs/control` — which is the fact probe 3 turns on.

| Probe | What it does | Observed, on the machine above |
|---|---|---|
| 1, paced client | enters a screen, stays 4 s, leaves, 1 s pause, twelve times | **24 invocations a minute, 0 refused** |
| 2, unpaced client | the same invocations with the pauses removed, 90 attempts | **60 accepted, 30 refused** |
| 3, multi-hub client | one account opening a connection per hub, then again | **before the amendment: 5 held, the 6th refused. After: 12 held, the 13th refused** |

### Verdict per number

| Number | Verdict | On what |
|---|---|---|
| **60 invocations per connection per minute** | **generous** | A realistic client reaches **24 a minute** — two and a half times under the limit. Probe 2 shows the limit binding exactly where it was set: the 61st invocation inside the window is refused, with `HubException` and the connection still open, which is the behaviour ADR-0030 asked for rather than a drop. Nothing a person does with one connection comes near it |
| **20 concurrent connections per address** | **was tighter than it read — amended to 60** | No probe filled it: the per-account ceiling refuses first. The arithmetic was the finding — at 3 connections per device, 20 per address is **6 devices** on one network, and the venue case ADR-0030 named is a room of people sharing one wifi. 60 is twenty devices |
| **5 concurrent connections per account** | **was wrong, for the reason the ADR itself gave — amended to 12** | Probe 3 held 5 and was refused the 6th, and a single device costs **3**. See below, and the re-run beneath it |

### The ceiling counts connections; its rationale counted devices

ADR-0030 justified five per account as *"a phone, a tablet and a reconnect in flight, with room to spare"*. That
sentence counts **devices**. The ceiling counts **connections**, and probe 3 measured what one device actually
opens: a connection on each of `/hubs/playlists`, `/hubs/events` and `/hubs/control` — **three**, all three
accepted for the same account.

So the number does not buy what its rationale says it buys:

| What the ADR described | Connections it costs | Against a ceiling of 5 |
|---|---|---|
| one device | 3 | fits, with 2 spare |
| a phone and a tablet | 6 | **refused** |
| a phone, a tablet and a reconnect in flight | 7 to 9 | refused |

Probe 3 is the evidence: six attempts for one account, five established, the sixth refused — at the hub filter
rather than the handshake, which is SPEC-026's authoritative half of the ceiling doing its job. The limit works
exactly as built. It is the value that does not match the intent.

**The number was not changed by this measurement.** ADR-0030 reserves that: *"Changing one is an amendment here,
not a code review argument."* SPEC-033 stopped at `draft` with the figures and a proposal; the owner amended the
ADR on 2026-10-06 — **12 per account, 60 per address, the invocation limit unchanged** — and corrected the
proposal while doing it. The proposal was 10, which stopped one connection short of its own reason:
`ClientTimeoutInterval` is 60 seconds, so a phone that changes network drops all three connections at once and
the server reaps none of them for up to a minute. It holds three stale and three new, so reconnect headroom has
to be a **whole device** — 9 + 3 — or the ceiling refuses the reconnect it exists to allow.

### The same probe, after the amendment

Re-run on the same machine, against an API container rebuilt with the new defaults:

| | Attempted | Established | Refused |
|---|---|---|---|
| before, ceiling 5 | 6 | **5** | the 6th |
| after, ceiling 12 | 13 | **12** | the 13th |

Both refusals came from the hub filter rather than the handshake, which is SPEC-026's authoritative half of the
ceiling: the middleware reads a count it does not own, so a connection that races past it is stopped where the
count lives. The rate probes were not re-run — nothing about them moved, and §5.6's 24 and 60 stand as measured.

A changed default with no run behind it is what this unit exists to prevent, which is why both counts are here
rather than only the new one.

## 6. The bottleneck, and the two defects this measurement found

### 6.1 The bottleneck: PostgreSQL connections, not CPU

At the 200-user reorder step the API answers `500` to a quarter of the reads. Every one of them is the same
server-side error:

```
Npgsql.PostgresException (0x80004005): 53300: sorry, too many clients already
```

10 874 of those lines in the API log during one 100-second run. `max_connections` on the database container
is the PostgreSQL default of 100; Npgsql's default maximum pool size is also 100, and PostgreSQL reserves
slots for superuser connections, so the pool is allowed to ask for more connections than the server will ever
give. Past roughly 100 in-flight requests the pool wins the race against the server's limit and the extra
requests fail outright instead of queueing.

CPU was **not** the limit. Sampled every 4 seconds through that run, the peak was:

| container | peak CPU | peak memory |
|---|---|---|
| `api` | 453% of 1500% available | 395 MiB |
| `db` | 484% of 1500% available | 270 MiB |

About 62% of the VM's CPU was still idle at the moment requests were failing — and that is with the load
generator on the same machine. The ceiling is a configured connection count, not the hardware.

The fix is a deployment decision rather than a code change, and it is **not** made here: it is issue **O22**
in `docs/SDD.md` §17. Capping Npgsql's pool below the server's `max_connections` makes requests queue instead
of fail; raising `max_connections` moves the ceiling. Which pair of numbers is right depends on where this
runs, and S4 moves `DATABASE_URL` to Supabase, whose pooler changes the arithmetic completely. Picking a
number now, against a database the application will not be using, would be guessing. `load-testing` requires
a re-measurement after a change like that, so the change and its measurement belong together in one unit.

### 6.2 A defect: the per-account vote limit was a per-address limit

The first `vote-storm` run refused 98% of votes at every step, including the ten-user step — 120 votes
accepted in a whole run, which is two windows of the per-account permit. 220 accounts could not have shared
one bucket unless they were not being counted per account.

They were not. `app.UseRateLimiter()` ran **before** `app.UseAuthentication()`, so `HttpContext.User` carried
no claims when the partition key was computed, and SPEC-018's per-account vote policy silently took its
fallback: the remote address. The feature exists for a room full of people voting over one venue's network,
and for exactly those people it had become one allowance of sixty votes a minute, shared.

No test could see it. The functional suites raise the credential limits and never cast sixty votes, and for
one account on one address a per-account bucket and a per-address bucket are the same bucket.

[[ADR-0041-limiter-after-authentication]] records the fix — the limiter now runs after authentication and
before authorisation — and `EventVoteRateLimitingTests` is the regression: two accounts, one address, and the
second must still be able to vote. Section 5.2 is the re-measurement: 3 302 votes, none refused.

This is what the load measurement was for. A capacity run found a security control that was not doing what
its specification said, which no amount of throughput would have revealed.

### 6.3 A harness defect worth recording: four steps, not five

`reorder` was first written with four injection steps while every other scenario had five, so its first run
stopped at 100 concurrent users — one step short of the only step that fails. The ladder now comes from one
place, `LoadProfile.closedSteps()`, for every closed-model scenario. A report that had compared a read
measured to 200 users against a write measured to 100 would have been comparing two different runs and would
have missed the ceiling entirely.

## 7. The figure

**On the machine in section 1, one instance of this API supports 100 concurrent users performing contended
writes, and 200 concurrent users reading or listening.**

The arithmetic, from the measurements above:

1. **The write path sets the limit.** `reorder` at 100 concurrent collaborators: 1 624 requests/s, 0.00%
   failures, p99 86 ms. At 200: 3.86% failures, which is over `load-testing`'s 1% rule, so by that rule the
   last good step is 100. Throughput fell at the same time, which is the signature of a ceiling rather than of
   a slow step.
2. **The read path is not the limit.** 963 requests/s at 200 concurrent readers with p99 14 ms and nothing
   failing. It was never saturated: the scenario's own think time set the rate.
3. **The realtime path is not the limit either, up to where it was measured.** 200 concurrent connections in
   one group, broadcast intervals flat at ~525 ms against a writer at 500 ms. Its ceiling is unknown and is
   above 200.
4. **Votes are bounded by a decision, not by capacity.** 200 concurrent voters at a human pace cost p99
   25 ms. One account may cast 60 votes a minute by policy, so a vote figure is a policy figure.
5. **Credentials are bounded by a decision too.** 20 accepted attempts per IP per 5 minutes, and refusals
   cost ~1 ms. A credential flood does not consume this server.

So the honest sentence is the one above: **100 concurrent writers** on this machine, with the ceiling being
PostgreSQL's connection count and not the hardware — 62% of the CPU was idle when the failures began.

What this figure is **not**:

- It is not a figure for a deployed environment. ADR-0015 deploys nowhere yet (issue O11), and S4 moves the
  database to Supabase. Both changes invalidate the ceiling in section 6.1, in opposite directions.
- It is not extrapolated to more instances. Limiter state is per process (issue O8), so a second instance
  changes behaviour and not only throughput.
- It is not a figure for a machine with a separate load generator. The generator shared these 15 cores. CPU
  headroom says that did not cause the ceiling; it does not prove the latencies would be identical.
- It does not state a ceiling for the realtime path, because this measurement did not find one.

## 8. Reproducing this

```bash
make up                                  # one API, one PostgreSQL
./tests/load/seed/seed.sh                # 220 accounts, one event, one playlist — writes tests/load/seed/out/
./tests/load/seed/tokens.sh              # re-run before each measurement session: tokens live 15 minutes
make load SCENARIO=queue-read            # then vote-storm, reorder, fan-out, login
./tests/load/analyse.py                  # per-step table for a closed-model scenario
./tests/load/analyse.py --windows        # per-window table for fan-out and login
```

The first run of a scenario after a container start is a warm-up and was discarded: section 5.2's first step
shows what that costs when it is not.
