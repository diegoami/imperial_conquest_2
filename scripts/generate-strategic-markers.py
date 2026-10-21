#!/usr/bin/env python3
"""Draw the strategic-map army and fleet markers: {army,fleet}/tier1..3.bmp, recolour templates.

The owner's request of 2026-10-21: the markers should give the general idea of the original's (a
soldier, a ship or a building on a square in the owner's colours, larger as it grows), without
being the same. These are drawn here from shapes, not traced from the original. Fleets go from a
small sailboat to a galley with three shields and a larger galley with four, as the original's do
(docs/asset-specification.md §3.2). Towns go from a house to a hall, a castle and a walled town with
a keep, and a capital is a columned temple; a town's (background, outline, fill) are the same three
colours (§3.3).

It also draws the terrain tiles the map's "Classic tiles" style lays one per cell (terrain/*.bmp,
24-bit opaque, seamless at the cell edges): plains with grass strokes, a forest of round trees,
mountain peaks with snow, desert sand with specks and dunes, and calm and rough sea with waves.

Each 32x32 template uses the three colours the original's recolour replaces (BattleIconTint,
docs/asset-specification.md §2): purple 0x800080 is the field (the owner's first colour), white the
outline, limbs and weapon (the second), blue 0x0000FF the helmet, body, shield and sail (the
third). A fleet's hull is black, which the recolour keeps, as the original's ship hulls are dark.

    python3 scripts/generate-strategic-markers.py [--out assets/packs/authored] [--preview out.png]
"""
import argparse
import os
import struct

SIZE = 32
FIELD, OUTLINE, FILL, HULL = (0x80, 0x00, 0x80), (0xFF, 0xFF, 0xFF), (0x00, 0x00, 0xFF), (0x00, 0x00, 0x00)

# Per tier: the soldier grows, the shield widens and the weapon gets heavier (spear, ball-tipped
# spear, mace-headed staff), as the original's three bands do (asset-specification.md §3.1).
# Boxes are (x, y, width, height) on the 32x32 grid.
TIERS = {
    1: dict(head=(14, 8, 4, 4), body=(13, 13, 6, 7), shield=(17, 14, 5, 6), feet=28, spear_x=9, spear_top=6,
            tip="point", crest=False, emblem=None),
    2: dict(head=(14, 6, 5, 5), body=(12, 12, 8, 8), shield=(17, 13, 7, 9), feet=29, spear_x=8, spear_top=4,
            tip="ball", crest=False, emblem="boss"),
    3: dict(head=(13, 3, 6, 5), body=(10, 11, 10, 9), shield=(17, 11, 10, 12), feet=29, spear_x=6, spear_top=3,
            tip="mace", crest=True, emblem="cross"),
}


def box(x, y, w, h, cut=()):
    """The cells of a box, less the named corners ('tl', 'tr', 'bl', 'br')."""
    cells = {(xx, yy) for yy in range(y, y + h) for xx in range(x, x + w)}
    corners = {"tl": (x, y), "tr": (x + w - 1, y), "bl": (x, y + h - 1), "br": (x + w - 1, y + h - 1)}
    return cells - {corners[c] for c in cut}


def heater(x, y, w, h):
    """A heater shield: square shoulders, sides that curve in over the lower third to a point."""
    cells = set()
    straight = h - (w + 1) // 2
    for yy in range(h):
        inset = 0 if yy < straight else yy - straight + 1
        for xx in range(inset, w - inset):
            cells.add((x + xx, y + yy))
    return cells - {(x, y), (x + w - 1, y)}


def draw(tier):
    t = TIERS[tier]
    lines = set()
    hx, hy, hw, hh = t["head"]
    bx, by, bw, bh = t["body"]
    qx, qy, qw, qh = t["shield"]
    feet, sx, top = t["feet"], t["spear_x"], t["spear_top"]

    # Limbs and weapon are white strokes, drawn under the outlined shapes.
    left, right = bx + 1, bx + bw - 2
    for yy in range(by + bh - 1, feet + 1):
        lines |= {(left, yy), (right, yy)}
    lines |= {(left - 1, feet), (right + 1, feet)}
    lines |= {(xx, by + 2) for xx in range(sx + 1, bx)}
    lines |= {(sx, yy) for yy in range(top, feet + 1)}
    if t["tip"] == "point":
        lines |= {(sx, top - 2), (sx - 1, top), (sx + 1, top), (sx, top - 1)}
    elif t["tip"] == "ball":
        lines |= box(sx - 1, top - 2, 3, 3)
    else:
        lines |= box(sx - 1, top - 2, 3, 4) | {(sx - 2, top - 1), (sx + 2, top - 1), (sx, top - 3)}
    lines |= {(hx + hw // 2, yy) for yy in range(hy + hh, by)}  # the neck

    # Outlined shapes, back to front: body, head (and crest), shield.
    head = box(hx, hy, hw, hh, cut=("tl", "tr"))
    if t["crest"]:
        head |= box(hx + 1, hy - 2, hw - 2, 2)
    shapes = [box(bx, by, bw, bh, cut=("bl", "br")), head,
              heater(qx, qy, qw, qh) if qh >= 8 else box(qx, qy, qw, qh, cut=("tl", "tr", "bl", "br"))]

    image = [[FIELD] * SIZE for _ in range(SIZE)]

    def paint(cells, colour):
        for (x, y) in cells:
            if 0 <= x < SIZE and 0 <= y < SIZE:
                image[y][x] = colour

    paint(lines, OUTLINE)
    for shape in shapes:
        ring = {(x + dx, y + dy) for (x, y) in shape for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - shape
        paint(ring, OUTLINE)
        paint(shape, FILL)

    # Two eye slits in the helmet, and the shield's emblem.
    eye_y = hy + (1 if hh < 5 else 2)
    paint({(hx + 1, eye_y), (hx + hw - 2, eye_y)}, OUTLINE)
    cx, cy = qx + qw // 2, qy + (qh - 2) // 2
    if t["emblem"] == "boss":
        paint(box(cx - 1, cy - 1, 2, 2), OUTLINE)
    elif t["emblem"] == "cross":
        paint({(cx, yy) for yy in range(qy + 2, qy + qh - 3)} | {(xx, cy) for xx in range(qx + 2, qx + qw - 2)}, OUTLINE)
    return image


# Per fleet tier: (hull left, hull right, hull top row, hull depth), the sail box, the shields along
# the gunwale and the oars below it.
FLEETS = {
    1: dict(hull=(9, 22, 20, 3), sail=(13, 10, 7, 7), mast=15, top=7, shields=0, oars=0),
    2: dict(hull=(5, 26, 20, 4), sail=(10, 7, 12, 10), mast=15, top=4, shields=3, oars=5),
    3: dict(hull=(3, 28, 19, 5), sail=(8, 5, 16, 11), mast=15, top=2, shields=4, oars=7),
}


def draw_fleet(tier):
    t = FLEETS[tier]
    image = [[FIELD] * SIZE for _ in range(SIZE)]

    def paint(cells, colour):
        for (x, y) in cells:
            if 0 <= x < SIZE and 0 <= y < SIZE:
                image[y][x] = colour

    left, right, hy, depth = t["hull"]
    # The hull: a deck line that rises into a curled prow and stern, narrowing to the keel.
    hull = set()
    for d in range(depth):
        hull |= {(x, hy + d) for x in range(left + d + 1, right - d)}
    prow = {(left, hy - 1), (left, hy - 2), (left + 1, hy), (right, hy - 1), (right, hy - 2), (right - 1, hy)}
    if tier == 3:
        prow |= {(left + 1, hy - 3), (right - 1, hy - 3)}
    hull |= prow
    # Oars: white strokes slanting down from under the gunwale, painted before the hull.
    oars = set()
    span = right - left
    for i in range(t["oars"]):
        ox = left + 3 + (i * (span - 6)) // max(1, t["oars"] - 1)
        oars |= {(ox - 1, hy + depth), (ox - 2, hy + depth + 1), (ox - 3, hy + depth + 2)}
    paint(oars, OUTLINE)

    sx, sy, sw, sh = t["sail"]
    sail = box(sx, sy, sw, sh)
    if tier == 1:
        sail = {(x, y) for (x, y) in sail if x - sx <= (y - sy) + 1}  # a small triangular sail
    else:
        sail -= {(sx, sy), (sx + sw - 1, sy)}
        sail |= {(x, sy + sh) for x in range(sx + 1, sx + sw - 1)}  # bellied foot
    mast = {(t["mast"], y) for y in range(t["top"], hy)}
    pennant = {(t["mast"] + 1, t["top"]), (t["mast"] + 2, t["top"]), (t["mast"] + 1, t["top"] + 1)}

    for shape, colour in ((sail, FILL), (hull, HULL)):
        ring = {(x + dx, y + dy) for (x, y) in shape for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - shape
        paint(ring, OUTLINE)
        paint(shape, colour)
    paint(mast | pennant, OUTLINE)
    if tier > 1:
        # A yard across the top of the sail.
        paint({(x, sy - 1) for x in range(sx - 1, sx + sw + 1)}, OUTLINE)
    for i in range(t["shields"]):
        cx = left + 4 + (i * (span - 8)) // max(1, t["shields"] - 1)
        paint({(cx, hy), (cx + 1, hy), (cx, hy + 1), (cx + 1, hy + 1)}, OUTLINE)
    return image


def gable(x, y, w, h):
    """A roof: a triangle on a base w wide at row y + h - 1, rising to a ridge at row y."""
    cells = set()
    for d in range(h):
        half = (w * (d + 1)) // (2 * h)
        mid = x + w // 2
        cells |= {(xx, y + d) for xx in range(mid - half, mid + half + (w % 2))}
    return cells


def battlements(x, y, w):
    """Merlons along the top of a wall: every other pair of cells in the row above y."""
    return {(xx, y - 1) for xx in range(x, x + w) if (xx - x) % 3 != 2}


CITIES = ("tier1", "tier2", "tier3", "tier4", "capital")


def draw_city(kind):
    image = [[FIELD] * SIZE for _ in range(SIZE)]

    def paint(cells, colour):
        for (x, y) in cells:
            if 0 <= x < SIZE and 0 <= y < SIZE:
                image[y][x] = colour

    details = set()
    if kind == "tier1":  # a house: walls, a gable roof, a door and a window
        shapes = [box(11, 16, 10, 9), gable(10, 9, 12, 7)]
        details |= box(15, 20, 2, 5) | box(18, 18, 2, 2)
    elif kind == "tier2":  # a hall: two storeys of windows under a wide roof
        shapes = [box(7, 15, 18, 11), gable(6, 8, 20, 7)]
        for wx in (9, 13, 19, 23):
            details |= box(wx - 0, 17, 2, 2)
        for wx in (9, 23):
            details |= box(wx, 21, 2, 2)
        details |= box(15, 20, 2, 6) | box(14, 21, 4, 5) - {(14, 21), (17, 21)}
    elif kind == "tier3":  # a castle: a curtain wall between two towers, with a gate
        wall, left, right = box(9, 15, 14, 11), box(4, 9, 6, 17), box(22, 9, 6, 17)
        shapes = [wall | battlements(9, 15, 14), left | battlements(4, 9, 6), right | battlements(22, 9, 6)]
        details |= box(14, 19, 4, 7) - {(14, 19), (17, 19)}
        details |= box(6, 13, 2, 3) | box(24, 13, 2, 3)
    elif kind == "tier4":  # a walled town: a keep behind a crenellated wall with corner towers
        keep = box(11, 7, 10, 12) | battlements(11, 7, 10)
        wall = box(4, 18, 24, 9) | battlements(4, 18, 24)
        towers = box(2, 15, 5, 12) | battlements(2, 15, 5), box(25, 15, 5, 12) | battlements(25, 15, 5)
        shapes = [keep, wall, towers[0], towers[1]]
        details |= box(14, 10, 4, 2) | box(14, 21, 4, 6) - {(14, 21), (17, 21)}
        details |= box(8, 21, 2, 2) | box(22, 21, 2, 2)
    else:  # the capital: a temple, its pediment on a row of columns over two steps
        pediment = gable(4, 5, 24, 6)
        beam = box(5, 11, 22, 2)
        columns = [box(x, 14, 2, 10) for x in (6, 11, 15, 20, 24)]
        steps = box(4, 24, 24, 2) | box(2, 26, 28, 2)
        shapes = [steps, beam | pediment] + columns
        details |= {(x, 9) for x in range(14, 18)}
    for shape in shapes:
        ring = {(x + dx, y + dy) for (x, y) in shape for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - shape
        paint(ring, OUTLINE)
        paint(shape, FILL)
    paint(details, OUTLINE)
    return image


def wrap(cells):
    return {(x % SIZE, y % SIZE) for (x, y) in cells}


def disc(cx, cy, r):
    return {(x, y) for y in range(cy - r, cy + r + 1) for x in range(cx - r, cx + r + 1)
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r + r}


def wave_row(y0, amplitude, period, phase=0):
    """A sine stroke across the tile; a period dividing 32 keeps it seamless."""
    import math
    return {(x, y0 + round(amplitude * math.sin(2 * math.pi * (x + phase) / period))) for x in range(SIZE)}


def draw_terrain(name):
    def fill(colour):
        return [[colour] * SIZE for _ in range(SIZE)]

    def paint(image, cells, colour):
        for (x, y) in wrap(cells):
            image[y][x] = colour

    if name == "plain":
        image = fill((92, 164, 58))
        for (x0, y0) in ((2, 4), (18, 9), (8, 16), (24, 21), (3, 27), (14, 29)):
            # A short grass stroke: a little rising-and-falling wave, five cells long.
            paint(image, {(x0, y0 + 1), (x0 + 1, y0), (x0 + 2, y0), (x0 + 3, y0 + 1), (x0 + 4, y0 + 1), (x0 + 5, y0)}, (56, 118, 38))
        paint(image, {(11, 6), (28, 14), (20, 26)}, (150, 205, 96))
    elif name == "forest":
        image = fill((40, 92, 34))
        for (cx, cy, r) in ((7, 7, 5), (23, 5, 5), (15, 18, 6), (29, 21, 5), (5, 26, 5)):
            paint(image, {(cx, cy + r), (cx, cy + r + 1), (cx, cy + r + 2)}, (96, 62, 30))  # trunk
            ring = {(x + dx, y + dy) for (x, y) in disc(cx, cy, r) for dx in (-1, 0, 1) for dy in (-1, 0, 1)}
            paint(image, ring - disc(cx, cy, r), (20, 52, 18))
            paint(image, disc(cx, cy, r), (62, 140, 46))
            paint(image, disc(cx - r // 3, cy - r // 3, max(1, r // 3)), (110, 182, 72))
    elif name == "mountain":
        image = fill((132, 124, 98))
        for (cx, peak, base) in ((10, 6, 27), (24, 11, 30)):
            height = base - peak
            for d in range(height + 1):
                half = (d * 11) // height
                y = peak + d
                paint(image, {(x, y) for x in range(cx - half, cx)}, (176, 166, 140))   # lit face
                paint(image, {(x, y) for x in range(cx, cx + half + 1)}, (88, 80, 66))  # shaded face
                paint(image, {(cx - half - 1, y), (cx + half + 1, y)}, (52, 46, 40))
            paint(image, {(x, peak + d) for d in range(4) for x in range(cx - d * 11 // height, cx + d * 11 // height + 1)}, (245, 245, 250))
            paint(image, {(cx, peak - 1)}, (52, 46, 40))
    elif name == "desert":
        image = fill((226, 198, 112))
        paint(image, wave_row(9, 2, 32), (204, 170, 86))
        paint(image, wave_row(24, 2, 32, 16), (204, 170, 86))
        for (x, y) in ((4, 3), (13, 5), (27, 4), (8, 14), (21, 16), (30, 13), (2, 20), (15, 21), (25, 28), (6, 30), (18, 29)):
            paint(image, {(x, y)}, (140, 104, 52))
        for (x, y) in ((10, 11), (24, 9), (28, 23), (12, 26)):
            paint(image, {(x, y)}, (246, 228, 160))
    elif name == "sea_calm":
        image = fill((36, 88, 172))
        for y0 in (6, 22):
            paint(image, {(x, y) for (x, y) in wave_row(y0, 2, 16) if (x // 8) % 2 == 0}, (110, 160, 228))
        for y0 in (14, 30):
            paint(image, {(x, y) for (x, y) in wave_row(y0, 2, 16, 8) if (x // 8) % 2 == 1}, (110, 160, 228))
    elif name == "sea_rough":
        image = fill((22, 58, 132))
        for y0 in (4, 12, 20, 28):
            paint(image, wave_row(y0, 2, 16, y0), (96, 146, 214))
            paint(image, {(x, y - 1) for (x, y) in wave_row(y0, 2, 16, y0) if (x + y0) % 16 == 4}, (232, 240, 250))
    else:  # river: plain with a stream across it
        image = draw_terrain("plain")
        band = set()
        for (x, y) in wave_row(16, 3, 32):
            band |= {(x, y - 2), (x, y - 1), (x, y), (x, y + 1)}
        paint(image, band, (52, 112, 200))
    return image


TERRAIN = ("plain", "desert", "forest", "mountain", "river", "sea_calm", "sea_rough")


def write_bmp24(path, image):
    """24-bit opaque, bottom-up: the pack's terrain tile format (asset-specification.md §1.2)."""
    rows = b"".join(bytes(v for (r, g, b) in row for v in (b, g, r)) for row in reversed(image))
    header = struct.pack("<2sIHHI", b"BM", 14 + 40 + len(rows), 0, 0, 54)
    info = struct.pack("<IiiHHIIiiII", 40, SIZE, SIZE, 1, 24, 0, len(rows), 2835, 2835, 0, 0)
    with open(path, "wb") as f:
        f.write(header + info + rows)


def write_bmp(path, image):
    """32-bit BGRA, opaque, bottom-up: the pack's sprite format (asset-specification.md §1.2)."""
    rows = b"".join(bytes(v for (r, g, b) in reversed_row for v in (b, g, r, 255))
                    for reversed_row in reversed(image))
    header = struct.pack("<2sIHHI", b"BM", 14 + 40 + len(rows), 0, 0, 54)
    info = struct.pack("<IiiHHIIiiII", 40, SIZE, SIZE, 1, 32, 0, len(rows), 2835, 2835, 0, 0)
    with open(path, "wb") as f:
        f.write(header + info + rows)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "assets", "packs", "authored"))
    parser.add_argument("--preview", help="also write a PNG preview recoloured for a few owners")
    args = parser.parse_args(argv)
    images = {("army", tier): draw(tier) for tier in TIERS}
    images.update({("fleet", tier): draw_fleet(tier) for tier in FLEETS})
    images.update({("city", name): draw_city(name) for name in CITIES})
    for (kind, tier), image in images.items():
        os.makedirs(os.path.join(args.out, kind), exist_ok=True)
        name = tier if isinstance(tier, str) else f"tier{tier}"
        write_bmp(os.path.join(args.out, kind, f"{name}.bmp"), image)
    terrain = {name: draw_terrain(name) for name in TERRAIN}
    os.makedirs(os.path.join(args.out, "terrain"), exist_ok=True)
    for name, image in terrain.items():
        write_bmp24(os.path.join(args.out, "terrain", f"{name}.bmp"), image)
    if args.preview:
        from PIL import Image
        owners = [((0xFF, 0x00, 0x00), (0x00, 0x00, 0x00), (0xFF, 0xFF, 0xFF)),
                  ((0x00, 0x00, 0x80), (0xFF, 0xFF, 0xFF), (0xFF, 0x00, 0xFF)),
                  ((0x80, 0x80, 0x00), (0xFF, 0xFF, 0xFF), (0x80, 0x00, 0x00)),
                  ((0x80, 0x80, 0x80), (0xFF, 0xFF, 0xFF), (0x00, 0x00, 0x00)),
                  ((0x80, 0x00, 0x80), (0xFF, 0xFF, 0x00), (0x00, 0x80, 0x00))]
        scale = 4
        sheet = Image.new("RGB", (len(images) * (SIZE * scale + 8) + 8, len(owners) * (SIZE * scale + 8) + 8), (40, 40, 40))
        for row, (first, second, third) in enumerate(owners):
            recolour = {FIELD: first, OUTLINE: second, FILL: third, HULL: HULL}
            for col, tier in enumerate(images):
                tier_image = images[tier]
                tile = Image.new("RGB", (SIZE, SIZE))
                tile.putdata([recolour[p] for r in tier_image for p in r])
                sheet.paste(tile.resize((SIZE * scale, SIZE * scale), Image.NEAREST),
                            (8 + col * (SIZE * scale + 8), 8 + row * (SIZE * scale + 8)))
        sheet.save(args.preview)
        # The terrain tiles, each as a 3x3 block so the seams show, side by side.
        block = SIZE * 3 * 3
        tiles = Image.new("RGB", (len(terrain) * (block + 8) + 8, block + 16), (40, 40, 40))
        for col, (name, image) in enumerate(terrain.items()):
            tile = Image.new("RGB", (SIZE, SIZE))
            tile.putdata([p for r in image for p in r])
            big = Image.new("RGB", (SIZE * 3, SIZE * 3))
            for i in range(9):
                big.paste(tile, ((i % 3) * SIZE, (i // 3) * SIZE))
            tiles.paste(big.resize((block, block), Image.NEAREST), (8 + col * (block + 8), 8))
        tiles.save(args.preview.replace(".png", "-terrain.png"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
