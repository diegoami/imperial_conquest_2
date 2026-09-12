# Imperial Conquest 2: initial static reverse-engineering report

12 September 2026. Source: the `impconq2.zip` attachment in the referenced conversation (482,252 bytes; SHA-256 `5a996fee02c28c818b47ab13ec884907c2b5f620fff5267dd8c9b652ca1fe45f`). The archive was read in memory for analysis. **The game executable was not run.** ZIP CRC verification passed for every member. SHA-256 hashes for the archive and every member are in [impconq2-sha256.txt](impconq2-sha256.txt).

## Inventory and file identification

| Members | Uncompressed size | Identification |
| --- | ---: | --- |
| `Imperial Conquest 2.exe` | 1,166,336 | MZ DOS stub plus 32-bit Windows PE executable |
| `Imperial Conquest 2.dat` | 140,706 | Custom binary game data; regular grid and fixed-size records identified below |
| `Imperial Conquest 2.HLP` | 53,910 | Classic Windows Help binary |
| `Imperial Conquest 2.cnt` | 2,566 | Plain-text Windows Help contents/index map |
| `Read me.txt` | 590 | Plain-text installation and shareware notes |
| `Wavs/Sound1.wav` through `Sound10.wav` | 61,876 total | Ten RIFF/WAVE sound files; mono PCM, 8- or 16-bit, 5,512–22,050 Hz |

The readme calls this a Windows 95 shareware release for 1–16 players on one computer. It says to copy the files together and retain the `WAVS` subfolder for sound. No installer, original save file, or source code is in the archive.

## Executable

The DOS header begins `MZ`, with `e_lfanew = 0x100`. The new-executable signature there is **`PE\0\0`, not `NE`**. The DOS stub itself says the program must be run under Win32. This corrects the earlier tentative 16-bit hypothesis.

| PE field | Value |
| --- | --- |
| Machine | `0x014c`: Intel 386/x86, 32-bit |
| Optional header | `0x010b`: PE32 |
| Subsystem | `2`: Windows graphical application |
| Image base | `0x00400000` |
| Entry RVA | `0x0005bcc4` |
| Sections | `CODE`, `DATA`, `BSS`, `.idata`, `.tls`, `.rdata`, `.reloc`, `.rsrc` |
| Resource section | 759,296 bytes on disk, about 65% of the EXE |

The COFF timestamp decodes to 19 June 1992 UTC, but this is unlikely to represent the game's 1997 release build date; it should not be used to date the binary without corroboration. There is no export directory, debug directory, or version-info resource in the PE headers/resources parsed here.

The executable imports 292 functions across nine import descriptors (seven distinct DLLs): `kernel32.dll`, `user32.dll`, `oleaut32.dll`, `gdi32.dll`, `comctl32.dll`, `comdlg32.dll`, and `winmm.dll`. The import set is a conventional Win32 desktop application: window/message handling and `WinHelpA`; GDI drawing; common controls and Open/Save dialogs; file I/O; OLE variants; and `PlaySoundA`. There is no DirectX or networking import in the static import table. Dynamic loading via `LoadLibraryA`/`GetProcAddress` is present, so an absent static import is not proof that an API is never used.

Strings and resources strongly identify **Borland Delphi**, probably Delphi 2: the binary contains `TObject`, `TPersistent`, `TComponent`, many `T...` form class names, a literal `Delphi 2` string, and 30 `RCDATA` resources beginning with Delphi's binary-form marker `TPF0`. The visible `Delphi32.Application.1` string may be runtime or embedded material; it is not proof the IDE was needed to play. Form resource names include `TAreaMap`, `TUnitMap`, `TBattleMap`, `TPolitics`, `TBuildFleet`, `TArmyRecruits`, and `TToEndTurn`. Other resources are 10 bitmap button glyphs, 20 string tables, cursors, and an icon. The large area-map and unit-map form resources are 413,778 and 170,307 bytes respectively; they likely include substantial embedded graphical data, though their internal object streams have not yet been decoded.

Game-facing strings expose `Imperial Conquest 2.DAT`, `Imperial Conquest 2.HLP`, a `Saved games|*.sav` dialog filter, `v0.99`, and actions for armies, fleets, cities, taxation, politics, battle, and turns. Error messages include limits of 20 units per army, 100,000 troops per army, and 100 ships when combining fleets. These are useful leads for control-flow analysis, not yet a complete rules specification. A raw scan also finds Pascal-like procedure text and unrelated Windows/MSN-looking fragments in parts of the binary; their provenance and relevance need checking before treating them as recoverable game source.

## `.DAT` structure

There is no obvious file signature or container header. Its opening region is highly regular and mostly small little-endian values; its overall entropy is about 2.93 bits/byte, inconsistent with a wholly compressed or encrypted blob.

| Offset | Length | Static finding | Confidence |
| --- | ---: | --- | --- |
| `0x00000`–`0x15dff` | 89,600 | 44,800 little-endian 16-bit values, 83 distinct values, range 0–335; most are 0, 2, 3, 4, or 5 | High for representation |
| `0x15e00`–`0x18a5b` | 11,356 | 334 consecutive 34-byte records, each beginning with an ancient city name (`Sala`, `Olisipo`, …, `Rhagae`) | High |
| `0x18a5c` onward | 39,750 | More structured records, names such as unit battalions and peoples, runs of `0xff`, and other binary blocks | Unmapped |

**Map hypothesis:** 44,800 cells fit **320 × 140** exactly. In the 334 city records, 16-bit values at record offsets `+14` and `+16` range from 9–317 and 2–136 respectively, supporting an `(x, y)` interpretation on that grid. This does not yet establish row order, terrain codes, or whether all 334 city records are active in the shareware edition. Names occupy the beginning of each 34-byte record; their exact field width and the remaining fields are still unverified.

Some bytes in the city-name padding and later `.DAT` blocks resemble unrelated Windows registry/path text (for example an MSN dialer reference). This may be unused memory or an artifact of the original serialization/build process, but the current evidence does not explain it. An importer should parse only validated fields and avoid assuming every nonzero byte is intentional game data. The archive's valid CRC establishes that the ZIP member is internally intact, not that the historical source file was pristine.

## `.HLP` and `.CNT`

The `.HLP` starts with little-endian magic `0x00035f3f`, consistent with classic WinHelp. Its header points to an internal directory at offset 3,039 and records a total length of 53,910 bytes, matching the member size. Directory names visible there include `|CONTEXT`, `|CTXOMAP`, `|FONT`, `|KWBTREE`, `|KWDATA`, `|KWMAP`, `|SYSTEM`, `|TOPIC`, and embedded bitmaps `|bm0` through `|bm47`. Topic content is encoded/compressed; a plain ASCII strings scan cannot be treated as the full manual.

The `.cnt` is readable text with `:Base`, `:Title`, `:Index`, and `:Link` directives and a numbered three-level contents tree (90 numbered lines). It maps help topics for game concepts, file operations, strategy, nation selection, the area/unit maps, army/fleet actions, cities, shortcuts, and speed buttons. Its `:Base` names `Imperial Conquest 2.hlp`, while `:Index`/`:Link` name `ImperialConquest.hlp` without spaces; this inconsistency may be a stale link and is worth checking when help compatibility is tested. The `.cnt` provides a useful chapter outline, but not the topic body text.

## Concrete next steps for a compatible new engine

1. **Freeze a reproducible fixture.** Keep this ZIP private as an input fixture and preserve its hashes. Add read-only parsers that validate sizes, offsets, and bounds and emit structured JSON summaries. Keep copyrighted assets out of the new engine repository; load them from a user's installation.
2. **Decode the world data.** Render the 320 × 140 candidate grid using each common cell value as a color, compare city `(x, y)` positions with the image, and determine row order and terrain meanings. Parse all 334 city records, label only fields confirmed by cross-checks, and map the remaining `.DAT` sections.
3. **Extract the game specification.** Decode WinHelp topics and Delphi `TPF0` form objects. The help contents tree and form/event names can seed a rules and UI inventory. Review apparent embedded Pascal snippets cautiously and correlate them with code before using them.
4. **Map executable behavior statically.** Load the PE32 image in a disassembler/decompiler with x86 Delphi awareness. Start from references to the DAT filename, `.sav` filter, end-turn form, combat errors, and city/map code. Record data layouts, turn sequence, formulas, and AI decisions with evidence and confidence levels.
5. **Implement in layers.** First build an asset loader and world model; then deterministic turn, economy, diplomacy, army/fleet, and battle modules; then save/load and a modern UI. Use golden data assertions from the original files and rule examples from the manual. Later behavioral comparison would require a separately approved, isolated run of the original game; none was performed for this report.

The most valuable immediate deliverable is a **validated DAT parser and map visualization**. It would turn the strongest static hypothesis into a testable world model before substantial engine code is written.
