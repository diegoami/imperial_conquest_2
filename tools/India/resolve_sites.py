"""Resolve the approximate India centre hints (targets.py) to Pleiades places.

Run from the repository root: python -X utf8 tools/India/resolve_sites.py
Needs the ignored rendered/near-east-sources/pleiades_gis_data.zip (see tools/NearEast/README.md).
A place must match a name keyword AND lie within 0.6 degrees of the hint. The output is a
starting point to verify by hand, not a final selection.
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

KW = {
    'taxila': ['taxila', 'taksasila'], 'pushkalavati': ['pushkalavati', 'peukela', 'charsadda'],
    'bazira': ['bazira', 'barikot'], 'bucephala': ['boukephala', 'bucephala'],
    'nicaea_india': ['nikaia', 'nicaea'], 'sagala': ['sagala', 'euthymedia', 'sialkot'],
    'mulasthana': ['mulasthana', 'mallaj', 'multan'], 'harappa': ['harappa'],
    'alexandria_indus': ['alexandria', 'patala'], 'roruka': ['roruka', 'rori', 'aror'],
    'pataliputra': ['pataliputra', 'palibothra', 'palimbothra'], 'rajagriha': ['rajagriha', 'rajgir', 'ragaya'],
    'varanasi': ['varanasi', 'benares', 'sarnath', 'isipatana'], 'kaushambi': ['kausambi', 'kau-shambi', 'kosam'],
    'sravasti': ['sravasti', 'savatthi', 'sahet'], 'vaisali': ['vaisali', 'vesali', 'basarh'],
    'champa_ganges': ['champa', 'campa', 'bhagalpur'], 'ayodhya': ['ayodhya', 'saketa', 'ajudhya'],
    'gaya': ['gaya', 'gaya ', 'bodh'], 'kapilavastu': ['kapilavastu', 'kapilavatthu', 'piprahwa'],
    'kushinagar': ['kusinara', 'kusinagara', 'kasia'], 'hastinapura': ['hastinapura', 'hastinapur'],
    'indraprastha': ['indraprastha', 'indrapat', 'delhi'], 'mathura': ['mathura', 'methora', 'muttra'],
    'ahichchhatra': ['ahichchhatra', 'ahicchatra', 'ramnagar'], 'kampil': ['kampil', 'kampilya'],
    'sringaverapura': ['sringaverapura', 'sringavera'], 'ujjain': ['ujjain', 'ozene', 'ujjeni', 'ujjayini'],
    'mahishmati': ['mahishmati', 'maheshwar', 'mahismati'], 'vidisha': ['vidisha', 'besnagar', 'bhilsa'],
    'eran': ['eran', 'erakina'], 'madhyamika': ['madhyamika', 'bairat', 'birat'],
    'pratishthana': ['pratishthana', 'paithan', 'pithana'], 'nasik': ['nasik', 'nashik', 'pandulena'],
    'tagara': ['tagara', 'ter '], 'nevasa': ['nevasa'], 'pauni': ['pauni'], 'brahmapuri': ['brahmapuri', 'kolhapur'],
    'dharanikota': ['dharanikota', 'amaravati'], 'salihundam': ['salihundam'],
    'pithunda': ['pithunda', 'pithunda '], 'peddavegi': ['peddavegi', 'vengi', 'eluru'],
    'kotalingala': ['kotalingala'], 'sannati': ['sannati'], 'banavasi': ['banavasi', 'vanavasa'],
    'sisupalgarh': ['sisupalgarh', 'sisupala'], 'jaugada': ['jaugada', 'jaugada'],
    'tamralipti': ['tamralipti', 'tamluk', 'tamralipta'], 'chandraketugarh': ['chandraketugarh'],
    'pundranagar': ['pundranagara', 'mahasthangarh'], 'wari_bateshwar': ['wari', 'bateshwar'],
    'madurai': ['madura', 'madurai'], 'uraiyur': ['uraiyur', 'argaru', 'uraiyur '],
    'karur': ['karur', 'karuvur'], 'kaveripattinam': ['khaberis', 'poompuhar', 'kaveripattinam', 'kaveripumpattinam'],
    'arikamedu': ['arikamedu', 'poduke'], 'muziris': ['muziris'], 'korkai': ['kolkhoi', 'korkai', 'kolchoi'],
    'kodumanal': ['kodumanal'], 'alagankulam': ['alagankulam'], 'bharukachchha': ['barygaza', 'bharukachchha', 'bharuch'],
    'sopara': ['sopara', 'suppara', 'shurparaka'], 'junagadh': ['girnar', 'junagadh', 'girinagar'],
    'prabhasa': ['prabhasa', 'somnath'], 'dwarka': ['dwarka', 'dvaraka', 'dvaravati'],
}
SETTLE = {'settlement', 'urban', 'fort', 'fort-2', 'fortified-settlement', 'temple', 'temple-2', 'sanctuary', 'port', 'island', 'polis', 'archaeological-site'}
res = {}
for k, lon, lat in TARGETS:
    kws = KW.get(k, [k.split('_')[0]])
    best = None
    for i, p in places.items():
        try:
            plon = float(p['representative_longitude']); plat = float(p['representative_latitude'])
        except ValueError:
            continue
        d = math.hypot((plon - lon) * math.cos(math.radians(lat)), plat - lat)
        if d > 0.6:
            continue
        nm = allnames.get(i, set()) | {norm(p['title'])}
        if not any(any(kw in n for n in nm) for kw in kws):
            continue
        if not (types.get(i, set()) & SETTLE):
            continue
        score = (round(d, 1), -('urban' in types.get(i, ())), d)
        if best is None or score < best[0]:
            best = (score, i, p['title'], plon, plat, d, p['location_precision'])
    res[k] = best
    if best:
        print(f"{k:20} {best[1]:>9} {best[2][:36]:36} {best[3]:.2f},{best[4]:.2f} d{best[5]:.2f} {best[6][:4]}")
    else:
        print(f"{k:20} NONE")
json.dump({k: (v[1:] if v else None) for k, v in res.items()},
          open('rendered/near-east-sources/india-resolved.json', 'w', encoding='utf-8'), ensure_ascii=False)
