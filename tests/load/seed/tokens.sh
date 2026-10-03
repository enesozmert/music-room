#!/usr/bin/env bash
# Mints one access token per seeded account, into seed/out/tokens.txt.
#
# Why this is not part of a simulation: `POST /auth/sessions` allows twenty attempts per IP per five
# minutes (SPEC-006). Every virtual user comes from one address, so a scenario that signed its users in
# would measure the limiter instead of the endpoint under test, and raising the limiter on the system
# under measurement is how a capacity number stops describing the system that shipped.
#
# So the tokens are minted against a throwaway second instance of the same image, started with a raised
# sign-in limit and its own in-process limiter state, published on another port and stopped again. The
# measured instance keeps the approved numbers. Both read the same `JWT_SIGNING_KEY` from `.env`, so the
# token one mints is a token the other accepts (SPEC-023 decision 7).
#
# Access tokens live fifteen minutes (`JWT_ACCESS_TOKEN_MINUTES`). Re-run this before a measurement
# session rather than reusing an hour-old file.
set -euo pipefail

MINTER_PORT="${MUSICROOM_MINTER_PORT:-18080}"
MINTER_NAME="${MUSICROOM_MINTER_NAME:-music-room-token-minter}"
PASSWORD='LoadTest-Passw0rd!'
PARALLEL="${MUSICROOM_MINT_PARALLEL:-8}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../../.." && pwd)"
OUT="$HERE/out"

test -s "$OUT/accounts.txt" || {
  echo "tokens.sh: no $OUT/accounts.txt. Run seed.sh first." >&2
  exit 1
}

say() { printf '%s\n' "$*" >&2; }

stop_minter() { docker rm -f "$MINTER_NAME" >/dev/null 2>&1 || true; }
trap stop_minter EXIT

stop_minter

say "tokens: starting the minter on port $MINTER_PORT, sign-in limit raised for it alone"
(
  cd "$ROOT"
  docker compose run --rm --detach --no-deps --name "$MINTER_NAME" \
    --publish "$MINTER_PORT:8080" \
    -e RATE_LIMIT_SESSIONS_PERMITS=100000 \
    -e RATE_LIMIT_SESSIONS_WINDOW_SECONDS=60 \
    api >/dev/null
)

for _ in $(seq 1 60); do
  if curl -fsS -o /dev/null "http://localhost:$MINTER_PORT/health/ready" 2>/dev/null; then break; fi
  sleep 1
done
curl -fsS -o /dev/null "http://localhost:$MINTER_PORT/health/ready" || {
  echo "tokens.sh: the minter did not become ready on port $MINTER_PORT." >&2
  exit 1
}

say "tokens: minting one access token per account, $PARALLEL at a time"

mint_one() {
  curl -fsS -X POST "http://localhost:$1/api/v1/auth/sessions" \
    -H 'Content-Type: application/json' \
    -H 'X-Client-Platform: LoadTest' \
    -H 'X-Client-Device: seed' \
    -H 'X-Client-App-Version: 0.0.0-load' \
    -d "{\"email\":\"$2\",\"password\":\"$3\"}" |
    python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])'
}
export -f mint_one

# One line per account, in the same order, so a token and an account index still correspond.
xargs -P "$PARALLEL" -I{} bash -c "mint_one '$MINTER_PORT' '{}' '$PASSWORD'" \
  < "$OUT/accounts.txt" > "$OUT/tokens.txt"

minted="$(wc -l < "$OUT/tokens.txt" | tr -d ' ')"
accounts="$(wc -l < "$OUT/accounts.txt" | tr -d ' ')"

test "$minted" = "$accounts" || {
  echo "tokens.sh: minted $minted tokens for $accounts accounts. The file is incomplete." >&2
  exit 1
}

say "tokens: done — $minted tokens, valid for fifteen minutes"
