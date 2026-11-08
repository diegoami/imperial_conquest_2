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

## Authoring and future coverage

Each guide uses schema version 1: stable guide ID; unique scenario IDs; `historical` or `generated` kind; period/geography caption; background; abstractions; repository brief; validation limits; powers (`nationId`, focus, advice, suggested, optional `requiresStartingFleet`); source title/HTTPS URL pairs. Plain text is English; no markup or translated-resource system is introduced. Keep paragraphs under 900 characters and advice under 600. Localization can replace strings without changing nation/scenario identities.

`CampaignGuideCatalog` rejects invalid playable IDs, duplicate mappings and false fleet assertions. `CampaignGuideTests` requires coverage for every shipped chooser entry and reachable maintained briefs. New historical campaigns must add their sourced setting and provisional or evidenced advice. Fictional generated presets must explain geography and carry no fabricated history; toy/development maps may omit guidance. The selection UI reads data only and never creates a campaign or consumes campaign RNG to show advice.
