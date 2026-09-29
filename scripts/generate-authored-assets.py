#!/usr/bin/env python3
"""T51: the prompt-driven asset generator (issue #173).

A one-shot, re-runnable authoring tool in the shape T29's world export already established:
a human runs it when the prompts change, and the COMMITTED output (assets/packs/authored/)
is what every build, test and player uses. CI never calls it and holds no key.

It reads assets/prompts.json - one prompt per AssetKeys constant, authored from
docs/asset-specification.md - calls the configured OpenRouter-compatible chat-completions
endpoint with modalities ["image", "text"] (the image returns as a base64 data URL in the
assistant message's `images` field), conforms each result to the specification's format
rules (docs/asset-specification.md par. 1: 32x32 BMP, 24-bit opaque terrain tiles, 32-bit
BGRA sprites with the background keyed out to alpha), and writes assets/packs/authored/
with a manifest in the same shape as T11's placeholder pack.

Non-determinism is the defining constraint: the same prompt does not give the same pixels
twice, so the committed images are the source of truth, exactly as T29's exported JSON is.
This script never regenerates anything unless asked, and the pack's tests pin conformance,
never byte equality.

Cost model: dry run by default (DoD 3); a real, billable run needs --key <AssetKey>
(repeatable, DoD 4) or an explicit --all. The API key comes from assets-generator.local.ini
(git-ignored; copy assets-generator.example.ini) or the OPENROUTER_API_KEY environment
variable, is never printed, and request headers are never echoed. One run at a time: the
run is long, billable and network-bound, and the script takes a lock file to keep two
runs from double-billing.

Sound effects (the three sfx.* keys) are synthesized locally and deterministically in the
mono 44.1 kHz 16-bit PCM shape of docs/asset-specification.md par. 1.4 - no image API
produces audio - from the same prompts.json entries that describe them.

Requires Python 3 and Pillow (Pillow is NOT a build or CI dependency: the C# conformance
test reads committed BMPs and needs neither).
"""

from __future__ import annotations

import argparse
import base64
import configparser
import io
import json
import math
import os
import struct
import sys
import time
import urllib.error
import urllib.request
import wave

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover - environment guard
    sys.stderr.write(
        "Pillow is required for image conforming: pip install Pillow\n"
        "(Pillow is a generator-time dependency only, not a build or CI dependency.)\n"
    )
    raise SystemExit(2)

# --- Constants (docs/asset-specification.md par. 1) --------------------------------------

SIZE = 32  # par. 1.3: 32x32 for every map-grid and marker asset
DEFAULT_ENDPOINT = "https://openrouter.ai/api/v1"
DEFAULT_CONFIG_NAME = "assets-generator.local.ini"
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_PROMPTS = os.path.join(REPO_ROOT, "assets", "prompts.json")
DEFAULT_OUT_DIR = os.path.join(REPO_ROOT, "assets", "packs", "authored")
DEFAULT_CONFIG = os.path.join(REPO_ROOT, DEFAULT_CONFIG_NAME)
HTTP_TIMEOUT_SECONDS = 300  # image generation can take a minute or more per call
MODELS_TIMEOUT_SECONDS = 30
LOCK_FILE_NAME = ".generator.lock"

# Background keying: a pixel within this RGB distance of the estimated background colour
# becomes transparent. The prompts ask for a near-white subject on a solid black
# background, so subject pixels sit far outside this radius; the radius exists to absorb
# the model's background shading and compression noise.
KEY_TOLERANCE = 90.0

# The high-quality downscale filter, under either of Pillow's spellings:
# Image.Resampling.LANCZOS since Pillow 9.1, Image.LANCZOS before that.
LANCZOS = getattr(getattr(Image, "Resampling", Image), "LANCZOS")

# Cost estimate assumptions for the chat-completions image route (stated in every
# estimate the script prints; actual usage is reported by the provider per call).
ESTIMATED_PROMPT_TOKENS = 120
ESTIMATED_IMAGE_OUTPUT_TOKENS = 1300

VALID_KINDS = ("sprite", "tile", "sfx")


# --- Key conventions (docs/asset-specification.md par. 1.5) ------------------------------


def kind_for_key(key: str) -> str:
    """The asset kind implied by the key's first segment (sanity-checked against prompts.json)."""
    if key.startswith("terrain."):
        return "tile"
    if key.startswith("sfx."):
        return "sfx"
    return "sprite"  # unit icons and army/fleet/city markers


def asset_relpath(key: str) -> str:
    """The pack-relative path for a key, following par. 1.5's convention exactly.

    Mirrors the placeholder pack's own layout so the authored pack is drop-in: the
    folder is the key's first segment (plural `units/` being the shipped exception),
    and the file name is the remaining segments joined by `_`, dropping the trailing
    `.icon`/`.tile` suffix (sfx has no suffix).
    """
    segments = key.split(".")
    folder = "units" if segments[0] == "unit" else segments[0]
    name_segments = segments[1:]
    if name_segments and name_segments[-1] in ("icon", "tile"):
        name_segments = name_segments[:-1]
    name = "_".join(name_segments)
    kind = kind_for_key(key)
    ext = ".wav" if kind == "sfx" else ".bmp"
    return folder + "/" + name + ext


# --- BMP writers (hand-rolled, matching T11's placeholder byte shape: 14-byte --------------
# --- BITMAPFILEHEADER + 40-byte BITMAPINFOHEADER, bottom-up rows, BI_RGB, 4-byte padding) ---


def bmp_bytes(img: Image.Image, bit_count: int) -> bytes:
    """Serialize a 32x32 image as an uncompressed BMP: 24-bit BGR or 32-bit BGRA."""
    if img.size != (SIZE, SIZE):
        raise ValueError(f"expected {SIZE}x{SIZE}, got {img.size}")
    width, height = img.size
    bytes_per_pixel = bit_count // 8
    row_unpadded = width * bytes_per_pixel
    padding = (4 - row_unpadded % 4) % 4
    row_size = row_unpadded + padding
    data_size = row_size * height
    file_header = struct.pack(
        "<2sIHHI", b"BM", 14 + 40 + data_size, 0, 0, 14 + 40
    )
    info_header = struct.pack(
        "<IiiHHIIiiII",
        40,  # header size
        width,
        height,  # positive => bottom-up rows
        1,  # colour planes
        bit_count,
        0,  # BI_RGB, no compression
        data_size,
        0,  # X pixels per metre
        0,  # Y pixels per metre
        0,  # colours used
        0,  # important colours
    )
    pixels = img.load()
    out = bytearray()
    out += file_header
    out += info_header
    for y in range(height - 1, -1, -1):  # BMP rows are written bottom-up
        line = bytearray(row_unpadded)
        i = 0
        for x in range(width):
            pixel = pixels[x, y]
            if bit_count == 24:
                r, g, b = pixel[0], pixel[1], pixel[2]
                line[i] = b
                line[i + 1] = g
                line[i + 2] = r
            else:
                r, g, b, a = pixel
                line[i] = b
                line[i + 1] = g
                line[i + 2] = r
                line[i + 3] = a
            i += bytes_per_pixel
        out += line
        out += b"\x00" * padding
    return bytes(out)


# --- Conforming (DoD 5): downscale, key the background out to alpha, write BMP -------------


def estimate_background(img: Image.Image):
    """The median colour of the image's border ring - the prompts ask for a plain flat
    background, so the border is background unless the subject fills the frame."""
    width, height = img.size
    ring = max(2, min(width, height) // 64)
    pixels = img.load()
    channels = ([], [], [])
    for x in range(width):
        for y in list(range(ring)) + list(range(height - ring, height)):
            for c in range(3):
                channels[c].append(pixels[x, y][c])
    for y in range(ring, height - ring):
        for x in list(range(ring)) + list(range(width - ring, width)):
            for c in range(3):
                channels[c].append(pixels[x, y][c])
    return tuple(sorted(channel)[len(channel) // 2] for channel in channels)


def conform_sprite(img: Image.Image) -> bytes:
    """A marker/unit sprite: key the plain background out to alpha, downscale to 32x32
    with a high-quality filter, normalize the subject to a light/white neutral
    silhouette (the nation palette is applied at DRAW time by tinting, not baked in),
    and write a 32-bit BGRA BMP."""
    img = img.convert("RGBA")
    background = estimate_background(img)
    pixels = img.load()
    width, height = img.size
    for y in range(height):
        for x in range(width):
            pixel = pixels[x, y]
            distance = math.sqrt(
                (pixel[0] - background[0]) ** 2
                + (pixel[1] - background[1]) ** 2
                + (pixel[2] - background[2]) ** 2
            )
            if distance <= KEY_TOLERANCE:
                pixels[x, y] = (0, 0, 0, 0)
    small = img.resize((SIZE, SIZE), LANCZOS)
    small_pixels = small.load()
    # Kill faint background ghosts the resize smeared in; keep straight (non-premultiplied)
    # alpha for the rest, per docs/asset-specification.md par. 1.2.
    max_luminance = 0.0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = small_pixels[x, y]
            if a == 0:
                continue
            if a < 32:
                small_pixels[x, y] = (0, 0, 0, 0)
                continue
            luminance = 0.299 * r + 0.587 * g + 0.114 * b
            if luminance > max_luminance:
                max_luminance = luminance
    if max_luminance <= 0:
        max_luminance = 255.0
    scale = 255.0 / max_luminance
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = small_pixels[x, y]
            if a == 0:
                continue
            luminance = min(255.0, (0.299 * r + 0.587 * g + 0.114 * b) * scale)
            value = max(0, min(255, int(round(luminance))))
            small_pixels[x, y] = (value, value, value, a)
    return bmp_bytes(small, 32)


def conform_tile(img: Image.Image) -> bytes:
    """A terrain tile: fully opaque, downscale to 32x32 with a high-quality filter,
    write a 24-bit BGR BMP (docs/asset-specification.md par. 1.2: a terrain tile
    always fully covers its grid cell)."""
    img = img.convert("RGB").resize((SIZE, SIZE), LANCZOS)
    return bmp_bytes(img, 24)


# --- Sound synthesis (deterministic, stdlib only; docs/asset-specification.md par. 1.4) ---

SAMPLE_RATE = 44100


def _tone(samples: list, start: float, duration: float, freq: float, amp: float,
          harmonics=((2, 0.4), (3, 0.15)), attack: float = 0.008) -> None:
    """Add a decaying harmonic tone into `samples` (mono float list) in place."""
    start_i = int(start * SAMPLE_RATE)
    count = int(duration * SAMPLE_RATE)
    attack_i = max(1, int(attack * SAMPLE_RATE))
    for i in range(count):
        t = i / SAMPLE_RATE
        envelope = min(1.0, i / attack_i) * math.exp(-3.0 * t / duration)
        value = math.sin(2.0 * math.pi * freq * t)
        for multiple, weight in harmonics:
            value += weight * math.sin(2.0 * math.pi * freq * multiple * t)
        if start_i + i < len(samples):
            samples[start_i + i] += amp * envelope * value


def _noise_lcg(count: int, seed: int = 12345) -> list:
    """Deterministic white noise from a fixed-seed LCG - no RNG dependence, ever."""
    state = seed
    out = []
    for _ in range(count):
        state = (state * 1103515245 + 12345) & 0x7FFFFFFF
        out.append(state / 0x3FFFFFFF - 1.0)
    return out


def _lowpass(samples: list, coefficient: float = 0.15) -> list:
    accumulator = 0.0
    out = []
    for s in samples:
        accumulator += coefficient * (s - accumulator)
        out.append(accumulator)
    return out


def _drum(samples: list, start: float, freq: float = 70.0, amp: float = 0.9,
          duration: float = 0.35, seed: int = 7) -> None:
    """A low drum hit: a decaying low sine plus a short low-passed noise thump."""
    start_i = int(start * SAMPLE_RATE)
    count = int(duration * SAMPLE_RATE)
    noise = _lowpass(_noise_lcg(count, seed), 0.08)
    for i in range(count):
        t = i / SAMPLE_RATE
        envelope = math.exp(-7.0 * t / duration)
        value = math.sin(2.0 * math.pi * freq * t) + 0.5 * noise[i] * math.exp(-20.0 * t)
        if start_i + i < len(samples):
            samples[start_i + i] += amp * envelope * value


def _clash(samples: list, start: float, amp: float = 0.8, ring_hz: float = 320.0,
           seed: int = 3) -> None:
    """A metallic clash: a decaying noise burst plus a ringing partial."""
    start_i = int(start * SAMPLE_RATE)
    count = int(0.22 * SAMPLE_RATE)
    noise = _lowpass(_noise_lcg(count, seed), 0.5)
    for i in range(count):
        t = i / SAMPLE_RATE
        envelope = math.exp(-14.0 * t)
        value = noise[i] * math.exp(-9.0 * t) + 0.4 * math.sin(2.0 * math.pi * ring_hz * t)
        if start_i + i < len(samples):
            samples[start_i + i] += amp * envelope * value


def synth_city_captured() -> list:
    """A rising three-note brass fanfare answered by a low drum - sfx.city_captured."""
    samples = [0.0] * int(1.25 * SAMPLE_RATE)
    _tone(samples, 0.00, 0.30, 220.00, 0.7)
    _tone(samples, 0.28, 0.30, 277.18, 0.7)
    _tone(samples, 0.56, 0.45, 329.63, 0.8)
    _drum(samples, 0.95, freq=65.0, amp=0.9, duration=0.30)
    return samples


def synth_battle() -> list:
    """Overlapping metallic clashes over a low war drum - sfx.battle."""
    samples = [0.0] * int(1.15 * SAMPLE_RATE)
    _drum(samples, 0.00, freq=55.0, amp=0.8, duration=0.40, seed=11)
    for t, seed in ((0.00, 3), (0.16, 4), (0.30, 5), (0.44, 6), (0.62, 8)):
        _clash(samples, t, amp=0.75, seed=seed)
    _drum(samples, 0.50, freq=50.0, amp=0.7, duration=0.50, seed=13)
    return samples


def synth_unit_move() -> list:
    """A steady rhythmic tread of boots on packed earth - sfx.unit_move."""
    samples = [0.0] * int(1.40 * SAMPLE_RATE)
    for step in range(8):
        _drum(samples, step * 0.14, freq=88.0, amp=0.55, duration=0.12,
              seed=17 + step)
    return samples


def wav_bytes(samples: list) -> bytes:
    """Mono 44.1 kHz 16-bit PCM WAV, normalized - the shape of par. 1.4 and of the
    placeholder stubs, so the pipeline stays one format."""
    peak = max(1e-9, max(abs(s) for s in samples))
    normalization = 0.85 / peak
    frames = bytearray()
    for s in samples:
        value = max(-1.0, min(1.0, s * normalization))
        frames += struct.pack("<h", int(round(value * 32767)))
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav_file:
        wav_file.setnchannels(1)
        wav_file.setsampwidth(2)
        wav_file.setframerate(SAMPLE_RATE)
        wav_file.writeframes(bytes(frames))
    return buffer.getvalue()


# --- Self-check: prove the conforming step on synthetic images BEFORE any money -----------


def _parse_bmp_header(data: bytes):
    signature, file_size, _, _, offset = struct.unpack_from("<2sIHHI", data, 0)
    (header_size, width, height, planes, bit_count, compression, image_size,
     _, _, _, _) = struct.unpack_from("<IiiHHIIiiII", data, 14)
    return {
        "signature": signature, "file_size": file_size, "offset": offset,
        "header_size": header_size, "width": width, "height": height,
        "planes": planes, "bit_count": bit_count, "compression": compression,
        "image_size": image_size,
    }


def _check(failures: list, name: str, ok: bool, detail: str = "") -> None:
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" ({detail})" if detail else ""))
    if not ok:
        failures.append(name)


def self_check() -> list:
    """Run the conforming step on synthetic images drawn in code (never downloaded)
    and validate the output bytes against exactly what the C# conformance test will
    check: 32x32, BMP structure, bit depth per kind, and transparency for sprites.
    Runs automatically before every real generation - the conforming step is proved
    before any money is spent."""
    print("Self-check: conforming step proven on synthetic images (in memory, no network)")
    failures = []

    # Synthetic sprite: a near-white disc with a bar inside, on solid black.
    sprite_source = Image.new("RGB", (256, 256), (0, 0, 0))
    draw = ImageDraw.Draw(sprite_source)
    draw.ellipse((56, 56, 200, 200), fill=(235, 235, 235))
    draw.rectangle((116, 116, 140, 200), fill=(255, 255, 255))
    sprite_bmp = conform_sprite(sprite_source)

    header = _parse_bmp_header(sprite_bmp)
    _check(failures, "sprite: 'BM' signature", header["signature"] == b"BM")
    _check(failures, "sprite: file header size matches the file",
           header["file_size"] == len(sprite_bmp), f"{header['file_size']} vs {len(sprite_bmp)}")
    _check(failures, "sprite: pixel array offset is 54", header["offset"] == 54)
    _check(failures, "sprite: DIB header is 40 bytes", header["header_size"] == 40)
    _check(failures, "sprite: 32x32", (header["width"], header["height"]) == (SIZE, SIZE),
           f"{header['width']}x{header['height']}")
    _check(failures, "sprite: 32-bit BGRA", header["bit_count"] == 32)
    _check(failures, "sprite: BI_RGB uncompressed", header["compression"] == 0)
    _check(failures, "sprite: pixel array is 4096 bytes",
           header["image_size"] == SIZE * SIZE * 4 == len(sprite_bmp) - 54)

    # Pixel-level checks, read straight from the BMP bytes (bottom-up rows, BGRA quads):
    # the background must be keyed out (alpha 0 in a corner), the subject must be light
    # (a near-255 grey at the centre) and opaque (alpha 255).
    def sprite_pixel(x: int, y_from_top: int):
        y_from_bottom = SIZE - 1 - y_from_top
        i = 54 + (y_from_bottom * SIZE + x) * 4
        return (sprite_bmp[i + 2], sprite_bmp[i + 1], sprite_bmp[i], sprite_bmp[i + 3])

    corner = sprite_pixel(0, 0)
    centre = sprite_pixel(16, 16)
    _check(failures, "sprite: background keyed out to alpha 0 at a corner",
           corner[3] == 0, f"alpha {corner[3]}")
    _check(failures, "sprite: subject opaque at the centre", centre[3] == 255,
           f"alpha {centre[3]}")
    _check(failures, "sprite: subject normalized light at the centre",
           centre[0] >= 200 and centre[0] == centre[1] == centre[2],
           f"rgb {centre[0]},{centre[1]},{centre[2]}")

    # And an independent parse with Pillow (the reference decoder) of the same bytes.
    # Finding recorded on T51: Pillow 12.3.0 opens a 32-bit BI_RGB BMP as RGB and
    # discards the 4th byte - it does not honour the alpha channel most editors write
    # there. The alpha IS in the file (the byte-level checks above read it at the
    # spec's offsets); reader behaviour varies, which is exactly the open item
    # docs/asset-specification.md par. 1.2 leaves to whichever task first loads a
    # transparent icon on screen (T48/T24's Godot import). The self-check therefore
    # verifies the bytes directly and only asks Pillow for structure/size.
    reopened = Image.open(io.BytesIO(sprite_bmp))
    _check(failures, "sprite: Pillow re-opens it as a valid 32x32 BMP",
           reopened.size == (SIZE, SIZE),
           f"{reopened.mode} {reopened.size} (Pillow reads 32-bit BI_RGB as "
           "RGB - alpha verified from the bytes above, not via Pillow)")
    print("  note: Pillow's BMP reader ignores the 4th byte of a 32-bit BI_RGB BMP; "
          "the alpha channel is verified from the file bytes, per par. 1.2's own open "
          "item about readers.")

    # Synthetic tile: a green base with a brown undulating ridge (par. 4.5's plain).
    tile_source = Image.new("RGB", (256, 256), (60, 200, 60))
    tile_draw = ImageDraw.Draw(tile_source)
    for x in range(256):
        y = 128 + int(22 * math.sin(x / 40.0) * math.sin(x / 17.0))
        tile_draw.line((x, y - 3, x, y + 3), fill=(139, 105, 60))
    tile_bmp = conform_tile(tile_source)

    tile_header = _parse_bmp_header(tile_bmp)
    _check(failures, "tile: 'BM' signature", tile_header["signature"] == b"BM")
    _check(failures, "tile: file header size matches the file",
           tile_header["file_size"] == len(tile_bmp))
    _check(failures, "tile: 32x32", (tile_header["width"], tile_header["height"]) == (SIZE, SIZE))
    _check(failures, "tile: 24-bit opaque", tile_header["bit_count"] == 24)
    _check(failures, "tile: BI_RGB uncompressed", tile_header["compression"] == 0)
    _check(failures, "tile: pixel array is 3072 bytes",
           tile_header["image_size"] == SIZE * SIZE * 3 == len(tile_bmp) - 54)

    # WAV synthesis round-trip: the synthesized cue is a real RIFF/WAVE with the
    # par. 1.4 envelope (mono, 44100 Hz, 16-bit PCM).
    wav = wav_bytes(synth_unit_move())
    _check(failures, "wav: RIFF/WAVE signature",
           wav[:4] == b"RIFF" and wav[8:12] == b"WAVE")
    with wave.open(io.BytesIO(wav)) as wav_file:
        _check(failures, "wav: mono 44100 Hz 16-bit PCM",
               wav_file.getnchannels() == 1 and wav_file.getframerate() == SAMPLE_RATE
               and wav_file.getsampwidth() == 2)

    if failures:
        print(f"Self-check FAILED: {len(failures)} check(s) failed: {failures}")
    else:
        print("Self-check passed: the conforming step is proven before any money is spent.")
    return failures


# --- Config (DoD 2): endpoint/model/key from a git-ignored local config -------------------


def load_config(path: str) -> dict:
    config = {
        "endpoint": DEFAULT_ENDPOINT,
        "model": "",
        "api_key": "",
        "api_key_source": "not set",
        "config_file": path,
        "config_file_present": os.path.exists(path),
    }
    if config["config_file_present"]:
        parser = configparser.ConfigParser()
        with open(path, "r", encoding="utf-8") as handle:
            parser.read_file(handle)
        if parser.has_section("openrouter"):
            for option in ("endpoint", "model"):
                if parser.has_option("openrouter", option):
                    value = parser.get("openrouter", option).strip()
                    if value:
                        config[option] = value
            if parser.has_option("openrouter", "api_key"):
                key = parser.get("openrouter", "api_key").strip()
                if key:
                    config["api_key"] = key
                    config["api_key_source"] = DEFAULT_CONFIG_NAME
        else:
            config["config_error"] = (
                f"{path} exists but has no [openrouter] section - copy "
                "assets-generator.example.ini and fill it in"
            )
    if not config["api_key"] and os.environ.get("OPENROUTER_API_KEY", "").strip():
        config["api_key"] = os.environ["OPENROUTER_API_KEY"].strip()
        config["api_key_source"] = "OPENROUTER_API_KEY environment variable"
    return config


# --- Prompts ------------------------------------------------------------------------------


def load_prompts(path: str) -> list:
    with open(path, "r", encoding="utf-8") as handle:
        document = json.load(handle)
    entries = document.get("prompts")
    if not isinstance(entries, list) or not entries:
        raise ValueError(f"{path}: no prompts array found")
    seen = set()
    for entry in entries:
        for field in ("key", "kind", "prompt", "source"):
            if not isinstance(entry.get(field), str) or not entry[field].strip():
                raise ValueError(f"{path}: entry {entry.get('key')!r} has no {field}")
        if entry["kind"] not in VALID_KINDS:
            raise ValueError(f"{path}: {entry['key']}: kind must be one of {VALID_KINDS}")
        if entry["kind"] != kind_for_key(entry["key"]):
            raise ValueError(
                f"{path}: {entry['key']}: kind {entry['kind']!r} does not match the "
                f"key's own category (expected {kind_for_key(entry['key'])!r})"
            )
        if entry["key"] in seen:
            raise ValueError(f"{path}: duplicate key {entry['key']}")
        seen.add(entry["key"])
    return entries


# --- Provider access ---------------------------------------------------------------------


def fetch_models(endpoint: str) -> list:
    """The public, key-less models list; every model's architecture.output_modalities."""
    url = endpoint.rstrip("/") + "/models"
    request = urllib.request.Request(url, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=MODELS_TIMEOUT_SECONDS) as response:
        document = json.loads(response.read().decode("utf-8"))
    return document.get("data", [])


def image_output_models(models: list) -> list:
    rows = []
    for model in models:
        output_modalities = (model.get("architecture") or {}).get("output_modalities") or []
        if "image" in output_modalities:
            rows.append(model)
    rows.sort(key=lambda m: m.get("id", ""))
    return rows


def _per_token_rate(pricing: dict, field: str) -> float | None:
    raw = pricing.get(field)
    try:
        value = float(raw)
    except (TypeError, ValueError):
        return None
    return value if value >= 0 else None


def format_rate(pricing: dict, field: str) -> str:
    rate = _per_token_rate(pricing, field)
    if rate is None:
        return "n/a"
    if rate == 0:
        return "free"
    return f"${rate * 1e6:.2f}/M tok"


def per_image_estimate(pricing: dict) -> float | None:
    """A rough per-image cost from the live per-token prices on the chat-completions
    image route: prompt tokens + image-output tokens (completion as the fallback field)."""
    prompt_rate = _per_token_rate(pricing, "prompt")
    image_rate = _per_token_rate(pricing, "image_output")
    if image_rate is None:
        image_rate = _per_token_rate(pricing, "completion")
    if prompt_rate is None or image_rate is None:
        return None
    return (ESTIMATED_PROMPT_TOKENS * prompt_rate
            + ESTIMATED_IMAGE_OUTPUT_TOKENS * image_rate)


def call_image_model(config: dict, prompt: str) -> tuple:
    """One billable chat-completions call asking for an image (modalities
    ["image", "text"]); the image returns as a base64 data URL in the assistant
    message's `images` field. Request headers are never echoed; the key is never
    printed. Returns (image_bytes, usage)."""
    url = config["endpoint"].rstrip("/") + "/chat/completions"
    body = {
        "model": config["model"],
        "messages": [{"role": "user", "content": prompt}],
        "modalities": ["image", "text"],
    }
    request = urllib.request.Request(
        url,
        data=json.dumps(body).encode("utf-8"),
        headers={
            "Authorization": "Bearer " + config["api_key"],  # never logged, never echoed
            "Content-Type": "application/json",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT_SECONDS) as response:
            document = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:400]
        raise RuntimeError(
            f"HTTP {error.code} from POST chat/completions; the provider said: {detail}"
        ) from error
    except urllib.error.URLError as error:
        raise RuntimeError(
            f"could not reach {url}: {error.reason}"
        ) from error

    usage = document.get("usage") or {}
    choices = document.get("choices") or []
    if not choices:
        raise RuntimeError("the response has no choices: " + json.dumps(document)[:400])
    message = choices[0].get("message") or {}
    images = message.get("images") or []
    if not images:
        content = message.get("content")
        raise RuntimeError(
            "the model returned no image in message.images"
            + (f"; it said: {content[:200]}" if content else "")
        )
    image_url = images[0].get("image_url") or {}
    data_url = image_url.get("url", "")
    if not data_url.startswith("data:"):
        raise RuntimeError(
            "expected a base64 data URL in message.images[0].image_url.url, got: "
            + data_url[:80]
        )
    encoded = data_url.split(",", 1)[1]
    return base64.b64decode(encoded), usage


# --- Pack output -------------------------------------------------------------------------


def manifest_document(entries: list) -> dict:
    return {
        "schemaVersion": 1,
        "name": "Authored Asset Pack",
        "description": (
            "Generated by scripts/generate-authored-assets.py from assets/prompts.json "
            "(T51): image-model sprites and terrain tiles conformed to "
            "docs/asset-specification.md par. 1 (32x32 BMP; 24-bit opaque terrain; "
            "32-bit BGRA sprites keyed to alpha, tinted per nation at draw time), plus "
            "locally synthesized sound cues. The same shape as the placeholder pack's "
            "manifest."
        ),
        "assets": {entry["key"]: asset_relpath(entry["key"]) for entry in entries},
    }


def write_manifest(out_dir: str, entries: list) -> str:
    path = os.path.join(out_dir, "manifest.json")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest_document(entries), handle, indent=2)
        handle.write("\n")
    return path


def missing_pack_files(out_dir: str, entries: list) -> list:
    return [
        entry["key"]
        for entry in entries
        if not os.path.exists(os.path.join(out_dir, asset_relpath(entry["key"])))
    ]


def acquire_lock(out_dir: str) -> str:
    """The task entry's single-instance hazard, made mechanical: one billable run at a
    time. The lock lives inside the pack directory as runtime state; it is never part
    of the pack - delete it if a crashed run left it behind."""
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, LOCK_FILE_NAME)
    try:
        handle = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        os.write(handle, time.strftime("%Y-%m-%dT%H:%M:%S").encode("ascii"))
        os.close(handle)
    except FileExistsError:
        raise RuntimeError(
            f"another generation run appears to be active ({path} exists). This is a "
            "long, billable, single-instance operation: finish or stop the other run, "
            "then delete the lock file if it is stale."
        ) from None
    return path


def release_lock(path: str) -> None:
    try:
        os.remove(path)
    except OSError:
        pass


# --- Runs ---------------------------------------------------------------------------------


def print_header(title: str) -> None:
    print()
    print(title)
    print("-" * len(title))


def dry_run(config: dict, entries: list, selection: list, models: list | None) -> int:
    print("T51 asset generator - DRY RUN (default mode: nothing is sent, nothing is "
          "written, nothing is billed)")

    print_header("Provider config (assets-generator.local.ini, section [openrouter])")
    print(f"  endpoint : {config['endpoint']}")
    model_note = config["model"] if config["model"] else (
        "not configured - set model = <slug> in " + DEFAULT_CONFIG_NAME
        + "; candidates are listed below")
    print(f"  model    : {model_note}")
    if "config_error" in config:
        print(f"  warning  : {config['config_error']}")
    key_note = ("present (from " + config["api_key_source"] + "; never printed)"
                if config["api_key"] else
                "absent (a dry run needs none; a real run will refuse to start)")
    print(f"  api key  : {key_note}")

    print_header(f"Selection: {len(selection)} of {len(entries)} keys "
                 + "(a real run regenerates exactly these and nothing else)")
    for index, entry in enumerate(selection, start=1):
        kind = entry["kind"]
        note = ""
        if kind == "sfx":
            note = "  [synthesized locally - no API call, no bill]"
        print(f"  {index:2d}. {entry['key']}  [{kind} -> {asset_relpath(entry['key'])}]{note}")
    image_keys = [e for e in selection if e["kind"] != "sfx"]
    sfx_keys = [e for e in selection if e["kind"] == "sfx"]
    print()
    print(f"  Billable images this selection would request: {len(image_keys)} "
          f"({sum(1 for e in image_keys if e['kind'] == 'sprite')} sprites -> 32-bit BGRA "
          f"+ {sum(1 for e in image_keys if e['kind'] == 'tile')} terrain tiles -> 24-bit "
          "opaque)")
    print(f"  Not billed: {len(sfx_keys)} sfx synthesized locally "
          "(deterministic, free)")

    print_header("Every prompt that would be sent (exactly the text of the request)")
    for entry in selection:
        print()
        print(f"  {entry['key']} [{entry['kind']}]:")
        print(f"    prompt: {entry['prompt']}")
        print(f"    source: {entry['source']}")

    print_header("OpenRouter image-output models right now "
                 "(GET /api/v1/models; architecture.output_modalities includes \"image\")")
    if models is None:
        print("  (the public models endpoint could not be reached - run again online "
              "to see the candidates and prices)")
    else:
        image_models = image_output_models(models)
        for model in image_models:
            pricing = model.get("pricing") or {}
            estimate = per_image_estimate(pricing)
            estimate_note = (f"~ ${estimate:.3f}/image at ~{ESTIMATED_IMAGE_OUTPUT_TOKENS} "
                             "image-output tokens" if estimate is not None else
                             "per-image cost not published")
            print(f"  {model.get('id', '?')}")
            print(f"      {model.get('name', '')}")
            print(f"      prompt {format_rate(pricing, 'prompt')}, "
                  f"image output {format_rate(pricing, 'image_output')} | {estimate_note}")
        print(f"  ({len(image_models)} models with image output; prices as published at "
              "this moment - confirm at "
              "https://openrouter.ai/models?output_modalities=image)")

    print_header("Estimated cost of this selection")
    if models is not None:
        pricing_by_id = {
            m.get("id"): m.get("pricing") or {} for m in models
        }
        pricing = pricing_by_id.get(config["model"])
        if not config["model"]:
            print("  cannot estimate - no model configured (pick one above; a real run "
                  "refuses to start without one)")
        elif pricing is None:
            print(f"  cannot estimate - {config['model']} was not in the live models "
                  "list (is the slug right?)")
        elif not image_keys:
            print(f"  nothing billable in this selection (0 images; {len(sfx_keys)} sfx "
                  "are synthesized for free)")
        else:
            estimate = per_image_estimate(pricing)
            if estimate is None:
                print(f"  cannot estimate - no usable published rate for "
                      f"{config['model']}")
            else:
                total = len(image_keys) * estimate
                print(f"  ~ ${total:.2f} for {len(image_keys)} images with "
                      f"{config['model']}")
                print(f"    (live prices: prompt {format_rate(pricing, 'prompt')}, "
                      f"image output {format_rate(pricing, 'image_output')}; assumes ~"
                      f"{ESTIMATED_PROMPT_TOKENS} prompt tokens and ~"
                      f"{ESTIMATED_IMAGE_OUTPUT_TOKENS} image-output tokens per call - "
                      "actual usage is reported per call by the provider)")
    else:
        print("  cannot estimate - the models endpoint was unreachable")

    print_header("Next steps (nothing has been sent or written)")
    print("  A real run is a deliberate act, never a side effect: it bills per image.")
    print("    python scripts/generate-authored-assets.py --key <AssetKey>   "
          "# regenerate just that key (repeatable)")
    print("    python scripts/generate-authored-assets.py --all              "
          "# the full run, all billable images at once")
    print("  The key is read from " + DEFAULT_CONFIG_NAME + " (copy "
          "assets-generator.example.ini) or OPENROUTER_API_KEY,")
    print("  and is never printed, logged, or echoed. The run is single-instance: one "
          "billable run at a time.")
    return 0


def run_real(config: dict, entries: list, selection: list, out_dir: str,
             prompts_path: str) -> int:
    print("T51 asset generator - REAL RUN (billable, network-bound, single-instance)")

    if "config_error" in config:
        print(f"error: {config['config_error']}", file=sys.stderr)
        return 2
    if not config["model"]:
        print(
            f"error: no model configured. Set model = <slug> in the [openrouter] "
            f"section of {DEFAULT_CONFIG_NAME} (copy assets-generator.example.ini). "
            "The provider is not hardcoded - it is named in that config.",
            file=sys.stderr,
        )
        return 2
    if not config["api_key"]:
        print(
            "error: no API key found. Set api_key in the [openrouter] section of "
            f"{DEFAULT_CONFIG_NAME} (copy assets-generator.example.ini; the file is "
            "git-ignored) or export the OPENROUTER_API_KEY environment variable. "
            "The key is never printed or committed. Real runs are billable - use "
            "--dry-run to preview the bill.",
            file=sys.stderr,
        )
        return 2

    # Provider preflight from the public, key-less models list: fail BEFORE billing
    # if the configured model definitively cannot output images.
    models = None
    try:
        models = fetch_models(config["endpoint"])
    except Exception as error:  # offline is a warning, not a stop
        print(f"warning: could not fetch the models list for preflight: {error}")
    if models is not None:
        known = {m.get("id"): m for m in models}
        model = known.get(config["model"])
        if model is None:
            print(f"warning: {config['model']} is not in the live models list - "
                  "proceeding anyway (the list is advisory; check the slug).")
        else:
            modalities = (model.get("architecture") or {}).get("output_modalities") or []
            if "image" not in modalities:
                print(
                    f"error: {config['model']} exists but has no image output "
                    f"(output modalities: {modalities}). An image-capable model must be "
                    "configured - see the dry run's model list.",
                    file=sys.stderr,
                )
                return 2
            print(f"preflight: {config['model']} confirmed image-capable.")

    # Prove the conforming step before any money is spent.
    if self_check():
        print("error: the conforming self-check failed - refusing to spend anything.",
              file=sys.stderr)
        return 1

    lock_path = acquire_lock(out_dir)
    try:
        generated = 0
        for entry in selection:
            key = entry["key"]
            kind = entry["kind"]
            relpath = asset_relpath(key)
            full_path = os.path.join(out_dir, relpath)
            os.makedirs(os.path.dirname(full_path), exist_ok=True)
            existed = os.path.exists(full_path)
            if kind == "sfx":
                synthesizer = SYNTHESIZERS.get(key)
                if synthesizer is None:
                    print(f"error: no local synthesizer for sfx key {key} - add one "
                          "before regenerating it.", file=sys.stderr)
                    return 1
                samples = synthesizer()
                data = wav_bytes(samples)
                with open(full_path, "wb") as handle:
                    handle.write(data)
                duration = len(samples) / SAMPLE_RATE
                print(f"  synthesized {relpath} "
                      f"(mono {SAMPLE_RATE} Hz 16-bit PCM, {duration:.2f}s, "
                      f"{'overwrote' if existed else 'wrote'})")
                generated += 1
                continue
            image_bytes, usage = call_image_model(config, entry["prompt"])
            image = Image.open(io.BytesIO(image_bytes))
            if not image.mode.startswith("RGB"):
                image = image.convert("RGB")
            bmp = conform_sprite(image) if kind == "sprite" else conform_tile(image)
            with open(full_path, "wb") as handle:
                handle.write(bmp)
            depth = "32-bit BGRA" if kind == "sprite" else "24-bit opaque"
            cost = usage.get("cost")
            cost_note = f", reported cost ${float(cost):.4f}" if cost is not None else ""
            print(f"  generated {relpath} (source {image.size[0]}x{image.size[1]} -> "
                  f"{SIZE}x{SIZE} {depth}{' overwrote' if existed else ''}{cost_note})")
            generated += 1

        manifest_path = write_manifest(out_dir, entries)
        print(f"  wrote {os.path.relpath(manifest_path, REPO_ROOT)}")
        missing = missing_pack_files(out_dir, entries)
        if missing:
            print(f"  pack incomplete: {len(missing)} key(s) still have no file:")
            for key in missing:
                print(f"    {key}")
            print("  rerun with those --key values (or --all) to finish the pack.")
        else:
            print("  pack complete: every AssetKeys constant resolves to a file - "
                  "AssetLoader.ValidateAssets will report zero missing.")
        print(f"real run finished: {generated} asset(s) generated into "
              f"{os.path.relpath(out_dir, REPO_ROOT)}")
        return 0
    finally:
        release_lock(lock_path)


SYNTHESIZERS = {
    "sfx.city_captured": synth_city_captured,
    "sfx.battle": synth_battle,
    "sfx.unit_move": synth_unit_move,
}


def main(argv: list | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="generate-authored-assets.py",
        description="T51's prompt-driven asset generator. Dry run by default; a real, "
        "billable run needs --key or --all.",
    )
    parser.add_argument("--dry-run", action="store_true",
                        help="force a dry run even with --key/--all (the default without "
                             "them)")
    parser.add_argument("--all", action="store_true",
                        help="REAL RUN: regenerate every key (22 billable images)")
    parser.add_argument("--key", action="append", default=[], metavar="AssetKey",
                        help="REAL RUN: regenerate only this key (repeatable). Any "
                             "selection without --dry-run is a billable run.")
    parser.add_argument("--self-check", action="store_true",
                        help="run only the conforming self-check on synthetic images "
                             "(no network, no writes, free)")
    parser.add_argument("--config", default=DEFAULT_CONFIG, metavar="PATH",
                        help="local config path (default: " + DEFAULT_CONFIG_NAME
                             + " in the repository root)")
    parser.add_argument("--prompts", default=DEFAULT_PROMPTS, metavar="PATH",
                        help="prompts.json path (default: assets/prompts.json)")
    parser.add_argument("--out", default=DEFAULT_OUT_DIR, metavar="DIR",
                        help="output pack directory (default: assets/packs/authored)")
    args = parser.parse_args(argv)

    entries = load_prompts(args.prompts)
    entries_by_key = {entry["key"]: entry for entry in entries}

    if args.key:
        unknown = [key for key in args.key if key not in entries_by_key]
        if unknown:
            print(f"error: unknown --key value(s): {unknown}. Valid keys (from "
                  f"{args.prompts}):", file=sys.stderr)
            for entry in entries:
                print(f"    {entry['key']}", file=sys.stderr)
            return 2
        selection = [entries_by_key[key] for key in dict.fromkeys(args.key)]
    else:
        selection = list(entries)

    if args.self_check:
        return 1 if self_check() else 0

    real = (args.all or bool(args.key)) and not args.dry_run
    if not real:
        config = load_config(args.config)
        models = None
        try:
            models = fetch_models(config["endpoint"])
        except Exception as error:
            print(f"note: could not fetch the models list ({error}); the listing below "
                  "will say so.")
        return dry_run(config, entries, selection, models)

    if args.all and args.key:
        print("note: --all and --key given together - regenerating every key.")
        selection = list(entries)

    config = load_config(args.config)
    return run_real(config, entries, selection, args.out, args.prompts)


if __name__ == "__main__":
    raise SystemExit(main())
