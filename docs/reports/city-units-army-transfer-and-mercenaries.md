# City-unit totals, Ptolemaic movement, and the Roman 15,000-soldier transfer

This comparison uses only `12_ptol.sav`, `12_ptol_b.sav`, and `12_rom_a.sav`, plus screenshots `12_ptol_2.png`, `12_ptol_3.png`, and `12_rom_1.png`–`12_rom_3.png`. The user described `12_ptol.sav` → `12_ptol_b.sav` as moving two Ptolemaic armies toward Masada and Alexandria, and the next pair as transferring 15,000 soldiers from Rome to its army. The supplied post-transfer file is named **`12_rom_a.sav`** on disk; no `12_rom.sav` was present. These are read-only observations. The original EXE was not run, and the saves/screenshots remain outside Git.

| Save | Size | SHA-256 |
| --- | ---: | --- |
| `12_ptol.sav` | 129,421 | `72b49b0b74f95ca443e6f7a9339199bceaccb283d6c0adca4506e5014352a1e7` |
| `12_ptol_b.sav` | 129,421 | `da0d1e029684a776a484a4575977c75f3c1a8809d468bd88350b124eff4264ff` |
| `12_rom_a.sav` | 129,421 | `b58e551487833a6b0fadf25d0b9be0b461299662b10c37090748b9791813310d` |

## The number beside fortification counts units at the city

The parenthetical number in a city information panel is the sum of the troop quantities in that city's **Units at** list. It is separate from both the fortification percentage and city population. The units are held in 40 eight-byte slots within the owning nation's record and shown in the game's Army recruits dialog. They include entries labelled “not ready,” so the total should not be described as only ready or mobilized soldiers.

| Save and city | Screenshot fortification line | Sum of saved city-unit slots | Detail |
| --- | --- | ---: | --- |
| `12_ptol_b.sav`, Masada | `98% (49,800)` | 49,800 | Six listed units: 10,700 + 15,000 + 5,500 + 5,500 + 6,700 + 6,400 |
| `12_ptol_b.sav`, Rome | Earlier screenshot `78% (85,000)` | 85,000 | Twelve city-unit slots; Rome's 34-byte city record has no literal 85,000 field |
| `12_rom_a.sav`, Rome | `78% (70,000)` | 70,000 | Eleven slots remain after one 15,000 light-infantry unit leaves |

Masada is an independent check: its population is only **10,000**, while its six city units total **49,800**. The `98%` fortification and `(49,800)` troop total therefore cannot be a percentage-and-base pair. Rome's fortification stays **78%** and population stays **181,000** through the transfer while the parenthetical count falls by exactly 15,000. The viewer and inspector now calculate the city-unit total directly from the nation slots, and label the list **Units at city** rather than treating every entry as still recruiting.

## Moving the two Ptolemaic armies

`12_ptol.sav` → `12_ptol_b.sav` changes **17 bytes**: four map cells, two Ptolemaic army headers, and six bytes in Ptolemaic nation state. No city record changes. The two aligned 656-byte army records show:

| Army | Before → after position | Moves before → after | Other header change |
| --- | --- | ---: | --- |
| Near Masada | `(225, 91)` → `(224, 89)` | `7 → 5` | Candidate morale value `3 → 2` |
| Near Alexandria | `(192, 96)` → `(191, 95)` | `10 → 9` | None observed |

The cities are at Masada `(223, 88)` and Alexandria `(190, 94)`. This controlled movement supports the existing coordinate and moves-field interpretations. Why the first army's candidate morale value changed is not established.

## Moving 15,000 soldiers from Rome into its army

In `12_ptol_b.sav` → `12_rom_a.sav`, Rome's first two city-unit slots each contain **15,000 light infantry**. In the later save one such slot is removed, eleven slots remain, and the city-unit sum changes **85,000 → 70,000**. The Roman army at `(100, 42)` gains a new unit in slot 13: **3rd Foot Battalion**, light infantry, **15,000** troops, quality code `5`. Its total becomes **14 units / 63,173 troops**, from **13 / 48,173**, matching `12_rom_2.png`. Light infantry rises **9,710 → 24,710**; heavy infantry **35,250**, light cavalry **900**, and heavy cavalry **2,313** remain unchanged. Its supply **458 tons**, money **296**, and moves **8** remain unchanged. Rome's 34-byte city record also remains byte-identical, including fortification `78`, population `181`, and supplies `1,810`.

The original Rome city-unit screenshot labels the 15,000 light-infantry entries as “poor” or earlier “very poor” as their state changes over time; the newly moved army unit is the only 15,000 light infantry added and has quality code `5`. This supports **army quality `5` = poor**. It does not turn the city's first slot word into a direct quality field; that word also advances across turns for empty slots. Screenshot `12_rom_2.png` shows regular cost rising to **517 talents per quarter** from the prior **442**; a single transfer suggests a 75-talent increment, but the cost formula remains unverified.

The second pair changes **68 bytes** overall, including three map cells and an unrelated Gaul army movement `(64, 26)` → `(63, 21)` and several nation-state bytes. It therefore is not an otherwise byte-identical single-action pair. The matching 15,000 decrease in Rome's city-unit slots and increase in its army is still direct, localized evidence for this transfer.

## Mercenaries at Alexandria are in a different block

Screenshot `12_ptol_3.png` lists **Egyptian light infantry, 9,056, good** as available in Alexandria. Alexandria's two nation-record city-unit slots instead total **11,000** (9,000 light infantry and 2,000 archers), so the mercenary listing is separate from the city-unit list.

A candidate 12-byte record at absolute `0x1EDEC`, just after the sixteen nation records, contains six little-endian words: **190, 94, 35, 0, 9,056, 7**. The first two match Alexandria's coordinates, type `0` is light infantry, and quality `7` is “good.” The word `35` may select the “Egyptian” label, but that association is not proven. Adjacent records repeat at 12-byte intervals and include sentinel values such as `65,535`; the block's full count and empty-slot rules still need decoding before adding a general mercenary parser or viewer list.
