#!/usr/bin/env python3
"""Per-step numbers out of a Gatling run, which the console summary does not print.

Gatling reports one global summary and one per-request summary for a whole run. A capacity report needs
the numbers per concurrency step, because the question is where latency degrades and not what the average
of every step was. The steps are fixed-length and known (SPEC-023: five steps of twenty seconds), so each
request is attributed to the step its start timestamp falls in.

Usage: ./analyse.py [--windows] [run-directory]   (default: the most recent under target/gatling)

`--windows` labels the buckets by elapsed time instead of by concurrency. The open-model scenario ramps
connections rather than holding a step, so a concurrency label would be a guess there.
"""
import pathlib
import statistics
import sys

STEPS = [10, 25, 50, 100, 200]
STEP_SECONDS = 20


def percentile(values, fraction):
    """The value at `fraction` of a sorted sample, nearest-rank, which is what Gatling reports."""
    ordered = sorted(values)
    index = min(len(ordered) - 1, int(round(fraction * len(ordered) + 0.5)) - 1)
    return ordered[index]


def main():
    arguments = [argument for argument in sys.argv[1:] if argument != "--windows"]
    by_window = "--windows" in sys.argv

    gatling = pathlib.Path(__file__).parent / "target" / "gatling"
    directory = (
        pathlib.Path(arguments[0])
        if arguments
        else max((path for path in gatling.iterdir() if path.is_dir()), key=lambda path: path.stat().st_mtime)
    )

    run_start = None
    buckets = {}

    for line in (directory / "simulation.log").read_text().splitlines():
        fields = line.split("\t")

        if fields[0] == "RUN":
            run_start = int(fields[3])
        elif fields[0] == "REQUEST" and run_start is not None:
            name, start, end, status = fields[2], int(fields[3]), int(fields[4]), fields[5]
            step = (start - run_start) // (STEP_SECONDS * 1000)

            if 0 <= step and (by_window or step < len(STEPS)):
                bucket = buckets.setdefault((step, name), {"ok": [], "ko": 0})

                if status == "OK":
                    bucket["ok"].append(end - start)
                else:
                    bucket["ko"] += 1

    print(f"{directory.name}\n")
    label = "window" if by_window else "users"
    print(f"{label:>7} {'request':<22} {'count':>7} {'err%':>6} {'req/s':>7} {'p50':>6} {'p95':>6} {'p99':>6}")

    for (step, name), bucket in sorted(buckets.items()):
        latencies, failed = bucket["ok"], bucket["ko"]
        total = len(latencies) + failed

        bucket_label = f"{step * STEP_SECONDS}-{(step + 1) * STEP_SECONDS}s" if by_window else str(STEPS[step])

        print(
            f"{bucket_label:>7} {name:<22} {total:>7} {100 * failed / total:>6.2f} {total / STEP_SECONDS:>7.1f} "
            f"{statistics.median(latencies) if latencies else 0:>6.0f} "
            f"{percentile(latencies, 0.95) if latencies else 0:>6.0f} "
            f"{percentile(latencies, 0.99) if latencies else 0:>6.0f}"
        )


if __name__ == "__main__":
    main()
