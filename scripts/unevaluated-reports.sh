#!/usr/bin/env bash
# Lists the research reports this repository has not evaluated: added, changed or renamed in the research
# repository's docs/reports/ since a date, with no "Evaluated: <file> @ <commit>" line, for the report's
# current commit, in any issue or PR here (docs/evidence-pipeline.md, "What triggers it"). A plain mention
# in a discussion is not an evaluation.
# Output, one line per unevaluated report: "<path> <current commit> <range> <commits in range>", where the
# range is "<previous marker commit>..<current>", or "<since>..<current>" when the report has no marker yet.
# Every commit in that range is what stage 2 (or the triage) must read, not only the last one.
# Read-only: it fetches the research repository and reads origin/main, never pulls or touches its checkout.
# Two GitHub listing calls in all, never one search per report: the search API allows 30 a minute.
# Usage: scripts/unevaluated-reports.sh [since YYYY-MM-DD]
#   The default is FLOOR, when the marker was introduced: reports older than that were evaluated without
#   leaving one. Always scanning from FLOOR means a long gap between sessions misses nothing. Raise FLOOR in
#   a reviewed change, and only to a date up to which this script reports 0.
set -euo pipefail
shopt -s inherit_errexit
research=${IC2_RESEARCH_DIR:-C:/Users/diego/projects/RE-imperial-conquest-2}
repo=diegoami/imperial_conquest_2
FLOOR=2026-10-01
LIMIT=1000
since=${1:-$FLOOR}
git -C "$research" fetch -q origin main
ref=origin/main

# Every body and conversation comment of the issues and PRs updated since then, one per line. Each listing
# is its own command, so a failed call stops the script instead of silently dropping its markers.
# IC2_MENTIONS_FILE replaces both listings with a file of lines, for testing the matching offline.
if [[ -n ${IC2_MENTIONS_FILE:-} ]]; then
  mentions=$(cat "$IC2_MENTIONS_FILE")
else
  issues=$(gh issue list -R "$repo" --state all --search "updated:>=$since" --limit "$LIMIT" --json body,comments)
  prs=$(gh pr list -R "$repo" --state all --search "updated:>=$since" --limit "$LIMIT" --json body,comments)
  for listing in "$issues" "$prs"; do
    if [[ $(jq length <<<"$listing") -ge $LIMIT ]]; then
      echo "unevaluated-reports: a listing returned $LIMIT rows, so older markers may be cut off; raise FLOOR or LIMIT" >&2
      exit 2
    fi
  done
  mentions=$(jq -r '.[] | .body, .comments[].body' <<<"$issues"; jq -r '.[] | .body, .comments[].body' <<<"$prs")
fi

n=0
while read -r path; do
  [[ -n "$path" ]] && git -C "$research" cat-file -e "$ref:$path" 2>/dev/null || continue
  file=${path##*/}
  commit=$(git -C "$research" log -1 --format=%H "$ref" -- "$path" | cut -c1-7)
  grep -qF -- "Evaluated: $file @ $commit" <<<"$mentions" && continue
  # The newest earlier marker for this report that is an ancestor of the current commit starts the range;
  # with no marker, the range is every commit since the scan's start date.
  from=""
  for c in $(grep -oE -- "Evaluated: ${file//./\\.} @ [0-9a-f]{7}" <<<"$mentions" | awk '{print $NF}' | sort -u || true); do
    git -C "$research" merge-base --is-ancestor "$c" "$commit" 2>/dev/null || continue
    if [[ -z $from ]] || git -C "$research" merge-base --is-ancestor "$from" "$c"; then from=$c; fi
  done
  if [[ -z $from ]]; then
    range="$since..$commit"
    count=$(git -C "$research" rev-list --count --since="$since" "$ref" -- "$path")
  else
    range="$from..$commit"
    count=$(git -C "$research" rev-list --count "$from..$ref" -- "$path")
  fi
  echo "$path $commit $range $count"
  n=$((n + 1))
done < <(git -C "$research" log --since="$since" --diff-filter=AMR --name-only --format= "$ref" -- docs/reports/ | sort -u)
echo "unevaluated: $n (since $since)"
