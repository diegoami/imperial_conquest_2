# Asset specification: what the finished game needs to draw and play, and in what format

> **This is a requirements document so new art and audio can be authored — it is not an extraction
> plan.** The original *Imperial Conquest 2*'s own sprites and sounds can never ship. They live in the
> `.EXE`, the `.DAT`, and the `WAVS/` folder, and original game files never enter either repository (this
> has been the standing policy since the project's earliest RE work, and it is unchanged and
> unchallenged here). Everything below describes **what each asset must depict and how it must be
> formatted**, sourced to a screenshot, a research report, or an explicit `[designed]` tag, so that an
> artist with no access to the original can draw a faithful replacement. Nobody reading this should ever
> need to open the DAT.
>
> The original *is* on this machine, under the directory `assets.local.ini` names, which is why this
> task is `local-only`: the DAT, `WAVS/`, and the `screenshots/`/`screenshots-processed/` corpora were
> used **to describe faithfully and cite what shows it** — never to copy, trace pixel-for-pixel, or embed.

## Who this is for

The artist commissioned to draw or record replacement assets, and the implementer of any future task
that draws or plays one. Every row below is written to be actionable without also reading the engine
source: a key, a depiction specific enough to draw from, a size, a format, and a citation.

## How to read the tags

Same convention as `game-design.md` and `design-audit.md`: **[confirmed]** (direct RE evidence, cited),
**[derived]** (extrapolation from confirmed evidence), **[designed]** (new design, no RE evidence — valid
only when this document says what was searched and came up empty, per `design-audit.md` §4.5), **[open]**
(a question neither confirmed nor designed yet, with a pointer to where it will be settled).

---

## 0. Where the project actually stands

`src/IC2.Engine/Assets/AssetKeys.cs` declares **25** constants in six groups, and T11's placeholder pack
(`assets/packs/placeholder/`) holds exactly 25 matching files — flat colours and silent-but-valid audio
stubs, generated deterministically, "good enough for development and testing," never art. That set was
sized to what a placeholder generator could emit, not to what the finished game needs. Section 4 below
lists all 25 alongside every gap the finished game will actually hit: nation identity for 16 nations,
what a renderer does when a key is missing, and the chrome that later UI tasks need but nothing names
yet. **The gaps are the deliverable, not the 25.**

Nothing in this document adds a constant to `AssetKeys.cs` or a file to the placeholder pack — both are
T11's, and out of this task's Owns list. A key this document says is missing is a finding recorded here,
for a later task to act on.

---

## 1. Format specification

### 1.1 Visual assets: file format — BMP, and why not PNG

**The pack ships as uncompressed Windows BMP.** This is not the default choice — it is a decision made
once already, for a documented reason, and this document keeps it rather than re-opening it by default:

T11's placeholder generator originally emitted PNG. Its hand-rolled PNG writer **twice produced files
with structurally invalid chunks** (a little-endian chunk-length field where the format requires
big-endian, and a CRC hard-coded to `0xFFFFFFFF` instead of computed) that **green CI accepted both
times** — nothing in the test suite parsed the files deeply enough to notice, and a human reviewer caught
it, not the tests, on PR #96. T11's own regression test for this
(`PlaceholderPackIntegrationTests.ValidateBMPStructure`, in the file this task must not touch) exists
specifically because BMP's simpler, fully-specified 54-byte header made an equivalently strict,
independently-recomputed structural check practical to write; nothing comparable was ever written for the
PNG path before it was abandoned.

That specific defect was in a *generator* — a program writing PNG chunk headers by hand — not an inherent
property of the PNG format. A human using a real editor (Aseprite, GIMP, Photoshop) to export PNG would
not hit the same bug class, because they are not hand-encoding chunk lengths and CRCs. So the case for
BMP here is not "PNG is unsafe"; it is:

1. **One format, one loader, one validated pipeline.** `AssetPack.ResolveAsset` treats every key as an
   opaque relative path — the engine has no format-specific code — so splitting the pack across two
   formats buys nothing on the engine side and forces every future pack (including a hand-authored art
   pack) to carry two sets of tooling and two validators instead of one.
2. **Godot's BMP import path is still an open question, not a settled one.** T48's own hazard list says
   so explicitly: *"Godot's importer may or may not handle `.bmp` in the shape needed; a runtime `Image`
   load is the likely route. Confirm which works before building on it."* That confirmation is T48's to
   make, not this document's — but until it lands, introducing a *second* unverified format (PNG) instead
   of the one already partially exercised would be strictly worse, not better.
3. **The existing 25 files, the manifest, and every test that hashes them are already BMP.** Changing
   format now would mean re-touching T11's Owns list, which this task may not do.

**Decision: new hand-authored visual assets ship as BMP**, uncompressed, `BI_RGB` (no RLE compression),
matching the shipped pack's own `BITMAPFILEHEADER`/`BITMAPINFOHEADER` shape (54-byte header, bottom-up row
order, 4-byte row padding) that `PlaceholderPackIntegrationTests.ValidateBMPStructure` already checks
byte-for-byte. If Godot's own import behaviour (T48's open item) turns out to make BMP impractical for a
*hand-drawn* pack specifically, that is new evidence for a future revision of this decision — not a
reason to guess PNG back in now without it.

### 1.2 Visual assets: colour depth and transparency

The shipped placeholder pack is uniformly **24-bit RGB, no alpha channel** — confirmed by reading every
placeholder file's own DIB header (`width=32, height=32, bitCount=24, compression=0`). That is correct
for a **terrain tile**, which always fully covers its grid cell and never needs to show anything behind
it. It is *not* correct for a **marker drawn on top of terrain** (a unit icon, an army/fleet/city marker,
or any future UI chrome layered over the map or over another element): a fully opaque 32×32 square would
paint a solid-colour box over whatever terrain sits underneath it, which is not what the original's own
unit-map screenshots show (see §4.2–§4.4 — the army, fleet and city glyphts sit on a coloured *shape*, not
a coloured *square*, with the terrain visible around it).

**Decision**:
- **Terrain tiles**: 24-bit RGB, no alpha (fully opaque, unchanged from today).
- **Every marker/icon/chrome asset that is not a full-tile background** (unit icons, army/fleet/city
  tier markers, and every UI-chrome key in §4.7): **32-bit BGRA, straight (non-premultiplied) alpha** —
  the standard 32-bit `BI_RGB` BMP variant most image editors export when "transparency" is checked, with
  the unused byte of the file's declared 32bpp repurposed as the alpha channel. Everything outside the
  drawn silhouette is fully transparent (`alpha = 0`); everything inside is fully opaque (`alpha = 255`).
  No partial edge alpha is required (hard, pixel-art-style edges are the default fit for the source
  material — the original's own icons are hard-edged, per every screenshot cited below), but an editor
  that anti-aliases is not prohibited; nothing downstream depends on binary alpha.
- Whether the engine-side loader (Godot, via T48/T24) actually honours 32-bit BMP alpha the way this
  section assumes is **unverified and explicitly out of this document's scope to confirm** — that is the
  same open item T48's hazard list already names for BMP import generally. This section states the
  *authoring* requirement; the first task that actually loads a transparent icon on screen must record
  whether Godot's `Image`/import path preserves the alpha channel, and if not, that becomes a new finding
  against this section, not a silent workaround.

### 1.3 Visual assets: pixel dimensions

**32×32 pixels for every map-grid and marker asset** (unit icons, army/fleet/city tier markers, terrain
tiles) — unchanged from the shipped placeholder pack. This is kept rather than re-derived because it
already matches the one piece of engine code that renders a grid today: `MapViewer.cs`'s cell size is
`mapRect.Size.X / WorldPrefix.MapWidth` (a 320-column map) at a zoom the same file clamps to `1×`–`10×`
with an initial `4×` (`MapViewer.cs:10-12,138`) — i.e. an on-screen cell in the tens of pixels across the
supported zoom range, for which a single 32×32 source authored once and scaled by the engine is the right
authoring resolution (crisp at native zoom, and Godot's own texture filtering handles the rest), rather
than shipping multiple resolutions with no evidence anything needs them.

**32×32 pixels for new UI chrome too** (bottom-toolbar icons, any diplomacy/dialog iconography from
§4.7): **[designed]** — no report or the published mockup fixes a pixel size for chrome icons; searched
`game-design.md`'s UI section, `design-audit.md`, and the mockup artifact's own description, and found a
layout description but no stated icon dimension. 32×32 is chosen to keep the whole pack on one
resolution, one import path, and one visual scale language, not because any evidence requires it. If the
artist finds 32×32 illegible for a dialog-sized icon once it is on screen, that is grounds to revise this
line, not a constraint this document derived from anywhere.

### 1.4 Audio assets: format

The shipped placeholder stubs (`sfx/battle.wav`, `sfx/city_captured.wav`, `sfx/unit_move.wav`) are each a
1-second, mono, 44,100 Hz, 16-bit PCM WAV — confirmed by reading each file's own `fmt ` chunk. The
original's own `WAVS/SOUND1.WAV`…`SOUND10.WAV` are the same technical shape — mono, 44,100 Hz, 16-bit
PCM — just far shorter and more varied in length (0.02 s to 1.41 s across the ten files, read directly
from each file's `data` chunk frame count). **[confirmed: direct inspection of the files under the
directory `assets.local.ini` names]**

**Decision: new sound effects ship as mono, 44,100 Hz, 16-bit PCM WAV**, matching both the existing
placeholder stubs (so the pipeline stays one format) and the original's own technical envelope (so a
faithful re-recording does not need resampling). Duration is content-appropriate and short — the
original's own range (well under 1.5 s per effect) is the guide, not a hard cap; nothing in the engine
reads WAV duration.

**What the original's ten files actually trigger is not established, and is recorded as a gap in
§4.6**, not guessed at here: the `WAVS/` folder carries no filenames or notes correlating `SOUND1`…
`SOUND10` to a game event, and neither `docs/reports/` nor `docs/investigations/` (searched for `SOUND`
and `.WAV`) resolves it either. **[designed, search stated — see §4.6]**

### 1.5 Naming and folder convention

The shipped manifest already establishes the convention; new keys under an **existing** category follow
it exactly:

```
assets/packs/<pack-name>/<category-folder>/<name>.<ext>
```

| Key's first segment | Existing folder | Example |
| --- | --- | --- |
| `unit.*` | `units/` (plural — the one inconsistency already shipped; kept for drop-in compatibility with the current manifest, not repeated for new categories) | `units/light_infantry.bmp` |
| `army.*` | `army/` | `army/tier1.bmp` |
| `fleet.*` | `fleet/` | `fleet/tier1.bmp` |
| `city.*` | `city/` | `city/capital.bmp` |
| `terrain.*` | `terrain/` | `terrain/plain.bmp` |
| `sfx.*` | `sfx/` | `sfx/battle.wav` |

The file's own name is the key's remaining segment(s) joined by `_`, dropping the trailing `.icon`/
`.tile`/(no suffix for sfx) the same way the shipped manifest already does (`unit.light_infantry.icon` →
`light_infantry.bmp`). **A wholly new category** (the UI-chrome candidates in §4.7, should a future task
adopt any of them) should use the key's first segment, singular, as its folder — `unit.*` is the one
already-shipped exception, not the pattern to copy. None of this is enforced by code:
`AssetPack.ResolveAsset` returns whatever relative path string the manifest gives it, so this section is
a human convention for anyone authoring a manifest by hand, not a loader constraint.

### 1.6 The fallback rule

**What a renderer must do when a key is missing, and what it must never do:**

Two different code paths already exist, for two different moments, and they must stay different:

- **Pack-validation time** (`AssetLoader.ValidateAssets`, or `AssetPack.ResolveAsset` called directly):
  **failing loudly is correct.** `ResolveAsset` throws `AssetNotFoundException` naming the key;
  `ValidateAssets` returns the full list of missing keys. This runs before a pack ships, or in a test —
  never on a path a player is on — so there is nothing to protect the player from here, and softening it
  would only hide a broken pack until it hits someone.
- **Render time** (any UI code drawing a specific army, fleet, city or unit on screen, once such code
  exists — T24, and T48's own fallback for the map slice): **must call `TryResolveAsset`, never
  `ResolveAsset`, on any path that runs per-frame or per-draw.** On a miss it must:
  1. **substitute a fixed, always-present fallback** — T48's own DoD line 4 already establishes the
     pattern for the map slice (*"the affected marker falls back to T47's coloured shape"*); any later
     renderer with its own fallback shape follows the same rule: a fixed substitute the pack cannot fail
     to have, not another lookup that can itself miss;
  2. **log the failure once per missing key**, not once per frame/draw — the same key will typically miss
     on every redraw of the same marker, and a per-frame log is not a diagnostic, it is a denial-of-service
     against the log;
  3. **keep rendering.** The marker (or icon, or tile) must remain visible in its fallback form.

  It must **never**:
  - **throw an unhandled exception into the render/gameplay loop** — a missing icon is a degraded
    presentation, not a corrupted game state, and must not be treated as one;
  - **silently draw nothing** — an invisible marker for a live unit or city is strictly worse than an
    ugly but visible fallback, because it hides information the player needs (that something is there)
    behind a rendering failure the player has no way to distinguish from "nothing is there."

---

## 2. The sixteen-nation palette

### 2.1 Why this section exists: the current offenders

Sixteen nations need sixteen distinguishable colours. Today the game has **two** palettes, and neither
is fit for purpose. `godot/MapViewer.cs`'s `OwnerColor` (`:595-614`) is the one already in play, in
`NationCatalog`'s index order (`0`=Rome … `15`=Thracia, confirmed against `NationCatalog.cs` and the
DAT's own 16-name nation table at stride 1,055, `investigations/dat-file-layout.md:120-124`):

| Index | Nation | `OwnerColor` (R, G, B) |
| ---: | --- | --- |
| 0 | Rome | (0.50, 0, 0.50) |
| 1 | **Carthage** | **(1, 0, 0)** |
| 3 | **Ptolemaic** | **(0, 0, 0.50)** |
| 6 | Gaul | (0.50, 0, 0) |
| 9 | **Illyria** | **(0, 0, 0.50)** |
| 14 | **Media** | **(1, 0, 0)** |

**Carthage and Media are byte-identical: `(1, 0, 0)`.** **Ptolemaic and Illyria are byte-identical:
`(0, 0, 0.50)`.** **Rome and Gaul differ in exactly one channel** (blue: `0.50` vs `0`) — a difference
easy to lose at map scale, and how the user noticed this in play (issue #154). Sixteen nations, at most
**fourteen** distinguishable colours as shipped. This is the worked example DoD 3/8 asks for, and it is
why the check below is not optional.

### 2.2 The accessibility check applied

The palette below was constructed, then verified, against three conditions simultaneously — not just
picked by eye:

1. **Normal colour vision**, using CIE76 ΔE in CIELAB space (perceptually-uniform-ish colour distance;
   a difference below ≈2.3 is a "just noticeable difference," below ≈10 is often still hard to tell apart
   quickly at a glance, and the palette below targets well clear of both).
2. **Simulated protanopia and deuteranopia** (the two red-green colour-vision deficiencies that would
   most threaten a "just use more distinct reds and greens" palette), via a standard LMS cone-response
   transform with the deficient cone's response reconstructed from the other two (the Brettel/Viénot
   approach) — every candidate colour was projected through both simulations and re-measured in Lab
   space, not just eyeballed on a normal-vision screen.
3. **The map's own terrain colours as fixed background anchors** — `MapViewer.TerrainColor`'s seven
   values (deep/coastal sea, plain, desert, forest, mountain, and the generic fallback) — because DoD 3
   asks for a palette that "survives being drawn small, **on the map's terrain**," not just against its
   own fourteen other members.

The palette below was optimised (a randomised hill-climb over hue/saturation/lightness, maximising the
single worst pairwise distance across all three conditions above, including nation-vs-terrain pairs) to a
**worst-case pairwise ΔE of 18.7** — comfortably clear of the ~10 "quick glance" threshold in every one of
the three checked conditions, and far clear of the ~2.3 JND. (For comparison: the current `OwnerColor`
table's worst case is ΔE = 0, twice.)

### 2.3 The palette

Sixteen colours, in `NationCatalog` order. **Dark glyph** means the colour is light enough that a marker
glyph drawn on top of it (city roofline, unit silhouette, etc.) should be dark, mirroring `MapViewer.cs`'s
existing `codes 4,5,7,8,13,14 ? MarkerOutline : CityColor` rule at `:178` — that exact code list is
**superseded** by this palette (it was keyed to the *old* colours) and must be replaced with the list
below wherever it is applied, per DoD 8's folded follow-up (`#154`, T24 DoD 7).

| Index | Nation | Hex | RGB | Relative luminance | Glyph |
| ---: | --- | --- | --- | ---: | --- |
| 0 | Rome | `#4C0D19` | (76, 13, 25) | 0.019 | light |
| 1 | Carthage | `#DDB69C` | (221, 182, 156) | 0.512 | **dark** |
| 2 | Seleucid | `#671E0B` | (103, 30, 11) | 0.038 | light |
| 3 | Ptolemaic | `#E1EC25` | (225, 236, 37) | 0.761 | **dark** |
| 4 | Macedonia | `#719D16` | (113, 157, 22) | 0.277 | light |
| 5 | Numidia | `#C3E155` | (195, 225, 85) | 0.661 | **dark** |
| 6 | Gaul | `#107A16` | (16, 122, 22) | 0.141 | light |
| 7 | Greece | `#93F6D6` | (147, 246, 214) | 0.770 | **dark** |
| 8 | Celtiberia | `#29796D` | (41, 121, 109) | 0.153 | light |
| 9 | Illyria | `#A7DCF8` | (167, 220, 248) | 0.662 | **dark** |
| 10 | Dacia | `#237084` | (35, 112, 132) | 0.136 | light |
| 11 | Bithynia | `#5F50E6` | (95, 80, 230) | 0.139 | light |
| 12 | Galatia | `#4C0C8D` | (76, 12, 141) | 0.037 | light |
| 13 | Armenia | `#D760E8` | (215, 96, 232) | 0.286 | light |
| 14 | Media | `#581B45` | (88, 27, 69) | 0.033 | light |
| 15 | Thracia | `#D46CBB` | (212, 108, 187) | 0.283 | light |

**Relative luminance** uses the standard sRGB-to-linear + WCAG luminance formula
(`0.2126R + 0.7152G + 0.0722B` in linear space); the dark/light glyph rule uses the same `0.4` cut the
example above illustrates informally (every colour above and below that cut is unambiguous — no value
lands near the boundary).

**Where this lands**: `NationDefinition.ColorHex` (`src/IC2.Engine/Model/World.cs:215`) is the one field
both `MapViewer.cs`'s `OwnerColor` and the Godot slice's `NationColor` (`godot/Slice/Slice.cs:270-276`,
which explicitly reads `World.NationById(id).ColorHex` and is the helper T48 says to reuse instead of
copying `OwnerColor`) are meant to converge on. Today only the two-nation toy world sets `ColorHex`
(`data/worlds/toy-3city.json:178,199`, arbitrary placeholder values); no task has yet populated it for the
sixteen real nations. **This table is that population**, ready for T29 (or whichever task first writes
`data/worlds/classical-mediterranean.json`) to use verbatim, and for T24's folded follow-up (`#154`) to
apply to `MapViewer.cs`'s `OwnerColor` and its `:178` light-colour list — which becomes indices
`{1, 3, 5, 7, 9}`, replacing `{4, 5, 7, 8, 13, 14}`.

---

## 3. Army, fleet and city size tiers: one subject at three sizes, not three subjects

**User testimony, from play experience, confirms the tier *count* directly: "when it comes to towns and
armies and navies and cities there were three variants."** This agrees with, and is now corroborated by,
what the decompilation already established for armies and fleets (recorded against research item
`5e0e814`). The single most useful thing to tell an artist about this family: **a size tier is the same
subject drawn larger/heavier, not a different unit or a different building.** A `tier2` icon should read,
at a glance, as "a bigger version of `tier1`," not as an unrelated shape — exactly what the placeholder
pack's own `ArmyTierIcons_ArePixelDifferent`-style tests require (visually distinct, but the same
family).

### 3.1 Armies — three bands, thresholds confirmed

`FUN_0044A80C` (`decompiled-unit-map-orders-and-record-fields.md`, part 3): with `t = troops / 1000`,

```
marker = owner + (t < 25 ? 200 : t < 50 ? 216 : 232)
```

**[confirmed]** — `army.tier1.icon` = **under 25,000 troops**, `army.tier2.icon` = **25,000–49,999**,
`army.tier3.icon` = **≥ 50,000**. Depict the same army silhouette at increasing size/weight (more
figures, a denser formation, a larger banner) across the three tiers — cite the original's own red/purple
unit-map glyph for the shape family (`screenshots-processed/1_rome_270_summer_7_1.png`,
`screenshots-processed/1_rome_270_autumn_7_1.png`: a small upright figure pictogram on a nation-coloured
square).

### 3.2 Fleets — three bands, thresholds confirmed

`FUN_0044A878` (same report): with `s = ships`,

```
marker = owner + (s < 25 ? 300 : s < 50 ? 316 : 332)
```

**[confirmed]** — `fleet.tier1.icon` = **under 25 ships**, `fleet.tier2.icon` = **25–49**,
`fleet.tier3.icon` = **≥ 50**. Cross-checked against two real fleets: a 90-ship fleet reads marker `333`
(band 332 + owner 1, Carthage) and a 70-ship fleet reads `335` (band 332 + owner 3, Ptolemaic) — both
correctly in the ≥ 50 band. Depict a ship silhouette (the original's unit-map glyph reads as a small
diamond/hull shape on blue water — `screenshots-processed/1_rome_270_summer_7_1.png` shows this at the
sea tiles) at increasing size/count across the three tiers — a single ship, a small cluster, a larger
formation.

### 3.3 Cities — three bands (count confirmed), thresholds open

The map cell code does **not** size-band cities the way it does armies/fleets: a city's cell code
(20–199, `terrain-move-cost-table-in-dat.md`) is a pure identity/index with no population term in the
arithmetic — so, unlike armies and fleets, there is no marker-arithmetic shortcut that hands the tier
thresholds over for free. **Do not copy the army/fleet troop/ship boundaries onto cities** — they are a
different quantity (population) encoded in a cell-code space 180 values wide, far broader than three
16-wide bands, which is itself evidence the city code is carrying more than a three-way size split
somewhere in its range.

**What *is* now settled**: the tier *count* is three, plus the capital as a fourth, orthogonal state —
matching the user's own testimony above, and matching what the placeholder pack already ships
(`city.tier1/2/3.icon` + `city.capital.icon`). `AssetKeys.cs`'s own doc comment on the city tier group
already carries this exact caveat (*"placeholder pending confirmation of the original's own city-size
display logic"*) and it is still correct about the **thresholds**, even though the **count** is no longer
open. **The population thresholds themselves are `[open]`** — queued as decompilation plan item 16, not
yet run. Until that lands, a specific number (e.g. "under 10,000 population") would be invented, not
designed-with-reasoning, so none is given here.

Depiction, sourced to the original's own unit-map glyph (`screenshots-processed/1_rome_270_summer_7_1.png`,
`screenshots-processed/1_rome_270_autumn_7_1.png`, `screenshots/1_cartago_271_spring_3_1.png`): a small
peaked-roof building pictogram — a roofline plus two side walls, matching `MapViewer.cs`'s own synthetic
`DrawCity` glyph (`:178-182`, drawn as three line segments forming exactly this roof-and-walls shape) —
on a nation-coloured square background, sized to the tile. Across the three population tiers, depict the
same building motif at increasing scale/elaboration (a single small structure → a walled cluster → a
denser walled town), the same "bigger, not different" rule as armies and fleets. The capital
(`city.capital.icon`) is orthogonal to population size (`NationDefinition.CapitalCityId` is a separate
flag) — give it a distinguishing mark (a raised banner, a distinct roofline) layered on *whichever*
population tier it happens to be, not a size tier of its own; this is a `[designed]` rendering choice
(searched `game-design.md` §"City markers" and the screenshots corpus for a distinct capital glyph and
found none — the original's own screenshots show the same generic building glyph for Rome-the-capital as
for any other city; the *data* panel, not the sprite, is what names it "Rome (capital of Rome)" in
`screenshots-processed/processed/12_rom_1.png`).

---

## 4. The asset inventory

### 4.1 Unit icons — complete roster, no gap

Five unit types, and confirmed as the complete strategic-map roster (no elephants, siege engines, or
other types anywhere in the design or the audit — `game-design.md`'s Combat/Recruitment sections and
`design-audit.md`'s missing-mechanics sweep, §1, name exactly these five throughout and introduce no
sixth).

| Key | Depicts | First needed by | Exists today |
| --- | --- | --- | --- |
| `unit.light_infantry.icon` | Light infantry — a lightly-armoured foot soldier, minimal shield | T48 (first on-screen draw), T24 | Yes |
| `unit.heavy_infantry.icon` | Heavy infantry — armoured foot soldier, large shield, close order | T48, T24 | Yes |
| `unit.archers.icon` | Archers — a bow-armed foot soldier | T48, T24 | Yes |
| `unit.light_cavalry.icon` | Light cavalry — a lightly-armoured mounted soldier | T48, T24 | Yes |
| `unit.heavy_cavalry.icon` | Heavy cavalry — an armoured mounted soldier, heavier tack | T48, T24 | Yes |

These five feed the context panel's army-composition display (`game-design.md` §UI item 2), not the map
grid directly (the map grid uses the army/fleet/city tier markers in §3, not per-unit-type icons — T48
DoD 2 records that an army mixing several unit types needs its own stated drawing rule, `[designed]`,
separate from this table). No new key is needed here; this group is complete.

### 4.2 Army markers — see §3.1

`army.tier1.icon`, `army.tier2.icon`, `army.tier3.icon` — thresholds and depiction in §3.1. First needed
by T48, then T24. Exists today.

### 4.3 Fleet markers — see §3.2

`fleet.tier1.icon`, `fleet.tier2.icon`, `fleet.tier3.icon` — thresholds and depiction in §3.2. First
needed by T48, then T24. Exists today.

### 4.4 City markers — see §3.3

`city.tier1.icon`, `city.tier2.icon`, `city.tier3.icon`, `city.capital.icon` — tier count and depiction in
§3.3; population thresholds `[open]`, pointer given. First needed by T48, then T24. Exists today.

### 4.5 Terrain tiles — complete for today's renderer, one real gap underneath

| Key | Depicts | First needed by | Exists today |
| --- | --- | --- | --- |
| `terrain.plain.tile` | Flat grassland — uniform green, no motif | T47 (data only, "no art"), T48 (first art) | Yes |
| `terrain.desert.tile` | Arid/sand terrain — tan/yellow | T48 | Yes |
| `terrain.forest.tile` | Wooded terrain — green base with a dense tree/brush motif | T48 | Yes |
| `terrain.mountain.tile` | Mountainous terrain — jagged grey/brown peaks over the base | T48 | Yes |
| `terrain.river.tile` | A river-bearing tile | T48 | Yes, but see below |
| `terrain.sea_coastal.tile` | Shallow/coastal sea — lighter blue, wave motif | T48 | Yes |
| `terrain.sea_deep.tile` | Deep sea — darker blue, same wave motif | T48 | Yes |

Depiction of plain/forest/mountain/sea, cited directly: `screenshots-processed/1_rome_270_summer_7_1.png`
and `screenshots-processed/1_rome_270_autumn_7_1.png` show, on the "Unit map" panel, a flat green plain
tile; a green tile bearing a dense dark tree-clump motif for forest; a green tile bearing jagged grey
triangular peaks for mountain; and a uniform tile of small horizontal wave-squiggles in blue for sea. The
engine's own placeholder colour fill (`MapViewer.TerrainColor`, `:624-636`) already encodes a *darker*
blue for deep sea against a *lighter* blue for coastal — carry that same relative distinction into the
tile art, not just the colour swatch. Desert is `[designed]` for exact hue (tan/yellow is the
conventional reading of "arid"; no screenshot in the local corpus shows a desert tile directly — searched
both `screenshots/` and `screenshots-processed/` and found none) but the terrain code's own name
("Desert," confirmed in `terrain-move-cost-table-in-dat.md`) is not in doubt.

**The real gap, found while cross-checking this section**: the world model's own terrain table has
**12 tile-*type* codes and 6 distinct *names*** (`design-audit.md` §2.12) — Sea ×2 (coastal/deep, already
two separate keys above), Plain, Desert, Forest, Mountains, and **River ×6** (`river_1`…`river_6`, cell
codes 6–11, `data/worlds/toy-3city.json:114-120` and `terrain-move-cost-table-in-dat.md`). Those six river
codes are not six flavours of river — they are **connectivity codes**: `MapViewer.cs`'s own
`DrawRiver(code)` (`:161-169`) draws a different combination of the four cardinal edges for each of the
six values (e.g. code `6` draws right+left, code `7` draws up+down, codes `8`–`11` draw the four bends).
That is a **tile-connection/autotile problem**, and one static `terrain.river.tile` sprite cannot depict a
connected river network correctly — a straight horizontal reach and a bend look identical today only
because the current inspector draws rivers as procedural vector lines, never a sprite. **If a future
renderer (T24) draws terrain from the sprite pack instead of procedurally, it will need up to six river
tile variants — one per connectivity code — mapped 1:1 to `river_1`…`river_6`, not the single
`terrain.river.tile` key that exists today.** No `AssetKeys` change is proposed here (T11's list, out of
this task's Owns list); this is recorded as a finding for whichever task first draws terrain from
sprites, tagged `[designed]` (searched `game-design.md`, `design-audit.md` and `MapViewer.cs` for an
existing river-tile-variant convention and found none — the six-way connectivity split above is `[confirmed]` from the code and the world data; only *whether a sprite renderer needs six sprites* is
new, and follows directly from that confirmed fact).

### 4.6 Sound effects — three silent stubs, and the mapping gap

| Key | Trigger | First needed by | Exists today |
| --- | --- | --- | --- |
| `sfx.city_captured` | A city changes owner by force (`"falls to"`) | Not wired by any task's DoD yet | Yes (silent stub) |
| `sfx.battle` | A battle resolves | Not wired by any task's DoD yet | Yes (silent stub) |
| `sfx.unit_move` | An army or fleet completes a move order | Not wired by any task's DoD yet | Yes (silent stub) |

Searched `task-catalogue.md` in full for `sound`/`sfx`: no task's Done-when line currently plays any of
these three, or any other sound — the three stubs exist as placeholder-generator output, never called.
**[confirmed by search — see task-catalogue.md's own text: "the sounds beyond three stubs" names this
exact gap]**

**The mapping gap** (§1.4): the original's `WAVS/SOUND1.WAV`…`SOUND10.WAV` carry no filename-to-event
correlation anywhere in the local corpus or in `docs/reports/`/`docs/investigations/`. `[designed, search
stated]` — determining which of the ten corresponds to, say, "city captured" versus "battle" versus a
UI click would require capturing audio from a live session at the moment each event fires, which is
outside this pass; it is recorded here as unresolved rather than guessed.

**Candidate additional sfx keys — none required by any current task, all `[designed]`, listed so a
future task doesn't have to rediscover the need**: a distinct naval-loss sting (the confirmed *"A fleet
belonging to X is lost at sea"* news line, `design-audit.md` §1.2/§2.9a, has no accompanying sound
today), a diplomacy-proposal alert (T19 DoD 10's modal *"X wants to trade/ally"* dialog), a turn/round-end
chime, and a command-rejected error tone. None of these has RE evidence for *whether* the original had a
distinct sound at all beyond the ten undifferentiated files above; each would be a new UI/audio-design
choice for whichever task first wires audio playback, not a fidelity requirement.

### 4.7 Chrome with no key today — the main-screen, dialog, and battle-result gaps

Checked against the callouts in `task-catalogue.md`'s own T49 scope line (*"the fortification and siege
states T17 and T18 will draw, battle-screen sprites for T16, the news and dialog chrome T23 and T24
need"*) one at a time, against what each of those tasks' own Done-when lines and `game-design.md`'s UI
section actually require:

- **T16 battle-screen sprites: none needed.** T16's own scope is explicit that `BattleResult` "must stay
  presentation-agnostic" — it is an engine task and draws nothing. The eventual battle-result *screen*
  (T25, following `game-design.md` §UI item 3) is specified as a **numeric/text summary** — winner/loser,
  casualties, captured money/supplies, unity swing — with no icon named anywhere in that description or
  in the published mockup. **No new asset key is required for any current DoD.** If a future task wants a
  visual flourish (a victory/defeat banner), that is a new gap to record *then*, tagged `[designed]` at
  that point — inventing one now would be art direction this document is not supposed to specify.
- **T17/T18 fortification and siege states: no sprite exists, and none is confirmed needed.** Checked the
  original's own screenshots directly: `screenshots-processed/1_rome_270_summer_7_1.png` shows Felsina at
  51% fortification with the *same* generic building glyph every other city uses — the percentage is
  carried in the text info panel (`screenshots-processed/processed/12_rom_1.png`: "Fortification 78%
  (70,000)"), not in a distinct sprite. **[confirmed, from direct screenshot inspection]** the original
  itself draws no visual difference for fortification level or active siege on the map. A future task may
  still choose to add one for at-a-glance legibility (a besieged-city ring/overlay, say) — that would be a
  genuinely new `[designed]` UI decision with no original to be faithful *to*, not a missing recreation.
  Recorded here as an open option, not a requirement.
- **T24 bottom filter toolbar: no new asset — reuses the pack.** `game-design.md` §UI item 2 says this
  directly: *"the original's city/army/fleet-type filter icons ([`menu-and-toolbar-inventory.md`]),
  reused as map-overlay toggles."* The toolbar's icons are the same unit/army/fleet/city icons already in
  §4.1–§4.4, not a new set.
- **T24 diplomacy relation grid (peace/trade/alliance/war): no new asset — text/colour, per the cited
  report.** `game-design.md` §UI item 4 describes it as *"the original's peace/trade/ally/war grid...
  reused as-is"* from `menu-and-toolbar-inventory.md`'s International Relations screen — a grid, not an
  iconset. No pictorial badge is required to be faithful to the cited source; a future task adding one for
  polish would be a new `[designed]` choice.
- **T24/T25 hotseat handoff screen, and T19's pending-offer dialog: no new asset required.**
  `game-design.md` §UI item 5 specifies the handoff screen by its function (a blocking "Pass to
  [Nation]" screen) and T19 DoD 10 specifies the offer dialog by its exact text (*"X wants to trade with
  Y."*) — both are named as text/flow requirements, with nation identity already covered by §2's colour
  palette. Neither cites or implies an icon.

**Net finding for this section**: every UI-chrome gap the T49 scope line calls out resolves to "no new
asset is actually required by anything currently specified," once checked against what each citing
task's own text says, **except** the two genuine gaps already recorded above in §4.5 (river connectivity
sprites) and §4.6 (the sound-mapping gap and the candidate new sfx keys). That is a finding, not a
loophole: the callouts were right that nothing *named* these assets before this document; having checked,
most of them turn out not to need naming yet, and this section is the record of that check so a future
task does not have to redo it.

---

## 5. The drift test

`tests/IC2.Engine.Tests/Assets/AssetSpecificationCoverageTests.cs` (new file, this task's Owns list)
asserts, in both directions, against the fenced block in §6 below:

1. Every string in `AssetKeys.AllKeys` (read live from the compiled engine, not copied into the test)
   appears as its own line in §6's fenced block.
2. Every line in §6's fenced block also appears in `AssetKeys.AllKeys` — so a stray or misspelled "existing"
   key claimed here without a matching constant fails the same way a missing one does.

Gap/candidate keys throughout §4 (river variants, new sfx, chrome candidates) are deliberately **not** in
that block — DoD 4 only requires the *existing* set to round-trip; a proposed key that does not exist yet
must not make the test pass by accident, and must not make it fail either, since `AssetKeys.cs` is out of
this task's Owns list to change.

## 6. Existing keys — machine-checked ground truth

The exact 25 keys `AssetKeys.AllKeys` yields today, one per line, in the same six-group order as
`AssetKeys.cs` itself. This block is read verbatim by `AssetSpecificationCoverageTests.cs`; do not
reformat it without updating that test's expectations.

```text
unit.light_infantry.icon
unit.heavy_infantry.icon
unit.archers.icon
unit.light_cavalry.icon
unit.heavy_cavalry.icon
army.tier1.icon
army.tier2.icon
army.tier3.icon
fleet.tier1.icon
fleet.tier2.icon
fleet.tier3.icon
city.tier1.icon
city.tier2.icon
city.tier3.icon
city.capital.icon
terrain.plain.tile
terrain.desert.tile
terrain.forest.tile
terrain.mountain.tile
terrain.river.tile
terrain.sea_coastal.tile
terrain.sea_deep.tile
sfx.city_captured
sfx.battle
sfx.unit_move
```

---

## 7. Sourcing ledger

Every depiction claim above traces to one of:

- **A screenshot filename**, cited inline wherever used, from `screenshots/` or `screenshots-processed/`
  under the directory `assets.local.ini` names — never a byte, pixel, or frame of the file itself is
  reproduced here or anywhere in the repository.
- **A research report or investigation**, cited inline by filename (`decompiled-unit-map-orders-and-
  record-fields.md`, `terrain-move-cost-table-in-dat.md`, `design-audit.md` sections, `investigations/
  dat-file-layout.md`), several already cited by `game-design.md`/`design-audit.md` and re-cited here for
  the same claim, not re-derived independently.
- **An explicit `[designed]` tag** stating what was searched and came up empty, per `design-audit.md`
  §4.5, everywhere one appears above (desert tile colour, city-capital glyph distinction, UI-chrome pixel
  size, the sfx-mapping gap, the candidate new sfx keys, the optional siege/fortification overlay).

No claim in this document rests on having opened the `.EXE`'s or `.DAT`'s own image/audio resources —
only on the DAT's *data* tables (nation names, terrain codes, marker arithmetic — all numeric/structural,
already covered by the project's standing decompilation work), the screenshots corpus, and existing
research reports.
