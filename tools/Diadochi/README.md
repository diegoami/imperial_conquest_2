# Diadochi campaign authoring (T293.1 / #1837)

Brief and owner decisions: [docs/design/diadochi-campaigns.md](../../docs/design/diadochi-campaigns.md). The campaigns reuse the Near East revision-2 terrain; this folder will hold the authoring script for the 323 BC and 301 BC worlds. **No world data exists yet.**

## What exists

`targets.py` lists about 125 hoped-for centres with approximate coordinates (my own geographic knowledge, labelled as hints, not evidence). `resolve_sites.py` matches each to a Pleiades place that carries a matching name **and** lies within 0.6 degrees of the hint, writing `rendered/near-east-sources/diadochi-resolved.json` (ignored). It needs the ignored `pleiades_gis_data.zip` described in [tools/NearEast/README.md](../NearEast/README.md).

## Curated city list (frozen draft)

`cities.csv` has 143 centres with columns `id, pleiades, caption323, owner323, caption301, owner301, population, regional`. Owners and populations are **designed abstractions** (15 seats per opening, seat ids as in the brief); an empty caption/owner means the centre does not exist at that opening.

- **City records, not monuments:** the temple/monument stand-ins (Damascus, Jerusalem, Aleppo, Petra, Athens, Corinth, Thebes, Olympia, Eretria, Messene, Delos, Troy, Assur, Miletus, Pergamon, Priene, Thessalonica) now use the settlement record found by title and type.
- **Absent at the opening:** Thebes in Greece (destroyed 335, refounded 316) and Thessalonica (315) only appear in 301; Seleucia on the Tigris (about 305) only in 301; Edessa only in 301. Lysimacheia is "Kardia" and Cassandreia "Potidaia" (regional) in 323. Antioch (300) is not included in either opening.
- **No Pleiades record:** Thapsacus, Tushpa and Armavir have a blank `pleiades` and `regional=true`; `prepare_sites.py` must take their coordinates from `targets.py` and label them as hints.
- **Thin seats:** 143 centres now. Near East inland regional sites (Anshan, Shushtar, Sialk, Godin, Giyan, Qalaichi, Hasanlu, Haftavan, Marlik, Sos Hoyuk, Bozkurt, Kanesh, Alisar, Adana) are added as labelled regional centres with the seat that holds their region; Megara goes to Athens at 301. Still small: Epirus 2, Aetolia 3, Athens 2 (301), Armenia 4. Viability is judged in the acceptance runs, not here.
- **Contested holdings at 301:** Coele-Syria (Gaza, Jerusalem, Samaria, Ascalon, Joppa, Akko) is given to Egypt; Lycia and Pamphylia are given to Egypt as a disputed coast; Caria and Cilicia to Pleistarchus ("Cilicia" seat). One consistent snapshot, labelled in the scenario brief.
- **Dating is not done.** The Near East `sources/chronology.json` pattern (labelled contextual ranges with a cited source) is the model for the Hellenistic centres.

## Next steps

1. Fill the thin seats, then add dated evidence (`sources/chronology.json`) for the Hellenistic centres.
2. Add `prepare_sites.py` and `build_campaigns.py` modelled on `tools/NearEast/`, writing `data/worlds/diadochi-{323,301}bc.json`, rulesets, scenarios and guides on the shared `near-east-r2.terrain.b64` sidecar.
3. Acceptance per T293: valid data, viable openings for every seat, several rounds of real AI play, route checks, save/load, package launch.
