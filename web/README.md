# Browser runtime proof

This is the first implementation for [v0.13.0](https://github.com/diegoami/imperial_conquest_2/issues/1905). The Blazor WebAssembly app calls the production `GameSession` in the browser. It loads the authored Italy, 500 BC campaign for Rome with seed 42. The map displays terrain, cities and armies. Use the map or coordinate fields to issue a move, then end a turn. Save and Reload use one browser-local slot and the engine's native save format.

## Run locally

From the repository root:

```text
dotnet publish web/IC2.Web/IC2.Web.csproj -c Release -o rendered/web-proof
python -m http.server 8765 --bind 127.0.0.1 --directory rendered/web-proof
```

Open `http://127.0.0.1:8765/wwwroot/`. The publish output is a static site. Keep the trailing slash when testing under a hosting subpath. A GitHub Pages deployment would need to retain the `_framework` directory (use the Pages artifact workflow or add a `.nojekyll` marker for branch-based hosting).

The **Browser proof** workflow publishes the static files, runs the native CLI with the same scenario, seed and orders, then compares the full canonical save text before and after reload in Chromium. It uses an authored campaign and requires no private fixtures. The existing spectator page has its own deployment.

This proof covers one strategic turn flow. Scenario selection, tactical battle controls, save import/export and the published hosting path are decisions for the first playable web release.
