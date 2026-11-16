# India campaigns, c.325 and c.185 BC: draft brief for T293.4

**Status: draft brief for owner approval; no scenario data exists yet.** Tracks [#1840](https://github.com/diegoami/imperial_conquest_2/issues/1840), part of [#1808](https://github.com/diegoami/imperial_conquest_2/issues/1808). Contract: [T293](../tasks/T293.md). Openings fixed by [v011-campaigns-and-random-maps.md](v011-campaigns-and-random-maps.md#historical-starts): **c.325 BC** "regional powers before Mauryan unification"; **c.185 BC** "Mauryan fragmentation and the later political map". Dates establish campaign identity, not proven city ownership, troop counts or diplomacy. Owner decisions 2026-11-09: a **new map** (India has no existing geography — this task is also the geography-sourcing task), and **existing troop roles with authored names and art, labelled as abstractions; no new rules**. This brief proposes everything the owner must approve before data is authored; nothing here is a claim yet.

## Map and engine

- **A new authored map.** Proposed frame **60E-94E, 5N-37N at 8x10 tiles per degree (272x320)** — the revision-2 resolution family applied to the subcontinent. It covers the Indus and its five Punjab rivers, the Ganges-Yamuna plain to the delta, the Narmada, Tapi, Godavari, Krishna and Kaveri, the Vindhya-Satpura divide, the Deccan plateau, the Malabar and Coromandel coasts, Kalinga, the Tamil south to Kanyakumari, the Thar, the Himalayan foot and the Hindu Kush approaches (the Khyber and Bolan corridors). **Off-frame by design, never claimed by the data:** Sri Lanka (Tamraparni) beyond the Palk strait, Central Asia beyond the pass crests (Bactra itself sits off-frame; the 185 BC Indo-Greek seat's edge is the hills, as the Seleucid interior was in the Diadochi campaigns), the Tibetan plateau, the far eastern delta beyond the Brahmaputra mouths, and the Iranian plateau beyond the Bolan line.
- Geography from the pinned **Natural Earth v5.1.2** subsets (the Near East pipeline: public-domain vectors, clip script, recorded source hashes), clipped to the new frame — a fresh geography-sourcing step, byte-verified against the pinned snapshot's recorded hashes like every sibling campaign.
- **A shared authored terrain sidecar** for both openings; each opening carries its own **derived sidecar** for city tiles and landing aprons (the Persia/Mediterranean pattern, bounded diff).
- Relief per the [terrain-authoring rule](terrain-authoring.md): mountain only on the severe crests — the Himalaya front, the Karakoram/Hindu Kush edge, and the highest Western Ghats summits; forest as the lower-hill proxy across the Vindhya-Satpura, the Aravalli, the Siwaliks and both Ghats; desert across the Thar; and **deliberate plain passes** — the Khyber and Bolan corridors the northwestern approaches turn on, the Ganges doab route, the north-south Deccan crossings (Narmada gap and the Godavari/Penganga lines), and the coastal Malabar gap. Rivers from the same clipped vectors, navigable-water convention as in Italy and the Mediterranean.
- Existing mechanics only: the five unit roles with period labels (proposed: *Longbowmen* for archers — the Indian long bow is the period's famous arm — *Spearmen*, *Skirmishers*, *Light horse*, *Heavy horse*; final labels owner-approved), Italy-derived numeric rules unchanged, generic AI policy (no classical coordinate tables on the new map), **15 regular seats plus the barbarian arrival role** in every opening (frontier identities labelled per era: the hill and forest peoples at 325; the northwestern Saka/Parthian pressure at 185, labelled as the era's approaching forces, not attested invasions).
- Calendar and end: **20 years, total-conquest condition** (c.325 to 305 BC — the Mauryan unification happens inside the window; c.185 to 165 BC). No inherited 250 BC hard stop. Both ends are designed horizons: history is not forced to repeat.

## c.325 BC: proposed seats (15 regular)

The Nanda empire of Magadha is the east's great power; Alexander's withdrawal has just left the Indus under a provisional Macedonian administration with client kings between the rivers; the south is a set of regional powers the sources barely name.

| Seat | Represents | Principal region |
|---|---|---|
| Magadha | the Nanda empire (Dhana Nanda) | the middle Ganges, Pataliputra, the consolidated janapadas west to Kashi |
| Puru (Porus) | the restored kingdom between the Hydaspes and the Beas | the Punjab doab east of the Jhelum |
| Taxila | Ambhi's kingdom | Gandhara and the Indus confluence, Takshashila |
| Macedonian Indus command | the provisional satrapal administration (Peithon, Philip) | the lower Indus, the coast towns and the new foundations |
| Malloi-Madraka | the Punjab confederacy (gana-sangha) | the Ravi-Chenab interfluve |
| Abhisara | the hill kingdom met on the Jhelum | the Kashmir foothills |
| Malava | the Avanti-Malava region around Ujjain | western Madhya Pradesh |
| Vidarbha | the northern Deccan chiefdoms | Berar, the upper Wardha |
| Asmaka | the Godavari pair (Asmaka/Mulaka) | the Godavari around Paithan |
| Kuntala | the upper Krishna country | the Kanara-Deccan borderland |
| Andhra coast | the Krishna coastal chiefdoms | Dharanikota and the lower Krishna |
| Kalinga | the eastern coast kingdom | Tosali and Sisupalgarh |
| Saurashtra-Anarta | the coastal chiefs of the peninsula's west | Kathiawar, Girnar's country |
| Chola | the Kaveri kingdom | Uraiyur and Kaveripoompattinam |
| Pandya | the pearl-coast kingdom | Madurai and the Gulf coast |

**Uncertainty to label:** the Nanda consolidation's exact western and southern extent is a designed abstraction (Magadha's absorption of Kashi, Kosala and Vajji is older history; Avanti's annexation is a labelled choice — the seat exists as an abstraction). Porus' capital is unnamed in the sources — a regional centre stands in, owner-approved pattern. Musicanus' lower-Indus realm sits inside the Macedonian command's seat at this date (its rajas are reinstated 326-325), labelled. The Deccan's Vidarbha/Asmaka/Kuntala seats are regional abstractions at thin documentation — the owner-approved regional-centre pattern. The Tamil kingdoms' existence at 325 precedes their first firm evidence (Ashoka's edicts, about 250 BC) by 75 years — labelled as designed. The Macedonian command dissolved by about 316 BC; the campaign window keeps it for its whole 20 years, labelled. Possible replacements if a seat proves unplayable: Kuntala for Asmaka, the Sindhu commandos (the Sindhu gana of the sources) for Malloi-Madraka.

**Opening diplomacy (proposed):** war in progress — **Macedonian Indus command and Malloi-Madraka** (the punitive aftermath of the 326 BC campaign, labelled as designed); every other pair neutral. Default human seat (proposed): **Magadha** — the unifier's seat, the campaign's own engine of change.

## c.185 BC: proposed seats (15 regular)

The Mauryan empire has ended in a coup: Pushyamitra Sunga holds the old core, Indo-Greek kings press across the hills, and the south and Deccan are slipping into their own dynasties.

| Seat | Represents | Principal region |
|---|---|---|
| Sunga | Pushyamitra's realm | Pataliputra and the eastern-northern core |
| Indo-Greek kingdom | the Euthydemid command across the hills | the Indus towns, Sagala, Taxila; Bactra off-frame |
| Satavahana | the early Satavahana state | Pratishthana and the Deccan's core |
| Kalinga | the Mahameghavahana court | Tosali and the coastal towns |
| Vidarbha | the eastern Deccan kingdom | Berar (the campaign war's other side) |
| Malava | the Ujjain region under its own line | western Madhya Pradesh, Vidisha's neighbourhood |
| Panchala-Mitra | the Mitra houses of Panchala and Mathura | the upper Ganges doab |
| Audumbara | the Punjab hill republic | the Shiwalik edge |
| Kuninda | the Himalayan foothill kingdom | the upper Yamuna-Hindan country |
| Sibi-Madraka | the Punjab plains republics | the Sibi country of the upper Chenab |
| Saurashtra-Anarta | the peninsula's western coastal chiefs | Kathiawar and Girnar's country |
| Kuntala | the upper Krishna country | the Kanara-Deccan borderland |
| Chola | the Kaveri kingdom | Uraiyur and Kaveripoompattinam |
| Chera | the western-coast kingdom | Vanji/Karur and the Malabar coast |
| Pandya | the pearl-coast kingdom | Madurai and the Gulf coast |

**Uncertainty to label:** the Sunga-Indo-Greek line in the Punjab is a designed snapshot (the sources give the invasion, not the line); Demetrius' crossing is dated about 184-180 BC — the war is "in progress" by design, labelled. Kharavela's dates are debated; Kalinga's leader is the court, not a dated person. Vidarbha's ruling house is unnamed in the sources. The Satavahana northward extent is a design choice. The Tamil kingdoms' dynastic dating rests on later literature (the Sangam corpus) — labelled. Possible replacements if a seat proves unplayable: Asmaka for Kuntala, the Sindhu gana for Sibi-Madraka.

**Opening diplomacy (proposed):** war in progress — **Indo-Greek kingdom and Sunga** (the era's defining pressure, labelled as designed); every other pair neutral. Default human seat (proposed): **Sunga** — the usurper's realm defending the core, with the Indo-Greek kingdom as the strong alternative.

## Cities, sources and evidence

- Cities follow the existing authoring pattern: a curated list (about 130 centres, regional centres where documentation is thin), positions and dated evidence from the pinned **Pleiades** archive snapshot (global coverage; a new curated subset extracted through the existing pipeline and diffed against the committed subsets), owners and populations as **designed abstractions**. Anchor centres: Takshashila/Taxila, Pushkalavati, Sakala, Pataliputra, Kaushambi, Varanasi, Mathura, Ujjain, Vidisha, Rajagriha, Champa, Tamralipti, Tosali/Sisupalgarh, Dharanikota/Amaravati, Pratisthana, Nasik, Tagara, Sopara, Bharukachchha, Uraiyur, Karur, Madurai — with the founding-date review at authoring time (the new Alexandrian foundations exist at 325 by construction; Hellenistic-era names labelled; Arikamedu's occupation bounds checked at 185; the Sangam ports' occupation labelled).
- Secondary orientation: the [Met's Mauryan Empire essay](https://www.metmuseum.org/essays/mauryan-empire-ca-323-185-b-c) (already cited in the v0.11 design), and Livius's overviews (Porus, Taxila, Mauryan empire). Primary sources by title only: Megasthenes' *Indica* (in fragments, dated context about 300 BC), Arrian's *Anabasis* and *Indica*, Pliny's *Natural History* (Indian geography books), the edicts of Ashoka (as era context for 185's antecedents), the Periplus of the Erythraean Sea (first-century AD port context only, labelled), the Sangam corpus (later literature, labelled). Specific claims are sourced one by one at authoring time, none asserted here.
- Starting forces and economy are **designed**, scaled from represented population as the Near East, Diadochi, Persia and Mediterranean scenarios do; no troop census, no precise-year border or treaty claim.

## Acceptance (this task, per the T293 contract)

Both India openings selectable with guide entries (T290), valid deterministic data, a viable opening for every playable seat, several rounds of real AI play without stalls or rejections on the larger map, a cross-range route check (the Khyber and Bolan corridors, the Vindhya/Ganges routes, the Deccan north-south crossings), save/load and copied-package launch, and a build script reproducing the data. The two openings share the authored sidecar and each carries a derived sidecar diff test (the Persia bound). `IndiaCampaignTests` mirror the Persia suite for the two years; the listed-cards test and the map-fit checks gain the new 272x320 entry.

## Owner decisions (pending)

1. **Map frame and scale**: 60E-94E, 5N-37N at 8x10 tiles per degree (272x320) — approve as drafted, or adjust the frame.
2. **Both seat lists** (c.325 and c.185) as drafted, with the labelled uncertainties.
3. **Default human seats**: Magadha at c.325; Sunga at c.185.
4. **Opening diplomacy**: Macedonian Indus command-Malloi-Madraka at war at c.325; Indo-Greek kingdom-Sunga at war at c.185; every other pair neutral.
5. **Horizons**: 20 years each (c.325 to 305; c.185 to 165).
6. **Troop labels**: Longbowmen/Spearmen/Skirmishers/Light horse/Heavy horse as proposed, or the neutral Persia labels.
