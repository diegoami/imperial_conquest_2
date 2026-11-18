"""Resolve the approximate China centre hints (targets.py) to Pleiades places.

Run from the repository root: python -X utf8 tools/China/resolve_sites.py
Needs the ignored rendered/near-east-sources/pleiades_gis_data.zip (see tools/NearEast/README.md).
A place must match a name keyword AND lie within 0.6 degrees of the hint. The output is a
starting point to verify by hand, not a final selection. The archive's East Asia coverage is
sparse: most China hints are expected to stay author geographic hints.
"""
from pathlib import Path
import csv, io, zipfile, json, math, sys, unicodedata
sys.path.insert(0, str(Path(__file__).resolve().parent))
from targets import TARGETS

z = zipfile.ZipFile('rendered/near-east-sources/pleiades_gis_data.zip')


def rows(n):
    return csv.DictReader(io.TextIOWrapper(z.open('data/gis/' + n + '.csv'), encoding='utf-8-sig'))


def norm(t):
    return ''.join(c for c in unicodedata.normalize('NFKD', t.lower()) if c.isalnum() and not unicodedata.combining(c))


places = {r['id']: r for r in rows('places')}
allnames = {}
for r in rows('names'):
    allnames.setdefault(r['place_id'], set()).update(
        norm(r[k]) for k in ('title', 'romanized_form_1', 'romanized_form_2', 'romanized_form_3') if r[k])
types = {}
for r in rows('places_place_types'):
    types.setdefault(r['place_id'], set()).add(r['place_type'])

out = []
for sid, lon, lat in TARGETS:
    key = norm(sid.replace('_court', '').replace('_', ' '))
    hits = []
    for pid, names in allnames.items():
        p = places[pid]
        try:
            plon, plat = float(p['representative_longitude']), float(p['representative_latitude'])
        except (ValueError, KeyError):
            continue
        if abs(plon - lon) > 0.6 or abs(plat - lat) > 0.6:
            continue
        if any(key in n or n in key for n in names if len(n) >= 4):
            hits.append((pid, p['title'], round(plon, 2), round(plat, 2)))
    out.append({'id': sid, 'hint': (lon, lat), 'matches': hits[:5]})

print(json.dumps(out, indent=1))
