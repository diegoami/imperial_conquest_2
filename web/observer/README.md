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
The validator decodes the source and both MP4s, checks every timestamp and requires
exactly 30 frames per second. The `-youtube.mp4` release asset is the separate
H.264 High/CRF18 upload master (48 kHz stereo AAC); the web MP4 is smaller.
No upscaling is used. YouTube may initially show SD while HD processing completes;
encoding cannot force a viewer's default playback quality.

Buildless static HTML/CSS/modules, hosted under the repository's GitHub Pages
subpath. All media and links are relative or explicit HTTPS; there is no game
engine, service, credentials or original-game media in the payload.

`scripts/record-observers.mjs` creates a fresh, bounded batch from committed source,
builds/imports the real Godot client, forces authored-only fresh viewing settings,
records production scenarios and encodes/decodes/inspects MP4s. It retries idle
excerpts up to three seeds. `scripts/publish-observers.mjs` verifies hashes and
provenance before unique uploads to the `observer-media` prerelease, verifies
public media availability, and assembles a static output directory with the new
manifest. Previous generations are retained. Failed jobs never deploy a partial
playlist. This media archive is separate from versioned product releases.

Normal refresh is weekly (Monday 06:00 UTC), also manually dispatchable through
`Refresh observer recordings`. Each batch includes Classical and a rotating
historical scenario, with fresh seeds and 180-second excerpts. Job/frame/seed
retry budgets are bounded. The page refreshes its playlist only between clips;
an unavailable refresh retains the browser's current collection. After 14 days
the displayed update date reports a delayed refresh.

For a local batch, use a new directory (no overwrites):

```text
node scripts/record-observers.mjs --out=rendered/observer/example --seconds=180 --seed=42 --scenarios=classical-mediterranean,near-east-750bc
node scripts/publish-observers.mjs --batch=rendered/observer/example --site=rendered/observer/site
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
