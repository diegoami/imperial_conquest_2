# Diadochi campaigns, 323 BC and 301 BC: draft brief for T293.1

**Status: draft for owner approval; no scenario data exists.** Tracks [#1837](https://github.com/diegoami/imperial_conquest_2/issues/1837), part of [#1808](https://github.com/diegoami/imperial_conquest_2/issues/1808). Contract: [T293](../tasks/T293.md). Openings fixed by [v011-campaigns-and-random-maps.md](v011-campaigns-and-random-maps.md#historical-starts): **323 BC** "succession after Alexander" and **301 BC** "the successor balance after Ipsus". Dates establish campaign identity, not proven city ownership, troop counts or diplomacy.

## Map and engine

- Reuse the **Near East revision-2 map** (`near-east-r2`, 276x185 at 8x10 tiles per degree, 20E-54.5E, 24N-42.5N). It holds Macedon, the Aegean, Anatolia, Egypt, Syria, Mesopotamia, Media and Persis. It does **not** hold Alexander's eastern satraps (Bactria, Sogdiana, India) or Carthage and the west; these are outside both openings by design and appear, if at all, as a labelled off-map arrival role (the existing barbarian role), never as seats.
- The terrain authoring rule applies; the revision-2 passes already serve the main Taurus, Zagros and Elburz crossings. The new campaigns add **no terrain change**. A cross-range route check for any new seat start is part of acceptance.
- Existing mechanics only: the five unit roles, Italy-derived numeric rules as the Near East campaigns use, generic AI policy, 15 regular seats plus the barbarian role (the nation budget the 750 BC campaign already uses). Chariot combat and elephants are not modelled; units get period names and art only (decision 2026-11-09).
- Calendar and end: **20 years, total-conquest condition**, as the Near East campaigns (323 to 303 BC, and 301 to 281 BC). No inherited 250 BC hard stop. Both ends are designed horizons: history is not forced to repeat.

## 323 BC: proposed seats (15 regular)

The death of Alexander at Babylon and the settlement that followed. The names below are **grouped polities and regional commands**, a deliberate compression, not unified states.

| Seat | Represents | Principal region |
|---|---|---|
| Macedon | Antipater, with Craterus, as European strategos | Macedon, Thessaly |
| Royal army | Perdiccas and the kings Philip III and the infant Alexander IV | Babylonia, Mesopotamia |
| Egypt | Ptolemy, satrap | Egypt, Cyrene (map edge) |
| Thrace | Lysimachus | Thrace |
| Phrygia | Antigonus Monophthalmus | Greater Phrygia, Lycia, Pamphylia |
| Cappadocia | Eumenes and the local dynasty | Cappadocia, Paphlagonia |
| Western Anatolia | the satraps of Lydia, Caria and the Hellespont | Ionia, Caria, Lydia |
| Syria and Phoenicia | the Macedonian-held Levant and the Phoenician cities | Coastal Levant, Cyprus |
| Media | Peithon, with Atropates in the north | Media, Atropatene |
| Persis | the plateau satrapies | Persis, Susiana |
| Athens and its allies | the Greek revolt, the Lamian War | Attica, central Greece |
| Aetolia | the Aetolian League | Aetolia |
| Epirus | the Aeacid kingdom | Epirus |
| Rhodes and the islands | Rhodes, expelling its garrison in 323 | Aegean islands |
| Armenia and the Caucasus | the local Orontid and highland rulers | Armenia |

Possible replacements if a seat proves unplayable: merge Epirus into Macedon's neighbours or Aetolia, add the Cyprian kings separately. **Uncertainty to label in the brief:** the extent of each command in June 323 BC, and whether Seleucus holds any office (he receives Babylon in 321 BC, so he is a named general at the Royal army seat, not a seat). The 323 BC setting is deliberately the *start of the settlement*, before Triparadisus (321 BC).

## 301 BC: proposed seats (15 regular)

After Ipsus (301 BC): Antigonus is dead and his realm is divided; Demetrius survives as a sea power.

| Seat | Represents | Principal region |
|---|---|---|
| Seleucid kingdom | Seleucus I | Syria, Mesopotamia, the plateau |
| Thrace and Anatolia | Lysimachus | Thrace, northern and western Anatolia |
| Egypt | Ptolemy I | Egypt, claimed Coele-Syria |
| Macedon | Cassander | Macedon, Thessaly |
| Demetrius | the surviving Antigonid fleet and bases | Cyprus, Tyre, Sidon, Aegean bases |
| Cilicia | Cassander's family holding after the partition | Cilicia |
| Epirus | the Aeacid kingdom | Epirus |
| Aetolia | the Aetolian League | Aetolia |
| Athens | the city, again independent of Demetrius | Attica |
| Rhodes | Rhodes | Rhodes, nearby coast |
| Island League | the Nesiotic island communities | Cyclades |
| Cappadocia | the local kingdom | Cappadocia |
| Bithynia and Pontus | the northern Anatolian dynasties | Bithynia, Pontus |
| Armenia | the highland rulers | Armenia |
| Atropatene | the northern Median kingdom | Atropatene |

**Uncertainty to label:** the post-Ipsus partition is only partly settled in 301 BC (Cilicia and Coele-Syria were disputed); the brief records disputed holdings as such, and the opening states one consistent snapshot rather than mixing later years.

## Cities, sources and evidence

- Cities follow the existing authoring pattern: a curated list (about 110 centres, regional centres allowed where documentation is thin), positions and dated evidence from the pinned Pleiades subset plus supplemental chronology, with owners and populations as **designed abstractions** separate from the cited location and date evidence. The existing `tools/NearEast/` sources and licence/attribution apply; the Hellenistic cities (Alexandria, Antioch, Seleucia, Pella, Pergamon and others) need their own Pleiades dated evidence, and **a city founded after the opening date (for example Antioch, 300 BC) is excluded or labelled as a regional centre**, never silently included.
- Secondary orientation (owner's list, v011 scope): [Met, art of the Hellenistic age](https://www.metmuseum.org/essays/art-of-the-hellenistic-age-and-the-hellenistic-tradition) and [Livius, Ipsus](https://www.livius.org/articles/battle/ipsus). Primary narrative sources to cite by title only: Diodorus Siculus, books 18-21 (the successor wars); Arrian, *Events after Alexander*. Specific claims are to be sourced one by one at authoring time, none are asserted here.
- Starting forces and economy are **designed**, scaled from represented population as the Near East scenarios do; no troop census, no precise-year border or treaty claim.

## Acceptance (this task, per the T293 contract)

Both campaigns selectable with guide entries (T290), valid deterministic data, a viable opening for every playable seat, several rounds of real AI play without stalls or rejections, a cross-range route check, save/load and copied-package launch, and a build script reproducing the data. Each year has its own world file (cities, owners, forces) on the shared revision-2 terrain sidecar, as the Near East pair does.

## Questions for the owner (asked before data is built)

1. The two seat lists above, including merges such as Epirus.
2. Default human seat for each opening.
3. Whether the 20-year horizon is right, or the longer 40 years that would let a player span the whole wars.
