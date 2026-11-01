#!/usr/bin/env python3
"""T270: reuse the authored Italy campaign unchanged, adding an opt-in barbarian variant.

Run after build_italy.py if regenerating the base campaign. The old world,
scenario and terrain bytes are never modified. No original-game data or art.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VARIANT = "italy-500bc-barbarians"
ROLE = "barbarians"
DESIGN = "designed: owner-approved boundary arrivals, docs/design/barbarians.md; a gameplay faction, not a historical unified people."


def build():
    world = json.loads((ROOT / "data/worlds/italy-500bc.json").read_text(encoding="utf-8"))
    scenario = json.loads((ROOT / "data/scenarios/italy-500bc.json").read_text(encoding="utf-8"))
    rules = json.loads((ROOT / "data/rulesets/italy-500bc.json").read_text(encoding="utf-8"))
    world["id"] = VARIANT
    world["name"] = "Italy, 500 BC — barbarian arrivals"
    # Share our existing authored sidecar, in the same directory; do not copy its bytes or alter it.
    world["nations"].append(dict(
        id=ROLE, name="Barbarians", colorHex="#7A2E1F", leaderName="War leader",
        capitalCityId=None, treasury=0, unity=1000, wealth=0, taxBase=0,
        taxRatePercent=0, mobilizedPercent=0, population=0,
        _provenance={key: DESIGN for key in (
            "name", "colorHex", "leaderName", "capitalCityId", "treasury", "unity", "wealth",
            "taxBase", "taxRatePercent", "mobilizedPercent", "population")},
    ))
    world["turnOrder"].append(ROLE)
    relations = world["startingRelations"]
    relations["nationIds"].append(ROLE)
    war = rules["diplomacy"]["stateCodes"]["war"]
    for row in relations["matrix"]:
        row.append(war)
    relations["matrix"].append([war] * (len(relations["nationIds"]) - 1) + [0])
    world.setdefault("_provenance", {})["barbarianVariant"] = DESIGN + " All regular starting cities, forces and terrain retain the authored base campaign."
    scenario["id"] = VARIANT
    scenario["name"] = world["name"]
    scenario["worldId"] = VARIANT
    scenario["seats"].append(dict(nation=ROLE, control="ai", personality=None))
    scenario["barbarians"] = dict(
        nationId=ROLE,
        arrivalUnits=[
            dict(mercenaryLabel=0, unitTypeId="light_infantry", troops=6000, quality=6, name="Raiders 1"),
            dict(mercenaryLabel=0, unitTypeId="light_infantry", troops=6000, quality=6, name="Raiders 2"),
            dict(mercenaryLabel=0, unitTypeId="archers", troops=3000, quality=6, name="Archers 1"),
            dict(mercenaryLabel=0, unitTypeId="archers", troops=3000, quality=6, name="Archers 2"),
        ],
        fleetShips=40, armyMorale=70, chancePercent=25, armyCap=4, fleetCap=4,
    )
    scenario.setdefault("_provenance", {})["barbarians"] = (
        DESIGN + " Provisional 18,000 troops in four units of two existing types, quality 6, morale 70 and forty transport ships;"
        " the existing siege-strength check requires a force capable of threatening normal Italian towns."
        " 25 percent shared seasonal chance, four armies including cargo and four fleets."
        " No historical source fixes a spawn strength; tune through headless campaign runs."
    )
    scenario["_provenance"]["listed"] = "designed: separately offered variant; old Italy campaign remains available and unchanged for old saves."
    for folder, document in (("worlds", world), ("scenarios", scenario)):
        path = ROOT / "data" / folder / f"{VARIANT}.json"
        path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    build()
