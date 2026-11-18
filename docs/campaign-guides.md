# Historical campaign selection briefs

The player-facing background and advice live in `data/campaign-guides/*.json`. These are presentation metadata, separate from scenario rules, starting state and saves. Historical context describes a setting; suggested powers are authored gameplay advice. The panel displays actual opening city/army/fleet counts from the selected world. Neither historical success nor city count establishes an easy or balanced start.

All initial recommendations are **provisional**. Validation covers shipped playable seats, opening holdings and fleet availability, not a completed human campaign or measured difficulty. The full Classical observer recording is AI-play evidence, not human difficulty evidence. Historical borders, diplomacy and chronology must not be inferred from gameplay results.

## `classical`

Applies to `classical-mediterranean` and `example-classical-improved`. The setting is the western Mediterranean and Hellenistic kingdoms around 270 BC. Successor dynasties and smaller eastern kingdoms supply context; their precise political geography is compressed by the inherited sixteen-seat opening. The faithful/improved rules remain separately described.

Historical basis: [Metropolitan Museum, Hellenistic Age](https://www.metmuseum.org/essays/art-of-the-hellenistic-age-and-the-hellenistic-tradition); [Livy's surviving Pyrrhus summaries](https://www.livius.org/sources/content/livy/livy-on-pyrrhus/) give the immediately preceding Italian context, with the limits of a later literary tradition. These sources do not authenticate the game's exact owners, units or leaders.

Suggested openings: Rome for land operations; Carthage or the Ptolemaic realm for a starting fleet and land/sea choices. Seleucid and Macedonian entries explain other major land positions. World inspection confirms Rome/Seleucid have no starting fleet; Carthage/Ptolemaic do. No claim of beginner safety or win probability follows.

## `italy`

Applies to `italy-500bc` and `italy-500bc-barbarians`. Central Etruscan cities and Greek settlements in southern Italy/Sicily belong to the broad early-fifth-century setting. See [Met, Etruscan Art](https://www.metmuseum.org/essays/etruscan-art) and [Italian Peninsula chronology](https://artistproject.metmuseum.org/toah/ht/04/eust.html), plus the per-entry literary citations in `tools/Italy/build_italy.py`.

The authoring aggregates distinct cities/peoples into twelve regular seats. Its northern peoples and several territorial assignments are deliberate chronology/political compression, not exact-year state borders. The barbarian variant adds a non-playable designed roaming role. Opening populations, forces and money are designed quantities.

Suggested openings: Rome for central land operations, Syracuse or Carthage for fleets and island connections. Etruria and the Samnites explain broader central holdings and the interior alternative. Inspect both variant files when changing a guide; shared geography does not make the roaming-role rules identical.

## `near-east-1350bc`

Applies to `near-east-1350bc`. [UCL's Amarna inventory](https://www.ucl.ac.uk/museums-static/digitalegypt/amarna/cuneiform2.html) documents correspondence among Egypt and the other major courts, with vassal correspondence in Syria-Palestine. [British Museum EA 23](https://www.britishmuseum.org/collection/object/W_1888-1013-78) anchors the approximate period. Correspondence is evidence of relationships, not exact borders or equivalent armies.

The detailed [Near East brief](design/near-east-campaigns.md) and [authoring sources](../tools/NearEast/README.md) explain approximate chronology, archaeological centers and political groupings. In particular a Mycenaean seat aggregates palace communities. All opening forces/economics are designed.

Suggested openings: Egypt for a southern river base and fleet, Hatti for Anatolian land operations, Alashiya for a smaller naval base. Mitanni and Babylonia explain central and southern Mesopotamian alternatives. Every regular seat has one opening army. No route or difficulty certification is implied, particularly while the terrain-pass correction remains separately tracked.

## `near-east-750bc`

Applies to `near-east-750bc`. [ORACC countries and peoples](https://oracc.museum.upenn.edu/saao/aebp/essentials/countries/index.html) supplies the Neo-Assyrian/Urartian regional setting; [Tabal and Phrygia](https://oracc.museum.upenn.edu/saao/aebp/Essentials/Countries/TabalandPhrygia/index.html) explains political fragmentation. The earlier Hittite empire and later Assyrian conquests are not the opening. The [existing Near East brief](design/near-east-campaigns.md) documents Israel/Judah separation and the Median/community abstractions.

Suggested openings: Assyria for land operations, Phoenicia for a fleet/coastal opening, Egypt for a southern land base. Urartu and Judah explain smaller regional alternatives. Egypt has no starting fleet in this period. Coast, route and political-grouping advice is provisional, with no human balance or crossing playtest claim.

## `diadochi-323bc`

Applies to `diadochi-323bc`. The setting is the settlement after Alexander's death at Babylon and the Greek revolt that followed; see [Met, art of the Hellenistic age](https://www.metmuseum.org/essays/art-of-the-hellenistic-age-and-the-hellenistic-tradition). The [Diadochi brief](design/diadochi-campaigns.md) records the grouped seats, the dated evidence and the labelled uncertainty. Opening forces and populations are designed.

Suggested openings: Egypt (the default seat, with a fleet), Macedon for a land campaign, Phrygia for an inland base. Royal army and Rhodes explain other positions. No difficulty or route certification is implied.

## `diadochi-301bc`

Applies to `diadochi-301bc`. The setting is the balance after the battle of Ipsus; see [Livius, Ipsus](https://www.livius.org/articles/battle/ipsus). Disputed holdings (Coele-Syria, the southern Anatolian coast, Cilicia) are assigned one holder each as a design choice, as the [Diadochi brief](design/diadochi-campaigns.md) records.

Suggested openings: Seleucid kingdom, Egypt (the default seat) and Thrace and Anatolia. Macedon and Demetrius explain a European base and a fleet-centred position. No difficulty or route certification is implied.

## `persia-550bc`

Applies to `persia-550bc`. The setting is the war of Astyages and Cyrus, with Lydia, Babylon and Saite Egypt as the other established powers; see [Met, The Achaemenid Persian Empire](https://www.metmuseum.org/essays/the-achaemenid-persian-empire-550-330-b-c). Susiana's allegiance is a labelled regional abstraction, and Cyrus's eastern conquests are off the map, as the [Persia brief](design/persia-campaigns.md) records.

Suggested openings: Persis (the default seat, the campaign's challenger), Media for the established empire, Lydia for western wealth and a fleet. Babylon and Sparta explain other positions. No difficulty or route certification is implied.

## `persia-480bc`

Applies to `persia-480bc`. The setting is Xerxes' invasion: the empire and its satrapy seats against the resisting coalition, with the medizing seats' allegiance labelled as a designed snapshot; see [Livius, Xerxes](https://www.livius.org/articles/people/xerxes/). The [Persia brief](design/persia-campaigns.md) records the grouped seats and the dated evidence.

Suggested openings: Athens (the default seat, the fleet and the evacuated city) and Sparta for the coalition, Caria for a fleet-centred position on the empire's side, Egypt for a distant quiet game. No difficulty or route certification is implied.

## `mediterranean-431bc`

Applies to `mediterranean-431bc`. The setting is the Peloponnesian War already in progress between Athens' Delian empire and Sparta's league, with Persia's imperial west, Carthage, Syracuse, Macedon and a one-city Rome as the sea's other powers; see [Livius, Peloponnesian War](https://www.livius.org/articles/war/peloponnesian-war/). The [Mediterranean brief](design/mediterranean-campaigns.md) records the grouped seats, the labelled Persian satrapy west (Egypt inside it) and the off-frame interior.

Suggested openings: Athens (the default seat, the largest fleet and the tribute empire) and Sparta for the land league, Persia for the most centres, Carthage for the western sea, Rome as a deliberate one-city challenge. No difficulty or route certification is implied.

## `mediterranean-280bc`

Applies to `mediterranean-280bc`. The setting is one consistent snapshot, spring 280 BC: Pyrrhus answering Tarentum's call and Rome meeting an invasion from Epirus, with Carthage, the Sicilian towns and the Hellenistic east across the sea; see [Livius, Pyrrhus](https://www.livius.org/articles/people/pyrrhus/). Macedon's allegiance is a labelled snapshot choice between claimants, Thrace has no seat, and the Gauls are the off-map arrival role, as the [Mediterranean brief](design/mediterranean-campaigns.md) records.

Suggested openings: Epirus (the default seat, Pyrrhus' war with Rome), Rome for the republic's manpower, Carthage for the patient maritime game, Egypt and the Seleucid kingdom for the eastern powers. No difficulty or route certification is implied.

## `mediterranean-218bc`

Applies to `mediterranean-218bc`. The setting is the opening of the Second Punic War: Hannibal has taken Saguntum and the war opens in Iberia, with Hiero II's Syracuse at peace with Rome and Philip V neutral (his pact with Hannibal came in 215); see [Livius, Second Punic War](https://www.livius.org/articles/war/second-punic-war/). The Numidian seat abstracts both royal houses, and the Roman colonies founded in 218 are excluded, as the [Mediterranean brief](design/mediterranean-campaigns.md) records.

Suggested openings: Carthage (the default seat, Hannibal's war), Rome for the alliance towns and the long game, Syracuse for the watcher's seat, Macedon and Egypt for the neutral and eastern positions. No difficulty or route certification is implied.

## `india-325bc`

Applies to `india-325bc`. The setting is the subcontinent before the Mauryan unification: the Nanda empire of Magadha in the Ganges plain, the Punjab kings and the provisional Macedonian Indus command in the northwest, and the Deccan and Tamil regional powers; see [Livius, Porus](https://www.livius.org/articles/people/porus/). The [India brief](design/india-campaigns.md) records the grouped seats, the Deccan and Tamil abstractions and the off-frame edges.

Suggested openings: Magadha (the default seat, the Ganges plain and the unification ahead), Puru for the Punjab, the Macedonian Indus command for the outsider's war, Taxila and Pandya for the quiet corners. No difficulty or route certification is implied.

## `india-185bc`

Applies to `india-185bc`. The setting is the Mauryan fall: Pushyamitra Sunga's coup realm at Pataliputra against the Indo-Greek command across the northwestern hills, with the early Satavahanas in the Deccan, Kalinga's coastal court and the Tamil kingdoms in the south; see [the Met's Mauryan essay](https://www.metmuseum.org/essays/mauryan-empire-ca-323-185-b-c). The [India brief](design/india-campaigns.md) records the hill-republic seats and the debated chronologies, all labelled.

Suggested openings: Sunga (the default seat, the usurper's core), the Indo-Greek kingdom for the invader's war, the Satavahanas for the Deccan builder's game, Kalinga and Pandya for the coastal courts. No difficulty or route certification is implied.

## `china-350bc`

Applies to `china-350bc`. The setting is the open Warring States contest: Wei's hegemony broken at Guiling, Qin's Shang Yang reforms beginning behind the Hangu corridor, Chu across the middle Yangtze, Qi on the eastern coast, and the Zhou court's memory at Luoyang; see [the UK Defence Academy's China overview](https://www.gov.uk/government/publications/china-as-a-military-power-to-1700/china-as-a-military-power-to-1700). The [China brief](design/china-campaigns.md) records the fifteen grouped seats, the Wey/Wei distinction, the Yue remnant and the Shu-Ba abstractions.

Suggested openings: Wei (the default seat, the deposed hegemon's contested position), Qi for the eastern maritime game, Qin for the reformer's long game, Chu for the giant's broad fronts, Zhongshan for the wedged-in challenge. No difficulty or route certification is implied.

## `china-230bc`

Applies to `china-230bc`. The setting is Qin's endgame: the young King Zheng holding half the represented world from Xianyang, Han the first target at Xinzheng, Zhao fighting from Handan and the Dai frontier, Chu withdrawn to Shouchun, Qi watching from Shandong, and the Xiongnu-Donghu-Yuezhi tripartition beyond the passes; see [Wikipedia's unification wars overview](https://en.wikipedia.org/wiki/Qin%27s_wars_of_unification). The [China brief](design/china-campaigns.md) records the deliberate eleven-seat roster, the steppe edge abstractions and the labelled horizons.

Suggested openings: Qin (the default seat, the unification's engine), Chu for the southern defence, Zhao for the stubborn north, Qi for the neutral giant, Han for the authentic desperate seat. No difficulty or route certification is implied.

## Authoring and future coverage

Each guide uses schema version 1: stable guide ID; unique scenario IDs; `historical` or `generated` kind; period/geography caption; background; abstractions; repository brief; validation limits; powers (`nationId`, focus, advice, suggested, optional `requiresStartingFleet`); source title/HTTPS URL pairs. Plain text is English; no markup or translated-resource system is introduced. Keep paragraphs under 900 characters and advice under 600. Localization can replace strings without changing nation/scenario identities.

`CampaignGuideCatalog` rejects invalid playable IDs, duplicate mappings and false fleet assertions. `CampaignGuideTests` requires coverage for every shipped chooser entry and reachable maintained briefs. New historical campaigns must add their sourced setting and provisional or evidenced advice. Fictional generated presets must explain geography and carry no fabricated history; toy/development maps may omit guidance. The selection UI reads data only and never creates a campaign or consumes campaign RNG to show advice.
