#!/usr/bin/env bash
# Seeds the data the five simulations feed on, against a running compose stack.
#
# A load test cannot complete email verification: the token is only in the application log, because O17
# has no mail provider. So accounts are registered through the real endpoint — which keeps the password
# and the BCrypt cost honest — and then verified with one UPDATE. What is measured is capacity, not the
# registration flow (SPEC-023 decision 3).
#
# Everything it writes lands in seed/out/, which is git-ignored: ids belong to one throwaway database.
set -euo pipefail

BASE_URL="${MUSICROOM_BASE_URL:-http://localhost:8080}"
DB_CONTAINER="${MUSICROOM_DB_CONTAINER:-music-room-db-1}"
ACCOUNTS="${MUSICROOM_LOAD_ACCOUNTS:-220}"
SUGGESTIONS="${MUSICROOM_LOAD_SUGGESTIONS:-25}"
TRACKS="${MUSICROOM_LOAD_TRACKS:-25}"
PASSWORD='LoadTest-Passw0rd!'

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$HERE/out"
mkdir -p "$OUT"
: > "$OUT/accounts.txt"

headers=(-H 'Content-Type: application/json'
         -H 'X-Client-Platform: LoadTest'
         -H 'X-Client-Device: seed'
         -H 'X-Client-App-Version: 0.0.0-load')

say() { printf '%s\n' "$*" >&2; }

say "seed: $ACCOUNTS verified accounts, hashed in the database"

# No account is registered through the API, on purpose. `POST /auth/registrations` allows ten per IP per hour
# (SPEC-006), and a load seed that needs hundreds would either trip that limiter or require raising it — and
# raising a limiter on the system under test is how a capacity number stops describing the system that
# shipped. So the rows are written directly, with BCrypt hashes that pgcrypto produces at the same cost the
# application uses, which `BCrypt.Net` verifies because the `$2a$` prefix is the format it reads.
#
# Email verification is set here too: the mailed token is only in the application log, because O17 has no
# mail provider. What is being measured is capacity, not registration (SPEC-023 decision 3).
docker exec -i "$DB_CONTAINER" psql -U musicroom -d musicroom -v ON_ERROR_STOP=1 -q <<SQL
CREATE EXTENSION IF NOT EXISTS pgcrypto;

INSERT INTO users (id, email, password_hash, display_name, is_email_verified, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(),
       'load-' || n || '@loadtest.invalid',
       crypt('$PASSWORD', gen_salt('bf', 12)),
       'Load ' || n,
       true,
       now(),
       now()
FROM generate_series(1, $ACCOUNTS) AS n
ON CONFLICT (email) DO NOTHING;
SQL

for index in $(seq 1 "$ACCOUNTS"); do
  printf '%s\n' "load-${index}@loadtest.invalid" >> "$OUT/accounts.txt"
done

FIRST_EMAIL="load-1@loadtest.invalid"

token_for() {
  curl -fsS -X POST "$BASE_URL/api/v1/auth/sessions" "${headers[@]}" \
    -d "{\"email\":\"$1\",\"password\":\"$PASSWORD\"}" |
    sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p'
}

TOKEN="$(token_for "$FIRST_EMAIL")"
auth=(-H "Authorization: Bearer $TOKEN")

# Reads one id out of a JSON body. Python rather than sed: a `sed` pattern that silently matches nothing
# leaves an empty seed file and a simulation that fails much later with a confusing message.
id_of() { python3 -c 'import json,sys; print(json.load(sys.stdin)[sys.argv[1]])' "$1"; }

say "seed: one public event, licence Everyone, open now"
starts="$(date -u -v-1H '+%Y-%m-%dT%H:%M:%SZ' 2>/dev/null || date -u -d '-1 hour' '+%Y-%m-%dT%H:%M:%SZ')"
ends="$(date -u -v+6H '+%Y-%m-%dT%H:%M:%SZ' 2>/dev/null || date -u -d '+6 hours' '+%Y-%m-%dT%H:%M:%SZ')"

EVENT_ID="$(curl -fsS -X POST "$BASE_URL/api/v1/events" "${headers[@]}" "${auth[@]}" \
  -d "{\"name\":\"Load test event\",\"startsAtUtc\":\"$starts\",\"endsAtUtc\":\"$ends\",\"visibility\":0,\"licence\":0}" |
  id_of eventId)"
printf '%s\n' "$EVENT_ID" > "$OUT/event-id.txt"

: > "$OUT/suggestions.txt"
for index in $(seq 1 "$SUGGESTIONS"); do
  video="$(printf 'ld%09d' "$index" | cut -c1-11)"
  curl -fsS -X POST "$BASE_URL/api/v1/events/$EVENT_ID/tracks" "${headers[@]}" "${auth[@]}" \
    -d "{\"youtubeVideoId\":\"$video\"}" | id_of suggestionId >> "$OUT/suggestions.txt"
done

say "seed: one playlist, licence Everyone, with $TRACKS tracks"
PLAYLIST_ID="$(curl -fsS -X POST "$BASE_URL/api/v1/playlists" "${headers[@]}" "${auth[@]}" \
  -d '{"name":"Load test playlist","visibility":0,"licence":0}' | id_of playlistId)"
printf '%s\n' "$PLAYLIST_ID" > "$OUT/playlist-id.txt"

: > "$OUT/tracks.txt"
for index in $(seq 1 "$TRACKS"); do
  video="$(printf 'pl%09d' "$index" | cut -c1-11)"
  etag="$(curl -fsS -D - -o /dev/null "$BASE_URL/api/v1/playlists/$PLAYLIST_ID/tracks" "${headers[@]}" "${auth[@]}" |
    sed -n 's/^[Ee][Tt]ag: *\(.*\)$/\1/p' | tr -d '\r')"
  curl -fsS -X POST "$BASE_URL/api/v1/playlists/$PLAYLIST_ID/tracks" "${headers[@]}" "${auth[@]}" \
    -H "If-Match: $etag" -d "{\"youtubeVideoId\":\"$video\"}" | id_of playlistTrackId >> "$OUT/tracks.txt"
done

say "seed: done — $(wc -l < "$OUT/accounts.txt" | tr -d ' ') accounts, event $EVENT_ID, playlist $PLAYLIST_ID"
