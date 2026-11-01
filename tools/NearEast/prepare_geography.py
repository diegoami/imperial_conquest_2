#!/usr/bin/env python3
"""Prepare the small public-domain geographic subset used by the offline campaign builder.

Input: ignored rendered/near-east-sources/ne_50m_*.geojson, Natural Earth v5.1.2.
No original-game data or art. Coordinates are clipped/rounded, not map screenshots.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INPUT = ROOT / "rendered/near-east-sources"
OUTPUT = Path(__file__).resolve().parent / "sources"
BOUNDS = (20.0, 24.0, 54.5, 42.5)
COMMIT = "f1890d9f152c896d250a77557a5751a93d494776"
RIVERS = {"Nile", "Damietta Branch", "Rosetta Branch", "Jordan", "Tigris", "Dicle", "Euphrates", "Firat", "Al Furat", "Shatt al Arab"}
LAKES = {"Lake Van", "Lake Urmia", "Lake Sevan", "Dead Sea", "Sea of Galilee"}


def area(ring):
    return abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(ring, ring[1:] + ring[:1]))) / 2


def clip(ring):
    points = [tuple(p[:2]) for p in ring]
    for axis, bound, keep_above in ((0, BOUNDS[0], True), (0, BOUNDS[2], False), (1, BOUNDS[1], True), (1, BOUNDS[3], False)):
        output = []
        if not points:
            return []
        for a, b in zip(points, points[1:] + points[:1]):
            inside_a = a[axis] >= bound if keep_above else a[axis] <= bound
            inside_b = b[axis] >= bound if keep_above else b[axis] <= bound
            if inside_a != inside_b:
                t = (bound - a[axis]) / (b[axis] - a[axis])
                output.append((a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1])))
            if inside_b:
                output.append(b)
        points = output
    return [[round(x, 6), round(y, 6)] for x, y in points] if len(points) >= 3 else []


def polygons(geometry):
    return [geometry["coordinates"]] if geometry["type"] == "Polygon" else geometry["coordinates"]


def main():
    result = {"bounds": BOUNDS, "land": [], "lakes": [], "rivers": [], "sources": []}
    for layer in ("land", "lakes", "rivers_lake_centerlines"):
        name = f"ne_50m_{layer}.geojson"
        data = (INPUT / name).read_bytes()
        result["sources"].append({"name": name, "commit": COMMIT,
            "url": f"https://raw.githubusercontent.com/nvkelso/natural-earth-vector/{COMMIT}/geojson/{name}",
            "sha256": hashlib.sha256(data).hexdigest(),
            "gitBlob": hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest(),
            "licence": "Public domain", "licenceUrl": "https://www.naturalearthdata.com/about/terms-of-use/"})
        for feature in json.loads(data)["features"]:
            label = feature["properties"].get("name")
            geometry = feature["geometry"]
            if layer == "rivers_lake_centerlines":
                if label not in RIVERS:
                    continue
                lines = [geometry["coordinates"]] if geometry["type"] == "LineString" else geometry["coordinates"]
                for line in lines:
                    if any(BOUNDS[0] - 1 <= p[0] <= BOUNDS[2] + 1 and BOUNDS[1] - 1 <= p[1] <= BOUNDS[3] + 1 for p in line):
                        result["rivers"].append({"name": label, "points": [[round(p[0], 6), round(p[1], 6)] for p in line]})
            else:
                if layer == "lakes" and label not in LAKES:
                    continue
                for polygon in polygons(geometry):
                    outer = clip(polygon[0])
                    if not outer or area(outer) < 0.00001:
                        continue
                    # Keep marine-scale holes. Small inland holes are explicitly restored only from
                    # the named natural lakes above, avoiding modern reservoir outlines in antiquity.
                    holes = [c for ring in polygon[1:] if area(ring) >= 5 for c in [clip(ring)] if c]
                    item = {"rings": [outer] + holes}
                    if layer == "lakes":
                        item["name"] = label
                    result[layer].append(item)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    path = OUTPUT / "geography.json"
    path.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8", newline="\n")
    print(f"Prepared {len(result['land'])} land polygons, {len(result['lakes'])} natural lakes and {len(result['rivers'])} river lines; {path.stat().st_size} bytes.")


if __name__ == "__main__":
    main()
