# auto-resolve-approaches (archived)

**Question:** Which auto-resolve models could make army composition matter, and how would they be judged?

**Result:** Specified, not decided: five candidates (C1 the merged instant resolver as baseline, C2 the original's tactical model run headless, C3 type-weighted instant, C4 round-based, C5 morale-and-retreat), a constants register, nine metrics with pass bands fixed in advance, and four observed tactical battles as smoke tests only. Deliberately unranked. Corrected 2026-09-23 against the research pass for #288: K27 is settled (+3 for a computer-controlled side only), the initial-morale clamp is `[60, 90]`, the rout cascade is one level deep, and the merged baseline now includes the original's small-unit deletion (T63, #295, 562e608). Corrected 2026-10-04 against the tactical-battle decompile: the matrix K15 was the wrong table (#647), and the placeholders D06 (shooting distance) and D07 (morale comparison) are settled and depart from the original.

Full write-up, frozen: [archive/desktop-2026-10-20](https://github.com/diegoami/imperial_conquest_2/blob/archive/desktop-2026-10-20/docs/investigations/auto-resolve-approaches.md).
