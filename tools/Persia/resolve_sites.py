"""Resolve the approximate Persia city targets (targets.py) to Pleiades places.

Run from the repository root: python -X utf8 tools/Persia/resolve_sites.py
Needs the ignored rendered/near-east-sources/pleiades_gis_data.zip (see tools/NearEast/README.md).
A place must match a name keyword AND lie within 0.6 degrees of the hint. The output is a
starting point to verify by hand, not a final selection. Centres whose Pleiades record the
committed Near East / Diadochi subsets already carry are listed in cities.csv directly; this
aid exists to find records for centres new to that era (Sais, the Thracian coast, the small
Peloponnesian and Ionian towns, Thermopylae).
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
    'sais': ['sais', 'sa el'], 'memphis': ['memphis'], 'thebes_egypt': ['thebai', 'diospolis', 'thebes'],
    'pelusium': ['pelus'], 'naucratis': ['naukrat', 'naucrat'], 'coptos': ['kopt', 'copt'], 'syene': ['syene'],
    'cyrene': ['cyrene', 'kyrene'],
    'tyre': ['tyr'], 'sidon': ['sidon'], 'byblos': ['byblos', 'gebal', 'gubla'], 'arados': ['arad'],
    'damascus': ['damas'], 'gaza': ['gaza'], 'jerusalem': ['jerusalem', 'hierosolym'],
    'samaria': ['samaria', 'sebaste'], 'ascalon': ['ascalon', 'askalon', 'ashqelon'], 'joppa': ['ioppe', 'joppa'],
    'akko': ['akko', 'ake', 'ptolemais', 'acre'], 'aleppo': ['beroi', 'beroea', 'halab', 'aleppo'],
    'hamath': ['hamath', 'amathe', 'epiphaneia'], 'emesa': ['emesa'], 'palmyra': ['palmyra'], 'petra': ['petra'],
    'salamis_cy': ['salamis'], 'paphos': ['paphos'], 'kition': ['kition', 'citium'], 'soloi_cy': ['soloi'],
    'amathus': ['amathous', 'amathus'], 'kourion': ['kourion', 'curium'],
    'sardis': ['sardis', 'sardeis'], 'ephesus': ['ephes'], 'miletus': ['miletus', 'miletos'],
    'halicarnassus': ['halikarn', 'halicarn'], 'smyrna': ['smyrna'], 'pergamon': ['pergam'],
    'troy': ['troia', 'ilium', 'ilion', 'troy'], 'abydos': ['abydos'], 'cyzicus': ['kyzik', 'cyzic'],
    'lampsacus': ['lampsak', 'lampsac'], 'colophon': ['kolophon', 'colophon'], 'priene': ['priene'],
    'tralles': ['tralles'], 'xanthos': ['xanthos'], 'telmessus': ['telmessos', 'telmessus'],
    'rhodes_city': ['rhodos', 'rhodes'], 'cnidus': ['knidos', 'cnidus'], 'cos': ['kos', 'cos'],
    'kyme': ['kyme', 'cyme'], 'erythrae': ['erythra'], 'klazomenai': ['klazomen', 'clazomen'],
    'iasos': ['iasos', 'iasus'], 'kaunos': ['kaunos', 'caunus'],
    'gordion': ['gordion'], 'ancyra': ['ancyra', 'ankyra'], 'celaenae': ['kelainai', 'celaenae', 'apameia'],
    'pessinus': ['pessinous', 'pessinus'], 'dorylaeum': ['dorylai'], 'sinope': ['sinope'],
    'amisus': ['amisos', 'amisus'], 'heraclea_pontica': ['heraclea', 'herakleia'],
    'chalcedon': ['chalcedon', 'chalkedon'], 'byzantium': ['byzantion', 'byzantium'],
    'nicaea': ['nicaea', 'nikaia'], 'amaseia': ['amaseia'], 'mazaca': ['mazaka', 'caesarea'], 'tyana': ['tyana'],
    'trapezus': ['trapez'], 'tarsus': ['tarsos', 'tarsus'], 'soloi_cilicia': ['soloi', 'pompeiopolis'],
    'issus': ['issos', 'issus'], 'adana': ['adana'], 'cybistra': ['cybistra', 'kybistra'],
    'side': ['side'], 'perge': ['perge'], 'aspendos': ['aspendos'], 'phaselis': ['phaselis'],
    'sagalassos': ['sagalassos'],
    'babylon': ['babylon'], 'opis': ['opis'], 'nippur': ['nippur'], 'uruk': ['uruk', 'orchoe'],
    'nineveh': ['nineveh', 'ninos'], 'arbela': ['arbela'], 'assur': ['ashur', 'assur'],
    'carrhae': ['carrhae', 'harran'], 'nisibis': ['nisibis'], 'samosata': ['samosata'], 'melitene': ['melitene'],
    'susa': ['susa'], 'ecbatana': ['ecbatana', 'hagmatana'], 'persepolis': ['persepolis'],
    'pasargadae': ['pasargad'], 'rhagae': ['rhag'], 'gabae': ['gabai', 'gabae', 'aspadana'],
    'gazaca': ['gazaka', 'gazaca', 'ganzak', 'shiz'], 'tushpa': ['tushpa', 'tuspa', 'thospia'],
    'erebuni': ['erebuni', 'yerevan', 'erivan'], 'anshan': ['anshan', 'malyan'],
    'shushtar': ['shushtar', 'turgan'], 'taoke': ['taoke'], 'concobar': ['konkobar', 'concobar'],
    'hasanlu': ['hasanlu'], 'maragheh': ['maragha'], 'zanjan': ['zanjan'],
    'athens': ['athenai', 'athens'], 'corinth': ['korinth', 'corinth'], 'sparta': ['sparta'],
    'thebes_greece': ['thebai', 'thebae', 'thebes'], 'argos': ['argos'], 'delphi': ['delphi'],
    'thermopylae': ['thermopyl'], 'pella': ['pella'], 'aegae': ['aigai', 'aegae'],
    'larisa': ['larisa', 'larissa'], 'pharsalus': ['pharsal'], 'lamia': ['lamia'],
    'chalcis': ['chalkis', 'chalcis'], 'eretria': ['eretria'], 'megara': ['megara'],
    'ambracia': ['ambrakia', 'ambracia'], 'dodona': ['dodona'], 'thermon': ['thermon', 'thermum'],
    'delos': ['delos'], 'paros': ['paros'], 'naxos': ['naxos'], 'samos': ['samos'], 'chios': ['chios'],
    'mytilene': ['mytilene', 'lesbos'], 'plataia': ['plat'], 'thespiai': ['thespia'],
    'tanagra': ['tanagra'], 'sikyon': ['sikyon', 'sicyon'], 'phleious': ['phleious', 'phlius'],
    'amyklai': ['amyklai', 'amyclae'], 'gythion': ['gythion', 'gytheion'], 'nauplion': ['nauplion', 'nauplia'],
    'mycenae': ['mycenae', 'mykenai'], 'olympia': ['olympia'], 'messene': ['messene'],
    'eion': ['eion'], 'doriscos': ['dorisk'], 'akanthos': ['akanthos', 'acanth'],
    'abdera': ['abdera'], 'perinthus': ['perinth', 'herakleia'], 'sestos': ['sestos'],
    'lysimacheia': ['lysimach'], 'cassandreia': ['kassandr', 'cassandr', 'potidaia'],
    'amphipolis': ['amphipolis'], 'pydna': ['pydna'],
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
        print(f"{k:17} {best[1]:>9} {best[2][:40]:40} {best[3]:.2f},{best[4]:.2f} d{best[5]:.2f} {best[6][:4]}")
    else:
        print(f"{k:17} NONE")
json.dump({k: (v[1:] if v else None) for k, v in res.items()},
          open('rendered/near-east-sources/persia-resolved.json', 'w', encoding='utf-8'), ensure_ascii=False)
