# The DAT's own file layout, and why six of seven `IC2.Data` parsers throw on it

**Status: solved.** `Imperial Conquest 2.dat` is **not** a SAV file with a different extension. It shares
the SAV's first 100,956 bytes (map + city table) and diverges immediately afterwards: it carries **no
record-count words at all**, its army and fleet counts are hardcoded in the loader, and its nation
record is **1,055 bytes, not 1,172**. The complete read order below is decompiled from the loader and
accounts for all 140,706 bytes of the file exactly.

Two questions were open going in, and both now have answers:

1. **Does the DAT contain a nation table at all?** — **Yes**, a full 16-record table at `0x1B100`, in
   `NationCatalog` order. This is the answer that decides whether task **T30** is a one-line sentinel
   fix or real parsing work. It is real parsing work.
2. **How many saves actually fail to parse, and why?** — **3 of 51**, all from one cause; plus the
   DAT, from a different cause. The previously-reported "3 of 27" was a coincidence of two numbers:
   2 failing saves plus the DAT, out of a 27-file sample. The true counts are below.

Same tagging convention as `design-audit.md` / `game-design.md`: **[confirmed]** (direct RE evidence,
cited), **[derived]** (extrapolation), **[designed]** (new design, no RE evidence), **[open]** (not
established either way).

## Sources and method

- **Decompiled code:** `FUN_004481a0` (`0x004481A0`) and `FUN_00448aa4` (`0x00448AA4`), obtained by
  running the local Ghidra toolchain against the recovered project:

  ```text
  analyzeHeadless ghidra_projects IC2 -process "Imperial Conquest 2.exe" -noanalysis
    -scriptPath scripts -postScript ExportAddresses.java out.txt 00448aa4 004481a0
  ```

  Neither function appears in the `all_app_functions.txt` whole-application dump — they are reached
  only as indirect calls (`func_0x004481a0()`) from `TPremierForm_NewGame` (`0x0045a9e0`), which is
  in the dump and in `delphi_symbols.tsv`. That is why this layout had not been traced before.
- **Bytes:** `C:\Users\diego\Documents\imp_conq_original\Imperial Conquest 2.dat` (140,706 bytes),
  and all 51 `.sav` files under that directory's `saves\`, `saves-processed\` and
  `saves-processed\processed\`.
- **Parsers:** every public `Parse` in `IC2.Data`, run over every file (a throwaway console project,
  not committed — the permanent version of this sweep is T30's DoD line 4).

## `FUN_004481a0` is the DAT loader, and it reads fixed counts [confirmed]

`TPremierForm_NewGame` calls exactly two helpers before touching any UI: `FUN_004481a0` (read the
DAT) and `FUN_00448aa4` (fill in everything the DAT does not contain). The loader opens the file
named at `0x0045e744` and issues a sequence of `Read(dest, length)` calls through the stream object's
vtable — the same shape as the SAV load/save pair documented in
[`decompiled-sav-file-layout.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md),
and just as authoritative, because it *is* the code that consumes the file.

| DAT offset | Bytes | Destination | Contents |
| ---: | ---: | --- | --- |
| `0x00000` | 89,600 | `DAT_0045e870` | map, 320 × 140 × 2 — same shape as the SAV's |
| `0x15E00` | 11,356 | `DAT_00479590` | city table, 334 × 34 — same shape as the SAV's |
| `0x18A5C` | 15 × 656 | `DAT_0047c1ec + i × 0x290` | **army records — 15 of them, no count word** |
| `0x1B0CC` | 2 × 26 | `DAT_0049c26c + i × 0x1a` | **fleet records — 2 of them, no count word** |
| `0x1B100` | 16 × **1,055** | `DAT_00474670 + i × 0x494` | **nation records** (in-memory stride 1,172) |
| `0x1F2F0` | 1,494 | 14 destinations | fixed static tables — see below |
| `0x1F8C6` | 1,040 | `DAT_0049cc94` | static table |
| `0x1FCD6` | 3,012 | `DAT_0049d0a4` | static table |
| `0x2089A` | 4,992 | `DAT_0049dc68` | leader-name pool, 16 × 12 × 26 bytes |
| `0x21C1A` | 2,440 | `DAT_0049f994` | static table |
| | **140,706** | | **= the file size, exactly** |

Immediately after the army and fleet loops the loader **assigns** the counts the SAV stores on disk:

```c
DAT_004a0324 = 0xf;   // 15 armies
DAT_004a0326 = 2;     // 2 fleets
```

That is the whole defect in one line. The SAV format writes `count`, then `count` records; the DAT
writes records only, and the count lives in the executable.

Three independent cross-checks that the table above is right, beyond the byte total:

- `DAT_0047c1ec` (army array base, stride `0x290` = 656) and `DAT_0049c26c` (fleet array base, stride
  `0x1a` = 26) are the **same two globals** the turn tick walks in
  [`thracia-supply-morale.md`](thracia-supply-morale.md).
- The 40-byte static table at `DAT_004794a0` lands at file offset **`0x1F7D8`** — exactly where that
  investigation independently located the season table (Spring 50 · Summer 80 · Autumn 80 · Winter
  20), solved there from the DAT bytes with no knowledge of this read order.
- The DAT's army 14 reads `(160, 30)`, 22,000 troops, 142 tons, morale 65, and its fleet 0 reads 90
  ships / 80 tons / condition 85 — the exact starting values of the Thracian army and the
  Carthaginian fleet that the same investigation tracked through 13 and 10 saves respectively.

## The nation record: present, complete enough, and a different shape [confirmed]

The loader does **not** read a nation record as one block. It reads fourteen pieces of the 1,172-byte
in-memory record and leaves the rest untouched:

| In-memory offset | Bytes read | Field |
| --- | ---: | --- |
| `+0x000` | 11 | **name** |
| `+0x026` | 32 | — |
| `+0x046` | 2 | — |
| `+0x048` | 668 | — |
| `+0x2e4` | 320 | recruitment queue (`SaveNationLayout.RecruitmentOffset`) |
| `+0x430` | 4 | — (then the loader copies it to `+0x434`) |
| `+0x438` | 4 | **treasury** (then copied to `+0x43c`) |
| `+0x440` | 2 | **unity** |
| `+0x442` | 2 | **mobilized %** |
| `+0x444` | 2 | **capital city index** |
| `+0x446` | 2 | **city count** (then copied to `+0x448`) |
| `+0x44a` | 2 | **tax rate %** |
| `+0x44c` | 2 | — |
| `+0x44e` | 2 | — |
| | **1,055** | = the observed DAT stride, exactly |

So the DAT record is the SAV record with **117 bytes missing, in five separate places** — not shifted
by a constant, which is why no adjustment to `SaveNationLayout.Locate` can reach it:

| Not in the DAT | Bytes | Why |
| --- | ---: | --- |
| `+0x00b … +0x025` leader name | 27 | assigned at New Game — see below |
| `+0x424 … +0x42f` | 12 | not read |
| `+0x434`, `+0x43c`, `+0x448` | 4+4+2 | duplicates the loader *copies* rather than reads |
| `+0x450 … +0x493` trailer | 68 | includes the human-player flag at `+0x490` |

Mapped onto the DAT record's own offsets: treasury `+0x40d`, unity `+0x411`, mobilized `+0x413`,
capital `+0x415`, cities `+0x417`, tax `+0x419`. Verified directly — the 16 names at stride 1,055
read `Rome, Carthage, Seleucid, Ptolemaic, Macedonia, Numidia, Gaul, Greece, Celtiberia, Illyria,
Dacia, Bithynia, Galatia, Armenia, Media, Thracia`, in `NationCatalog`'s exact order, and Rome's
treasury/unity/capital/cities read 2,200 / 821 / 85 / 25 at those offsets, matching the values
`SaveNationTable` reads from the session's first save.

### Leader names and the human-player flag are genuinely not in the file [confirmed]

`FUN_00448aa4` — the *other* helper `TPremierForm_NewGame` calls — writes exactly the fields the DAT
omits, per nation, at in-memory stride `0x494`:

```c
i = random(12);
strcpy(record + 0x0b, leaderPool + i * 0x1a);   // leader name, 12 candidates per nation
record[0x490] = 0;                              // human-player flag: nobody is human yet
```

with the leader pool being the 4,992-byte table at `0x2089A` (16 nations × 12 candidates × 26 bytes),
and `TPickLeaders_InitializeForm` the dialog that lets the player override the draw. The same
function seeds the 16-entry turn-order table by shuffling `0 … 15`.

This matters for **T29**: a DAT export cannot source leader names or seat assignment from the DAT,
because the original does not either. They are New Game state, not world data.

## The parse sweep: every file, every parser [confirmed]

All 51 `.sav` files and the DAT, through all seven `IC2.Data` parsers.

| Class | Files | Failing parser | Cause |
| --- | ---: | --- | --- |
| Clean | **48 saves** | — | all seven parsers succeed |
| Army tombstone | **3 saves** | `SaveArmyTable` only | one `owner == 0xFFFF` record aborts the file |
| DAT layout | **1 DAT** | 6 of 7 (all but `WorldPrefix`) | no count words; different nation record |

The three tombstone saves, each containing **exactly one** such record, always with **valid**
coordinates:

| Save | Record | Coordinates | Troops |
| --- | ---: | --- | ---: |
| `1_thracia_271_spring_3.sav` | army 10 | (41, 62) | 44,497 |
| `1_thracia_271_autumn_1.sav` | army 9 | (38, 52) | 0 |
| `1_cartago_271_spring_5.sav` | army 0 | (97, 31) | 0 |

The third is new: it was missed because the previous pass sampled only the Thracia series. The
failure is `SaveArmyTable.cs:42`'s `owner > 15` check throwing `InvalidDataException`, which aborts
the whole file — the defect `thracia-supply-morale.md` already described, now counted properly. Note
that coordinates are always valid, so **the owner word alone is the discriminator**; widening the
coordinate checks would be the wrong fix.

The DAT's cascade, for the record: `SaveArmyTable.Parse` reads army 0's `x` (100) as an army count
and throws *"Army count 100 exceeds the available fixed-size records"*; `SaveNationLayout.Locate`
then throws *"Save ends before the fleet count"*, which takes `SaveNationTable`, `SaveFleetTable`,
`SaveMercenaryTable` and `SaveRecruitmentTable` with it; `SaveTurnState` throws on the trailer, which
the DAT does not have at all. Only `WorldPrefix` succeeds, because its 100,956-byte prefix is the one
part the two formats genuinely share.

## Conclusions

1. **The DAT contains a full 16-record nation table** at `0x1B100`, stride 1,055, names in
   `NationCatalog` order. `[confirmed]` — decompiled from `FUN_004481a0` and read out of the bytes.
   T30 is therefore real parsing work, not a "fails cleanly, source nations elsewhere" fix.
2. **The DAT is not SAV-shaped and cannot be reached by adjusting the SAV locator.** `[confirmed]` —
   no count words anywhere, and a nation record missing 117 bytes in five separate places.
3. **Leader names and the human-player flag are not in the DAT.** `[confirmed]` — both are written by
   `FUN_00448aa4` at New Game, the leader by a 1-in-12 draw from the pool at `0x2089A`.
4. **3 of 51 saves fail, all on the same `0xFFFF` army tombstone**, one record each, always with
   valid coordinates. `[confirmed]` — the remaining 48 parse clean through all seven parsers.
5. **The whole 140,706-byte file is accounted for.** `[confirmed]` — the read sequence sums to the
   file size exactly, and its season table lands on `0x1F7D8`, independently corroborating
   `thracia-supply-morale.md`.

### Still open

- **The fourteen fixed static tables at `0x1F2F0`** (1,494 bytes total: 200, 40, 10, 232, 336, 168,
  110, 110, 50, **40 = the season table**, 30, 64, 24, 80) are located but only one is identified.
  The four larger tables after them (1,040 / 3,012 / 4,992 / 2,440) are likewise unidentified apart
  from the leader pool. Several are plausibly the unit-type and terrain tables the fixtures corpus
  already carries from other reports — worth checking, but not needed by T29 or T30.
- **Whether the DAT's 15 armies and 2 fleets are the only scenario the executable can load.** The
  counts are compiled in, so the original ships exactly one world; nothing here says whether a
  differently-sized DAT would be readable. Irrelevant to this project (T29 exports to JSON once), but
  worth not assuming either way.
- **The unread nation fields** — `+0x048 … +0x2e3` (668 bytes) and `+0x2e4 … +0x423` (320 bytes, the
  recruitment queue) are read from the DAT but their contents are only partly labelled in
  `SaveNationTable`. Not a blocker: T29 needs the seven fields above and nothing else.
