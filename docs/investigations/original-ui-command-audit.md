# The original's menus, toolbars and map clicks, against the clone's engine and UI

**Question.** The user decided on 2026-10-01 that the game screen should work like the original: orders given
on the map by clicking a unit and then its target, the original's Delphi menu bar with its submenus, and its
shortcut toolbars, each wired to an engine command. Before any task is written, what does every menu entry
and toolbar icon of the original do, and what do the clone's engine and UI do for it today?

**Result.** 47 rows (the 16 nation entries share one row). 22 are present, 6 partial, 10 need only UI, 7 need
engine work, and 2 are proposed out of scope. The engine has no command for
the recruitment-slot Disband or Abdicate. The
Show-mercenaries filters wait on mercenary offers having a position. Of the existing pairwise orders, Join armies, Join fleets and
Transfer ships require the *same tile*, but the original takes the partner at distance exactly 1. That is the
shape of the embark defect ([#453](https://github.com/diegoami/imperial_conquest_2/issues/453)).
The map-click model is now known from the code. The original has two map windows: an overview *Area map* and a 32-px *Unit map*. Orders are given only on the Unit map.
A run of the original under Wine observed the select, move, peace-time attack prompt and Cancel selection rows (§2.1), the Taxation slider's range (§1.3) and where Split army puts the new army (§1.6). A second run observed the fleet: Build fleet and the launch (§1.3), the six Fleet entries (§1.6), embark, disembark and the fleet's move by click (§2.1), and Cancel selection on the fleet strip (§3.3). A third observed a naval battle at war, which opens no window (§2.1), a fourth the refusal to attack a docked fleet (§2.1) and a non-Roman nation's build and launch (§1.3), and a fifth the attack prompt for a fleet at trade terms, whose Yes declares war on the target and its ally (§2.1). Each is a candidate until the desktop original confirms it. On one row it disagrees with the code reading: **No** on the attack prompt keeps the army selected.

This is an audit, not a plan: the task split and the design questions that follow from it go to the main
session, which writes them into the catalogue and the design document.

## Evidence and tags

Tags follow [README.md](README.md): **[confirmed]**, **[derived]**, **[designed]**, **[open]**.

- **[Wine candidate: …]** marks a result observed only in a run of the original under Wine. It settles nothing until the desktop original confirms it ([evidence-pipeline.md](../evidence-pipeline.md)).

- **Research reports** are cited by name, from
  [`imperial-conquest-2-research/docs/reports/`](https://github.com/diegoami/imperial-conquest-2-research/tree/main/docs/reports).
  The inventory is [`menu-and-toolbar-inventory.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/menu-and-toolbar-inventory.md)
  (the menu tree, from a recording and the EXE's `TMainMenu` form data). It is "the inventory" below.
- **Decompilation read for this audit.** Several rows cite a function and address from the local dump
  `%LOCALAPPDATA%\ReTools\all_app_functions.txt` ([operating-guide.md §1.3](../operating-guide.md#13-local-toolchain-outside-both-repositories)).
  No research report decodes these yet. They are tagged **[derived: code]**: the code is unambiguous where
  quoted, but no second session has checked the reading, and the research side should promote it (see
  "Evidence gaps" at the end). Form-field captions (labels) are not in the dump; they live in the EXE's form
  stream, which only the inventory has read.
- **The clone** is cited by file. "Engine" means a typed `ICommand` under `src/IC2.Engine/**/Commands/` and its
  `GameSession` verb (`src/IC2.Engine/Presentation/GameSession.cs` `Submit`). "UI" means `godot/UI/**` and
  `godot/Screens/**`.

Status values: **present** (engine and UI both do it), **partial** (some of it), **UI-only missing** (the
engine command exists; no UI reaches it), **engine missing** (no engine command), **out of scope** (with the
reason).

## 1. The menu bar, entry by entry

The menu bar reads **File · Game · Strategy · Nations · Area map · Unit map · Help** [confirmed: the
inventory]. Every menu command routes through one of four main-form handlers: `StrategicDecision`,
`ChangeNation`, `ShowOnAreaMap` or `UnitMapAction` [confirmed: the inventory]. Each handler compares the
sender with a pair of fields, one menu item and one speed button, so a menu entry and its toolbar icon run the
same code [derived: code, `TPremierForm_StrategicDecision` `0x0045B2BC`].

### 1.1 File

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| New | Starts a new game [confirmed: the inventory]. Handler `TPremierForm_NewGame` `0x0045A9E0` [derived: code, by name]. | `GameSessionFactory` (new game) | File → **New**, from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)), after a prompt that the current game will be left [designed]; and from the main menu | **present** | — |
| Open | Opens a saved game (`Saved games\|*.sav` filter) [confirmed: `impconq2-initial-report.md`]. `TPremierForm_OpenGameFile` `0x0045AAD4`. | `load <path>` verb | File → **Open** (`LoadGameScreen`), from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)) and its main-toolbar icon (`CommandToolbar`), after the leave prompt [designed]; and from the main menu | **present** | — |
| Save | Saves to the current file, or falls through to Save As when there is none [derived: code, `TPremierForm_SaveGameFile` `0x0045AB84`]. | `save <path>` verb | File → **Save**, from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)) and its main-toolbar icon (`CommandToolbar`): an auto-named file under `user://saves` | **present** (the naming differs) | — |
| Save As | A file dialog, then save [derived: code, `TPremierForm_SaveGameFileAs` `0x0045ABB0`]. | `save <path>` verb | File → **Save As**: a name prompt, then a save under `user://saves` [designed]. It overwrites an existing file without asking | **present** (a name prompt, not a file dialog) | — |
| Close | Closes the main form, which ends the program [derived: code, `TPremierForm_Quit` `0x0045AC54` binds to the main form's close. That Close is bound to `Quit` is read from the name only]. | — | File → **Close**: after the leave prompt [designed], back to the main menu, which has Quit | **present** (Close returns to the main menu) | Whether Close is bound to `Quit` is read from the name only |

### 1.2 Game

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| End turn | Ends the active nation's turn [confirmed: the inventory, which lists a speed button too]. `TPremierForm_EndTurn` `0x0045B0A8`. When an army of the player's needs supplies, it first asks *"End turn ?"* ("An army of yours needs supplies. …"), with the buttons **End turn** and **Make more moves** **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. Six checks open it, for human seats only and only on units that have not acted this week: an army under 20 % of supply capacity, an army's purse below one round of mercenary pay, a fleet not docked at an own city, a fleet under `ships / 5` supplies, and the army aboard a fleet on the two army tests; at most five lines [confirmed: code, `2026-10-03-end-turn-warning-box.md`]. | `end` verb | Game → **End turn**, from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)) and its main-toolbar icon (`CommandToolbar`), one handler. Nothing asks about an army that needs supplies; the clone adopts the box in both presets [designed: the user's decision of 2026-10-02] | **partial**: no supply box | — (bug [#586](https://github.com/diegoami/imperial_conquest_2/issues/586)) |
| New player | Opens the leader-picking form (`TPickLeaders`), which adds a human seat in mid-game. If no human seat remains, the game ends. If the active nation is now computer-run, its turn ends [derived: code, `TPremierForm_NewPlayer` `0x0045B148`; the form is identified by its VMT address `0x456D00`, which precedes `TPickLeaders`' methods]. Adding the Ptolemaic human seat changes only three bytes of the save [confirmed: `2026-09-29-nation-view-origin-and-unit-map-clicks.md`]. | none. Seats are fixed at New Game (`ScenarioSeatScreen`). | none | **out of scope (proposed)**: it changes seats in mid-game, and the clone fixes seats at New Game. The user decides. | What `TPickLeaders` allows in mid-game |
| New nation | Asks *"Are you sure you want to lead a different nation ?"*. On yes, the current nation drops to computer control and `TPickLeaders` opens [derived: code, `TPremierForm_NewNation` `0x0045B198`]. | none | none | **out of scope (proposed)**, as New player | — |
| Abdicate | Asks *"Are you sure you want to abdicate ?"*. On yes, it calls the "nation drops out" handler `FUN_00449078`: the nation becomes computer-run with a new random leader. If no human seat remains, every form closes and the menus switch off [derived: code, `0x0045B24C`, `FUN_00449078`]. It is the same path as a human leader falling [confirmed: `decompiled-diplomacy-peace-terms-and-instant-battles.md`]. | none. The deposition path (`GameSession.cs` ~:730, `Control = SeatControl.Ai`) is the nearest. | none | **engine missing** (small: it reuses deposition). The user decides whether it is in scope. | — |

### 1.3 Strategy

The six entries have both menu items and speed buttons. In `StrategicDecision` the pairs map to: News →
`TInformation_PrintNews`; International relations → `TPolitics`; Taxation → `TChangeTax`; Balance sheet →
`TBalanceSheet`; Recruit unit → `TArmyRecruits`; Build fleet → `TPremierForm_BuildNewFleet`. Each form is
identified by its VMT address, which precedes that class's own methods [derived: code, `0x0045B2BC`].
`sb_Pols` → International relations is [confirmed: the inventory].

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| News | Prints the news log into the information window [derived: code]. | `NewsLogWriter`, `news` verb | Strategy → **News**, from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)) and its main-toolbar icon (`CommandToolbar`): toggles `NewsLogPanel` | **present** | — |
| International relations | The `TPolitics` row editor: peace / trade / ally / war per nation, committed on OK. Its methods are `MakePeace`, `MakeTrade`, `MakeAlliance`, `ChangeIR` and `OK` [confirmed: the inventory; `decompiled-diplomacy-peace-terms-and-instant-battles.md`]. | `declare-war`, `make-peace`, `propose-alliance`, `propose-trade`, `accept-offer`, `peace-yes`/`peace-no` | Strategy → **International relations**, from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)) and its main-toolbar icon (`CommandToolbar`): `DiplomacyScreen`, one action button per cell, not a row edited then committed | **present** (shape differs) | — |
| Taxation | The `TChangeTax` dialog ("Change tax level"): a slider over the current and new tax rate and income (`income = taxBase × tax% / 100`). OK writes nation `+0x44A` [confirmed: `decompiled-fleet-tax-and-mercenary-formulas.md`, `rome-tax-increase-and-sidon-capture.md`; the OK write is derived: code, `TChangeTax_OK` `0x004540B8`]. The slider runs over the integers **0 to 40**, one step per arrow key and **5 per Page key**; OK at either end writes 0 or 40 to `+0x44A` **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. | `set-tax <percent>` verb (`SetTaxRateCommandHandler`, [T103](https://github.com/diegoami/imperial_conquest_2/issues/561)): sets the active nation's `TaxRatePercent`, an integer within the ruleset's `economy` bounds 0 to 40 | The city and nation panels show it read-only (`ContextPanel.BuildCityPanel`) | **UI-only missing**: the Taxation dialog | Whether the slider opens at the current rate (the run's own click moved it) |
| Balance sheet | The `TBalanceSheet` dialog, read-only (OK only). It shows the active nation's quarterly budget. Income: tax income, `taxBase / 4`, and trade income (`FUN_004499ec`), with a total. Expenditure: `cities × 7 + wealth / 20000`, ships × 3, recruitment-slot upkeep, regulars' upkeep and mercenaries' pay, with a total. It also shows the treasury, and `wealth / 500`, capped at 20,000 and rounded [derived: code, `TBalanceSheet_PaintBalance` `0x0045376C`]. These are exactly the terms of the quarterly treasury credit and of the upkeep bill [confirmed: `decompiled-quarterly-billing-and-economy.md`, `upkeep-payment-and-desertion.md`]. `wealth / 500` is the deposition-for-debt threshold [confirmed: `upkeep-payment-and-desertion.md`]; its on-screen label is unknown. | **none**: the terms are computed inside the quarterly systems (`Economy/Quarterly*System.cs`), and there is no read-only projection | `BalanceSheet` (`src/IC2.Engine/Economy/BalanceSheet.cs`) and the `balance` verb, since T104 (PR #580) | **UI-only missing** (T109 builds the dialog) | Every line's caption, and the rounding steps of the last figure |
| Recruit unit | The `TArmyRecruits` dialog: pick a city, a type and a size; it shows initial and quarterly cost. Its buttons are **Recruit unit**, **Mobilize** and **Disband**, over a list of the city's units in training [confirmed: the inventory; `ptolemy-run-ui-inventory-and-leader-draw.md` §1; methods `RecruitUnit`, `MobilizeUnits`, `DisbandUnits`]. | `recruit-standing`, `mobilize`. **Nothing disbands a recruitment slot.** | The city panel's **Recruit** (type picker, troop spin box). The army panel's **Mobilize first ready slot**. The training list is on the nation and city panels. | **partial**: Disband is engine missing, and there is no dialog | Which cities may recruit ([#515](https://github.com/diegoami/imperial_conquest_2/issues/515)). What a disbanded slot refunds. |
| Build fleet | First lists the fleets under construction (*"A fleet of N ships will be ready in W weeks at C."*). It refuses with *"Only nations with coastal cities can build fleets."*, *"You do not have a free coastal city at this time."* or *"You cannot build a fleet at this time."* (the fleet table is full at 99). Otherwise it opens `TBuildFleet`: 10–100 ships, at `ships × 10` [derived: code, `TPremierForm_BuildNewFleet` `0x0045B3A8`; costs confirmed: `decompiled-unit-map-orders-and-record-fields.md`]. OK says *"The fleet will be built at C."* and the dialog **stays open**, so a second OK orders a second fleet. A 30-ship order cost 300 and launched 12 turns later on the sea tile next to the build city, with 0 moves that turn, supplies 50 and condition 100; the next turn it had 32 moves (`30 − (ships − 50)/10`). It launched there although the city had been captured in the meantime **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. Seleucid's 60-ship order said *"The fleet will be built at Issus."* and launched 12 turns later at (219,55), the south-west neighbour of Issus (220,54), with 29 moves the next turn **[Wine candidate: [`2026-10-03-pair-2-seleucid-ptolemaic.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-03-pair-2-seleucid-ptolemaic.md)]**; the clone launches at (219,54) ([#628](https://github.com/diegoami/imperial_conquest_2/issues/628)). At the start, Dacia, Galatia and Media get the first refusal, and each of the other 13 nations gets one port named: Rome Caere, Carthage Carthago, Seleucid Issus, Ptolemaic Alexandria, Macedonia Pynda, Numidia Siga, Gaul Rotomagus, Greece Athens, Celtiberia Saguntum, Illyria Epidamnus, Bithynia Sinope, Armenia Phasis, Thracia Byzantium **[Wine candidate: [`2026-10-02-start-as-each-nation.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-start-as-each-nation.md)]**. | `order-fleet` (`OrderFleetCommand`); its coastal test accepts Media's two Caspian cities ([#600](https://github.com/diegoami/imperial_conquest_2/issues/600)) | none | **UI-only missing** | Whether the player picks the build city. The game's coastal test, which refuses Media |

### 1.4 Nations

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| Rome … Thracia (16 entries, one row) | Sets the *viewed nation* (nation `+0x48A`), checks the menu item, presses that nation's speed button, and shows that nation's status panel (`TInformation_ShowNationStatus`) [derived: code, `TPremierForm_SelectNation` `0x0045B660`, `ChangeNation` `0x0045B6F0`]. The viewed nation also scopes the Area-map highlights (§1.5). The status panel lists the leader, capital, cities, population, unity as a word, tax %, mobilized %, treasury and that nation's relations [confirmed: `ptolemy-run-ui-inventory-and-leader-draw.md` §1]. The 16 coloured speed buttons pair left to right with these entries [confirmed: `rome-city-recruitment-and-nations.md`]. | Every nation's state is in `GameState.Nations`. There is no "viewed nation". | The **Nations** menu and the toolbar's 16 coloured nation buttons set the viewed nation ([T110](https://github.com/diegoami/imperial_conquest_2/issues/568)): the menu item is radio-checked, the button pressed, and the context panel shows that nation's status. A foreign nation's panel shows public facts only (leader, capital, cities, relations), by the user's decision of 2026-10-01. At each turn start the viewed nation returns to the active seat | **present** | What the panel withholds for a *foreign* nation. A foreign army's panel withholds moves, supply, morale and money [confirmed: `ptolemy-run-ui-inventory-and-leader-draw.md` §5]. Whether the nation panel hides the treasury is not observed. |
| All nations | Viewed nation = 16: the Area-map highlights cover every nation. No status panel is shown (`SelectNation` shows one only for an index below 16) [derived: code]. | — | **All nations** (menu item and toolbar button, [T110](https://github.com/diegoami/imperial_conquest_2/issues/568)): the Area-map highlights cover every nation; no status panel | **present** | — |

### 1.5 Area map

The Area map is a separate window: the whole 320 × 140 map, scaled. A click on it re-centres the Unit map
(`TAreaMap_AreaMapClick` `0x0043E63C` → `TUnitMap_AreaMapClicked` `0x004462AC`) [confirmed:
`2026-09-29-nation-view-origin-and-unit-map-clicks.md`]. It draws a rectangle for the Unit map's current
view (`TAreaMap_UnitMapCursor` `0x0043E760`) [derived: code]. The **Show** commands do not hide anything. Each
paints a highlight over the Area map for the *viewed nation* (all nations when All nations is selected), and
toggles a "highlights shown" flag. The window keeps up to 50 highlight operations so that it can repaint them
(`TAreaMap_StoreDraw` `0x0043E594`) [derived: code, `0x0043E210`–`0x0043E510`].

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| Show cities | Highlights every city of the viewed nation [derived: code, `TAreaMap_ShowCities` `0x0043E210`] | state only | **Show cities** (menu and Area-map strip, [T110](https://github.com/diegoami/imperial_conquest_2/issues/568)) highlights the viewed nation's cities on the mini-map; the old hide-layer toggle is gone | **present** | — |
| Show capital | Highlights each nation's capital, the viewed one or all [derived: code, `0x0043E288`] | `NationState` capital | **Show capital** ([T110](https://github.com/diegoami/imperial_conquest_2/issues/568)) highlights the viewed nation's capital, or every capital under All nations | **present** | — |
| Show armies | Highlights the viewed nation's armies [derived: code, `0x0043E31C`] | state only | **Show armies** ([T110](https://github.com/diegoami/imperial_conquest_2/issues/568)) highlights the viewed nation's armies, not those aboard a fleet; the old hide-layer toggle is gone | **present** | — |
| Show fleets | Highlights the viewed nation's launched fleets [derived: code, `0x0043E3AC`] | state only | **Show fleets** ([T110](https://github.com/diegoami/imperial_conquest_2/issues/568)) highlights the viewed nation's fleets; the old hide-layer toggle is gone | **present** | — |
| Show all | Show cities, capital, fleets and armies, together [derived: code, `0x0043E440`] | — | **Show all** ([T110](https://github.com/diegoami/imperial_conquest_2/issues/568)) turns the four highlights on together | **present** | — |
| Show mercenaries → Light infantry | Highlights every mercenary offer of that type (the 50-slot offer table), each drawn with its own symbol, bitmaps 4–8 [derived: code, `TAreaMap_ShowMercs` `0x0043E470`]. The submenu captions are form data only [confirmed: the inventory]. | **Offers have no position** ([#325](https://github.com/diegoami/imperial_conquest_2/issues/325), planned as [T76](https://github.com/diegoami/imperial_conquest_2/issues/330)). A new game starts with none ([#457](https://github.com/diegoami/imperial_conquest_2/issues/457)). | none | **engine missing** | — |
| Show mercenaries → Heavy infantry | as above, type 1 | as above | none | **engine missing** | — |
| Show mercenaries → Archers | as above, type 2 | as above | none | **engine missing** | — |
| Show mercenaries → Light cavalry | as above, type 3 | as above | none | **engine missing** | — |
| Show mercenaries → Heavy cavalry | as above, type 4 | as above | none | **engine missing** | — |
| Show mercenaries → All mercenaries | every type (`param 5`) | as above | none | **engine missing** | — |
| Find a city | The `TFindCity` dialog: a nation dropdown (any nation) and that nation's city list, with capitals marked. Choosing a city centres the Unit map on it and highlights it on the Area map [derived: code, `TFindCity_FillListBox` `0x0043D7DC`, `ChangeCity` `0x0043D93C`]. It is routed through `StrategicDecision`, not `ShowOnAreaMap`. | state only | **Find a city** (`FindCityDialog`, [T110](https://github.com/diegoami/imperial_conquest_2/issues/568)): a nation dropdown and its city list; choosing a city centres the map and highlights it on the mini-map. A capital is marked with a " (capital)" suffix [designed] | **present** | How a capital is marked in the list (a string transform; probably upper case) |

The Area-map toolbar has one more command with no menu entry: **`TAreaMap_ToggleMap`** (`0x0043DFD4`). It
flips a per-nation flag (nation `+0x46C`) and repaints [derived: code]. What it toggles is **[open]**. The
toolbar's "multicoloured map/palette" icon (§3.2) is the likely button.

### 1.6 Unit map → Army, Fleet, City, and Cancel selection

`UnitMapAction` dispatches every entry to a `TUnitMap_*` method [derived: code, `0x0045B810`]. Every Army
order acts on the selected army. If a fleet is selected instead, it acts on the army that fleet carries
[derived: code, the prologue of each `TUnitMap_*` army order].

**The second unit is picked by the game.** Join armies and Transfer unit take the selected army's partner
from `FUN_00449D64`. That function returns the last own army at Chebyshev distance **exactly 1**
(`FUN_004492A0` is `distance == 1`). Join fleets and Transfer ships use `FUN_00449DD8`, the same for own,
launched fleets [derived: code]. A research read has since confirmed `FUN_00449D64` as Transfer unit's gate; the dialog then takes the highest-index own army at distance 1, preferring an empty one, which is how Split army reuses the same dialog **[confirmed: code, [`2026-10-03-army-to-army-ok-supply-rebalancing.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-03-army-to-army-ok-supply-rebalancing.md) item 5 and "The partner"]**. The player never picks the partner. The engine's `AdjacentPartner` ([T106](https://github.com/diegoami/imperial_conquest_2/issues/564)) implements both picks.

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| Army → Supply army | `TAFSupply`: buy supply from a city or an own fleet within one tile (free from one's own, paid from a foreign city). `TAFSupply_ChangeMoney` moves money between the treasury, or a co-located fleet, and the army, capped at 1,000 [confirmed: `decompiled-unit-map-orders-and-record-fields.md`, `supply-capacity-rounding.md`]. | `buy` (`BuySupplyCommand`, from a city or a fleet). The money transfer is the `transfer-money <army> <amount> [via <fleet>]` verb ([T105](https://github.com/diegoami/imperial_conquest_2/issues/563)): to or from the treasury, or a fleet's purse within one tile | The city panel's **Transfer Supply** slider (0–50 t, to any own army) | **partial** | — |
| Army → Recruit mercenaries | `TRecruitMercs`: only from the offers of the first city at distance exactly 1. With none, nothing happens. The hire is paid from the army's purse [confirmed: `decompiled-mercenary-offer-list-and-position.md`, `decompiled-unit-map-orders-and-record-fields.md`]. | `hire-mercenary`. It can never succeed today: the pool is empty and has no position (`CommandVerbCatalog.ConfirmedUnreachable`; #325, #457; T76). | none | **partial** (the engine is blocked on T76) | — |
| Army → Transfer unit | `TArmyToArmy`: two unit lists with Transfer/Disband under each, and supply and money spinners. Conservation is exact [confirmed: `army-to-army-transfer-confirmed.md`]. `OK` refuses nothing: it caps the selected army's supply at `troops div 100` and pushes the excess to the partner, then the partner's back, so any surplus stays on the selected army; money is not rebalanced [confirmed: code, `2026-10-03-army-to-army-ok-supply-rebalancing.md`]. The partner is the adjacent own army (above). | `army-transfer <from> <to> [units=<i,j,…>] [supply=<tons>] [money=<talents>]` (`ArmyTransferCommandHandler`, [T106](https://github.com/diegoami/imperial_conquest_2/issues/564)); the partner pick is `AdjacentPartner.Army`. It refuses supply past the dialog cap where the original rebalances: bug [#619](https://github.com/diegoami/imperial_conquest_2/issues/619) | none | **UI-only missing** | — |
| Army → Split army | `TSplitArmyUnit`: needs 2 or more units; the 198-army cap; units, supply and money are allocated by hand [confirmed: `decompiled-unit-map-orders-and-record-fields.md`, `ptolemy-run-ui-inventory-and-leader-draw.md` §4]. The new army stands on an **adjacent tile, one step diagonally (+1, +1)**, not on the parent's tile **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. | `split-army`, which puts the new army on the parent's tile (`SplitArmyCommandHandler.cs` ~:116, [#584](https://github.com/diegoami/imperial_conquest_2/issues/584)) | none | **UI-only missing**. The engine's placement differs from the original. | Which tile the game picks when (+1, +1) is blocked (`FUN_00449F08`'s position argument) |
| Army → Join armies | Joins with the adjacent own army: 20 units and 100,000 troops at most, neither aboard a fleet, and the survivor's moves are zeroed [confirmed: the same report]. | `join-armies`, but its gate is **same tile** (`JoinArmiesCommandHandler.cs:53`) | none | **UI-only missing**. The engine gate differs from the original (§4). | — |
| Army → Change units | `TChangeArmyUnits`: **rename, split, join and disband** single units [confirmed: `decompiled-unit-map-orders-and-record-fields.md`; methods `RenameUnit`, `SplitUnit`, `JoinUnits`, `Disband`, `RemoveUnit`]. | `join-units`, plus `split-unit <army> <unit> <troops>`, `rename-unit <army> <unit> <name>` and `disband-unit <army> <unit>` ([T107](https://github.com/diegoami/imperial_conquest_2/issues/565)). Their minimum troops, longest name, and last-unit and refund rules are `[designed]` defaults pending an EXPLORE experiment. `RemoveUnit` is not built: the transfer report reads it as a list-display helper (`army-to-army-transfer-confirmed.md`). | none | **UI-only missing** | The original's split minimum, name length, last-unit disband and refund (the `[designed]` defaults on [#565](https://github.com/diegoami/imperial_conquest_2/issues/565)) |
| Army → Disband army | Only next to one's own city; asks for confirmation; money goes to the treasury and supplies to the city [confirmed: the same report; `attack-and-siege-are-adjacency-orders.md`]. | `disband-army` | The army panel's **Disband** | **present** | — |
| Fleet → Supply fleet | `TAFSupply` against a city or another fleet [confirmed: the same report]. Observed against an adjacent own city: the window "Supply fleet" lists the providers ("Supplies at Antium: 330"), the fleet's supplies, the national balance and the fleet's money, each with 10s and 100s arrows; one 100s press moved 100 tons from the city to the fleet **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. | `buy-fleet-supply`. The money transfer is the `transfer-money <fleet> <amount> [via <fleet>]` verb ([T105](https://github.com/diegoami/imperial_conquest_2/issues/563)), as for armies | none | **partial** | — |
| Fleet → Repair fleet | Only at an own city, not while carrying an army; costs `ships × points / 5`; zeroes the fleet's moves [confirmed]. Observed next to an own city: the dialog shows the original state of repair, 1s and 10s arrows, the new state and the cost; 3 points on 30 ships cost 18 and set the moves to 0. Next to a foreign city it says *"The fleet can only be repaired at one of your cities."* **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. | `repair-fleet` | The fleet panel's **Repair** | **present** | — |
| Fleet → Transfer ships | `TFleetToFleet`: ships, supply and money, both ways [confirmed]. The partner is the adjacent own fleet (above). Observed between two own fleets one tile apart: the window "Fleet to fleet transfer" has Split fleet's layout, and 20/10 became 15/15. With no own fleet adjacent, nothing happens **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. | `fleet-transfer`, with a **same-tile** gate (`FleetToFleetTransferCommandHandler.cs:65`) | none | **UI-only missing**. The engine gate differs (§4). | — |
| Fleet → Split fleet | Needs 20 or more ships; not while carrying an army [confirmed]. The dialog is a two-column table of the first and the second fleet's ships, supply and money, with 1s and 10s arrows for ships and 10s and 100s for supply and money; the down arrows move to the second fleet. The new fleet stands on an **adjacent tile** with 0 moves: a 20 + 10 split at (101,46) put it at (101,47) **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. | `split-fleet`, which puts the new fleet on the parent's tile (`SplitFleetCommandHandler.cs` ~:63, [#596](https://github.com/diegoami/imperial_conquest_2/issues/596)) | none | **UI-only missing**. The engine's placement differs from the original. | Which neighbour the game picks (one observation) |
| Fleet → Join fleets | Joins with the adjacent own fleet. The combined fleet must hold fewer than 101 ships (`< 0x65`) and neither may carry an army [confirmed: the same report; the `< 0x65` test is derived: code, `TUnitMap_JoinFleets` `0x00447A48`]. Observed with an own fleet one tile away: one click, no dialog, the ships add and the moves become 0. With no own fleet adjacent, nothing happens **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. | `join-fleets`, with a **same-tile** gate (`JoinFleetsCommandHandler.cs:68`). For the cap, see [#166](https://github.com/diegoami/imperial_conquest_2/issues/166). | none | **UI-only missing**. The engine gate differs (§4). | The Fleet submenu was read from form data only [the inventory] |
| Fleet → Scuttle fleet | Next to an own city, not while carrying an army, with confirmation [confirmed]. The prompt is *"Are you sure you want to scuttle this fleet ?"* with Yes, No and Cancel, and Yes removes the fleet. Away from an own city it says *"To scuttle a fleet it must be near one of your cities."* **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. | `scuttle-fleet` | The fleet panel's **Scuttle** | **present** | — |
| City → Fortify city | `TFortifyCity`: 0 … (100 − current) points at `population(k) × points`. It is refused under siege or when already fortifying [confirmed: the same report]. | `order-city … fortify` | The city panel's **Order Fortification** | **present** | [#549](https://github.com/diegoami/imperial_conquest_2/issues/549) (the treasury gate) |
| Cancel selection | `TUnitMap_EndUMSelection`: clears the selected army, fleet and city and switches every unit-map button off [derived: code, `0x00446EAC`]. Shortcut **Shift+X** [confirmed: `ptolemy-run-ui-inventory-and-leader-draw.md` §4]. Shift+X drops the selection and empties the unit strip, and the information panel **keeps showing** the army **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. | — | Unit map → **Cancel selection**, from the menu bar (`GameMenuBar`, [T100](https://github.com/diegoami/imperial_conquest_2/issues/558)); the tooltip shows Shift+X. Shift+X (through the command table) and Esc clear the selection only when no overlay is open (`MainGameScreen._UnhandledInput`), and the panel switches to the nation overview ([#583](https://github.com/diegoami/imperial_conquest_2/issues/583)) | **partial** | — |

### 1.7 Help

| Original entry | What it does in the original | Engine today | UI today | Status | Evidence gaps |
| --- | --- | --- | --- | --- | --- |
| Help topics | Opens the WinHelp file `Imperial Conquest 2.HLP` [derived: code, `TPremierForm_HelpTopics` `0x0045C280` → `Application.HelpCommand`; the file is confirmed by `impconq2-initial-report.md`] | — | Help → **Help topics**: a short in-game help page of the clone's own (`HelpPage`) [designed], never the original's help file | **present** (new text) | — |
| Show hints | Toggles tooltip hints (`Application.ShowHint`), the menu check mark, and a per-nation flag (nation `+0x46B`) [derived: code, `TPremierForm_ToggleHints` `0x0045C294`]. It was checked in the recording [confirmed: the inventory]. | — | Help → **Show hints**: turns tooltips on every toolbar button and menu-backed control on and off, with a check mark; on by default, kept for the session | **present** | — |
| About Imperial Conquest | The `TAboutIC` dialog [confirmed: the inventory] | — | Help → **About Imperial Conquest**: the game's name, version and a line saying it is a re-creation of the 1996 game (`AboutDialog`) | **present** | — |

### 1.8 Keyboard shortcuts found in the code

`TPremierForm_KeyPressed` (`0x0045B950`) is shared by the main form, the Unit map and the Area map. With one
modifier state, **N** shows the viewed nation's status panel. With another, **A** shows armies, **C** cities,
**F** fleets, **L** all, **N** the player's own nation, and **Q** capitals [derived: code]. The two modifier
constants (`DAT_0045BA64`, `DAT_0045BA68`) are data the dump does not resolve **[open]**. The menu-item
shortcuts, of which only Shift+X is observed, are in the form stream **[open]**.

## 2. The map-click model

### 2.1 How the original turns a click into an order

The original has **two map windows** [confirmed: the inventory; `2026-09-29-nation-view-origin-and-unit-map-clicks.md`]:

- the **Area map**, an overview of the whole world. A click there only moves the Unit map's view (§1.5);
- the **Unit map**, 32-px tiles over a 30-px strip. Every order is given here.

A click on the Unit map (`TUnitMap_UnitMapClick` `0x00446420`) reads the map word under the cursor
[confirmed: the same report] and then:

1. **Terrain** (codes below 20) → `TUnitMap_CheckForMove` (below).
2. **A city, army or fleet marker** (20–99, 200–247, 300–347): first the information window shows it. The left
   button shows its details (`ShowCityDetails`/`ShowArmyDetails`/`ShowFleetDetails`); the right button shows
   its unit list (`ShowCityUnits`/`ShowArmyUnits`/`ShowFleetUnits`). Then `TUnitMap_SelectUnit` runs [derived:
   code. Reading the third parameter as the mouse button follows Delphi's `OnMouseDown(Sender, Button, Shift,
   X, Y)` register convention, which also accounts for the report's `X div 32`, `(Y − 30) div 32`]. On an own,
   selected army, a left click shows its details and a right click replaces them with its unit list (name, type,
   troops and quality per unit), in the same panel; the army stays selected and no window opens **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**.

`TUnitMap_SelectUnit` (`0x004466CC`) decides, from what is already selected and what was clicked [derived:
code; the prompts and the war declaration are confirmed: `decompiled-diplomacy-peace-terms-and-instant-battles.md`]:

| Selected | Clicked | Result |
| --- | --- | --- |
| an own army **with moves ≥ 1, at distance exactly 1** | an enemy **city** | If not already at war, it asks *"Are you sure you want to attack this city ?"*, with the buttons Cancel, No and Yes. On yes it declares war (`FUN_00449B40(…, 3)`), then **besieges** (`FUN_0044B27C`), clears the selection and prints the news. If the army is not adjacent or has no moves, the selection is dropped. **On no, nothing changes: the relation, the army's tile, its moves and the selection stay as they were** **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. At war there is no box and the siege resolves on the click; at peace, Yes writes the war (relation 0 → 3, the news line) and the siege follows on the same click **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. Cancel was not tried on a city; on a fleet it changes nothing, like No (the fleet row below). |
| an own army, as above | an enemy **army** | The same, with *"…attack this army ?"*, then **field battle** (`FUN_0044AEE4`). Not observed on an army. |
| an own army, as above | an own **fleet** carrying no army | **Embark**, if `ships ≥ troops / 500`. Otherwise *"The army is too large for this fleet ?"*. Then the fleet becomes the selection. Observed from the tile next to the fleet: the army moves onto the fleet's tile and both units' moves become 0. Over capacity (10,700 troops, 20 ships), the box has **OK only**, the army stays where it was with its moves, and the click selects the fleet **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. |
| an own fleet with moves ≥ 1, at distance 1 | an enemy **fleet** | *"…attack this fleet ?"*, then **naval battle** (`FUN_0044B5D0`). The game refuses with *"You cannot attack a fleet docked at its own city !"* when the target is docked at one of its own cities. Observed at war: there is no box, the battle resolves on the click with no window, the selection is cleared, the attacker's moves become 0, the loser's fleet is destroyed, and the next turn's news reads *"Carthage sinks fleet of Ptolemaic."* **[Wine candidate: [`2026-10-02-naval-battles.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-naval-battles.md)]**. A second run observed the docked refusal: with the target fleet diagonal to its own city (Issus), the click opened the refusal box (its text read garbled) and nothing changed, and the same attack 3 tiles from the city was allowed, so "docked" is within one tile of an own city **[Wine candidate: [`2026-10-03-pair-2-seleucid-ptolemaic.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-03-pair-2-seleucid-ptolemaic.md)]**. A third run tried the prompt at trade terms (relation 1): the box has Yes, No and Cancel, and No and Cancel change nothing (both fleets and both relation entries). Yes writes war on both sides (1 → 3) and on the target's ally (Numidia, 0 → 3), not on a nation on trade terms with both; the battle resolves on the same click, with the result the at-war run gives, seed for seed; and the next news reads *"PTOLEMAIC DECLARES WAR ON CARTHAGE."*, *"PTOLEMAIC DECLARES WAR ON NUMIDIA."*, *"Carthage sinks fleet of Ptolemaic."*, in that order **[Wine candidate: [`2026-10-03-fleet-peace-prompt.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-03-fleet-peace-prompt.md)]**. The prompt at peace (0) or against an ally, and a peace-time attack on a docked fleet, were not tried. |
| anything | an **own** army, fleet or city | It becomes the selection, and the unit map's button strip switches to its orders (§3.3) |
| anything | an enemy unit or city that cannot be attacked from here | The selection is cleared and only the information panel changes |

`TUnitMap_CheckForMove` (`0x00446CA4`), on a terrain click [derived: code]:

| Selected | Clicked terrain | Result |
| --- | --- | --- |
| an own army | land (code 2 or more) | **Move**: a Bresenham walk toward the tile [confirmed: `decompiled-army-movement-and-river-cost.md`]. A second walk follows if moves remain short of the tile. **The army stays selected while it has moves left**, and the selection ends when they reach 0 (`TUnitMap_MoveHumanArmy` `0x00446D9C`). Observed for one army: 8 moves → 4 stays selected, 4 → 0 is deselected, with no box **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**. |
| an own fleet | sea (0 or 1) | **Move fleet**, the same pattern (`TUnitMap_MoveHumanFleet` `0x00446E24`). Observed: a sea tile two away, 29 moves → 27 **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. |
| an own fleet **carrying an army** | land (2–11) at distance 1 | **Disembark** onto that tile (`FUN_0044B840`). The selection is cleared and the army's details are shown. Observed: the army lands on the clicked tile, **both units' moves become 0**, and both selection variables are cleared **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**. The clone's verb and click name no landing tile ([#594](https://github.com/diegoami/imperial_conquest_2/issues/594)), and its disembark leaves the fleet's moves ([#595](https://github.com/diegoami/imperial_conquest_2/issues/595)). |

Three consequences, all **[derived: code]**:

- **A fleet cannot besiege a city or attack an army.** The city and army branches test only the selected
  *army*. This settles the question `attack-and-siege-are-adjacency-orders.md` left open ("whether a fleet can
  besiege a coastal city").
- **Clicking an own city with an army selected does nothing to the army.** The army is deselected, and the
  city and its Fortify button are selected. No order moves an army onto a city tile, which agrees with the
  report above [confirmed].
- **Attack confirmation is part of the click**, and it is skipped when already at war. Both are observed for a city **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**, and for a fleet **[Wine candidate: [`2026-10-02-naval-battles.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-naval-battles.md), [`2026-10-03-fleet-peace-prompt.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-03-fleet-peace-prompt.md)]**.

**Cancel selection** is the menu entry or Shift+X (§1.6). The selection also ends by itself after an attack,
a disembark, or an army or fleet running out of moves. It does not end on No at the attack prompt (above).

### 2.2 What the clone does today

- **`GameMapView`** (`godot/UI/GameMapView.cs`) is one zoomable, pannable map (wheel zoom, drag pan). With no
  order pending, a left click selects the army, the fleet or the city on that tile, in that priority, and an
  empty tile clears the selection (`HandleClick`, ~:350). There is no right-click behaviour.
- An order needs a **button first**. The army panel's **Move** and **Attack** buttons arm `BeginMoveOrder` or
  `BeginAttackOrder`. The next click then submits `move <army> x y`, or `attack-army` against an army or
  `besiege-city` against a city on that tile. Anything else reports "No valid target at that tile."
  (`ResolvePendingAction`, ~:390). The army is re-selected after the order, but the order is disarmed, so each
  further move needs the button again.
- **Fleets have no map orders.** No UI reaches `move-fleet`, `attack-fleet`, `embark-army` or
  `disembark-army`. The engine's embark is also unreachable in play
  ([#453](https://github.com/diegoami/imperial_conquest_2/issues/453), planned as [T93](https://github.com/diegoami/imperial_conquest_2/issues/456)).
- **No confirmation before an attack.** `GameSession` puts a `declare-war` in front of an attack on a nation it
  is not at war with (`ComposeDeclareWarIfNeeded`, `GameSession.Commands.cs` ~:199), and does not ask first.
- **No fog of war on the panel.** `ContextPanel.BuildArmyPanel` prints moves, morale, money and supply for
  every army. The original withholds those four for a foreign army
  [confirmed: `ptolemy-run-ui-inventory-and-leader-draw.md` §5].
- **The context panel** (`godot/UI/ContextPanel.cs`) carries the information *and* these actions. City:
  Recruit, Order Fortification, Transfer Supply. Army: Move, Attack, Mobilize first ready slot, Disband.
  Fleet: Repair, Scuttle. Nation overview: none.

### 2.3 What is unknown

- Whether the left and right buttons act on mouse-down or mouse-up. The mapping itself is observed on an own,
  selected army **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**; a click on an unselected own army, an enemy army or a city was not tried.
- The adjacency metric is settled: Chebyshev, by `FUN_00449018` [confirmed:
  `attack-and-siege-are-adjacency-orders.md`]. "Exactly 1" for the attack, embark and partner tests is
  derived from code read for this audit.
- Whether an own army with 0 moves can be selected. A fresh split army, with 0 moves, could not be selected
  that turn **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**; whether that holds for any army at 0 moves is not observed.
- What the information window shows when an army is selected and an enemy army it cannot reach is clicked.
  The code shows the target's details, but whether anything marks the order as refused is not observed.
- Whether the Unit map scrolls by edge, by scroll bars or by keys. The form has scroll bars (`UnitMapScrolled`),
  and the rest is not read.

## 3. The toolbars

The inventory says the toolbar icons are shortcuts to the same menu commands, and confirms named speed buttons
for Open, Save, End turn, the six Strategy entries, the 16 nations and All nations [confirmed: the inventory].
The other rows were counted from screenshots in [asset-specification.md §4.7](../asset-specification.md#47-chrome-with-no-key-today--the-main-screen-dialog-and-battle-result-gaps)
(T24 rework), and from code read for this audit.

### 3.1 Main toolbar

| Group | Icons | Source |
| --- | --- | --- |
| File and Game | Open, Save, End turn | [confirmed: the inventory] |
| Strategy | News, International relations, Taxation, Balance sheet, Recruit unit, Build fleet | [confirmed: the inventory]. The field pairs in `StrategicDecision` follow this order [derived: code]. |
| Nations | 16 nation buttons, coloured, plus All nations: a radio group | [confirmed: the inventory, `rome-city-recruitment-and-nations.md`]. The pressed-button behaviour is [derived: code, `SelectNation`]. |

The order *across* groups, and any separators, are **[open]**. The form stream has them; the inventory did not
decode the speed-button geometry.

### 3.2 Area-map toolbar

The asset specification counted **13 buttons** in `1_rome_270_summer_7_1.png`: a multicoloured map/palette
icon; a house; a columned temple; a soldier; a ship; a soldier with a house; five geometric symbols (`+`, `×`,
`#`, a diamond, a circle); a combined multi-symbol icon; and a gold coin. Matched to the menu, in the same
order, these are [derived: the order matches the Area map menu, and the five symbols match `ShowMercs`
drawing a distinct bitmap per type]:

ToggleMap · Show cities · Show capital · Show armies · Show fleets · Show all · LI · HI · Ar · LC · HC
mercenaries · All mercenaries · **gold coin = [open]** (Find a city is the only entry left, but a coin is an odd
icon for it).

### 3.3 Unit-map toolbar: a context strip

Code read for this audit settles its shape [derived: code, `TUnitMap_AllButtonsOff` `0x004460A8`,
`ArmyButtonsOn` `0x00446128`, `FleetButtonsOn` `0x00446188`, `CityButtonsOn` `0x00446268`]:

- **7 army buttons**, **6 fleet buttons** and **1 city button**, one per Unit map submenu entry. Only the
  selected unit's group is enabled and shown. The strip resizes to fit it: 174 px for an army, 150 for a fleet,
  30 for a city.
- **A fleet carrying an army shows both groups**: the 6 fleet buttons, then the 7 army buttons after them
  (324 px).
- **Nothing is shown for a foreign unit** (`AllButtonsOff`).
- The pitch is 24 px per button, which puts the original's icons near 20–22 px.
- `AllButtonsOff` loops over **15** buttons, one more than 7 + 6 + 1. With a fleet selected, the strip's
  tooltips name the six fleet orders and **Cancel selection** **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**, so the 15th is
  most likely Cancel selection. Which icon is which was not recorded.

The asset specification counted **7** icons on the fleet strip in `1_cartago_271_spring_3_1.png` and read
them by shape as load-army, repair, join, split, build/add, scuttle and a white circle. The code says six fleet
orders: Supply, Repair, Transfer ships, Split, Join, Scuttle. The likely match is: the "ship with cargo" is
Supply fleet, and the "ship with a plus sign" is Transfer ships. The seventh, white circle is outside the
fleet group, most likely the 15th button above, Cancel selection [Wine candidate, above].

### 3.4 Battle toolbar

**In scope for v0.6.0 *Battles* (v0.5.0 until the user's decision of 2026-10-04)** ([game-design.md](../game-design.md), Combat, "The tactical battle", and
"User interface", item 3; T127 builds the screen). The battle window has no menu and a toolbar of **eight
buttons**, in this order: *Unit moves*, *Friendly units*, *Enemy units*, *Cancel selection*, *End turn*,
*Change pauses*, *Computer general on*, *Surrender* **[confirmed: code and the `TBATTLEMAP` resource, static,
[`2026-10-04-decompiled-tactical-battle-rules.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-decompiled-tactical-battle-rules.md)
§10; Wine candidate: a tooltip scan of the running battle window found the same eight in the same order,
[`2026-10-04-battle-probe.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-battle-probe.md)
item 1]**. The main window's toolbar *Save* does nothing during a battle; *File → Save As* writes a save
with the battle block **[Wine candidate: the same report, item 2]**.

### 3.5 Asset needs

The two shipped packs (`assets/packs/placeholder/manifest.json`, `assets/packs/authored/manifest.json`)
hold `unit.*.icon`, `army|fleet.tier1–3.icon`, `city.tier1–3|capital.icon`, `terrain.*.tile`, `sfx.*` and,
since [T101](https://github.com/diegoami/imperial_conquest_2/issues/559), 36 `ui.command.<id>.icon` toolbar keys,
catalogued in [asset-specification.md §4.7](../asset-specification.md#47-chrome-with-no-key-today--the-main-screen-dialog-and-battle-result-gaps)
(which explains how its per-strip split differs from the table below). On this audit's count the need is:

| Toolbar | Pictorial icons | Notes |
| --- | ---: | --- |
| Main | 9 | Open, Save, End turn and the 6 Strategy entries. The 17 nation buttons can be colour swatches from the nation palette ([T97](https://github.com/diegoami/imperial_conquest_2/issues/532)) with no new art **[designed]**. |
| Area map | 13 | Including ToggleMap and the coin, both **[open]** in meaning |
| Unit map | 14 (+1 [open]) | 7 army, 6 fleet, 1 city |
| **Total** | **36 (+1)** | The size is 32 × 32, under §4.7's **[designed]** chrome rule. The original's own art cannot ship, so every icon is authored. The placeholder pack needs a generated stand-in per key. |

## 4. A defect class this audit found

Join armies, Join fleets and Transfer ships each take their partner at Chebyshev distance **exactly 1** in the
original (§1.6, `FUN_00449D64`/`FUN_00449DD8`). The clone's commands require the **same tile**:
`JoinArmiesCommandHandler.cs:53`, `JoinFleetsCommandHandler.cs:68` and `FleetToFleetTransferCommandHandler.cs:65`.
This is the gate that made embarking unreachable ([#453](https://github.com/diegoami/imperial_conquest_2/issues/453)).
The clone's army walk refuses a tile with another army on it (`MoveArmyCommandHandler.IsBlocked`, ~:86), so
in play the only way two armies share a tile may be a fresh split. The original's split places the new army one
step diagonally, at distance 1 **[Wine candidate: [`2026-10-02-unit-map-mouse-orders-and-tax-range.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-unit-map-mouse-orders-and-tax-range.md)]**, so its partner is in reach at once; the clone's split puts it on the
parent's tile ([#584](https://github.com/diegoami/imperial_conquest_2/issues/584)). Split fleet is the same: the original puts the new fleet on an adjacent
tile, and Join fleets and Transfer ships then work between the two **[Wine candidate: [`2026-10-02-fleet-orders-live.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-02-fleet-orders-live.md)]**; the clone puts it on the parent's
tile ([#596](https://github.com/diegoami/imperial_conquest_2/issues/596)). This audit does not file the defect. Under
[build-process.md §4.6](../build-process.md#46-bugs-and-follow-ups) it goes to the bug list, and its fix may
fold into T93's adjacency change.

## 5. Evidence gaps, collected

Things the reports and this audit do not settle. Each is marked **[open]** above where it bears on a row.

1. **Promote the code reads.** Every **[derived: code]** row in this document comes from the local dump and
   has not been checked by a research session. The highest-value reads are `TUnitMap_SelectUnit`,
   `CheckForMove`, the button-strip functions and `TBalanceSheet_PaintBalance`. Two have since been read: the
   army partner gate `FUN_00449D64` with the whole `TArmyToArmy` dialog, and the End-turn gate
   (`2026-10-03-army-to-army-ok-supply-rebalancing.md`, `2026-10-03-end-turn-warning-box.md`).
2. **The form stream.** It holds the Balance sheet's line captions, the toolbar
   order and separators, the menu shortcuts (only Shift+X is observed), and the two key-handler modifier
   constants.
3. What `TAreaMap_ToggleMap` toggles, and what the Area-map gold coin is. The unit-map strip's 15th button is
   most likely Cancel selection [Wine candidate, §3.3].
4. What a foreign nation's status panel withholds.
5. What disbanding a recruitment slot in `TArmyRecruits` refunds.
6. Which tile `FUN_00449F08` gives a split army when (+1, +1) is blocked. The one observed split, and a second
   consistent datum, put it at (+1, +1) [Wine candidate]. One fleet split put the new fleet at (+0, +1), where
   (+1, +1) is land [Wine candidate]; whether fleets share the army's placement code is not read.
7. The desktop confirmation of the two Wine runs' results (the click order, the left/right-button reading,
   the confirmation prompt and its No; embark, disembark, the fleet's move and the Fleet entries; the fleet's
   prompt and its war on the target's ally), and the rows no run reached: an attack on an army, Cancel on a
   city's prompt, and a peace-time attack on a docked fleet or on an ally. A naval battle at war, the docked
   refusal and the fleet's prompt at trade terms are observed under Wine (§2.1).
