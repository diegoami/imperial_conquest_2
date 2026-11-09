# Diadochi campaign authoring (T293.1 / #1837)

Brief and owner decisions: [docs/design/diadochi-campaigns.md](../../docs/design/diadochi-campaigns.md). The campaigns reuse the Near East revision-2 terrain; this folder will hold the authoring script for the 323 BC and 301 BC worlds. **No world data exists yet.**

## What exists

`targets.py` lists about 125 hoped-for centres with approximate coordinates (my own geographic knowledge, labelled as hints, not evidence). `resolve_sites.py` matches each to a Pleiades place that carries a matching name **and** lies within 0.6 degrees of the hint, writing `rendered/near-east-sources/diadochi-resolved.json` (ignored). It needs the ignored `pleiades_gis_data.zip` described in [tools/NearEast/README.md](../NearEast/README.md).

## Known gaps in the first resolution (verify before use)

- **Temple or monument records stand in for the city** where the city record did not match: Damascus, Jerusalem, Aleppo, Petra, Athens (Kydathenaion), Corinth, Thebes (Greece), Olympia, Eretria, Messene, Delos, Troy, Assur, Miletus, Pergamon, Priene, Thessalonica. Their coordinates are right for placement, but their dated evidence belongs to the monument, so the city's own record must be found before it can carry dating evidence.
- **No match:** Thapsacus, Armavir. **Weak (over 0.2 degrees or a different place):** Celaenae/Apamea, Gazaca, Cassandreia (Potidaia), Lysimacheia.
- **Dating is not done.** The resolver does not test evidence at 323 or 301 BC; the Near East `sources/chronology.json` pattern (explicitly labelled contextual ranges with a cited source) is the model for the Hellenistic centres.
- **Founding dates to respect:** a city founded after the opening (Antioch, 300 BC, is the standing example) is excluded from 323 BC and labelled where used at 301 BC; Seleucia on the Tigris and Cassandreia need the same check.

## Next steps

1. Fix the gaps above and freeze a curated `cities.csv` like `tools/NearEast/cities.csv`, with owners and populations as labelled abstractions for both openings (seat lists are in the brief).
2. Add `prepare_sites.py` and `build_campaigns.py` modelled on `tools/NearEast/`, writing `data/worlds/diadochi-{323,301}bc.json`, rulesets, scenarios and guides on the shared `near-east-r2.terrain.b64` sidecar.
3. Acceptance per T293: valid data, viable openings for every seat, several rounds of real AI play, route checks, save/load, package launch.
