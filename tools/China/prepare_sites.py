#!/usr/bin/env python3
"""Extract the curated, attributed Pleiades facts for the China campaigns; reject missing temporal evidence.

The full public GIS archive stays ignored. The small output retains references, coordinate
precision and broad date-resource bounds, not copied descriptions. Supplemental chronologies
are separately sourced and distinguish contextual ranges.

Source note: the Near East and Mediterranean subsets were extracted from pinned archive
snapshots (snapshot dates and SHA-256 recorded in their sites.json). This subset is extracted
from the same current snapshot the Mediterranean subset records (its snapshot date and SHA-256
are carried below unchanged, because the same archive file is read); the archive carries no
Warring States settlement records, so every centre here is an author geographic hint
(regional=true) whose coordinates come from targets.py and whose dated context comes from
sources/chronology.json - the Madaktu pattern India used for its sparse South Asia coverage.
"""
import csv
import hashlib
import io
import json
from pathlib import Path
import re
import zipfile
from collections import defaultdict
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from targets import TARGETS

HINTS = {k: (lon, lat) for k, lon, lat in TARGETS}

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
ARCHIVE = ROOT / "rendered/near-east-sources/pleiades_gis_data.zip"
YEARS = (350, 230)
WEST, SOUTH, EAST, NORTH = 98.0, 22.0, 124.0, 44.0


def main():
    chosen = list(csv.DictReader((HERE / "cities.csv").open(encoding="utf-8")))
    supplemental_path = HERE / "sources/chronology.json"
    supplemental = json.loads(supplemental_path.read_text(encoding="utf-8")) if supplemental_path.exists() else {}
    with zipfile.ZipFile(ARCHIVE) as archive:
        def rows(name):
            return csv.DictReader(io.TextIOWrapper(archive.open(f"data/gis/{name}.csv"), encoding="utf-8-sig"))
        pleiades_ids = {r["pleiades"] for r in chosen if r["pleiades"]}
        places = {r["id"]: r for r in rows("places") if r["id"] in pleiades_ids}
        evidence = defaultdict(list)
        for table in ("location_points", "location_polygons", "names"):
            for r in rows(table):
                if r["place_id"] not in pleiades_ids:
                    continue
                try:
                    lower, upper = int(r["year_after_which"]), int(r["year_before_which"])
                except ValueError:
                    continue
                evidence[r["place_id"]].append({"kind": table, "uri": r["uri"], "from": lower, "to": upper,
                    "certainty": r.get("association_certainty", ""), "note": "Broad resource bounds, not proof of precise-year ownership or continuous urban occupation."})
        prepared, missing = [], []
        for row in chosen:
            if not re.fullmatch(r"[a-z][a-z0-9_]*", row["id"]):
                raise ValueError(f"Invalid stable site id {row['id']!r}")
            if row["pleiades"]:
                p = places[row["pleiades"]]
                lon, lat = float(p["representative_longitude"]), float(p["representative_latitude"])
                coordinate_source, precision = p["uri"], p["location_precision"]
                source_title, source_provenance = p["title"], p["provenance"]
                if not (WEST <= lon <= EAST and SOUTH <= lat <= NORTH):
                    raise ValueError(f"{row['id']} outside the authored frame: {lon},{lat}")
                own_evidence = evidence[row["pleiades"]]
                pleiades_uri = p["uri"]
            else:
                if row["regional"] != "true":
                    raise ValueError(f"{row['id']} has no Pleiades record and is not marked regional")
                lon, lat = HINTS[row["id"]]
                if not (WEST <= lon <= EAST and SOUTH <= lat <= NORTH):
                    raise ValueError(f"{row['id']} hint outside the authored frame: {lon},{lat}")
                coordinate_source = "author geographic hint"
                precision = "author hint"
                source_title = f"{row['id']} (author hint)"
                source_provenance = "designed: author geographic hint for a centre the archive does not carry; the population and era owners are our design."
                own_evidence = []
                pleiades_uri = ""
            eras = {}
            for year in YEARS:
                owner = row[f"owner{year}"]
                if not owner:
                    continue
                dated = [e for e in own_evidence if e["from"] <= -year <= e["to"]]
                dated += [e for e in supplemental.get(row["id"], []) if e["from"] <= -year <= e["to"]]
                if not dated:
                    missing.append({"id": row["id"], "year": year, "description": row["id"], "details": ""})
                eras[str(year)] = {"name": row[f"caption{year}"], "owner": owner, "evidence": dated[:4]}
            prepared.append({"id": row["id"], "pleiades": pleiades_uri, "sourceTitle": source_title,
                "lon": round(lon, 7), "lat": round(lat, 7), "coordinateSource": coordinate_source, "precision": precision,
                "sourceProvenance": source_provenance, "population": int(row["population"]),
                "regional": row["regional"] == "true", "eras": eras})
        if missing:
            path = ROOT / "rendered/near-east-sources/china-missing-chronology.json"
            path.write_text(json.dumps(missing, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"Missing dated/contextual evidence for {len(missing)} site/era entries; see {path}.")
            return 1
        med = json.loads((ROOT / "tools/Mediterranean/sources/sites.json").read_text(encoding="utf-8"))
        document = {"attribution": "Pleiades contributors, with project editors; copyright the contributors. Adapted location/title/date facts; political grouping and balance are our design.",
            "licence": "CC BY 3.0", "licenceUrl": "https://creativecommons.org/licenses/by/3.0/",
            "credits": "https://pleiades.stoa.org/credits", "source": "https://atlantides.org/downloads/pleiades/gis/",
            "snapshotDate": med["snapshotDate"], "retrieved": med["retrieved"], "archiveSha256": med["archiveSha256"],
            "sites": prepared}
        path = HERE / "sources/sites.json"
        path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"Prepared {len(prepared)} curated site records with coordinate/date references and attribution.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
