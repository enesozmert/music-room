#!/usr/bin/env bash
# Regenerate .agents/skills/ from .claude/commands/.
#
# Two tools, one set of commands. Claude Code reads .claude/commands/*.md; Antigravity reads
# {workspace}/.agents/skills/<name>/SKILL.md and turns each into a /<name> slash command. The
# bodies are the prompts in docs/prompt-library/PROMPTS.md either way, so there is exactly one
# place to change a rule and this script carries it to the second tool.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

SRC=".claude/commands"
DST=".agents/skills"
[ -d "$SRC" ] || { echo "$SRC not found" >&2; exit 1; }

mkdir -p "$DST"
written=0

for f in "$SRC"/*.md; do
  name="$(basename "$f" .md)"
  desc="$(sed -n 's/^description: *//p' "$f" | head -1)"
  hint="$(sed -n 's/^argument-hint: *//p' "$f" | head -1)"
  [ -n "$desc" ] || { echo "no description in $f" >&2; exit 1; }
  [ -n "$hint" ] && desc="$desc. Argument: $hint"

  # Everything after the closing --- of the frontmatter is the prompt.
  body="$(awk 'BEGIN{n=0} /^---$/{n++; next} n>=2{print}' "$f")"

  mkdir -p "$DST/$name"
  {
    printf -- '---\n'
    printf 'name: %s\n' "$name"
    printf 'description: %s\n' "$desc"
    printf -- '---\n\n'
    printf '# %s\n\n' "$name"
    printf '%s\n' "$body"
  } > "$DST/$name/SKILL.md"
  written=$((written + 1))
done

echo "regenerated $written skills in $DST"
echo "a skill added while an agy session is open is invisible to it — restart the session."
