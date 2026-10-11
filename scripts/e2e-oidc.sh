#!/usr/bin/env bash
#
# The end-to-end check — SPEC-046.
#
# Drives the whole identity surface against a running stack over real HTTP: register, verify, sign in,
# authorize, exchange, read UserInfo, rotate, revoke. Every assertion is a thing a client does, and every
# failure names the step rather than a status code in isolation.
#
# It is not a replacement for the suite. The 664 tests in `make test` prove the rules; this proves that the
# composed stack — Kestrel, PostgreSQL, the middleware order, the configuration a container actually gets —
# serves them. Those are different claims, and only the second one is made here.
#
# **No token, code, key or password is ever printed.** A smoke log that carries a working credential is a
# credential in a terminal buffer, a CI artifact and a screenshot.
#
# Usage: make e2e            (starts the stack, applies migrations, runs this)
#        scripts/e2e-oidc.sh (against a stack already up, with the e2e overlay)

set -euo pipefail

readonly BASE_URL="${E2E_BASE_URL:-http://localhost:8080}"
readonly CLIENT_ID="${E2E_CLIENT_ID:-musicroom-e2e}"
readonly REDIRECT_URI="${E2E_REDIRECT_URI:-https://e2e.musicroom.invalid/callback}"
readonly PASSWORD="${E2E_PASSWORD:-correct-horse-battery-staple-42}"
readonly EMAIL="e2e-$(date +%s)-$RANDOM@musicroom.invalid"
readonly STATE="the-clients-own-state"
readonly NONCE="the-clients-own-nonce"

readonly HEADERS=(
  -H "X-Client-Platform: e2e"
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
# an empty string, which buries the one finding that mattered under twenty that did not.
require() {
  check "$@"

  if [ "$failures" -gt 0 ]; then
    say ""
    say "stopping here: the steps after this one all depend on it."
    exit 1
  fi
}

# Reads one member out of a JSON body on stdin. Absent answers the empty string, so a missing claim is a
# failed comparison rather than a crashed script.
member() {
  python3 -c '
import json, sys
body = json.load(sys.stdin)
value = body.get(sys.argv[1])
print("" if value is None else ("true" if value is True else "false" if value is False else value))
' "$1"
}

absent() {
  python3 -c '
import json, sys
print("absent" if sys.argv[1] not in json.load(sys.stdin) else "present")
' "$1"
}

# The status and the body of one request, the body in a file so a status check never consumes it.
readonly BODY_FILE="$(mktemp)"
trap 'rm -f "$BODY_FILE"' EXIT

request() {
  curl -s -o "$BODY_FILE" -w '%{http_code}' "$@"
}

body() { cat "$BODY_FILE"; }

# --- 0. the stack answers ------------------------------------------------------------------------

say "waiting for ${BASE_URL}/health/ready"

for attempt in $(seq 1 60); do
  if curl -fsS "${BASE_URL}/health/ready" >/dev/null 2>&1; then
    break
  fi

  if [ "$attempt" = 60 ]; then
    say "the stack never became ready. \`docker compose logs api\` has the reason."
    exit 1
  fi

  sleep 2
done

say ""
say "discovery"

status="$(request "${BASE_URL}/.well-known/openid-configuration")"
check "the discovery document answers" 200 "$status"

issuer="$(body | member issuer)"
check "its issuer is this host" "$BASE_URL" "$issuer"

# Every URL it advertises, called. The rule is that a client may assume each one exists, and this is the
# only place that rule is checked against a stack rather than against a test host.
advertised="$(body | python3 -c '
import json, sys
document = json.load(sys.stdin)
print("\n".join(
    value for name, value in document.items()
    if (name.endswith("_endpoint") or name == "jwks_uri") and isinstance(value, str)))')"

for url in $advertised; do
  code="$(curl -s -o /dev/null -w '%{http_code}' "$url")"
  check "advertised ${url#"$BASE_URL"} is not 404" "not-404" "$([ "$code" = 404 ] && echo 404 || echo not-404)"
done

status="$(request "${BASE_URL}/.well-known/jwks.json")"
check "the key set answers" 200 "$status"
check "it publishes no private exponent" absent "$(body | python3 -c '
import json, sys
keys = json.load(sys.stdin)["keys"]
print("present" if any(any(part in key for part in ("d", "p", "q")) for key in keys) else "absent")')"

readonly JWKS_KIDS="$(body | python3 -c '
import json, sys
print(" ".join(key.get("kid", "") for key in json.load(sys.stdin)["keys"]))')"

# --- 1. an account, end to end ------------------------------------------------------------------

say ""
say "registration and verification"

status="$(request -X POST "${BASE_URL}/api/v1/auth/registrations" "${HEADERS[@]}" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"${EMAIL}\",\"password\":\"${PASSWORD}\",\"displayName\":\"End To End\"}")"
require "registration is accepted" 202 "$status"

# The logging sender writes the token to the application log, which is the only place it exists outside the
# database's digest of it — and the reason this overlay uses that sender rather than the real one.
verification_token="$(docker compose logs --no-log-prefix api 2>/dev/null \
  | python3 -c '
import json, sys
token = ""
for line in sys.stdin:
    line = line.strip()
    if not line.startswith("{"):
        continue
    try:
        entry = json.loads(line)
    except ValueError:
        continue
    if "VerificationToken" in entry:
        token = entry["VerificationToken"]
print(token)')"

require "the verification token reached the log" present \
  "$([ -n "$verification_token" ] && echo present || echo absent)"

status="$(request -X POST "${BASE_URL}/api/v1/auth/email-verifications" "${HEADERS[@]}" \
  -H 'Content-Type: application/json' \
  -d "{\"token\":\"${verification_token}\"}")"
require "the address is verified" 204 "$status"

status="$(request -X POST "${BASE_URL}/api/v1/auth/sessions" "${HEADERS[@]}" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"${EMAIL}\",\"password\":\"${PASSWORD}\"}")"
require "sign-in answers a pair" 200 "$status"

first_party_access="$(body | member accessToken)"
first_party_refresh="$(body | member refreshToken)"

# --- 2. the authorization code flow -------------------------------------------------------------

say ""
say "the authorization code flow"

verifier="$(openssl rand -base64 48 | tr '+/' '-_' | tr -d '=\n' | cut -c1-64)"
challenge="$(printf '%s' "$verifier" | openssl dgst -sha256 -binary | openssl base64 | tr '+/' '-_' | tr -d '=\n')"

authorize_url="${BASE_URL}/oauth2/authorize?response_type=code&client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_URI}&scope=openid%20email&state=${STATE}&nonce=${NONCE}&code_challenge=${challenge}&code_challenge_method=S256"

status="$(curl -s -o /dev/null -w '%{http_code}' "$authorize_url")"
check "authorize without a token is refused" 401 "$status"

location="$(curl -s -o /dev/null -D - "$authorize_url" -H "Authorization: Bearer ${first_party_access}" \
  | awk 'tolower($1) == "location:" { print $2 }' | tr -d '\r')"

require "authorize redirects to the registered URI" present \
  "$([ -n "$location" ] && echo present || echo absent)"
check "it redirects to the registered URI and no other" "${REDIRECT_URI}" "${location%%\?*}"

code="$(printf '%s' "$location" | sed -n 's/.*[?&]code=\([^&]*\).*/\1/p')"
returned_state="$(printf '%s' "$location" | sed -n 's/.*[?&]state=\([^&]*\).*/\1/p')"

check "the state comes back untouched" "$STATE" "$returned_state"
check "a code was issued" present "$([ -n "$code" ] && echo present || echo absent)"

status="$(request -X POST "${BASE_URL}/oauth2/token" "${HEADERS[@]}" \
  -d "grant_type=authorization_code&client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_URI}&code=${code}&code_verifier=${verifier}")"
require "the code is exchanged" 200 "$status"

granted_access="$(body | member access_token)"
granted_refresh="$(body | member refresh_token)"
id_token="$(body | member id_token)"
check "the grant is the scope that was asked for" "openid email" "$(body | member scope)"

# The ID token, read the way a client reads it: the header names a key the JWKS publishes, and the claims
# are the ones ADR-0031 lists. The signature itself is proven by `IdTokenIssuerTests`; what is new here is
# that the key this stack signs with is the key this stack publishes.
id_header="$(printf '%s' "$id_token" | cut -d. -f1 | python3 -c '
import base64, json, sys
raw = sys.stdin.read().strip()
print(json.dumps(json.loads(base64.urlsafe_b64decode(raw + "=" * (-len(raw) % 4)))))')"
id_claims="$(printf '%s' "$id_token" | cut -d. -f2 | python3 -c '
import base64, json, sys
raw = sys.stdin.read().strip()
print(json.dumps(json.loads(base64.urlsafe_b64decode(raw + "=" * (-len(raw) % 4)))))')"

check "the ID token is RS256" RS256 "$(printf '%s' "$id_header" | member alg)"

id_kid="$(printf '%s' "$id_header" | member kid)"
kid_is_published=absent

for published in $JWKS_KIDS; do
  if [ "$published" = "$id_kid" ]; then
    kid_is_published=present
  fi
done

check "its kid is one the JWKS publishes" present "$kid_is_published"

check "its issuer is this host" "$BASE_URL" "$(printf '%s' "$id_claims" | member iss)"
check "its audience is the client" "$CLIENT_ID" "$(printf '%s' "$id_claims" | member aud)"
check "it echoes the nonce" "$NONCE" "$(printf '%s' "$id_claims" | member nonce)"
check "it carries auth_time" present \
  "$([ -n "$(printf '%s' "$id_claims" | member auth_time)" ] && echo present || echo absent)"

status="$(request -X POST "${BASE_URL}/oauth2/token" "${HEADERS[@]}" \
  -d "grant_type=authorization_code&client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_URI}&code=${code}&code_verifier=${verifier}")"
check "a spent code is refused" 400 "$status"
check "and says invalid_grant" invalid_grant "$(body | member error)"

# --- 3. UserInfo, and the grant it filters on ---------------------------------------------------

say ""
say "UserInfo"

status="$(request "${BASE_URL}/oauth2/userinfo" -H "Authorization: Bearer ${granted_access}")"
check "UserInfo answers the granted session" 200 "$status"
check "the email scope gives the address" "$EMAIL" "$(body | member email)"
check "and says it is verified" true "$(body | member email_verified)"
check "the profile scope was not asked for, so no name" absent "$(body | absent name)"

status="$(request "${BASE_URL}/oauth2/userinfo" -H "Authorization: Bearer ${first_party_access}")"
check "a first-party session gets the full claim set" 200 "$status"
check "including the name" "End To End" "$(body | member name)"

status="$(request "${BASE_URL}/oauth2/userinfo")"
check "UserInfo without a token is refused" 401 "$status"

# --- 4. rotation and revocation -----------------------------------------------------------------

say ""
say "rotation and revocation"

status="$(request -X POST "${BASE_URL}/oauth2/token" "${HEADERS[@]}" \
  -d "grant_type=refresh_token&refresh_token=${granted_refresh}")"
check "the refresh grant rotates" 200 "$status"

rotated_refresh="$(body | member refresh_token)"
rotated_access="$(body | member access_token)"
check "the refresh token changed" changed \
  "$([ "$rotated_refresh" != "$granted_refresh" ] && echo changed || echo same)"
check "no ID token is asserted on a refresh" absent "$(body | absent id_token)"

status="$(request "${BASE_URL}/oauth2/userinfo" -H "Authorization: Bearer ${rotated_access}")"
check "the rotated session kept its narrow grant" absent "$(body | absent name)"

status="$(request -X POST "${BASE_URL}/oauth2/revoke" "${HEADERS[@]}" \
  -d "token=${rotated_refresh}&token_type_hint=refresh_token")"
check "revocation answers 200" 200 "$status"

status="$(request -X POST "${BASE_URL}/oauth2/token" "${HEADERS[@]}" \
  -d "grant_type=refresh_token&refresh_token=${rotated_refresh}")"
check "a revoked session cannot rotate" 400 "$status"

status="$(request -X POST "${BASE_URL}/oauth2/revoke" "${HEADERS[@]}" -d "token=$(openssl rand -hex 16)")"
check "revoking an unknown token still answers 200" 200 "$status"

status="$(request -X POST "${BASE_URL}/oauth2/revoke" "${HEADERS[@]}" \
  -d "token=${first_party_access}&token_type_hint=access_token")"
check "an access token hint is refused" 400 "$status"
check "and says unsupported_token_type" unsupported_token_type "$(body | member error)"

status="$(request -X POST "${BASE_URL}/oauth2/token" "${HEADERS[@]}" \
  -d "grant_type=refresh_token&refresh_token=${first_party_refresh}")"
check "the refused hint revoked nothing" 200 "$status"

status="$(request -X POST "${BASE_URL}/oauth2/token" "${HEADERS[@]}" -d "grant_type=password&username=${EMAIL}&password=${PASSWORD}")"
check "the password grant does not exist" 400 "$status"
check "and says unsupported_grant_type" unsupported_grant_type "$(body | member error)"

# --- the verdict ---------------------------------------------------------------------------------

say ""

if [ "$failures" -gt 0 ]; then
  say "${failures} of ${checks} checks failed."
  exit 1
fi

say "${checks} checks passed against ${BASE_URL}."
