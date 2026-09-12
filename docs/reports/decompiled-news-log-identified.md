# The 61-byte record identified: it's the news log

Closes the open item from `decompiled-sav-file-layout.md`. `DAT_0049f994` — the base address of the 61-byte, count-prefixed record run found in the SAV file right after the mercenary table — is also touched by `TInformation_PaintForm` (the news/library panel seen in screenshots throughout this project, e.g. the "Week 9 Spring 270BC / Caere (Rome) falls to Gaul"-style entries) and by `FUN_00449240`, which is called after every capture/defection/battle message this project has decompiled (`" falls to "`, `" defects from "`, etc.). Decompiling `FUN_00449240` confirms it directly:

```text
FUN_00449240(message):
    if newsCount == 39:                       // log is full (40 slots, indices 0-39)
        shift every entry down one slot        // drop the oldest, index 0
    newsCount = min(39, newsCount + 1)
    copy `message` into slot[newsCount]         // each slot is 61 bytes (0x3d)
```

This is a **ring-buffer news log**: up to 40 recent event messages, each a fixed 61-byte string buffer, with the oldest entry dropped once full. `DAT_004a031e` — the field the SAV file stores immediately before this run of records — is not a record *count* in the usual sense, it's **the index of the most recently used slot**, which is exactly why the save format loops `count + 1` times: it's saving slots `0` through `DAT_004a031e` inclusive.

Every "X falls to Y", "X defects from Y to Z", and combat-result message decompiled across this whole project (`decompiled-city-capture-resolution.md`, `decompiled-defection-and-siege-attrition.md`, `decompiled-combat-formula-structure.md`) ends with a call to this exact function — meaning **every one of those events gets logged here**, and this array is the authoritative source for the "Information" news panel's scrolling history.

## What this does not fully establish

- The exact byte layout *within* one 61-byte slot (presumably a null-terminated or length-prefixed string plus possibly a small header, not decompiled at the byte level).
- Why the previously-measured 3,042-byte region size didn't reconcile exactly with `(count+1)×61` plus the known fixed tail (a ~6-byte gap noted in `decompiled-sav-file-layout.md`) — still open, though now that the record's true purpose (a ring buffer, not a simple list) is known, the discrepancy might trace to an off-by-one in how the "count" field's own semantics (max-used-index, not count) were reconciled, worth re-checking with this correction in mind.

## Reproduction

Found by grepping the whole-application decompiled dump for the SAV layout's `DAT_0049f994` array base address, then decompiling its two other referencing functions (`TInformation_PaintForm`, `FUN_00449240`) with `ExportAddresses.java`.
