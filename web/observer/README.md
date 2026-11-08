# Observer showcase

The owner narrowed first publication to **one full-campaign YouTube pilot** on
lynxsupercluster, in a separate Open Imperial Conquest playlist. Pages excerpts
must link to that matching completed video; no invented/pending YouTube URL is
accepted. Recurring publication is disabled until the owner reviews that pilot.
Use `scripts/record-full-observer.mjs` to capture one actual campaign through its
terminal boundary and prepare an unlisted upload request; it performs no upload.
An exhausted budget is a failure, never a full-campaign claim. Full recordings
use streamed hashing/timestamp checks rather than whole-video memory buffers.

All new recordings are captured natively at **1920×1080, constant 30 fps**.
The validator checks the native 30 Hz timeline and Movie Maker frame counter,
then decodes both MP4s and checks every frame timestamp. The `-youtube.mp4` file is the separate
H.264 High/CRF18 upload master (48 kHz stereo AAC); the web MP4 is smaller.
No upscaling is used. YouTube may initially show SD while HD processing completes;
encoding cannot force a viewer's default playback quality.

Buildless static HTML/CSS/modules, hosted under the repository's GitHub Pages
subpath. All media and links are relative or explicit HTTPS; there is no game
engine, service, credentials or original-game media in the payload.

`scripts/record-full-observer.mjs` captures one production campaign through its
actual terminal boundary and derives an excerpt from that same recording. It
forces authored-only fresh settings and records scenario/seed/build/end metadata.
`scripts/prepare-observer-pilot.mjs` pairs it with a receipt from the sole accepted
YouTube upload after HD processing is verified. `scripts/publish-observers.mjs`
verifies the files, uploads only the excerpt/poster/provenance to `observer-media`,
checks public media, and assembles the Pages payload. The full master belongs on
YouTube and in the private artifact archive. No full master is uploaded to Pages
or the public media release. Failed jobs never deploy a partial playlist.

Recurring publication is disabled for the one-pilot review. `Publish observer
pilot page` is manually dispatched with an already verified generation and
performs no capture or YouTube upload. Later scenario/seed rotation can be enabled
after the owner reviews the pilot. The page refreshes metadata between clips;
an unavailable refresh retains the current collection. After 14 days it reports
a delayed refresh. `record-observers.mjs` remains a bounded format/renderer QA
tool; its unpaired excerpts cannot pass the publisher's full-campaign-link gate.

For the single pilot, use a new directory and retain the native upload identity:

```text
node scripts/record-full-observer.mjs --out=rendered/observer/pilot --seed=44 --excerpt-seconds=180
node scripts/prepare-observer-pilot.mjs --pilot=rendered/observer/pilot --receipt=rendered/observer/pilot/youtube-receipt.json
node scripts/publish-observers.mjs --batch=rendered/observer/pilot --site=rendered/observer/site
```

Dependencies: .NET 10, Godot 4.7.2 mono, Node 24, FFmpeg/ffprobe and gh. Linux needs
a real display or Xvfb/OpenGL software rendering. Windows may pass absolute native
`.exe` paths through `--godot`, `--ffmpeg`, `--ffprobe`; a shell shim is not needed.
Use a clean committed checkout. Capture diagnostics and sources remain in the
ignored output folder. In automated Linux runs all XDG paths are isolated; the
recording profile also bypasses owner settings/migration on every platform and
redirects both engine and structured diagnostics to the capture folder.

Serve the assembled site with a static HTTP server, then test public media with:

```text
node scripts/check-observer-web.mjs --url=http://127.0.0.1:8080/imperial_conquest_2/ --playwright-root=/path/to/ai-player --out=rendered/observer-web
```

Chromium and Firefox must be installed for that Playwright environment. The
player uses standard MP4/native video controls; Safari is a target with no native
Safari acceptance until explicitly tested. The frontend needs no ai-player AI,
TTS keys or browser-game adapter. Its installed Playwright can run these tests.
See `docs/design/v011-observer-recordings.md` and T286 for acceptance and limits.

`check-observer-web.mjs --fixture=<local-fixture.json>` exercises the real
captured MP4 with isolated range-serving routes and an explicit QA banner. That
is local player/codec evidence only, not public-host or completed-YouTube evidence.
