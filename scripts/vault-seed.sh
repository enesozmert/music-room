#!/usr/bin/env bash
#
# The vault seeder — SPEC-054.
#
# Writes this deployment's secrets into an Infisical project's environment over the API, so nobody has
# to type ten values into a web form. Idempotent: it creates what is missing and updates what exists,
# so running it twice is a no-op and rotating one secret is the same command with one changed line.
#
# **No value is ever printed** — not on success, not in an error, not in a URL. The script refuses to
# run traced, because `set -x` would echo every secret into a terminal buffer and any CI log.
#
# It writes only the names in ALLOWED below. The source file also holds the five INFISICAL_* values
# that open the vault, and those must stay outside it — ADR-0029.
#
# Usage: make vault-seed FILE=.env.prod
#        FILE=.env.prod scripts/vault-seed.sh --dry-run

set -euo pipefail

# Decision 2. `$-` carries `x` when this shell is tracing, which catches `bash -x scripts/vault-seed.sh`
# and an exported `SHELLOPTS=xtrace`. A parent shell's own `set -x` does not reach a child process, so
# there is nothing to catch in that case and nothing leaks either.
case "$-" in
  *x*)
    echo "vault-seed: refusing to run with tracing on — it would print every secret." >&2
    exit 2
    ;;
esac

readonly ALLOWED=(
  DATABASE_URL
  MAIL_API_KEY
  MAIL_FROM
  MAIL_FROM_NAME
  OIDC_SIGNING_KEY
  OIDC_SIGNING_KEY_PREVIOUS
  YOUTUBE_API_KEY
  GOOGLE_CLIENT_ID
  GOOGLE_CLIENT_SECRET
  GOOGLE_DISCOVERY_URL
  GOOGLE_ISSUER
  CORS_ALLOWED_ORIGINS
)

dry_run=false

for argument in "$@"; do
  case "$argument" in
    --dry-run) dry_run=true ;;
    *)
      echo "vault-seed: unknown argument ${argument}. Only --dry-run is accepted." >&2
      exit 2
      ;;
  esac
done

# Decision 4: named, never defaulted. Seeding production from the development stack's .env is how a
# development key reaches a production host.
if [ -z "${FILE:-}" ]; then
  echo "vault-seed: FILE is required — e.g. make vault-seed FILE=.env.prod" >&2
  echo "            .env.* is git-ignored, which is where a production file belongs — ADR-0026." >&2
  exit 2
fi

if [ ! -f "$FILE" ]; then
  echo "vault-seed: ${FILE} does not exist." >&2
  exit 2
fi

# The file is the only place values come from, and they never leave this shell.
set -a
# shellcheck disable=SC1090
. "$FILE"
set +a

for name in INFISICAL_SITE_URL INFISICAL_CLIENT_ID INFISICAL_CLIENT_SECRET \
  INFISICAL_PROJECT_ID INFISICAL_ENVIRONMENT; do
  eval "value=\${${name}:-}"

  if [ -z "$value" ]; then
    echo "vault-seed: ${name} is not set in ${FILE}. All five are required — SPEC-047." >&2
    exit 2
  fi
done

readonly SITE_URL="${INFISICAL_SITE_URL%/}"
readonly SECRET_PATH="${INFISICAL_SECRET_PATH:-/}"

echo "vault-seed: ${SITE_URL}, project ${INFISICAL_PROJECT_ID}, environment ${INFISICAL_ENVIRONMENT}"

# --- the names, before anything is sent -----------------------------------------------------------
#
# Reported up front so a missing one is seen before a write begins rather than halfway through.

present=()
skipped=()

for name in "${ALLOWED[@]}"; do
  eval "value=\${${name}:-}"

  if [ -n "$value" ]; then
    present+=("$name")
  else
    skipped+=("$name")
  fi
done

if [ "${#skipped[@]}" -gt 0 ]; then
  echo ""
  echo "absent from ${FILE}, so not written (an empty secret boots the host and fails later):"

  for name in "${skipped[@]}"; do
    echo "  skip    ${name}"
  done
fi

if [ "${#present[@]}" -eq 0 ]; then
  echo ""
  echo "vault-seed: nothing to write — ${FILE} holds none of the names this script knows." >&2
  exit 1
fi

if [ "$dry_run" = true ]; then
  echo ""
  echo "dry run — nothing is written. What a real run would send:"

  for name in "${present[@]}"; do
    echo "  would write  ${name}"
  done

  exit 0
fi

# --- the machine identity's token ------------------------------------------------------------------
#
# The same login the application performs at start-up — SPEC-047. A body file rather than a variable so
# a `curl` failure cannot interleave the token into an error message.

readonly BODY_FILE="$(mktemp)"
trap 'rm -f "$BODY_FILE"' EXIT

status="$(curl -s -o "$BODY_FILE" -w '%{http_code}' \
  -X POST "${SITE_URL}/api/v1/auth/universal-auth/login" \
  -H 'Content-Type: application/json' \
  --data-binary @<(python3 -c '
import json, os, sys
json.dump({
    "clientId": os.environ["INFISICAL_CLIENT_ID"],
    "clientSecret": os.environ["INFISICAL_CLIENT_SECRET"],
}, sys.stdout)'))"

if [ "$status" != 200 ]; then
  echo ""
  echo "vault-seed: the vault refused the machine identity with ${status}." >&2

  # The vault's own `message` and nothing else from the body — the same bounded reason the application
  # carries, because "no identity with that client id" and "that secret is wrong" need different fixes.
  python3 -c '
import json, sys
try:
    body = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(0)
message = body.get("message")
if isinstance(message, str):
    print("            the vault says: " + message[:200], file=sys.stderr)
' "$BODY_FILE" || true

  echo "            Check INFISICAL_CLIENT_ID — it is the identity's **client id**, not its" >&2
  echo "            identity id — and that the identity is a member of the project." >&2
  exit 1
fi

TOKEN="$(python3 -c '
import json, sys
print(json.load(open(sys.argv[1])).get("accessToken", ""))' "$BODY_FILE")"
readonly TOKEN

if [ -z "$TOKEN" ]; then
  echo "vault-seed: the vault's login answer carried no access token." >&2
  exit 1
fi

# --- the writes -------------------------------------------------------------------------------------
#
# Decision 3: create, then update when the name is already there. The value travels in a JSON body
# built by python3 from the environment, so it never appears in an argument list that `ps` can read.

echo ""

failures=0

send() {
  local method="$1" name="$2"

  curl -s -o "$BODY_FILE" -w '%{http_code}' \
    -X "$method" "${SITE_URL}/api/v3/secrets/raw/${name}" \
    -H "Authorization: Bearer ${TOKEN}" \
    -H 'Content-Type: application/json' \
    --data-binary @<(SECRET_NAME="$name" python3 -c '
import json, os, sys
name = os.environ["SECRET_NAME"]
json.dump({
    "workspaceId": os.environ["INFISICAL_PROJECT_ID"],
    "environment": os.environ["INFISICAL_ENVIRONMENT"],
    "secretPath": os.environ["SECRET_PATH"],
    "secretValue": os.environ[name],
}, sys.stdout)')
}

export SECRET_PATH

for name in "${present[@]}"; do
  status="$(send POST "$name")"

  case "$status" in
    200 | 201)
      echo "  created  ${name}"
      continue
      ;;
  esac

  # Already there. Infisical answers 400 or 409 depending on version, so both lead to the update
  # rather than one of them being treated as a failure.
  if [ "$status" = 400 ] || [ "$status" = 409 ]; then
    status="$(send PATCH "$name")"

    if [ "$status" = 200 ]; then
      echo "  updated  ${name}"
      continue
    fi
  fi

  echo "  FAILED   ${name} — the vault answered ${status}"

  python3 -c '
import json, sys
try:
    body = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(0)
message = body.get("message")
if isinstance(message, str):
    print("           the vault says: " + message[:200])
' "$BODY_FILE" || true

  failures=$((failures + 1))
done

echo ""

if [ "$failures" -eq 0 ]; then
  echo "${#present[@]} secret(s) in the vault. Nothing above is a value, by design."
  echo "Next: the five INFISICAL_* variables on the host, and only then SECRET_PROVIDER=infisical."
  exit 0
fi

echo "${failures} of ${#present[@]} failed."
echo "A 403 on a write means the identity's role is read-only in this project — that is the posture"
echo "the application wants, so grant write for the seed and take it back afterwards."
exit 1
