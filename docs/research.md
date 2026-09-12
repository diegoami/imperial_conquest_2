# Static research notes

All observations below come from reading the supplied demo ZIP, full-version ZIP, and two saves as data. No game binary was executed. Field labels beyond city names and the candidate coordinates remain provisional.

Detailed evidence and per-file hashes are preserved in the [demo static-analysis report](reports/impconq2-initial-report.md), [full-version/save analysis](reports/impconq2-full-save-analysis.md), [one-turn save comparison](reports/one-turn-save-comparison.md), and [map-layout notes](reports/map-layout.md).

## Known inputs

| Input | SHA-256 | Notes |
| --- | --- | --- |
| Demo `impconq2.zip` | `5a996fee02c28c818b47ab13ec884907c2b5f620fff5267dd8c9b652ca1fe45f` | Shareware v0.99 |
| Full `2_imperial2.zip` | `f58a2cf8102fc1c79da8c7ceece12b1bbda2401d9c72af63cc0174ac4440d02a` | Readme calls it full freeware v1.01, repacked August 2001 |
| Full/demo `Imperial Conquest 2.dat` | `94d0ccfc67148d727de4c4e60aefc3c53ba9e67775bd689f0fcb2e23c12e5fbd` | Byte-identical in both packages |
| Supplied `1.sav` (same contents as earlier `1,sav.sav`) | `d20971d5c73a39c82bb9a6c76413394d1d6fc8174bf5ae857a8161eaba5d7c10` | Before the reported turn |
| Supplied `4.sav` | `e1d5f0489f4f24544a70af8c26b14a0d2d14e3acba20a614a0ae79da412d0b32` | After the reported turn |

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

## Next checks

1. Identify terrain value meanings, using the now-confirmed column-major rendering and the original help files.
2. Decode full-version WinHelp topics and Delphi form streams to inventory rules and UI actions.
3. Obtain more paired saves around one controlled action at a time, then compare bytes to identify state fields.
4. Map full-version executable references to DAT and SAV structures before implementing turn, economy, diplomacy, and combat rules.
