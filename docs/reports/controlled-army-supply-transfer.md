# Controlled army-supply transfer in save 11

The user saved immediately before and after supplying an army near Rome, reporting that the army's stock rose from **403 to 482 tons**. This is a 79-ton transfer. We read `11.sav` and `11_supply.sav` as data and did not run the game. The pair contains no turn advance or other changed bytes visible in the files.

| Local save | Bytes | SHA-256 | Latest embedded calendar label |
| --- | ---: | --- | --- |
| `saves/11.sav` | 129,421 | `e21ce740295c51f55758c26776e6e485f0a7e7e1106bc133d2f0bf0c0073f77d` | Week 7 Summer 270 BC |
| `saves/11_supply.sav` | 129,421 | `7608ea62372f8dacba3af38698e7462b111b96e488062ab2c3479ca34128d6f0` | Week 7 Summer 270 BC |

The files differ in **three bytes at two locations**. The map, all other city fields, all other post-city bytes, news, and final calendar trailer are identical.

| Location | Before → after, little-endian 16-bit | Change | Interpretation |
| --- | ---: | ---: | --- |
| Rome city record 85, word `+24`; absolute `0x16962` (92,514) | `0x0712` = 1,810 → `0x06C3` = 1,731 | −79 | City's supply stock in tons |
| Word at absolute `0x18A68` (100,968), 12 bytes after the 334-city table | `0x0193` = 403 → `0x01E2` = 482 | +79 | Supplied army's stock in tons |

The city word changes in both bytes; the army word changes only in its low byte, so the whole-file byte difference is three rather than four. Rome's record is at `0x1694A`, with candidate coordinates `(101, 43)`. The city table ends at `0x18A5C` (100,956). A later [Roman roster comparison](army-records-and-roman-roster.md) established a SAV army count at that boundary and 656-byte army records. The word at `0x18A68` is the first army record's `+10` supply field. Its coordinates `(100, 42)` and the user's panel showing 482 tons confirm the identification. The pair establishes exact conservation of the 79-ton transfer in this scenario; general transfer rules remain unknown.

Earlier multi-turn comparisons showed city word `+24` changing widely but could not identify it. This controlled action now strongly identifies it as **city supplies**. The parser exposes that word as `CityRecord.Supplies`, while preserving the remaining unknown city bytes. The inspector's equal-length save comparison also reports exact changed post-city byte runs, so another controlled pair can be reproduced without a separate script.

From the repository root with `assets.local.ini` pointing to the original asset directory:

```text
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --compare-saves saves/11.sav saves/11_supply.sav
```

## Next checks

1. Supply a different army at a different city with a known amount. Verify that the city's `+24` word and the corresponding army word again change by opposite amounts, and locate that second army word.
2. Use a pair that transfers money separately from supplies to distinguish adjacent post-city fields.
3. Compare additional controlled transfers to verify that the `+10` army supply field changes consistently in other army records.
