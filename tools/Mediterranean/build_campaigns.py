#!/usr/bin/env python3
"""T293.3: Mediterranean campaigns, 431, 280 and 218 BC (offline, reproducible).

Reads the committed public geography subset and the curated site list; builds the shared authored
terrain sidecar, three derived per-opening sidecars, and the worlds, rulesets and listed scenarios.
No original-game files, network or random generator.
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
spec = importlib.util.spec_from_file_location("near_east_authoring", ROOT / "tools/NearEast/build_campaigns.py")
near_east = importlib.util.module_from_spec(spec)
spec.loader.exec_module(near_east)
DEFAULT_LABELS = near_east.DEFAULT_LABELS
GEOGRAPHY = json.loads((HERE / "sources/geography.json").read_text(encoding="utf-8"))
SOURCE = "historical: copyright Pleiades contributors; adapted title/location/date facts, CC BY 3.0 https://creativecommons.org/licenses/by/3.0/; https://pleiades.stoa.org/credits. Dated resources and coordinates in tools/Mediterranean/sources/sites.json."
DESIGN = "designed: regional/polity compression and gameplay balance; docs/design/mediterranean-campaigns.md. No historical census or exact-year border claim."

DEFAULT_HUMAN_SEAT = {431: "athens", 280: "epirus", 218: "carthage"}
NAMES = {431: "The Peloponnesian War", 280: "Pyrrhus in Italy", 218: "Hannibal in Italy"}
# Owner decision 2026-11-16: wars in progress at the openings.
WARS = {431: [("sparta", "athens")],
        280: [("epirus", "rome")],
        218: [("rome", "carthage")]}
MARITIME = {431: ("athens", "corinth", "carthage", "syracuse", "massalia", "taras"),
            280: ("carthage", "syracuse", "egypt", "rhodes", "massalia"),
            218: ("carthage", "rhodes", "massalia", "syracuse")}
# 431 names the era's identities (brief section "Map and engine"); 280/218 keep the neutral
# mixed-era labels the Persia campaigns shipped for the same five roles.
PERIOD_LABELS = {
    431: {"light_infantry": ("Peltasts", "Pe", "Peltasts"), "heavy_infantry": ("Hoplites", "Ho", "Hoplites"),
          "archers": ("Archers", "Ar", "Archers"), "light_cavalry": ("Light horse", "LH", "Light horse"),
          "heavy_cavalry": ("Heavy horse", "HH", "Heavy horse")},
    280: {"light_infantry": ("Peltasts", "Pe", "Peltasts"), "heavy_infantry": ("Spear infantry", "Sp", "Spearmen"),
          "archers": ("Archers", "Ar", "Bowmen"), "light_cavalry": ("Light horse", "LH", "Light horse"),
          "heavy_cavalry": ("Heavy horse", "HH", "Heavy horse")},
    218: {"light_infantry": ("Peltasts", "Pe", "Peltasts"), "heavy_infantry": ("Spear infantry", "Sp", "Spearmen"),
          "archers": ("Archers", "Ar", "Bowmen"), "light_cavalry": ("Light horse", "LH", "Light horse"),
          "heavy_cavalry": ("Heavy horse", "HH", "Heavy horse")},
}

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
        ("rome", "Rome", "rome", "#A24E32", "Consuls"),
        ("etruria", "Etruria", "volsinii", "#8A683C", "city rulers"),
        ("massalia", "Massalia", "massalia", "#4E8A6B", "city council"),
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
        ("egypt", "Egypt", "alexandria", "#A0622E", "Ptolemy II"),
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
        ("egypt", "Egypt", "alexandria", "#A0622E", "Ptolemy IV"),
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
    ("Coastal Pyrenees", [(1.4, 42.4), (2.2, 42.3), (2.9, 42.3)]),
    ("Somport (Pyrenees)", [(0.1, 42.7), (0.8, 42.6), (1.4, 42.6)]),
    ("Ligurian coast road", [(8.6, 44.5), (9.2, 44.3), (9.8, 44.2)]),
    ("Cisa (Apennines)", [(9.9, 44.4), (10.4, 44.3), (10.9, 44.2)]),
    ("Adriatic Apennines", [(13.3, 42.9), (13.7, 42.7)]),
    ("Sicilian interior", [(14.2, 37.7), (14.9, 37.5), (15.1, 37.5)]),
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

SHARED_SIDECAR = ROOT / "data/worlds/mediterranean.terrain.b64"


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


def nearest_free(grid, start, taken, sea=False, radius=5):
    # The Near East helper is bound to its own map bounds; this frame is 400x220, so the same
    # breadth-first free-cell search runs against WIDTH/HEIGHT here.
    queue = deque([(start, 0)])
    seen = {start}
    while queue:
        (x, y), distance = queue.popleft()
        if 0 <= x < WIDTH and 0 <= y < HEIGHT and (x, y) not in taken and ((grid[y][x] in (0, 1)) == sea):
            return x, y
        if distance >= radius:
            continue
        for point in neighbours(x, y):
            if point not in seen:
                seen.add(point)
                queue.append((point, distance + 1))
    raise ValueError(f"No {'sea' if sea else 'land'} placement near {start}")


def write_sidecar(path, grid):
    codes = [c for row in grid for c in row]
    path.write_text(base64.b64encode(struct.pack("<" + "H" * len(codes), *codes)).decode() + "\n",
                    encoding="ascii", newline="\n")


def load_grid(path=SHARED_SIDECAR):
    codes = struct.unpack("<" + "H" * (WIDTH * HEIGHT), base64.b64decode(path.read_text(encoding="ascii")))
    return [list(codes[y * WIDTH:(y + 1) * WIDTH]) for y in range(HEIGHT)]


def site_positions(grid, sites):
    positions, taken = {}, set()
    for site in sites:
        point = cell(site["lon"], site["lat"])
        if point in taken:
            point = nearest_free(grid, point, taken, radius=2)
        x, y = point
        if grid[y][x] < 2:
            grid[y][x] = 2  # Source points can lie on sub-cell coasts or tiny islands.
        positions[site["id"]] = point
        taken.add(point)
    for site in sites:
        x, y = positions[site["id"]]
        free_land = [(nx, ny) for nx, ny in neighbours(x, y) if grid[ny][nx] >= 2 and (nx, ny) not in taken]
        needed = max(0, 3 - len(free_land))
        candidates = [(nx, ny) for nx, ny in neighbours(x, y) if (nx, ny) not in taken and grid[ny][nx] < 2]
        if len(candidates) < needed:
            raise ValueError(f"No army/landing apron at {site['id']}")
        for nx, ny in candidates[:needed]:
            grid[ny][nx] = 2
    return positions


def rules_for(year):
    rules = json.loads((ROOT / "data/rulesets/italy-500bc.json").read_text(encoding="utf-8"))
    rules["id"] = f"mediterranean-{year}bc"
    rules["name"] = NAMES[year]
    rules["description"] = f"Historical Mediterranean opening in {year} BC, using existing troop roles and mechanics with period presentation."
    rules["calendar"]["startYearBc"] = year
    rules["victory"]["hardEndYearBc"] = year - 20
    rules["calendar"].setdefault("_provenance", {})["startYearBc"] = f"designed: owner-selected historical opening around {year} BC."
    rules["victory"].setdefault("_provenance", {})["hardEndYearBc"] = "designed: a twenty-year campaign, the Mediterranean campaign's existing horizon."
    rules["mapMarkers"].setdefault("_provenance", {})["cityPopulationTierThresholds"] = "designed: reuse Italy's [6,10,18] smaller-town marker scale for authored city/region populations; not a historical census."
    for unit in rules["unitTypes"]:
        name, abbreviation, label = PERIOD_LABELS[year][unit["id"]]
        unit.update(name=name, abbreviation=abbreviation, battalionLabel=label)
        unit.setdefault("_provenance", {})["presentation"] = "designed: period label/art proxy for the existing canonical troop archetype; every numeric statistic is unchanged."
    rules.setdefault("_provenance", {})["_group"] = "designed: existing Italy/classical mechanics with the chosen calendar and period presentation; no new combat coefficients."
    return rules


def world_for(year, grid, sites, positions, rules):
    identity = f"mediterranean-{year}bc"
    polities = POLITIES[year]
    capitals = {p[2] for p in polities}
    cities = []
    for site in sites:
        era = site["eras"].get(str(year))
        if era is None:
            continue
        x, y = positions[site["id"]]
        pop = site["population"]
        capital = site["id"] in capitals
        fort = 80 if capital else 50 if pop >= 12 else 30
        dates = "; ".join(e["uri"] for e in era["evidence"])
        cities.append(dict(id=site["id"], name=era["name"], x=x, y=y, owner=era["owner"], allegiance=era["owner"],
            loyalty=80, supplyTons=200 + pop * 8, fortificationCode=fort,
            populationThousands=pop, maxPopulationThousands=round(pop * 1.5), tribute=max(1, round(pop * .7)), garrison=[],
            _provenance={
                "_group": SOURCE + f" {site['pleiades']}; date/context resources: {dates}",
                "name": ("designed: labelled regional centre at a dated archaeological site or an author geographic hint, owner-approved 2026-11-01; no contemporary urban-capital claim." if site["regional"]
                         else f"historical: conventional site name adapted from {site['pleiades']}; broad dating context, not exact-year political control."),
                "x": "derived: equirectangular tile projection of the cited coordinate; collision/shore quantization may shift at most two cells.",
                "y": "derived: equirectangular tile projection; shared terrain includes minimum landing/army aprons at tiny inhabited islands.",
                "owner": DESIGN, "allegiance": DESIGN,
                "populationThousands": DESIGN + " Relative city/region size, not a census.",
                "loyalty": "designed: 80, the authored Italy baseline.",
                "fortificationCode": "designed: 80 for administrative capitals, 50 for larger towns, 30 otherwise.",
                "supplyTons": "designed: 200 plus eight per thousand city/region population, supporting existing supply play.",
                "maxPopulationThousands": "derived: starting size times 1.5.", "tribute": "derived: 0.7 times city/region size, rounded, minimum one.",
            }))
    by_owner = {p[0]: [c for c in cities if c["owner"] == p[0]] for p in polities}
    known = set(by_owner)
    if any(c["owner"] not in known for c in cities):
        raise ValueError("Unknown city polity")
    nations, armies, fleets = [], [], []
    taken = {(c["x"], c["y"]) for c in cities}
    for nation_id, name, capital_id, color, leader in polities:
        own = by_owner[nation_id]
        if not own:
            raise ValueError(f"No centres for {nation_id}/{year}")
        capital = next(c for c in own if c["id"] == capital_id)
        pop = sum(c["populationThousands"] for c in own)
        treasury = 2500 + len(own) * 500 + (2000 if nation_id in MARITIME[year] else 0)
        nations.append(dict(id=nation_id, name=name, colorHex=color, leaderName=leader, capitalCityId=capital_id,
            treasury=treasury, unity=760, wealth=pop * 3000, taxBase=4 * sum(c["tribute"] for c in own),
            taxRatePercent=10, mobilizedPercent=20, population=pop,
            battleColorsHex=[color, "#FFFFFF", "#FFD54F"], glyphColorHex="#FFD54F",
            _provenance={"_group": "historical: period frame in docs/design/mediterranean-campaigns.md; " + DESIGN,
                "capitalCityId": "designed: gameplay administrative centre of the grouped seat; tribal/uncertain centres are not asserted historical capitals.",
                "leaderName": "designed: institutional title where a dated personal ruler is not established; no invented historical person.",
                "colorHex": "designed: distinct neighbour palette.", "treasury": "designed: 2500 plus 500 per centre; maritime trade seats add 2000.",
                "unity": "designed: 760.", "wealth": "derived: city/region population sum times 3000, authored Italy convention.",
                "taxBase": "derived: four times city tribute sum.", "population": "derived: sum of represented city/region sizes.",
                "taxRatePercent": "designed: 10%.", "mobilizedPercent": "designed: 20%."}))
        # Existing archetype mix, scaled for small regional seats; no new troop statistics.
        scale = min(1.0, max(.3, pop / 80))
        counts = [12000, 6000, 3500, 3500, 1250]
        units = []
        for kind, count in zip(rules["unitTypes"], counts):
            label = kind.get("battalionLabel", DEFAULT_LABELS[kind["id"]])
            units.append(dict(mercenaryLabel=0, unitTypeId=kind["id"], troops=max(100, int(count * scale)), quality=6,
                              name=f"1st {label} Battalion"))
        ax, ay = nearest_free(grid, (capital["x"], capital["y"]), taken, radius=3)
        taken.add((ax, ay))
        troops = sum(u["troops"] for u in units)
        armies.append(dict(id=f"{nation_id}-army-1", nation=nation_id, x=ax, y=ay, morale=70, money=100,
            supplyTons=troops // rules["economy"]["armySupplyTonsPerTroops"], moves=8, units=units,
            _provenance={"_group": "designed: one field army near the administrative capital; existing five-role mix scaled by represented population, quality 6, full normal supply."}))
        ports = [(c, (nx, ny)) for c in own for nx, ny in neighbours(c["x"], c["y"])
                 if grid[ny][nx] in (0, 1) and (nx, ny) not in taken
                 and any(grid[ly][lx] >= 2 and ((lx, ly) not in taken or (lx, ly) == (ax, ay))
                         for lx, ly in neighbours(nx, ny))]
        if ports:
            port, (fx, fy) = ports[0]
            taken.add((fx, fy))
            ships = 60
            fleets.append(dict(id=f"{nation_id}-fleet-1", nation=nation_id, x=fx, y=fy, ships=ships,
                conditionPercent=100, money=200, supplyTons=ships * rules["economy"]["fleetSupplyTonsPerShip"], moves=25,
                _provenance={"_group": "designed: sixty-ship fleet at a represented coast/large lake, full normal supply; no exact historical fleet count."}))
    role = dict(id="barbarians", name="Barbarians", colorHex="#522727", leaderName="War leader", capitalCityId=None,
        treasury=0, unity=1000, wealth=0, taxBase=0, taxRatePercent=0, mobilizedPercent=0, population=0,
        _provenance={"_group": "designed: off-map arrival role with the era's frontier identities in the guide "
            "(Thracian and Illyrian raiders at 431, the Gauls at 280 and 218, Numidian and Iberian riders at 218); "
            "not a unified historical people; docs/design/barbarians.md."})
    nations.append(role)
    ids = [n["id"] for n in nations]
    war = rules["diplomacy"]["stateCodes"]["war"]
    peace = rules["diplomacy"]["stateCodes"]["peace"]
    at_war = {frozenset(pair) for pair in WARS[year]}
    matrix = [[war if a != b and (frozenset((a, b)) in at_war or "barbarians" in (a, b)) else peace for b in ids] for a in ids]
    base = json.loads((ROOT / "data/worlds/italy-500bc.json").read_text(encoding="utf-8"))
    world = dict(schemaVersion=1, id=f"{identity}", name=f"Mediterranean, {year} BC", width=WIDTH, height=HEIGHT,
        terrain=dict(encoding="base64", runs=None, data=None, dataFile=f"mediterranean-{year}bc.terrain.b64"),
        tileTypes=base["tileTypes"], nations=nations, cities=cities, startingArmies=armies, startingFleets=fleets,
        turnOrder=ids, startingRelations=dict(nationIds=ids, matrix=matrix), weatherCentres=[],
        _provenance={"terrain": "derived: the authored shared Mediterranean terrain (tools/Mediterranean/build_campaigns.py: "
            "Natural Earth v5.1.2 public-domain coast/rivers/lakes with authored relief bands, plain passes and open straits "
            "per docs/design/terrain-authoring.md), plus city tiles and landing aprons. Modern coast approximates antiquity.",
            "nations": DESIGN, "startingRelations": f"designed: the owner-approved wars in progress {WARS[year]}, all other regular-state pairs neutral; not a reconstruction of precise-year treaty obligations; barbarians always hostile.",
            "weatherCentres": "designed: no rough-sea overlay centres; normal seasonal fleet attrition rules remain enabled.",
            "width": "derived: 50 longitude degrees times eight cells.", "height": "derived: 22 latitude degrees times ten cells."})
    role_units = copy.deepcopy(json.loads((ROOT / "data/scenarios/italy-500bc-barbarians.json").read_text(encoding="utf-8"))["barbarians"])
    for u in role_units["arrivalUnits"]:
        u["name"] = "Frontier infantry" if u["unitTypeId"] == "light_infantry" else "Frontier archers"
    scenario = dict(schemaVersion=1, id=identity, name=world["name"], worldId=f"{identity}", rulesetId=identity,
        seats=[dict(nation=id, control="human" if id == DEFAULT_HUMAN_SEAT[year] else "ai", personality=None if id == DEFAULT_HUMAN_SEAT[year] or id == "barbarians" else dict(aggression=.5, expansionDrive=.5, loyaltyToAlliances=.5)) for id in ids],
        victory=dict(type="totalConquest", goal=None), turnLimit=None, blindHotseat=False, randomSeed=year, listed=True,
        barbarians=role_units,
        _provenance={"_group": "designed: owner-approved opening with explicit grouped-polity and regional-centre abstractions "
            "(docs/design/mediterranean-campaigns.md, decisions 2026-11-16); all numerical setup is gameplay balance.",
            "victory": "designed: existing total conquest and twenty-year horizon.", "barbarians": "designed: approved shared boundary-arrival policy and provisional Italy force, with generic frontier labels."})
    return world, scenario


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
    write_sidecar(SHARED_SIDECAR, grid)
    if args.terrain_only:
        print("Prepared the shared authored terrain sidecar.")
        return
    sites = json.loads((HERE / "sources/sites.json").read_text(encoding="utf-8"))["sites"]
    shared = load_grid()
    for year in (431, 280, 218):
        year_grid = load_grid()
        year_sites = [s for s in sites if str(year) in s["eras"]]
        positions = site_positions(year_grid, year_sites)
        # Each opening derives its own sidecar from the shared authored map, so the city sets can
        # differ per year without touching the shared map (the Persia pattern, bounded diff).
        changed = sum(a != b for ra, rb in zip(year_grid, shared) for a, b in zip(ra, rb))
        write_sidecar(ROOT / f"data/worlds/mediterranean-{year}bc.terrain.b64", year_grid)
        print(f"Derived sidecar {year} BC: {changed} cells differ from the shared authored terrain.")
        rules = rules_for(year)
        world, scenario = world_for(year, year_grid, year_sites, positions, rules)
        dump(ROOT / f"data/worlds/mediterranean-{year}bc.json", world)
        dump(ROOT / f"data/rulesets/mediterranean-{year}bc.json", rules)
        dump(ROOT / f"data/scenarios/mediterranean-{year}bc.json", scenario)
        print(f"Built {world['name']}: {len(world['nations']) - 1} playable seats, {len(world['cities'])} centres, "
              f"{len(world['startingArmies'])} armies, {len(world['startingFleets'])} fleets.")


if __name__ == "__main__":
    main()
