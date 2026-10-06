#!/usr/bin/env python3
"""T148 Done-when 4: measure the shallow rim's luminance against the deep sea's.

The windowed ``TerrainSurfaceCheck`` run (``IC2_SCREENSHOT_DIR`` set, 2560x1351) saves
``05-rim-probe.png``: a scripted 40 x 20 grid, plain (code 2) in columns 0-19 and sea (code 0) in
columns 20-39, drawn through the surface shader at exactly 32 px a cell with the grid's top-left cell
at the screen's top-left. This script reads it and takes the mean Rec. 709 luminance
(``0.2126 R + 0.7152 G + 0.0722 B`` on 0-255 values) over two regions, rows 5-14 of cells in both:

* the **rim**: the pixel columns of cells 20.0-21.0 -- the first sea cell, surf and shallow tint
  included, as drawn;
* the **deep sea**: the pixel columns of cells 30.0-38.0.

The rim must be at most **twice** the deep sea's (the user's decision of 2026-10-06 at the second
escalation; round 3 measured 3.3x). Exits non-zero when it is not.

Usage: ``python scripts/measure-rim.py [path/to/05-rim-probe.png]``
"""

from __future__ import annotations

import sys

from PIL import Image

CELL_PIXELS = 32
RIM_FIRST_CELL = 20
RIM_LAST_CELL = 21
DEEP_FIRST_CELL = 30
DEEP_LAST_CELL = 38
FIRST_ROW = 5
LAST_ROW_EXCLUSIVE = 15

DEFAULT_PROBE = "rendered/t148-review-r4/05-rim-probe.png"


def mean_luminance(image: Image.Image, x0: int, y0: int, x1: int, y1: int) -> float:
    """The mean Rec. 709 luminance of the [x0, x1) x [y0, y1) pixel region."""
    pixels = image.load()
    total = 0.0
    count = 0
    for y in range(y0, y1):
        for x in range(x0, x1):
            red, green, blue = pixels[x, y][:3]
            total += (0.2126 * red) + (0.7152 * green) + (0.0722 * blue)
            count += 1
    return total / count if count else 0.0


def main(argv: list[str]) -> int:
    path = argv[1] if len(argv) > 1 else DEFAULT_PROBE
    image = Image.open(path).convert("RGB")
    width, height = image.size

    need_width = DEEP_LAST_CELL * CELL_PIXELS
    need_height = LAST_ROW_EXCLUSIVE * CELL_PIXELS
    if width < need_width or height < need_height:
        print(f"FAIL: {path} is {width}x{height}, too small for the {need_width}x{need_height} regions")
        return 1

    rim = mean_luminance(
        image,
        RIM_FIRST_CELL * CELL_PIXELS,
        FIRST_ROW * CELL_PIXELS,
        RIM_LAST_CELL * CELL_PIXELS,
        LAST_ROW_EXCLUSIVE * CELL_PIXELS,
    )
    deep = mean_luminance(
        image,
        DEEP_FIRST_CELL * CELL_PIXELS,
        FIRST_ROW * CELL_PIXELS,
        DEEP_LAST_CELL * CELL_PIXELS,
        LAST_ROW_EXCLUSIVE * CELL_PIXELS,
    )
    ratio = rim / deep if deep > 0.0 else float("inf")

    print(f"rim luminance  = {rim:.2f}  (cells {RIM_FIRST_CELL}.0-{RIM_LAST_CELL}.0, rows {FIRST_ROW}-{LAST_ROW_EXCLUSIVE - 1})")
    print(f"deep luminance = {deep:.2f}  (cells {DEEP_FIRST_CELL}.0-{DEEP_LAST_CELL}.0, rows {FIRST_ROW}-{LAST_ROW_EXCLUSIVE - 1})")
    print(f"rim / deep     = {ratio:.3f}  (limit 2.0)")

    if ratio > 2.0:
        print("FAIL: the rim is more than twice the deep sea")
        return 1

    print("PASS: the rim is at most twice the deep sea")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
