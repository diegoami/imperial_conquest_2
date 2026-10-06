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
BGRA sprites with the background keyed out to alpha, and T101's 32-bit BGRA ui.command.*
toolbar icons - keyed to alpha but kept full-colour, never neutral-silhouetted), and writes
assets/packs/authored/ with a manifest in the same shape as T11's placeholder pack.

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

Originals and offline re-conforming: every real run first saves the full-resolution image
exactly as the model returned it to rendered/authored-raw/<asset-key>.png (git-ignored),
before any conforming. `--reconform` then rebuilds assets/packs/authored/ (images plus
manifest) from those originals with NO network and NO key - free - so a change to the
conforming step never means paying again. `--key` narrows it; keys with no original are
reported. Sprites are conformed by a chroma key: the prompts ask for the subject on a solid
flat pure magenta (#FF00FF), which is keyed out by HUE (so a light or white subject is
never eaten), the magenta spill is removed from the anti-aliased edge, the result is
cropped to the subject's bounding box, padded to a square, downscaled with Lanczos, and
made a solid silhouette (alpha 255 or 0).

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
import random
import struct
import sys
import time
import urllib.error
import urllib.request
import wave

try:
    from PIL import Image, ImageChops, ImageDraw, ImageFilter
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
# Every real run keeps the full-resolution image exactly as the model returned it, BEFORE
# any conforming, so a change to the conforming step is re-applied offline (--reconform)
# instead of paying for every image again. /rendered/ is git-ignored: never committed.
DEFAULT_RAW_DIR = os.path.join(REPO_ROOT, "rendered", "authored-raw")
HTTP_TIMEOUT_SECONDS = 300  # image generation can take a minute or more per call
MODELS_TIMEOUT_SECONDS = 30
LOCK_FILE_NAME = ".generator.lock"
# The lock is runtime state, so it lives under the git-ignored /rendered/, never inside the
# committed pack directory where a `git add assets/packs/authored` could pick it up.
DEFAULT_LOCK_DIR = os.path.join(REPO_ROOT, "rendered")

# Chroma-key background (sprite prompts ask for a solid flat pure magenta #FF00FF). The key
# is a HUE test, not a distance-to-background test: a pixel's magenta excess is
# min(R, B) - G, which is ~255 on magenta and ~0 on white, grey and light subjects, so a
# light subject can never be eaten. Alpha is 1 - excess / background excess, with a dead
# zone at each end that absorbs the model's background noise.
MAGENTA = (255, 0, 255)
# The model's "magenta" is not exactly #FF00FF (Gemini returned (228, 39, 146), a hot pink,
# whose excess is 107 not 255), so the key is measured against the colour ESTIMATED from the
# frame's border, not against pure magenta: anything saturated on the magenta/pink side
# (red and blue both well above green) is accepted as the chroma background.
MIN_BACKGROUND_EXCESS = 64  # border median min(R,B) - G at least this, or the model ignored the key
MIN_BORDER_FLATNESS = 0.8   # and this fraction of the border ring must be at least half as chroma-y
KEY_OPAQUE_T = 0.10  # excess <= 10% of the background's: fully opaque subject
KEY_CLEAR_T = 0.90   # excess >= 90% of the background's: fully clear background
SPRITE_MARGIN_PX = 1  # transparent margin around the cropped subject, in output pixels
# Solid silhouette cut-off on the downscaled alpha (see conform_sprite_image).
SOLID_ALPHA_CUTOFF = 96

# The high-quality downscale filter, under either of Pillow's spellings:
# Image.Resampling.LANCZOS since Pillow 9.1, Image.LANCZOS before that.
LANCZOS = getattr(getattr(Image, "Resampling", Image), "LANCZOS")
BOX = getattr(getattr(Image, "Resampling", Image), "BOX")

# Cost estimate assumptions for the chat-completions image route (stated in every
# estimate the script prints; actual usage is reported by the provider per call).
ESTIMATED_PROMPT_TOKENS = 120
ESTIMATED_IMAGE_OUTPUT_TOKENS = 1300

VALID_KINDS = ("sprite", "ui", "tile", "overlay", "sfx")


# --- Key conventions (docs/asset-specification.md par. 1.5) ------------------------------


def kind_for_key(key: str) -> str:
    """The asset kind implied by the key's first segment (sanity-checked against prompts.json).

    `ui` is T101's toolbar-command category (`ui.command.<id>.icon`): 32-bit BGRA like a
    sprite, but pictorial and full-colour rather than a neutral silhouette, because the
    draw-time nation tint applies to markers only (docs/asset-specification.md 4.7).
    """
    if key.startswith("terrain.shore."):
        return "overlay"
    if key.startswith("terrain."):
        return "tile"
    if key.startswith("sfx."):
        return "sfx"
    if key.startswith("ui.command."):
        return "ui"
    return "sprite"  # unit icons and army/fleet/city markers


def asset_relpath(key: str) -> str:
    """The pack-relative path for a key, following par. 1.5's convention exactly.

    Mirrors the placeholder pack's own layout so the authored pack is drop-in: the
    folder is the key's first segment (plural `units/` being the shipped exception),
    and the file name is the remaining segments joined by `_`, dropping an
    `.icon`/`.tile` suffix (sfx has no suffix). T148's variant keys carry a `tile`
    segment in the middle (`terrain.plain.tile.2`), so it is dropped wherever it
    appears, not only as the last segment: that key becomes `terrain/plain_2.bmp`.
    """
    segments = key.split(".")
    folder = "units" if segments[0] == "unit" else segments[0]
    name_segments = [segment for segment in segments[1:] if segment not in ("icon", "tile")]
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


# --- Conforming (DoD 5): key the chroma background out, crop, downscale, solidify ---------


class ConformError(Exception):
    """An image the conforming step cannot turn into a sprite (no magenta background,
    or no subject). The raw original is already on disk, so nothing is lost."""


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


def _magenta_excess(rgb: Image.Image) -> Image.Image:
    """Per pixel: min(R, B) - G, clipped at 0 (an 'L' image). Pure magenta scores 255 and
    the model's hot-pink "magenta" about 107.
    White, every grey, and any subject colour without both a strong red AND a strong
    blue component over a weak green one scores ~0, so the test cannot hit a light or
    neutral subject - which is exactly why the prompts ask for magenta, not black."""
    red, green, blue = rgb.split()
    return ImageChops.subtract(ImageChops.darker(red, blue), green)


def _border_flatness(excess: Image.Image, background_excess: float) -> float:
    """The fraction of the border ring whose magenta excess is at least half the
    estimated background's. A flat chroma background is ~100%; a scene that merely has
    a pink median is not."""
    width, height = excess.size
    ring = max(2, min(width, height) // 64)
    strips = [excess.crop((0, 0, width, ring)), excess.crop((0, height - ring, width, height)),
              excess.crop((0, ring, ring, height - ring)),
              excess.crop((width - ring, ring, width, height - ring))]
    floor = int(background_excess * 0.5)
    total = hits = 0
    for strip in strips:
        histogram = strip.histogram()
        total += sum(histogram)
        hits += sum(histogram[floor:])
    return hits / total if total else 0.0


def _alpha_lut(background_excess: float) -> list:
    """Map a pixel's magenta excess to alpha. A pixel that is a blend of alpha `a` of a
    neutral subject over magenta has excess (1 - a) * background_excess exactly, so
    physical alpha is 1 - t with t = excess / background_excess. The dead zones at each
    end (t <= KEY_OPAQUE_T fully opaque, t >= KEY_CLEAR_T fully clear) absorb the
    model's background noise and compression ripple."""
    lut = []
    for excess in range(256):
        t = min(1.0, excess / background_excess)
        a = (KEY_CLEAR_T - t) / (KEY_CLEAR_T - KEY_OPAQUE_T)
        lut.append(int(round(255 * max(0.0, min(1.0, a)))))
    return lut


def key_magenta(img: Image.Image) -> Image.Image:
    """Full-resolution RGBA with the chroma background keyed out to alpha and its spill
    removed from the anti-aliased edge. The key colour is the one MEASURED on the frame's
    border (any saturated magenta/pink), and alpha and despill are scaled to it. Dark
    and light subject pixels alike (excess ~0) stay opaque: the black outline survives.
    Raises ConformError when the border is not a saturated, flat magenta-side chroma."""
    if img.mode in ("RGBA", "LA", "PA") or "transparency" in img.info:
        # A model that already returned transparency: flatten onto pure magenta so
        # one path keys everything.
        flat = Image.new("RGBA", img.size, MAGENTA + (255,))
        flat.alpha_composite(img.convert("RGBA"))
        img = flat
    rgb = img.convert("RGB")
    background = estimate_background(rgb)
    background_excess = min(background[0], background[2]) - background[1]
    if background_excess < MIN_BACKGROUND_EXCESS:
        raise ConformError(
            f"the frame's border is not a saturated magenta/pink chroma background "
            f"(border median rgb {background}); the model ignored the chroma-key background")
    excess = _magenta_excess(rgb)
    flatness = _border_flatness(excess, background_excess)
    if flatness < MIN_BORDER_FLATNESS:
        raise ConformError(
            f"the frame's border is not a flat background (only {flatness:.0%} of it is "
            f"magenta-side chroma; median rgb {background}): a busy scene, not a chroma key")
    alpha = excess.point(_alpha_lut(float(background_excess)))

    # Decontaminate the partially covered edge: pixel = a*fg + (1-a)*bg, so
    # fg = (pixel - (1-a)*bg) / a, with the PHYSICAL a = 1 - t (not the dead-zoned one).
    width, height = rgb.size
    pixels = rgb.load()
    alpha_pixels = alpha.load()
    excess_pixels = excess.load()
    for y in range(height):
        for x in range(width):
            if 0 < alpha_pixels[x, y] < 255:
                t = min(1.0, excess_pixels[x, y] / background_excess)
                physical = max(0.05, 1.0 - t)
                p = pixels[x, y]
                pixels[x, y] = tuple(
                    max(0, min(255, int(round((p[c] - t * background[c]) / physical))))
                    for c in range(3))
    # Despill what decontamination cannot see: pull any remaining magenta cast (red and
    # blue both above green) down to neutral, so no pink fringe survives the tint.
    spill = _magenta_excess(rgb)
    red, green, blue = rgb.split()
    rgb = Image.merge("RGB", (ImageChops.subtract(red, spill), green,
                              ImageChops.subtract(blue, spill)))
    keyed = rgb.convert("RGBA")
    keyed.putalpha(alpha)
    return keyed


def _subject_bbox(alpha: Image.Image):
    """Bounding box (x0, y0, x1, y1) of the subject: the pixels at least half covered,
    ignoring specks. A coarse pass (box-averaged blocks that are >= ~10% covered)
    finds the subject and drops noise; a fine pass then measures it exactly."""
    mask = alpha.point(lambda v: 255 if v >= 128 else 0)
    width, height = mask.size
    block = max(1, min(width, height) // 128)
    coarse = mask.resize((max(1, width // block), max(1, height // block)), BOX)
    coarse_box = coarse.point(lambda v: 255 if v >= 26 else 0).getbbox()
    if coarse_box is None:
        return None
    region = (max(0, (coarse_box[0] - 1) * block), max(0, (coarse_box[1] - 1) * block),
              min(width, (coarse_box[2] + 1) * block), min(height, (coarse_box[3] + 1) * block))
    fine = mask.crop(region).getbbox()
    if fine is None:
        return None
    return (region[0] + fine[0], region[1] + fine[1], region[0] + fine[2], region[1] + fine[3])


def crop_to_subject(keyed: Image.Image) -> Image.Image:
    """Crop the keyed image to the subject's bounding box, pad it to a SQUARE with
    transparency (aspect ratio kept, subject centred) leaving SPRITE_MARGIN_PX at 32 px
    on every side, so the subject fills the tile instead of the middle half of it."""
    box = _subject_bbox(keyed.getchannel("A"))
    if box is None:
        raise ConformError("no subject found after keying the magenta background out")
    subject = keyed.crop(box)
    side = max(subject.size)
    padded_side = int(math.ceil(side * SIZE / (SIZE - 2 * SPRITE_MARGIN_PX)))
    canvas = Image.new("RGBA", (padded_side, padded_side), (0, 0, 0, 0))
    canvas.paste(subject, ((padded_side - subject.size[0]) // 2,
                           (padded_side - subject.size[1]) // 2))
    return canvas


def conform_sprite_image(img: Image.Image) -> Image.Image:
    """A marker/unit sprite as a 32x32 RGBA image: key the flat magenta background out
    to alpha (removing the edge spill), crop to the subject, pad to a square, downscale
    with a high-quality filter, make the silhouette SOLID, and normalize the subject to
    a light neutral silhouette (the nation palette is applied at DRAW time by tinting,
    not baked in). Normalisation is luminance-preserving: the brightest subject pixel is
    stretched to 255 and every pixel keeps its relative brightness, so a black outline
    stays black and the tinted marker reads as a dark outline around a tinted body."""
    canvas = crop_to_subject(key_magenta(img))
    small = canvas.resize((SIZE, SIZE), LANCZOS)  # RGBA resizes premultiplied
    pixels = small.load()
    # Solid silhouette: alpha at or above SOLID_ALPHA_CUTOFF becomes 255, below becomes
    # 0. The cut-off sits below one half so that thin strokes (a spear shaft, a mast) a
    # pixel wide, which straddle two pixels at ~50% coverage each, survive the cut
    # instead of vanishing; it is not lower, so the edge does not fatten by a full
    # pixel. The result has no partial-alpha pixels: crisp pixel-art edges that a
    # draw-time tint cannot wash out.
    opaque = 0
    max_luminance = 0.0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = pixels[x, y]
            if a < SOLID_ALPHA_CUTOFF:
                pixels[x, y] = (0, 0, 0, 0)
                continue
            opaque += 1
            max_luminance = max(max_luminance, 0.299 * r + 0.587 * g + 0.114 * b)
    if opaque == 0:
        raise ConformError("the subject vanished in the downscale")
    scale = 255.0 / max_luminance if max_luminance > 0 else 1.0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            value = max(0, min(255, int(round(
                min(255.0, (0.299 * r + 0.587 * g + 0.114 * b) * scale)))))
            pixels[x, y] = (value, value, value, 255)
    return small


def conform_sprite(img: Image.Image) -> bytes:
    """conform_sprite_image, serialized as a 32-bit BGRA BMP."""
    return bmp_bytes(conform_sprite_image(img), 32)


def conform_tile(img: Image.Image) -> bytes:
    """A terrain tile: fully opaque, downscale to 32x32 with a high-quality filter,
    write a 24-bit BGR BMP (docs/asset-specification.md par. 1.2: a terrain tile
    always fully covers its grid cell)."""
    img = img.convert("RGB").resize((SIZE, SIZE), LANCZOS)
    return bmp_bytes(img, 24)


def conform_ui_icon(img: Image.Image) -> Image.Image:
    """A toolbar-command icon (`ui.command.*`, T101): key the flat magenta background out to
    alpha and crop/pad/downscale exactly like a sprite, but KEEP the subject's own colours.
    A marker is a neutral silhouette so the sixteen-nation palette can be multiplied onto it
    at draw time; a ui.command icon is pictorial, full-colour and never tinted (the task
    entry's Style line, docs/asset-specification.md 4.7) - so the luminance-stretch and solid
    neutral silhouette steps of `conform_sprite_image` deliberately do not apply here. The
    result is 32-bit BGRA with transparency at the edges, which is what
    AuthoredPackConformanceTests' ui.command format/size/transparency check reads."""
    canvas = crop_to_subject(key_magenta(img))
    return canvas.resize((SIZE, SIZE), LANCZOS)


def conform_ui(img: Image.Image) -> bytes:
    """conform_ui_icon, serialized as a 32-bit BGRA BMP."""
    return bmp_bytes(conform_ui_icon(img), 32)


def conform_overlay(img: Image.Image) -> bytes:
    """A shore overlay (`terrain.shore.*`, T148): key the magenta background out to alpha
    exactly like a sprite, but do NOT crop or silhouettes — a shore band's position inside
    the tile is its meaning (the task requires every opaque pixel within 10 px of the named
    edge), so the full frame is kept and only the scale is normalized. 32-bit BGRA with
    straight alpha, docs/asset-specification.md 1.2."""
    keyed = key_magenta(img)
    small = keyed.resize((SIZE, SIZE), LANCZOS)  # RGBA resizes premultiplied
    return bmp_bytes(small, 32)


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


def _synthetic_figure(frame: int, box: tuple, colour: tuple, noise: int = 0,
                      background_colour: tuple = MAGENTA, outline: tuple | None = None):
    """A stick-figure soldier (head, torso, shield, spear) in `colour`, anti-aliased,
    blended over flat magenta in a frame x frame image - drawn in code, never
    downloaded. `box` = (left, top, width, height) of the figure inside the frame.
    Returns (image, figure_mask); the mask is the exact 0..255 coverage, so the caller
    knows the true subject area and bounding box. `noise` adds +/-noise of per-channel
    random ripple to the background only (a model's imperfect flat colour).
    `background_colour` is the chroma colour (the real model returns (228, 39, 146), not
    pure magenta); `outline`, when given, draws a border of that colour around the figure
    (the model's black outline) and the returned mask covers figure plus outline."""
    scale = 4
    left, top, width, height = box
    big = Image.new("L", (frame * scale, frame * scale), 0)
    draw = ImageDraw.Draw(big)

    def rect(x0, y0, x1, y1):
        draw.rectangle([(left + x0 * width) * scale, (top + y0 * height) * scale,
                        (left + x1 * width) * scale, (top + y1 * height) * scale], fill=255)

    def oval(x0, y0, x1, y1):
        draw.ellipse([(left + x0 * width) * scale, (top + y0 * height) * scale,
                      (left + x1 * width) * scale, (top + y1 * height) * scale], fill=255)

    oval(0.30, 0.00, 0.60, 0.16)   # head
    rect(0.28, 0.18, 0.62, 0.62)   # torso
    rect(0.30, 0.62, 0.44, 1.00)   # legs
    rect(0.48, 0.62, 0.60, 1.00)
    oval(0.02, 0.28, 0.26, 0.58)   # shield
    rect(0.80, 0.02, 0.86, 1.00)   # spear shaft
    mask = big.resize((frame, frame), BOX)
    background = Image.new("RGB", (frame, frame), background_colour)
    if noise:
        rng = random.Random(7)
        pixels = background.load()
        for y in range(frame):
            for x in range(frame):
                pixels[x, y] = tuple(
                    max(0, min(255, c + rng.randint(-noise, noise))) for c in background_colour)
    outer = mask
    if outline is not None:
        radius = max(2, frame // 200)
        outer = mask.filter(ImageFilter.MaxFilter(2 * radius + 1)).filter(
            ImageFilter.GaussianBlur(1))
        background = Image.composite(Image.new("RGB", (frame, frame), outline), background, outer)
    image = Image.composite(Image.new("RGB", (frame, frame), colour), background, mask)
    return image, outer


def _sprite_stats(sprite: Image.Image) -> dict:
    """Opaque/partial pixel counts, bounding box and worst magenta cast of a 32x32 sprite."""
    pixels = sprite.load()
    opaque = partial = 0
    cast = 0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b, a = pixels[x, y]
            if a == 255:
                opaque += 1
            elif a > 0:
                partial += 1
            if a > 0:
                cast = max(cast, min(r, b) - g)
    box = sprite.getchannel("A").point(lambda v: 255 if v > 0 else 0).getbbox()
    return {"opaque": opaque, "partial": partial, "box": box, "cast": cast}


def _self_check_run_survives_failures(failures: list) -> None:
    """run_generation with a stubbed model. Every way a key can fail - a no-image reply,
    an HTTP error, a conform rejection, and, AFTER the image was paid for, a save_raw
    OSError and a non-ConformError raised inside conform_image - lands in the failed list
    with its reason; a cost that is not a number is reported as unknown instead of
    crashing; every key is still attempted, the keys after the failures are generated,
    the manifest and the failed-key summary are still written, and the exit code is 1."""
    import contextlib
    import shutil

    global save_raw, conform_image

    good_frame, _ = _synthetic_figure(256, (60, 40, 130, 176), (235, 235, 235))
    good_buffer = io.BytesIO()
    good_frame.save(good_buffer, format="PNG")
    good = good_buffer.getvalue()
    bad_buffer = io.BytesIO()
    Image.new("RGB", (256, 256), (0, 0, 0)).save(bad_buffer, format="PNG")  # no magenta

    # key -> reply: image bytes, (image bytes, usage), or an exception to raise.
    replies = {
        "unit.a.icon": (good, {"cost": 0.04}),
        "unit.b.icon": RuntimeError("the model returned no image in message.images; it said: `"),
        "unit.c.icon": RuntimeError("HTTP 502 from POST chat/completions; the provider said: bad gateway"),
        "unit.d.icon": bad_buffer.getvalue(),
        "unit.e.icon": (good, {"cost": "n/a"}),          # paid for, cost is not a number
        "unit.f.icon": (good, {"cost": 0.04}),           # save_raw fails (patched below)
        "unit.g.icon": (good, {"cost": 0.04}),           # conform_image blows up (patched below)
        "unit.h.icon": (good, {"cost": 0.04}),
    }
    calls = []

    def stub(config, prompt):
        calls.append(prompt)
        reply = replies[prompt]
        if isinstance(reply, Exception):
            raise reply
        if isinstance(reply, tuple):
            return reply
        return reply, {}

    real_save_raw, real_conform_image = save_raw, conform_image

    def failing_save_raw(raw_dir, key, image_bytes, image):
        if key == "unit.f.icon":
            raise OSError("disk full (simulated)")
        return real_save_raw(raw_dir, key, image_bytes, image)

    def failing_conform_image(kind, image):
        if calls[-1] == "unit.g.icon":
            raise KeyError("simulated conforming bug")
        return real_conform_image(kind, image)

    selection = [{"key": key, "kind": "sprite", "prompt": key} for key in replies]
    scratch_root = os.path.join(REPO_ROOT, "rendered")
    os.makedirs(scratch_root, exist_ok=True)
    scratch = os.path.join(scratch_root, "selfcheck-run-%d" % os.getpid())
    pack = os.path.join(scratch, "pack")
    stdout, stderr = io.StringIO(), io.StringIO()
    save_raw, conform_image = failing_save_raw, failing_conform_image
    try:
        with contextlib.redirect_stderr(stderr), contextlib.redirect_stdout(stdout):
            exit_code = run_generation(
                {}, selection, selection, pack, os.path.join(scratch, "raw"),
                call=stub, lock_dir=scratch)
        written = sorted(os.listdir(os.path.join(pack, "units")))
        manifest_written = os.path.exists(os.path.join(pack, "manifest.json"))
        lock_left = os.path.exists(os.path.join(scratch, LOCK_FILE_NAME))
    finally:
        save_raw, conform_image = real_save_raw, real_conform_image
        shutil.rmtree(scratch, ignore_errors=True)
    errors, output = stderr.getvalue(), stdout.getvalue()
    _check(failures, "run: every selected key is attempted after a failure (no abort)",
           len(calls) == 8, str(len(calls)))
    _check(failures, "run: the exit code is 1 when any key failed", exit_code == 1, str(exit_code))
    _check(failures, "run: the keys that could be generated are, and nothing else is written",
           written == ["a.bmp", "e.bmp", "h.bmp"], str(written))
    _check(failures, "run: the manifest is still written after failures", manifest_written)
    _check(failures, "run: the failed-key summary names exactly b, c, d, f and g",
           "5 key(s) FAILED: ['unit.b.icon', 'unit.c.icon', 'unit.d.icon', "
           "'unit.f.icon', 'unit.g.icon']" in errors)
    _check(failures, "run: each failure is reported on stderr with the model's reason",
           "no image in message.images" in errors and "HTTP 502" in errors
           and "unit.d.icon" in errors)
    _check(failures, "run: a save_raw error after payment is recorded, not raised",
           "OSError: disk full (simulated)" in errors)
    _check(failures, "run: a non-ConformError from conform_image is recorded, not raised",
           "KeyError" in errors and "simulated conforming bug" in errors)
    _check(failures, "run: a cost that is not a number is reported as unknown, not a crash",
           "reported cost unknown ('n/a')" in output)
    _check(failures, "run: the lock is released", not lock_left)
    _check(failures, "run: the default lock lives under the git-ignored rendered/, not in the pack",
           os.path.relpath(DEFAULT_LOCK_DIR, REPO_ROOT) == "rendered")


def self_check() -> list:
    """Run the conforming step on synthetic images drawn in code (never downloaded)
    and validate the output bytes against exactly what the C# conformance test will
    check: 32x32, BMP structure, bit depth per kind, and transparency for sprites.
    Runs automatically before every real generation - the conforming step is proved
    before any money is spent."""
    print("Self-check: conforming step proven on synthetic images (no network, no key; "
          "the run case writes a scratch pack under rendered/ and deletes it)")
    failures = []

    # Synthetic sprite: a near-white disc with a bar inside, on solid flat magenta.
    sprite_source = Image.new("RGB", (256, 256), MAGENTA)
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

    # --- The chroma-key conforming rules (the user's first real image was faint, fill
    # --- half the tile and washed out; these prove the fix without spending anything).
    tall_box = (300, 150, 130, 230)  # a small tall subject in the middle of a 800 px frame
    for label, colour in (("light-grey", (215, 215, 215)), ("pure-white", (255, 255, 255))):
        source, mask = _synthetic_figure(800, tall_box, colour)
        sprite = conform_sprite_image(source)
        stats = _sprite_stats(sprite)
        figure_box = mask.point(lambda v: 255 if v >= 128 else 0).getbbox()
        long_side = max(figure_box[2] - figure_box[0], figure_box[3] - figure_box[1])
        padded_side = math.ceil(long_side * SIZE / (SIZE - 2 * SPRITE_MARGIN_PX))
        area = sum(1 for v in mask.tobytes() if v >= 128)
        expected = area / (padded_side * padded_side) * SIZE * SIZE
        visible = stats["opaque"] + stats["partial"]
        _check(failures, f"{label} figure on magenta: mostly opaque, "
               "0 partial pixels outside a thin edge",
               visible > 0 and stats["opaque"] / visible >= 0.9 and stats["partial"] == 0,
               f"{stats['opaque']} opaque, {stats['partial']} partial")
        _check(failures, f"{label} figure on magenta: the light subject was not keyed out "
               "(opaque area within 15% of the drawn figure's)",
               abs(stats["opaque"] - expected) <= 0.15 * expected,
               f"{stats['opaque']} opaque vs ~{expected:.0f} expected")
        box = stats["box"]
        long_out = max(box[2] - box[0], box[3] - box[1]) if box else 0
        _check(failures, f"{label} small subject in a large frame fills the tile "
               "(bounding box long side >= 26 px)", long_out >= 26,
               f"{long_out} px on the long side of {SIZE}")
        if label == "light-grey":
            aspect_in = (figure_box[2] - figure_box[0]) / (figure_box[3] - figure_box[1])
            aspect_out = (box[2] - box[0]) / (box[3] - box[1])
            _check(failures, "crop keeps the aspect ratio (padded to a square, not "
                   "stretched)", abs(aspect_in - aspect_out) <= 0.12,
                   f"{aspect_in:.2f} in vs {aspect_out:.2f} out")
        keyed = key_magenta(source)
        raw = keyed.tobytes()
        keyed_cast = max((min(raw[i], raw[i + 2]) - raw[i + 1])
                         for i in range(0, len(raw), 4) if raw[i + 3] > 0)
        _check(failures, f"{label} figure: no pink fringe survives the keying "
               "(no visible pixel with a magenta cast)",
               keyed_cast <= 8 and stats["cast"] <= 8,
               f"worst cast {keyed_cast} after keying, {stats['cast']} in the 32x32 output")

    # The REAL model's "magenta" is a hot pink, (228, 39, 146) (excess 107, not 255), and
    # its figures are white/light grey WITH BLACK OUTLINES. The key is measured against the
    # border, and the black outline must survive as dark opaque pixels.
    hot_pink = (228, 39, 146)
    source, mask = _synthetic_figure(800, tall_box, (245, 245, 245), noise=3,
                                     background_colour=hot_pink, outline=(8, 8, 8))
    sprite = conform_sprite_image(source)
    stats = _sprite_stats(sprite)
    sprite_pixels = sprite.load()
    dark = sum(1 for y in range(SIZE) for x in range(SIZE)
               if sprite_pixels[x, y][3] == 255 and sprite_pixels[x, y][0] <= 80)
    light = sum(1 for y in range(SIZE) for x in range(SIZE)
                if sprite_pixels[x, y][3] == 255 and sprite_pixels[x, y][0] >= 200)
    figure_box = mask.point(lambda v: 255 if v >= 128 else 0).getbbox()
    long_side = max(figure_box[2] - figure_box[0], figure_box[3] - figure_box[1])
    padded_side = math.ceil(long_side * SIZE / (SIZE - 2 * SPRITE_MARGIN_PX))
    area = sum(1 for v in mask.tobytes() if v >= 128)
    expected = area / (padded_side * padded_side) * SIZE * SIZE
    box = stats["box"]
    long_out = max(box[2] - box[0], box[3] - box[1]) if box else 0
    _check(failures, "hot-pink (228,39,146) background, white figure with a black outline: "
           "keyed against the measured border colour (opaque area within 15% of the "
           "drawn figure plus outline, 0 partial pixels)",
           abs(stats["opaque"] - expected) <= 0.15 * expected and stats["partial"] == 0,
           f"{stats['opaque']} opaque vs ~{expected:.0f} expected, {stats['partial']} partial")
    _check(failures, "hot-pink background: the black outline survives as dark opaque pixels "
           "and the body stays light", dark >= 20 and light >= 30,
           f"{dark} dark (<= 80) and {light} light (>= 200) opaque pixels")
    _check(failures, "hot-pink background: fills the tile and leaves no pink fringe",
           long_out >= 26 and stats["cast"] <= 8,
           f"{long_out} px long side, worst cast {stats['cast']}")
    keyed_hot = key_magenta(source)
    raw = keyed_hot.tobytes()
    cast_hot = max((min(raw[i], raw[i + 2]) - raw[i + 1])
                   for i in range(0, len(raw), 4) if raw[i + 3] > 0)
    _check(failures, "hot-pink background: keyed pixels carry no pink spill (despilled "
           "against the estimated colour)", cast_hot <= 8, f"worst cast {cast_hot}")
    for label, background_colour in (("white", (255, 255, 255)), ("grey", (128, 128, 128)),
                                     ("green", (60, 200, 60))):
        try:
            conform_sprite_image(Image.new("RGB", (256, 256), background_colour))
            rejected = False
        except ConformError:
            rejected = True
        _check(failures, f"a {label} frame is rejected (not a magenta-side chroma)", rejected)
    # A busy border that the background-strength test alone would reject: alternating
    # pink and green bars, green being MORE than half of the ring, so the per-channel
    # median lands on green (no saturated background at all).
    scene = Image.new("RGB", (256, 256), hot_pink)
    scene_draw = ImageDraw.Draw(scene)
    for index in range(0, 256, 16):
        scene_draw.rectangle((index, 0, index + 7, 255), fill=(40, 180, 60))
    try:
        conform_sprite_image(scene)
        rejected = False
    except ConformError:
        rejected = True
    _check(failures, "a busy frame whose border is half green is rejected (weak background)",
           rejected)

    # A busy border that only the FLATNESS test catches: pink is the majority (70%), so the
    # median IS a strong pink and MIN_BACKGROUND_EXCESS passes, but 30% of the ring is
    # green bars, so the ring is not a flat chroma background.
    scene = Image.new("RGB", (256, 256), hot_pink)
    scene_draw = ImageDraw.Draw(scene)
    for index in range(0, 256, 20):
        scene_draw.rectangle((index, 0, index + 5, 255), fill=(40, 180, 60))
    scene_draw.rectangle((100, 90, 156, 170), fill=(235, 235, 235))
    try:
        conform_sprite_image(scene)
        flatness_message = ""
    except ConformError as error:
        flatness_message = str(error)
    _check(failures, "a busy frame with a pink MEDIAN is rejected by the flatness test alone "
           "(MIN_BORDER_FLATNESS)", "not a flat background" in flatness_message,
           flatness_message[:60])

    # The background-strength test alone: a flat but faint pink (excess 20 < 64) around a
    # real subject. Flatness passes, so only MIN_BACKGROUND_EXCESS can reject it.
    faint, _ = _synthetic_figure(600, (200, 120, 160, 300), (100, 100, 100),
                                 background_colour=(255, 200, 220))
    try:
        conform_sprite_image(faint)
        faint_message = ""
    except ConformError as error:
        faint_message = str(error)
    _check(failures, "a flat but faint pink background is rejected by MIN_BACKGROUND_EXCESS",
           "not a saturated" in faint_message, faint_message[:60])

    # Decontamination: a subject pixel half covered over hot pink must come out as the
    # SUBJECT colour, not a blend. A 50% blend of (200,200,200) over (228,39,146) is
    # (214,120,173) before decontamination; after it, it is ~(200,200,200) again.
    blend = Image.new("RGB", (64, 64), hot_pink)
    blend_draw = ImageDraw.Draw(blend)
    blend_draw.rectangle((20, 20, 40, 40), fill=(200, 200, 200))
    blend_draw.line((19, 20, 19, 40), fill=(214, 120, 173))
    edge = key_magenta(blend).load()[19, 30]
    _check(failures, "decontamination: a half-covered edge pixel is restored to the subject "
           "colour (not left as a pink blend)",
           all(abs(edge[c] - 200) <= 12 for c in range(3)) and 100 <= edge[3] <= 156,
           str(edge))

    # The alpha ramp's dead zones (KEY_OPAQUE_T / KEY_CLEAR_T) against a hot-pink key
    # (background excess 107): 10% of it (10) is still fully opaque, 90% of it (97) is
    # already fully clear, and the ramp between them is monotonic and spans 0..255.
    lut = _alpha_lut(107.0)
    _check(failures, "alpha ramp: an excess of 10 (<= 10% of the background's) is fully "
           "opaque, 97 (>= 90%) is fully clear", lut[10] == 255 and lut[97] == 0,
           f"lut[10]={lut[10]} lut[97]={lut[97]}")
    _check(failures, "alpha ramp: monotonic falling, and 11..96 is partial (the ramp exists)",
           all(lut[i] >= lut[i + 1] for i in range(255)) and 0 < lut[53] < 255,
           f"lut[53]={lut[53]}")

    # Luminance stretch: a DIM subject (110 grey) is stretched to a white-ish silhouette
    # and stays neutral, so the draw-time tint has a light silhouette to multiply.
    dim, _ = _synthetic_figure(800, tall_box, (110, 110, 110))
    dim_pixels = conform_sprite_image(dim).load()
    dim_opaque = [dim_pixels[x, y] for y in range(SIZE) for x in range(SIZE)
                  if dim_pixels[x, y][3] == 255]
    _check(failures, "a dim (110 grey) subject is stretched to a light neutral silhouette "
           "(brightest opaque pixel >= 250, all opaque pixels R==G==B)",
           bool(dim_opaque) and max(p[0] for p in dim_opaque) >= 250
           and all(p[0] == p[1] == p[2] for p in dim_opaque),
           f"max {max((p[0] for p in dim_opaque), default=None)}")

    # SPRITE_MARGIN_PX, stated with the literal 1 (the earlier area checks compute their
    # expectation FROM the constant, so they cannot notice it being changed): a full-height
    # subject leaves a transparent row above and below it.
    tall_stats = _sprite_stats(conform_sprite_image(_synthetic_figure(800, tall_box, (215, 215, 215))[0]))
    tall_bounds = tall_stats["box"] or (0, 0, 0, 0)
    _check(failures, "the subject keeps a 1 px transparent margin (box within 1..31 on both "
           "axes) and still fills the tile (long side >= 28)",
           tall_bounds[0] >= 1 and tall_bounds[1] >= 1 and tall_bounds[2] <= SIZE - 1
           and tall_bounds[3] <= SIZE - 1 and tall_bounds[3] - tall_bounds[1] >= 28,
           str(tall_bounds))

    # The speck floor: two lone 1 px dust specks far from the figure are not the subject,
    # so the crop (and therefore the whole sprite) is byte-identical with or without them.
    clean, _ = _synthetic_figure(800, tall_box, (215, 215, 215))
    dusty = clean.copy()
    dusty.putpixel((30, 30), (200, 200, 200))
    dusty.putpixel((770, 770), (200, 200, 200))
    _check(failures, "lone 1 px specks do not move the crop (the speck floor)",
           conform_sprite(clean) == conform_sprite(dusty))

    noisy, _ = _synthetic_figure(600, (200, 120, 160, 300), (200, 200, 200), noise=14)
    noisy_stats = _sprite_stats(conform_sprite_image(noisy))
    _check(failures, "rippled magenta background (+/-14 per channel) still keys out cleanly",
           noisy_stats["partial"] == 0 and 200 <= noisy_stats["opaque"] <= 700
           and noisy_stats["box"] is not None and noisy_stats["box"][0] > 0,
           f"{noisy_stats['opaque']} opaque, {noisy_stats['partial']} partial")

    try:
        conform_sprite_image(Image.new("RGB", (256, 256), (0, 0, 0)))
        rejected = False
    except ConformError:
        rejected = True
    _check(failures, "a frame with no magenta background is rejected, not turned into a "
           "solid block", rejected)

    # Toolbar-command icons (ui.command.*, T101): the chroma key and crop apply, but the
    # subject KEEPS its own colours - a pictorial, full-colour icon, never tinted at draw
    # time (unlike the neutral silhouette a marker gets).
    ui_source = Image.new("RGB", (256, 256), MAGENTA)
    ui_draw = ImageDraw.Draw(ui_source)
    ui_draw.rectangle((72, 72, 184, 184), fill=(196, 64, 40))
    ui_draw.rectangle((104, 104, 152, 152), fill=(40, 80, 200))
    ui_bmp = conform_ui(ui_source)
    ui_header = _parse_bmp_header(ui_bmp)
    _check(failures, "ui icon: 'BM' signature", ui_header["signature"] == b"BM")
    _check(failures, "ui icon: file header size matches the file",
           ui_header["file_size"] == len(ui_bmp))
    _check(failures, "ui icon: 32x32", (ui_header["width"], ui_header["height"]) == (SIZE, SIZE))
    _check(failures, "ui icon: 32-bit BGRA", ui_header["bit_count"] == 32)
    _check(failures, "ui icon: BI_RGB uncompressed", ui_header["compression"] == 0)
    _check(failures, "ui icon: pixel array is 4096 bytes",
           ui_header["image_size"] == SIZE * SIZE * 4 == len(ui_bmp) - 54)
    # Read the bytes directly: Pillow's 32-bit BI_RGB reader drops the fourth byte.
    def bmp_pixel(bmp_bytes: bytes, x: int, y_from_top: int):
        y_from_bottom = SIZE - 1 - y_from_top
        i = 54 + (y_from_bottom * SIZE + x) * 4
        return (bmp_bytes[i + 2], bmp_bytes[i + 1], bmp_bytes[i], bmp_bytes[i + 3])
    ui_corner = bmp_pixel(ui_bmp, 0, 0)
    ui_centre = bmp_pixel(ui_bmp, 16, 16)
    _check(failures, "ui icon: background keyed out to alpha 0 at a corner",
           ui_corner[3] == 0, f"alpha {ui_corner[3]}")
    _check(failures, "ui icon: the centre is opaque and keeps a saturated non-neutral colour "
           "(pictorial, not a light silhouette)",
           ui_centre[3] == 255 and max(ui_centre[:3]) - min(ui_centre[:3]) >= 60,
           f"rgba {ui_centre}")
    # A real --key or --reconform run conforms through conform_image's `if kind == "ui"`
    # dispatch, not a direct conform_ui call. Exercise the dispatch here, so deleting it
    # fails the self-check rather than silently writing these icons as 24-bit opaque tiles
    # after the image has been paid for.
    ui_dispatch_bmp, ui_depth = conform_image("ui", ui_source)
    _check(failures, "ui icon: conform_image('ui', ...) returns a 32-bit BGRA result",
           _parse_bmp_header(ui_dispatch_bmp)["bit_count"] == 32,
           f"{_parse_bmp_header(ui_dispatch_bmp)['bit_count']}-bit")
    _check(failures, "ui icon: conform_image('ui', ...) keys a corner to alpha 0",
           bmp_pixel(ui_dispatch_bmp, 0, 0)[3] == 0,
           f"alpha {bmp_pixel(ui_dispatch_bmp, 0, 0)[3]}")
    _check(failures, "ui icon: conform_image('ui', ...) reports the full-colour depth label",
           ui_depth == "32-bit BGRA (full-colour icon)", str(ui_depth))
    _check(failures, "ui icon: conform_image('ui', ...) matches a direct conform_ui",
           ui_dispatch_bmp == ui_bmp)

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

    # Run robustness: one key failing must not abort the run. call_image_model is stubbed,
    # so this uses no network and no key; the scratch pack goes under rendered/ (never TEMP).
    _self_check_run_survives_failures(failures)

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
            "docs/asset-specification.md par. 1 (32x32 BMP; 24-bit opaque terrain tiles; "
            "32-bit BGRA sprites keyed to alpha and tinted per nation at draw time; 32-bit "
            "BGRA transparent shore overlays), plus "
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


def acquire_lock(lock_dir: str = DEFAULT_LOCK_DIR) -> str:
    """The task entry's single-instance hazard, made mechanical: one billable run at a
    time. The lock lives under the git-ignored rendered/ (DEFAULT_LOCK_DIR), never in the
    pack - delete it if a crashed run left it behind."""
    os.makedirs(lock_dir, exist_ok=True)
    path = os.path.join(lock_dir, LOCK_FILE_NAME)
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
          f"+ {sum(1 for e in image_keys if e['kind'] == 'ui')} toolbar icons -> 32-bit BGRA "
          f"+ {sum(1 for e in image_keys if e['kind'] == 'tile')} terrain tiles -> 24-bit "
          f"opaque + {sum(1 for e in image_keys if e['kind'] == 'overlay')} shore overlays "
          "-> 32-bit BGRA)")
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
             prompts_path: str, raw_dir: str = DEFAULT_RAW_DIR) -> int:
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

    return run_generation(config, entries, selection, out_dir, raw_dir)


def run_generation(config: dict, entries: list, selection: list, out_dir: str, raw_dir: str,
                   call=None, lock_dir: str = DEFAULT_LOCK_DIR) -> int:
    """The billable part of a real run, after every preflight: take the lock, generate
    the selection (a per-key failure never aborts it), ALWAYS write the manifest, report
    what is still missing and every failed key, and return the exit code (1 when any key
    failed). `call` and `lock_dir` exist so a self-check can stub the network."""
    lock_path = acquire_lock(lock_dir)
    try:
        generated, failed = generate_selection(config, selection, out_dir, raw_dir, call=call)

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
        if failed:
            print(f"  {len(failed)} key(s) FAILED: {[key for key, _ in failed]}.",
                  file=sys.stderr)
            for key, reason in failed:
                print(f"    {key}: {reason}", file=sys.stderr)
            print("  Rerun just those with --key <AssetKey> (a failed call is usually not "
                  "billed); a key whose original was kept but would not conform can be "
                  "fixed with --reconform (free) instead of paying again.", file=sys.stderr)
            return 1
        return 0
    finally:
        release_lock(lock_path)


def _cost_note(usage) -> str:
    """", reported cost $x.xxxx", ", reported cost unknown ('n/a')" for a value that is
    not a number, or "" when the provider reported none. Never raises."""
    cost = usage.get("cost") if isinstance(usage, dict) else None
    if cost is None:
        return ""
    try:
        return f", reported cost ${float(cost):.4f}"
    except (TypeError, ValueError):
        return f", reported cost unknown ({cost!r})"


def generate_selection(config: dict, selection: list, out_dir: str, raw_dir: str,
                       call=None) -> tuple:
    """Generate every selected key. One key failing (the model returned no image, an HTTP
    or network error, an undecodable image, a conform rejection) is reported on stderr
    with its reason and the run CONTINUES with the next key: a crash on key 20 must not
    strand the paid-for keys 21-40. Returns (generated_count, [(key, reason), ...]);
    the caller exits 1 when the list is not empty. `call` defaults to call_image_model
    and exists so a self-check can stub the network."""
    call = call or call_image_model
    generated = 0
    failed = []
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
                reason = "no local synthesizer for this sfx key - add one before regenerating it"
                failed.append((key, reason))
                print(f"  FAILED {key}: {reason}", file=sys.stderr)
                continue
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
        try:
            image_bytes, usage = call(config, entry["prompt"])
            image = Image.open(io.BytesIO(image_bytes))
            image.load()
        except Exception as error:  # noqa: BLE001 - any per-key failure must not abort the run
            reason = f"{type(error).__name__}: {error}"
            failed.append((key, reason))
            print(f"  FAILED {key}: {reason}. Nothing was written to the pack for it; "
                  "continuing with the next key.", file=sys.stderr)
            continue
        # The image was paid for, so from here on ANY failure is recorded for this key and
        # the run continues (a bad cost figure, a full disk and a conforming bug included).
        cost_note = _cost_note(usage)
        raw_path = None
        try:
            # Keep the original BEFORE conforming: every later conforming change is
            # applied to it offline (--reconform).
            raw_path = save_raw(raw_dir, key, image_bytes, image)
            bmp, depth = conform_image(kind, image)
            temp_path = full_path + ".tmp"
            try:
                with open(temp_path, "wb") as handle:
                    handle.write(bmp)
                os.replace(temp_path, full_path)
            finally:
                if os.path.exists(temp_path):
                    os.remove(temp_path)
        except Exception as error:  # noqa: BLE001 - see above
            kept = (f"The original is kept at {os.path.relpath(raw_path, REPO_ROOT)}"
                    if raw_path else "The original could not be saved")
            if isinstance(error, ConformError):
                reason = f"could not be conformed: {error}"
                label = "FAILED to conform"
            else:
                reason = f"{type(error).__name__}: {error}"
                label = "FAILED (after the image was paid for)"
            failed.append((key, reason))
            print(f"  {label} {key}: {error if isinstance(error, ConformError) else reason}. "
                  f"{kept}{cost_note}; nothing was written to the pack for it; "
                  "continuing with the next key.", file=sys.stderr)
            continue
        print(f"  generated {relpath} (source {image.size[0]}x{image.size[1]} kept at "
              f"{os.path.relpath(raw_path, REPO_ROOT)} -> {SIZE}x{SIZE} {depth}"
              f"{' overwrote' if existed else ''}{cost_note})")
        generated += 1
    return generated, failed


def raw_path_for(raw_dir: str, key: str) -> str:
    return os.path.join(raw_dir, key + ".png")


def save_raw(raw_dir: str, key: str, image_bytes: bytes, image: Image.Image) -> str:
    """Write the full-resolution image as the model returned it (PNG bytes are kept
    verbatim; any other format is re-encoded losslessly as PNG, keeping its pixels)."""
    os.makedirs(raw_dir, exist_ok=True)
    path = raw_path_for(raw_dir, key)
    if image.format == "PNG":
        with open(path, "wb") as handle:
            handle.write(image_bytes)
    else:
        image.save(path, format="PNG")
    return path


def conform_image(kind: str, image: Image.Image) -> tuple:
    """The conforming step for one image kind: (BMP bytes, depth description)."""
    if kind == "sprite":
        return conform_sprite(image), "32-bit BGRA"
    if kind == "ui":
        return conform_ui(image), "32-bit BGRA (full-colour icon)"
    if kind == "overlay":
        return conform_overlay(image), "32-bit BGRA (transparent overlay)"
    return conform_tile(image), "24-bit opaque"


def run_reconform(entries: list, selection: list, out_dir: str, raw_dir: str,
                  write: bool = True) -> int:
    """Offline re-conform: no network, no key, no bill. Rebuilds the pack (images plus
    manifest) from the originals in `raw_dir` for every selected key that has one, and
    reports every selected image key that has none."""
    print("T51 asset generator - RECONFORM (offline: no network, no key, no bill)"
          + ("" if write else " [dry run: nothing is written]"))
    print(f"  originals : {os.path.relpath(raw_dir, REPO_ROOT)}")
    print(f"  pack      : {os.path.relpath(out_dir, REPO_ROOT)}")
    lock_path = acquire_lock() if write else None
    try:
        done = []
        no_raw = []
        failed = []
        for entry in selection:
            key = entry["key"]
            kind = entry["kind"]
            if kind == "sfx":
                continue  # synthesized locally, has no original
            raw_path = raw_path_for(raw_dir, key)
            if not os.path.exists(raw_path):
                no_raw.append(key)
                continue
            try:
                with Image.open(raw_path) as opened:
                    opened.load()
                    image = opened.copy()
                bmp, depth = conform_image(kind, image)
            except ConformError as error:
                failed.append(key)
                print(f"  FAILED {key}: {error}", file=sys.stderr)
                continue
            relpath = asset_relpath(key)
            if write:
                full_path = os.path.join(out_dir, relpath)
                os.makedirs(os.path.dirname(full_path), exist_ok=True)
                with open(full_path, "wb") as handle:
                    handle.write(bmp)
            done.append(key)
            verb = "reconformed" if write else "would reconform"
            print(f"  {verb} {relpath} ({image.size[0]}x{image.size[1]} -> "
                  f"{SIZE}x{SIZE} {depth})")
        if write:
            manifest_path = write_manifest(out_dir, entries)
            print(f"  wrote {os.path.relpath(manifest_path, REPO_ROOT)}")
        print(f"reconform finished: {len(done)} reconformed, {len(no_raw)} with no original, "
              f"{len(failed)} failed")
        if no_raw:
            print("  no original in " + os.path.relpath(raw_dir, REPO_ROOT)
                  + " (generate these with --key; they are not re-conformable):")
            for key in no_raw:
                print(f"    {key}")
        return 1 if failed else 0
    finally:
        if lock_path:
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
                        help="REAL RUN: regenerate every key (86 billable images: 15 sprites, "
                             "36 toolbar icons, 31 terrain tiles, 4 shore overlays; 3 sfx "
                             "synthesized locally, not billed)")
    parser.add_argument("--key", action="append", default=[], metavar="AssetKey",
                        help="REAL RUN: regenerate only this key (repeatable). Any "
                             "selection without --dry-run is a billable run.")
    parser.add_argument("--reconform", action="store_true",
                        help="OFFLINE and FREE (no network, no key): rebuild the pack "
                             "(images plus manifest) from the full-resolution originals "
                             "every real run saves in rendered/authored-raw/. Use it after "
                             "tuning the conforming step. --key selects which keys; keys "
                             "with no original are reported, not re-conformable. Combined "
                             "with --dry-run it writes nothing.")
    parser.add_argument("--self-check", action="store_true",
                        help="run only the conforming self-check on synthetic images "
                             "(no network, no key, free; writes only a scratch pack under rendered/, which it deletes)")
    parser.add_argument("--config", default=DEFAULT_CONFIG, metavar="PATH",
                        help="local config path (default: " + DEFAULT_CONFIG_NAME
                             + " in the repository root)")
    parser.add_argument("--prompts", default=DEFAULT_PROMPTS, metavar="PATH",
                        help="prompts.json path (default: assets/prompts.json)")
    parser.add_argument("--out", default=DEFAULT_OUT_DIR, metavar="DIR",
                        help="output pack directory (default: assets/packs/authored)")
    parser.add_argument("--raw", default=DEFAULT_RAW_DIR, metavar="DIR",
                        help="where full-resolution originals are kept and re-conformed "
                             "from (default: rendered/authored-raw, git-ignored)")
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

    if args.reconform:
        return run_reconform(entries, selection, args.out, args.raw,
                             write=not args.dry_run)

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
    return run_real(config, entries, selection, args.out, args.prompts, args.raw)


if __name__ == "__main__":
    raise SystemExit(main())
