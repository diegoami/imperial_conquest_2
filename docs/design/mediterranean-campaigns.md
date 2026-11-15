# Mediterranean campaigns, 431, 280 and 218 BC: draft brief for T293.3

**Status: draft for owner approval; no scenario data exists yet.** Tracks [#1839](https://github.com/diegoami/imperial_conquest_2/issues/1839), part of [#1808](https://github.com/diegoami/imperial_conquest_2/issues/1808). Contract: [T293](../tasks/T293.md). Openings fixed by [v011-campaigns-and-random-maps.md](v011-campaigns-and-random-maps.md#historical-starts): **431 BC** "whole-Mediterranean power balance including Persia, Carthage, Syracuse, Greek leagues, Macedon, early Rome and other regional powers; the Peloponnesian War is one theatre"; **280 BC** "Pyrrhus entering Italy, with Rome, Carthage, Sicily and the contemporary Hellenistic powers across the whole Mediterranean"; **218 BC** "Hannibal and the opening of the Second Punic War, within the wider Mediterranean". Dates establish campaign identity, not proven city ownership, troop counts or diplomacy. Owner decision 2026-11-09: a **new map**, not the classical 320x140 world; **280 BC uses one consistent snapshot**.

## Map and engine

- **A new authored map** (owner decision 2026-11-09). Proposed frame **10W-40E, 24N-46N at 8x10 tiles per degree (400x220)**, the Near East revision-2 resolution family applied to the whole sea. It covers Gades and the Iberian coast to the Levant, the Nile to Thebes, Carthage and Numidia, Sicily, Massalia and the Rhone crossing, Macedon, Thrace and the Hellespont. **Off-frame by design, never claimed by the data:** Persia east of the Euphrates (the Persian seat is the imperial west, as at 480 BC), the steppe and India, Britain and the Baltic, Sub-Saharan Africa and Carthage's trans-Saharan trade, the Baltic amber routes and the Atlantic beyond Gades. The 280 BC snapshot's Hellenistic east sits inside the frame only to the Euphrates bend; the Seleucid interior is labelled as the seat's edge.
- Geography from the pinned **Natural Earth v5.1.2** subsets (the Near East pipeline: public-domain vectors, clip script, recorded source hashes), extended west and north to the new frame. Relief and vegetation are **authored abstractions** per the [terrain-authoring rule](terrain-authoring.md): mountains reserved for severe relief (Alps, Pyrenees, Apennines, the Atlas, the Taurus, the Pindos-Rhodope chains, the Sicilian highlands), forest as lower hills, and **deliberate plain passes** — the Alpine and Pyrenean crossings the 218 invasion question turns on, the coastal routes past the Ligurian Apennines, the Haemon/Pindos corridors, the Cilician and Syrian gates. Rivers (Ebro, Rhone, Po, Tiber, the Nile with its delta, the Danube edge) from the same clipped vectors.
- **A shared authored terrain sidecar** for all three openings; each opening carries its own **derived sidecar** for city tiles and landing aprons (the Persia pattern, bounded diff), so the city sets can differ per year without touching the shared map.
- Existing mechanics only: the five unit roles with period labels and art (431: *Hoplites*, *Peltasts*; 280/218: neutral mixed-era labels over the same five roles), Italy-derived numeric rules unchanged, generic AI policy (no classical coordinate tables on the new map), 15 regular seats plus the barbarian arrival role in every opening (frontier identities labelled per era: Thracian and Illyrian raiders at 431, the Gauls at 280 and 218, Numidian and Iberian frontier riders at 218).
- Calendar and end: **20 years, total-conquest condition** (431 to 411, 280 to 260, 218 to 198 BC). No inherited 250 BC hard stop. All three ends are designed horizons: history is not forced to repeat.

## 431 BC: proposed seats (15 regular)

The Peloponnesian War opens between Athens' empire and Sparta's league; Persia, Carthage, Syracuse and the young Roman republic are the other powers of the sea.

| Seat | Represents | Principal region |
|---|---|---|
| Athens | the Delian League empire | Attica, the Aegean, the Pontic approaches |
| Sparta | the Peloponnesian League | the Peloponnese |
| Corinth | the Isthmus and the gulf trade | the Isthmus, the Corinthian gulf shore |
| Argos | Argos and the Argolid | the Argolid |
| Boeotia | the Boeotian league at Thebes | Boeotia |
| Macedon | Perdiccas II | Macedon |
| Thessaly | the Thessalian tetrarchies | Thessaly |
| Epirus | the Molossian kingdom | Epirus |
| Persia | the imperial west — Anatolia, the Levant, the Nile satrapy | Sardis to Memphis |
| Carthage | the Punic trade empire | Africa, western Sicily, Sardinia |
| Syracuse | eastern Sicily and the Italiote friends | Syracuse, the Sicilian east coast |
| Taras | the Italiote cities | Taras, the gulf of Tarentum |
| Rome | the Latin republic and its allies | Latium |
| Etruria | the Etruscan cities | Etruria |
| Massalia | the Gallic coast and the western sea lanes | Massalia, the Ligurian shore |

**Uncertainty to label:** Persia's seat is the empire's western satrapies only (the interior is off-frame); Egypt stands inside it as a re-annexed satrapy after the Inaros revolt failed (454 BC). Cyprus carries Persian allegiance inside the Persia seat's sphere. Carthage and Syracuse are formally at peace at the opening (the Sicilian wars were intermittent); their rivalry is labelled, not scripted. Possible replacements if a seat proves unplayable: Cyrene (the Battiads) for Massalia, Illyria for Thessaly.

## 280 BC: proposed seats (15 regular) — one consistent snapshot

Pyrrhus answers Tarentum's call; Rome meets an invasion from Epirus while Carthage watches the straits. The snapshot is **spring 280 BC**, labelled as such: Macedon sits between the fall of Ptolemy Keraunos and Antigonus Gonatas' rise (279-277), and Thrace is a broken Lysimachid inheritance — the brief states one holder per seat and does not mix years.

| Seat | Represents | Principal region |
|---|---|---|
| Epirus | Pyrrhus and the Tarentine call | Epirus, Tarentum |
| Rome | the republic through the Samnite wars | Latium, Campania's edge |
| Carthage | the Punic empire | Africa, Sardinia, western Sicily |
| Syracuse | the Sicilian Greek state | Syracuse |
| Samnites | the highland federation | the Samnium hills |
| Lucanians | the southern highland peoples | Lucania, Bruttium |
| Macedon | the Antipatrid hold (snapshot label) | Macedon |
| Aetolia | the Aetolian League | Aetolia |
| Athens | the city under its restored democracy | Attica |
| Pergamon | Philetaerus' city-state | Pergamon |
| Egypt | Ptolemy II | the Nile, Cyprus, the Levant coast |
| Seleucid kingdom | Antiochus I | Syria, Mesopotamia to the Euphrates bend |
| Illyria | the Illyrian kingdoms | the Adriatic east shore |
| Rhodes | Rhodes and nearby islands | Rhodes |
| Massalia | the Gallic coast and the western trade | Massalia |

**Uncertainty to label:** Macedon's allegiance in spring 280 is a snapshot choice between claimants, labelled as designed; Thrace has no seat — its towns carry the Macedon and Illyria seats' abstractions; Tarentum belongs to Pyrrhus' seat, with the other Italiote towns as labelled friends or neutrals. The Gauls appear only as the off-map arrival role (their Tylis kingdom post-dates the snapshot). Possible replacements: Crete for Illyria, Cyrene for Massalia, Boeotia for Athens.

## 218 BC: proposed seats (15 regular)

Hannibal has taken Saguntum; Rome declares war and the war opens in Iberia.

| Seat | Represents | Principal region |
|---|---|---|
| Rome | the republic and its Italian allies | Latium, Campania, the Adriatic coast |
| Carthage | the Barcid state — Iberia and Africa | New Carthage, Gades, Carthage |
| Syracuse | Hiero II, Rome's ally | Syracuse |
| Macedon | Philip V | Macedon |
| Aetolia | the Aetolian League | Aetolia |
| Achaea | the Achaean League | the northern Peloponnese |
| Epirus | the Epirote league | Epirus |
| Egypt | Ptolemy IV | the Nile, Cyprus |
| Seleucid kingdom | Antiochus III | Syria, Mesopotamia to the Euphrates bend |
| Pergamon | Attalus I | Pergamon |
| Rhodes | Rhodes, the naval friend | Rhodes |
| Illyria | the Illyrian kingdoms | the Adriatic east shore |
| Massalia | Rome's ally on the Rhone | Massalia |
| Numidia | the Massylii and Masaesyli | Cirta, Siga |
| Athens | the neutral cultural and trade city | Attica |

**Uncertainty to label:** the Numidian seat holds both royal houses as one abstraction (Masinissa and Syphax are named leaders of its two halves in the guide, not seats); Syracuse's loyalty to Rome is Hiero's peace, labelled as designed; Philip V is neutral at the opening (his pact with Hannibal came in 215) and Aetolia watches from its peace with Philip. Roman colonies founded in 218 (Placentia, Cremona) are excluded, not labelled.

## Cities, sources and evidence

- Cities follow the existing authoring pattern: a curated list (about 130 centres, regional centres where documentation is thin), positions and dated evidence from the pinned Pleiades subset plus supplemental chronology, owners and populations as **designed abstractions**. The committed Near East and Diadochi subsets carry the eastern centres; the **western Mediterranean needs new resolutions** (Rome, Carthage, Utica, Syracuse, Akragas, Panormus, Messana, Neapolis, Capua, Taras, Croton, Massalia, Emporion, Saguntum, New Carthage, Gades, Cirta and the Etruscan and Samnite centres), taken from the same archive snapshot the Persia subset records.
- Founding-date review at authoring time: Massalia (about 600 BC) ✓; Rome and the Etruscan cities ✓; the Samnite and Lucanian seats use the owner-approved regional-centre pattern at dated archaeological sites; Numidia's royal seats (Cirta, Siga) carry Pleiades bounds; colonies founded after an opening are excluded (Placentia and Cremona at 218, Aquileia, all Hellenistic foundations east of the frame where their bounds demand it).
- Secondary orientation: the [Met's Hellenistic essays](https://www.metmuseum.org/essays/art-of-the-hellenistic-age-and-the-hellenistic-tradition) and Livius overviews (Pyrrhus, the First and Second Punic War, the Peloponnesian War). Primary sources by title only: Thucydides, *History*; Polybius, *Histories* (books 1-3 for 264-218); Livy, *Ab Urbe Condita* (books 21-22 for 218). Specific claims are sourced one by one at authoring time, none asserted here.
- Starting forces and economy are **designed**, scaled from represented population as the Near East, Diadochi and Persia scenarios do; no troop census, no precise-year border or treaty claim.

## Acceptance (this task, per the T293 contract)

All three campaigns selectable with guide entries (T290), valid deterministic data, a viable opening for every playable seat, several rounds of real AI play without stalls or rejections on the larger map, a cross-range route check (the Alpine and Pyrenean crossings, the Greek corridors), save/load and copied-package launch, and a build script reproducing the data. The three openings share the authored sidecar and each carries a derived sidecar diff test (the Persia bound). `MediterraneanCampaignTests` mirror the Persia suite for the three years; the listed-cards test and the map-fit checks (400x220) gain the new entries.

## Owner decisions to take

1. **Map frame and scale**: proposed 10W-40E, 24N-46N at 8x10 tiles per degree (400x220). Alternatives: a narrower sea frame (30N-46N — Egypt without Thebes, 400x170); or a coarser 6x8 tiles per degree (288x176, closer to classical pacing and cheaper to author).
2. **All three seat lists** as drafted (uncertain holdings stay labelled), or amended.
3. **Default human seats**: proposed **Athens at 431**, **Epirus (Pyrrhus) at 280**, **Carthage (Hannibal) at 218**. Alternatives: Sparta at 431; Rome at 280; Rome at 218.
4. **Opening diplomacy**: wars in progress — **Sparta-Athens at 431**, **Epirus-Rome at 280**, **Rome-Carthage at 218**; every other pair neutral. The alternative is the all-neutral Diadochi precedent.

**Next:** with the decisions recorded, the map authoring script (geography subsets, relief and passes, terrain import), then the curated city list, worlds, rulesets, scenarios, guides and the acceptance checks.
