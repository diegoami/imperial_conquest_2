#!/usr/bin/env bash
# Lists the research reports this repository has not evaluated: added or changed in the research
# repository's docs/reports/ since a date, and mentioned by no issue or PR here updated after the
# report's last commit (docs/evidence-pipeline.md, "What triggers it"). Read-only.
# Two GitHub listing calls in all, never one search per report: the search API allows 30 a minute.
# Usage: scripts/unevaluated-reports.sh [since YYYY-MM-DD]
#   The default is 14 days ago, but never before the rule's start (FLOOR); reports older than that
#   were evaluated before mentions were the marker.
set -euo pipefail
research=${IC2_RESEARCH_DIR:-C:/Users/diego/projects/RE-imperial-conquest-2}
repo=diegoami/imperial_conquest_2
FLOOR=2026-10-01
if [[ $# -ge 1 ]]; then since=$1; else
  since=$(date -d '14 days ago' +%F)
  [[ "$since" < "$FLOOR" ]] && since=$FLOOR
fi
git -C "$research" pull -q --ff-only

# One line per issue or PR: "<updatedAt> <body and every comment, flattened>".
mentions=$( { gh issue list -R "$repo" --state all --search "updated:>=$since" --limit 1000 --json updatedAt,body,comments
              gh pr list -R "$repo" --state all --search "updated:>=$since" --limit 1000 --json updatedAt,body,comments; } |
  jq -r '.[] | "\(.updatedAt) \(([.body] + [.comments[].body]) | join(" ") | gsub("[\r\n]"; " "))"')

n=0
while read -r path; do
  [[ -n "$path" && -f "$research/$path" ]] || continue
  file=${path##*/}
  changed=$(git -C "$research" log -1 --format=%cI -- "$path" | TZ=UTC date -f - +%FT%TZ)
  if ! grep -F -- "$file" <<<"$mentions" | awk -v c="$changed" '$1 > c { found=1 } END { exit !found }'; then
    echo "$path $(git -C "$research" log -1 --format=%h -- "$path")"
    n=$((n + 1))
  fi
done < <(git -C "$research" log --since="$since" --diff-filter=AM --name-only --format= -- docs/reports/ | sort -u)
echo "unevaluated: $n (since $since)"
