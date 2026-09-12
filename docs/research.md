# Static research notes

All observations below come from reading the supplied demo ZIP, full-version ZIP, ten saves, paired screenshots, and three user recordings as data or media. No game binary was executed for this research. Field labels are assigned only where screen, save-difference, or structural evidence supports them; remaining interpretations are marked provisional.

Detailed evidence and per-file hashes are preserved in the [demo static-analysis report](reports/impconq2-initial-report.md), [full-version/save analysis](reports/impconq2-full-save-analysis.md), [one-turn save comparison](reports/one-turn-save-comparison.md), [map-layout notes](reports/map-layout.md), [save/screenshot analysis](reports/saves-and-screenshots.md), [battle observation](reports/battle-observation.md), [strategic recording and summer saves](reports/strategic-recording-and-summer-saves.md), [menu/toolbar inventory](reports/menu-and-toolbar-inventory.md), [controlled army-supply transfer](reports/controlled-army-supply-transfer.md), [army-table analysis](reports/army-records-and-roman-roster.md), and [Rome city, recruitment, and nation mapping](reports/rome-city-recruitment-and-nations.md).

## Known inputs

| Input | SHA-256 | Notes |
| --- | --- | --- |
| Demo `impconq2.zip` | `5a996fee02c28c818b47ab13ec884907c2b5f620fff5267dd8c9b652ca1fe45f` | Shareware v0.99 |
| Full `2_imperial2.zip` | `f58a2cf8102fc1c79da8c7ceece12b1bbda2401d9c72af63cc0174ac4440d02a` | Readme calls it full freeware v1.01, repacked August 2001 |
| Full/demo `Imperial Conquest 2.dat` | `94d0ccfc67148d727de4c4e60aefc3c53ba9e67775bd689f0fcb2e23c12e5fbd` | Byte-identical in both packages |
| Supplied `1.sav` (same contents as earlier `1,sav.sav`) | `d20971d5c73a39c82bb9a6c76413394d1d6fc8174bf5ae857a8161eaba5d7c10` | Before the reported turn |
| Supplied `4.sav` | `e1d5f0489f4f24544a70af8c26b14a0d2d14e3acba20a614a0ae79da412d0b32` | After the reported turn |
| Supplied `5.sav` | `3581c50662719bf9dee3682aff1001cd6f00cb0a228c3df717c39b3e8cb8b4bf` | Week 7 label |
| Supplied `6.sav` | `ecc3008e2d6f69118d42e52e0b503710111b56c951a667198e71c0287d50c321` | Week 9 label |
| Supplied `7.sav` | `733a16aa7ee5aeacbca9e689b0d5465c277af5402683860df0e8dfb8fde2c93b` | Week 11; five paired screenshots |
| Supplied `8.sav` | `e45d3b17fd4b7b95d74a3a2919ec2dd4089f708a60318fb0ca24698db0c78bf1` | Week 1 Summer; news of Rome defeating Gaul |
| Supplied `9.sav` | `2171344c4cbef87f90944d02c873a21d507475e8dbefa6f4cac7cb27bb069e25` | Week 3 Summer |
| Supplied `10.sav` | `2d42dc110a3f2cd8058c4e7d6626c299fca17c4885ee4bb058767d2dd6d917b3` | Week 5 Summer; three Gaul→Rome ownership changes |
| Supplied `11.sav` | `e21ce740295c51f55758c26776e6e485f0a7e7e1106bc133d2f0bf0c0073f77d` | Before the 79-ton army-supply transfer |
| Supplied `11_supply.sav` | `7608ea62372f8dacba3af38698e7462b111b96e488062ab2c3479ca34128d6f0` | After the transfer; same week |

## Executable and resources

The EXE is a 32-bit x86 PE32 Windows GUI program, not a 16-bit NE program. Borland Delphi class names, a `Delphi 2` string, and `TPF0` binary form resources strongly suggest Delphi 2. The full build has 29 form resources; the demo has 30, with `TPDMESSAGE` present only in the demo. The full EXE imports Win32 GUI, GDI, file, sound, and OLE APIs. Its `CODE` section is 1,536 bytes larger than the demo's, so the two EXEs should not be treated as differing only in a license flag. The classic WinHelp files differ; the full `.cnt` omits the demo's Ordering entry.

The full ZIP places the ten WAV files at the archive root, while the EXE contains `\\WAVS\\` and the demo ZIP uses `Wavs/`. That layout discrepancy is untested; a future asset loader should be explicit about it.

## DAT/SAV shared world prefix

| Region | Offset | Length | Evidence |
| --- | ---: | ---: | --- |
| Candidate map | `0x00000` | 89,600 | 44,800 little-endian 16-bit cells; column-major 320 × 140 layout produces recognizable Mediterranean geography |
| City records | `0x15e00` | 11,356 | 334 consecutive records, 34 bytes each, from Sala to Rhagae |
| Later DAT sections | `0x18a5c` onward | 39,750 | Unit and nation names, binary state; layout unverified |

In each city record, bytes `0..13` contain a NUL-terminated ASCII name and little-endian words at `+14` and `+16` fall within `x=9..317`, `y=2..136`. Rendering the grid column-major places Rome, Carthago, Alexandria, Sidon, and Rhagae at geographically plausible locations, strongly supporting these as map coordinates. The parser validates that layout and preserves the remaining city bytes without naming their fields.

The first save is 131,313 bytes and begins with the same world-prefix layout. Compared with the DAT, it has 326 changed map cells; 311 transitions are `0 → 1`. All 334 city names and candidate coordinates agree. Twelve cities have byte changes in the word beginning at city-record offset `+24` (13 changed bytes in total). Later save data differs substantially and includes text news/events and a visible `Week 1      Spring      270 BC` label. The map-cell transitions remain unexplained. A later controlled transfer identified city word `+24` as supplies; see below.

Some city-name padding and later blocks in both DAT and SAV contain unrelated-looking Windows/MSN text. Parsers should not infer field boundaries from printable strings alone.

Comparing the two saves after one reported turn shows 331 city records change at word `+24`, and Sidon's word `+18` changes `3 → 2` alongside a news report of a Ptolemaic-to-Seleucid capture. The later save adds three 61-byte news slots and a candidate week byte changes `1 → 3` in a 55-byte trailer. These are correlations, not yet a complete field specification; see the detailed comparison linked above.

Additional saves and matching screenshots strengthen two interpretations: the trailer byte at `+40` matches displayed weeks 1, 3, 7, 9, and 11, and city word `+18` tracks ownership changes for Rome/Gaul as well as Ptolemaic/Seleucid. Registered screenshots also establish original terrain colors for cell values `0`, `2`, `3`, `4`, and `5`. See the save/screenshot analysis for exact evidence and limitations.

Further registration shows that all sampled cells with values `6`–`11` contain dark-blue river pixels, with six map codes corresponding to straight and corner connections. In both DAT and `7.sav`, all 334 cells valued `20`–`199` coincide with city coordinates, and `7.sav` city codes match `20 + owner + 16 × variant` in all 334 cases. Sparse values `200`–`299` and the observed `333`/`335` align with army and fleet icons on land and water respectively. The Godot viewer can draw these layers and switch between the initial DAT and local saves. See [rivers and map markers](reports/rivers-and-map-markers.md).

The later saves confirm that the trailer's `+40` byte resets from 11 to 1 at the Spring→Summer boundary and then advances to 3 and 5. It is a displayed week-within-season candidate, not a monotonic turn counter. Trailer `+44` is 0 in every Spring save and 1 in every Summer save, strongly suggesting a season index. `10.sav` changes Tarquinii, Caere, and Ariminum from owner code 6 to 0; the news explicitly reports Tarquinii and Ariminum defecting from Gaul to Rome. The battle recording ends in a Rome victory with 39,941 of 50,700 Rome troops surviving, and `8.sav` records “Rome destroys army of Gaul.” See the two recording reports for exact evidence and limits.

A third user recording walks through the menus and shortcut icon rows. It confirms the main UI's File, Game, Strategy, Nations, Area map, Unit map, and Help command groups. Static reading of the full EXE's Delphi menu stream fills in submenu captions for fleet orders, city fortification, and mercenary filters. The recruitment and supply dialogs expose distinct unit counts, costs, stocks, and balances that can guide controlled save comparisons. See the menu/toolbar inventory; no game binary was run.

The controlled `11.sav` → `11_supply.sav` pair differs in only three bytes. Rome city word `+24` falls **1,810 → 1,731**, and a post-city word at absolute `0x18A68` rises **403 → 482**. Both changes are 79 tons, matching the user's army-supply action. The user's later composition screenshots show this Roman army at `(100, 42)` with **13 units**, **48,173 troops**, **482** tons of supply, and **296** money. These values and every troop count appear in the first of ten 656-byte SAV army records; all available saves share that record layout. The first Roman army's **50,700** troops in `7.sav` and **39,941** in `8.sav` independently match the recorded battle's pre- and post-battle totals. The initial DAT uses a different post-city layout. The parser now exposes save army records and the Godot viewer can show their rosters. See the [controlled-pair report](reports/controlled-army-supply-transfer.md) and [army-table analysis](reports/army-records-and-roman-roster.md). General transfer rules remain unknown.

Five further `11_supply` screenshots show Rome's city information, its recruitment list, and an ordered nation icon/name key. Rome's city record contains **1,731** supplies, **78** fortification percent, **181** population thousands, and **317** tribute talents, matching the panel exactly. A 40-slot recruitment section contains twelve Rome entries whose type codes and troop quantities match the displayed list; type code `2` is now identified as archers. The positional icon/name key resolves all sixteen nation identifiers and corrects the viewer colors for Celtiberia, Armenia, and Media. See [Rome city, recruitment, and nations](reports/rome-city-recruitment-and-nations.md). The fortification panel's parenthetical **85,000**, the numeric loyalty thresholds, and the recruitment state word remain unresolved.

## Next checks

1. Identify the remaining terrain and special-cell meanings, especially water value `1`, and decode the fleet records and the DAT's different post-city layout.
2. Decode full-version WinHelp topics and Delphi form streams to inventory rules and UI actions.
3. Obtain more paired saves around one controlled action at a time, especially a second supply transfer at another city/army, a money transfer, and recruitment, then compare bytes to identify state fields.
4. Map full-version executable references to DAT and SAV structures before implementing turn, economy, diplomacy, and combat rules.
