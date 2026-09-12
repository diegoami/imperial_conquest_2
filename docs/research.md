# Static research notes

All observations below come from reading the supplied demo ZIP, full-version ZIP, eight saves, eight paired screenshots, and two user recordings as data or media. No game binary was executed for this research. Field labels beyond city names, coordinates, screenshot-backed terrain classes, and strongly corroborated owner codes remain provisional.

Detailed evidence and per-file hashes are preserved in the [demo static-analysis report](reports/impconq2-initial-report.md), [full-version/save analysis](reports/impconq2-full-save-analysis.md), [one-turn save comparison](reports/one-turn-save-comparison.md), [map-layout notes](reports/map-layout.md), [save/screenshot analysis](reports/saves-and-screenshots.md), [battle observation](reports/battle-observation.md), and [strategic recording and summer saves](reports/strategic-recording-and-summer-saves.md).

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

The save is 131,313 bytes and begins with the same world-prefix layout. Compared with the DAT, it has 326 changed map cells; 311 transitions are `0 → 1`. All 334 city names and candidate coordinates agree. Twelve cities have byte changes in the word beginning at city-record offset `+24` (13 changed bytes in total). Later save data differs substantially and includes text news/events and a visible `Week 1      Spring      270 BC` label. The `+24` field and cell transitions are mutable but their meanings are unknown.

Some city-name padding and later blocks in both DAT and SAV contain unrelated-looking Windows/MSN text. Parsers should not infer field boundaries from printable strings alone.

Comparing the two saves after one reported turn shows 331 city records change at word `+24`, and Sidon's word `+18` changes `3 → 2` alongside a news report of a Ptolemaic-to-Seleucid capture. The later save adds three 61-byte news slots and a candidate week byte changes `1 → 3` in a 55-byte trailer. These are correlations, not yet a complete field specification; see the detailed comparison linked above.

Additional saves and matching screenshots strengthen two interpretations: the trailer byte at `+40` matches displayed weeks 1, 3, 7, 9, and 11, and city word `+18` tracks ownership changes for Rome/Gaul as well as Ptolemaic/Seleucid. Registered screenshots also establish original terrain colors for cell values `0`, `2`, `3`, `4`, and `5`. See the save/screenshot analysis for exact evidence and limitations.

The later saves confirm that the trailer's `+40` byte resets from 11 to 1 at the Spring→Summer boundary and then advances to 3 and 5. It is a displayed week-within-season candidate, not a monotonic turn counter. Trailer `+44` is 0 in every Spring save and 1 in every Summer save, strongly suggesting a season index. `10.sav` changes Tarquinii, Caere, and Ariminum from owner code 6 to 0; the news explicitly reports Tarquinii and Ariminum defecting from Gaul to Rome. The battle recording ends in a Rome victory with 39,941 of 50,700 Rome troops surviving, and `8.sav` records “Rome destroys army of Gaul.” See the two recording reports for exact evidence and limits.

## Next checks

1. Identify the remaining terrain and special-cell meanings, using the registered screenshots and original help files.
2. Decode full-version WinHelp topics and Delphi form streams to inventory rules and UI actions.
3. Obtain more paired saves around one controlled action at a time, including a city-management change and a season boundary, then compare bytes to identify state fields.
4. Map full-version executable references to DAT and SAV structures before implementing turn, economy, diplomacy, and combat rules.
