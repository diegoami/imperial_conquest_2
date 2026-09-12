# Comparing `1.sav` and `4.sav` after one reported turn

This is a static, read-only comparison of the two saves supplied with the full version. The user reports that `4.sav` was made after one turn from `1.sav`. Neither save nor the game executable was run or modified for this analysis. The binary saves remain outside this public repository.

| Save | Bytes | SHA-256 |
| --- | ---: | --- |
| `1.sav` | 131,313 | `d20971d5c73a39c82bb9a6c76413394d1d6fc8174bf5ae857a8161eaba5d7c10` |
| `4.sav` | 131,496 | `e1d5f0489f4f24544a70af8c26b14a0d2d14e3acba20a614a0ae79da412d0b32` |

The saves have the same 9,646-byte prefix. The size grows by 183 bytes, exactly three 61-byte slots. The existing news slots at offsets 131075, 131136, and 131197 are byte-identical in both saves. The later save appends slots at 131258 (blank), 131319 (`Week  3      Spring      270BC`), and 131380 (`Sidon   (Ptolemaic)  falls to Seleucid.`). The first save's last visible dated slot says `Week 1      Spring      270 BC`. The added blank slot may be a separator; its role is unverified.

The final 55 bytes align after accounting for the three inserted slots: offsets 131258–131312 in `1.sav` and 131441–131495 in `4.sav`. They differ at only one position, trailer offset `+40` (absolute 131298 and 131481), whose byte is `1` then `3`. This correlates with the week labels and is a candidate calendar field, not yet a proven turn counter. A single reported turn appears to advance the displayed week from 1 to 3 in this scenario; more samples are needed to determine the general calendar rule. **Later check:** three more saves carry weeks 7, 9, and 11 at the same trailer position; see [later saves and screenshots](saves-and-screenshots.md).

## Shared world prefix

Treating bytes 0–89599 as 44,800 little-endian 16-bit candidate map cells, 342 cells change between the two saves. In 311 cells, value `1` becomes `0`. Those exactly reverse the 311 `0 → 1` differences between the original DAT and `1.sav`; `4.sav` differs from the DAT at only 32 candidate cells, versus 326 for `1.sav`. The repeated `1` values may represent a transient overlay or game state, but their purpose is unknown. Some other cell changes exchange values in the 200–335 range with neighboring values; their meaning needs spatial visualization.

The 334 city records retain their names and candidate coordinates. Of these, 331 change at record word `+24`: 316 values rise and 15 fall, with signed differences from -103 to +191. Brixia, Modena, and Verona do not change. This is strong evidence that `+24` is a mutable field affected by turn processing; a later [controlled supply transfer](controlled-army-supply-transfer.md) identifies its units as tons of city supplies, while the turn-processing rule remains unknown. Sidon (record 263) also changes at raw byte offsets `+18`, `+22`, `+26`, and `+28`. Its little-endian word at `+18` changes `3 → 2`, which correlates with the new report of a change from Ptolemaic to Seleucid control. Tentatively, `+18` may encode owner nation, with `3` and `2` identifying those two factions. Confirm with other cities and saves before assigning those labels in the parser.

The binary regions after the city table also change. They likely contain mutable world state, but their record boundaries are not yet established, so a same-offset byte count there would be misleading once the news slots are inserted.

## Next checks

1. Extend the new save-to-save comparison command beyond the known map and city regions as later record boundaries become established.
2. Get another controlled before/after pair for a turn with no Sidon capture, or for a single city action, to separate regular turn effects from battle effects.
3. Find the news-list start and count, verify whether all entries are fixed 61-byte strings, and test the 55-byte trailer interpretation against additional saves.
4. Visualize changed map cells and cross-reference city `+18` values against faction labels in the help and event log.
