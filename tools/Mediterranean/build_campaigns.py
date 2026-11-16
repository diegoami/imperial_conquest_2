#!/usr/bin/env python3
"""T293.3: Mediterranean campaigns, 431, 280 and 218 BC (offline, reproducible).

Reads committed public source subsets; --terrain-only prepares the shared map before the
curated site list is complete. No original-game files, network or random generator.
Brief and owner decisions 2026-11-16: docs/design/mediterranean-campaigns.md.
"""
import argparse
import base64
from collections import deque
import copy
import importlib.util
import json
import math
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
WEST, SOUTH, EAST, NORTH = -10.0, 24.0, 40.0, 46.0
TPD_X, TPD_Y = 8, 10
WIDTH, HEIGHT = round((EAST - WEST) * TPD_X), round((NORTH - SOUTH) * TPD_Y)
spec = importlib.util.spec_from_file_location("italy_authoring", ROOT / "tools/Italy/build_italy.py")
italy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(italy)
GEOGRAPHY = json.loads((HERE / "sources/geography.json").read_text(encoding="utf-8"))
SOURCE = "historical: copyright Pleiades contributors; adapted title/location/date facts, CC BY 3.0 https://creativecommons.org/licenses/by/3.0/; https://pleiades.stoa.org/credits. Dated resources and coordinates in tools/Mediterranean/sources/sites.json."
DESIGN = "designed: regional/polity compression and gameplay balance; docs/design/mediterranean-campaigns.md. No historical census or exact-year border claim."

DEFAULT_HUMAN_SEAT = {431: "athens", 280: "epirus", 218: "carthage"}
# Owner decision 2026-11-16: wars in progress at the openings.
WARS = {431: [("sparta", "athens")],
        280: [("epirus", "rome")],
        218: [("rome", "carthage")]}
MARITIME = {431: ("athens", "corinth", "carthage", "syracuse", "massalia", "taras"),
            280: ("carthage", "syracuse", "egypt", "rhodes", "massalia"),
            218: ("carthage", "rhodes", "massalia", "syracuse")}

# Regular seats; the final reserved seat is the non-playable barbarian role.
POLITIES = {
    431: [
        ("athens", "Athens", "athens", "#41708E", "assembly"),
        ("sparta", "Sparta", "sparta", "#B23A2A", "kings and gerousia"),
        ("corinth", "Corinth", "corinth", "#8F5A2E", "assembly"),
        ("argos", "Argos", "argos", "#984A63", "king and council"),
        ("boeotia", "Boeotia", "thebes_greece", "#6A6530", "Boeotarchs"),
        ("macedon", "Macedon", "aegae", "#395A93", "Perdiccas II"),
        ("thessaly", "Thessaly", "larisa", "#566A38", "Tetrarchs"),
        ("epirus", "Epirus", "ambracia", "#8A4B29", "Molossian king"),
        ("persia", "Persia", "sardis", "#6A3C88", "Great King"),
        ("carthage", "Carthage", "carthage", "#7B446E", "Suffetes"),
        ("syracuse", "Syracuse", "syracuse", "#B87924", "assembly"),
        ("taras", "Taras", "taras", "#2E7A76", "city council"),
        ("rome", "Rome", "rome", "#8F5A2E", "Consuls"),
        ("etruria", "Etruria", "volsinii", "#8A683C", "city rulers"),
        ("massalia", "Massalia", "massalia", "#8A4B29", "city council"),
    ],
    280: [
        ("epirus", "Epirus", "ambracia", "#94353C", "Pyrrhus"),
        ("rome", "Rome", "rome", "#8A4B29", "Consuls"),
        ("carthage", "Carthage", "carthage", "#7B446E", "Suffetes"),
        ("syracuse", "Syracuse", "syracuse", "#B87924", "assembly"),
        ("samnites", "Samnites", "bovianum", "#6A6530", "federation chiefs"),
        ("lucanians", "Lucanians", "heracleia_lucania", "#566A38", "federation chiefs"),
        ("macedon", "Macedon", "aegae", "#395A93", "local king"),
        ("aetolia", "Aetolia", "thermon", "#984A63", "League strategos"),
        ("athens", "Athens", "athens", "#41708E", "assembly"),
        ("pergamon", "Pergamon", "pergamon", "#4E8A6B", "Philetaerus"),
        ("egypt", "Egypt", "alexandria", "#B87924", "Ptolemy II"),
        ("seleucid", "Seleucid kingdom", "antioch", "#6A3C88", "Antiochus I"),
        ("illyria", "Illyria", "scodra", "#3A7045", "local kings"),
        ("rhodes", "Rhodes", "rhodes_city", "#39718B", "Council"),
        ("massalia", "Massalia", "massalia", "#2E7A76", "city council"),
    ],
    218: [
        ("rome", "Rome", "rome", "#8A4B29", "Consuls"),
        ("carthage", "Carthage", "carthage", "#7B446E", "Hannibal"),
        ("syracuse", "Syracuse", "syracuse", "#B87924", "Hiero II"),
        ("macedon", "Macedon", "pella", "#395A93", "Philip V"),
        ("aetolia", "Aetolia", "thermon", "#984A63", "League strategos"),
        ("achaea", "Achaea", "corinth", "#6A6530", "League strategos"),
        ("epirus", "Epirus", "ambracia", "#566A38", "League council"),
        ("egypt", "Egypt", "alexandria", "#B87924", "Ptolemy IV"),
        ("seleucid", "Seleucid kingdom", "antioch", "#6A3C88", "Antiochus III"),
        ("pergamon", "Pergamon", "pergamon", "#4E8A6B", "Attalus I"),
        ("rhodes", "Rhodes", "rhodes_city", "#39718B", "Council"),
        ("illyria", "Illyria", "scodra", "#3A7045", "local kings"),
        ("massalia", "Massalia", "massalia", "#2E7A76", "city council"),
        ("numidia", "Numidia", "cirta", "#8A683C", "the kings"),
        ("athens", "Athens", "athens", "#41708E", "assembly"),
    ],
}

# Authored relief bands (docs/design/terrain-authoring.md): mountain only on the high crest of a
# band, forest as the lower-hill proxy, and deliberate plain passes across each belt. The lines are
# gameplay abstractions at this map scale, named for the ranges/routes they approximate.
RIDGES = [
    ("Alps", [(5.5, 45.8), (7.5, 46.2), (10.0, 46.7), (12.5, 47.0), (14.8, 47.3)], .9),
    ("Apennines", [(9.8, 44.3), (11.0, 43.6), (12.8, 42.9), (14.5, 42.1), (16.0, 41.3), (16.2, 40.0), (15.8, 39.4)], .5),
    ("Sicilian highlands", [(14.3, 37.9), (15.0, 37.6), (15.3, 37.4)], .35),
    ("Pyrenees", [(-1.6, 42.8), (0.0, 42.6), (1.6, 42.5), (2.6, 42.4)], .45),
    ("Cantabrian range", [(-7.0, 43.2), (-5.0, 43.0), (-3.0, 42.8)], .4),
    ("Iberian system", [(-2.5, 41.5), (-1.0, 41.0), (0.5, 40.7)], .35),
    ("Betic ranges", [(-4.5, 37.5), (-3.0, 37.1), (-1.5, 37.0)], .3),
    ("Tell Atlas", [(0.0, 35.8), (2.0, 36.3), (4.0, 36.5), (6.0, 36.5), (8.0, 36.5)], .45),
    ("Tunisian Dorsale", [(9.0, 36.5), (9.3, 36.0), (9.6, 35.4)], .3),
    ("Pindos", [(21.0, 40.0), (22.0, 38.0), (22.7, 36.5)], .4),
    ("Haemus", [(22.9, 42.7), (24.5, 42.6), (26.0, 42.6)], .35),
    ("Rhodope", [(23.5, 41.7), (25.0, 41.5), (26.2, 41.4)], .3),
    ("Dinaric Alps", [(15.2, 45.3), (17.0, 44.5), (18.8, 43.3), (19.5, 42.2)], .4),
    ("Taurus", [(27.5, 37.0), (31.0, 37.0), (35.0, 37.5), (39.0, 39.0)], .55),
    ("Pontic range", [(32.5, 41.5), (36.0, 41.0), (39.5, 40.9)], .4),
    ("Caucasus edge", [(39.5, 43.8), (40.0, 44.8), (40.0, 45.8)], .5),
    ("Levant ranges", [(35.5, 33.8), (35.8, 34.5), (36.0, 35.2)], .25),
    ("Crete", [(23.8, 35.3), (25.2, 35.3), (26.3, 35.2)], .25),
    ("Corsica", [(8.9, 42.2), (9.1, 41.9)], .25),
    ("Sardinia", [(9.0, 40.3), (9.3, 39.8)], .25),
]
CREST_FRACTION = .5
PASS_RADIUS = .1
PASSES = [
    ("Mont Genevre (Alps)", [(6.6, 44.8), (7.0, 45.2), (7.4, 45.6)]),
    ("Brenner (Alps)", [(11.3, 46.6), (11.8, 47.0)]),
    ("Coastal Pyrenees", [(1.4, 42.4), (2.2, 42.3), (2.9, 42.3)]),
    ("Somport (Pyrenees)", [(0.1, 42.7), (0.8, 42.6), (1.4, 42.6)]),
    ("Ligurian coast road", [(8.6, 44.5), (9.2, 44.3), (9.8, 44.2)]),
    ("Cisa (Apennines)", [(9.9, 44.4), (10.4, 44.3), (10.9, 44.2)]),
    ("Adriatic Apennines", [(13.3, 42.9), (13.7, 42.7)]),
    ("Sicilian interior", [(14.2, 37.7), (14.9, 37.5), (15.4, 37.4)]),
    ("Tunisian passes", [(8.8, 36.2), (9.3, 35.7), (9.7, 35.2)]),
    ("Taygetus crossing (Peloponnese)", [(21.7, 37.0), (22.0, 37.0), (22.4, 37.1), (22.6, 37.5), (22.72, 37.63)]),
    ("Pindos corridor", [(21.2, 40.1), (21.6, 39.6), (22.0, 39.0)]),
    ("Succi (Haemus)", [(24.2, 42.5), (24.9, 42.5), (25.5, 42.5)]),
    ("Dinaric coast road", [(15.9, 44.9), (16.8, 44.1), (17.6, 43.3), (18.3, 42.6)]),
    ("Cilician Gates (Taurus)", [(34.85, 36.9), (34.75, 37.5), (34.7, 38.2)]),
    ("Belen (Syrian gates)", [(36.4, 36.5), (36.8, 36.3)]),
    ("Coele corridor", [(35.9, 34.2), (36.2, 34.6)]),
]
# Narrow real straits kept open at the map's coarse scale, without a new movement rule.
STRAITS = [
    [(-6.0, 35.95), (-5.5, 36.0)],                                # Gibraltar
    [(15.6, 38.2), (15.65, 38.05)],                               # Messana
    [(9.2, 41.35), (9.4, 41.3)],                                  # Bonifacio
    [(26.0, 39.9), (26.4, 40.1), (26.7, 40.4)],                   # Hellespont
    [(28.9, 40.9), (29.05, 41.15), (29.2, 41.35)],                # Bosphorus
    [(36.4, 45.2), (36.7, 45.4)],                                 # Cimmerian Bosporus
]


def cell(lon, lat):
    return int((lon - WEST) * TPD_X), int((NORTH - lat) * TPD_Y)


def centre(x, y):
    return WEST + (x + 0.5) / TPD_X, NORTH - (y + 0.5) / TPD_Y


def neighbours(x, y):
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if (dx or dy) and 0 <= x + dx < WIDTH and 0 <= y + dy < HEIGHT:
                yield x + dx, y + dy


def paint_ring(grid, ring, value):
    # Scanline at cell centres. Only clipped public vector coordinates are used.
    for y in range(HEIGHT):
        lat = centre(0, y)[1]
        crossings = []
        for a, b in zip(ring, ring[1:] + ring[:1]):
            if (a[1] > lat) != (b[1] > lat):
                crossings.append(a[0] + (lat - a[1]) * (b[0] - a[0]) / (b[1] - a[1]))
        crossings.sort()
        for left, right in zip(crossings[::2], crossings[1::2]):
            for x in range(max(0, math.ceil((left - WEST) * TPD_X - 0.5)), min(WIDTH, math.ceil((right - WEST) * TPD_X - 0.5))):
                grid[y][x] = value


def in_nile_delta(lon, lat):
    return 29.5 <= lon <= 32.5 and lat >= 29.5


def terrain():
    grid = [[0] * WIDTH for _ in range(HEIGHT)]
    for polygon in GEOGRAPHY["land"]:
        paint_ring(grid, polygon["rings"][0], 2)
        for hole in polygon["rings"][1:]:
            paint_ring(grid, hole, 0)
    for y in range(HEIGHT):
        for x in range(WIDTH):
            if grid[y][x] == 0:
                continue
            lon, lat = centre(x, y)
            # Owner rule (docs/design/terrain-authoring.md): mountain only on the high crest
            # (the inner CREST_FRACTION of a band); the rest of the band is forest, the lower-hill proxy.
            ridge_distance = min((italy.dist_to_segment(lon, lat, a, b) / radius
                                  for _name, line, radius in RIDGES for a, b in zip(line, line[1:])), default=9)
            if ridge_distance <= 1:
                crest = ridge_distance <= CREST_FRACTION and italy.value_noise(x, y, 4, 750) <= .62
                grid[y][x] = 5 if crest else 4
            elif lat < 30.5 or (lon > 25 and lat < 31.5 and not in_nile_delta(lon, lat)) or (lon > 36.5 and lat < 33.5):
                grid[y][x] = 3
            elif italy.value_noise(x, y, 9, 1350) > .64:
                grid[y][x] = 4
    # Plain passes: continuous corridors across each belt. Pass lines and the corridor width
    # are authored gameplay abstractions at this map scale, named for the historical route they
    # approximate; they are not surveyed road geometry.
    for _name, line in PASSES:
        for y in range(HEIGHT):
            for x in range(WIDTH):
                if grid[y][x] in (4, 5):
                    lon, lat = centre(x, y)
                    if any(italy.dist_to_segment(lon, lat, a, b) <= PASS_RADIUS for a, b in zip(line, line[1:])):
                        grid[y][x] = 2
    # Natural inland water uses the existing navigable-water convention, as in Italy.
    for lake in GEOGRAPHY["lakes"]:
        paint_ring(grid, lake["rings"][0], 0)
    for points in STRAITS:
        for a, b in zip(points, points[1:]):
            for x, y in italy.line_cells(cell(*a), cell(*b)):
                if 0 <= x < WIDTH and 0 <= y < HEIGHT:
                    grid[y][x] = 0
    river_cells = set()
    for line in GEOGRAPHY["rivers"]:
        for a, b in zip(line["points"], line["points"][1:]):
            for x, y in italy.line_cells(cell(*a), cell(*b)):
                if 0 <= x < WIDTH and 0 <= y < HEIGHT and grid[y][x] >= 2:
                    river_cells.add((x, y))
    for x, y in river_cells:
        for nx, ny in neighbours(x, y):
            if grid[ny][nx] in (3, 4):
                grid[ny][nx] = 2
    for x, y in sorted(river_cells):
        exits = []
        for dx, dy, direction in ((-1, 0, "W"), (1, 0, "E"), (0, -1, "N"), (0, 1, "S")):
            nx, ny = x + dx, y + dy
            if (nx, ny) in river_cells or not (0 <= nx < WIDTH and 0 <= ny < HEIGHT) or grid[ny][nx] == 0:
                exits.append(direction)
        if len(exits) < 2:
            exits += [italy.OPPOSITE[exits[0]]] if exits else ["N", "S"]
        grid[y][x] = italy.EXIT_CODE[frozenset(exits[:2])]
    return grid


def write_png(path, grid):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    palette = b"".join(struct.pack(">BBB", c >> 16, (c >> 8) & 255, c & 255) for c in italy.PALETTE)
    raw = b"".join(b"\0" + bytes(row) for row in grid)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 3, 0, 0, 0))
                     + chunk(b"PLTE", palette) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def dump(path, document):
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--terrain-only", action="store_true")
    args = parser.parse_args()
    grid = terrain()
    counts = {}
    for row in grid:
        for value in row:
            counts[value] = counts.get(value, 0) + 1
    print(f"Terrain {WIDTH}x{HEIGHT}: " + ", ".join(f"code {k}: {v}" for k, v in sorted(counts.items())))
    write_png(HERE / "mediterranean.png", grid)
    if args.terrain_only:
        print("Prepared the shared terrain PNG.")
        return
    print("Full build: not implemented yet.")


if __name__ == "__main__":
    main()
