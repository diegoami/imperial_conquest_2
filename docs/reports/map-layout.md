# Candidate DAT map layout

This is a static rendering of the original DAT's first 89,600 bytes as 44,800 little-endian 16-bit cells, with 334 city coordinates from the following records. No original executable was run. The generated map image is kept outside the public repository because it derives from the original game data; the repository contains only the renderer and these observations.

The grid dimensions are 320 × 140. **Cell storage is column-major:** the value at candidate `(x, y)` is the word at byte offset `2 × (x × 140 + y)`. Interpreting the same words row-major produces horizontal striping. Column-major rendering instead produces a recognizable map of the Mediterranean, Europe, North Africa, and the Near East. Candidate city locations such as Rome `(101, 43)`, Carthago `(93, 78)`, Alexandria `(190, 94)`, Sidon `(221, 71)`, and Rhagae `(317, 49)` align with their expected geography. This combination is strong evidence for the dimensions, order, and coordinate orientation.

Five later screenshots independently align with the grid and identify the original display of values `0` (water), `2` (green land), `3` (desert), `4` (forest), and `5` (mountains). See the [save/screenshot analysis](saves-and-screenshots.md) for registration counts and color evidence. [Later comparison](rivers-and-map-markers.md) identifies `6`–`11` as six blue river shapes, `20`–`199` as city markers, and sparse `200`–`299` plus observed `333`/`335` as candidate army and fleet overlays. The exact unit-record mapping remains unverified. The SVG renderer still uses a provisional palette; the Godot viewer draws river connections and code-native markers without reproducing original tile art.

Run `dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --render-map rendered/map.svg` from the repository root after setting `assets.local.ini`. The SVG is local and `rendered/` is ignored by Git.

## Next checks

1. Compare the remaining cell values, including water value `1`, against WinHelp descriptions and executable terrain tables.
2. Map save-to-save army and fleet marker changes to post-city entity records and movement observations.
3. Confirm whether city coordinates refer to the cell itself, a nearby symbol, or a visual anchor.
