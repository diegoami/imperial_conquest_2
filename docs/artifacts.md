# Artifacts

What we produce (builds, installers, screenshots and recordings of **our own** game, review output,
reports) must stay reachable from the cloud. Use `scripts/publish-artifact.sh <file>`.

| Size / kind | Where | Why |
| --- | --- | --- |
| Under ~5 MB: screenshots, reports, review output, small fixtures we authored | `artifacts/` in the repo | One clone has everything |
| Release builds (zip, installer) | The tag's GitHub Release (`release.yml` attaches them) | Versioned, downloadable |
| Large or bulky: recordings, big test captures | The Drive location: `IC2_DRIVE_REMOTE` (an rclone remote, e.g. `gdrive:IC2/artifacts`) | Keeps the repo small; the user downloads from Drive |

**Drive setup (one time, by the user):** `rclone config` → new remote `gdrive` (Google Drive), then
set `IC2_DRIVE_REMOTE=gdrive:IC2/artifacts` in the cloud environment's variables and share that folder.
An agent without rclone or the variable falls back to a release.

**Still excluded:** the original game's files (`.exe`, `.dat`, `.hlp`, WAVS), its saves, and recordings or
screenshots of the *original* game. They are someone else's copyrighted material, and a private repo
or release does not change that. Evidence made from them stays in the originals folder and the
`imp_conquest_fixtures` releases already in use ([operating-guide §1.2](operating-guide.md)); the
older rule "no save, screenshot, recording or game file enters either repository" applies to
that material only. `.gitignore`'s `*.zip`/`*.exe` lines do not apply to `artifacts/`.
