#!/usr/bin/env bash
#
# The deployment smoke — SPEC-053.
#
# What a deployment can get wrong that no test can catch: a variable nobody set, a proxy that hides the
# scheme, a database that is not migrated, a key that is not published. This checks those, against any
# base URL, in a few seconds.
#
# **It is not a product test, deliberately.** The 720 tests in `make agent-check` prove the rules against a
# real engine and a real database. Repeating them in shell against production would prove the weaker half
# twice and leave accounts, playlists and events behind in a database nothing resets — and send real mail on
# every run. What this proves is configuration.
#
# So it is read-mostly, and it creates nothing. With no credentials it checks the anonymous surface. With
# SMOKE_EMAIL and SMOKE_PASSWORD for one already-verified account, it also signs in, reads the profile and
# runs one catalog search, then ends its own session so a run leaves no trace. The account is reused: this
# script never registers, so it never sends mail.
#
# **No token, key or password is printed.** A smoke log that carries a working credential is a credential in
# a terminal buffer, a CI artifact and a screenshot.
#
# Usage: make smoke                        (against the composed stack)
#        make smoke-live                   (against the deployment)
#        scripts/smoke.sh https://host     (against anything)

set -euo pipefail

readonly BASE_URL="${1:-${SMOKE_BASE_URL:-http://localhost:8080}}"
readonly API="${BASE_URL}/api/v1"

# A fixed query, so a second run inside the cache lifetime costs no quota at all: the server answers it from
# L1 or L2. A random one would spend 100 of the 10,000 daily units on every run — ADR-0023.
readonly SEARCH_QUERY="${SMOKE_QUERY:-daft punk}"

readonly HEADERS=(
  -H "X-Client-Platform: smoke"
  -H "X-Client-Device: ci"
  -H "X-Client-App-Version: 1.0.0"
)

checks=0
failures=0

say() { printf '%s\n' "$*"; }

check() {
  local name="$1" expected="$2" actual="$3"

  checks=$((checks + 1))

  if [ "$expected" = "$actual" ]; then
    say "  ok      $name"
  else
    say "  FAILED  $name — expected ${expected}, got ${actual}"
    failures=$((failures + 1))
  fi
}

# For a step the rest of the run is built on. A failed sign-in makes every later assertion a message about
# an empty string, which buries the one finding that mattered.
require() {
  local before="$failures"

  check "$@"

  # Only this check's own failure stops the run. Counting every failure so far would abort at the next
  # `require` after any earlier finding, and report a passing step as the reason.
  if [ "$failures" -gt "$before" ]; then
    say ""
    say "stopping here: the steps after this one all depend on it."
    exit 1
  fi
}

# Says what is wrong and what to do about it, for the findings that have a known cause.
advise() { say "          -> $*"; }

# Reads one member out of a JSON body on stdin. Absent answers the empty string, so a missing member is a
# failed comparison rather than a crashed script.
member() {
  python3 -c '
import json, sys
body = json.load(sys.stdin)
value = body.get(sys.argv[1])
print("" if value is None else ("true" if value is True else "false" if value is False else value))
' "$1"
}

readonly BODY_FILE="$(mktemp)"
trap 'rm -f "$BODY_FILE"' EXIT

request() {
  curl -s -o "$BODY_FILE" -w '%{http_code}' "$@"
}

body() { cat "$BODY_FILE"; }

say "smoke: ${BASE_URL}"

# --- 1. the process is up ------------------------------------------------------------------------
#
# Generous, because a free-tier instance sleeps after fifteen idle minutes and a cold start is slow.
# A sleeping host is not a broken host, and a smoke that reports one as broken teaches people to
# ignore it — ADR-0046.

say ""
say "liveness"

status=000

for attempt in $(seq 1 30); do
  status="$(request "${BASE_URL}/health")"

  if [ "$status" = 200 ]; then
    break
  fi

  if [ "$attempt" = 1 ]; then
    say "  (waiting — a free-tier instance may be waking up)"
  fi

  sleep 4
done

require "the host answers /health" 200 "$status"

# --- 2. the dependencies it reports ---------------------------------------------------------------

say ""
say "readiness"

status="$(request "${BASE_URL}/health/ready")"
require "readiness answers" 200 "$status"

database="$(body | member database)"
check "the database is reachable" "Healthy" "$database"

if [ "$database" != "Healthy" ]; then
  advise "DATABASE_URL is wrong, the database is asleep, or migrations were never applied."
fi

# Reported, never fatal — SPEC-053 decision 4. Only search touches the provider, so a host without a key
# serves everything else. This is the check that would have found the 503 on 2026-10-17 in one request.
catalog="$(body | member catalog)"
check "the catalog reports its state" "reported" "$([ -n "$catalog" ] && echo reported || echo absent)"
say "          catalog: ${catalog:-absent}"

if [ "$catalog" = "Degraded" ]; then
  advise "YOUTUBE_API_KEY is not set on this host. Track search answers 503 until it is."
  advise "Everything else is unaffected, which is why readiness is still 200."
fi

# --- 3. what it publishes about itself ------------------------------------------------------------
#
# The issuer is the one value a deployment gets wrong silently. Behind a TLS-terminating proxy without
# forwarded headers the host believes it is plain HTTP and signs tokens with an `http://` issuer that no
# client will accept — the live defect of 2026-10-16, fixed in #106.

say ""
say "discovery"

status="$(request "${BASE_URL}/.well-known/openid-configuration")"
require "the discovery document answers" 200 "$status"

issuer="$(body | member issuer)"
check "its issuer is this host, scheme included" "$BASE_URL" "$issuer"

if [ "$issuer" != "$BASE_URL" ]; then
  advise "A scheme mismatch means the proxy's headers are not trusted:"
  advise "set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true on the host."
fi

status="$(request "${BASE_URL}/.well-known/jwks.json")"
require "the key set answers" 200 "$status"

check "it publishes a key" "at-least-one" "$(body | python3 -c '
import json, sys
print("at-least-one" if json.load(sys.stdin).get("keys") else "none")')"

check "it publishes no private exponent" absent "$(body | python3 -c '
import json, sys
keys = json.load(sys.stdin)["keys"]
print("present" if any(any(part in key for part in ("d", "p", "q")) for key in keys) else "absent")')"

# --- 4. one account, reused, and nothing left behind ----------------------------------------------

if [ -z "${SMOKE_EMAIL:-}" ] || [ -z "${SMOKE_PASSWORD:-}" ]; then
  say ""
  say "signed-in checks skipped: SMOKE_EMAIL and SMOKE_PASSWORD are not set."
  say "  Set them to one already-verified account to also check the database schema and the catalog."
else
  say ""
  say "signed in as the smoke account"

  status="$(request -X POST "${API}/auth/sessions" \
    "${HEADERS[@]}" \
    -H 'Content-Type: application/json' \
    --data "$(python3 -c '
import json, os, sys
json.dump({"email": os.environ["SMOKE_EMAIL"], "password": os.environ["SMOKE_PASSWORD"]}, sys.stdout)')")"
  require "sign-in answers" 200 "$status"

  access="$(body | member accessToken)"
  require "it returns an access token" "present" "$([ -n "$access" ] && echo present || echo absent)"

  readonly AUTHORIZED=(-H "Authorization: Bearer ${access}")

  # A read that touches the account's own rows, which is what proves the schema is migrated rather than
  # merely that the database accepts a connection.
  status="$(request "${API}/me" "${AUTHORIZED[@]}")"
  check "the own profile reads" 200 "$status"

  if [ "$status" = 500 ]; then
    advise "A 500 on a plain read usually means the schema is behind the code: run the migration job."
  fi

  # The only path that can reach the expensive provider call, and the one that was dead on the live
  # deployment. 503 here is a configuration finding, not a crash.
  status="$(request --get "${API}/tracks" "${AUTHORIZED[@]}" --data-urlencode "q=${SEARCH_QUERY}")"
  check "track search answers" 200 "$status"

  if [ "$status" = 503 ]; then
    advise "The catalog is unavailable. The host's log says which cause it was: no provider key,"
    advise "a key the provider rejects (invalid, expired or restricted), the daily quota, or the"
    advise "provider itself. Search for \"YouTube\" in the log; one warning line names it."
  fi

  # The session this run opened is closed by this run. A smoke that accumulates sessions in a production
  # database is a smoke that changes the thing it measures.
  status="$(request -X DELETE "${API}/auth/sessions/current" "${HEADERS[@]}" "${AUTHORIZED[@]}")"
  check "the run ends its own session" 204 "$status"
fi

# --- summary --------------------------------------------------------------------------------------

say ""

if [ "$failures" -eq 0 ]; then
  say "${checks} checks, all passing."
  exit 0
fi

say "${checks} checks, ${failures} failing."
exit 1
