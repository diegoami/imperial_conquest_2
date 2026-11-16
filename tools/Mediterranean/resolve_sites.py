"""Resolve the approximate Mediterranean city targets (targets.py) to Pleiades places.

Run from the repository root: python -X utf8 tools/Mediterranean/resolve_sites.py
Needs the ignored rendered/near-east-sources/pleiades_gis_data.zip (see tools/NearEast/README.md).
A place must match a name keyword AND lie within 0.6 degrees of the hint. The output is a
starting point to verify by hand, not a final selection. Centres whose Pleiades record the
committed Near East / Diadochi / Persia subsets already carry are listed in cities.csv directly;
this aid exists to find records for the western and new Hellenistic centres.
"""
from pathlib import Path
import csv, io, zipfile, json, math, sys, unicodedata
sys.path.insert(0, str(Path(__file__).resolve().parent))
from targets import TARGETS

z = zipfile.ZipFile('rendered/near-east-sources/pleiades_gis_data.zip')


def rows(n):
    return csv.DictReader(io.TextIOWrapper(z.open('data/gis/' + n + '.csv'), encoding='utf-8-sig'))


def norm(t):
    return ''.join(c for c in unicodedata.normalize('NFKD', t).lower() if c.isalnum() and not unicodedata.combining(c))


places = {r['id']: r for r in rows('places')}
allnames = {}
for r in rows('names'):
    allnames.setdefault(r['place_id'], set()).update(
        norm(r[k]) for k in ('title', 'romanized_form_1', 'romanized_form_2', 'romanized_form_3') if r[k])
types = {}
for r in rows('places_place_types'):
    types.setdefault(r['place_id'], set()).add(r['place_type'])

KW = {
    'rome': ['roma', 'rome'], 'capua': ['capua'], 'neapolis': ['neapol', 'napoli', 'parthenope'],
    'venusia': ['venusia', 'venosa'], 'luceria': ['luceria'], 'beneventum': ['benevent', 'malevent'],
    'ariminum': ['arimin', 'rimini'], 'brundisium': ['brund', 'brentesion', 'brindisi'],
    'volsinii': ['volsinii', 'volsini', 'bolsena'], 'clusium': ['clusium', 'chiusi', 'clusi'],
    'caere': ['caere', 'cerveteri', 'agylla'], 'tarquinii': ['tarquini'], 'populonia': ['populon', 'fufluna'],
    'bovianum': ['bovian', 'boiano'], 'saepinum': ['saepin'], 'compsa': ['compsa'],
    'aesernia': ['aesern', 'isernia'], 'grumentum': ['grument'], 'laus': ['laus'], 'petelia': ['petelia'],
    'taras': ['taras', 'taranto', 'tarentum'], 'metapontum': ['metapont'], 'heracleia_lucania': ['herakleia', 'heraclea', 'policoro'],
    'thurii': ['thurii', 'thurioi', 'sybaris'], 'croton': ['croton', 'kroton', 'crotone'],
    'locri': ['locri', 'lokri', 'locroi'], 'rhegium': ['rhegion', 'rhegium', 'reggio'],
    'syracuse': ['syracus', 'surakous'], 'akragas': ['akragas', 'agrigent'], 'gelas': ['gela'],
    'katane': ['katane', 'catania'], 'messana': ['messana', 'zancle', 'messina'], 'leontinoi': ['leontino'],
    'panormus': ['panorm', 'palermo'], 'motya': ['motya', 'mozia', 'san pantaleo'],
    'lilybaeum': ['lilybae', 'marsala'], 'solus': ['solus', 'soloeis', 'solunto'],
    'carthage': ['carthag', 'karchedon', 'kart-hadasht'], 'utica': ['utica', 'uttica'],
    'hadrumetum': ['hadrumet', 'soussa', 'sousse'], 'hippo_regius': ['hippo', 'annaba', 'bona'],
    'leptis_magna': ['leptis'], 'cirta': ['cirta', 'constantine'], 'siga': ['siga'], 'tipasa': ['tipasa', 'tipassa'],
    'massalia': ['massalia', 'massili', 'marseille'], 'agatha': ['agathe', 'agde'],
    'emporion': ['emporion', 'empuries', 'emporiae'], 'rhode_iberia': ['rhoda', 'roses'],
    'saguntum': ['sagunt', 'zacinth'], 'carthago_nova': ['carthago nova', 'kart-hadasht', 'cartagena'],
    'gades': ['gades', 'gadir', 'cadiz'], 'scodra': ['scodra', 'skadar', 'shkodra'],
    'lissos': ['lissos', 'lissus', 'lezhe'], 'apollonia_illyria': ['apollonia'],
    'dyrrhachium': ['dyrrhach', 'epidamn', 'durres', 'durrazzo'], 'aegion': ['aegion', 'aigion', 'aegium', 'egion'],
    'olynthus': ['olynth'], 'demetrias': ['demetri'], 'maroneia': ['maroneia'],
    'ptolemais_egypt': ['ptolemais', 'el manshah'], 'laodicea_syria': ['laodicea'],
    'seleucia_pieria': ['seleuceia', 'seleucia', 'selevki'],
    'apamea_syria': ['apameia', 'pella'], 'elaea': ['elaia', 'elaea'], 'camirus': ['kamiros', 'camirus'],
    'ialysos': ['ialysos', 'ialsyos'],
}
SETTLE = {'settlement', 'urban', 'fort', 'fort-2', 'fortified-settlement', 'temple', 'temple-2', 'sanctuary', 'port', 'island', 'polis'}
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
          open('rendered/near-east-sources/mediterranean-resolved.json', 'w', encoding='utf-8'), ensure_ascii=False)
