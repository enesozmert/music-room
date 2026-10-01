#!/usr/bin/env bash
# Measure how far the backend has got, from the repository itself.
#
#   make progress              <- print the summary
#   make progress WRITE=1      <- also write docs/progress/PROGRESS.json
#   ./.agent/progress.sh --json
#
# Everything here is counted, never estimated. Two numbers are read from a column rather than
# inferred, because no amount of grepping can tell them:
#
#   DELIVERY-PLAN slice table   -> State:    merged | in progress | blocked | not started
#   decisions-index             -> Lands in: landed | landed, <slice> | <slice>
#
# Those columns are the contract. Keep them true and this script stays true.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

WRITE=0
QUIET=0
for arg in "$@"; do
  case "$arg" in
    --write) WRITE=1 ;;
    --json)  WRITE=1; QUIET=1 ;;
    *) echo "unknown argument: $arg" >&2; exit 64 ;;
  esac
done

PLAN="docs/DELIVERY-PLAN.md"
SDD="docs/SDD.md"
INDEX="brain/decisions/decisions-index.md"
INVENTORY="docs/api/ENDPOINTS.md"

for f in "$PLAN" "$SDD" "$INDEX" "$INVENTORY"; do
  [ -f "$f" ] || { echo "missing $f" >&2; exit 1; }
done

COMMIT="$(git rev-parse --short HEAD)"
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
TODAY="$(date -u +%Y-%m-%d)"

# --- slices: State column of the slice table, matched on an exact slice id ------------------
slice_state() {
  awk -F'|' -v want="$1" '
    $2 ~ /^ *S[0-9]+[ab]? *$/ {
      id=$2; gsub(/ /,"",id)
      st=$6; gsub(/^ +| +$/,"",st)
      if (st == want) n++
    }
    END { print n+0 }' "$PLAN"
}
SLICES_TOTAL=$(awk -F'|' '$2 ~ /^ *S[0-9]+[ab]? *$/ {n++} END {print n+0}' "$PLAN")
SLICES_MERGED=$(slice_state "merged")
SLICES_PROGRESS=$(slice_state "in progress")
SLICES_BLOCKED=$(slice_state "blocked")
SLICES_IDLE=$(slice_state "not started")

# --- SDD section 16: one row per mandatory requirement -------------------------------------
req_count() {
  awk -v want="$1" '
    /^## 16\./ {inside=1}
    /^## 17\./ {inside=0}
    inside && /^\| [VI]/ {
      n=split($0, c, "|"); st=c[5]
      sub(/ -- .*/, "", st); sub(/ — .*/, "", st)
      gsub(/\*\*/, "", st); gsub(/^ +| +$/, "", st)
      if (st == want) hits++
    }
    END { print hits+0 }' "$SDD"
}
REQ_TOTAL=$(awk '/^## 16\./{i=1} /^## 17\./{i=0} i && /^\| [VI]/{n++} END{print n+0}' "$SDD")
REQ_DONE=$(req_count "done")
REQ_IMPL=$(req_count "implemented")
REQ_PARTLY=$(req_count "partly implemented")
REQ_DESIGNED=$(req_count "designed")
REQ_OTHER=$(( REQ_TOTAL - REQ_DONE - REQ_IMPL - REQ_PARTLY - REQ_DESIGNED ))

# --- SDD section 17: decision debt ---------------------------------------------------------
ISSUES_OPEN=$(awk '/^### Open/{i=1; next} /^### Closed/{i=0} i && /^\| O/{n++} END{print n+0}' "$SDD")
ISSUES_CLOSED=$(awk '/^### Closed/{i=1; next} /^---/{if(i)i=0} i && /^\| [OG]/{n++} END{print n+0}' "$SDD")

# --- ADRs: the Lands in column -------------------------------------------------------------
ADR_TOTAL=$(grep -c '^| \[\[ADR' "$INDEX")
ADR_LANDED=$(awk -F'|' '/^\| \[\[ADR/{v=$5; gsub(/^ +| +$/,"",v); if (v=="landed") n++} END{print n+0}' "$INDEX")
ADR_PARTLY=$(awk -F'|' '/^\| \[\[ADR/{v=$5; gsub(/^ +| +$/,"",v); if (v ~ /^landed,/) n++} END{print n+0}' "$INDEX")
ADR_WAITING=$(( ADR_TOTAL - ADR_LANDED - ADR_PARTLY ))

# --- specs ---------------------------------------------------------------------------------
SPECS_TOTAL=$(ls docs/specs/SPEC-0*.md 2>/dev/null | wc -l | tr -d ' ')
SPECS_DONE=$(grep -l '^status: *done' docs/specs/SPEC-0*.md 2>/dev/null | wc -l | tr -d ' ')

# --- surface and size ----------------------------------------------------------------------
ENDPOINTS_MAPPED=$(grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("' MusicRoom/src --include="*.cs" 2>/dev/null | grep -v '/obj/' | wc -l | tr -d ' ')
ENDPOINTS_INVENTORY=$(grep -cE '^\| *(GET|POST|PUT|PATCH|DELETE) ' "$INVENTORY")
TESTS_DECLARED=$(grep -rhoE '\[(Fact|Theory)\]' MusicRoom/tests --include="*.cs" 2>/dev/null | wc -l | tr -d ' ')
MIGRATIONS=$(ls MusicRoom/src/MusicRoom/Persistence/Migrations/*.cs 2>/dev/null | grep -vc 'Designer\|Snapshot')
PROJECTS=$(find MusicRoom -name '*.csproj' -not -path '*/obj/*' 2>/dev/null | wc -l | tr -d ' ')

# A requirement counts as built when it is done or implemented; partly is counted separately
# rather than folded in at half, because half a row is not a number anyone can defend.
REQ_BUILT=$(( REQ_DONE + REQ_IMPL ))

if [ "$WRITE" -eq 1 ]; then
  OUT="docs/progress/PROGRESS.json"
  cat > "$OUT" <<JSON
{
  "generated": "$TODAY",
  "commit": "$COMMIT",
  "branch": "$BRANCH",
  "slices":       { "total": $SLICES_TOTAL, "merged": $SLICES_MERGED, "in_progress": $SLICES_PROGRESS, "blocked": $SLICES_BLOCKED, "not_started": $SLICES_IDLE },
  "requirements": { "total": $REQ_TOTAL, "built": $REQ_BUILT, "done": $REQ_DONE, "implemented": $REQ_IMPL, "partly": $REQ_PARTLY, "designed": $REQ_DESIGNED, "other": $REQ_OTHER },
  "adrs":         { "total": $ADR_TOTAL, "landed": $ADR_LANDED, "partly": $ADR_PARTLY, "waiting": $ADR_WAITING },
  "issues":       { "open": $ISSUES_OPEN, "closed": $ISSUES_CLOSED },
  "specs":        { "total": $SPECS_TOTAL, "done": $SPECS_DONE },
  "surface":      { "endpoints_mapped": $ENDPOINTS_MAPPED, "endpoints_inventory": $ENDPOINTS_INVENTORY },
  "size":         { "tests_declared": $TESTS_DECLARED, "migrations": $MIGRATIONS, "projects": $PROJECTS }
}
JSON
  [ "$QUIET" -eq 1 ] && { cat "$OUT"; exit 0; }
  echo "wrote $OUT"
fi

pct() { [ "$2" -eq 0 ] && { echo 0; return; }; echo $(( $1 * 100 / $2 )); }

printf '\n  music-room — backend progress\n'
printf '  %s @ %s, %s\n\n' "$BRANCH" "$COMMIT" "$TODAY"
printf '  slices         %2d / %-2d merged   %3d%%   (%d in progress, %d blocked, %d not started)\n' \
  "$SLICES_MERGED" "$SLICES_TOTAL" "$(pct "$SLICES_MERGED" "$SLICES_TOTAL")" "$SLICES_PROGRESS" "$SLICES_BLOCKED" "$SLICES_IDLE"
printf '  requirements   %2d / %-2d built    %3d%%   (%d partly, %d designed, %d pending)\n' \
  "$REQ_BUILT" "$REQ_TOTAL" "$(pct "$REQ_BUILT" "$REQ_TOTAL")" "$REQ_PARTLY" "$REQ_DESIGNED" "$REQ_OTHER"
printf '  ADRs           %2d / %-2d landed   %3d%%   (%d partly, %d waiting on a slice)\n' \
  "$ADR_LANDED" "$ADR_TOTAL" "$(pct "$ADR_LANDED" "$ADR_TOTAL")" "$ADR_PARTLY" "$ADR_WAITING"
printf '  endpoints      %2d / %-2d mapped   %3d%%   (the inventory is the contract, it runs ahead)\n' \
  "$ENDPOINTS_MAPPED" "$ENDPOINTS_INVENTORY" "$(pct "$ENDPOINTS_MAPPED" "$ENDPOINTS_INVENTORY")"
printf '  specs          %2d / %-2d done\n' "$SPECS_DONE" "$SPECS_TOTAL"
printf '  decision debt  %2d open, %d closed\n' "$ISSUES_OPEN" "$ISSUES_CLOSED"
printf '  size           %d test cases declared, %d migrations, %d projects\n\n' "$TESTS_DECLARED" "$MIGRATIONS" "$PROJECTS"
