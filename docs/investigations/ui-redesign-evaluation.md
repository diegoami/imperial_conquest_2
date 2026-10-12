# The UI's look, evaluated, with font and palette candidates (T168)

**Task:** [T168](../tasks/T168.md), issue [#1072](https://github.com/diegoami/imperial_conquest_2/issues/1072), planned from [#899](https://github.com/diegoami/imperial_conquest_2/issues/899) and [#1070](https://github.com/diegoami/imperial_conquest_2/issues/1070). [T169](../tasks/T169.md) applies what the user chooses here. **Nothing in the game changes in T168.**

**The user's words** (2026-10-12): *"most important is the font refont (too impersonal and not compact enough)"*. From #899 (2026-10-09): the army panel's font is too large and its line spacing too loose; an ancient-looking font, more font variety, a reworked colour scheme. From #1070: the transfer dialog's font is too big.

Tags follow `design-audit.md`: **[confirmed]** (measured or seen in a screenshot this task rendered, or stated by a cited report), **[derived]**, **[designed]** (a proposal, for the user to decide), **[open]**.

**How to read this:** the screenshots are not committed (original-game material never enters the repository, and renders stay under the git-ignored `rendered/`). The main session posts them for the user; their file names are listed below and in each table. Everything the document says about a screenshot is what I saw in the file of that name.

## 1. The screens as they stand

### 1.1 How the UI is built today [confirmed]

- No project theme, no font file: every label uses Godot's built-in default font (a plain humanist sans-serif, no inscriptional or period character). `godot/project.godot` sets no `gui/theme/*` entry, and `godot/` contains no font asset before this task.
- `godot/UI/UiKit.cs` is the one shared helper: `MakeLabel(text, fontSize = 16, colour)` sets a per-label `font_size` override, `MakeButton` has a **40 px minimum height**, `MakePanel` has **12 px side and 10 px top and bottom margins**. Sizes found in the code: 12 (notes), 14 (facts and table text), 15-18 (list rows, dialog headers), 20 (panel headings), 32 (the main-menu title). Panels build one `Label` per line inside a `VBoxContainer` whose default separation is 4 px (`ContextPanel.Fact`, `Heading`, `Note`, `Blank`).
- Colours today (`UiKit`): background `#121C26`, panel `#1C2938`, raised panel `#263647`, text `#EDF0EB`, muted `#ADB5BD`, accent gold `#DBA845`. A dark blue-grey with one gold accent; nothing in it refers to the ancient setting.

### 1.2 The measured compactness complaint [confirmed]

In `rendered/candidates/00-baseline.png` (the baseline sample, same layout as the proposals, today's sizes and the default font) one army-panel line costs **about 23 base px** (30 px apart in the 2000 px preview, base viewport 1500 px, so a factor of 1.333): a 14 px font whose natural line height is about 1.36 em (19 px) plus the 4 px container separation. That is what the user sees as "too large and too loose". The real panel in `rendered/asis2/asis-10-panel-army.png` shows the same rhythm (about 38 px between lines in the 2000 px preview).

### 1.3 Screen by screen [confirmed from the named screenshot]

The criteria are the task's: hierarchy, legibility, consistency, compactness, and where the app reads as modern rather than ancient. "Pre-existing" tours are `ScreenshotTour`, `EndTurnBoxScreenshotTour`, `SaveResumeScreenshotTour` and `ScreensScreenshotTour`; the "as-is" tour is `FontCandidatesTour` run with `IC2_TOUR_MODE=asis` (a fixture game: the classical world, Rome human, two adjacent Roman armies and one Roman fleet added; it opens the real dialogs through the menu, the way the checks do).

| Screen / dialog | Screenshot (under `rendered/`) | Judgement |
| --- | --- | --- |
| Main menu | `asis/01-main-menu.png` | Hierarchy is clear (gold title 32 px, muted subtitle, four equal buttons) but the title is set in the default sans, the buttons are flat dark rectangles with no border or ornament, and the whole screen is empty space. Reads as a generic app launcher, not an ancient empire game. |
| Ruleset chooser | `asis/02-ruleset-chooser.png` | Consistent with the menu (same panel, same buttons). Legible. No hierarchy between a preset's name and its description. |
| Scenario / seat step | `asis/03-scenario-seat.png` | Same look; long lists in the default font at 14-16 px. Legible, uniform, impersonal. |
| Main game screen, city panel | `asis/04-main-game-screen.png` | The map dominates, and the right panel is cramped by comparison: its lines are 38 px apart in the 2000 px preview (about 28 base px with the heading margins), the news log below is a full-width band with 29 px between lines. The menu bar and the toolbar icons are the original's (icons) in the default sans (menu). The panel's heading, gold 20 px, is the only hierarchy. |
| Army panel | `asis2/asis-10-panel-army.png` | The user's complaint, visible: 14 px facts with about 23 px pitch, a blank 8 px label between groups, and the panel shows only about eleven lines before it scrolls. |
| Fleet panel | `asis2/asis-11-panel-fleet.png` | Same pitch and the same issue. |
| City panel (fixture) | `asis2/asis-12-panel-city.png` | Same. |
| Army to army transfer (#1070) | `asis2/asis-32-dialog-army-transfer.png` | A 760 px dialog: two list boxes of 14-16 px rows, a four-button row under each side, a Supply row of `-100 t`, a spinner and `+100 t` buttons, a Money row, Cancel and OK. Buttons are 40 px tall, so every row of controls is tall and the dialog fills a third of the screen for a handful of numbers. List rows show the raw slot comment (`tour-a unit 1`) and a truncated name (`tour-a un...`). The Supply and Money rows do not say from which army to which (the content problem of #1070's other points, a UI task of its own, not a font problem). The font size and the tall buttons contribute; the sample dialog in `rendered/candidates/` shows the same content at the proposed scale (§4). |
| Recruit unit (Army recruits) | `asis2/asis-22-dialog-recruit-unit.png` | Hierarchy fine (gold header, tab-like buttons `Lit inf`, `Hvy inf`...). Raw identifiers are shown to the player (`light_infantry — 5500 troops — 4 weeks`), a content problem; the 40 px buttons and the 14-16 px rows make the list loose. |
| Taxation | `asis2/asis-20-dialog-taxation.png` | A small dialog in the same components; consistent with the rest (not judged further). |
| Balance sheet | `asis2/asis-21-dialog-balance-sheet.png` | A two-column list of figures at 14/15 px (`BalanceSheetDialog.cs` sets 14, and 15 for the total, so the total is emphasised by size only). The right column is a column of figures in the default font (whether that font's digits are tabular was not checked [open]), so it is where tabular figures matter most. |
| Build fleet | `asis2/asis-23-dialog-build-fleet.png` | Same family as Recruit unit. |
| Find a city | `asis2/asis-24-dialog-find-city.png` | Search box and list in the default look. |
| Split army | `asis2/asis-33-dialog-army-split.png` | Same two-list pattern as the transfer dialog. |
| Change units | `asis2/asis-34-dialog-army-change-units.png` | Same. |
| Fleet supply, Repair, Split fleet | `asis2/asis-40-dialog-fleet-supply.png`, `asis-41-dialog-fleet-repair.png`, `asis-43-dialog-fleet-split.png` | Same components. (Fleet transfer needs two adjacent fleets and did not open on the fixture; Join armies opens no dialog; Recruit mercenaries needs a city; none is captured.) |
| Fortify city | `asis2/asis-50-dialog-fortify-city.png` | A one-control dialog; the header-to-body hierarchy is the only structure. |
| International relations | `asis/03-diplomacy-grid.png`, `asis2/asis-60-menu-nations-relations.png` | A header, a six-column grid whose column titles are muted 14 px and whose buttons (`Declare War`, `Make Peace`, `Ally`, `Trade`) are 12 px on 40 px-high flat rectangles: the buttons' text is smaller than the row's text, an inconsistency. |
| Battle result | `asis/01-battle-result-destroyed.png`, `asis/02-battle-result-scattered.png`, `asis2/` (a battle result also opened by accident in an early run) | Eight 14 px lines of text with 35 px between them: loose and flat; the winner line differs only by weight. |
| Hotseat hand-off | `asis/04-hotseat-handoff-not-blind.png`, `asis/05-hotseat-handoff-blind.png` | Full-screen opaque card; large centred text; fine. |
| End of turn box | `asis/01-one-line.png`, `asis/02-five-lines.png` | Warning lines at 14 px in the muted colour on the panel colour; legible, loose pitch. |
| Save / Load | `asis/01-main-game-screen-save-control.png`, `asis/02-save-confirmation.png`, `asis/03-load-screen-listing-a-real-save.png`, `asis/04-resumed-main-game-screen.png` | The Load list is plain rows in the default font; consistent. |
| News log | the lower band of `asis/04-main-game-screen.png` | Full-width, 29 px pitch in the preview. |

**Summary [confirmed]:** the UI is consistent (one helper builds almost everything) and legible (contrast is high, §3), but it is **impersonal** (one default font, one flat dark blue-grey, no ornament, no period reference anywhere in type or colour) and **loose** (a 1.36 em default line height plus container gaps plus 40 px buttons). It reads as a modern utility, not an ancient one. The two cheapest, biggest changes are the typeface and the line pitch.

### 1.4 Against the original [confirmed from the cited reports; I did not view the original's images]

The original's recordings and screenshots are release assets of the research repository, not files in it, so these claims are the cited reports' own statements, and I make no claim about pixels I did not see.

- The original is a Delphi program whose dialogs are standard Windows forms: the dialog scan of 22 recordings finds a dialog because "a Windows dialog is bright and near-grey" against the saturated map ([ptolemy-run-ui-inventory-and-leader-draw.md §1](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/ptolemy-run-ui-inventory-and-leader-draw.md)). It has 29 forms and 908 controls ([2026-10-05-player-facing-feature-inventory.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-05-player-facing-feature-inventory.md)). So **the original is not "ancient-looking" either**: it is period Windows chrome (light grey, small system font), with the ancient setting carried by the map art, the toolbar icons and the text. An ancient look is therefore a design choice of this project, not a restoration. **[designed]**
- Its dialogs are compact: the End of Game window is 450 x 347 with one OK button ([2026-10-05-end-of-game-screens.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-05-end-of-game-screens.md), claim 1), and the two-army transfer dialog carries both armies' lists, supply and money spinners ([ptolemy-run-ui-inventory-and-leader-draw.md §1](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/ptolemy-run-ui-inventory-and-leader-draw.md), [2026-10-08-transfer-dialog-20-units-and-slot-gaps.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-08-transfer-dialog-20-units-and-slot-gaps.md)). Our transfer dialog is 760 px wide at the 1500 px base for the same content. That the original's dialogs are small relative to ours follows from a Windows form's 8-9 pt system font; the End of Game report says its labels use "a different MS Sans Serif" under Wine than on Windows (line 78), which names the original's face as MS Sans Serif. **[derived]**
- The menu bar reads **File, Game, Strategy, Nations, Area map, Unit map, Help** in the recording ([menu-and-toolbar-inventory.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/menu-and-toolbar-inventory.md): H.264 1920 x 1080 recording); ours matches the captions.
- **[open]:** the exact font and size of each of the 29 forms (the `forms.json` of the feature-inventory run holds them; the reports do not quote them). If the user wants a measured original type scale, that is one lookup in that run, not needed for this choice.

## 2. The candidates

Every font below is under the **SIL Open Font License 1.1**, which allows bundling and redistribution in a released zip and installer (the licence text is `OFL.txt` beside each font; fonts may not be sold on their own and keep their reserved names, which the renaming of two bracketed file names does not touch). Source for all: <https://github.com/google/fonts> (`ofl/<family>/`), files copied unmodified, under `godot/Assets/Fonts/candidates/`. A listing is in `godot/Assets/Fonts/candidates/SOURCES.txt`.

| Family | Role | Files | Licence file | Copyright holder (from the licence text) | Digits [confirmed, measured on the file] |
| --- | --- | --- | --- | --- | --- |
| Cinzel | display | `Cinzel/Cinzel-wght.ttf` (variable weight, 400-900) | `Cinzel/OFL.txt` | The Cinzel Project Authors (NDISCOVER) | proportional (no `tnum`): display use only |
| Marcellus | display | `Marcellus/Marcellus-Regular.ttf` | `Marcellus/OFL.txt` | Brian J. Bonislawsky (Astigmatic) | proportional: display use only |
| Cormorant SC | display | `CormorantSC/CormorantSC-SemiBold.ttf` | `CormorantSC/OFL.txt` | The Cormorant Project Authors | proportional |
| Source Sans 3 | body | `SourceSans3/SourceSans3-wght.ttf` (variable weight) | `SourceSans3/OFL.txt` | Adobe | **tabular by default** |
| IBM Plex Sans Condensed | body | `IBMPlexSansCondensed/IBMPlexSansCondensed-{Regular,SemiBold}.ttf` | `IBMPlexSansCondensed/OFL.txt` | IBM Corp. | **tabular by default** |
| Alegreya Sans | body | `AlegreyaSans/AlegreyaSans-{Regular,Bold}.ttf` | `AlegreyaSans/OFL.txt` | The Alegreya Sans Project Authors | proportional by default; the `tnum` feature gives tabular figures (the tour turns it on) |
| Fira Sans Condensed | body | `FiraSansCondensed/FiraSansCondensed-{Regular,SemiBold}.ttf` | `FiraSansCondensed/OFL.txt` | The Mozilla Foundation and Telefonica S.A. | proportional by default; `tnum` gives tabular figures (the tour turns it on) |

Body-face facts measured from the files [confirmed]: x-height / em: Source Sans 3 0.478, IBM Plex Sans Condensed 0.516, Alegreya Sans 0.458, Fira Sans Condensed 0.526 (a larger x-height reads bigger at the same size, which matters at 13 px). Natural line height per em (hhea): Source Sans 3 1.42, Plex Condensed 1.30, Alegreya Sans 1.20, Fira Sans Condensed 1.20, against about 1.36 for today's default font; so the two Source-Sans/Plex pairings need a negative line spacing to reach the proposed pitch, and the Alegreya and Fira ones reach it by themselves.

Two notes about the files for T169 [designed]: (a) the two variable files (Cinzel, Source Sans 3) default to a thin master in Source Sans 3's case; the tour sets `wght` 400 explicitly through `FontVariation`; T169 must do the same or use static instances; (b) the candidates add about 3.3 MB to the repository, and `godot/export_presets.cfg` exports all resources, so once Godot has imported them they would travel in a build; T169 deletes every candidate the user does not choose.

### 2.1 The four pairings

| | Display (titles, headers, menu bar, buttons) | Body (panels, tables, news) | Why | Risk |
| --- | --- | --- | --- | --- |
| **A** | Cinzel (weight 500) | Source Sans 3 (weight 400, tabular by default) | Cinzel is described by its designers as inspired by first-century Roman inscriptions (the Cinzel project page, <https://github.com/NDISCOVER/Cinzel>), a direct "carved in stone" reference for a Roman setting; its lower case are small capitals, so a menu bar or a header reads as an inscription whatever the case. Source Sans 3 is a neutral, very legible body face with tabular figures out of the box and a wide weight range. | Cinzel's small capitals widen headers (its capitals are wider than the default sans for the same text (see the header widths in `01-A-...png` against `00-baseline.png`)). Source Sans' 1.42 em line height needs a -2 px line spacing to reach the compact pitch. |
| **B** | Marcellus | IBM Plex Sans Condensed (tabular) | Marcellus is a flared, softer Roman capital with real lower case, so headers keep their word shapes (easier scanning of mixed-case titles) while still reading classical. Plex Condensed is the narrowest and the one whose digits are tabular by default; it is the pairing that most reduces width. | Marcellus is a lighter, less "carved" look than Cinzel; only one weight. Plex Condensed's regular weight is slightly cool and technical against a classical display. |
| **C** | Cormorant SC (SemiBold) | Alegreya Sans (Regular, `tnum` on) | The most calligraphic and "manuscript" pairing: a Garamond-descended small-capital display with a humanist body that has a calligraphic rhythm. Alegreya Sans' line height (1.20 em) gives a compact pitch with no adjustment. | Cormorant has very fine strokes and a small body, so at 14-17 px it is the thinnest and weakest on a dark panel (SemiBold is the heaviest static weight bundled); Alegreya Sans' x-height (0.458 em) is the smallest of the four, so at 13 px it reads smaller than the others. Tabular figures need the OpenType `tnum` feature on, which Godot supports through `FontVariation`. |
| **D** | Cinzel (weight 500) | Fira Sans Condensed (`tnum` on) | The carved capitals of A with the most compact sturdy body: Fira Sans Condensed has the largest x-height (0.526 em) and a 1.20 em line height, so 13 px text stays legible and the panel pitch is the tightest. | A plain, slightly "technical" body next to a classical display; tabular figures need `tnum` on, as in C. |

I recommend **A or D** for the user's two complaints (ancient feel and compactness), D being the more compact and A the more neutral; B is the best if the user finds Cinzel's capitals heavy; C is the most atmospheric and the least safe at small sizes. [designed]

## 3. Palettes

Three candidates and the current one, as defined in the tour. The nations' sixteen colours stay the original's under all of them. Contrast is the WCAG relative-luminance ratio, **computed by the tour itself** (it prints a `contrast ...` line for every row; the figures below are that output). WCAG AA needs 4.5:1 for body text (the rule `asset-specification.md` §2.2 applies to the nations' colours); all ratios below pass it.

| Palette | Background | Panel | Raised panel | Text | Muted text | Accent | Idea |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Current (UiKit) | `#121C26` | `#1C2938` | `#263647` | `#EDF0EB` | `#ADB5BD` | `#DBA845` | blue-grey and gold |
| **Bronze Night** | `#14110D` | `#1E1912` | `#2C251B` | `#EDE3CC` | `#B8AB8E` | `#D9A55B` | near-black umber, parchment text, bronze accent |
| **Parchment** | `#CDBF9D` | `#E6D9B8` | `#F1E7CC` | `#2A2015` | `#52432A` | `#7A1F52` | light: papyrus panels, dark-brown text, Tyrian purple accent |
| **Stone and Tyrian** | `#1B1C1F` | `#26282C` | `#33363B` | `#ECEAE4` | `#B0B2B3` | `#DA94C2` | charcoal stone, bone text, Tyrian purple accent |

Contrast ratios (text colour on background / panel / raised panel) [confirmed, the tour's output]:

| Palette | Text | Muted | Accent |
| --- | --- | --- | --- |
| Current | 14.97 / 12.83 / 10.74 | 8.30 / 7.11 / 5.95 | 7.95 / 6.81 / 5.70 |
| Bronze Night | 14.75 / 13.68 / 11.86 | 8.29 / 7.69 / 6.67 | 8.50 / 7.88 / 6.83 |
| Parchment | 8.77 / 11.39 / 12.95 | 5.25 / 6.83 / 7.76 | 5.38 / 6.98 / 7.94 |
| Stone and Tyrian | 14.16 / 12.27 / 10.08 | 8.00 / 6.94 / 5.70 | 7.30 / 6.33 / 5.20 |

The lowest ratio in the whole table is 5.20:1. The Parchment palette is the only light one: it would sit differently against the dark map and the dark toolbar icons, and the toolbar's icon backgrounds are the original's; that is a judgement for the user, not a contrast problem. [designed]

## 4. The compact type scale (proposal) [designed]

Sizes are base pixels at the 1500 x 850 design viewport (`project.godot`: stretch `canvas_items`, aspect `expand`), the unit every current `font_size` override already uses.

| Role | Today | Proposed | Notes |
| --- | --- | --- | --- |
| Main-menu title | 32, default font | **34**, display face | |
| Menu bar | 16 (Godot default) | **14**, display face | The display face's capitals read larger than the same size in lower case. |
| Dialog / panel / news header | 18-20 | **17**, display face, accent colour | |
| Body (facts, table cells, news) | 14 | **13**, body face | Tabular figures for every figure column. |
| Muted notes and column titles | 12 | **12** | unchanged. |
| Button text | 14-16 | **13** (display face for command buttons) | |
| Button minimum height | 40 | **27** (font + 14) | |
| Line pitch (panels and news) | about 23 (14 px, default 1.36 em, +4 separation) | **about 18** (13 px; `line_spacing` -1, container separation 3) | Measured in the sample: the panel's text block (`Army of Rome` to the last unit line) is about 302 base px high in `00-baseline.png` and about 251 in pairing A (`01-A-cinzel-sourcesans3-bronze.png`), about -17 %; the more compact bodies (C, D) save more. |
| Container separation | 4 (panels), 6-8 (dialogs) | **3** (panels), **4** (dialogs) | |
| Table row gap | default | **1** | |
| Panel margins | 12 side, 10 top and bottom | unchanged (the tour keeps them) | The pitch and the button height give most of the saving. |

**The army panel at the proposed scale:** the right-hand panel of every `rendered/candidates/NN-<pairing>-<palette>.png` shows T140's fields (`Army of Rome`, `Moves`, `Supply`, `Morale`, `Money`, `Terrain`, `Total troops`, `No. of units`, `Regulars cost`, `Mercenary pay` and the unit lines) in the proposed scale, to be compared with the same panel in `00-baseline.png`; the sample's block is about 17 % shorter for pairing A (§4 table). The **transfer dialog** (#1070) is the middle block of the same boards, at the same scale.

**Risk [open]:** 13 base px is about 12 physical px on a 1366 x 768 window (the viewport scales by the window's width over 1500) and 16.6 physical px at 1920 x 1080. The smallest laptop sizes are therefore where the proposal is tightest; T169 may want a lower bound (13 px at a scale below 1 is the user's call) or a UI-scale setting, which this task does not propose.

**Where the numbers come from:** the base sizes are today's own values from `UiKit` and the panels (§1.1); the proposed values are choices, not measurements of the original (the original's form sizes are [open], §1.4).

## 5. The tour

`godot/Checks/FontCandidatesTour.cs` + `.tscn`, a windowed tour whose name does not end in `Check` (CI's loop skips it). It builds one sample board per candidate: a menu bar, the main-menu block (title and four buttons), the transfer dialog (header, a two-sided unit table with right-aligned figures, a supply and money line, buttons), the army panel of T140's fields and the news log, with the pairing and palette named in a caption at the bottom. First comes `00-baseline.png` (default font, today's sizes and colours), then **4 pairings x 3 palettes = 12 boards**. It reads the fonts as raw bytes, so no import step is needed, and changes nothing else.

```
dotnet build godot/IC2.MapViewer.csproj
IC2_SCREENSHOT_DIR=<dir> godot --path godot res://Checks/FontCandidatesTour.tscn --quit-after 1500
IC2_TOUR_MODE=asis IC2_SCREENSHOT_DIR=<dir> godot --path godot res://Checks/FontCandidatesTour.tscn --quit-after 1500   # the as-is dialogs
```

The 13 candidate boards (`rendered/candidates/`): `00-baseline.png`; `01..04` pairings A, B, C, D on Bronze Night; `05..08` A, B, C, D on Parchment; `09..12` A, B, C, D on Stone and Tyrian (file names `NN-<A|B|C|D>-<pairing>-<palette>.png`). What I saw in them: A (Cinzel + Source Sans 3) reads as an inscription in the menu bar and headers, and its body is clean but the loosest of the four pitches; B keeps readable mixed-case headers and is the narrowest; C is the most delicate, and its body is visibly the smallest at 13 px; D's body is the most compact (by its metrics, §2); I looked at A, B and C in detail and at D only briefly. Tabular figures work in all four (the right-aligned troop columns line up).

## 6. What T168 did not do

- It did not apply anything (T169), nor touch the city markers (#912) or the battle screen (T128).
- It captured no Supply army, Recruit mercenaries, Join armies or Transfer ships dialog (§1.3) and did not view the original's recordings (§1.4).
- The research reports' statements about the original are not re-checked here.

## 7. The user's decision

*To be recorded by the main session after the review: the pairing, the palette and the scale the user picks (or the changes the user asks for), and T169's entry updated to name them before T169 is dispatched.*
