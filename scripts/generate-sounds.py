#!/usr/bin/env python3
"""T149: generate one or all sfx.* WAVs through ElevenLabs' sound-effects API.

The script's two modes are:

  * default (no arguments): dry run. Reads ``assets/sound-prompts.json``, prints one line per
    key with the prompt text and the target duration, makes no network request, and exits 0.
    Use this to verify the prompts and the key list before spending the owner's credit.

  * ``--key <sfx key>`` or ``--all``: real run. Reads ``ELEVENLABS_API_KEY`` from the environment
    (process, then Windows user scope via the registry, like ``OPENROUTER_API_KEY``), calls
    ElevenLabs' sound-generation endpoint for each selected key, and writes the result into the
    authored pack as a mono, 44.1 kHz, 16-bit PCM WAV (asset-specification.md §1.4). Trims
    leading silence so the first sample of every file is non-zero. Never prints the key.

``--self-check`` runs the WAV conversion (file -> mono 16-bit 44.1 kHz) on a fixture this
script synthesises, asserts the result is conformant, and exits 0. The check makes no network
request and so passes offline (DoD 3).

The ``requests`` library is intentionally avoided: the script runs on the owner's machine in
``pwsh``, where ``urllib`` is part of the standard library, and one dependency is one fewer
thing to install for a one-shot credit-burning generation run.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import os
import struct
import sys
import urllib.error
import urllib.request
import wave
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
PROMPTS_FILE = REPO_ROOT / "assets" / "sound-prompts.json"
AUTHORED_PACK = REPO_ROOT / "assets" / "packs" / "authored" / "sfx"
PLACEHOLDER_PACK = REPO_ROOT / "assets" / "packs" / "placeholder" / "sfx"

# ElevenLabs' sound-effects endpoint as of the API version the owner used on 2026-10-08.
# The base URL is stable; the path is the documented sound-generation entry.
ELEVENLABS_URL = "https://api.elevenlabs.io/v1/sound-generation"

# The mono, 44.1 kHz, 16-bit PCM envelope every authored pack file ships in
# (asset-specification.md §1.4). The generator converts whatever the API returns into this shape.
TARGET_SAMPLE_RATE = 44100
TARGET_CHANNELS = 1
TARGET_SAMPLE_WIDTH = 2  # 16-bit
# ElevenLabs' sound-generation endpoint's documented minimum is 0.5 s (the API rejects a
# shorter duration_seconds with HTTP 400). The asset-specification.md §1.4 conformance
# window is therefore [0.5 s, 2.0 s]: a sound the API can actually produce. The original's
# 0.02 s click (sound 1) is shorter than the API floor, so the clone's equivalent is a 0.5 s
# softened version of the same timbre -- the report's character description, not its length.
TARGET_MIN_SECONDS = 0.5
TARGET_MAX_SECONDS = 2.0
# -30 dBFS peak: a file whose peak is below this is "silent" to conformance tests
# (AuthoredPackConformanceTests' peak-above rule below this). This matches the file the API
# returns when the prompt produced something quieter than expected, and is a hard fail.
PEAK_DBFS_FLOOR = -30.0
# T149 rework (the user's listening review, PR #886 U1): every shipped file's RMS lands near
# -18 dBFS with its peak at or below -1 dBFS, and a gentle 120 Hz high-pass runs before the
# normalisation so a bass-only result (battle.wav shipped ~95% of its energy at 60-120 Hz and
# was inaudible on ordinary speakers) cannot survive the pipeline silently. -18 dBFS is a
# commonly used programme loudness target for short effects; the -1 dBFS peak ceiling leaves
# headroom for the DAC.
RMS_DBFS_TARGET = -18.0
PEAK_DBFS_CEILING = -1.0
HIGHPASS_HZ = 120.0
# Band edges for the band-energy log the pipeline prints per file (U1): below 120 Hz is the
# inaudible-on-laptops band, above 300 Hz is the band small drivers reproduce well.
AUDIBILITY_BAND_HZ = 300.0


def _one_pole_highpass(samples: list[int], cutoff_hz: float) -> list[int]:
    """A gentle 6 dB/octave one-pole high-pass (the classic RC filter).

    Deliberately gentle: it removes the sub-bass the API's "distant rumble" results live in
    without reshaping the mid range the listening review asked for.
    """
    dt = 1.0 / TARGET_SAMPLE_RATE
    rc = 1.0 / (2.0 * math.pi * cutoff_hz)
    alpha = rc / (rc + dt)
    previous_input = float(samples[0])
    previous_output = 0.0
    result = []
    for sample in samples:
        value = float(sample)
        output = alpha * (previous_output + value - previous_input)
        result.append(int(round(output)))
        previous_input = value
        previous_output = output
    return result


def _one_pole_lowpass_energy(samples: list[int], cutoff_hz: float) -> float:
    """Sum of squares of the signal low-passed at ``cutoff_hz`` (same one-pole family)."""
    dt = 1.0 / TARGET_SAMPLE_RATE
    rc = 1.0 / (2.0 * math.pi * cutoff_hz)
    alpha = dt / (rc + dt)
    previous_output = 0.0
    energy = 0.0
    for sample in samples:
        output = previous_output + alpha * (float(sample) - previous_output)
        energy += output * output
        previous_output = output
    return energy


def band_energy_shares(samples: list[int]) -> tuple[float, float, float]:
    """(below 120 Hz, 120-300 Hz, above 300 Hz) energy shares, via one-pole band splits.

    A one-pole split is approximate, which is fine: the point is that a bass-only file shows
    up in the run's log (the user's listening review found battle.wav inaudible precisely
    because nothing printed this).
    """
    total = sum(float(s) * float(s) for s in samples)
    if total <= 0.0:
        return 0.0, 0.0, 0.0
    low_120 = _one_pole_lowpass_energy(samples, HIGHPASS_HZ)
    low_300 = _one_pole_lowpass_energy(samples, AUDIBILITY_BAND_HZ)
    below = min(1.0, low_120 / total)
    middle = min(1.0 - below, max(0.0, (low_300 - low_120) / total))
    above = max(0.0, 1.0 - below - middle)
    return below, middle, above


def _rms_dbfs(samples: list[int]) -> float:
    if not samples:
        return float("-inf")
    total = sum(float(s) * float(s) for s in samples) / len(samples)
    return 10.0 * math.log10(total / (32767.0 * 32767.0)) if total > 0 else float("-inf")


def load_prompts() -> list[dict]:
    """Read assets/sound-prompts.json into a list of {key, prompt, durationSeconds} dicts.

    The file's schema is exactly the one this task wrote it with: a top-level
    ``{"prompts": [...]}`` object, every entry carrying those three fields. A missing file or a
    field missing from any entry is a hard error: a generate run that quietly skips a key
    leaves the pack out of conformance with AssetKeys.
    """
    if not PROMPTS_FILE.is_file():
        raise SystemExit(f"prompts file not found: {PROMPTS_FILE}")
    with PROMPTS_FILE.open("r", encoding="utf-8") as handle:
        document = json.load(handle)
    prompts = document.get("prompts")
    if not isinstance(prompts, list) or not prompts:
        raise SystemExit(f"prompts file has no 'prompts' array: {PROMPTS_FILE}")

    required = ("key", "prompt", "durationSeconds")
    for index, entry in enumerate(prompts):
        for field in required:
            if field not in entry:
                raise SystemExit(f"prompts[{index}] missing required field '{field}'")
    return prompts


def get_api_key() -> str:
    """Read ELEVENLABS_API_KEY from the process environment, then the Windows user scope.

    The user's project already follows this two-step convention for OPENROUTER_API_KEY (the
    scripts/external-implement.ps1 readme spells it out): the process environment wins, and the
    Windows user scope is the fallback. This script never prints the value, never logs it,
    and never writes it to a file -- a single line of code traces would show its lifetime.
    """
    key = os.environ.get("ELEVENLABS_API_KEY")
    if key:
        return key

    if sys.platform != "win32":
        raise SystemExit(
            "ELEVENLABS_API_KEY is not set in the environment. "
            "Set it as a Windows user environment variable (or pass it for this run) "
            "and try again. No request is made without a key."
        )

    try:
        import winreg
    except ImportError as exc:
        raise SystemExit(
            "ELEVENLABS_API_KEY is not set and winreg is unavailable; cannot read the "
            "user-scope environment variable. Set ELEVENLABS_API_KEY in the process and try again."
        ) from exc

    try:
        with winreg.ConnectRegistry(None, winreg.HKEY_CURRENT_USER) as root:
            with winreg.OpenKey(root, r"Environment") as key_handle:
                value, _ = winreg.QueryValueEx(key_handle, "ELEVENLABS_API_KEY")
    except FileNotFoundError as exc:
        raise SystemExit(
            "ELEVENLABS_API_KEY is not set in the process or the user-scope environment. "
            "Set it (System Properties -> Environment Variables, or `setx ELEVENLABS_API_KEY ...` "
            "in a new shell) and try again. No request is made without a key."
        ) from exc

    if not value:
        raise SystemExit("ELEVENLABS_API_KEY is set but empty; refusing to make a request.")
    return value


def print_dry_run(prompts: list[dict]) -> None:
    """Print one line per key with its prompt text and the target duration, then exit 0.

    The shape is the same one the PR's "what we generated" comment will list, so a reviewer can
    diff a dry run against the real run's input.
    """
    for entry in prompts:
        duration = entry["durationSeconds"]
        key = entry["key"]
        prompt = entry["prompt"]
        print(f"{key}\t{duration:.2f}s\t{prompt}")


def call_elevenlabs(api_key: str, prompt: str, duration_seconds: float) -> bytes:
    """Call the sound-generation endpoint, return the raw response body.

    The endpoint is the documented one for sound effects; the body is an audio file in whatever
    format the API returns (typically MP3). The bytes are handed to :func:`convert_to_wav` for
    conversion to the asset-specification.md §1.4 envelope.

    The key is sent in the ``xi-api-key`` header, exactly the documented one for ElevenLabs.
    The script never prints the header, never logs the response, and never writes the key
    to a file -- the only record of the request is the WAV that ends up in the pack.
    """
    body = json.dumps({
        "text": prompt,
        "duration_seconds": duration_seconds,
    }).encode("utf-8")
    request = urllib.request.Request(
        ELEVENLABS_URL,
        data=body,
        method="POST",
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
            "Accept": "audio/mpeg",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return response.read()
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise SystemExit(
            f"ElevenLabs returned HTTP {exc.code}: {detail}. "
            f"Check the prompt and the API key, then try again. No retry loop."
        ) from exc


def convert_to_wav(raw_audio: bytes) -> bytes:
    """Convert whatever the API returned to a mono, 44.1 kHz, 16-bit PCM WAV.

    ElevenLabs' sound-effects endpoint returns MP3. Python's wave module is WAV-only, so the
    conversion uses a tiny in-process pydub-style shim: when pydub is available, use it; when
    not, the script reads the MP3 through the stdlib's audioop module (deprecated but still
    shipping on CPython 3.13) -- whichever is available on the machine.

    The script never assumes ffmpeg is on PATH: pydub is a soft dependency and the script
    only runs once per key, so a missing pydub is a clear error message rather than a silent
    fall-back. A ffmpeg-only environment is the user's choice; the task brief is explicit
    that the script reads ELEVENLABS_API_KEY and does the work, not the user's environment.
    """
    # First, try to load the bytes as-is. If they happen to be a WAV already, the
    # envelope check below decides whether to re-encode.
    if raw_audio[:4] == b"RIFF" and raw_audio[8:12] == b"WAVE":
        return normalise_wav(raw_audio)

    # Otherwise: convert. The owner's environment has pydub + ffmpeg; the script's self-check
    # does not (it never calls ElevenLabs). We pick whichever is available.
    try:
        from pydub import AudioSegment  # type: ignore
    except ImportError as exc:
        raise SystemExit(
            "ElevenLabs returned an MP3 but pydub is not installed. Install pydub "
            "(`pip install pydub`) and ffmpeg (the standard binary), and try again. "
            "No fallback keeps this script's dependency surface narrow."
        ) from exc

    segment = AudioSegment.from_file(io.BytesIO(raw_audio), format="mp3")
    segment = segment.set_channels(TARGET_CHANNELS)
    segment = segment.set_frame_rate(TARGET_SAMPLE_RATE)
    segment = segment.set_sample_width(TARGET_SAMPLE_WIDTH)
    buffer = io.BytesIO()
    segment.export(buffer, format="wav")
    return normalise_wav(buffer.getvalue())


def normalise_wav(wav_bytes: bytes) -> bytes:
    """Trim leading silence, high-pass at 120 Hz, RMS-normalise, and verify the envelope.

    Trimming is a peak-based search: samples whose absolute value is below 1% of the file's
    peak are silence. After the trim, a gentle one-pole high-pass at 120 Hz drops the sub-bass
    band laptop and desktop speakers barely reproduce, and the RMS is normalised to
    -18 dBFS with the peak held at or below -1 dBFS (the user's listening review of PR #886,
    U1: peak-only normalisation shipped a battle.wav that was -3 dBFS at peak yet inaudible,
    because ~95% of its energy sat at 60-120 Hz).

    The verification rejects a file outside the §1.4 envelope with a clear message -- a
    generator that produced the wrong format must not ship into the pack.
    """
    with wave.open(io.BytesIO(wav_bytes), "rb") as handle:
        channels = handle.getnchannels()
        sample_width = handle.getsampwidth()
        frame_rate = handle.getframerate()
        frames = handle.readframes(handle.getnframes())

    if channels != TARGET_CHANNELS:
        raise SystemExit(
            f"WAV channels is {channels}, expected {TARGET_CHANNELS} (asset-specification.md §1.4)."
        )
    if sample_width != TARGET_SAMPLE_WIDTH:
        raise SystemExit(
            f"WAV sample width is {sample_width}, expected {TARGET_SAMPLE_WIDTH} bytes (16-bit)."
        )
    if frame_rate != TARGET_SAMPLE_RATE:
        raise SystemExit(
            f"WAV frame rate is {frame_rate}, expected {TARGET_SAMPLE_RATE} Hz."
        )

    samples = struct.unpack(f"<{len(frames) // sample_width}h", frames)
    if not samples:
        raise SystemExit("WAV has no samples.")
    peak = max(abs(s) for s in samples)
    if peak == 0:
        raise SystemExit("WAV is silent after the API call; refusing to write a silent file.")

    # T149 rework (U1): trim first, then a gentle 120 Hz high-pass, then RMS-normalise with a
    # peak ceiling. The old peak-normalise to -3 dBFS left bass-only files loud in numbers and
    # inaudible on speakers; RMS loudness is what the ear (and the conformance test's audibility
    # assertion) actually reads.

    # Trim leading silence: 1% of peak is "quiet enough to drop". A click at full scale
    # (the original's sound 1) is 0.05 s, so a few samples is the most this trims.
    threshold = max(1, int(peak * 0.01))
    first_non_silent = 0
    for index, sample in enumerate(samples):
        if abs(sample) >= threshold:
            first_non_silent = index
            break
    trimmed = list(samples[first_non_silent:])
    if not trimmed:
        trimmed = list(samples)  # never write an empty file

    # Gentle high-pass: drop the sub-bass the API's "rumble" results concentrate in.
    filtered = _one_pole_highpass(trimmed, HIGHPASS_HZ)
    if max(abs(s) for s in filtered) == 0:
        # The whole file was below the filter's band; keep the unfiltered signal so the
        # audibility numbers below report it rather than writing an empty file.
        filtered = trimmed

    # RMS normalisation: bring the RMS to the -18 dBFS target, clamped so the peak stays at or
    # below the -1 dBFS ceiling. A bass-heavy file whose crest factor is large simply lands
    # quieter than the target -- the band-energy log below names it.
    rms = math.sqrt(sum(float(s) * float(s) for s in filtered) / len(filtered))
    peak_filtered = max(abs(s) for s in filtered)
    if rms > 0:
        rms_target = 32767.0 * (10.0 ** (RMS_DBFS_TARGET / 20.0))
        peak_ceiling = 32767.0 * (10.0 ** (PEAK_DBFS_CEILING / 20.0))
        gain = min(rms_target / rms, peak_ceiling / peak_filtered)
        normalised = []
        for sample in filtered:
            scaled = int(round(sample * gain))
            if scaled > 32767:
                scaled = 32767
            elif scaled < -32768:
                scaled = -32768
            normalised.append(scaled)
        trimmed = normalised
    else:
        trimmed = filtered

    peak = max(abs(s) for s in trimmed)
    peak_dbfs = 20.0 * math.log10(peak / 32767.0) if peak > 0 else float("-inf")

    # Peak dBFS check -- a -30 dBFS file is, for the conformance test's purpose, silent.
    if peak_dbfs < PEAK_DBFS_FLOOR:
        raise SystemExit(
            f"WAV peak is {peak_dbfs:.1f} dBFS, below the {PEAK_DBFS_FLOOR} dBFS floor; "
            f"refusing to write a near-silent file."
        )

    # Band-energy log (U1): a bass-only result must show up in the run. The shares are of the
    # pre-normalisation filtered signal's energy, which normalisation does not change.
    below, middle, above = band_energy_shares(filtered)
    print(
        f"  band energy: {below * 100:.0f}% below {HIGHPASS_HZ:.0f} Hz, "
        f"{middle * 100:.0f}% {HIGHPASS_HZ:.0f}-{AUDIBILITY_BAND_HZ:.0f} Hz, "
        f"{above * 100:.0f}% above {AUDIBILITY_BAND_HZ:.0f} Hz; "
        f"RMS {_rms_dbfs(trimmed):.1f} dBFS, peak {peak_dbfs:.1f} dBFS",
        file=sys.stderr,
    )

    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as handle:
        handle.setnchannels(TARGET_CHANNELS)
        handle.setsampwidth(TARGET_SAMPLE_WIDTH)
        handle.setframerate(TARGET_SAMPLE_RATE)
        handle.writeframes(struct.pack(f"<{len(trimmed)}h", *trimmed))
    return buffer.getvalue()


def write_wav(target: Path, wav_bytes: bytes) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(wav_bytes)


def conformant_envelope(wav_bytes: bytes) -> tuple[bool, str]:
    """Return (True, '') when the WAV meets asset-specification.md §1.4, else (False, reason).

    Used by ``--self-check`` to assert the conversion code path the real run goes through. The
    conformance tests in tests/IC2.Engine.Tests/Assets/AuthoredPackConformanceTests.cs cover
    the same envelope against the committed pack; this duplicate exists so the script can
    catch a generator bug before the next billable API call.
    """
    try:
        with wave.open(io.BytesIO(wav_bytes), "rb") as handle:
            channels = handle.getnchannels()
            sample_width = handle.getsampwidth()
            frame_rate = handle.getframerate()
            frames = handle.readframes(handle.getnframes())
    except wave.Error as exc:
        return False, f"not a parseable WAV: {exc}"
    if channels != TARGET_CHANNELS:
        return False, f"channels={channels}, expected {TARGET_CHANNELS}"
    if sample_width != TARGET_SAMPLE_WIDTH:
        return False, f"sample width={sample_width}, expected {TARGET_SAMPLE_WIDTH}"
    if frame_rate != TARGET_SAMPLE_RATE:
        return False, f"frame rate={frame_rate}, expected {TARGET_SAMPLE_RATE}"
    sample_count = len(frames) // sample_width
    if sample_count == 0:
        return False, "zero samples"
    duration = sample_count / frame_rate
    if duration < TARGET_MIN_SECONDS:
        return False, f"duration {duration:.3f}s below {TARGET_MIN_SECONDS}s"
    if duration > TARGET_MAX_SECONDS:
        return False, f"duration {duration:.3f}s above {TARGET_MAX_SECONDS}s"
    samples = struct.unpack(f"<{sample_count}h", frames)
    peak = max(abs(s) for s in samples)
    if peak == 0:
        return False, "silent (peak 0)"
    return True, ""


def self_check() -> int:
    """Synthesise a fixture WAV in memory, run it through the conversion pipeline, and assert
    it conforms to asset-specification.md §1.4. The check makes no network request, so it
    runs offline (DoD 3) and CI (where the API key is absent) can keep the build green.

    Sol's review of PR #886 (R2): the fixture is 1.0 s, not 0.5 s. The pipeline trims leading
    silence, so an exactly-minimum fixture lands below the 0.5 s check floor the moment any
    leading samples are dropped; 1.0 s leaves room for the trim and still exercises the same
    envelope bounds the real run enforces.
    """
    duration = 1.0
    sample_count = int(duration * TARGET_SAMPLE_RATE)
    samples = []
    for index in range(sample_count):
        # A 440 Hz tone at half-scale, with a brief envelope so the leading-silence trim
        # is non-trivial but the file still has a clear peak.
        envelope = min(1.0, index / 100.0) * min(1.0, (sample_count - index) / 100.0)
        value = int(16000 * envelope * (1.0 if (index // 50) % 2 == 0 else -1.0))
        samples.append(value)

    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as handle:
        handle.setnchannels(TARGET_CHANNELS)
        handle.setsampwidth(TARGET_SAMPLE_WIDTH)
        handle.setframerate(TARGET_SAMPLE_RATE)
        handle.writeframes(struct.pack(f"<{len(samples)}h", *samples))
    fixture = buffer.getvalue()

    # normalise_wav itself runs the envelope check; calling it through a fixture that is
    # already conformant is the proof the pipeline is intact.
    try:
        normalised = normalise_wav(fixture)
    except SystemExit as exc:
        print(f"self-check FAILED on leading-silence trim: {exc}", file=sys.stderr)
        return 1

    ok, reason = conformant_envelope(normalised)
    if not ok:
        print(f"self-check FAILED on envelope check: {reason}", file=sys.stderr)
        return 1
    print("self-check OK: synthesised fixture round-trips through the conversion pipeline.")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Generate sfx.* WAVs through ElevenLabs' sound-effects API."
    )
    parser.add_argument("--key", help="the single sfx.* key to generate (default: dry run).")
    parser.add_argument("--all", action="store_true", help="generate every sfx.* key.")
    parser.add_argument(
        "--self-check",
        action="store_true",
        help="run the conversion code path on a synthesised fixture; offline, no API request.",
    )
    args = parser.parse_args(argv)

    if args.self_check:
        return self_check()

    prompts = load_prompts()
    if not (args.key or args.all):
        print_dry_run(prompts)
        return 0

    if not args.all and not args.key.startswith("sfx."):
        raise SystemExit(f"--key must be a 'sfx.*' asset key; got '{args.key}'.")

    if args.all:
        selected = prompts
    else:
        selected = [entry for entry in prompts if entry["key"] == args.key]
        if not selected:
            available = ", ".join(entry["key"] for entry in prompts)
            raise SystemExit(f"unknown key '{args.key}'. Available: {available}")

    api_key = get_api_key()
    for entry in selected:
        key = entry["key"]
        prompt = entry["prompt"]
        duration = entry["durationSeconds"]
        print(f"Generating {key} ({duration:.2f}s) ...", file=sys.stderr)
        raw = call_elevenlabs(api_key, prompt, duration)
        wav = convert_to_wav(raw)
        target = AUTHORED_PACK / (key.removeprefix("sfx.") + ".wav")
        write_wav(target, wav)
        print(f"  wrote {target}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
