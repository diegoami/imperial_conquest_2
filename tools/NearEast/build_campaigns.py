#!/usr/bin/env python3
"""T273: offline, reproducible historical campaign data; existing engine mechanics only.

Reads committed public source subsets. --terrain-only prepares the shared map before
the curated site list is complete. No original-game files, network or random generator.
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
WEST, SOUTH, EAST, NORTH = 20.0, 24.0, 54.5, 42.5
TPD_X, TPD_Y = 8, 10
WIDTH, HEIGHT = round((EAST - WEST) * TPD_X), round((NORTH - SOUTH) * TPD_Y)
spec = importlib.util.spec_from_file_location("italy_authoring", ROOT / "tools/Italy/build_italy.py")
italy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(italy)
GEOGRAPHY = json.loads((HERE / "sources/geography.json").read_text(encoding="utf-8"))
SOURCE = "historical: copyright Pleiades contributors; adapted title/location/date facts, CC BY 3.0 https://creativecommons.org/licenses/by/3.0/; https://pleiades.stoa.org/credits. Dated resources and coordinates in tools/NearEast/sources/sites.json."
DESIGN = "designed: regional/polity compression and gameplay balance; docs/design/near-east-campaigns.md. No historical census or exact-year border claim."

# Regular seats; the final reserved seat is the non-playable barbarian role.
# Capitals are gameplay administrative centres; uncertain tribal centres are labelled regions.
POLITIES = {
    1350: [
        ("egypt", "Egypt", "thebes_egypt", "#B87924", "Pharaoh"),
        ("hatti", "Hatti", "hattusa", "#8F303A", "Great King"),
        ("mitanni", "Mitanni", "fekheriye", "#6A3C88", "King of Mitanni"),
        ("babylonia", "Babylonia", "babylon", "#37648E", "Kassite king"),
        ("assyria", "Assyria", "assur", "#8A4B29", "King of Assur"),
        ("arzawa", "Arzawa", "ephesus", "#6A6530", "Western king"),
        ("alashiya", "Alashiya", "enkomi", "#2E7A76", "King of Alashiya"),
        ("mycenaeans", "Mycenaean Greece", "mycenae", "#395A93", "Wanax"),
        ("elam", "Elam", "susa", "#3A7045", "Elamite king"),
        ("syrian_cities", "Syria", "ugarit", "#984A63", "City rulers"),
        ("canaan", "Canaan", "shechem", "#8A683C", "City rulers"),
        ("highlands", "Highlands", "marlik", "#566A38", "Highland chief"),
    ],
    750: [
        ("egypt", "Egypt", "tanis", "#B87924", "Pharaoh"),
        ("assyria", "Assyria", "kalhu", "#8A4B29", "Assyrian king"),
        ("urartu", "Urartu", "tushpa", "#7B3864", "King of Urartu"),
        ("babylonia", "Babylonia", "babylon", "#37648E", "Babylonian king"),
        ("elam", "Elam", "susa", "#3A7045", "Elamite king"),
        ("phrygia", "Phrygia-Lydia", "gordion", "#6A6530", "Local kings"),
        ("neo_hittite", "Neo-Hittites", "tyana", "#8F303A", "Local kings"),
        ("aram", "Aram", "damascus", "#984A63", "Aramaean king"),
        ("phoenicia", "Phoenicia", "tyre", "#7B446E", "City rulers"),
        ("israel", "Israel", "samaria", "#39718B", "King of Israel"),
        ("judah", "Judah", "jerusalem", "#8A683C", "King of Judah"),
        ("philistia", "Philistia", "ashkelon", "#2E7A76", "City rulers"),
        ("mannea", "Mannea", "qalaichi", "#49724A", "Mannean king"),
        ("medes", "Median lands", "godin", "#666038", "Local chiefs"),
        ("greeks", "Greece", "athens", "#395A93", "City rulers"),
    ],
}
DEFAULT_LABELS = dict(zip(italy.UNIT_NAMES, ("Foot", "Guards", "Bowmen", "Lancers", "Dragoons")))


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


def terrain():
    grid = [[0] * WIDTH for _ in range(HEIGHT)]
    for polygon in GEOGRAPHY["land"]:
        paint_ring(grid, polygon["rings"][0], 2)
        for hole in polygon["rings"][1:]:
            paint_ring(grid, hole, 0)
    ridges = [([(21, 40), (22, 38), (22.7, 36.5)], .3),
              ([(27.5, 37), (31, 37), (35, 37.5), (39, 39)], .55),
              ([(43, 39), (45, 36), (48, 33), (52, 29)], .65),
              ([(48, 37), (51, 36.5), (54.5, 36.5)], .4)]
    for y in range(HEIGHT):
        for x in range(WIDTH):
            if grid[y][x] == 0:
                continue
            lon, lat = centre(x, y)
            mountainous = any(any(italy.dist_to_segment(lon, lat, a, b) <= radius for a, b in zip(line, line[1:])) for line, radius in ridges)
            if mountainous:
                grid[y][x] = 4 if italy.value_noise(x, y, 4, 750) > .62 else 5
            elif lat < 32 or (lon > 37 and lat < 35):
                grid[y][x] = 3
            elif italy.value_noise(x, y, 9, 1350) > .64:
                grid[y][x] = 4
    # Natural inland water uses the existing navigable-water convention, as in Italy.
    for lake in GEOGRAPHY["lakes"]:
        paint_ring(grid, lake["rings"][0], 0)
    # Keep narrow real straits open at the map's coarse scale, without a new movement rule.
    for points in [[(26.0, 39.9), (26.4, 40.1), (26.7, 40.4)], [(28.9, 40.9), (29.05, 41.15), (29.2, 41.35)]]:
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


def nearest_free(grid, start, taken, sea=False, radius=5):
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


def rules_for(year):
    rules = json.loads((ROOT / "data/rulesets/italy-500bc.json").read_text(encoding="utf-8"))
    rules["id"] = f"near-east-{year}bc"
    rules["name"] = "Late Bronze Age" if year == 1350 else "Early Iron Age"
    rules["description"] = f"Historical Near East opening around {year} BC, using existing troop roles and mechanics with period presentation."
    rules["calendar"]["startYearBc"] = year
    rules["victory"]["hardEndYearBc"] = year - 20
    rules["calendar"].setdefault("_provenance", {})["startYearBc"] = f"designed: owner-selected historical opening around {year} BC."
    rules["victory"].setdefault("_provenance", {})["hardEndYearBc"] = "designed: a twenty-year campaign, the Mediterranean campaign's existing horizon."
    rules["mapMarkers"].setdefault("_provenance", {})["cityPopulationTierThresholds"] = "designed: reuse Italy's [6,10,18] smaller-town marker scale for authored city/region populations; not a historical census."
    names = {
        "light_infantry": ("Spearmen", "Sp", "Spearmen", None),
        "heavy_infantry": ("Guard infantry", "Gu", "Guards", None),
        "archers": ("Archers", "Ar", "Bowmen", None),
        "light_cavalry": ("Light chariots", "Ch", "Chariots", "light_chariot") if year == 1350 else ("Mounted scouts", "Ms", "Riders", None),
        "heavy_cavalry": ("Heavy chariots", "HC", "Charioteers", "heavy_chariot"),
    }
    for unit in rules["unitTypes"]:
        name, abbreviation, label, visual = names[unit["id"]]
        unit.update(name=name, abbreviation=abbreviation, battalionLabel=label)
        if visual:
            unit["visualTypeId"] = visual
        unit.setdefault("_provenance", {})["presentation"] = "designed: period label/art proxy for the existing canonical troop archetype; every numeric statistic is unchanged."
    rules.setdefault("_provenance", {})["_group"] = "designed: existing Italy/classical mechanics with the chosen calendar and period presentation; no new combat coefficients."
    return rules


def dump(path, document):
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


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


def world_for(year, grid, sites, positions, rules):
    identity = f"near-east-{year}bc"
    polities = POLITIES[year]
    capitals = {p[2] for p in polities}
    cities = []
    for site in sites:
        era = site["eras"].get(str(year))
        if era is None:
            continue
        x, y = positions[site["id"]]
        pop = site["population"]
        if year == 750 and site["id"] in ("mycenae", "tiryns", "knossos"):
            pop = {"mycenae": 4, "tiryns": 3, "knossos": 8}[site["id"]]
        capital = site["id"] in capitals
        fort = 80 if capital else 50 if pop >= 12 else 30
        dates = "; ".join(e["uri"] for e in era["evidence"])
        cities.append(dict(id=site["id"], name=era["name"], x=x, y=y, owner=era["owner"], allegiance=era["owner"],
            loyalty=80, supplyTons=200 + pop * 8, fortificationCode=fort,
            populationThousands=pop, maxPopulationThousands=round(pop * 1.5), tribute=max(1, round(pop * .7)), garrison=[],
            _provenance={
                "_group": SOURCE + f" {site['pleiades']}; date/context resources: {dates}",
                "name": ("designed: labelled regional centre at a dated archaeological site, owner-approved 2026-11-01; no contemporary urban-capital claim." if site["regional"]
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
        treasury = 2500 + len(own) * 500 + (2000 if nation_id in ("alashiya", "greeks", "mycenaeans", "phoenicia") else 0)
        nations.append(dict(id=nation_id, name=name, colorHex=color, leaderName=leader, capitalCityId=capital_id,
            treasury=treasury, unity=760, wealth=pop * 3000, taxBase=4 * sum(c["tribute"] for c in own),
            taxRatePercent=10, mobilizedPercent=20, population=pop,
            battleColorsHex=[color, "#FFFFFF", "#FFD54F"], glyphColorHex="#FFD54F",
            _provenance={"_group": "historical: period frame in docs/design/near-east-campaigns.md; " + DESIGN,
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
        _provenance={"_group": "designed: off-map arrival role, not a unified historical people; docs/design/barbarians.md."})
    nations.append(role)
    ids = [n["id"] for n in nations]
    war = rules["diplomacy"]["stateCodes"]["war"]
    peace = rules["diplomacy"]["stateCodes"]["peace"]
    matrix = [[war if a != b and "barbarians" in (a, b) else peace for b in ids] for a in ids]
    base = json.loads((ROOT / "data/worlds/italy-500bc.json").read_text(encoding="utf-8"))
    world = dict(schemaVersion=1, id=identity, name=f"Near East, c{year} BC", width=WIDTH, height=HEIGHT,
        terrain=dict(encoding="base64", runs=None, data=None, dataFile="near-east-1350bc.terrain.b64"),
        tileTypes=base["tileTypes"], nations=nations, cities=cities, startingArmies=armies, startingFleets=fleets,
        turnOrder=ids, startingRelations=dict(nationIds=ids, matrix=matrix), weatherCentres=[],
        _provenance={"terrain": "derived: Natural Earth v5.1.2 public-domain coast/rivers/natural lakes, clipped and rasterized by tools/NearEast/build_campaigns.py. "
            "Designed relief/vegetation, one-cell real strait corridors and small-island landing aprons; modern coast approximates antiquity. No modern canals/reservoirs.",
            "nations": DESIGN, "startingRelations": "designed: neutral regular-state opening, not a reconstruction of precise-year treaty obligations; barbarians always hostile.",
            "weatherCentres": "designed: no rough-sea overlay centres; normal seasonal fleet attrition rules remain enabled.",
            "width": "derived: 34.5 longitude degrees times eight cells.", "height": "derived: 18.5 latitude degrees times ten cells."})
    role_units = copy.deepcopy(json.loads((ROOT / "data/scenarios/italy-500bc-barbarians.json").read_text(encoding="utf-8"))["barbarians"])
    for u in role_units["arrivalUnits"]:
        u["name"] = "Frontier infantry" if u["unitTypeId"] == "light_infantry" else "Frontier archers"
    scenario = dict(schemaVersion=1, id=identity, name=world["name"], worldId=identity, rulesetId=identity,
        seats=[dict(nation=id, control="human" if i == 0 else "ai", personality=None if i == 0 or id == "barbarians" else dict(aggression=.5, expansionDrive=.5, loyaltyToAlliances=.5)) for i, id in enumerate(ids)],
        victory=dict(type="totalConquest", goal=None), turnLimit=None, blindHotseat=False, randomSeed=year, listed=True,
        barbarians=role_units,
        _provenance={"_group": "designed: owner-selected historical opening with explicit grouped-polity and regional-centre abstractions; all numerical setup is gameplay balance.",
            "victory": "designed: existing total conquest and twenty-year horizon.", "barbarians": "designed: approved shared boundary-arrival policy and provisional Italy force, with generic frontier labels."})
    return world, scenario


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--terrain-only", action="store_true")
    args = parser.parse_args()
    grid = terrain()
    write_png(HERE / "near-east.png", grid)
    if args.terrain_only:
        print(f"Prepared {WIDTH}x{HEIGHT} shared terrain PNG.")
        return
    sites = json.loads((HERE / "sources/sites.json").read_text(encoding="utf-8"))["sites"]
    positions = site_positions(grid, sites)
    write_png(HERE / "near-east.png", grid)
    codes = [c for row in grid for c in row]
    sidecar = ROOT / "data/worlds/near-east-1350bc.terrain.b64"
    sidecar.write_text(base64.b64encode(struct.pack("<" + "H" * len(codes), *codes)).decode() + "\n", encoding="ascii", newline="\n")
    for year in (1350, 750):
        rules = rules_for(year)
        world, scenario = world_for(year, grid, sites, positions, rules)
        dump(ROOT / f"data/worlds/near-east-{year}bc.json", world)
        dump(ROOT / f"data/rulesets/near-east-{year}bc.json", rules)
        dump(ROOT / f"data/scenarios/near-east-{year}bc.json", scenario)
        print(f"Built {world['name']}: {len(world['nations']) - 1} playable seats, {len(world['cities'])} centres, {len(world['startingArmies'])} armies, {len(world['startingFleets'])} fleets.")


if __name__ == "__main__":
    main()
