# Usability test — v0.6.2

2026-10-20 · tested on tag `v0.6.2` (commit `4253d8a`) · [#1280](https://github.com/diegoami/imperial_conquest_2/issues/1280). The player manual written from the same session is [v062-user-manual.md](v062-user-manual.md).

## Verdict

v0.6.2 is playable end to end: in about 40 minutes of real play every main feature worked, and a full 20-year game ran in the terminal without a crash. The features covered were a new game, moves, a siege, a won tactical battle, recruiting, a fleet order, diplomacy, save and load. The obstacles for a new player are in the interface, not the rules:

- Map markers are too small to find and hit.
- A missed click silently moves your army.
- Most feedback arrives as faint, developer-worded status lines.
- Nothing in the game explains the goal or how to fight.

The findings below list 28 usability issues (6 high, 14 medium, 8 low) and 3 defects; the one exception seen is harmless to the player.

## Test setup

| Item | Value |
| --- | --- |
| Build | Tag `v0.6.2` (commit 4253d8a), Godot project and `IC2.sln` built from source with .NET 10.0.113 |
| Engine | Godot 4.7.2 .NET for Linux, OpenGL 3 on software rendering (Mesa llvmpipe), audio disabled (no sound card) |
| Screen | 1600 × 1000 virtual display |
| Game | Classical Faithful, Classical Mediterranean 270 BC, playing Rome, AI sliders at default |
| Method | Expert walkthrough: the tester played as a first-time player, driving mouse and keyboard and reading each screen from screenshots; every menu and dialog was opened at least once. The source was read only to explain what was seen. |
| Long run | Three 520-turn passive games in the terminal client (`IC2.Cli`) as Rome, Media and Armenia, to reach the end-of-game paths |
| Not covered | Windows build and installer, sound, hotseat, the Improved ruleset in the UI, fleets at sea, mercenary hiring, peace offers |

## Session log

| Turn | What the tester did | What happened |
| --- | --- | --- |
| Setup | New Game → Classical Faithful → Rome → Start Game | Map opened in about 8 s. Rome: 25 cities, 2,200 talents, at war with Gaul. |
| 0 | Opened every menu; Taxation, Balance sheet, International relations | All opened. Esc on the Balance sheet logged an exception. |
| 0 | Proposed trade to Carthage | Refused ("already trades with 3 nations"), shown only in the status bar. An AI battle report opened on top of the dialog. |
| 0 | Find a city → Rome; zoomed; clicked cities | Map centred but Rome was not marked; needed several clicks to tell cities from armies. |
| 0 | Selected army-1, moved it 3 tiles | Worked. A later click meant to select army-0 missed and marched army-1 a further 5 tiles instead. |
| 0 | Marched army-0 beside Felsina (Gaul) and besieged it | Siege failed, 20,720 against 34,050; Felsina's fortification fell from 68% to 65%. |
| 0 → 1 | End turn | Movement replay, then one AI battle report. Fifteen presses of Esc did not close it. |
| 1 | Recruit unit: mobilized a ready heavy infantry; recruited 1,200 more | Worked; a new army appeared beside Rome. Feedback read "recruitment.mobilize-recruit-slot accepted." |
| 1 | Saved | "Saved to …" in the top bar. |
| 1 | Attacked Gaul's army (43,263) with army-0 (21,801) | Tactical board opened. Placed and moved units, shot once, then handed over to the computer general. |
| 1 | Waited for the battle | Rome won after about 6 minutes; Gaul's army was destroyed and 96 talents captured. |
| 1 → 3 | Help, About; End turn twice | Two AI battle reports per turn; an End turn click was swallowed while a second report was queued. |
| 3 | News; File → Close; Settings; Load → Continue | All worked; Close did not mention unsaved turns; the save resumed correctly. |
| 1 (reloaded) | Build fleet; F12; right-clicked Rome; mercenary filter | Fleet ordered at Pisae; panel toggled; "There are no mercenaries at Rome"; offers marked on the minimap only. |
| 1 | Declared war on Thracia; proposed peace to Gaul | War declared at once with no question; Dacia joined the war too. Gaul refused peace. |

## Findings

Ordered by severity. **High** stops or misleads a typical player; **Medium** costs time or causes mistakes; **Low** is polish. Severity is the tester's first call; re-triage as needed.

| # | Severity | Area | Finding | Evidence | Suggested fix |
| --- | --- | --- | --- | --- | --- |
| F1 | High | Map | Markers are too small to find or hit. Cities and armies are 5–8 px squares that look alike, with no names and no hover tooltip. | Four mis-clicks in one session; telling army from city needed a zoomed screenshot. | Larger army counters with a distinct shape; city names at closer zoom; tooltip on hover; ring the selected unit. |
| F2 | High | Map | A click on empty ground instantly moves the selected army; a near-miss on a marker becomes a move that cannot be undone. | Aiming at army-0 marched army-1 five tiles and spent all its moves. | Show the path and cost on the first click and move on the second, or offer one-step undo while no battle has occurred. |
| F3 | High | Feedback | Results and refusals appear only in three faint lines under the map, often in developer wording. | "diplomacy.propose-trade rejected (diplomacy.target-trade-cap-reached)"; "recruitment.mobilize-recruit-slot accepted." | Plain-language messages, shown in the dialog that gave the order or as a toast; keep the status log as history. |
| F4 | High | Diplomacy | Declare War acts at once with no confirmation, and silently drags in the target's allies. | Declaring on Thracia also started a war with Dacia; the only notice was the status line. | Confirm first, listing every nation that will be at war with you. |
| F5 | High | Copy | Internal text leaks into the player's view. | Ruleset cards quote flags ("flags.diplomacyModel"); city panel "display-only — no command sets it; see this class's remarks"; "fortification word"; "Army — army-1"; "4100x light\_infantry"; "Battlefield reconciled"; "controller handoff pending"; "> news"; tile coordinates in move messages. | A copy pass over all player-facing strings; keep ids and coordinates for the log and CLI. |
| F6 | High | Onboarding | Nothing in the game explains the goal, how to attack or besiege, supply, or how a game ends. | Help is one page of controls and menu one-liners. | A short "How to play" page or a first-turn guide covering win and loss, moving, attacking, supply and End turn. |
| F7 | Medium | Turn flow | Every AI-vs-AI battle opens a modal Battle Result at the start of your turn. | One or two per turn in the first three turns; one opened over the diplomacy dialog. | Show only battles involving you; send the rest to News, with an option to show them all. |
| F8 | Medium | Turn flow | While a report is queued, clicks on End turn are ignored without any sign. | End turn did nothing until a second battle report was closed. | Disable the End turn button while overlays are pending, or queue the click. |
| F9 | Medium | Dialogs | Battle Result does not close with Esc, unlike the other dialogs. | Fifteen presses of Esc had no effect. | Esc and Enter close it. |
| F10 | Medium | Battles | Siege results name a "winner" inconsistently. | Our failed siege: "Winner: Gaul". An AI siege that took no city: "Winner: Seleucid" with 1,810 casualties against 0. | Say "City held", "City taken" or "Siege continues" instead of "Winner". |
| F11 | Medium | Battles | No odds are shown before an attack; the city panel gives no defence strength. | Our siege failed at 20,720 against 34,050 with no warning beforehand. | Show the defence strength and an estimate before an attack, or ask to confirm when the odds are poor. |
| F12 | Medium | Layout | Panels appear and resize and shift the map and board under the pointer. | The order strip moves the map about 50 px; the Skip/Fast row and the wrapped battle header move it again. | Reserve fixed space for the strip, the replay controls and the header. |
| F13 | Medium | Battles | The tactical board fills under a third of the screen. | At 1600 × 1000 the 14 × 12 board is 480 × 410 px, with empty space beside it. | Scale the board to the window. |
| F14 | Medium | Battles | Clicks made while a unit is still moving are silently dropped. | Two of three Guards orders were lost until the tester waited between clicks. | Queue the order or show a busy cursor. |
| F15 | Medium | Battles | A computer-general battle is slow at the default pauses. | One battle took about 6 minutes. | Lower the default pauses or add a Fast button on the board. |
| F16 | Medium | Navigation | Find a city centres the map but does not mark the city, and has no text search. | After Find → Rome the tester still could not tell which square was Rome. | Select and flash the city; add a type-to-search box. |
| F17 | Medium | Saving | Leaving a game does not mention unsaved progress, and there is no autosave. | Close after two unsaved turns asked only "Continue?". | Offer Save / Don't save / Cancel; autosave each turn. |
| F18 | Medium | Dialogs | Recruit unit's Cancel does not undo a recruit already made in the dialog. | Recruit unit took effect immediately; OK and Cancel then both just close. | Rename Cancel to Close, or apply orders only on OK. |
| F19 | Medium | Diplomacy | Disabled buttons look almost the same as enabled ones; the header labels sit off their columns. | International relations screenshot. | Clearly dimmed disabled buttons with a tooltip saying why; align the headers. |
| F20 | Medium | Economy | The tax dialog shows only extra income, not the cost in unity; the balance sheet's "Balance" is the treasury, with no net figure per season. | Tax 10% → 16%: "Income 252 → 404", nothing else. | Show the unity change at the new rate; add a "Net per quarter" line. |
| F21 | Low | Map | Show armies and the mercenary filters mark only the minimap, not the main map. | Show armies changed nothing visible on the main map. | Highlight on both maps. |
| F22 | Low | Copy | The main menu calls it a 1999 game while About says 1996; the window and save folder are named "Map Viewer". | Main menu subtitle; About box; app-data folder name. | One year everywhere; a player-facing app name. |
| F23 | Low | Top bar | No season in the top bar; the "Saved to …" message stays for several turns and is cut off. | "Week 3, 270 BC (turn 1)"; the season shows only in the status line. | Add the season; fade the save message after a few seconds. |
| F24 | Low | Dialogs | Recruit unit highlights two tabs at once and uses cryptic abbreviations. | "Lit Inf" stayed lit after choosing "Hvy Inf". | One selected tab; full unit names. |
| F25 | Low | Help | Help says right-click shows a city's unit list; it shows only the mercenaries there. | "There are no mercenaries at Rome". | Correct the Help text or show the garrison too. |
| F26 | Low | Settings | Settings are forgotten on quit; there is no volume, window-size or fullscreen option. | Settings screen. | Save settings; add volume and display mode. |
| F27 | Low | Load | The Load list shows file name, scenario id and turn only. | "classical-mediterranean-turn-1.json — classical-mediterranean, turn 1". | Add nation, date in game, ruleset and real time saved. |
| F28 | Low | Diagnostics | The journal rotates quickly, so long sessions keep little history. | 1 MiB written in about 20 minutes, mostly by one tactical battle; five files hold about 1.5 h. | Log fewer battle frames or raise the cap. |

## Defects

The Godot log recorded one exception in the whole session, and the terminal game ran 520 turns without an error.

| # | Defect | Where | Cause and fix |
| --- | --- | --- | --- |
| D1 | Closing the Balance sheet with Esc throws `NullReferenceException`; the dialog still closes. | `godot/UI/Dialogs/BalanceSheetDialog.cs:166` | `Ok()` raises `Closed`, which removes the dialog, so the following `GetViewport()` returns null. Mark the input handled before calling `Ok()`, and check the other dialogs' Esc handlers for the same order. |
| D2 | The city panel says "Tax rate: 10% (display-only — no command sets it; see this class's remarks)", but Strategy → Taxation does set it. | `ContextPanel.cs:328` | Stale text from before Taxation was wired. Show just "Tax rate: N%". |
| D3 | About shows "Version" with nothing after it; the diagnostics header logs `version=` while the build is stamped `1.0.0+4253d8a`. | `AboutDialog.cs:45`, `project.godot` | Godot always defines `application/config/version` (empty), so the "development" fallback never applies. Set the version to 0.6.2 at release, or fall back when the value is empty. |

Also confirmed to work as designed: after the human nation falls, the terminal game keeps running in watch mode past 250 BC. The 250 BC reign end applies only to a living human nation and is covered by passing engine tests (16 victory and reign-end tests run green).

## What worked well

- **Stability.** No freeze or crash in about 40 minutes of interactive play under software rendering; one 20-year game in 6 minutes in the terminal.
- **The tactical battle.** Placing, moving, shooting and the computer general all worked; the "Current exchange" read-out explains every shot and blow, and the Battle ended report is complete and readable.
- **Dialogs with previews.** Taxation previews the income, Build fleet shows cost, upkeep and capacity, and Recruit unit shows cost and training time.
- **Save and load.** The saved game resumed exactly, with the same treasury and turn.
- **Tooltips** name every toolbar and side-panel icon.
- **Turn replay.** Skip and Fast let you hurry the movement replay.
- **The diagnostics journal** records commands, overlays and battle frames and would make a hang report useful.

## Full-game run

A passive player does not last: three 520-turn games in the terminal client, ending the turn every time with no other order, all ended with the human nation conquered. Each 20-year game took about 6 minutes (0.7 s a turn for all 16 nations).

| Nation played | Fell | Conquered by | Message |
| --- | --- | --- | --- |
| Armenia | Winter 268 BC (about turn 44) | Seleucid | "Your nation has been conquerred by Seleucid." |
| Media | Autumn 267 BC (about turn 63) | Seleucid | Same message |
| Rome | Spring 253 BC (about turn 408) | Greece | "Your nation has been conquerred by Greece." |

Rome, snapshot every two years (start of spring):

| Year | Cities | Armies | Treasury (talents) | Unity |
| --- | --- | --- | --- | --- |
| 270 BC (start) | 25 | 2 | 2,200 | 821 |
| 268 BC | 19 | 0 | 4,719 | 818 |
| 264 BC | 23 | 0 | 11,927 | 990 |
| 260 BC | 24 | 0 | 19,119 | 990 |
| 258 BC | 15 | 0 | 22,675 | 915 |
| 256 BC | 12 | 0 | 26,155 | 983 |
| 254 BC | 18 | 0 | 30,285 | 967 |
| Winter 253 BC | 0 (conquered) | 0 | 32,869 | 0 |

After the fall the client switches to watch mode ("one round per end, no orders") and the world plays on. Greece conquered Illyria in the first year; the Seleucids took Bithynia; Carthage took Numidia; Macedonia took Thracia and Illyria; Celtiberia took Gaul. A Rome run by the computer reappeared later with 8 cities, a treasury of −2,468 talents and tax at 40%.

The Godot game was not run to the end: at about a minute of real time per turn with reports to close, 480 turns would take most of a day. The end-of-game window was therefore checked against the engine's tests, not seen on screen.

## Recommendations

In priority order. The first three remove most of the friction a new player meets in the first ten minutes.

1. **Make the map readable and forgiving** (F1, F2, F12, F16, F21): bigger, distinct army counters; names and hover tooltips; a selected-unit ring; move on a second click or allow one undo; a fixed layout so nothing shifts under the pointer.
2. **Rewrite player-facing text and move feedback to where the player is looking** (F3, F5, D2, F25): one copy pass, and show the outcome of each order in its own dialog or as a toast.
3. **Add a "How to play" page** (F6): the goal, moving, attacking and sieges, supply, ending the turn and the ways a reign ends. The user manual's "Playing a turn" and "Winning and losing" sections can serve as its draft.
4. **Protect irreversible choices** (F4, F11, F17): confirm a war and list the allies it pulls in; preview the odds of an attack; offer to save when leaving, and autosave every turn.
5. **Quiet the turn start** (F7, F8, F9, F10): report only battles you fought, let Esc close reports, block End turn visibly while reports are pending, and word siege outcomes as "held" or "taken".
6. **Tactical board** (F13, F14, F15): scale it to the window, queue clicks during moves, and speed up the default pauses.
7. **Small fixes** (D1, D3, F18–F20, F22–F24, F26–F28).
