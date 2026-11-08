# v0.11 panel and player-setup layout

Owner request, 2026-11-08: [#1818](https://github.com/diegoami/imperial_conquest_2/issues/1818). Keep the strategic map as the primary area and arrange the overview map, selected-object information, News and Log on the right. Each panel must be independently hideable and reopenable. Reduce the overview-map location/filter controls and the main top-toolbar icons, including excessive padding and row height.

## Requested behavior

| Panel | Content and visibility |
| --- | --- |
| Overview map | Overview/minimap with its location/filter icon strip above it. Hide both together without hiding the other panels; the owner seldom needs the overview. |
| Selected object | Current army, fleet/navy or town information and existing controls. Reopening shows the current selection with existing information permissions. |
| News | Existing campaign reports, independently scrollable, on the right rather than below the main map. Preserve ordering, dismissal and hotseat/privacy semantics. |
| Log | In-game command/activity output, independently scrollable, on the right. Define the content/history-depth contract before implementation; moving output alone does not authorize a new battle-history policy or a developer diagnostics viewer. |

Provide a hide/close control on each panel and a discoverable View/Panels menu or compact toggle strip that remains available when panels are closed. It must be possible to reopen any panel while the overview, other panels or entire right column are hidden. Expose meaningful panel names and checked/visible states to keyboard and accessibility users.

Hidden panels give their space back. When all four are hidden, the main map gains the column width. Reconcile the existing F12/global-column toggle with per-panel choices so opening the column preserves the chosen panel combination. Selection changes and arriving news/output must respect visibility choices; hidden panels retain authoritative current content for reopening.

Use sensible height allocation and independent scrolling so several open panels fit the supported window sizes. Exact heights, order details and any resize affordance require the implementation contract; a drag-and-drop docking framework is not implicit in this request. Remembering visibility across launches is a proposed per-machine UI refinement, outside campaign saves, rather than an already-approved persistence contract.

Smaller toolbar/location icons must produce a visibly more compact layout, with legible illustrations, usable pointer targets, preserved tooltips/shortcuts and focus/disabled/selected feedback. Choose and measure sizes in the contract; test all four themes and supported DPI rather than shrinking textures inside an unchanged oversized toolbar.

## Implementation and acceptance

Current entry points: `MainGameScreen.cs` places the AreaMapView strip/minimap and ContextPanel in the right column, while `_lastCommandLabel` and NewsLogPanel are appended below the map. `SidePanelToggle`/F12 currently hides the column as one unit. Related sources include `AreaMapView`, `ContextPanel`, `NewsLogPanel`, `CommandToolbar`, `QolCommandIcons` and `UserSettings`.

Read the relevant private original-manual sections and screenshots before implementation. This right-side layout and independent visibility are owner-requested designed QOL changes, not a claim that the original used this arrangement. Original media remains private. No gameplay, RNG, news-generation, save-format or information-permission change is implied.

Validate panels individually and in combinations, all hidden then reopened, army/fleet/town selection changes, news/output arriving while hidden, hotseat and observer contexts, window resizing, themes and DPI. Check actual layout space, always-reachable reopening controls, focus and scrolling, and unchanged campaign/RNG state. Include window captures for visual acceptance alongside meaningful real-control checks.

## Player selection: visible Start and right-side settings

Owner addition, 2026-11-08: [#1820](https://github.com/diegoami/imperial_conquest_2/issues/1820). The owner reports that Start Game is hardly visible at the bottom of the player-selection screen and requests using the right-hand space for Start and the aggressivity/behaviour controls.

Keep the nation/Human/leader-name roster in an independently scrollable left area. Put a prominent Start Game button and all three current personality controls (Aggression, Expansion drive, Loyalty to alliances) in a right-hand setup/action area. Start must remain fully visible and keyboard accessible at supported sizes/DPI, regardless of roster length/scroll position. Adapt narrower layouts without burying the primary action below overflowing content.

Preserve the current personality values/ranges/defaults and their application, selected humans/leader names, observer/hotseat choices, Back navigation and Start validation/confirmation. This is a layout change, not a new AI policy. Coordinate with the historical background/advice scope (#1811).

`ScenarioSeatScreen.cs` currently builds headers, a player scroller with a 420px minimum height, three sliders and Start in one vertical column. The structure is source-confirmed; the clipping/visibility report is the owner's observation. Before implementation, consult the relevant private manual and cite this owner-requested layout exception. Validate actual visible bounds in window captures, long rosters and high DPI, plus existing New Game/hotseat/observer control checks and unchanged session-factory inputs. No game runs or GUI changes are part of this planning record.
