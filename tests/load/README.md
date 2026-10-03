# tests/load — the capacity harness

Five Gatling simulations, the data they feed on, and a per-step report the Gatling console does not print.
Built by SPEC-023; the measured numbers live in `docs/load/REPORT.md`.

This project is **outside `MusicRoom.sln`**. Maven resolves `gatling-maven-plugin` and
`gatling-charts-highcharts`, and neither can reach a shipped artifact, which is the only reason adding them
was cheap.

## Running it

```bash
make up                                  # the system under test: one API, one PostgreSQL
./tests/load/seed/seed.sh                # 220 accounts, one event with suggestions, one playlist with tracks
./tests/load/seed/tokens.sh              # one access token per account — re-run before each session
make load SCENARIO=queue-read            # one scenario
make load                                # all five
./tests/load/analyse.py                  # per-step numbers for a closed-model scenario
./tests/load/analyse.py --windows        # per-window numbers for fan-out and login
```

`make load` runs Gatling inside `maven:3-eclipse-temurin-21` on the host network, with `~/.m2` mounted so a
second run does not re-download Maven Central. `MUSICROOM_BASE_URL` overrides the target, which defaults to
`http://localhost:8080`.

Everything the seed writes lands in `seed/out/`, which is git-ignored: those ids belong to one throwaway
database.

## The scenarios

| `SCENARIO=` | Class | What it measures |
|---|---|---|
| `queue-read` | `QueueReadSimulation` | `GET /events/{id}/queue`, the hot authenticated read, whose cost is a `GROUP BY` over the vote rows |
| `vote-storm` | `VoteStormSimulation` | `PUT …/vote` from many accounts on one event — subject V.2.1 |
| `reorder` | `PlaylistReorderSimulation` | `GET` for an `ETag` then `PATCH` with `If-Match`, all aimed at the head of one list — subject V.2.3 |
| `fan-out` | `HubFanOutSimulation` | 200 SignalR connections in one group, with one writer, measuring the interval between broadcasts arriving |
| `login` | `LoginBurstSimulation` | `POST /auth/sessions` past its limit: the refusal is the measurement |

Every closed-model scenario walks the same ladder — `LoadProfile.STEPS`, five steps of twenty seconds — so
two scenarios' numbers can be compared. `fan-out` and `login` are open-model, because connections and arrival
rate are what they ramp.

## Two things that look like workarounds and are not

**No scenario signs in.** `POST /auth/sessions` allows twenty attempts per IP per five minutes (SPEC-006), and
a load generator is one IP. `tokens.sh` mints one token per account against a **throwaway second instance** of
the same image, started with a raised sign-in limit and its own limiter state, published on port 18080 and
stopped again. The measured instance keeps the approved numbers, and both read the same signing key, so one
mints what the other accepts. Tokens live fifteen minutes — re-run `tokens.sh` before a measurement session
rather than reusing an old file.

**Accounts are seeded in SQL.** Registration allows ten per IP per hour. `seed.sh` writes the rows with
pgcrypto's `crypt(…, gen_salt('bf', 12))`, which produces the `$2a$12$` hashes BCrypt.Net verifies at the cost
the application uses, so a sign-in still pays the real price. Raising a limiter on the system under
measurement was rejected: a figure measured with the controls turned down does not describe what shipped.

## Reading a result

A scenario's own Gatling assertions are its pass condition — error rate at or under 1%, and a p95 ceiling per
request. Beyond that:

- `412` on a move and `429` on a login are **expected answers**, not failures. Only `5xx` is a failure.
- `analyse.py` percentiles are over successful responses only, so read the `err%` column beside them.
- The first run after a container start is a warm-up. Discard it.
