#!/usr/bin/env bash
# Big milestone review, run directly from a CLI (not through the harness).
#   scripts/review-milestone.sh <tag> [--base <prev-tag>] [--cli "<command>"] [--post] [--dry-run]
# Builds the brief from docs/milestone-review.md plus the milestone's claims, runs a headless CLI
# on it in the current checkout, saves rendered/review-<tag>.md and, with --post, comments it on
# the review PR (or the milestone issue). Default CLI: `claude -p`; any command that reads the
# prompt on stdin and prints the review works (e.g. --cli "codex exec -", --cli "opencode run").
set -euo pipefail

tag="${1:?usage: review-milestone.sh <tag> [--base <prev-tag>] [--cli cmd] [--post] [--dry-run]}"; shift
base=""; cli="claude -p"; post=0; dry=0
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base="$2"; shift 2 ;;
    --cli) cli="$2"; shift 2 ;;
    --post) post=1; shift ;;
    --dry-run) dry=1; shift ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done

cd "$(git rev-parse --show-toplevel)"
[ -n "$base" ] || base="$(git tag --sort=-v:refname | grep -vx "$tag" | head -1 || true)"
cand="$(git rev-parse --verify -q "$tag^{commit}" || git rev-parse HEAD)"
mkdir -p rendered
brief="rendered/review-brief-$tag.md"
out="rendered/review-$tag.md"

{
  echo "# Milestone review: $tag"
  echo
  echo "You are the independent reviewer. Work in this checkout. Do not modify, commit, push or merge."
  echo "Candidate commit: $cand. Baseline: ${base:-none (first release)}."
  echo "Build and test: dotnet build IC2.sln --nologo -v q; dotnet test IC2.sln --nologo -v q."
  echo
  echo "## Process and output format"
  sed -n '/^## The review brief/,$p' docs/milestone-review.md
  echo
  echo "## The milestone's claims"
  if command -v gh >/dev/null 2>&1; then
    # REST only: gh issue/pr/repo commands use GraphQL, which the cloud sandbox may block
    ms="$(gh api "repos/{owner}/{repo}/milestones?state=all&per_page=100" --jq ".[]|select(.title==\"$tag\")|.number" 2>/dev/null || true)"
    if [ -n "$ms" ]; then
      gh api "repos/{owner}/{repo}/issues?milestone=$ms&state=all&per_page=100" \
        --jq '.[]|select(.pull_request|not)|"- #\(.number) [\(.state)] \(.title)"'
    else echo "(no GitHub milestone named $tag)"; fi
  fi
  grep -n -A40 -i "$tag" docs/release-plan.md | head -80 || true
  echo
  echo "## The diff to review"
  [ -n "$base" ] && git diff --stat "$base..$cand" | tail -40
  echo "Read the diff yourself with: git diff ${base:-$(git rev-list --max-parents=0 HEAD | tail -1)}..$cand -- <path>"
  echo
  echo "Never use closing keywords (fixes/closes #N) in the review."
} > "$brief"

echo "brief: $brief ($(wc -c < "$brief") bytes), cli: $cli"
[ "$dry" -eq 1 ] && exit 0

$cli < "$brief" | tee "$out"
echo "review saved: $out"

if [ "$post" -eq 1 ]; then
  owner="$(gh api "repos/{owner}/{repo}" --jq .owner.login)"
  pr="$(gh api "repos/{owner}/{repo}/pulls?head=$owner:review/$tag&state=open" --jq '.[0].number // empty' 2>/dev/null || true)"
  if [ -n "$pr" ]; then gh api "repos/{owner}/{repo}/issues/$pr/comments" -F body=@"$out" --jq .html_url
  else echo "no review PR for review/$tag; review left in $out (post it by hand)"; fi
fi
