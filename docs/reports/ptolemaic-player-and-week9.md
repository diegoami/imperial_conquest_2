# Ptolemaic player addition and Week 9 Summer turn

This report reads three user-supplied saves—`11_supply.sav`, `11_ptol.sav`, and `12_ptol.sav`—and screenshots `11_supply.6.png`–`11_supply.8.png` plus `12_ptol.1.png` as data. The user states that the first pair adds a human Ptolemaic player and the second pair ends the Roman turn. The original game executable was not run for this analysis. The saves and screenshots remain outside Git.

| Save | Bytes | SHA-256 | Decoded trailer |
| --- | ---: | --- | --- |
| `11_supply.sav` | 129,421 | `7608ea62372f8dacba3af38698e7462b111b96e488062ab2c3479ca34128d6f0` | Week 7 Summer 270 BC; Rome active |
| `11_ptol.sav` | 129,421 | `185850942e71a713d6fbf9f785c3640ccc6507fb418e409b8ce34b038775ba86` | Week 7 Summer 270 BC; Rome active |
| `12_ptol.sav` | 129,421 | `72b49b0b74f95ca443e6f7a9339199bceaccb283d6c0adca4506e5014352a1e7` | Week 9 Summer 270 BC; Ptolemaic active |

## Nation table and the controlled player change

For these saves, the post-city army count is followed by 656-byte army records, a fleet count of two, two candidate 26-byte fleet records, then **16 nation records of 1,172 bytes** each. The first nation record begins at `0x1A434` and names occur every `0x494` bytes: Rome, Carthage, Seleucid, Ptolemaic, and so on through Thracia. Nation code equals record index. The 40 recruitment slots identified earlier are **embedded in each nation record** at `+0x2E4`, not a single global queue. The parser now reads those slots for all 16 nations.

| Nation record offset | Interpretation | Evidence and limit |
| ---: | --- | --- |
| `+0` | 11-byte name area | All 16 names match the ordered screenshot key |
| `+11` | 34-byte leader area | Rome `Licinus Crassus`; Carthage `Glaucus`, matching screenshots |
| `+0x2E4` | 40 × 8-byte recruitment slots | Rome's twelve entries match the earlier recruiting screenshots |
| `+0x438` | signed 32-bit treasury | Rome `−644` talents on nation panel |
| `+0x440` | candidate numeric unity | Rome `822` displays “very high”; Carthage `765` displays “high”; thresholds unknown |
| `+0x442` | mobilized percentage | Rome `65`, matching panel |
| `+0x444` | capital city index | Rome `85` → Rome; Carthage `72` → Carthago |
| `+0x446` | current city count | Rome `25`, Carthage `34`; matches panels and owner-count totals in all 16 nation records of these three saves |
| `+0x448` | candidate reference city count | Often equals current count; Ptolemaic is `45` current, `46` here |
| `+0x44A` | tax rate percentage | Rome `15`, Carthage `5`, matching panels |
| `+0x490` | human-player flag | Rome is `1`; Ptolemaic alone changes `0 → 1` when the user adds that player |

Adding the Ptolemaic player changes only **three bytes** in the entire save, with no map or city change. All three are in the Ptolemaic record: `+0x486` changes `45 → 90`, `+0x488` changes `55 → 186`, and `+0x490` changes `0 → 1`. The last field is strongly identified as the human-player flag by this controlled action and by Rome's existing value `1`. The meanings of the two other changed words remain unknown; they should not be treated as player flags or game rules yet.

The screenshots show Rome with **25 cities** and **2,535,000** population, Carthage with **34 cities** and **4,866,000** population. Summing the owned city population fields gives 845,000 and 1,622,000 respectively; multiplying by **three** reproduces both nation population displays exactly. This is a candidate aggregation rule, not yet proven for other nations or circumstances.

## Roman army information panel

Screenshot `11_supply.8.png` names the army at `(100, 42)` and shows **Moves 8**, **Supply 482 tons (100%)**, **Morale very high**, **Money 296 talents**, **Terrain River**, and **13 units / 48,173 troops**. Its record words at `+6`, `+8`, `+10`, and `+12` are `8`, `9`, `482`, and `296`: moves, candidate morale value, supply, and money. The initial DAT tile beneath the army is river shape `9`. Summing the decoded unit slots by type gives **9,710 light infantry**, **35,250 heavy infantry**, **0 archers**, **900 light cavalry**, and **2,313 heavy cavalry**, exactly the panel totals. The screen also shows regular cost **442 talents per quarter** and mercenary pay **0**; their storage or calculation is not yet identified. The label thresholds for morale and the supply percentage rule also remain unknown.

## Ending the Roman turn

`11_ptol.sav` → `12_ptol.sav` keeps the same length but changes **8 map cells**, **18 city records** (all at city supply word `+24`), and **2,547 post-city bytes** in 876 runs. Rome's city supply rises `1,731 → 1,810`, while the Roman army at `(100, 42)` keeps its 48,173 troops and its own supply falls `482 → 458`. These are observed changes, not yet a production or consumption formula. Several army and fleet markers move, and the news screenshot for Week 9 Summer reports Seleucid destroying a Bithynian army and Bithynia suing for peace, ending trade and alliances, and paying **334 talents** in reparations. Related strings are present in the new save.

In the 55-byte save trailer, relative word `+36` changes **0 → 3**, matching the active nation change from Rome to Ptolemaic; word `+40` changes **7 → 9**, matching the new Week 9 screenshot. The year `270` and season code `1` (Summer) stay fixed. Word `+38` also changes `7 → 2`, but its meaning is unknown. In the nation records, the first word of **593 of 640** recruitment slots advances by `+2`, including **495 empty slots**, strongly suggesting a general time-related counter rather than a direct troop-quality value. The other slots have state changes or recruitment activity that need a separate comparison.

These findings support reading nation details and current turn state from known saves. They do **not** yet establish the complete turn order, AI rules, diplomacy mechanics, or battle calculations.
