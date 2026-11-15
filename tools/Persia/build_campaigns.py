#!/usr/bin/env python3
"""T293.2: Persia campaigns, c.550 BC and 480 BC (offline, reproducible).

Reads tools/Persia/sources/sites.json and the shared Near East revision-2 terrain
(data/worlds/near-east-r2.terrain.b64, never rewritten here). Existing engine mechanics only.
Brief and owner decisions 2026-11-15: docs/design/persia-campaigns.md.
"""
import base64
import copy
import importlib.util
import json
from pathlib import Path
import struct
import sys

# Owner decision 2026-11-15: Persis (Cyrus) is the default human seat at 550 BC, Athens at 480 BC.
DEFAULT_HUMAN_SEAT = {550: "persis", 480: "athens"}
# Owner decision 2026-11-15: wars in progress at the openings.
WARS = {550: [("persis", "media")], 480: [("persia", "sparta"), ("persia", "athens"), ("persia", "corinth")]}

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("near_east_authoring", ROOT / "tools/NearEast/build_campaigns.py")
ne = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ne)
WIDTH, HEIGHT = ne.WIDTH, ne.HEIGHT
cell, neighbours, nearest_free, dump = ne.cell, ne.neighbours, ne.nearest_free, ne.dump
italy, SOURCE, DESIGN, DEFAULT_LABELS = ne.italy, ne.SOURCE.replace("tools/NearEast/sources", "tools/Persia/sources"), ne.DESIGN, ne.DEFAULT_LABELS
SIDECAR = ROOT / "data/worlds/near-east-r2.terrain.b64"
NAMES = {550: "Rise of Cyrus", 480: "Xerxes in Greece"}
MARITIME = {550: ("ionia", "phoenicia", "cyprus"),
            480: ("athens", "corinth", "ionia", "caria", "phoenicia_cyprus")}

# Regular seats (the brief's two approved lists); the final reserved seat is the barbarian role.
POLITIES = {
    550: [
        ("persis", "Persis", "pasargadae", "#94353C", "Cyrus"),
        ("media", "Media", "ecbatana", "#3A7045", "Astyages"),
        ("lydia", "Lydia", "sardis", "#2E7A76", "Croesus"),
        ("babylon", "Babylon", "babylon", "#6A3C88", "Nabonidus"),
        ("egypt", "Egypt", "sais", "#B87924", "Amasis"),
        ("susiana", "Susiana", "susa", "#8A683C", "local princes"),
        ("cilicia", "Cilicia", "tarsus", "#7B446E", "Syennesis"),
        ("cappadocia", "Cappadocia", "mazaca", "#6A6530", "local rulers"),
        ("ionia", "Ionian cities", "miletus", "#4A5FA5", "city councils"),
        ("phoenicia", "Phoenicia", "tyre", "#8A4B29", "city kings"),
        ("judah", "the Babylonian west", "jerusalem", "#395A93", "governor"),
        ("cyprus", "Cyprus", "salamis_cy", "#39718B", "city kings"),
        ("armenia", "Armenia", "tushpa", "#984A63", "highland rulers"),
        ("sparta", "Sparta", "sparta", "#B23A2A", "kings and gerousia"),
        ("athens", "Athens", "athens", "#41708E", "Peisistratus"),
    ],
    480: [
        ("persia", "Persia", "susa", "#6A3C88", "Xerxes"),
        ("sparta", "Sparta", "sparta", "#B23A2A", "kings and gerousia"),
        ("athens", "Athens", "athens", "#41708E", "assembly"),
        ("corinth", "Corinth", "corinth", "#8F5A2E", "assembly"),
        ("argos", "Argos", "argos", "#984A63", "king and council"),
        ("boeotia", "Boeotia", "thebes_greece", "#6A6530", "Boeotarchs"),
        ("thessaly", "Thessaly", "larisa", "#566A38", "Aleuads"),
        ("macedon", "Macedon", "aegae", "#395A93", "Alexander I"),
        ("thrace", "Thrace", "eion", "#8A4B29", "satrap"),
        ("ionia", "Ionia", "miletus", "#2E7A76", "tyrants"),
        ("caria", "Caria", "halicarnassus", "#39718B", "Artemisia"),
        ("lycia", "Lycia", "xanthos", "#4E8A6B", "dynasts"),
        ("cilicia", "Cilicia", "tarsus", "#A0622E", "Syennesis"),
        ("phoenicia_cyprus", "Phoenicia and Cyprus", "tyre", "#7B446E", "city kings"),
        ("egypt", "Egypt", "memphis", "#B87924", "satrap"),
    ],
}


def load_grid():
    codes = struct.unpack("<" + "H" * (WIDTH * HEIGHT), base64.b64decode(SIDECAR.read_text(encoding="ascii")))
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
    rules["id"] = f"persia-{year}bc"
    rules["name"] = NAMES[year]
    rules["description"] = f"Historical Achaemenid-era opening in {year} BC, using existing troop roles and mechanics with period presentation."
    rules["calendar"]["startYearBc"] = year
    rules["victory"]["hardEndYearBc"] = year - 20
    rules["calendar"].setdefault("_provenance", {})["startYearBc"] = f"designed: owner-selected historical opening around {year} BC."
    rules["victory"].setdefault("_provenance", {})["hardEndYearBc"] = "designed: a twenty-year campaign, the Mediterranean campaign's existing horizon."
    rules["mapMarkers"].setdefault("_provenance", {})["cityPopulationTierThresholds"] = "designed: reuse Italy's [6,10,18] smaller-town marker scale for authored city/region populations; not a historical census."
    names = {
        "light_infantry": ("Peltasts", "Pe", "Peltasts", None),
        "heavy_infantry": ("Spear infantry", "Sp", "Spearmen", None),
        "archers": ("Archers", "Ar", "Bowmen", None),
        "light_cavalry": ("Light horse", "LH", "Light horse", None),
        "heavy_cavalry": ("Heavy horse", "HH", "Heavy horse", None),
    }
    for unit in rules["unitTypes"]:
        name, abbreviation, label, visual = names[unit["id"]]
        unit.update(name=name, abbreviation=abbreviation, battalionLabel=label)
        if visual:
            unit["visualTypeId"] = visual
        unit.setdefault("_provenance", {})["presentation"] = "designed: period label/art proxy for the existing canonical troop archetype; every numeric statistic is unchanged."
    rules.setdefault("_provenance", {})["_group"] = "designed: existing Italy/classical mechanics with the chosen calendar and period presentation; no new combat coefficients."
    return rules


def world_for(year, grid, sites, positions, rules):
    identity = f"persia-{year}bc"
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
            _provenance={"_group": "historical: period frame in docs/design/persia-campaigns.md; " + DESIGN,
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
    at_war = {frozenset(pair) for pair in WARS[year]}
    matrix = [[war if a != b and (frozenset((a, b)) in at_war or "barbarians" in (a, b)) else peace for b in ids] for a in ids]
    base = json.loads((ROOT / "data/worlds/italy-500bc.json").read_text(encoding="utf-8"))
    world = dict(schemaVersion=1, id=f"{identity}", name=f"Persia, {year} BC", width=WIDTH, height=HEIGHT,
        terrain=dict(encoding="base64", runs=None, data=None, dataFile=f"persia.terrain.b64"),
        tileTypes=base["tileTypes"], nations=nations, cities=cities, startingArmies=armies, startingFleets=fleets,
        turnOrder=ids, startingRelations=dict(nationIds=ids, matrix=matrix), weatherCentres=[],
        _provenance={"terrain": "derived: the shared Near East revision-2 terrain, unchanged (Natural Earth v5.1.2 public-domain coast/rivers/lakes; tools/NearEast/build_campaigns.py). "
            "Modern coast approximates antiquity.",
            "nations": DESIGN, "startingRelations": f"designed: the owner-approved wars in progress {WARS[year]}, all other regular-state pairs neutral; not a reconstruction of precise-year treaty obligations; barbarians always hostile.",
            "weatherCentres": "designed: no rough-sea overlay centres; normal seasonal fleet attrition rules remain enabled.",
            "width": "derived: 34.5 longitude degrees times eight cells.", "height": "derived: 18.5 latitude degrees times ten cells."})
    role_units = copy.deepcopy(json.loads((ROOT / "data/scenarios/italy-500bc-barbarians.json").read_text(encoding="utf-8"))["barbarians"])
    for u in role_units["arrivalUnits"]:
        u["name"] = "Frontier infantry" if u["unitTypeId"] == "light_infantry" else "Frontier archers"
    scenario = dict(schemaVersion=1, id=identity, name=world["name"], worldId=f"{identity}", rulesetId=identity,
        seats=[dict(nation=id, control="human" if id == DEFAULT_HUMAN_SEAT[year] else "ai", personality=None if id == DEFAULT_HUMAN_SEAT[year] or id == "barbarians" else dict(aggression=.5, expansionDrive=.5, loyaltyToAlliances=.5)) for id in ids],
        victory=dict(type="totalConquest", goal=None), turnLimit=None, blindHotseat=False, randomSeed=year, listed=True,
        barbarians=role_units,
        _provenance={"_group": "designed: owner-approved opening with explicit grouped-polity and regional-centre abstractions (docs/design/persia-campaigns.md, decisions 2026-11-15); all numerical setup is gameplay balance.",
            "victory": "designed: existing total conquest and twenty-year horizon.", "barbarians": "designed: approved shared boundary-arrival policy and provisional Italy force, with generic frontier labels."})
    return world, scenario


def main():
    grid = load_grid()
    before = [row[:] for row in grid]
    sites = json.loads((HERE / "sources/sites.json").read_text(encoding="utf-8"))["sites"]
    positions = site_positions(grid, sites)
    changed = sum(a != b for ra, rb in zip(grid, before) for a, b in zip(ra, rb))
    # City tiles and landing aprons at the new coastal centres change a few cells of the shared map,
    # so the Persia worlds carry their own derived sidecar and the shared r2 file stays untouched.
    codes = [c for row in grid for c in row]
    (ROOT / "data/worlds/persia.terrain.b64").write_text(
        base64.b64encode(struct.pack("<" + "H" * len(codes), *codes)).decode() + "\n", encoding="ascii", newline="\n")
    print(f"Derived sidecar: {changed} cells differ from the shared revision-2 terrain.")
    for year in (550, 480):
        rules = rules_for(year)
        world, scenario = world_for(year, grid, sites, positions, rules)
        dump(ROOT / f"data/worlds/persia-{year}bc.json", world)
        dump(ROOT / f"data/rulesets/persia-{year}bc.json", rules)
        dump(ROOT / f"data/scenarios/persia-{year}bc.json", scenario)
        print(f"Built {world['name']}: {len(world['nations']) - 1} playable seats, {len(world['cities'])} centres, {len(world['startingArmies'])} armies, {len(world['startingFleets'])} fleets.")


if __name__ == "__main__":
    main()
