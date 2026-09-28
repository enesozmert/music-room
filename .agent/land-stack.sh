#!/usr/bin/env bash
# Land a stack of open pull requests onto main, bottom first.
#
#   make land                 <- plan only, changes nothing
#   make land READY=1         <- actually land them
#   ./.agent/land-stack.sh --dry-run
#   ./.agent/land-stack.sh --ready --limit 3
#
# A stacked spec branch cannot simply be merged: branch protection requires the head to be up to
# date with main, and each branch still carries the pre-rebase copies of its ancestors. This
# script does what a human otherwise does by hand, in the order that does not lose a pull request.
#
# Passing --ready marks every scheduled pull request ready for review, which is the human review
# signal ADR-0027 asks for. One flag for the batch instead of one command per pull request. It is
# still a human running it: putting this in CI would need ADR-0027 amended first.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

DRY_RUN=1
LIMIT=0
while [ $# -gt 0 ]; do
  case "$1" in
    --ready)   DRY_RUN=0 ;;
    --dry-run) DRY_RUN=1 ;;
    --limit)   shift; LIMIT="${1:-0}" ;;
    *) echo "unknown argument: $1" >&2; exit 64 ;;
  esac
  shift
done

command -v gh >/dev/null || { echo "gh not found" >&2; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "gh is not authenticated" >&2; exit 1; }

# One driver at a time, same lock the cycle runner takes. Landing a stack rewrites branches that
# an unattended cycle may be sitting on.
LOCK=".agent/.lock"
if [ -e "$LOCK" ] && kill -0 "$(cat "$LOCK" 2>/dev/null)" 2>/dev/null; then
  echo "an agent cycle is running (pid $(cat "$LOCK")); refusing to land a stack under it" >&2
  exit 1
fi

WT=".agent/.land-worktree"
REPO="$(pwd)"
git fetch -q origin

# ---------------------------------------------------------------------------
# Order the stack before touching anything.
#
# The order has to be read while the base chain still exists. Retargeting comes next and erases
# it, and a merge deletes the branch a child names as its base — at which point GitHub CLOSES the
# child instead of retargeting it, and a closed pull request whose base branch is gone cannot be
# reopened. #22 was lost exactly that way on 2026-09-28.
# ---------------------------------------------------------------------------
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# macOS ships bash 3.2: no mapfile, no associative arrays. Rows live in a file and lookups are
# awk, which works on every machine the team has.
gh pr list --state open --json number,headRefName,baseRefName,isDraft \
  -q '.[] | [.number, .headRefName, .baseRefName, (.isDraft|tostring)] | @tsv' > "$TMP/prs.tsv"

if [ ! -s "$TMP/prs.tsv" ]; then
  echo "nothing open to land."
  exit 0
fi

field() { awk -F'\t' -v n="$1" -v c="$2" '$1==n{print $c; exit}' "$TMP/prs.tsv"; }
head_of()  { field "$1" 2; }
base_of()  { field "$1" 3; }
draft_of() { field "$1" 4; }
pr_by_head() { awk -F'\t' -v h="$1" '$2==h{print $1; exit}' "$TMP/prs.tsv"; }
scheduled() { grep -qx "$1" "$TMP/order" 2>/dev/null; }

: > "$TMP/order"

# Topological order: a pull request is ready when its base is main, or is the head of one already
# scheduled.
while : ; do
  progressed=0
  while IFS=$'\t' read -r num head base draft; do
    scheduled "$num" && continue
    if [ "$base" = "main" ]; then
      echo "$num" >> "$TMP/order"; progressed=1; continue
    fi
    parent="$(pr_by_head "$base")"
    if [ -n "$parent" ] && scheduled "$parent"; then
      echo "$num" >> "$TMP/order"; progressed=1
    fi
  done < "$TMP/prs.tsv"
  [ "$progressed" -eq 0 ] && break
done

while IFS=$'\t' read -r num head base draft; do
  scheduled "$num" || echo "WARN: #$num (base $base) is not reachable from main; skipped" >&2
done < "$TMP/prs.tsv"

if [ "$LIMIT" -gt 0 ]; then
  head -n "$LIMIT" "$TMP/order" > "$TMP/order.trimmed" && mv "$TMP/order.trimmed" "$TMP/order"
fi

ORDER="$(cat "$TMP/order")"

echo "Plan, bottom first:"
for num in $ORDER; do
  printf '  #%-4s %-55s base=%s draft=%s\n' "$num" "$(head_of "$num")" "$(base_of "$num")" "$(draft_of "$num")"
done

if [ "$DRY_RUN" -eq 1 ]; then
  echo
  echo "Plan only. Re-run with --ready to land these."
  exit 0
fi

# Retarget every scheduled pull request to main now, while all the base branches still exist.
for num in $ORDER; do
  [ "$(base_of "$num")" = "main" ] && continue
  gh pr edit "$num" --base main >/dev/null 2>&1 && echo "#$num retargeted to main"
done

# A dedicated worktree, always detached: a spec branch is often checked out in the main working
# copy, and `git worktree add <branch>` refuses a branch that is checked out elsewhere.
git worktree prune
[ -d "$WT" ] || git worktree add -q --detach "$WT" origin/main

land_one() {
  local pr="$1" branch="$2"
  echo
  echo "=== #$pr $branch"

  cd "$REPO/$WT" || return 1
  git fetch -q origin
  git checkout -q --detach "origin/$branch" || { echo "FAIL: cannot check out origin/$branch"; return 1; }

  # Replay this branch's own commit alone. One spec is one commit (ADR-0025), and everything
  # before it is the pre-rebase copy of an ancestor that already landed under a different sha.
  # Replaying those conflicts them against their own landed twins.
  if ! git rebase --onto origin/main HEAD~1 >/dev/null 2>&1; then
    local unmerged nonpdf
    unmerged=$(git diff --name-only --diff-filter=U)
    nonpdf=$(printf '%s\n' "$unmerged" | grep -v '\.pdf$' | grep -v '^$')
    if [ -n "$nonpdf" ]; then
      echo "STOP: #$pr needs a human. Conflicts outside generated PDFs:"
      printf '  %s\n' $nonpdf
      git rebase --abort
      return 2
    fi
    # A PDF conflict is not a conflict: Markdown is the source, so regenerate and move on.
    ./docs/build-pdf.sh >/dev/null 2>&1
    printf '%s\n' "$unmerged" | while read -r f; do [ -n "$f" ] && git add -- "$f"; done
    git add -A
    GIT_EDITOR=true git rebase --continue >/dev/null 2>&1 || { echo "FAIL: rebase --continue on $branch"; git status --short | head -5; return 1; }
  fi

  # Rebasing changed the Markdown under this commit, so its PDFs are a version behind.
  ./docs/build-pdf.sh >/dev/null 2>&1
  if [ -n "$(git status --porcelain)" ]; then
    git add -A && git commit -q --amend --no-edit
  fi

  git push -q --force-with-lease origin "HEAD:refs/heads/$branch" || { echo "FAIL: push $branch"; return 1; }

  cd "$REPO"
  gh pr ready "$pr" >/dev/null 2>&1

  # A freshly pushed head has no check runs yet, and `gh pr checks --watch` returns immediately
  # when it finds none. Wait for the workflow to register, then watch, then merge; the merge is
  # refused while the rollup is still pending, so retry.
  local state=""
  for _ in 1 2 3 4; do
    sleep 30
    gh pr checks "$pr" --watch --interval 20 >/dev/null 2>&1
    gh pr merge "$pr" --merge --delete-branch >/dev/null 2>&1
    state=$(gh pr view "$pr" --json state -q .state 2>/dev/null)
    [ "$state" = "MERGED" ] && break
  done

  if [ "$state" != "MERGED" ]; then
    echo "STOP: #$pr did not merge (state=$state). Checks:"
    gh pr checks "$pr" 2>&1 | tail -5
    return 3
  fi
  echo "#$pr merged."
  return 0
}

failed=0
for num in $ORDER; do
  land_one "$num" "$(head_of "$num")"
  rc=$?
  if [ "$rc" -ne 0 ]; then
    failed="$num"
    echo
    echo "Stopped at #$num. Everything below it is on main; nothing above it was touched."
    break
  fi
done

cd "$REPO"
git worktree remove --force "$WT" >/dev/null 2>&1
git worktree prune

if [ "$failed" != 0 ]; then
  echo "Remaining, still open:"
  gh pr list --state open --json number,title -q '.[]|"  #\(.number) \(.title)"'
  exit 1
fi

echo
echo "Stack landed. Open pull requests: $(gh pr list --state open --json number -q length)"
echo "Now check the knowledge layer against what main holds: /brain-check, then /sdd-sync."
