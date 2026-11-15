# Persia campaigns, c.550 BC and 480 BC: draft brief for T293.2

**Status: brief approved by the owner 2026-11-15; no scenario data exists yet.** Tracks [#1838](https://github.com/diegoami/imperial_conquest_2/issues/1838), part of [#1808](https://github.com/diegoami/imperial_conquest_2/issues/1808). Contract: [T293](../tasks/T293.md). Openings fixed by [v011-campaigns-and-random-maps.md](v011-campaigns-and-random-maps.md#historical-starts): **c.550 BC** "Rise of Cyrus: an emerging empire among competing regional powers" and **480 BC** "Xerxes: an established empire and the Greek maritime/land coalition". Dates establish campaign identity, not proven city ownership, troop counts or diplomacy.

## Map and engine

- Reuse the **Near East revision-2 map** (`near-east-r2`, 276x185 at 8x10 tiles per degree, 20E-54.5E, 24N-42.5N), as the Diadochi campaigns already do. It holds Macedon, the Aegean, Anatolia, the Levant, Egypt, Mesopotamia, Media and Persis, and it covers the whole 480 BC Greek theatre: Thermopylae (about 22.5E, 38.8N), Salamis and Attica, the Isthmus, the Hellespont crossings at about 40.2N and the Thracian mustering coast. The 550 BC Persian heartland fits with a margin (Pasargadae 53.2E against the 54.5E frame edge; the southern frame at 24N holds the Persian Gulf coast and Egypt to the first cataract).
- **Off-frame by design, never claimed by the data:** the far eastern satrapies (Bactria, Sogdiana, Arachosia, Drangiana, India) — at 550 BC Cyrus has not conquered them yet, and at 480 BC they are interior provinces whose absence is labelled in the scenario brief. The western Mediterranean (Sicily, South Italy; Himera and Syracuse of 480 BC) never lies on this map. Corcyra and the western Greek islands lie outside the frame edge and are not represented.
- The terrain authoring rule applies; the revision-2 passes already serve the main Taurus, Zagros and Elburz crossings. The campaigns add **no terrain change**. A derived sidecar (`persia.terrain.b64`) carries only city tiles and landing aprons at new coastal centres, bounded like the Diadochi diff (35 cells). A cross-range route check for every seat start is part of acceptance (Babylon/Susa to Persis across the Zagros, Cilicia to Cappadocia across the Taurus, the Aegean routes at 480).
- Existing mechanics only: the five unit roles with period labels and art (Greek *Hoplites*, Persian *Sparabara* and *Persian archers*, cavalry labelled for the period), Italy-derived numeric rules unchanged, generic AI policy, 15 regular seats plus the barbarian arrival role (the nation budget the 750 BC and Diadochi campaigns already use). Chariot combat and the Immortals as a special rule are not modelled; units get period names and art only.
- Calendar and end: **20 years, total-conquest condition** (550 to 530 BC, and 480 to 460 BC). No inherited 250 BC hard stop. Both ends are designed horizons: history is not forced to repeat.

## c.550 BC: proposed seats (15 regular)

The war between Astyages and his grandson Cyrus is in progress; Lydia, Babylon and Egypt are the other established powers. Names are **grouped polities**, a deliberate compression, not unified states.

| Seat | Represents | Principal region |
|---|---|---|
| Persis | Cyrus, king of Anshan and Persis | Persis, the Zagros valleys |
| Media | Astyages | Media, the plateau north to the Elburz |
| Lydia | Croesus | Western Anatolia to the Aegean coast |
| Babylon | Nabonidus | Mesopotamia, the Euphrates towns |
| Egypt | Amasis II | The Nile, the Saite kingdom |
| Susiana | the Neo-Elamite remnants | Susa and the Uxian valleys |
| Cilicia | the Syennesis | the Cilician plain and passes |
| Cappadocia | the local rulers | the central plateau frontier |
| Ionia | the Greek coastal cities, under Lydian tribute | Ionia and the islands |
| Phoenicia | the Levantine coast cities, under Babylonian tribute | Tyre, Sidon, the coast |
| Judah | the Babylonian west — Judah, Samaria, Philistia | the Levant hills |
| Cyprus | the Cypriot kingdoms | Cyprus |
| Armenia | the highland rulers of former Urartu | Armenia, Lake Van |
| Sparta | the Peloponnesian League | the Peloponnese |
| Athens | Athens under the Peisistratids | Attica |

**Uncertainty to label in the brief:** Susiana's allegiance in 550 BC is not established (Neo-Elamite princes, Babylonian influence or Median control); the seat is a labelled regional abstraction. Cappadocia sits in the contested Lydia-Media sphere. Caria has no separate seat — its cities carry the Lydian tribute abstraction. Cyrus's eastern conquests (Hyrcania, Parthia, Bactria) lie off-frame and are not seats. Opening diplomacy (owner decision 2026-11-15): **Persis and Media at war** (the war of the opening, 553-550 BC, a designed abstraction); every other pair neutral.

## 480 BC: proposed seats (15 regular)

Xerxes' invasion is under way: the empire marches with its subject and allied contingents, and the resisting Greek coalition holds the Isthmus and the sea.

| Seat | Represents | Principal region |
|---|---|---|
| Persia | Xerxes | Susa, Persepolis, Media, the imperial core |
| Sparta | the coalition's land leadership | the Peloponnese |
| Athens | the fleet and the evacuated city | Attica |
| Corinth | the coalition's naval base on the Isthmus | the Isthmus, the Gulf shore |
| Argos | Argos, watching from neutrality | the Argolid |
| Boeotia | the medizing league at Thebes | Boeotia |
| Thessaly | the medizing Aleuads | Thessaly |
| Macedon | Alexander I, the empire's submitted guide | Macedon |
| Thrace | the European satrapy and mustering coast | Doriscus, Eion, the Stryme towns |
| Ionia | the Greek cities of Asia, Persian since 494 | Ionia |
| Caria | Artemisia and the Carian dynasts | Caria |
| Lycia | the Xanthian dynasts, ships and coast | Lycia, Pamphylia |
| Cilicia | the Syennesis, ships and passes | Cilicia |
| Phoenicia and Cyprus | the fleet's core contingents | Tyre, Sidon, Cyprus |
| Egypt | the satrapy, re-subdued after the 486 revolt | the Nile |

**Uncertainty to label:** Argos stayed out of the coalition; Herodotus reports Persian negotiation — the opening keeps Argos neutral and labels the stance. Egypt's and Babylonia's revolts (486-484 BC) were suppressed before the invasion; Egypt is a re-subdued satrapy seat, and there is **no Babylon seat** — the Mesopotamian towns are Persia's own holdings, labelled as such, so the empire seats one huge but playable core. Lycia stands in for the empire's smaller ship contingents; the Ionian cities carry Persian garrison influence. Opening diplomacy (owner decision 2026-11-15): **Persia at war with Sparta, Athens and Corinth** (the resisting coalition); every other pair neutral, with the medizing seats' stance labelled in the scenario brief.

## Cities, sources and evidence

- Cities follow the existing authoring pattern: a curated list (about 130 centres, regional centres where documentation is thin), positions and dated evidence from the pinned Pleiades subset plus supplemental chronology, owners and populations as **designed abstractions**. The committed `tools/NearEast/sources/sites.json` and `tools/Diadochi/sources/sites.json` subsets already carry settlement records with dated evidence for nearly every centre the two openings need; the new `tools/Persia/prepare_sites.py` re-extracts from a current archive snapshot (the pinned 2026-10-30 archive is not re-downloadable), records the new snapshot date and SHA-256, and diffs its shared records against the committed subsets as part of the review.
- Centres beyond the Diadochi subset: **Sais** (the Saite capital, bounds from 750 BC), the **Thracian coast** for 480 (Doriscus, Eion, Acanthos), and **Thermopylae** as a regional pass centre. **Anshan/Tal-i Malyan is not used**: the archive interval spans the opening, but the site's occupation ended centuries before it — the committed pattern's rule against inserting abandoned phases.
- **Founded after the opening is excluded or labelled:** Persepolis (begun about 515 BC) is a centre at 480 and **not present at 550**. Pasargadae (begun about 546 BC) needs a labelled contextual range for the 550 opening: Cyrus's court at the campaign's start year is a designed abstraction. Thin bounds noted at authoring time: Assur's bounds end at 540 BC (not used at 480; Arbela and Nineveh carry the region), and Halicarnassus, Xanthos, Tralles, Lamia, Nicaea and Heraclea Pontica begin exactly at 550 BC — thin at 550, contextual ranges where they are used in that opening.
- Secondary orientation (the v011 scope's owner list): [Met: Achaemenid Persia](https://www.metmuseum.org/essays/the-achaemenid-persian-empire-550-330-b-c). Primary narrative sources to cite by title only: Herodotus, *Histories* (books 1 and 7-9); the Nabonidus Chronicle; the Behistun inscription. Livius articles (Cyrus, Astyages, Xerxes, Thermopylae, Salamis) as overviews. Specific claims are sourced one by one at authoring time, none asserted here.
- Starting forces and economy are **designed**, scaled from represented population as the Near East and Diadochi scenarios do; no troop census, no precise-year border or treaty claim.

## Acceptance (this task, per the T293 contract)

Both campaigns selectable with guide entries (T290), valid deterministic data, a viable opening for every playable seat, several rounds of real AI play without stalls or rejections, a cross-range route check, save/load and copied-package launch, and a build script reproducing the data. Each year has its own world file (cities, owners, forces) on a derived revision-2 sidecar, as the Diadochi pair does. `PersiaCampaignTests` mirror the Diadochi suite (load and validation, Italy-rules identity, 200 AI seat turns on two seeds per year, fleet orders, barbarian arrival, embark/landing, dated evidence, derived-sidecar diff, pinned per-seat opening counts, default-seat agreement with the guide); the listed-cards test's pinned scenario list gains the two Persia entries.

## Owner decisions, 2026-11-15

1. Both seat lists approved as drafted (uncertain holdings stay labelled in the scenario brief).
2. Default human seat: **Persis (Cyrus) at 550**, **Athens at 480**.
3. Horizon: 20 years, total conquest (ends 530 and 460 BC).
4. Opening diplomacy: wars in progress — **Persis-Media at 550**, **Persia against Sparta, Athens and Corinth at 480**; every other pair neutral.

**Next:** the curated city list with dated Pleiades evidence and supplemental chronology (new `tools/Persia/` authoring scripts on the shared revision-2 terrain), then worlds, rulesets, scenarios, guides and the acceptance checks.
