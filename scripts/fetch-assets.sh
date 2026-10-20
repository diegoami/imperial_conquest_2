#!/usr/bin/env bash
# Pull the original game files from the user's private Drive into a cloud session and point
# assets.local.ini at them. Needs IC2_DRIVE_ASSETS (an rclone path, e.g. gdrive:IC2/original).
# The files stay in $IC2_ASSETS_DIR (default ~/ic2-original), outside the repository;
# assets.local.ini is git-ignored. Never commit, release or publish them (docs/artifacts.md).
set -euo pipefail
: "${IC2_DRIVE_ASSETS:?set IC2_DRIVE_ASSETS, e.g. gdrive:IC2/original}"
dest="${IC2_ASSETS_DIR:-$HOME/ic2-original}"
cd "$(git rev-parse --show-toplevel)"
mkdir -p "$dest"
rclone copy "$IC2_DRIVE_ASSETS" "$dest" -v \
  --exclude "*.mp4" --exclude "recordings*/**" --exclude "WAVS - Copy/**" --exclude ".git/**"
[ -f "$dest/Imperial Conquest 2.dat" ] || { echo "no 'Imperial Conquest 2.dat' in $dest" >&2; exit 1; }
printf '[assets]\ndirectory = %s\n' "$dest" > assets.local.ini
echo "assets ready: $dest ($(du -sh "$dest" | cut -f1)); assets.local.ini written"
