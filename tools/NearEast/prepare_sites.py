#!/usr/bin/env python3
"""Extract the curated, attributed Pleiades facts; reject missing temporal evidence.

The full public GIS archive stays ignored. The small output retains references,
coordinate precision and broad date-resource bounds, not copied descriptions.
Supplemental chronologies are separately sourced and distinguish contextual ranges.
"""
import csv
import hashlib
import io
import json
from pathlib import Path
import re
import zipfile
from collections import defaultdict

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
ARCHIVE = ROOT / "rendered/near-east-sources/pleiades_gis_data.zip"


def main():
    chosen = list(csv.DictReader((HERE / "cities.csv").open(encoding="utf-8")))
    ids = {r["pleiades"] for r in chosen}
    supplemental_path = HERE / "sources/chronology.json"
    supplemental = json.loads(supplemental_path.read_text(encoding="utf-8")) if supplemental_path.exists() else {}
    with zipfile.ZipFile(ARCHIVE) as archive:
        def rows(name):
            return csv.DictReader(io.TextIOWrapper(archive.open(f"data/gis/{name}.csv"), encoding="utf-8-sig"))
        places = {r["id"]: r for r in rows("places") if r["id"] in ids}
        evidence = defaultdict(list)
        points = defaultdict(list)
        for table in ("location_points", "location_polygons", "names"):
            for r in rows(table):
                if r["place_id"] not in ids:
                    continue
                if table == "location_points" and r["location_precision"] == "precise" and r["association_certainty"] == "certain":
                    match = re.fullmatch(r"POINT\s*\(\s*([-\d.eE]+)\s+([-\d.eE]+)\s*\)", r["geometry_wkt"])
                    if match:
                        points[r["place_id"]].append((float(match[1]), float(match[2]), r["uri"], r["accuracy_radius"]))
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
            p = places[row["pleiades"]]
            lon, lat = float(p["representative_longitude"]), float(p["representative_latitude"])
            coordinate_source, precision = p["uri"], p["location_precision"]
            if precision == "rough" and points[row["pleiades"]]:
                lon, lat, coordinate_source, _ = min(points[row["pleiades"]], key=lambda a: (a[0] - lon) ** 2 + (a[1] - lat) ** 2)
                precision = "precise location resource"
            if not (20 <= lon <= 54.5 and 24 <= lat <= 42.5):
                raise ValueError(f"{row['id']} outside the authored frame: {lon},{lat}")
            eras = {}
            for year in (1350, 750):
                owner = row[f"owner{year}"]
                if not owner:
                    continue
                dated = [e for e in evidence[row["pleiades"]] if e["from"] <= -year <= e["to"]]
                dated += [e for e in supplemental.get(row["id"], []) if e["from"] <= -year <= e["to"]]
                if not dated:
                    missing.append({"id": row["id"], "year": year, "uri": p["uri"], "description": p["description"], "details": p["details"][:1000]})
                eras[str(year)] = {"name": row[f"caption{year}"], "owner": owner, "evidence": dated[:4]}
            prepared.append({"id": row["id"], "pleiades": p["uri"], "sourceTitle": p["title"],
                "lon": round(lon, 7), "lat": round(lat, 7), "coordinateSource": coordinate_source, "precision": precision,
                "sourceProvenance": p["provenance"], "population": int(row["population"]),
                "regional": row["regional"] == "true", "eras": eras})
        if missing:
            path = ROOT / "rendered/near-east-sources/missing-chronology.json"
            path.write_text(json.dumps(missing, ensure_ascii=False, indent=2), encoding="utf-8")
            print(f"Missing dated/contextual evidence for {len(missing)} site/era entries; see {path}.")
            return 1
        document = {"attribution": "Pleiades contributors, with project editors; copyright the contributors. Adapted location/title/date facts; political grouping and balance are our design.",
            "licence": "CC BY 3.0", "licenceUrl": "https://creativecommons.org/licenses/by/3.0/",
            "credits": "https://pleiades.stoa.org/credits", "source": "https://atlantides.org/downloads/pleiades/gis/",
            "snapshotDate": "2026-10-30", "retrieved": "2026-11-01", "archiveSha256": hashlib.sha256(ARCHIVE.read_bytes()).hexdigest(),
            "sites": prepared}
        path = HERE / "sources/sites.json"
        path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"Prepared {len(prepared)} curated site records with coordinate/date references and attribution.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
