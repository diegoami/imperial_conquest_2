# Imperial Conquest 2: full-version and save-file static analysis

12 September 2026. This extends the [initial demo report](impconq2-initial-report.md). Inputs were `2_imperial2.zip` (431,135 bytes) and `1,sav.sav` (131,313 bytes). Both were read as data; **neither game executable was run**. ZIP CRC verification passed for all 15 full-version members. SHA-256 values for both inputs and every archive member are in [impconq2-full-save-sha256.txt](impconq2-full-save-sha256.txt).

## What changed from demo to full edition

The full readme identifies the package as **“Full Freeware version 1.01”** and says it was repacked for Home of the Underdogs in August 2001. It also says filenames were expanded to correct a DAT-opening error. This is provenance from the package's readme, not an independent verification of the original publisher's release history.

| Component | Demo | Full package | Finding |
| --- | ---: | ---: | --- |
| EXE | 1,166,336 bytes; `v0.99` and `shareware` strings | 1,166,336 bytes; `v1.01` and `freeware` strings | Different executable; same PE32 x86/Win32 format and same 292 static imports |
| DAT | 140,706 bytes | 140,706 bytes | **Byte-identical**, SHA-256 `94d0ccfc67148d727de4c4e60aefc3c53ba9e67775bd689f0fcb2e23c12e5fbd` |
| HLP | 53,910 bytes | 52,661 bytes | Different WinHelp file; same classic WinHelp signature |
| CNT | 2,566 bytes | 2,544 bytes | Different contents map; full version omits the demo's “Ordering” topic entry |
| WAVs | Ten files under `Wavs/` | Same ten byte-identical files at ZIP root | Package layout changed; sound lookup may need a `WAVS` folder |
| Readme | 590 bytes | 520 bytes | Different shareware/freeware and packaging instructions |

The full EXE still has eight PE sections, but its on-disk `CODE` section is 1,536 bytes larger and `.rsrc` is 1,536 bytes smaller than the demo's. It has 29 Delphi `TPF0` form resources rather than 30; `TPDMESSAGE` is the only resource name present in the demo but absent from the full version. This is consistent with removing a demo message, although that form's exact behavior has not been established. The full EXE contains `\WAVS\`; its ten WAV files are flat in the ZIP. On a case-insensitive Windows installation, placing those WAVs in a `WAVS` subfolder is the layout suggested by the embedded path. This has **not** been tested by running the game.

The identical DAT is especially useful: the map and initial city data discovered in the demo report remain the reference data for the full version. The full EXE and help should be the primary references for final game rules and UI because they are the later, non-demo edition.

## Save-file layout: first pass

The `.sav` has no obvious signature at its beginning. Its first 89,600 bytes align directly with the DAT's candidate 320 × 140 grid of little-endian 16-bit cells. **326 of 44,800 cells differ** between this save and the initial DAT. Of those changes, 311 are value `0 → 1`; the others involve swaps or replacements among larger cell values. Their game meaning is not yet known. The comparison is strong evidence that the save stores a mutable copy of the world grid, but it does not prove that every cell value is terrain.

At offset `0x15e00` (89,600), the save has the same sequence of **334 city names in 34-byte records** as the DAT. The city-record region differs by just 13 bytes, all in the field at record offset `+24` for 12 cities, including Genua, Mediolanum, Alexandria, and Iconium. At the time of this initial comparison, one save was insufficient to label it as money, population, defense, or anything else. The city-coordinate candidate fields and city names are unchanged. **Later evidence:** a [controlled 79-ton transfer](controlled-army-supply-transfer.md) strongly identifies `+24` as city supplies.

From offset `0x18a5c` (100,956) onward, the save differs substantially from the DAT. It contains unit names and other game-state structures, plus a trailing block of plain-text news/events. Visible text includes `Week 1      Spring      270 BC` and a report of a Seleucid army destroying a Ptolemaic army. The final bytes also contain small integer fields. The save is shorter than the DAT by 9,393 bytes; it appears to serialize much of the same world structure followed by dynamic state, rather than store only a compact list of changes. Exact boundaries, counts, and serialization rules remain to be mapped.

The odd Windows/MSN-looking text previously noted in the DAT also appears in the save. That reinforces the need to establish which byte ranges are real fields before interpreting or copying the later records; it does not establish why those bytes are there.

## What this changes for the reimplementation

1. Treat the **full v1.01 EXE and HLP** as the main behavioral reference. Use the demo EXE to isolate demo-only UI and code changes.
2. Build one bounded parser for the **shared DAT grid and 334 city records**, then a second parser for the matching save regions. Verify that a save's immutable city names and coordinates agree with the supplied DAT before loading it.
3. Make map cells and city fields part of explicit mutable game state, with their semantics marked unknown until corroborated. The `0 → 1` cell changes and city `+24` changes are high-value targets for controlled save comparisons.
4. Obtain a few more saves from **known actions** (before/after moving one army, changing one tax rate, fortifying one city, and ending one turn). A bytewise comparison of each pair would identify field meanings far more reliably than this single snapshot. Such comparisons can be done statically once those files exist; this analysis did not run the game.
5. Keep the asset loader tolerant of the two WAV packaging layouts while requiring exact DAT/EXE file identities where needed. The new engine can present a clear missing-sound message instead of silently failing.

The immediately actionable milestone is now a **DAT + SAV world-state viewer**: render the candidate grid, overlay the 334 cities, and highlight the 326 cells changed in this save. That will also test the proposed grid orientation and coordinate mapping.
