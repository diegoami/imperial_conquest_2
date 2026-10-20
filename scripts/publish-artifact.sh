#!/usr/bin/env bash
# Put a produced artifact somewhere reachable.
#   scripts/publish-artifact.sh <file> [--to auto|repo|release|drive] [--tag <release-tag>]
# auto: under 5 MB -> artifacts/ in the repo; larger -> the release for --tag (or the latest), or the
# Drive location when IC2_DRIVE_REMOTE is set. Drive uses rclone: IC2_DRIVE_REMOTE="gdrive:IC2/artifacts"
# (configure once with `rclone config`; share the folder so you can download from it).
# Never pass original-game files or anything derived from them (docs/artifacts.md).
set -euo pipefail
f="${1:?usage: publish-artifact.sh <file> [--to auto|repo|release|drive] [--tag t]}"; shift
to=auto; tag=""
while [ $# -gt 0 ]; do case "$1" in --to) to="$2"; shift 2;; --tag) tag="$2"; shift 2;; *) exit 2;; esac; done
cd "$(git rev-parse --show-toplevel)"
size=$(stat -c %s "$f" 2>/dev/null || stat -f %z "$f")
if [ "$to" = auto ]; then
  if [ "$size" -lt 5000000 ]; then to=repo
  elif [ -n "${IC2_DRIVE_REMOTE:-}" ]; then to=drive
  else to=release; fi
fi
case "$to" in
  repo) mkdir -p artifacts; cp "$f" artifacts/; echo "copied to artifacts/$(basename "$f"); commit it" ;;
  release)
    [ -n "$tag" ] || tag="$(gh release list --limit 1 --json tagName --jq '.[0].tagName')"
    gh release upload "$tag" "$f" --clobber; echo "uploaded to release $tag" ;;
  drive)
    : "${IC2_DRIVE_REMOTE:?set IC2_DRIVE_REMOTE, e.g. gdrive:IC2/artifacts}"
    rclone copy "$f" "$IC2_DRIVE_REMOTE" -v; echo "copied to $IC2_DRIVE_REMOTE" ;;
  *) echo "bad --to" >&2; exit 2 ;;
esac
