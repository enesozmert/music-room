#!/usr/bin/env bash
# PreToolUse guard for Edit / Write / NotebookEdit.
#
# Mechanical enforcement of the three write rules that have no legitimate exception, so they
# survive a prompt the model misreads. Everything else stays a matter of judgement and review.
#
# Rules: AGENTS.md, CLAUDE.md, docs/prompt-library/PROMPTS.md section 2.8.
# Exit 2 blocks the tool call and shows stderr to the model.
set -euo pipefail

input=$(cat)
path=$(printf '%s' "$input" | jq -r '.tool_input.file_path // .tool_input.notebook_path // empty')

[ -n "$path" ] || exit 0

case "$path" in
  */reference/*|reference/*)
    echo "reference/ is read-only study material. Read the pattern, then write your own under MusicRoom/. AGENTS.md, subject IV.1." >&2
    exit 2
    ;;
  */.env|.env)
    echo ".env holds secrets and is never read, written or committed. Change .env.example instead, and name the key rather than its value." >&2
    exit 2
    ;;
  *.pdf)
    echo "PDFs are generated artifacts. Markdown is the source of truth: edit the .md, then run docs/build-pdf.sh." >&2
    exit 2
    ;;
esac

exit 0
