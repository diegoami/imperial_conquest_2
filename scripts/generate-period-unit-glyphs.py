#!/usr/bin/env python3
"""T271: original-authored chariot symbols in the existing native glyph system.

No originals, network, randomness or billed generation. Reuses T182's integer
rasterizer, horse motif, three-size tint frame and BMP writer. Writes eight
optional visual keys per pack, preserving all existing assets/manifest entries.
--verify writes nothing. --board emits an ignored PNG contact sheet for inspection.
"""
import argparse
import importlib.util
import json
import struct
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("native_glyphs", ROOT / "scripts/generate-unit-glyphs.py")
native = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(native)
VISUALS = ("light_chariot", "heavy_chariot")


def symbol(heavy):
    d = native.Raster(26)
    white, blue = native.WHITE, native.BLUE
    horse = native.motif("light_cavalry").resize(13)
    # One visible wheel in side view, drawn as a rim/hub/spokes, not a four-wheel wagon.
    for offset in ((12, 2), (9, 0)) if heavy else ((12, 3),):
        for y in range(13):
            for x in range(13):
                pixel = horse.pixels[y * 13 + x]
                if pixel[3]:
                    d.point((x + offset[0], y + offset[1]), pixel)
    d.line([(9, 14), (22, 12)], white, width=2)
    d.polygon([(1, 10), (11, 10), (13, 17), (2, 17)], white)
    d.line([(3, 13), (10, 13)], blue, width=2)
    d.ellipse((2, 16, 12, 25), white)
    d.ellipse((4, 18, 10, 23), (0, 0, 0, 0))
    d.line([(7, 17), (7, 24)], blue, width=1)
    d.line([(3, 21), (11, 21)], blue, width=1)
    d.ellipse((5, 19, 8, 22), white)
    d.ellipse((2, 2, 7, 7), white)
    d.line([(5, 7), (5, 13)], white, width=2)
    d.line([(6, 8), (17, 9)], blue, width=1)
    if heavy:
        d.polygon([(1, 7), (10, 7), (10, 14), (5, 17), (1, 14)], white)
        d.line([(5, 9), (5, 14)], blue, width=2)
    return d


def strategic(heavy):
    glyph = symbol(heavy)
    out = native.Raster(32)
    # Neutral sprite with black separation, as in the existing tintable strategic vocabulary.
    for y in range(26):
        for x in range(26):
            if glyph.pixels[y * 26 + x][3]:
                out.rectangle((x + 2, y + 2, x + 4, y + 4), (0, 0, 0, 255))
    for y in range(26):
        for x in range(26):
            if glyph.pixels[y * 26 + x][3]:
                out.point((x + 3, y + 3), native.WHITE)
    return out


def entries():
    for visual in VISUALS:
        heavy = visual == "heavy_chariot"
        yield f"unit.{visual}.icon", f"units/{visual}.bmp", strategic(heavy)
        for tier, size in enumerate(native.SIZES):
            yield f"battle.unit.{visual}.{size}", f"battle/unit_{visual}_{size}.bmp", native.battle_glyph(symbol(heavy), tier)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--board", type=Path)
    args = parser.parse_args()
    items = list(entries())
    if args.board:
        width, height = 512, 256
        board = [(220, 208, 180, 255)] * (width * height)
        def paste(image, left, top):
            for y in range(image.side):
                for x in range(image.side):
                    pixel = image.pixels[y * image.side + x]
                    if pixel[3]:
                        board[(top + y) * width + left + x] = pixel
        for row, visual in enumerate(VISUALS):
            for column, (_, _, glyph) in enumerate(item for item in items if visual in item[0]):
                paste(glyph.resize(96), column * 128, row * 128)
                paste(glyph, column * 128 + 96, row * 128 + 96)
        def chunk(kind, data):
            return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
        raw = b"".join(b"\0" + bytes(channel for pixel in board[y * width:(y + 1) * width] for channel in pixel) for y in range(height))
        args.board.parent.mkdir(parents=True, exist_ok=True)
        args.board.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
                              + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))
        print("Board: light/heavy rows; strategic, small, medium, large columns; 3x and native views.")
    failures = []
    for pack in ("authored", "placeholder"):
        directory = ROOT / "assets/packs" / pack
        manifest_path = directory / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        for key, relative, glyph in items:
            path = directory / relative
            data = native.bmp_bytes(glyph)
            if args.verify:
                if manifest["assets"].get(key) != relative or not path.exists() or path.read_bytes() != data:
                    failures.append(f"{pack}: {key}")
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
                manifest["assets"][key] = relative
        if not args.verify:
            manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    if failures:
        print("MISMATCH: " + ", ".join(failures))
        return 1
    print("Verified" if args.verify else "Generated", "16 optional period-unit BMPs; existing glyphs preserved.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
