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

# dotnet and pwsh on the default PATH (tests spawn pwsh; tool shells may not source ~/.bashrc)
[ -x "$HOME/.dotnet/dotnet" ] && ln -sf "$HOME/.dotnet/dotnet" /usr/local/bin/dotnet
if ! have pwsh; then
  "$HOME/.dotnet/dotnet" tool install --global PowerShell >/dev/null 2>&1 || dotnet tool install --global PowerShell >/dev/null 2>&1
  [ -x "$HOME/.dotnet/tools/pwsh" ] && ln -sf "$HOME/.dotnet/tools/pwsh" /usr/local/bin/pwsh
fi
# The save corpus (private repo). Set IC2_FIXTURES_DIR=$HOME/ic2-test-fixtures in the environment variables.
fx="${IC2_FIXTURES_DIR:-$HOME/ic2-test-fixtures}"
if [ ! -d "$fx/.git" ] && [ -n "${GH_TOKEN:-}" ]; then
  git clone --depth 1 "https://x-access-token:${GH_TOKEN}@github.com/diegoami/ic2-test-fixtures.git" "$fx" >/dev/null 2>&1     && git -C "$fx" remote set-url origin https://github.com/diegoami/ic2-test-fixtures.git
fi

echo "--- cloud-setup report"
for t in git gh jq python3 dotnet pwsh rclone; do have "$t" && echo "ok      $t" || echo "MISSING $t"; done
[ -n "${GH_TOKEN:-}" ]        && echo "ok      GH_TOKEN set"        || echo "MISSING GH_TOKEN"
[ -n "${IC2_DRIVE_REMOTE:-}" ] && echo "ok      IC2_DRIVE_REMOTE=$IC2_DRIVE_REMOTE" || echo "unset   IC2_DRIVE_REMOTE (artifacts fall back to releases)"
have rclone && [ -n "${IC2_DRIVE_REMOTE:-}" ] && { rclone lsd "${IC2_DRIVE_REMOTE%%:*}:" >/dev/null 2>&1 && echo "ok      rclone reaches Drive" || echo "FAILED  rclone cannot reach Drive (token, or network allowlist)"; }
have gh && [ -n "${GH_TOKEN:-}" ] && { gh api repos/diegoami/imperial_conquest_2 --jq .full_name >/dev/null 2>&1 && echo "ok      gh REST works (GraphQL may be blocked: use gh api, docs/cloud-mode.md)" || echo "FAILED  gh REST call failed (token, or network allowlist for api.github.com)"; }
if [ -n "${IC2_DRIVE_ASSETS:-}" ] && [ ! -f assets.local.ini ] && have rclone; then bash "$(dirname "$0")/fetch-assets.sh" || echo "FAILED  fetch-assets"; fi
[ -d "$fx/.git" ] && echo "ok      fixtures in $fx ($(ls "$fx" | wc -l) entries); set IC2_FIXTURES_DIR=$fx" || echo "MISSING fixtures (needs GH_TOKEN with access to ic2-test-fixtures)"
true
