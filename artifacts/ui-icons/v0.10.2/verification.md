# v0.10.2 builder verification, 2026-11-07

Implementation checkpoint `21cb5eadaa9b0d75d73cdcd502d57ce6149fa2c9`, plus the owner-added #1804 regression in `HumanTurnNewsCheck`. This is builder evidence, not an independent milestone verdict.

The family sheet and native theme/state sheet were visually inspected at their stored sizes. The fifteen symbols remain distinguishable on Parchment, Dark, High contrast and Slate; focus outlines and disabled fading are visible. Small detailed motifs use their tooltips/accessibility labels for precise meaning. The sheet composites native-loaded textures with theme colours; it does not establish visible gameplay acceptance or replace a window capture.

## Campaign-turn coverage (#1804)

The existing season-boundary scenario now loads global turn 73 / Summer week 11, then ends the round to global turn 74 / Autumn week 1. It requires the real `CampaignDate` label to lead with the global turn in both states and retains seasonal date checks. Production code and save naming are unchanged.

Two temporary mutations to `MainGameScreen.RefreshCalendarAndStatus`'s label assignment were built and run separately:

1. Remove `Turn {calendar.TurnIndex} · `: `HumanTurnNewsCheck` exits 1, failing the loaded/global-turn assertion and the post-boundary assertion.
2. Replace `calendar.TurnIndex` in that prefix with `calendar.Week`: the same regression fails and exits 1.

Original source bytes were restored in a `finally` block and rebuilt. `HumanTurnNewsCheck` and `SaveNamingCheck` then both passed. See the retained mutation and restored-check logs beside this receipt.

## Builds and package

- `dotnet build IC2.sln --nologo -v q`: passed, zero warnings/errors.
- `dotnet build godot/IC2.MapViewer.csproj --nologo -v q`: passed; the first build reported existing nullable warnings. The subsequent package build passed with zero warnings/errors.
- `pwsh scripts/package.ps1`: passed. This is a self-contained Windows export; no installer lifecycle check was performed.
- Copied the export to a new isolated folder, removed SDK/runtime roots and fixture environment variables, reduced PATH to Windows system directories, and isolated APPDATA/LOCALAPPDATA. No original configuration or media exists in that package.
- The copied executable passed `AssetPackSelectionCheck`, `UnitActionIconsCheck`, `ObserverPlaybackCheck`, `HumanTurnNewsCheck` and `SaveNamingCheck`, each exiting zero with no FAIL/SCRIPT ERROR. `UnitActionIconsCheck` requires all fifteen PNGs to decode with alpha/colour and checks live theme changes. The observer check exercises dynamic playback state and existing behavior.
- No authored pixels, generation code, game rules, scenario data or save formats changed after the original eight-scene checkpoint. That checkpoint's offline generator, unrelated-art hash and other real-control checks are recorded in PR #1807.
- Godot churn guard restored only whitespace churn in `MapViewer.cs`; a second guard check is required before commit. Export-generated untracked UID sidecars were removed. `git diff --check` passed.

## Solution tests and limitations

The default local solution run passed 7,171 engine tests with 38 skips; the local data run passed 1,034 with five skips and one failure. The failure is an existing corpus-coverage guard: the owner's larger configured corpus contains `AUTO0742.SAV`, which has no row in the committed outcome table (`CorpusOutcomeGeneratorTests.cs:74`). No parser or fixture was changed to silence it.

Using the existing private CI fixture corpus explicitly through `IC2_FIXTURES_DIR`, the complete data suite passed all 1,040 tests with zero skips/failures. Hosted CI must still validate the exact PR/main candidate with its configured fixtures. Local skip counts are not treated as passes or as native-original acceptance.

Original-assets research/extractor delivery and the ten-scenario v0.11 programme are separate issues #1740/#1798/#1808. No extractor, Python runtime, original files or extracted media enters this patch.
