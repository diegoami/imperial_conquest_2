# China campaigns, c.350 and 230 BC: draft brief for T293.5

**Status: brief approved by the owner 2026-11-17; authoring follows on the T293.5 branch (the India pipeline: build script, worlds, rulesets, scenarios, guides, engine and Godot checks).** Tracks [#1841](https://github.com/diegoami/imperial_conquest_2/issues/1841), part of [#1808](https://github.com/diegoami/imperial_conquest_2/issues/1808). Contract: [T293](../tasks/T293.md). Openings fixed by [v011-campaigns-and-random-maps.md](v011-campaigns-and-random-maps.md#historical-starts): **c.350 BC** "a more open Warring States contest"; **230 BC** "Qin's final unification campaigns". Dates establish campaign identity, not proven city ownership, troop counts or diplomacy. Owner decisions 2026-11-09: a **new map** (this task is also the geography-sourcing task), and **existing troop roles with authored names and art, labelled as abstractions; no new rules** — the same troop-role decision India took.

## Map and engine

- **A new authored map.** Proposed frame **98E-124E, 22N-44N at 8x10 tiles per degree (208x220)** — the revision-2 resolution family applied to the Warring States world. It covers the Yellow River's Ordos bend and the Wei-Fen loess valleys around Guanzhong, the Taihang edge and the North China plain, the Shandong hills and coast, the Huai and Han river systems, the middle and lower Yangtze, the Sichuan basin (Shu and Ba) and the Qinling-Daba divide, the Liaodong approaches, and the northern steppe frontier. **Off-frame by design, never claimed by the data:** Lingnan and the Pearl basin beyond the Nan Ling crest (Qin's historical southern campaigns fall after this frame's horizons; the Nan Ling are the southern edge, as the Hindu Kush was India's northwestern edge), the Korean peninsula beyond Liaodong, the deep steppe beyond the frontier line, the Tibetan highlands beyond the upper Yangtze, and the Gansu corridor's far west. Taiwan's coast stays inside the rectangular frame as empty land — no seat, city or claim, as Central Asia did for India.
- Geography from the pinned **Natural Earth v5.1.2** subsets (the Near East pipeline: public-domain vectors, clip script, recorded source hashes), clipped to the new frame — a fresh geography-sourcing step, byte-verified against the pinned snapshot's recorded hashes like every sibling campaign.
- **A shared authored terrain sidecar** for both openings; each opening carries its own **derived sidecar** for city tiles and landing aprons (the Persia/Mediterranean pattern, bounded diff).
- Relief per the [terrain-authoring rule](terrain-authoring.md): mountain only on the severe crests — the Qinling-Daba front between Guanzhong and Shu, the Taihang wall between the loess and the plain, the highest Nan Ling and Liaodong summits; forest as the lower-hill proxy across the Shandong hills, the Daba, the southern hill belts and the loess margins; desert across the Ordos and the arid northwest; and **deliberate plain passes** — the Hangu/Xiao corridor on the Guanzhong-central-plain axis, Wu Pass down the Dan river into Chu, the middle Yellow River fords, Jingxing and the other Taihang crossings, the Daba crossings into Shu (the later Jinniu road), the Yangtze gorges into Ba, the Ming'e/Fangcheng lines into Chu's north, and the Huai corridor between Qi and Chu. The *Lüshi chunqiu* (239 BC) names nine of these fortresses (the *sai*), among them Yao/Xiao, Jingxing and Juyong — period evidence for the pass plan. Rivers from the same clipped vectors, navigable-water convention as in Italy and the Mediterranean.
- Existing mechanics only: the five unit roles with period labels (proposed: *Crossbowmen* for archers — the crossbow is the period's signature arm — *Halberdiers* for dagger-axe infantry, *Skirmishers*, *Light horse*, *Chariots* on the heavy-horse role; final labels owner-approved), Italy-derived numeric rules unchanged, generic AI policy (no classical coordinate tables on the new map), and the seat rosters below (**15 regular at c.350; 11 at 230 — a deliberate, labelled reduction**, see the roster section) plus the barbarian arrival role in every opening.
- Calendar and end: **20 years, total-conquest condition** (c.350 to 330 BC; 230 to 210 BC). The historical unification completes in 221, inside the second window, and Modu's steppe unification in 209 falls near its end — both are horizons, not scripts; history is not forced to repeat. No inherited 250 BC hard stop.

## c.350 BC: proposed seats (15 regular)

The old Zhou order is a memory with a royal court still at Luoyang; Wei's hegemony has just been broken at Guiling, and Qin's Shang Yang reforms are under way: an open contest.

| Seat | Represents | Principal region |
|---|---|---|
| Wei | the Jin successor of the deposed hegemon | the Yellow River bend at Anyi and the central plain's western marches |
| Qin | the reforming western state (Xiao gong, Shang Yang) | Guanzhong and Longxi |
| Chu | the great southern kingdom | the middle Yangtze, the Han and the Huai |
| Qi | the eastern maritime state | the Shandong plain around Linzi |
| Zhao | the northern Jin successor | Handan, Taiyuan and the Dai frontier |
| Yan | the northeastern state | the Ji plain and the Liaodong approaches |
| Han | the southern Jin successor | Xinzheng and the Yingchuan corridor |
| Zhou royal domain | the royal court and its split halves | the Yi-Luo basin around Luoyang |
| Song | the merchant state of the central plain | Shangqiu and the lower Si-Huan basin |
| Zhongshan | the restored Di state within the Jin lands | the eastern Taihang margin around Lingshou |
| Lu | the old ritual duchy | the Si river around Qufu |
| Wey | the ancient margraviate (Wei of Puyang, distinct from Wei of Liang) | the lower Yellow River around Puyang |
| Yue | the southeastern coastal remnant after Chu's thrust | the Qiantang and Hangzhou bay coast |
| Shu | the Sichuan basin kingdom | the Chengdu plain |
| Ba | the eastern Sichuan kingdom | Jiangzhou and the gorge country |

**Uncertainty to label:** Wei's capital at 350 sits at Anyi or is already moving to Daliang — sources differ; the anchor centre is labelled. The Yue court's fate after Chu's thrust (traditionally 333 BC) is contested; the seat is a coastal abstraction. Zhou's East/West split folds into one royal-domain seat, labelled. Shu and Ba's dynasties rest on late, thin sources — the owner-approved regional-centre pattern. Song's pre-286 extent and Zhongshan's territory (partly archaeological, the Lingshou tombs) are designed snapshots. Possible replacements if a seat proves unplayable: Yiqu for Yue; Xue for Lu.

**Opening diplomacy (proposed):** war in progress — **Qin and Wei** (the western front against the reforms, labelled as designed); every other pair neutral. Default human seat (proposed): **Wei** — the deposed hegemon defending a contested position — with Qi as the strong alternative.

## 230 BC: proposed seats (11 regular — a deliberate, labelled reduction)

Qin holds Shu-Ba, has ended the Zhou (249) and broken the coalition era; the endgame opens against Han. The historical roster is genuinely short, so the brief accepts fewer seats rather than inventing states.

| Seat | Represents | Principal region |
|---|---|---|
| Qin | the unification state under the young Zheng | Guanzhong, Longxi, Shu and Ba |
| Han | the first target | Xinzheng and the Yingchuan corridor |
| Zhao | the northern power | Handan, Taiyuan and the Dai frontier |
| Wei | the central-plain remnant | Daliang and the He-nei marches |
| Chu | the great southern kingdom | the middle-lower Yangtze and the Huai, court at Shouchun since Ying fell in 278 |
| Qi | the eastern state | the Shandong plain around Linzi |
| Yan | the northeastern state | Ji and Liaodong |
| Wey | the nominal margraviate under Qin's hand | Puyang, a title seat — kept only if it proves playable |
| Xiongnu | the rising steppe confederation (Touman's) | the Ordos edge and the steppe beyond |
| Donghu | the eastern steppe power | beyond Yan's north |
| Yuezhi | the western steppe power | the Hexi corridor's eastern approaches, at the frame edge |

**Uncertainty to label:** the steppe tripartition rests on the *Shiji*'s retrospective account of Modu's rise (written about a century later) — the three seats are edge abstractions, labelled. Modu's unification (209) and the Donghu's destruction fall inside the window as horizons, not events. Wey at 230 is a title without substance; the labelled alternative is the Loufan. Qi's decades of armed neutrality are the era's documented posture, offered as an opening stance, never as a rule. Yan's Jing Ke affair (227) is history, not a scripted event. Possible replacements if a seat proves unplayable: Loufan for Wey; the Lin-Hu for Donghu.

**Opening diplomacy (proposed):** war in progress — **Qin and Han** (the endgame's first act); every other pair neutral. Default human seat (proposed): **Qin** — the unifier's seat, the campaign's engine of change — with Chu as the strong alternative.

## Cities, sources and evidence

- Cities follow the existing authoring pattern: a curated list (about 130 centres, regional centres where documentation is thin). The Pleiades archive's East Asia coverage is sparse, so — as for South Asia in India — centres without an archive record become **author geographic hints** (the Madaktu pattern: regional=true, coordinates from the curated targets, dated context from a chronology table, each labelled "author-interpreted contextual range"); owners and populations are **designed abstractions**. Anchor centres: Xianyang, Yong, Daliang, Anyi, Handan, Jinyang, Xinzheng, Linzi, Ji, Luoyang, Shangqiu, Qufu, Puyang, Lingshou, Kuaiji, Chengdu, Jiangzhou, Ying, Chen, Shouchun, and the pass gate towns (Hangu, Wu Pass, Jingxing, Juyong). Founding and occupation bounds reviewed at authoring time, the India pattern.
- Secondary orientation: the [UK Defence Academy: China as a military power](https://www.gov.uk/government/publications/china-as-a-military-power-to-1700/china-as-a-military-power-to-1700) (already cited in the v0.11 design) and Livius's overviews. Primary sources by title only: the *Shiji* (Sima Qian, written about 100 BC — retrospective for both openings, labelled as such), the *Zhanguo Ce* (a Han compilation), the *Lüshi chunqiu* (239 BC, inside the second opening's window — the nine *sai*), and the *Huayang Guo Zhi* (a much later Jin source for Shu and Ba, labelled). Specific claims are sourced one by one at authoring time, none asserted here.
- Starting forces and economy are **designed**, scaled from represented population as the Near East, Diadochi, Persia, Mediterranean and India scenarios do; no troop census, no precise-year border or treaty claim.

## Acceptance (this task, per the T293 contract)

Both China openings selectable with guide entries (T290), valid deterministic data, a viable opening for every playable seat, several rounds of real AI play without stalls or rejections on the larger map, a cross-range route check (the Hangu/Xiao corridor, Wu Pass, the middle Yellow River fords, Jingxing and the Taihang crossings, the Daba crossings and the gorges, the Ming'e/Fangcheng lines, the Huai corridor), save/load and copied-package launch, and a build script reproducing the data. The two openings share the authored sidecar and each carries a derived sidecar diff test (the Persia bound). `ChinaCampaignTests` mirror the India suite for the two years; the listed-cards test and the map-fit checks gain the new 208x220 entry.

## Owner decisions, 2026-11-17 (approved as drafted)

1. **Map frame and scale**: 98E-124E, 22N-44N at 8x10 tiles per degree (208x220), with Lingnan, the Korean peninsula, the deep steppe and the Gansu corridor's far west off-frame — approved.
2. **Both seat lists** as drafted — 15 regular at c.350, and the deliberate labelled reduction to 11 regular at 230 (the seven states, Wey and the three steppe powers) — approved.
3. **Default human seats**: Wei at c.350; Qin at 230 — approved.
4. **Opening diplomacy**: Qin-Wei at war at c.350; Qin-Han at war at 230; every other pair neutral — approved.
5. **Horizons**: 20 years each (c.350 to 330; 230 to 210) — approved.
6. **Troop labels**: Crossbowmen/Halberdiers/Skirmishers/Light horse/Chariots — approved.
