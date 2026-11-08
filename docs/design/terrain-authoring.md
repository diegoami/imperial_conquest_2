# Terrain authoring: hills, mountains and passes

**Owner rule, 2026-11-08.** Apply to authored historical campaigns and generated fictional worlds. The owner reports overly dense, effectively blocking mountain ranges in the Middle East scenarios; their correction is tracked in [#1822](https://github.com/diegoami/imperial_conquest_2/issues/1822).

## Rule

- Reserve **mountain terrain** for the highest or most severe relief. A mapped mountain range is not a mandate to fill its whole footprint with mountain tiles: the game's existing penalties make that too restrictive.
- Use **forest terrain as the game's metaphor for lower hills**, foothills and rough uplands when expressing relief. Such a tile does not require a claim of dense woodland; do not equate the gameplay category with literal vegetation coverage.
- Author deliberate **plain-terrain passes and valleys** through ranges where movement should be possible at campaign scale. Passes must form continuous usable routes across the belt, not isolated plain cells inside an otherwise expensive wall.
- Preserve the region's recognisable high relief and strategic choices while allowing reasonable movement between relevant regions and settlements. Distribute routes deliberately; random noise or scattered forest holes is not a substitute for a pass plan.
- Apply this through terrain placement and generation. Retain the existing terrain IDs, movement/combat rules and costs. Original/reference-world bytes are not rewritten by this authoring policy.

This is an owner-approved gameplay/geography abstraction. Historical pass locations and relief evidence need their own sources; authored widths, cell boundaries and hill proxies must be labelled as abstractions. The rule does not establish palaeovegetation, elevation measurements or original-game fidelity.

## Authoring and review

Record the high-relief footprint, lower forest-coded hill areas and intended plain crossings in reproducible authoring inputs/code. Rebuild the image and terrain sidecar through the normal importer; a correction to generated output alone will be lost on regeneration. Apply all masks in an order that preserves intended water/river topology and usable crossings.

Check representative cross-range routes with the production movement/pathfinding rules and ordinary army budgets. A binary land-connectivity check cannot establish that a route is practically usable. Record affected regions/settlements, changed tile classifications, crossing costs or turn requirements, and before/after map views. Check AI navigation and opening access as well as human routes.

No universal pass count, width or new movement-cost threshold is specified by this rule. Select these for each map scale and explain the result. Before changing an existing world's terrain, define how revised geography affects new games and existing saves/world identity; do not silently assume compatibility.

Use this rule in Near East corrections and the [v0.11 historical/random-map programme](v011-campaigns-and-random-maps.md). Keep the main game map, minimap and asset presentation consistent with the unchanged terrain categories.
