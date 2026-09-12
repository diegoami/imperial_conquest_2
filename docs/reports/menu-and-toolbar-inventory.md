# Menu and toolbar inventory

This is an initial feature inventory for the full Imperial Conquest 2 v1.01 interface. The user recorded the menu walkthrough in `recordings/bandicam 2026-09-12 06-04-01-165.mp4` and explained that the toolbar icons are shortcuts to the same menu commands. We inspected the MP4 in a video player and checked menu captions against the full EXE's embedded Delphi `TMainMenu`/`TMenuItem` form data. We did **not** execute the game binary. The original video and EXE remain outside Git.

| Evidence | Detail |
| --- | --- |
| Recording | 38,344,604 bytes; SHA-256 `09b4e4a1f1bb00cff5806339b268fc0398aaa50e18128d3404501adaa0a2bf4b` |
| Encoding | H.264 1920 × 1080 video, AAC audio, 1:53.900 duration |
| Full EXE form data | `TMainMenu` begins near file offset `0xEF3A6` (decimal 979,878); captions and event-handler names occur in the embedded binary form stream. These are file offsets, not runtime addresses. |

## Command groups

The menu bar reads **File · Game · Strategy · Nations · Area map · Unit map · Help**. Entries below are observed in sampled video frames unless explicitly marked **form only**; the embedded form data corroborates the names. Menu presence does not prove that every command succeeds in every game state.

| Menu | Entries |
| --- | --- |
| File | New; Open; Save; Save As; Close. Visible around 00:03. |
| Game | End turn; New player; New nation; Abdicate. Visible around 00:05. |
| Strategy | News; International relations; Taxation; Balance sheet; Recruit unit; Build fleet. Visible around 00:20. |
| Nations | Rome; Carthage; Seleucid; Ptolemaic; Macedonia; Numidia; Gaul; Greece; Celtiberia; Illyria; Dacia; Bithynia; Galatia; Armenia; Media; Thracia; All nations. Visible around 00:45. |
| Area map | Show cities; Show capital; Show armies; Show fleets; Show all; Show mercenaries; Find a city. Visible around 00:58. **Show mercenaries** expands to Light infantry, Heavy infantry, Archers, Light cavalry, Heavy cavalry, and All mercenaries (**form only** for submenu captions). |
| Unit map | Army; Fleet; City; Cancel selection. Visible around 01:05. |
| Unit map → Army | Supply army; Recruit mercenaries; Transfer unit; Split army; Join armies; Change units; Disband army. Visible around 01:20. |
| Unit map → Fleet | Supply fleet; Repair fleet; Transfer ships; Split fleet; Join fleets; Scuttle fleet. **Form only:** this submenu was not held open long enough in sampled video frames to transcribe reliably. |
| Unit map → City | Fortify city. **Form only:** caption from the embedded menu stream. |
| Help | Help topics; Show hints; About Imperial Conquest. Visible around 01:30; Show hints was checked. |

The nation-coloured icon row corresponds to the Nations selection menu; the area-map and unit-map toolbars expose pictorial shortcuts. The full EXE's main-form data confirms named speed buttons and hints for **Open**, **Save**, **End turn**, all six Strategy entries, and each of the 16 named nations plus **All nations**. For example, the `sb_Pols` button hint is “International relations”; both it and that menu entry name the `StrategicDecision` handler. The user's description establishes the intended relationship for the other icon rows. We have not yet mapped every area-map and unit-map icon or checked whether every menu command has a toolbar icon. A modern interface should route both menus and toolbar buttons through the same command definitions, so they cannot silently diverge.

## Dialogs and state surfaced by the walkthrough

- **International Relations** (around 00:10) lists nations with columns labelled **peace**, **trade**, **ally**, and **war**, using mutually exclusive selection controls per nation. This is direct UI evidence for four displayed relation choices; the underlying diplomacy rules remain unknown.
- **Army recruits** (around 00:25–00:35) offers light infantry, heavy infantry, archers, light cavalry, and heavy cavalry; a troop-quantity control; eligible recruiting cities; a list of existing units; **Initial cost**, **Quarterly cost**, **Mobilize**, and **Disband** controls. One sampled light-cavalry selection displayed 1,400 troops, initial cost 105, and quarterly cost 21. These are example screen values, not yet a general cost formula.
- **Supply army** (around 01:15) shows stock at a city and in the army, plus a national balance and army money, with quantity controls. This separates city supplies, army supplies, national funds, and army funds in the UI. Exact save fields and transfer rules remain to be located.
- The selected-army information panel (around 01:20) shows movement, supply amount and percentage, morale, money, terrain, troop totals by class, number of units, regular cost, and mercenary pay. This is a useful target schema for later army-record decoding.
- **About Imperial Conquest** (around 01:45) identifies the game as *Imperial Conquest 2 v1.01* and the full/freeware version, corroborating the package readme.

## Implementation implications and next checks

1. Model commands independently of presentation: File/game flow, national strategy, map filters, army/fleet/city orders, and help. Menu entries and their shortcut icons should call the same command handler.
2. Decode the full main-form stream to map toolbar button names and hints to menu handlers, then document exact icon-to-command matches. The EXE contains event-handler names such as `StrategicDecision`, `ChangeNation`, `ShowOnAreaMap`, and `UnitMapAction`; those names show routing but not gameplay effects.
3. The [first controlled supply pair](controlled-army-supply-transfer.md) identifies Rome's city supply word and one army supply word. Repeat at another city/army, then compare a separate recruitment or money-transfer pair to identify the remaining fields.
4. Decode help topics for preconditions and costs before implementing each command in the headless engine.
