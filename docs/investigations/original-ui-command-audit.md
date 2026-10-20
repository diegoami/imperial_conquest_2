# original-ui-command-audit (archived)

**Question:** What does every menu entry, toolbar icon and map click of the original do, and what do the clone's engine and UI do for it?

**Result:** Audited: 47 rows (8 present, 11 partial, 14 UI-only missing, 11 engine missing, 3 out of scope); the map-click model and the unit-map context strip read from `TUnitMap_SelectUnit`/`CheckForMove`/`*ButtonsOn`; Join armies/fleets and Transfer ships gate on the same tile where the original takes the partner at distance 1. A run of the original under Wine observed the click rows for an army and a city, the Taxation range 0–40 and the split's (+1, +1) placement, and a second run the fleet orders (Build fleet and the launch, embark and disembark by click, the six Fleet entries, Cancel selection on the fleet strip), a third a naval battle at war, which opens no window, a fourth the refusal to attack a fleet docked one tile from its own city, and a fifth the attack prompt for a fleet at trade terms, whose Yes declares war on the target and its ally, as candidates; the first contradicts the code reading on the attack prompt's No, which keeps the selection.

Full write-up, frozen: [archive/desktop-2026-10-20](https://github.com/diegoami/imperial_conquest_2/blob/archive/desktop-2026-10-20/docs/investigations/original-ui-command-audit.md).
