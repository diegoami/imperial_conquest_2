# v0.7.0 owner playthrough: the Italy 500 BC campaign

T204 Done-when 3. Play a packaged build (from the release workflow, or `scripts/package.ps1` locally) and tick each line in the tracking issue. Note anything that looks wrong, even when the line passes. Your result decides the 0.7.0 cut.

## Before you start

- [ ] The build installs and starts. The main menu says **Open Imperial Conquest**.
- [ ] Your saves and settings from the old "Map Viewer" folder are still there (the one-time migration).

## New Game

- [ ] **New Game** shows three cards: Classical Faithful, Improved, and **Italy, 500 BC**.
- [ ] The Italy card names its map (209 × 233 tiles), its rules (500 BC to 460 BC, a 40-year reign), the goal (hold every city) and its 12 nations.
- [ ] The seat screen lists all twelve nations. Pick one other than Rome at least once.

## The map and the menus

- [ ] The map is Italy with Sicily, Sardinia and Corsica, with no empty brown country. It shows forests, rivers, lakes, the Alps and mountains only at the high massifs.
- [ ] The mini-map shows the whole map and its view rectangle.
- [ ] The **Nations** menu and the toolbar swatches list exactly the twelve Italy nations. Each one opens its overview.
- [ ] Towns read as towns. Note whether big and small ones can be told apart ([#1309](https://github.com/diegoami/imperial_conquest_2/issues/1309)).

## Playing

- [ ] **Movement**: an army moves; forest and rivers cost more than plain; a fleet sails and carries an army.
- [ ] **Field battle**: attack an army; the battle board opens and plays out.
- [ ] **Siege**: besiege and take a town, then a defended one.
- [ ] **Diplomacy**: declare war (the confirmation names the target's allies) and make peace.
- [ ] **Economy**: a season's payout arrives; taxes and the balance sheet look sane for a small nation.
- [ ] **The computer nations** move, fight and take towns. None stands still for a whole round.

## Saving

- [ ] **Save**, then quit, start again and **Load**. The game resumes on the Italy map, with the same nations, menus and turn.

## The end

- [ ] Either hold every city (total conquest; an all-AI run or a late save helps), or play on to the reign's end in 460 BC.
- [ ] The **End of Game** window names the reason. At the reign's end it reads "your allotted 40 years".

## Report

Comment on the tracking issue:
- which lines passed;
- what failed or looked wrong, with a screenshot if you can;
- whether 0.7.0 can be cut.
