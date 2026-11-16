"""Approximate India centre hints (T293.4): id, longitude, latitude.

Hints, not evidence: resolve_sites.py matches each hint against the pinned Pleiades archive
(name keyword AND within 0.6 degrees). Centres whose committed records the Near East /
Mediterranean / Diadochi / Persia / Mediterranean subsets already carry are absent here; the
regional hints with no Pleiades settlement record become author-hint centres (regional=true)
with labelled contextual ranges in sources/chronology.json, per the Madaktu pattern.
"""
T = """taxila 72.55 33.75|pushkalavati 71.62 34.16|bazira 72.92 34.65|bucephala 73.45 32.69|
nicaea_india 72.32 32.62|sagala 74.53 32.24|mulasthana 71.45 30.22|harappa 72.85 30.63|
alexandria_indus 68.07 24.72|roruka 68.88 27.18|sehwan 67.80 26.42|abhisara 74.30 33.20|
pataliputra 85.15 25.60|rajagriha 85.42 25.02|varanasi 83.02 25.38|kaushambi 81.35 25.34|
sravasti 82.03 27.50|vaisali 85.13 26.00|champa_ganges 86.97 25.25|ayodhya 82.20 26.80|
gaya 84.99 24.70|kapilavastu 83.05 27.48|kushinagar 83.70 26.74|hastinapura 78.05 29.17|
indraprastha 77.17 28.61|mathura 77.53 27.49|ahichchhatra 79.13 28.42|kampil 79.40 27.60|
sringaverapura 78.90 27.20|bhita 81.80 25.20|ujjain 75.78 23.18|mahishmati 75.58 22.17|
vidisha 77.70 23.55|eran 78.15 24.10|madhyamika 76.35 27.40|pratishthana 75.40 19.48|
nasik 73.80 20.00|tagara 76.22 18.32|nevasa 74.95 19.55|bhokardan 75.65 20.65|
pauni 79.65 21.03|kaundanyapur 78.25 21.35|brahmapuri 74.22 16.72|dharanikota 80.35 16.58|
salihundam 83.93 18.30|pithunda 83.35 17.85|peddavegi 80.98 16.71|kotalingala 79.20 18.50|
sannati 76.75 16.75|banavasi 75.02 14.98|sisupalgarh 85.82 20.25|jaugada 84.75 19.65|
kalingapatnam 84.60 18.30|tamralipti 88.30 22.28|chandraketugarh 88.68 22.85|pundranagar 89.72 24.95|
wari_bateshwar 92.15 24.08|madurai 78.12 9.92|uraiyur 78.85 10.85|karur 78.07 10.95|
kaveripattinam 79.85 11.15|arikamedu 79.82 11.90|muziris 76.20 10.15|korkai 78.06 8.64|
kodumanal 77.60 11.10|alagankulam 78.98 9.36|kanchipuram 79.70 12.83|bharukachchha 72.98 21.70|
sopara 72.83 19.40|junagadh 70.45 21.52|prabhasa 70.40 20.89|dwarka 68.97 22.24|
kuninda 77.10 31.40|audumbara 75.65 32.30|sibi 72.10 30.80"""
TARGETS = [(a[0], float(a[1]), float(a[2])) for a in (x.split() for x in T.replace("\n", "|").split("|") if x.strip())]
