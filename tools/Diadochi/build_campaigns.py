#!/usr/bin/env python3
"""T293.1: Diadochi campaigns, 323 BC and 301 BC (offline, reproducible).

Reads tools/Diadochi/sources/sites.json and the shared Near East revision-2 terrain
(data/worlds/near-east-r2.terrain.b64, never rewritten here). Existing engine mechanics only.
"""
import base64
import copy
import importlib.util
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("near_east_authoring", ROOT / "tools/NearEast/build_campaigns.py")
ne = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ne)
WIDTH, HEIGHT = ne.WIDTH, ne.HEIGHT
cell, neighbours, nearest_free, dump = ne.cell, ne.neighbours, ne.nearest_free, ne.dump
italy, SOURCE, DESIGN, DEFAULT_LABELS = ne.italy, ne.SOURCE.replace("tools/NearEast/sources", "tools/Diadochi/sources"), ne.DESIGN, ne.DEFAULT_LABELS
SIDECAR = ROOT / "data/worlds/near-east-r2.terrain.b64"
NAMES = {323: "Succession after Alexander", 301: "After Ipsus"}
MARITIME = ("rhodes", "athens", "syria", "demetrius", "islands")

# Regular seats (the brief's two lists); the final reserved seat is the barbarian role.
POLITIES = {
    323: [
        ("egypt", "Egypt", "alexandria", "#B87924", "Ptolemy"),
        ("macedon", "Macedon", "pella", "#395A93", "Antipater"),
        ("royal", "Royal army", "babylon", "#6A3C88", "Perdiccas"),
        ("thrace", "Thrace", "lysimacheia", "#8A4B29", "Lysimachus"),
        ("phrygia", "Phrygia", "celaenae", "#8F303A", "Antigonus"),
        ("cappadocia", "Cappadocia", "mazaca", "#6A6530", "Eumenes"),
        ("wanatolia", "Western Anatolia", "sardis", "#2E7A76", "Satraps"),
        ("syria", "Syria and Phoenicia", "damascus", "#7B446E", "Macedonian governors"),
        ("media", "Media", "ecbatana", "#3A7045", "Peithon"),
        ("persis", "Persis", "persepolis", "#37648E", "Satraps"),
        ("athens", "Athens and allies", "athens", "#4A5FA5", "Assembly"),
        ("aetolia", "Aetolia", "thermon", "#984A63", "League strategos"),
        ("epirus", "Epirus", "ambracia", "#566A38", "Aeacid king"),
        ("rhodes", "Rhodes and the islands", "rhodes_city", "#39718B", "Council"),
        ("armenia", "Armenia", "armavir", "#8A683C", "Orontid rulers"),
    ],
    301: [
        ("seleucid", "Seleucid kingdom", "seleucia_tigris", "#6A3C88", "Seleucus"),
        ("lysimachus", "Thrace and Anatolia", "lysimacheia", "#8A4B29", "Lysimachus"),
        ("egypt", "Egypt", "alexandria", "#B87924", "Ptolemy"),
        ("macedon", "Macedon", "pella", "#395A93", "Cassander"),
        ("demetrius", "Demetrius", "tyre", "#7B446E", "Demetrius"),
        ("cilicia", "Cilicia", "tarsus", "#8F303A", "Pleistarchus"),
        ("epirus", "Epirus", "ambracia", "#566A38", "Aeacid king"),
        ("aetolia", "Aetolia", "thermon", "#984A63", "League strategos"),
        ("athens", "Athens", "athens", "#4A5FA5", "Assembly"),
        ("rhodes", "Rhodes", "rhodes_city", "#39718B", "Council"),
        ("islands", "Island League", "delos", "#2E7A76", "League council"),
        ("cappadocia", "Cappadocia", "mazaca", "#6A6530", "Local king"),
        ("bithynia", "Bithynia and Pontus", "chalcedon", "#3A7045", "Local kings"),
        ("armenia", "Armenia", "armavir", "#8A683C", "Orontid rulers"),
        ("atropatene", "Atropatene", "gazaca", "#37648E", "Atropates"),
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
    rules["id"] = f"diadochi-{year}bc"
    rules["name"] = NAMES[year]
    rules["description"] = f"Historical Diadochi opening in {year} BC, using existing troop roles and mechanics with period presentation."
    rules["calendar"]["startYearBc"] = year
    rules["victory"]["hardEndYearBc"] = year - 20
    rules["calendar"].setdefault("_provenance", {})["startYearBc"] = f"designed: owner-selected historical opening around {year} BC."
    rules["victory"].setdefault("_provenance", {})["hardEndYearBc"] = "designed: a twenty-year campaign, the Mediterranean campaign's existing horizon."
    rules["mapMarkers"].setdefault("_provenance", {})["cityPopulationTierThresholds"] = "designed: reuse Italy's [6,10,18] smaller-town marker scale for authored city/region populations; not a historical census."
    names = {
        "light_infantry": ("Peltasts", "Pe", "Peltasts", None),
        "heavy_infantry": ("Phalangites", "Ph", "Phalanx", None),
        "archers": ("Archers", "Ar", "Bowmen", None),
        "light_cavalry": ("Light horse", "LH", "Light horse", None),
        "heavy_cavalry": ("Companion cavalry", "CC", "Companions", None),
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
    identity = f"diadochi-{year}bc"
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
        treasury = 2500 + len(own) * 500 + (2000 if nation_id in MARITIME else 0)
        nations.append(dict(id=nation_id, name=name, colorHex=color, leaderName=leader, capitalCityId=capital_id,
            treasury=treasury, unity=760, wealth=pop * 3000, taxBase=4 * sum(c["tribute"] for c in own),
            taxRatePercent=10, mobilizedPercent=20, population=pop,
            battleColorsHex=[color, "#FFFFFF", "#FFD54F"], glyphColorHex="#FFD54F",
            _provenance={"_group": "historical: period frame in docs/design/diadochi-campaigns.md; " + DESIGN,
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
    world = dict(schemaVersion=1, id=f"{identity}", name=f"Diadochi, {year} BC", width=WIDTH, height=HEIGHT,
        terrain=dict(encoding="base64", runs=None, data=None, dataFile=f"diadochi.terrain.b64"),
        tileTypes=base["tileTypes"], nations=nations, cities=cities, startingArmies=armies, startingFleets=fleets,
        turnOrder=ids, startingRelations=dict(nationIds=ids, matrix=matrix), weatherCentres=[],
        _provenance={"terrain": "derived: the shared Near East revision-2 terrain, unchanged (Natural Earth v5.1.2 public-domain coast/rivers/lakes; tools/NearEast/build_campaigns.py). "
            "Modern coast approximates antiquity.",
            "nations": DESIGN, "startingRelations": "designed: neutral regular-state opening, not a reconstruction of precise-year treaty obligations; barbarians always hostile.",
            "weatherCentres": "designed: no rough-sea overlay centres; normal seasonal fleet attrition rules remain enabled.",
            "width": "derived: 34.5 longitude degrees times eight cells.", "height": "derived: 18.5 latitude degrees times ten cells."})
    role_units = copy.deepcopy(json.loads((ROOT / "data/scenarios/italy-500bc-barbarians.json").read_text(encoding="utf-8"))["barbarians"])
    for u in role_units["arrivalUnits"]:
        u["name"] = "Frontier infantry" if u["unitTypeId"] == "light_infantry" else "Frontier archers"
    scenario = dict(schemaVersion=1, id=identity, name=world["name"], worldId=f"{identity}", rulesetId=identity,
        seats=[dict(nation=id, control="human" if i == 0 else "ai", personality=None if i == 0 or id == "barbarians" else dict(aggression=.5, expansionDrive=.5, loyaltyToAlliances=.5)) for i, id in enumerate(ids)],
        victory=dict(type="totalConquest", goal=None), turnLimit=None, blindHotseat=False, randomSeed=year, listed=False,
        barbarians=role_units,
        _provenance={"_group": "designed: owner-selected historical opening with explicit grouped-polity and regional-centre abstractions; all numerical setup is gameplay balance.",
            "victory": "designed: existing total conquest and twenty-year horizon.", "barbarians": "designed: approved shared boundary-arrival policy and provisional Italy force, with generic frontier labels."})
    return world, scenario


def main():
    grid = load_grid()
    before = [row[:] for row in grid]
    sites = json.loads((HERE / "sources/sites.json").read_text(encoding="utf-8"))["sites"]
    positions = site_positions(grid, sites)
    changed = sum(a != b for ra, rb in zip(grid, before) for a, b in zip(ra, rb))
    # City tiles and landing aprons at the new coastal centres change a few cells of the shared map,
    # so the Diadochi worlds carry their own derived sidecar and the shared r2 file stays untouched.
    codes = [c for row in grid for c in row]
    (ROOT / "data/worlds/diadochi.terrain.b64").write_text(
        base64.b64encode(struct.pack("<" + "H" * len(codes), *codes)).decode() + "\n", encoding="ascii", newline="\n")
    print(f"Derived sidecar: {changed} cells differ from the shared revision-2 terrain.")
    for year in (323, 301):
        rules = rules_for(year)
        world, scenario = world_for(year, grid, sites, positions, rules)
        dump(ROOT / f"data/worlds/diadochi-{year}bc.json", world)
        dump(ROOT / f"data/rulesets/diadochi-{year}bc.json", rules)
        dump(ROOT / f"data/scenarios/diadochi-{year}bc.json", scenario)
        print(f"Built {world['name']}: {len(world['nations']) - 1} playable seats, {len(world['cities'])} centres, {len(world['startingArmies'])} armies, {len(world['startingFleets'])} fleets.")


if __name__ == "__main__":
    main()
