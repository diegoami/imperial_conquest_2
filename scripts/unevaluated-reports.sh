#!/usr/bin/env bash
# Lists the research reports this repository has not evaluated: added or changed in the research
# repository's docs/reports/ since a date, with no "Evaluated: <file> @ <commit>" line, for the
# report's last commit, in any issue or PR here (docs/evidence-pipeline.md, "What triggers it").
# A plain mention in a discussion is not an evaluation. Read-only.
# Two GitHub listing calls in all, never one search per report: the search API allows 30 a minute.
# Usage: scripts/unevaluated-reports.sh [since YYYY-MM-DD]
#   The default is FLOOR, when the marker was introduced: reports older than that were evaluated
#   without leaving one. Always scanning from FLOOR means a long gap between sessions misses nothing;
#   raise FLOOR in a reviewed change when the scan grows slow.
set -euo pipefail
research=${IC2_RESEARCH_DIR:-C:/Users/diego/projects/RE-imperial-conquest-2}
repo=diegoami/imperial_conquest_2
FLOOR=2026-10-01
since=${1:-$FLOOR}
git -C "$research" pull -q --ff-only

# Every body and comment of the issues and PRs updated since then, one per line.
mentions=$( { gh issue list -R "$repo" --state all --search "updated:>=$since" --limit 1000 --json body,comments
              gh pr list -R "$repo" --state all --search "updated:>=$since" --limit 1000 --json body,comments; } |
  jq -r '.[] | .body, .comments[].body')

n=0
while read -r path; do
  [[ -n "$path" && -f "$research/$path" ]] || continue
  file=${path##*/}
  commit=$(git -C "$research" log -1 --format=%H -- "$path" | cut -c1-7)
  if ! grep -qF -- "Evaluated: $file @ $commit" <<<"$mentions"; then
    echo "$path $commit"
    n=$((n + 1))
  fi
done < <(git -C "$research" log --since="$since" --diff-filter=AM --name-only --format= -- docs/reports/ | sort -u)
echo "unevaluated: $n (since $since)"
