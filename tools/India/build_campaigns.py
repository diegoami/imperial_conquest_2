#!/usr/bin/env python3
"""T293.4: India campaigns, c.325 and c.185 BC (offline, reproducible).

Reads the committed public geography subset and the curated site list; builds the shared authored
terrain sidecar, per-opening derived sidecars, and the worlds, rulesets and listed scenarios.
No original-game files, network or random generator.
Brief and owner decisions 2026-11-16 (approved): docs/design/india-campaigns.md.
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
WEST, SOUTH, EAST, NORTH = 60.0, 5.0, 94.0, 37.0
TPD_X, TPD_Y = 8, 10
WIDTH, HEIGHT = round((EAST - WEST) * TPD_X), round((NORTH - SOUTH) * TPD_Y)
spec = importlib.util.spec_from_file_location("italy_authoring", ROOT / "tools/Italy/build_italy.py")
italy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(italy)
GEOGRAPHY = json.loads((HERE / "sources/geography.json").read_text(encoding="utf-8"))
SOURCE = "historical: copyright Pleiades contributors; adapted title/location/date facts, CC BY 3.0 https://creativecommons.org/licenses/by/3.0/; https://pleiades.stoa.org/credits. Dated resources and coordinates in tools/India/sources/sites.json."
DESIGN = "designed: regional/polity compression and gameplay balance; docs/design/india-campaigns.md. No historical census or exact-year border claim."

DEFAULT_HUMAN_SEAT = {325: "magadha", 185: "sunga"}
NAMES = {325: "Before the Mauryas", 185: "The Mauryan Fall"}
# Owner decision 2026-11-16 (approved): wars in progress at the openings.
WARS = {325: [("macedonian_indus", "malloi")], 185: [("indo_greek", "sunga")]}
MARITIME = {325: ("macedonian_indus", "andhra", "kalinga", "saurashtra", "chola", "pandya"),
            185: ("indo_greek", "satavahana", "kalinga", "saurashtra", "chola", "chera", "pandya")}
# Approved period labels: the Indian long bow is the era's famous arm; every numeric statistic
# is the existing Italy/classical archetype, unchanged.
PERIOD_LABELS = {
    325: {"light_infantry": ("Skirmishers", "Sk", "Skirmishers"), "heavy_infantry": ("Spearmen", "Sp", "Spearmen"),
          "archers": ("Longbowmen", "Lb", "Longbowmen"), "light_cavalry": ("Light horse", "LH", "Light horse"),
          "heavy_cavalry": ("Heavy horse", "HH", "Heavy horse")},
    185: {"light_infantry": ("Skirmishers", "Sk", "Skirmishers"), "heavy_infantry": ("Spearmen", "Sp", "Spearmen"),
          "archers": ("Longbowmen", "Lb", "Longbowmen"), "light_cavalry": ("Light horse", "LH", "Light horse"),
          "heavy_cavalry": ("Heavy horse", "HH", "Heavy horse")},
}
DEFAULT_LABELS = {"light_infantry": "Light infantry", "heavy_infantry": "Heavy infantry", "archers": "Archers",
                  "light_cavalry": "Light cavalry", "heavy_cavalry": "Heavy cavalry"}

# Approved seat lists (docs/design/india-campaigns.md); the final reserved seat is the barbarian role.
POLITIES = {
    325: [
        ("magadha", "Magadha", "pataliputra", "#395A93", "Dhana Nanda"),
        ("puru", "Puru", "bucephala", "#B23A2A", "Porus"),
        ("taxila", "Taxila", "taxila", "#2E7A76", "Ambhi"),
        ("macedonian_indus", "Macedonian Indus command", "alexandria_indus", "#6A3C88", "the satraps Peithon and Philip"),
        ("malloi", "Malloi-Madraka confederacy", "mulasthana", "#6A6530", "the confederacy"),
        ("abhisara", "Abhisara", "abhisara", "#984A63", "the hill king"),
        ("malava", "Malava-Avanti", "ujjain", "#8A4B29", "the Malava gana"),
        ("vidarbha", "Vidarbha", "pauni", "#8A683C", "the chiefdom"),
        ("asmaka", "Asmaka", "pratishthana", "#4E8A6B", "the chiefdom"),
        ("kuntala", "Kuntala", "banavasi", "#4A5FA5", "the chiefdom"),
        ("andhra", "Andhra coast", "dharanikota", "#39718B", "the coastal chiefs"),
        ("kalinga", "Kalinga", "sisupalgarh", "#B87924", "the coastal court"),
        ("saurashtra", "Saurashtra-Anarta", "bharukachchha", "#8F5A2E", "the coastal chiefs"),
        ("chola", "Chola", "uraiyur", "#41708E", "the Chola king"),
        ("pandya", "Pandya", "madurai", "#7B446E", "the Pandya king"),
    ],
    185: [
        ("sunga", "Sunga", "pataliputra", "#B23A2A", "Pushyamitra Sunga"),
        ("indo_greek", "Indo-Greek kingdom", "sagala", "#395A93", "the Euthydemid kings"),
        ("satavahana", "Satavahana", "pratishthana", "#4E8A6B", "the early Satavahanas"),
        ("kalinga", "Kalinga", "sisupalgarh", "#B87924", "the Mahameghavahana court"),
        ("vidarbha", "Vidarbha", "pauni", "#8A683C", "the eastern chiefdom"),
        ("malava", "Malava", "ujjain", "#8A4B29", "the Malava line"),
        ("panchala", "Panchala-Mitra", "ahichchhatra", "#984A63", "the Mitra houses"),
        ("audumbara", "Audumbara republic", "audumbara", "#39718B", "the republic"),
        ("kuninda", "Kuninda", "kuninda", "#4A5FA5", "the hill kingdom"),
        ("sibi", "Sibi-Madraka republics", "sibi", "#566A38", "the republics"),
        ("saurashtra", "Saurashtra-Anarta", "bharukachchha", "#8F5A2E", "the coastal chiefs"),
        ("kuntala", "Kuntala", "banavasi", "#2E7A76", "the chiefdom"),
        ("chola", "Chola", "uraiyur", "#41708E", "the Chola king"),
        ("chera", "Chera", "karur", "#6A6530", "the Chera king"),
        ("pandya", "Pandya", "madurai", "#7B446E", "the Pandya king"),
    ],
}

# Authored relief bands (docs/design/terrain-authoring.md): mountain only on the high crest of a
# band, forest as the lower-hill proxy, and deliberate plain passes across each belt. The lines
# are gameplay abstractions at this map scale, named for the ranges they approximate.
RIDGES = [
    ("Himalaya front", [(73.5, 36.5), (76.5, 35.2), (79.5, 33.8), (83.0, 32.0), (86.5, 30.8), (90.0, 29.8), (93.0, 29.0)], 1.3),
    ("Hindu Kush edge", [(71.0, 36.6), (70.0, 35.4), (69.0, 34.6), (68.0, 34.2)], 1.0),
    ("Sulaiman range", [(68.0, 32.5), (68.6, 31.0), (69.4, 29.5), (70.0, 28.0)], 0.7),
    ("Aravalli", [(76.2, 27.8), (75.0, 26.2), (73.6, 24.6)], 0.5),
    ("Vindhya-Satpura", [(73.6, 23.9), (76.0, 23.2), (78.6, 23.8), (81.4, 23.4)], 0.6),
    ("Western Ghats", [(73.4, 20.6), (73.6, 18.2), (74.0, 15.8), (75.0, 13.4), (76.2, 11.2), (77.0, 9.6)], 0.5),
    ("Eastern Ghats", [(79.2, 20.8), (79.6, 18.4), (78.9, 16.0), (78.4, 14.2)], 0.45),
]
CREST_FRACTION = .5
PASS_RADIUS = .1
PASSES = [
    ("Khyber corridor", [(70.9, 34.5), (71.3, 34.1), (71.8, 33.9)]),
    ("Bolan corridor", [(66.6, 30.1), (67.0, 29.5), (67.5, 29.0)]),
    ("Ganges doab road", [(78.0, 28.0), (78.6, 27.4), (79.2, 26.9)]),
    ("Narmada gap", [(74.6, 22.0), (75.2, 21.8), (75.8, 21.9)]),
    ("Naneghat crossing", [(73.5, 18.7), (73.9, 18.3), (74.3, 18.0)]),
    ("Kanara pass", [(74.4, 14.2), (74.8, 13.8), (75.2, 13.4)]),
    ("Palghat gap", [(75.9, 10.9), (76.3, 10.8), (76.7, 10.7)]),
    ("Vindhya east road", [(80.0, 23.6), (80.6, 23.3), (81.2, 23.1)]),
    ("Coastal Coromandel", [(80.2, 15.4), (79.9, 14.6), (79.6, 13.8)]),
]
# The Thar's desert box and the subcontinent's forest pockets use the authored conditions below.
THAR = (69.5, 24.0, 74.6, 29.5)


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


def in_thar(lon, lat):
    return THAR[0] <= lon <= THAR[2] and THAR[1] <= lat <= THAR[3]


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
            elif in_thar(lon, lat) or (lat < 8.5 and 71.0 <= lon <= 77.0):
                grid[y][x] = 3
            elif italy.value_noise(x, y, 9, 1350) > .68:
                grid[y][x] = 4
    # Plain passes: continuous corridors across each belt. Pass lines and the corridor width
    # are authored gameplay abstractions at this map scale, named for the routes they approximate.
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


def write_sidecar(path, grid):
    codes = [c for row in grid for c in row]
    path.write_text(base64.b64encode(struct.pack("<" + "H" * len(codes), *codes)).decode() + "\n",
                    encoding="ascii", newline="\n")


def load_grid(path):
    codes = struct.unpack("<" + "H" * (WIDTH * HEIGHT), base64.b64decode(path.read_text(encoding="ascii")))
    return [list(codes[y * WIDTH:(y + 1) * WIDTH]) for y in range(HEIGHT)]


def nearest_free(grid, start, taken, sea=False, radius=5):
    # The Near East helper is bound to its own map bounds; this frame is 272x320, so the same
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
    rules["id"] = f"india-{year}bc"
    rules["name"] = NAMES[year]
    rules["description"] = f"Historical Indian opening around {year} BC, using existing troop roles and mechanics with period presentation."
    rules["calendar"]["startYearBc"] = year
    rules["victory"]["hardEndYearBc"] = year - 20
    rules["calendar"].setdefault("_provenance", {})["startYearBc"] = f"designed: owner-selected historical opening around {year} BC."
    rules["victory"].setdefault("_provenance", {})["hardEndYearBc"] = "designed: a twenty-year campaign, the India campaign's existing horizon."
    rules["mapMarkers"].setdefault("_provenance", {})["cityPopulationTierThresholds"] = "designed: reuse Italy's [6,10,18] smaller-town marker scale for authored city/region populations; not a historical census."
    for unit in rules["unitTypes"]:
        name, abbreviation, label = PERIOD_LABELS[year][unit["id"]]
        unit.update(name=name, abbreviation=abbreviation, battalionLabel=label)
        unit.setdefault("_provenance", {})["presentation"] = "designed: period label/art proxy for the existing canonical troop archetype; every numeric statistic is unchanged."
    rules.setdefault("_provenance", {})["_group"] = "designed: existing Italy/classical mechanics with the chosen calendar and period presentation; no new combat coefficients."
    return rules


def world_for(year, grid, sites, positions, rules):
    identity = f"india-{year}bc"
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
                "_group": SOURCE + (f" {site['pleiades']}; date/context resources: {dates}" if site["pleiades"] else f"; date/context resources: {dates}"),
                "name": ("designed: labelled regional centre at a dated archaeological site or an author geographic hint; no contemporary urban-capital claim." if site["regional"]
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
            _provenance={"_group": "historical: period frame in docs/design/india-campaigns.md; " + DESIGN,
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
                _provenance={"_group": "designed: sixty-ship fleet at a represented coast/large river, full normal supply; no exact historical fleet count."}))
    role = dict(id="barbarians", name="Barbarians", colorHex="#522727", leaderName="War leader", capitalCityId=None,
        treasury=0, unity=1000, wealth=0, taxBase=0, taxRatePercent=0, mobilizedPercent=0, population=0,
        _provenance={"_group": "designed: off-map arrival role with the era's frontier identities in the guide "
            "(the hill and forest peoples at 325, the northwestern Saka-Parthian pressure at 185, labelled as designed); "
            "not a unified historical people; docs/design/barbarians.md."})
    nations.append(role)
    ids = [n["id"] for n in nations]
    war = rules["diplomacy"]["stateCodes"]["war"]
    peace = rules["diplomacy"]["stateCodes"]["peace"]
    at_war = {frozenset(pair) for pair in WARS[year]}
    matrix = [[war if a != b and (frozenset((a, b)) in at_war or "barbarians" in (a, b)) else peace for b in ids] for a in ids]
    base = json.loads((ROOT / "data/worlds/italy-500bc.json").read_text(encoding="utf-8"))
    world = dict(schemaVersion=1, id=f"{identity}", name=f"India, {year} BC", width=WIDTH, height=HEIGHT,
        terrain=dict(encoding="base64", runs=None, data=None, dataFile=f"india-{year}bc.terrain.b64"),
        tileTypes=base["tileTypes"], nations=nations, cities=cities, startingArmies=armies, startingFleets=fleets,
        turnOrder=ids, startingRelations=dict(nationIds=ids, matrix=matrix), weatherCentres=[],
        _provenance={"terrain": "derived: the authored shared India terrain (tools/India/build_campaigns.py: "
            "Natural Earth v5.1.2 public-domain coast/rivers/lakes with authored relief bands, plain passes and the "
            "Thar desert per docs/design/terrain-authoring.md), plus city tiles and landing aprons. Modern coast approximates antiquity.",
            "nations": DESIGN, "startingRelations": f"designed: the owner-approved wars in progress {WARS[year]}, all other regular-state pairs neutral; not a reconstruction of precise-year treaty obligations; barbarians always hostile.",
            "weatherCentres": "designed: no rough-sea overlay centres; normal seasonal fleet attrition rules remain enabled.",
            "width": "derived: 34 longitude degrees times eight cells.", "height": "derived: 32 latitude degrees times ten cells."})
    role_units = copy.deepcopy(json.loads((ROOT / "data/scenarios/italy-500bc-barbarians.json").read_text(encoding="utf-8"))["barbarians"])
    for u in role_units["arrivalUnits"]:
        u["name"] = "Frontier infantry" if u["unitTypeId"] == "light_infantry" else "Frontier archers"
    scenario = dict(schemaVersion=1, id=identity, name=world["name"], worldId=f"{identity}", rulesetId=identity,
        seats=[dict(nation=id, control="human" if id == DEFAULT_HUMAN_SEAT[year] else "ai", personality=None if id == DEFAULT_HUMAN_SEAT[year] or id == "barbarians" else dict(aggression=.5, expansionDrive=.5, loyaltyToAlliances=.5)) for id in ids],
        victory=dict(type="totalConquest", goal=None), turnLimit=None, blindHotseat=False, randomSeed=year, listed=True,
        barbarians=role_units,
        _provenance={"_group": "designed: owner-approved opening with explicit grouped-polity and regional-centre abstractions "
            "(docs/design/india-campaigns.md, decisions 2026-11-16); all numerical setup is gameplay balance.",
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
    write_png(HERE / "india.png", grid)
    write_sidecar(ROOT / "data/worlds/india.terrain.b64", grid)
    if args.terrain_only:
        print("Prepared the shared authored terrain sidecar.")
        return
    sites = json.loads((HERE / "sources/sites.json").read_text(encoding="utf-8"))["sites"]
    shared = load_grid(ROOT / "data/worlds/india.terrain.b64")
    for year in (325, 185):
        year_grid = load_grid(ROOT / "data/worlds/india.terrain.b64")
        year_sites = [s for s in sites if str(year) in s["eras"]]
        positions = site_positions(year_grid, year_sites)
        # Each opening derives its own sidecar from the shared authored map, so the city sets can
        # differ per year without touching the shared map (the Persia/Mediterranean pattern).
        changed = sum(a != b for ra, rb in zip(year_grid, shared) for a, b in zip(ra, rb))
        write_sidecar(ROOT / f"data/worlds/india-{year}bc.terrain.b64", year_grid)
        print(f"Derived sidecar {year} BC: {changed} cells differ from the shared authored terrain.")
        rules = rules_for(year)
        world, scenario = world_for(year, year_grid, year_sites, positions, rules)
        dump(ROOT / f"data/worlds/india-{year}bc.json", world)
        dump(ROOT / f"data/rulesets/india-{year}bc.json", rules)
        dump(ROOT / f"data/scenarios/india-{year}bc.json", scenario)
        print(f"Built {world['name']}: {len(world['nations']) - 1} playable seats, {len(world['cities'])} centres, "
              f"{len(world['startingArmies'])} armies, {len(world['startingFleets'])} fleets.")


if __name__ == "__main__":
    main()
