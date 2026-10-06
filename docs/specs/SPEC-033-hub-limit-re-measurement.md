---
id: SPEC-033
title: The re-measurement ADR-0030 commissioned — is 60 invocations a minute generous or tight
status: approved        # draft | approved | in-progress | done | blocked
tier: B                 # A = routine, agent may self-approve if the gate is clean; B = human gate
slice: S9               # which DELIVERY-PLAN slice this belongs to
requirements: [V.7, V.6]
decisions: [ADR-0030, ADR-0018, ADR-0023]
standards: [load-testing, signalr-realtime, testing]
approved_by: enesozmert # gate 1 answered by the owner on 2026-10-06 — see the escalation
escalation: |
  **The measurement ran and one number does not hold. Stopped at `draft` by decision 5, which is this spec's own
  stop condition.** Nothing was changed: ADR-0030 reserves an amendment to itself.

  Machine: the one `docs/load/REPORT.md` §1 declares — Apple M5 Pro, 15 cores, 24 GiB, Docker VM 15 CPUs and
  7.75 GiB, one API container and one PostgreSQL container, generator on the same machine. Figures are from the
  second run; the first was discarded as a warm-up and gave the same three counts. Full section:
  `docs/load/REPORT.md` §5.6.

  | Number | Verdict | Observed |
  |---|---|---|
  | 60 invocations per connection per minute | **generous** | a realistic client reaches **24 a minute**; the 61st inside the window is refused with `HubException`, connection still open |
  | 20 concurrent connections per address | **not reached**, and tighter than it reads | the per-account ceiling refuses first; at 3 connections per device this is **6 devices** on one network |
  | 5 concurrent connections per account | **wrong** | probe 3 held **5** and was refused the 6th; **one device costs 3** |

  **What is wrong, and why it is the ADR's own reason rather than a rate.** The ADR justified five as *"a phone,
  a tablet and a reconnect in flight, with room to spare"* — that counts **devices**. The ceiling counts
  **connections**, and `PlaylistHub`, `EventHub` and `ControlHub` are three separate connections at three paths.
  Probe 3 opened all three for one account and all three were accepted, so:

  - one device costs **3** connections and fits, with two spare;
  - a phone **and** a tablet cost **6** and the sixth is refused;
  - the three-device case the rationale describes costs **7 to 9**.

  The limiter is working exactly as SPEC-026 built it — the sixth attempt was refused at the hub filter, the
  authoritative half of the ceiling. It is the value that does not match the intent.

  **Proposed value: 10 concurrent connections per account.** Three devices at three hubs is 9, plus one for a
  reconnect the server has not yet reaped — the "room to spare" the rationale asked for, at the connection
  granularity the code actually counts. A second observation for the same amendment, not proposed as a number
  here: at 3 per device, **20 per address is 6 devices**, and the venue case ADR-0030 named is a room sharing one
  wifi.

  Both rate verdicts are published regardless, per decision 6: §5.6 of the report carries them, so the
  invocation measurement is not withheld because the ceiling escalated.

  **What is needed:** an ADR-0030 amendment setting connections-per-account, and a decision on whether
  connections-per-address should move with it. Then a one-line change to
  `HubConnectionLimits.DefaultConnectionsPerUser` and its test, as its own unit.

  **Owner's answer, 2026-10-06: the amendment is written and this unit carries the change.** ADR-0030 now says
  12 per account and 60 per address, with the arithmetic beside each. Gate 1 therefore becomes a
  human-answered **yes** — the ADR changed — and the Files table gains the three paths the new numbers need.
  Folding the change in here rather than opening a second unit is what `load-testing` asks for: the change and
  its measurement in one place, and the measurement is already in this branch.

  Four answers, binding:

  1. **12 per account, not the 10 you proposed.** Your arithmetic stopped one connection short of its own
     reason. `ClientTimeoutInterval` is 60 seconds, so a phone that changes network drops all three
     connections at once and the server reaps none of them for up to a minute: it holds three stale and three
     new. Reconnect headroom is a whole device — 9 + 3 — or the ceiling refuses the reconnect it exists to
     allow.
  2. **60 per address.** Your second observation is right and it moves with the first: 20 was 6 devices on a
     shared wifi, and the venue case is the one the rationale named explicitly. 20 devices × 3 hubs.
  3. **60 invocations a minute stays.** 24 measured against 60 is 2.5× headroom, and the burst that would
     touch it is a rejoin after a reconnect — refusing that refuses a recovery, not an attack.
  4. **Re-run probe 3 after the change** and put both counts in the report: 12 accepted and the 13th refused.
     A changed default with no run behind it is the thing this spec exists to prevent. Do not re-run the rate
     probes; nothing about them moved.

  Not in this unit, recorded instead: the ceiling counts connections and means devices, so a fourth hub would
  make 12 three devices again. The handshake already carries `X-Client-Device`, so it could count what it
  means. Open that as **O25** in `docs/SDD.md` §16 and leave it open — it is a design change, not a number.
---

# Re-measuring the hub limits

## Goal

[[ADR-0030-hub-rate-limits-and-origin]] commissioned this unit in as many words: its three numbers are *"a
starting point with a measurement behind the shape rather than the value"*, and *"the first run after this lands
says whether 60 is generous or tight"*. SPEC-026 landed them. This measures them.

After this spec `docs/load/REPORT.md` carries a section that states, for each of the three numbers separately
and on a declared machine: the invocation rate a realistic client actually reaches, the rate an aggressive one
reaches before the limiter refuses, and how many concurrent connections one real client holds. Each number gets
its own verdict — generous, tight, or wrong — with the observation behind it.

## Out of scope

- **Changing any of the three numbers.** ADR-0030: *"Changing one is an amendment here, not a code review
  argument."* If the measurement says a number is wrong, this spec stops at `draft` with the figure and the
  proposed value in its escalation block, and the owner amends the ADR. That is gate 1 and not this unit's.
- **Anything under `MusicRoom/src/`.** No production code changes. If a source file needs to change, this spec
  is wrong and the run stops.
- **Re-measuring REST capacity.** SPEC-023 did that, and nothing in SPEC-026 touched a REST path. The report's
  existing sections stay as they are.
- **The functional proof that the limits refuse at all.** `HubLimitTests` and `HubOriginNegotiateTests` already
  hold that. This unit measures where a real client sits relative to them.

## Decisions taken under the blanket approval

| # | Decision | Why, and what was rejected |
|---|---|---|
| 1 | **A new simulation beside `HubFanOutSimulation`, not an extension of it.** `tests/load/src/test/java/musicroom/HubLimitProbeSimulation.java`, reusing `LoadProfile` for the base URL, the token feeder, the protocol and the ramp. | They measure different things with incompatible shapes. Fan-out injects 200 connections and one writer to time a broadcast reaching a listener; this one needs few connections and many invocations on each, and a `repeat` count that is the measurement itself. Folding them together would make both numbers harder to read and would change a scenario the report already cites. `LoadProfile` is **used, never forked** — one place still decides the base URL, the tokens and the client headers |
| 2 | **Three probes, one per number, reported separately.** A paced client, an unpaced client, and a client that opens every hub it would really open. | The report's verdict is per number: "60 is generous" and "5 is tight" can both be true, and a single aggregate would hide whichever matters |
| 3 | **A realistic client is defined before it is measured**, and the definition goes in the report: one connection per hub it needs, a join on entering a screen, a leave on leaving it, and otherwise listening. | ADR-0030's own words — *"a client that joins a group and then mostly listens"*. A measurement against an undefined client measures the generator |
| 4 | **The verdict is observation plus the machine**, as `load-testing` requires: *"Report the number that was measured on the machine that was declared."* | A rate without the machine beside it is not a measurement. The report already declares the machine in §1 and this section cites it rather than restating it |
| 5 | **Probe 3 has its own stop condition, independent of any rate.** A realistic client holding **more than two** connections stops this unit at `draft`, however comfortable the invocation numbers are. | ADR-0030 justified five per account as *"a phone, a tablet and a reconnect in flight"* — which counts **devices**, while the ceiling counts **connections**, and the three hubs are three connections at three paths. If a device costs three slots, the number is wrong for the reason the ADR itself gave, not for a rate anybody measured. The escalation then carries the observed count per client, the arithmetic for three devices, and a proposed value; the amendment stays the owner's (binding instruction, 2026-10-06) |
| 6 | **The two rate verdicts are published either way.** A `draft` status on the ceiling does not make the invocation measurement unpublished work. | The report is the deliverable, and half a measurement withheld because the other half escalated is a figure somebody has to take twice |
| 7 | **`make load SCENARIO=hub-limits` runs it**, mapped in the `Makefile` beside the five that exist. | The harness's own entry point. A simulation that can only be run by hand is one nobody reruns after a change |

## Gate

| # | Question | Answer |
|---|---|---|
| 1 | new/changed ADR, or contradicts one? | **yes, answered by the owner on 2026-10-06** — the measurement found a number wrong, the spec stopped at `draft` exactly as decision 1 required, and the owner amended ADR-0030 to 12 per account and 60 per address. The amendment is in the Files table below |
| 2 | new or upgraded dependency? | no — Gatling 3.11.5 and the `gatling-maven-plugin` are already pinned in `tests/load/pom.xml`, which is outside `MusicRoom.sln` |
| 3 | schema change beyond this spec, or destructive migration? | no — no migration. The existing `seed.sh` writes rows to a throwaway database and this unit does not change it |
| 4 | changes `docs/api/ENDPOINTS.md`? | no |
| 5 | touches authn, authz, tokens, secrets, audit? | **no, and it exercises them** — the simulation carries bearer tokens that `tokens.sh` minted, and it provokes the hub limiter. It changes no authentication, authorisation, token, secret or audit code, adds no credential, and reads none: the token file is produced by a script this unit does not touch, against a throwaway database |
| 6 | lowers a severity, deletes or skips a test? | no |
| 7 | touches or copies from `reference/`? | no |

Six are no and gate 1 is a yes the owner answered, so this is Tier B: `status: approved`,
`approved_by: enesozmert`.

## Files

| Path | Action |
|---|---|
| `tests/load/src/test/java/musicroom/HubLimitProbeSimulation.java` | create — the three probes, reusing `LoadProfile` |
| `Makefile` | change — `LOAD_SIMULATION_hub-limits` beside the five existing scenarios |
| `docs/load/REPORT.md` | change — a section per number, with the observation, the machine and the verdict |
| `docs/SDD.md` | change — §13.3's realtime table gains the measured figures beside the decided ones; §16's V.7 row says the re-measurement happened |
| `docs/DELIVERY-PLAN.md` | change — S9 |
| `brain/projects/musicroom-backend.md` | change — S9 |
| `brain/decisions/ADR-0030-hub-rate-limits-and-origin.md` | change — the amendment: 12 per account, 60 per address, the invocation limit unchanged, with the arithmetic and the `ClientTimeoutInterval` reasoning |
| `MusicRoom/src/MusicRoom/WebAPI/Hubs/HubConnectionLimits.cs` | change — `DefaultConnectionsPerUser` 5 to 12, `DefaultConnectionsPerAddress` 20 to 60, and the doc comments that state the arithmetic |
| `MusicRoom/tests/MusicRoom.WebAPI.Tests/HubLimitTests.cs` | change — the ceiling tests carry the new numbers; the suite proves the ceiling refuses, and at which count |
| `docs/specs/SPEC-033-hub-limit-re-measurement.md` | change — status transitions |

### The Files table walk

**1. Assemblies, and the references that implies.** None. `tests/load/` is a Maven project outside
`MusicRoom.sln`, so nothing it contains can reach a shipped assembly and no `.csproj` changes. That is also why
gate 2 is a no: its dependencies are pinned in `tests/load/pom.xml`, which no .NET project reads.

**2. Types crossing an assembly boundary.** None. One Java class, `public` because Gatling instantiates a
simulation reflectively, in its own file as the other five are.

**3. Does anything invoke it?** `make load SCENARIO=hub-limits`, via the map in the `Makefile`. Without that
entry the simulation is a file the plugin can only be pointed at by hand — decision 5.

**4. A CLI, a tool manifest, or an unpinned package?** No. The `maven:3-eclipse-temurin-21` image and the
plugin are what SPEC-023 established, and `make load` already mounts `~/.m2`.

**5. Do the four corePackages contain what I assume?** They are assumed to contain nothing relevant, and that
is checked rather than guessed: `Core.Application` holds one source file, the other three hold none. This unit
touches no C# at all.

**The four answers this unit turns on:**

- **Extend or add beside.** Added beside — decision 1. `HubFanOutSimulation` measures the interval between a
  broadcast arriving at one of 200 listeners; this unit measures invocations on a handful of connections.
  Different injection profiles, different assertions, and the report already cites the fan-out numbers, so
  changing that scenario would invalidate a published measurement. `LoadProfile` is reused and not forked.
- **`docs/load/REPORT.md` is the output**, and it states the machine (§1, already declared), the scenario, the
  observed rate a realistic client reaches, and **a separate verdict on each of the three numbers**. A figure
  with no machine beside it is not a measurement.
- **A wrong number is not fixed here.** If the measurement contradicts 60, 5 or 20, this spec's status goes
  back to `draft`, the escalation carries the measured figure and the value I would propose, and the run stops.
  ADR-0030 reserves the amendment to itself.
- **Nothing in `MusicRoom/src/` changes.** The Files table contains no path under it. If the measurement cannot
  be taken without touching one, the spec is wrong and the run stops rather than improvising.

**What the walk caught.** One thing, and it is why probe 3 exists. A real client does not hold *one* connection:
`PlaylistHub`, `EventHub` and `ControlHub` are three separate SignalR connections at `/hubs/playlists`,
`/hubs/events` and `/hubs/control`. A phone watching a playlist, following an event and listening for control
grants holds three. ADR-0030's rationale for five per account reads *"a phone, a tablet and a reconnect in
flight"* — which counts devices, not connections. Probe 3 measures what one client actually opens, because if a
device costs three slots then five per account is one device and a spare, not three devices.

## Steps

1. `HubLimitProbeSimulation`: three scenarios — paced client, unpaced client, and multi-hub client — each
   reusing `LoadProfile.tokens()`, `LoadProfile.protocol()` and the handshake shape the fan-out simulation
   established.
2. `Makefile`: `LOAD_SIMULATION_hub-limits := musicroom.HubLimitProbeSimulation`.
3. Bring the stack up, seed, mint tokens, run the scenario, and discard the first run as a warm-up the way
   SPEC-023 did.
4. Write the section in `docs/load/REPORT.md`: observation, machine, verdict per number.
5. If every number holds: `docs/SDD.md`, the delivery plan, the project card, then `make agent-check`.
6. If any number does not hold: write the report section anyway, then `status: draft`, escalation with the
   figure and the proposal, and stop. Probe 3 showing more than two connections per client is such a case on
   its own, whatever the rates say.

## Verification

```bash
make up && ./tests/load/seed/seed.sh && ./tests/load/seed/tokens.sh
make load SCENARIO=hub-limits
./tests/load/analyse.py
make agent-check
```

| Check | Proves |
|---|---|
| `make load SCENARIO=hub-limits` runs the new simulation | the `Makefile` entry works and the scenario reaches the hub through the real handshake |
| probe 1, the paced client | the invocation rate a realistic client reaches, which is the number "60 a minute" has to be judged against |
| probe 2, the unpaced client | where the limiter actually refuses, and that it refuses with `HubException` rather than by dropping the connection |
| probe 3, the multi-hub client | how many concurrent connections **one** client holds, which is what the 5-per-account ceiling is spent on |
| `docs/load/REPORT.md` | a verdict per number, each with its observation and the declared machine beside it |
| `make agent-check` | nothing in the solution moved: no C# changed, so the test count and the drift guards are untouched |

## Done when

- [ ] `HubLimitProbeSimulation` exists and `make load SCENARIO=hub-limits` runs it
- [ ] `docs/load/REPORT.md` states the observation, the machine and a separate verdict for 60, 5 and 20
- [ ] no file under `MusicRoom/src/` is touched
- [ ] `make agent-check` passes
- [ ] the first run is discarded as a warm-up, as SPEC-023 did, and the reported figures come from the second
- [ ] every figure in the report has the declared machine beside it
- [ ] `status: done`, **or** `status: draft` with a measured figure and a proposed value in escalation
