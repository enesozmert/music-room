---
id: SPEC-023
title: Capacity — five Gatling scenarios, and the number they justify
status: done           # draft | approved | in-progress | done | blocked
tier: B                # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9              # which DELIVERY-PLAN slice this belongs to
requirements: [V.7, V.6]
decisions: [ADR-0015, ADR-0016, ADR-0018, ADR-0040]
standards: [load-testing, ci-cd-containerization, testing, logging-observability]
approved_by: enesozmert # blanket approval of outstanding specs and ADRs, 2026-09-29
escalation: |
  Gate 2 answers "yes" and gate 1 "as a written deviation". ADR-0018 says Gatling "run from its Docker image";
  no official Gatling image resolves from this machine's registry (probed: `gatlingcorp/gatling`,
  `gatlingcorp/gatling-maven` — neither exists; `denvazh/gatling` exists and is an unmaintained third-party
  mirror). Decision 1 runs Gatling's own `gatling-maven-plugin` inside the first-party `maven` image instead,
  which keeps the ADR's intent — Gatling, containerised, reproducible — and is recorded here rather than by
  editing an accepted record. The other decision worth the owner's eye is 5: the measurement runs with
  `ASPNETCORE_ENVIRONMENT=Development`, because SPEC-003's start-up guard refuses to boot any other environment
  until O17 lands a real mail provider.

  Two things the measurement itself produced, after the fact. **It found a defect in shipped code**: the rate
  limiter ran before authentication, so SPEC-018's per-account vote policy was partitioning on the remote
  address and a whole venue shared one allowance. ADR-0040 and a regression test fix it inside this spec,
  because a capacity report measured against a broken control would have reported a false number. **And it
  found the ceiling is a configured one**: PostgreSQL `max_connections`, not CPU. That fix is issue O22 and is
  deliberately not made here — it is a deployment decision, S4 moves the database to Supabase, and
  `load-testing` wants the change and its re-measurement in one unit.
---

# Capacity measurement

## Goal

After this spec subject V.7 is answerable with numbers: five committed Gatling simulations under
`tests/load/`, a `make load` that runs them against the compose stack, and `docs/load/REPORT.md` carrying the
machine specification, the per-scenario results at each concurrency step, the bottleneck with its evidence,
and **the supported concurrent-user figure with the reasoning that connects it to the measurements**.

First unit of slice S9. It does not close S9: the hub call limits of ADR-0030 (issue O21) and the remaining
`asvs-conformance` evidence are separate units, and the first of those is still waiting on numbers the owner
owes.

## Out of scope

- **ADR-0030's hub call limits and handshake origin.** Issue O21, and its numbers are
  `UNKNOWN - requires decision`. Measuring what an unlimited hub does is in scope — that measurement is part
  of the evidence for choosing those numbers — but building the limit is not.
- **Tuning.** This spec measures and reports. A change to the data access or realtime layer to improve a
  number is its own unit, and `load-testing` requires a re-measurement after one.
- **A hosted environment.** ADR-0015 deploys nowhere yet (O11). The figure is for the declared machine, and
  the report says so.

## Decisions taken under the blanket approval, 2026-10-03

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **Gatling runs as `gatling-maven-plugin` inside `maven:3-eclipse-temurin-21`**, not from a Gatling image. | No official Gatling image resolves: `gatlingcorp/gatling` and `gatlingcorp/gatling-maven` do not exist on the registry this machine reaches, and the one that does, `denvazh/gatling`, is an unmaintained third-party mirror. The plugin is GatlingCorp's own artifact from Maven Central, and the image is first-party, so the run stays containerised and reproducible — ADR-0018's actual intent. Rejected: the stale mirror, and a host install of Gatling, which is the opposite of reproducible |
| 2 | **Simulations are written in Gatling's Java DSL.** | The repository is .NET and nobody here maintains Scala. Java is a first-class DSL in Gatling 3.9+, and a Java simulation is readable by whoever maintains the backend |
| 3 | **Accounts are seeded entirely in SQL, with pgcrypto hashes.** | Registration allows ten per IP per hour (SPEC-006), and the first attempt at seeding through the API was refused after ten accounts. `crypt(…, gen_salt('bf', 12))` produces the `$2a$12$` hashes BCrypt.Net verifies at the cost the application uses, so every sign-in still pays the real price, and `is_email_verified` is set in the same statement because the verification token is only in the application log (O17). Rejected: raising the registration limiter, which is how a capacity figure stops describing the system that shipped |
| 4 | **Five scenarios, each with its own injection profile and its own numbers**, as `load-testing` lists them: authenticated queue read, concurrent votes on one event, playlist reorder by several collaborators, SignalR fan-out in one group, login burst. | The standard names them, and they are the five shapes this API has: a hot read, two contended writes, a connection-bound realtime path, and a deliberately rate-limited one. Reporting one aggregate number across all five would hide which of them is the ceiling |
| 5 | **The run uses `ASPNETCORE_ENVIRONMENT=Development`, and the report says so.** | SPEC-003's start-up guard throws in any other environment while `IMailService` has only its logging implementation (O17), so Production cannot boot. The image is a `-c Release` publish either way, which is the half of "production configuration" that affects throughput; the differences that remain are log verbosity and the mail adapter, and **no scenario sends mail**. Rejected: measuring a Debug build, and claiming a Production figure that was not measured |
| 6 | **An error rate above 1% marks the step as over capacity**, and the ceiling is the last step below it. | `load-testing`'s rule, taken literally. A step that answers `429` is **not** over capacity when the scenario is the login burst: that is the limiter working, and the report separates refusals-by-design from failures |
| 7 | **No scenario signs in. Tokens are minted beforehand, against a throwaway second instance** of the same image started with a raised sign-in limit, on another port, and stopped again. | Sign-in allows twenty attempts per IP per five minutes and a load generator is one IP, so a scenario that signed its virtual users in would report the limiter instead of the endpoint under test — the first run of `queue-read` failed 94% of its requests for exactly that reason. A second instance has its own in-process limiter state and reads the same signing key, so it mints tokens the measured instance accepts while the measured instance keeps the approved numbers. Rejected: raising the limit on the system under measurement, and spacing hundreds of sign-ins over an hour |
| 8 | **The vote scenario is paced at one vote per 1.2 seconds per voter**, which is fifty a minute against the sixty a minute a single account may cast. | Above that pace every step reports the limiter refusing, which measures a decided number instead of the write. The capacity statement for votes is therefore "N concurrent voters at a human rate", and the per-account ceiling stays a policy figure — SPEC-018's, not this spec's |
| 9 | **The defect the measurement found is fixed in this spec; the ceiling it found is not.** | The limiter-ordering defect made SPEC-018's per-account vote limit a per-address limit, so the numbers measured against it were wrong — a report has to be re-measured after the fix, and ADR-0040 plus `EventVoteRateLimitingTests` land here. The connection ceiling is a pair of deployment numbers (Npgsql pool size, PostgreSQL `max_connections`) that S4 will change by moving to Supabase, so it is issue O22 and its own unit, with the re-measurement `load-testing` requires |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **both** — decision 1 departs from ADR-0018's "its Docker image" because no such image resolves, recorded here with the probe evidence. And the measurement forced a new one: [[ADR-0041-limiter-after-authentication]] moves `UseRateLimiter` after `UseAuthentication`, replacing the ordering SPEC-006 wrote and documented in `Program.cs` |
| 2 | new or upgraded dependency? | **yes, and none of it ships** — `gatling-maven-plugin` and `gatling-charts-highcharts`, used only by `tests/load/`, which is not in `MusicRoom.sln` and is not referenced by any shipped project. No change to `Directory.Packages.props` |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration. The seed writes rows through the API and one `UPDATE`, against a throwaway database |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **it exercises them** — the simulations sign in and carry bearer tokens, and the seed sets `is_email_verified`. No production credential is involved: the compose stack's `.env` values are local, and no token or password reaches the report |
| 6 | lowers a severity, deletes or skips a test? | no — and it **adds** one, `EventVoteRateLimitingTests`, for the defect in section 6.2 of the report |
| 7 | touches or copies from `reference/`? | no |

## Files

| Path | Action |
|---|---|
| `tests/load/pom.xml` | create — the plugin, the Gatling version, and nothing that ships |
| `tests/load/src/test/java/musicroom/LoadProfile.java` | create — the shared base: base URL, steps, assertions, the token feeder |
| `tests/load/src/test/java/musicroom/QueueReadSimulation.java` | create — the hot authenticated read |
| `tests/load/src/test/java/musicroom/VoteStormSimulation.java` | create — the contended write of subject V.2.1 |
| `tests/load/src/test/java/musicroom/PlaylistReorderSimulation.java` | create — the contended write of V.2.3 |
| `tests/load/src/test/java/musicroom/HubFanOutSimulation.java` | create — N connections in one group |
| `tests/load/src/test/java/musicroom/LoginBurstSimulation.java` | create — the rate-limited path, which must degrade correctly |
| `tests/load/seed/seed.sh` | create — write the accounts in SQL, then build the event and the playlist through the API |
| `tests/load/seed/tokens.sh` | create — mint one access token per account against a throwaway instance (decision 7) |
| `tests/load/analyse.py` | create — the per-step and per-window tables the Gatling console does not print |
| `tests/load/README.md` | create — how to run one scenario, and what each measures |
| `MusicRoom/src/MusicRoom/WebAPI/Program.cs` | change — the limiter moves after authentication (ADR-0040) |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/EventVoteRateLimitingTests.cs` | create — two accounts, one address, and the second still votes |
| `brain/decisions/ADR-0041-limiter-after-authentication.md` | create — the ordering decision the measurement forced |
| `.gitignore` | change — `tests/load/target/` and `tests/load/seed/out/` |
| `Makefile` | change — `make load` replaces the placeholder that says S9 will bring one |
| `docs/load/REPORT.md` | create — the seven sections `load-testing` requires |
| `docs/SDD.md` | change — §14.4, §16's two V.7 rows, and §17's new O22 |
| `brain/projects/musicroom-backend.md` | change — S9 started |
| `docs/specs/SPEC-023-load-measurement.md` | change — status transitions |

### The Files table walk

**1. Assemblies.** Nothing in `MusicRoom/`. `tests/load/` is a Maven project outside the solution, so a load
dependency can never reach a shipped artifact — which is the only reason adding two of them is cheap.

**2. Boundary types.** None; the simulations are test code.

**3. Invoked?** `make load` runs one or all five.

**4. CLI or package.** The `maven` image, the Gatling plugin, and `psql` inside the running `db` container.

**5. corePackages.** Unchanged.

**What the walk caught.** Three things.
- **The report is the deliverable, not the simulations.** A committed simulation nobody ran proves nothing, so
  this spec is not done until `docs/load/REPORT.md` carries measured numbers from the declared machine.
- **Docker Desktop's VM is not the machine.** The host is an Apple M5 Pro with 15 cores and 24 GiB; the
  containers see whatever the VM was given. Both are reported, because a figure justified by the host's cores
  while the API ran in a 4-core VM is a figure justified by the wrong number.
- **The seed needs the database, not just the API**, which is why it is a script beside the simulations rather
  than a Gatling `before` block.

## Steps

1. `tests/load/pom.xml` and `LoadProfile`: base URL from an environment variable, the ramp steps, the shared
   assertions, and the feeder that reads seeded credentials.
2. `seed.sh`: write N verified accounts in one SQL statement with pgcrypto, create one event with suggestions
   and one playlist with tracks through the API, and write the ids the simulations feed on. `tokens.sh`: mint
   one access token per account against a throwaway instance.
3. The five simulations, each with its own injection profile and its own assertions.
4. `make load`, running one scenario or all five in the `maven` image on the compose network.
5. Warm up, discard the first run, then measure each scenario in steps and record where latency degrades.
6. `docs/load/REPORT.md`: the machine, the topology, the tool and command, the per-scenario numbers, the
   bottleneck with evidence, and the stated figure with its reasoning.
7. `docs/SDD.md` §14.4 and §16, then the documents.

## Verification

```bash
make agent-check   # unchanged: the load project is outside the solution
make load          # the measurement itself
```

| Check | Proves |
|---|---|
| `make load SCENARIO=queue-read` runs and reports | the harness works against the compose stack |
| each simulation's Gatling assertions | the step's own pass condition, in the simulation rather than in prose |
| `docs/load/REPORT.md` section 1 | the machine is declared, host **and** container VM |
| section 5, per scenario, per step | requests/sec and p50 / p95 / p99, never a mean alone |
| section 6 | the bottleneck, with the evidence that identified it |
| section 7 | the concurrent-user figure, and the arithmetic from the measurements to it |
| the login-burst scenario | the limiter refuses rather than fails — `429` separated from error |
| `make agent-check` | the shipped solution is untouched by any of this |

## Done when

- [x] every file in the Files table exists
- [x] `make agent-check` passes, unchanged
- [x] `make load` runs all five scenarios against the compose stack
- [x] `docs/load/REPORT.md` carries measured numbers, the bottleneck, and the stated figure
- [x] `docs/SDD.md` §14.4 and §16's V.7 rows say what was measured
- [x] the defect the measurement found is fixed, with a regression test and an ADR
- [x] the ceiling it found is recorded as O22, not quietly tuned away
- [x] `status: done` in this file
