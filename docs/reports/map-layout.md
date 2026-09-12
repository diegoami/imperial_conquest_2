# Candidate DAT map layout

This is a static rendering of the original DAT's first 89,600 bytes as 44,800 little-endian 16-bit cells, with 334 city coordinates from the following records. No original executable was run. The generated map image is kept outside the public repository because it derives from the original game data; the repository contains only the renderer and these observations.

The grid dimensions are 320 × 140. **Cell storage is column-major:** the value at candidate `(x, y)` is the word at byte offset `2 × (x × 140 + y)`. Interpreting the same words row-major produces horizontal striping. Column-major rendering instead produces a recognizable map of the Mediterranean, Europe, North Africa, and the Near East. Candidate city locations such as Rome `(101, 43)`, Carthago `(93, 78)`, Alexandria `(190, 94)`, Sidon `(221, 71)`, and Rhagae `(317, 49)` align with their expected geography. This combination is strong evidence for the dimensions, order, and coordinate orientation.

Value `0` traces the seas and coastline, so it is likely water. Values `2`, `3`, `4`, and `5` occupy large land regions. Smaller values and sparse values from `20` upward appear in localized features, while values in the `200`–`335` range may be dynamic markers. Their exact terrain or entity meanings remain unverified. The SVG renderer deliberately uses a *provisional* palette and labels only a few landmarks; it does not claim to reproduce the original game's art or terrain colors.

Run `dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --render-map rendered/map.svg` from the repository root after setting `assets.local.ini`. The SVG is local and `rendered/` is ignored by Git.

## Next checks

1. Compare candidate cell values against the WinHelp terrain descriptions and executable terrain tables.
2. Overlay save-to-save changed cells to distinguish static terrain from transient markers and moving entities.
3. Confirm whether city coordinates refer to the cell itself, a nearby symbol, or a visual anchor.
