# v0.11 campaign and map scope

Owner decisions, 2026-11-07: add eleven historical scenarios, optional locally supplied original artwork/audio, and a first random-map option. These extend the existing Improved-general and browser-view scope rather than replacing it. Tracking: [historical campaigns #1808](https://github.com/diegoami/imperial_conquest_2/issues/1808), [random maps #1809](https://github.com/diegoami/imperial_conquest_2/issues/1809), [original asset choice #1798](https://github.com/diegoami/imperial_conquest_2/issues/1798), [feasibility #1740](https://github.com/diegoami/imperial_conquest_2/issues/1740).

This records scope and proposals. Detailed scenario briefs and implementation contracts come before implementation; it does not settle unmeasured mechanics or political abstractions.

## Historical starts

The owner approved the proposed pairs in principle, then chose both the 431 BC and Pyrrhus starts. There are three Mediterranean campaigns, with two in each other group. The 431 BC opening must cover the whole Mediterranean, not just Greece.

| Group | Opening | Campaign identity |
| --- | --- | --- |
| Persia | c.550 BC | Rise of Cyrus: an emerging empire among competing regional powers. |
| Persia | 480 BC | Xerxes: an established empire and the Greek maritime/land coalition. |
| Mediterranean | 431 BC | Whole-Mediterranean power balance including Persia, Carthage, Syracuse, Greek leagues, Macedon, early Rome and other regional powers; the Peloponnesian War is one theatre. |
| Mediterranean | 280 BC | Pyrrhus entering Italy, with Rome, Carthage, Sicily and the contemporary Hellenistic powers across the whole Mediterranean. |
| Mediterranean | 218 BC | Hannibal and the opening of the Second Punic War, within the wider Mediterranean. |
| India | c.325 BC | Regional powers before Mauryan unification. |
| India | c.185 BC | Mauryan fragmentation and the later political map. |
| China | c.350 BC | A more open Warring States contest. |
| China | 230 BC | Qin's final unification campaigns. |
| Diadochi | 323 BC | Succession after Alexander. |
| Diadochi | 301 BC | The successor balance after Ipsus. |

The dates establish campaign identity, not proven city ownership, troop counts or diplomacy. In particular, 280 BC must use a consistent snapshot rather than combining the successor rulers of several nearby years. Early Diadochi satrapies and Indian regional boundaries require explicit sources, uncertainty and approved abstractions.

Each brief must specify map extent/scale, cities, factions/playable seats, ownership, period troop roles/art, starting diplomacy, forces/economy and their numerical abstractions, calendar/end year and victory policy. Do not inherit the classical 250 BC hard stop for these starts. Reuse geography only where extent and scale fit; overlapping eras must offer distinct campaigns. Source-backed claims and authored balance choices stay separate. Additional troop roles or new rules require explicit design decisions.

Acceptance requires eleven selectable campaigns with reproducible authoring, valid deterministic data, viable openings, several-round/AI checks, save/load and copied-package checks. All work with shipped authored artwork. Original artwork is optional, with fallback for regions/roles it does not cover.

Initial orientation, not the final scenario bibliographies:

- [Met: Achaemenid Persia](https://www.metmuseum.org/essays/the-achaemenid-persian-empire-550-330-b-c).
- [Met: Mauryan Empire](https://www.metmuseum.org/essays/mauryan-empire-ca-323-185-b-c).
- [UK Defence Academy: China as a military power](https://www.gov.uk/government/publications/china-as-a-military-power-to-1700/china-as-a-military-power-to-1700).
- [Met: Hellenistic age](https://www.metmuseum.org/essays/art-of-the-hellenistic-age-and-the-hellenistic-tradition), [Livius: Ipsus](https://www.livius.org/articles/battle/ipsus).
- [National Archaeological Museum: Classical period](https://www.namuseum.gr/en/collection/klasiki-periodos/), [Dickinson: Second Punic War](https://dcc.dickinson.edu/nepos-hannibal/second-punic-war), [Osprey: Pyrrhus](https://www.ospreypublishing.com/us/army-of-pyrrhus-of-epirus-9781472833488/).

## Campaign-selection background and advice

Owner addition, 2026-11-07 ([#1811](https://github.com/diegoami/imperial_conquest_2/issues/1811)): **all historical campaigns**, including existing shipped ones, show background and playable-power recommendations when selected in New Game. Use the description panel beside the compact campaign list. Summarize the period/situation and principal powers, then identify suggested playable choices with a short reason such as a more secure opening, naval focus, expansion opportunity or demanding position.

Historical claims require sources in maintained scenario briefs and explicit uncertainty/abstractions. Gameplay recommendations must refer to the actual starting setup and available playable IDs; historical success alone does not establish an easy or balanced campaign. Record play-validation evidence and label provisional advice where it remains untested. Use authored scenario metadata rather than screen-specific prose; validate coverage and stale/missing recommended IDs as rosters change.

Keep the panel concise, scrollable and keyboard accessible across themes/DPI, preserving Continue, default seats, nation selection and observer behavior. Actual selection must update the panel without changing campaign/RNG state. Fictional random maps may explain geography and strategic choices but receive no fabricated historical narrative. Toy/development scenarios do not need historical background.

## Fictional regional geography

The owner requests five regional composition types, not maps of the actual regions. These recipes are proposed ways to express that character with existing terrain. They do not imply new climate, altitude, weather or combat simulation.

| Type | Proposed composition | Campaign opportunities |
| --- | --- | --- |
| Mediterranean | An irregular inland sea, straits/outlets, peninsulas, islands, coastal plains, dry interiors and broken uplands. | Competing land/naval routes and several useful ports. |
| Middle East | Broad dry areas, a few fertile river corridors, uplands, passes and a gulf/coastal edge. | Settlement clusters connected through valleys and contested corridors. |
| Central/South Asia | Mountain belts, interior basins, long valleys, fertile foothills and drier plains. | Several connected pockets and meaningful passes rather than an impassable wall. |
| India | A broad fertile river plain, a mountain barrier, a drier plateau, wooded pockets and coastal lowlands. | Contrasting interior/coastal expansion routes; varied orientation and coastlines avoid a subcontinent copy. |
| China | Several large river basins, broad plains, wooded hills, a rugged frontier and a coast with bays. | Regional consolidation and routes between basins. |

Proposed first flow: **New Game → Random map → choose type/seed → generate three candidate previews → choose one → choose playable seats**. The owner requested a few generated maps; three previews is the builder's proposed starting limit. Sizes, faction counts, naming and detailed controls require a bounded contract. A world editor is outside this first option.

Before implementing, audit the engine's terrain/river encodings, limits, city/port rules, pathfinding and AI navigation. Original/classical navigation tables cannot simply be applied to arbitrary worlds; the supported AI policy needs an explicit decision. Define authored factions and naming rather than silently transplanting historical powers to fictional geography.

The same generator version/seed/type/parameters must reproduce world and starting placements without consuming campaign RNG. Saves must retain enough generated world/scenario data to survive generator upgrades or preview-cache eviction. Existing campaigns and save compatibility remain supported; the representation is an implementation decision requiring a contract.

Validate land/sea connectivity, navigable straits, reachable ports, legal settlements/army starts, nearby economic resources and expansion opportunities. Viability need not mean identical or symmetric starts. Reject/retry invalid outputs with a bounded attempt count and useful failure. River drawing must agree with supported crossings/connectivity. Check multiple seeds per type, reproducibility, disconnected/adversarial maps, generation time, AI play, save/load and packaging.

## Original-assets delivery and integration boundary

Conquest owns the extractor; Research owns its specification and independent review. Main owns the consumers, runtime packaging, cache/fallback and UI feasibility. Do not duplicate extraction work.

Reviewed source: [`scripts/extract_original_assets.py` at `1544d666c45004f1eda362cb84a4701e0e4143d2`](https://github.com/diegoami/ic2-conquest/blob/1544d666c45004f1eda362cb84a4701e0e4143d2/scripts/extract_original_assets.py). Expected SHA256: `5d266a075ce1cc75ccb359f2b4a64aa5bbf82e2dca031965c729e6abd53a97f5`. [Research acceptance PR220](https://github.com/diegoami/imperial-conquest-2-research/pull/220) accepts this exact static tool; review pin `71414df8c96b702a8d8bcb7a34bee6b864e4410d`. [Canonical specification](https://github.com/diegoami/imperial-conquest-2-research/blob/a0870dd26b86f74233334e7d9500309082403933/docs/reports/2026-11-07-original-assets218-extraction-spec.md).

Read Conquest's [integration guide](https://github.com/diegoami/ic2-conquest/blob/main/docs/original-assets-extractor.md) and [handoff](https://github.com/diegoami/ic2-conquest/blob/main/docs/notes-for-main-session-assets218.md). The tool requires Python 3.10+ standard library. Main must explicitly supply that runtime or port the tested contract into .NET; Godot's bundled runtime does not execute Python automatically. Confirm any changed runtime arrangement with Conquest. Retrieve the pinned development tests when validating integration.

Static extraction acceptance is not native composition or playable-consumer acceptance. Masks/AND-XOR/SRCERASE, nation recolouring, owner-specific city markers, six river shapes, scale and scenario gaps remain consumer responsibilities. Unknown/partial installations and absent sounds need clear diagnostics and authored fallback.

Original files and extracted media remain private and user-supplied. Only the tool may ship; no original media enters git, releases or Pages. This handoff authorizes no original-game runs, gameplay changes or complete original-interface reproduction. All these v0.11 features are outside v0.10.2.
