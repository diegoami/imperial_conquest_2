#!/usr/bin/env python3
"""Prepare the small public-domain geographic subset for the China campaign builder.

Input: ignored rendered/near-east-sources/ne_50m_*.geojson, Natural Earth v5.1.2 at the same
pinned commit the Near East subset records; the downloaded files are verified against those
recorded hashes. No original-game data or art. Coordinates are clipped/rounded, not screenshots.

The frame is 98E-124E, 22N-44N (the Warring States world approved for T293.5). At the 50m scale
the Wei, Fen and Huai lines are absent — the Huang He line and the Huai basin's lakes (Hongze,
Gaoyou, Weishan) carry those basins; the provenance in the builder labels that approximation.
Tibet's highlands, the deep steppe and the far southwest beyond the Nan Ling stay inside the
rectangular frame as empty land: no seat, city or claim is authored there.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INPUT = ROOT / "rendered/near-east-sources"
OUTPUT = Path(__file__).resolve().parent / "sources"
BOUNDS = (98.0, 22.0, 124.0, 44.0)
COMMIT = "f1890d9f152c896d250a77557a5751a93d494776"
# The represented world's rivers: the Huang He and Yangtze systems (with the Chang Jiang/Jinsha
# reach names Natural Earth uses), the Han, Gan, Yuan and Min of the south, Dadu into Ba-Shu, and
# the Liao/Xiliao/Xar Moron lines of Yan's northeast frontier. The far southwest (Lancang, Nu,
# Salween, Red River) and the Xi Jiang system belong to Lingnan and Yunnan — outside the frame.
RIVERS = {"Huang", "Chang Jiang", "Yangtze", "Jinsha", "Dadu", "Gan", "Han", "Yuan", "Min",
          "Liao", "Xiliao", "Xar Moron"}
# Named natural lakes inside the represented world; Qinghai Hu and the small southern lakes sit
# beyond it. Unnamed features (?) are never restored.
LAKES = {"Tai Hu", "Poyang Hu", "Hongze Hu", "Gaoyou Hu", "Chao Hu", "Shijiu Hu", "Weishan Hu",
         "Hong Hu", "Liangzi Hu"}


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
