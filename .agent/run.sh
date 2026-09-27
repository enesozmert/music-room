#!/usr/bin/env bash
# Drive unattended agent cycles defined in docs/prompt-library/PROMPTS.md Part 2.
#
#   make agent-run                     <- recommended; wires permissions and live output
#   AGENT_CMD="claude -p" ./.agent/run.sh 5
#   AGENT_CMD="gemini -p" ./.agent/run.sh 3
#
# A bare `claude -p` cannot ask for permission, so a write it has not been granted is simply
# refused and the cycle ends with nothing on disk. Pass .agent/permissions.json, or use
# `make agent-run`, which does it for you.
#
# Pull the cord at any time:  touch .agent/STOP
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

MAX_CYCLES="${1:-5}"
: "${AGENT_CMD:?set AGENT_CMD, e.g. 'claude -p' or 'gemini -p'}"

# Optional live view. Text output from a non-interactive agent arrives only at the end, so a long
# cycle looks frozen. Set AGENT_FILTER to a command that turns the agent's stream into lines.
AGENT_FILTER="${AGENT_FILTER:-cat}"
mkdir -p .agent/runs

# One cycle at a time. Two concurrent drivers switch branches under each other, and uncommitted
# work from one lands in the other's commit — observed 2026-09-27, when an analyzer change rode
# into an unrelated pull request that way.
LOCK=".agent/.lock"
if [ -e "$LOCK" ] && kill -0 "$(cat "$LOCK" 2>/dev/null)" 2>/dev/null; then
  echo "another cycle is running (pid $(cat "$LOCK")); refusing to start" >&2
  exit 1
fi
echo $$ > "$LOCK"
trap 'rm -f "$LOCK"' EXIT INT TERM

# ADR-0025: a run must start from a clean tree, or the spec commit captures unrelated work.
if [ -n "$(git status --porcelain)" ]; then
  echo "working tree is dirty; commit or stash before an unattended run" >&2
  git status --short >&2
  exit 1
fi

for i in $(seq 1 "$MAX_CYCLES"); do
  if [ -f .agent/STOP ]; then
    echo "STOP file present, halting"
    exit 0
  fi

  log=".agent/runs/$(date +%Y%m%d-%H%M%S)-cycle$i.log"
  echo "== cycle $i -> $log"

  $AGENT_CMD "$(cat <<'PROMPT'
Read AGENTS.md. Run one full cycle of docs/prompt-library/PROMPTS.md Part 2:
stage 1 pick, stage 2 spec, stage 3 implement, stage 4 verify and self-heal, stage 5 close out.
Apply the guardrail block in section 2.8. Honour every stop condition in section 2.7.
End your output with exactly one line: RESULT: pass | blocked | environment | halted | nothing-to-do
PROMPT
)" 2>&1 | eval "$AGENT_FILTER" | tee "$log"

  result=$(grep -oE '^RESULT: [a-z-]+' "$log" | tail -1 | cut -d' ' -f2 || echo unknown)
  echo "-- result: $result"

  case "$result" in
    pass)
      echo "-- branch: $(git branch --show-current)"
      git --no-pager log --oneline -1
      ;;
    blocked|environment|halted|nothing-to-do)
      echo "stopping: $result"
      exit 0
      ;;
    *)
      echo "unrecognised result, stopping for safety"
      exit 1
      ;;
  esac
done

echo "budget of $MAX_CYCLES cycles reached"
