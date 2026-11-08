# Producing and finding observer videos

## Where the videos are

Run commands from the repository root. Recordings are large, git-ignored files under `rendered/`; committing scripts does **not** upload the videos.

The first complete Classical campaign (seed 44, source `fa1b19552b66f9476979c77caad9c44219746dcf`) was captured in:

```text
C:\Users\diego\projects\imperial_conquest_2\rendered\observer-pilot\classical-44-fa1b\
```

Its original playable recording is **`campaign.ogv`**. Open it in VLC or FFplay. It contains the whole 480-round campaign through its time-limit ending: 196,808 frames, **1:49:20.267 at 30 fps**, natively 1920×1080. OGV is the Ogg container produced by Godot, with Theora video and Vorbis audio.

The encoder writes these files beside it:

| File | Purpose |
| --- | --- |
| `classical-mediterranean-44-fa1b19552b66-youtube.mp4` | Full campaign, high-quality H.264/AAC master; upload this to YouTube |
| `classical-mediterranean-44-fa1b19552b66-excerpt.mp4` | First three minutes from that same campaign, compressed for Pages |
| `classical-mediterranean-44-fa1b19552b66.jpg` | Poster image |
| `capture-request.json`, `capture.log`, `progress.json` | Source revision, seed, capture diagnostics and progress; the terminal event in the log proves completion |
| `pilot.json`, `upload-request.json` | Completed pipeline metadata and the single unlisted-upload request |

An MP4 is ready only after the encoder exits successfully and verification completes; a growing or zero-byte MP4 is not a deliverable. `verified-youtube.mp4` under `rendered/v011-observer/` is a separate **60-second format test**, not the full campaign.

The completed pilot's native/MP4 frame checks, file hashes and archive links are preserved in
[the full-campaign verification receipt](../artifacts/observer/v0.11/full-campaign-verification.json).
Its [full MP4 on private Drive](https://drive.google.com/file/d/1MhDqMneKluYfsSPO18SGZHMsmK7HbP_z/view?usp=drivesdk)
is 4,015,876,479 bytes; the [original OGV](https://drive.google.com/file/d/1kcyibW2U6ZRlrFRLM22uEw2VkB41iJXN/view?usp=drivesdk)
is 1,249,886,324 bytes. These links retain the folder's private permissions.
The master/excerpt/poster and JSON provenance archive passed `rclone check` with ten matching
files and zero differences on 2026-11-08 at 23:34:38 local time; the raw/log archive had its own
four-file comparison. A Drive archive is separate from YouTube publication.

For new campaigns use a fresh folder such as `rendered/observer/<scenario>-<seed>-<revision>/`. Never overwrite a previous attempt. Keep the raw OGV, masters, logs, provenance and upload receipt together.

## Capture one full campaign

Requirements: Node 24, .NET 10, Godot 4.7.2 mono, FFmpeg/ffprobe, PowerShell and Git. Use a clean committed checkout. On Linux provide a real display or Xvfb with OpenGL. Capture may take several hours; do not run another Godot job concurrently.

```powershell
node scripts/record-full-observer.mjs --out=rendered/observer/pilot --seed=44 --excerpt-seconds=180
```

On Windows, if the tools are not native executables on PATH, pass their actual executable paths (not `.cmd` shims):

```powershell
node scripts/record-full-observer.mjs --out=rendered/observer/pilot --seed=44 --excerpt-seconds=180 `
  '--godot=C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' `
  '--ffmpeg=C:\Users\diego\projects\imperial_conquest_2\rendered\tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffmpeg.exe' `
  '--ffprobe=C:\Users\diego\projects\imperial_conquest_2\rendered\tools\ffmpeg\ffmpeg-9.0.2-essentials_build\bin\ffprobe.exe'
```

Optional `--scenario=<production-scenario-id>` chooses another listed historical scenario; `--seed=<integer>` selects its seed. The default capture budget is 21,600 recorded seconds (six hours); `--max-seconds=...` can reduce it. Reaching the budget is a failure, not evidence of a full campaign. This command captures exactly one campaign, using authored assets, isolated settings, slow movement-following and tactical observer battles. It exits at the actual terminal boundary, derives the paired excerpt and writes metadata. It performs no upload.

## Verify picture, cadence and completion

The pipeline checks native 1920×1080, square pixels, 16:9 and a 30 Hz timeline. It compares the OGV duration against Godot's exact movie frame counter, then decodes and checks **every MP4 frame timestamp and count**, hashes the files and validates stereo 48 kHz AAC. It does not upscale. The YouTube master uses H.264 High, CRF 18; the excerpt uses CRF 23.

Theora stores unchanged frames as zero-byte repeats, so the number of newly decoded source images can be lower than the movie's logical frame count. The final MP4 must nevertheless contain every constant-rate frame. Do not infer a lower native frame rate from that source image count or relabel it as 60 fps. Theora can also report a padded coded height of 1088; visible height must remain 1080.

Read `OBSERVER_END` in `capture.log` and the final “frames at 30 FPS” line. `progress.json` can stop one round before the ending; it is not the completion receipt. Play the beginning, middle and ending of the full master. After uploading, wait for YouTube HD processing, select 1080p and verify playback; a 1080p upload cannot force every viewer's automatic default quality.

## Recover an encoding failure without recording again

Preserve `campaign.ogv`, `capture-request.json` and `capture.log`. A capture can be complete even if the subsequent encoder failed. The encoder's fractional timeout bug is fixed in commit `2426b497`; use that fix or later before retrying a long recording.

The reusable `encodeObserver` export in `scripts/observer-encoding.mjs` accepts `ffmpeg`, `ffprobe`, `source`, the complete log text, `webPath`, `uploadPath`, `posterPath` and `excerptFrames` (5,400 for three minutes). Call it from an ignored `.mjs` helper, await it, and save the returned verification JSON beside the source. This repeats encoding/validation, not simulation. Use output names derived from the **capture-request source revision**, not the newer encoder revision. If necessary reconstruct `pilot.json` and `upload-request.json` using the final metadata block in `record-full-observer.mjs`, preserving the original request and terminal event. Do not claim a recovered job is publication-ready until these provenance fields and hashes agree.

## Archive and publish

Local `rendered/` is a working area. Archive large completed recordings and their provenance to the private Drive artifact location described in [artifacts.md](artifacts.md):

```powershell
rclone copy rendered/observer/pilot gdrive:IC2/artifacts/observer/pilot
rclone check rendered/observer/pilot gdrive:IC2/artifacts/observer/pilot --one-way
```

The configured artifact folder is [IC2 artifacts on Drive](https://drive.google.com/drive/folders/1nSWlIA7ibt40gjYj0aoG_hr0VHe-CsiR). Record the actual archive destination and verified file links in the publication receipt. A local file is not archived until the transfer and readback succeed. Keep original-game media out of this workflow.

The owner's publication limit is **one video first**, unlisted on **lynxsupercluster**, channel `UCQHMx6rNOKfLxFRI4_Tf6cA`, in a separate **Open Imperial Conquest** playlist. Keep the upload identity/receipt; do not retry an uncertain upload by creating another video. Do not use ai-player's GeoClick playlist or upload queues. No recurring upload is authorized before pilot review. The upload itself is manual: the owner uploads the full master in YouTube Studio (an agent's browser file-upload tool is capped at 10 MB; the master is ~4 GB) and supplies the video ID/URL, from which the receipt is prepared below. An agent must never fabricate a URL.

After the one upload has finished HD processing, prepare its verified receipt and pair it:

```powershell
node scripts/prepare-observer-pilot.mjs --pilot=rendered/observer/pilot --receipt=rendered/observer/pilot/youtube-receipt.json
node scripts/publish-observers.mjs --batch=rendered/observer/pilot --site=rendered/observer/site
```

See [the showcase README](../web/observer/README.md) and `prepare-observer-pilot.mjs` for the receipt schema and Pages workflow. Only excerpts, posters and provenance go to the public `observer-media` release. Full masters belong on YouTube and in the private archive. Pages contains no playable game, and links each excerpt to its matching full campaign, repository and releases. Its publishing workflow is manual; future refresh cadence remains subject to pilot review.
