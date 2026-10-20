#!/usr/bin/env bash
# Cloud environment setup (Claude Code on the web). Idempotent; safe to re-run.
# Installs what a session needs beyond the base image and reports what is missing.
set -uo pipefail
have() { command -v "$1" >/dev/null 2>&1; }

have jq     || { apt-get update -qq && apt-get install -y -qq jq; }
have rclone || curl -fsSL https://rclone.org/install.sh | bash
if ! have dotnet || ! dotnet --list-sdks | grep -q '^10\.'; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
  echo 'export PATH="$HOME/.dotnet:$PATH"' >> "$HOME/.bashrc"
fi

echo "--- cloud-setup report"
for t in git gh jq python3 dotnet rclone; do have "$t" && echo "ok      $t" || echo "MISSING $t"; done
[ -n "${GH_TOKEN:-}" ]        && echo "ok      GH_TOKEN set"        || echo "MISSING GH_TOKEN"
[ -n "${IC2_DRIVE_REMOTE:-}" ] && echo "ok      IC2_DRIVE_REMOTE=$IC2_DRIVE_REMOTE" || echo "unset   IC2_DRIVE_REMOTE (artifacts fall back to releases)"
have rclone && [ -n "${IC2_DRIVE_REMOTE:-}" ] && { rclone lsd "${IC2_DRIVE_REMOTE%%:*}:" >/dev/null 2>&1 && echo "ok      rclone reaches Drive" || echo "FAILED  rclone cannot reach Drive (token, or network allowlist)"; }
have gh && [ -n "${GH_TOKEN:-}" ] && { gh api repos/diegoami/imperial_conquest_2 --jq .full_name >/dev/null 2>&1 && echo "ok      gh REST works (GraphQL may be blocked: use gh api, docs/cloud-mode.md)" || echo "FAILED  gh REST call failed (token, or network allowlist for api.github.com)"; }
if [ -n "${IC2_DRIVE_ASSETS:-}" ] && [ ! -f assets.local.ini ] && have rclone; then bash "$(dirname "$0")/fetch-assets.sh" || echo "FAILED  fetch-assets"; fi
true
