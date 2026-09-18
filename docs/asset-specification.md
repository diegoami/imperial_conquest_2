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
for a **terrain tile**, which always fully covers its grid cell.

**Correction (rework round 1): every marker examined in the corpus is fully opaque, and the previous
version of this section justified 32-bit alpha with a claim the screenshots contradict.** Direct
inspection of both cited screenshots shows the army marker and every city-building variant (§3.3) as a
**solid nation-coloured square filling the entire 32×32 cell**, and the fleet marker (§3.2) as an
opaque **diamond** — a black ship silhouette on a coloured halo that itself fills the cell corner to
corner, with zero sea-terrain pixels visible inside that cell. `rivers-and-map-markers.md` depends on
this too: it samples "the corners of registered city tiles" to read owner colour, which only works if
the corners are opaque. So **"terrain visible around the marker" is false, and there is no fidelity
argument for alpha** — a pack that matched the original exactly could ship every asset as opaque 24-bit,
including markers.

**The real, checkable argument for 32-bit BGRA is narrower, and is this**: the original's own convention
is not uniformly square. The fleet marker is an opaque **diamond**, not a square (§3.2) — and a plain
24-bit BMP is inherently a filled rectangle with no way to declare "these corner pixels are not part of
the sprite." Reproducing a diamond, a circle, or any silhouette narrower than its bounding box (the
fleet's diamond today; potentially other shapes if a future glyph follows suit) needs either an alpha
channel or a hard-coded background colour that must exactly match whatever happens to sit behind it —
which breaks the moment the pack, the terrain palette, or the background changes. Alpha is the only one
of those two options that stays correct regardless of what is drawn underneath.

**Decision**:
- **Terrain tiles**: 24-bit RGB, no alpha (fully opaque, unchanged from today, and matching the
  original's own fully-opaque tile convention).
- **Every marker/icon/chrome asset that is not a full-tile background** (unit icons, army/fleet/city
  tier markers, and every UI-chrome key in §4.7): **32-bit BGRA, straight (non-premultiplied) alpha** —
  the standard 32-bit `BI_RGB` BMP variant most image editors export when "transparency" is checked, with
  the unused byte of the file's declared 32bpp repurposed as the alpha channel. This is specified
  uniformly across the whole non-terrain category — including assets whose silhouette genuinely is a
  full square, like the army and city markers — for one format and one validator across the pack, rather
  than a third BMP variant reserved for exactly one shape. **A marker whose silhouette is a full square
  may fill every pixel opaque** (`alpha = 255` throughout, functionally identical to 24-bit); the alpha
  channel exists so a diamond, a circle, or any other non-square silhouette *can* leave its corners
  transparent, not because every asset is required to use them.
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
single worst pairwise distance across all three conditions above, including nation-vs-terrain pairs).
The true worst case, under that definition, is **ΔE 18.64** — deuteranopia, Carthage (`#DDB69C`) against
the plain terrain anchor `(0.46, 0.66, 0.37)`. The worst *nation-vs-nation* pair alone (ignoring terrain)
is ΔE 18.71, under protanopia, Carthage against Greece; **18.7 is that nation-only figure, not the
figure for the metric as this section defines it, and is corrected here to 18.6.** Both numbers land in
the same place for the purpose this section exists for: comfortably clear of the ~10 "quick glance"
threshold under every one of the three checked conditions, against nations and against terrain, and far
clear of the ~2.3 JND. (For comparison: the current `OwnerColor` table's worst case is ΔE = 0, twice.)

**Method sensitivity, stated rather than hidden**: the protanopia/deuteranopia figures above apply the
Viénot/Brettel LMS substitution in **linear RGB** (the colour-science-correct space for a cone-response
transform). Applying the same substitution in gamma-encoded sRGB instead — an easy mistake, and one this
document does not make, but a plausible one for anyone re-deriving this check — drops the global minimum
to **16.00**, still clear of both thresholds but by a smaller margin. Anyone re-running this check should
confirm which variant they are using before comparing results to this section's numbers.

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
| 4 | Macedonia | `#719D16` | (113, 157, 22) | 0.277 | **dark** |
| 5 | Numidia | `#C3E155` | (195, 225, 85) | 0.661 | **dark** |
| 6 | Gaul | `#107A16` | (16, 122, 22) | 0.141 | light |
| 7 | Greece | `#93F6D6` | (147, 246, 214) | 0.770 | **dark** |
| 8 | Celtiberia | `#29796D` | (41, 121, 109) | 0.153 | light |
| 9 | Illyria | `#A7DCF8` | (167, 220, 248) | 0.662 | **dark** |
| 10 | Dacia | `#237084` | (35, 112, 132) | 0.136 | light |
| 11 | Bithynia | `#5F50E6` | (95, 80, 230) | 0.139 | light |
| 12 | Galatia | `#4C0C8D` | (76, 12, 141) | 0.037 | light |
| 13 | Armenia | `#D760E8` | (215, 96, 232) | 0.286 | **dark** |
| 14 | Media | `#581B45` | (88, 27, 69) | 0.033 | light |
| 15 | Thracia | `#D46CBB` | (212, 108, 187) | 0.283 | **dark** |

**Relative luminance** uses the standard sRGB-to-linear + WCAG luminance formula
(`0.2126R + 0.7152G + 0.0722B` in linear space).

**Correction (rework round 1): the glyph cut is not 0.4, and is not picked by eye.** The two colours
`MarkerOutline` and `CityColor` (`MapViewer.cs`) actually draw the glyph — dark glyph
`(0.07, 0.09, 0.14)`, luminance ≈0.0086, and light glyph `(1.0, 0.97, 0.86)`, luminance ≈0.931. The
background luminance at which the WCAG contrast ratio of each glyph colour against that background is
equal — the correct cut, derived from the two colours the code actually uses, not asserted — is
**`Lbg ≈ 0.190`**, giving ≈4.09:1 either way at the boundary. Applying **0.4** instead (the previous,
unsourced value) misclassified three nations as light-glyph when dark gives meaningfully better
contrast: Macedonia (0.277), Armenia (0.286) and Thracia (0.283) each sit **below** 0.4 but **above**
0.190 — under the old cut they got the light glyph at **~2.9–3.0:1**, under the 3:1 floor this table
exists to avoid missing again (`#154` was exactly a glyph going invisible); under the corrected 0.190
cut they get the dark glyph at **~5.6–5.7:1**. The table above uses the corrected cut: **dark-glyph
indices are `{1, 3, 4, 5, 7, 9, 13, 15}`**, superseding both the original code's `{4, 5, 7, 8, 13, 14}`
and this document's own first-round `{1, 3, 5, 7, 9}`.

**Where this lands**: `NationDefinition.ColorHex` (`src/IC2.Engine/Model/World.cs:215`) is the one field
both `MapViewer.cs`'s `OwnerColor` and the Godot slice's `NationColor` (`godot/Slice/Slice.cs:270-276`,
which explicitly reads `World.NationById(id).ColorHex` and is the helper T48 says to reuse instead of
copying `OwnerColor`) are meant to converge on. Today only the two-nation toy world sets `ColorHex`
(`data/worlds/toy-3city.json:178,199`, arbitrary placeholder values); no task has yet populated it for the
sixteen real nations. **This table is that population**, ready for T29 (or whichever task first writes
`data/worlds/classical-mediterranean.json`) to use verbatim, and for T24's folded follow-up (`#154`) to
apply to `MapViewer.cs`'s `OwnerColor` and its `:178` light-colour list — which becomes indices
`{1, 3, 4, 5, 7, 9, 13, 15}`, replacing `{4, 5, 7, 8, 13, 14}`.

---

## 3. Army, fleet and city marker variants: confirmed for two of three, open for the third

**Correction (rework round 1): this section previously extended a size-tier reading to cities on the
strength of a mid-task correction that turned out to be wrong on both the variant count (three, not
five) and the occupied code range (180-wide, not 80-wide) — see §3.3.** What holds, confirmed directly
from the decompilation for **armies and fleets only**: each is a genuine three-band size split, and the
single most useful thing to tell an artist about *those two* families is that **a size tier is the same
subject drawn larger/heavier, not a different unit** — a `tier2` icon should read, at a glance, as "a
bigger version of `tier1`," exactly what the placeholder pack's own `ArmyTierIcons_ArePixelDifferent`-
style tests require (visually distinct, but the same family). **Cities do not get that same "three
sizes" framing** — §3.3 below has the corrected picture: five confirmed map-code variants, an open
question about what they mean, and direct evidence the original draws more than one distinct building
shape, not one shape at three sizes.

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
correctly in the ≥ 50 band.

**Correction (rework round 1): the depiction below previously cited a screenshot with no fleet in it,
and misdescribed the glyph.** The 90-ship Carthaginian fleet cross-checked above is the one visible on
the map in `screenshots/1_cartago_271_spring_3_1.png` (its own info panel reads *"Fleet of Carthage /
Ships 90"*), not `screenshots-processed/1_rome_270_summer_7_1.png`, which has no fleet marker anywhere
on its unit-map panel. Direct pixel inspection of the real glyph, at 8x zoom: it is a **black ship
silhouette — hull, mast, sail and an anchor at the base — on a cyan halo, both filling the tile as a
diamond inscribed corner-to-corner in the 32×32 cell**, not "a small diamond/hull shape on blue water."
The diamond's four points meet the midpoints of the tile's four edges; the triangular regions between
the diamond and the tile's actual corners are filled cyan, not sea texture, so — like the army and city
markers — the cell is **fully opaque**, with no terrain visible through it (see §1.2's corrected
transparency reasoning). Depict a ship silhouette of this shape at increasing size/count across the
three tiers — a single ship, a small cluster, a larger formation — sourced to this glyph, not the
earlier, unverifiable citation.

### 3.3 Cities — five confirmed map-code variants; meaning open; direct evidence of more than one glyph shape

**Correction (rework round 1, superseding this section's first draft in full).** The first draft of this
section was built on a mid-task correction that was itself wrong on two points, both now fixed upstream
(research commit `c3cb609`; the residual inconsistency between reports is filed as build-repo issue
`#175`) — and on a `[designed]` search claim (the capital glyph) that the cited screenshots directly
contradict. Both are corrected here from primary evidence, not patched over.

**The confirmed encoding.** `rivers-and-map-markers.md`, verified against every city in the data:

```
map code = 20 + owner + 16 × variant,  variant ∈ {0, 1, 2, 3, 4}
```

holding for **334/334 cities**. This is the *same* `owner + 16 × band` shape armies (`200/216/232`) and
fleets (`300/316/332`) use — so, contrary to this section's first draft, **the city map code does carry
a band term**; "a pure identity/index with no population term in the arithmetic" was this document's own
unsupported inference, not something any cited report says. Five 16-wide bands starting at 20, variants
0–4, top out at `20 + 15 + 4×16 = 99` — an **80-value space (20–99)**, not the 180-value "20–199" the
first draft attributed to `terrain-move-cost-table-in-dat.md`. That report's own city-code note gives
20–99; "20–199" is a range from `rivers-and-map-markers.md` that the earlier draft mis-cited as the
*occupied* range when the report itself gives it as the range that was *searched*. Both corrections are
upstream, in the research repo, not repeated here as new derivations.

**So: the original has five city map-code variants, not three.** The placeholder pack ships four keys
(`city.tier1/2/3.icon` + `city.capital.icon`). **Five confirmed variants against four shipped keys is
itself the gap this section now records**, rather than closing early the way the first draft did. This
document does not know, and does not guess, whether the fifth variant is a fourth population tier, a
distinct building type orthogonal to size, the capital flag folded into the same code space, or
something else — `rivers-and-map-markers.md` states plainly that "the variant's meaning (city size, icon,
or another display category) still needs confirmation," and that is exactly where it stays until
decompilation plan item 16 (rewritten per `c3cb609`) settles it. **Do not copy the army/fleet troop/ship
thresholds onto cities** — a population number attached to a city tier here would be invented, not
designed-with-reasoning, and no specific threshold is given.

**Direct evidence, from looking again at the corpus myself: the original draws more than one city
building shape, correcting this section's first draft outright.** The first draft's capital-glyph
`[designed]` tag stated a search of `screenshots-processed/1_rome_270_summer_7_1.png` and
`screenshots/1_cartago_271_spring_3_1.png` that found "the same generic building glyph" everywhere. That
search result was false, and I re-ran it by eye, at 6–10x pixel zoom, tile by tile:

- **A plain house**: a peaked roof over a rectangular body with one or two vertical window/support
  bars — the shape most city tiles use, in every nation colour sampled (e.g. cyan glyph on a dark-maroon
  tile, magenta glyph on cyan, white glyph on purple, gold glyph on navy — all in
  `screenshots-processed/1_rome_270_summer_7_1.png`).
- **A columned temple with a stepped triangular pediment and three columns**, structurally distinct from
  the house — not a recolour, a different silhouette — on a purple-nation tile north of the river bend in
  the same screenshot, `screenshots-processed/1_rome_270_summer_7_1.png` (roughly a third of the way down
  the panel, west of a north–south river reach).
- **A crenellated walled castle with two corner towers and a gatehouse**, a third distinct silhouette, on
  a red-nation tile in `screenshots/1_cartago_271_spring_3_1.png` (the southernmost city marker visible in
  that panel).

That is **at least three distinct building shapes**, not one uniform glyph — direct, first-hand
confirmation (not merely a report citation) that the original's city iconography varies by more than
colour, and a second, independent line of evidence for the same conclusion the five-variant map code
already implies. **What this does not establish**: which shape (if any) is reserved for capital status
specifically, as opposed to being what a population tier or the still-unconfirmed fifth variant selects.
I cannot tell, from a screenshot alone and without the underlying save data, whether the purple
temple-tile or the red castle-tile is that nation's *capital*, a large city, or simply a different
nation's standard style — correlating a specific tile's variant code, its `PopulationThousands`, and its
`CapitalCityId` flag is exactly decompilation plan item 16's job, not something derivable by eye. So:
**five confirmed variants, at least three confirmed distinct glyph shapes, meaning of both still open —
this is not "three sizes," and this document does not resolve it.**

**Depiction for an artist, kept deliberately general given the above**: draw at least the three shapes
directly observed — a small house, a columned temple, a walled castle — as the working example of the
kind of variety the five-variant code plausibly selects between, each recognisable at 32×32 and each
distinct from the others in silhouette, not just colour, matching `MapViewer.cs`'s own synthetic
`DrawCity` glyph shape (`:179-182`, three line segments forming a roofline-and-walls silhouette — the
generic case, not the temple or castle) as the baseline "house" this document's `city.tier1/2/3.icon`
keys already draw from. **Do not assume `tier1`→house, `tier2`→temple, `tier3`→castle or any other
specific mapping** — that would assert a meaning this section explicitly does not have evidence for.
The capital (`city.capital.icon`) may or may not correspond to one of these three shapes; until plan
item 16 settles it, giving it a distinguishing mark (a raised banner, a distinct roofline) layered on
whichever tier's icon it draws from remains this document's own `[designed]` fallback, stated as a
fallback rather than as a finding about the original.

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

### 4.4 City markers — see §3.3; four shipped keys against five confirmed variants

`city.tier1.icon`, `city.tier2.icon`, `city.tier3.icon`, `city.capital.icon` — depiction guidance in
§3.3. First needed by T48, then T24. Exists today, **but §3.3's own gap applies here directly**: the
original's confirmed map-code encoding has **five** variants (`rivers-and-map-markers.md`, 334/334
cities), not the four this group's keys provide for, and the variant's meaning is unconfirmed
(decompilation plan item 16). This is not a call to add a fifth key — that is T11's list, and inventing
a mapping from an unconfirmed variant to a new constant would be worse than the gap itself — it is the
gap DoD 1 asks this document to record: **four shipped keys may not be enough once the fifth variant's
meaning is known**, and whoever settles plan item 16 should re-open this section rather than assume the
existing four already cover it.

### 4.5 Terrain tiles — complete for today's renderer, one real gap underneath

| Key | Depicts | First needed by | Exists today |
| --- | --- | --- | --- |
| `terrain.plain.tile` | Grassland — bright green base with a brown undulating ridge line and scattered dark-green/red speckles | T47 (data only, "no art"), T48 (first art) | Yes |
| `terrain.desert.tile` | Arid terrain — see below (corrected) | T48 | Yes |
| `terrain.forest.tile` | Wooded terrain — green base with a dense tree/brush motif | T48 | Yes |
| `terrain.mountain.tile` | Mountainous terrain — grey base, jagged black peaks, white snow caps, a dark red-brown foothill band | T48 | Yes |
| `terrain.river.tile` | A river-bearing tile | T48 | Yes, but see below |
| `terrain.sea_coastal.tile` | Shallow/coastal sea — lighter blue, wave motif | T48 | Yes |
| `terrain.sea_deep.tile` | Deep sea — darker blue, same wave motif | T48 | Yes |

**Correction (rework round 1): the plain and mountain depictions above were wrong, and the desert
`[designed]` tag missed evidence the corpus holds.** Direct pixel sampling of
`screenshots-processed/1_rome_270_summer_7_1.png`'s unit-map tiles: the **plain** tile is not "uniform
green, no motif" — every plain tile samples as bright green (`#00FF00`) carrying a darker-green
(`#008000`) undulating ridge line plus scattered olive (`#808000`) speckles, a clear repeating motif.
The **mountain** tile's base is not green — it samples as grey (`#808080`) with jagged black (`#000000`)
peaks, white (`#FFFFFF`) snow caps, and a dark red-brown (`#800000`) foothill band, matching what the
table above now says. **Desert**: the previous `[designed]` tag said the unit-map tiles were searched
and no desert tile was found in the local corpus, which is true as far as it went, but incomplete — the
same screenshots' **Area map** panel (the small strategic overview, top-left of each frame) renders the
North African/Middle Eastern desert band as a flat **`#808000` olive** (11,582 px sampled in that
region of `screenshots-processed/1_rome_270_autumn_7_1.png`, the only such colour band on that panel
outside sea-blue and plain-green). That is the one piece of evidence the corpus holds about the
original's own desert colour, and it points at olive, not the "tan/yellow" this section previously
adopted by convention without checking. Given the area map is a coarse, zoomed-out overview rather than
the unit-map tile art itself, this document does not treat `#808000` as `[confirmed]` for the *tile*'s
exact hue — it is evidence a `[designed]` choice should engage with, not silently contradict. **Revised
tag**: `terrain.desert.tile` is arid terrain in an olive-to-tan range; searched the unit-map tiles in
both `screenshots/` and `screenshots-processed/` for a desert tile directly and found none, but the area
map's `#808000` olive band is the corpus's only colour evidence and should be the starting point, not
an unrelated tan/yellow guess. The terrain code's own name
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
- **T17/T18 fortification and siege states: no sprite is confirmed needed, from the one data point
  available.** Checked the original's own screenshots directly — **correction (rework round 1): the
  citation named the wrong file.** Felsina at 51% fortification is in
  `screenshots-processed/1_rome_270_autumn_7_1.png`'s Information panel (*City Felsina / Controlled by
  Rome / Allegiance to Gaul / Fortification 51%*), not the summer frame, whose Information panel shows
  the news log instead. In the autumn frame, Felsina's own map glyph is the plain-house shape (§3.3) —
  the percentage is carried in the text info panel
  (`screenshots-processed/processed/12_rom_1.png`: "Fortification 78% (70,000)" for Rome itself, in the
  same text-panel form), not in a distinct sprite for either city. This is one city, checked once, not a
  sweep of every fortification level — §3.3's correction means this document can no longer claim "the
  *same* generic glyph every other city uses" as a general fact, only that *this* city's glyph does not
  visibly track its own fortification percentage over this one comparison. A future task may still choose
  to add a siege/fortification indicator for at-a-glance legibility (a besieged-city ring/overlay, say) —
  that would be a genuinely new `[designed]` UI decision, not a missing recreation. Recorded here as an
  open option, not a requirement.
- **T24 bottom filter toolbar: real chrome is missing, and enumerating it is the deliverable —
  correction (rework round 1), reversing this bullet's first draft.** The first draft claimed the
  toolbar "reuses the pack" and needs no new asset, citing `game-design.md` §UI item 2's *"the original's
  city/army/fleet-type filter icons ([`menu-and-toolbar-inventory.md`]), reused as map-overlay toggles."*
  That citation is about the map's *type filters* specifically, and does not cover the rest of what the
  original's own toolbars carry — which the cited report's own words say has never been fully catalogued:
  *"the area-map and unit-map toolbars expose pictorial shortcuts"*, and *"We have not yet mapped every
  area-map and unit-map icon or checked whether every menu command has a toolbar icon."* Counted directly
  from the two cited screenshots, tile by tile:

  - The **area-map toolbar** in `screenshots-processed/1_rome_270_summer_7_1.png` carries **13 buttons**:
    a multicoloured map/palette toggle; a house (city filter); a columned building matching §3.3's
    temple shape (a second, distinct city-related filter); a soldier with spear and shield (army
    filter); a grey ship/anchor icon (fleet filter); a small combined soldier-and-house icon (a combined
    filter); five plain geometric overlay toggles (`+`, `×`, `#`, an outlined diamond, an outlined
    circle); a combined multi-symbol icon (toggle-all); and a gold coin (an economy/money overlay).
  - The **unit-map toolbar** in `screenshots/1_cartago_271_spring_3_1.png` carries **7 buttons**, all
    fleet-order commands on a teal background: ship-with-cargo-and-marker (load an army), ship ringed
    with dots (repair), ship with a "1" and split arrows (split fleet), two ships either side of a
    vertical divider (join fleets), ship with a plus sign (build/add ships), a tilted beached ship over a
    blue line (scuttle), and a plain white circle (clear filter).

  That is **at least 18 buttons across the two toolbars I actually counted**, most of them either a
  *command* (a fleet order, a filter toggle) rather than the *marker* icons `AssetKeys` already has, or a
  filter for something (the temple-shaped building filter, the economy coin) with no existing key at all.
  This is a floor, not a ceiling — I checked two toolbar rows in two screenshots, not the full menu
  surface `menu-and-toolbar-inventory.md` itself says is still incompletely mapped. **The "no new asset
  needed" conclusion in this bullet's first draft cannot stand against this evidence.** Recorded here as
  a real, still only partially enumerated gap: a `ui.toolbar.*` or `ui.command.*` category (naming left
  to whichever task first wires the toolbar, since committing to specific key names without also wiring
  their commands would be presumptuous) covering at minimum the type-filter icons beyond the five already
  in `AssetKeys` (a temple/capital filter, a fleet filter distinct from `fleet.tier*`, a combined-unit
  filter), the geometric overlay toggles, an economy/money icon, and the seven fleet-order command icons.
  Producing the exhaustive, authoritative version of this list is `menu-and-toolbar-inventory.md`'s own
  job — this section records that the gap is real and roughly this size, not a substitute for that
  report's own complete inventory.
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

**Net finding for this section — corrected, rework round 1**: this section's first draft concluded that
every UI-chrome callout resolves to "no new asset required." That conclusion does not survive checking
the evidence it claimed to have checked: the **T24 toolbar** gap is real, and enumerable at at least 18
buttons across two toolbar rows this document counted directly. Two resolutions still hold on inspection
— **T16/T25 battle-screen iconography** (a text/numeric summary, nothing icon-shaped named anywhere) and
**T17/T18 fortification/siege state** (no visual difference confirmed for the one city checked, though
now on a corrected citation and a narrower claim) — and two more hold on the same reasoning as before
(**T24 diplomacy grid**, **T24/T25 hotseat and offer dialogs**, both specified as text/flow, not icons).
So: **one real, sizeable gap (the toolbar), one already-recorded gap this section originally missed
folding in (§3.3/§4.4's five-vs-four city variant mismatch, which is chrome-adjacent since it affects
the same map rendering T24 builds), and three callouts that do check out as "no new asset needed."** The
lesson carried forward: a "nothing is missing" finding is only as good as how many of the cited sources
were actually re-opened, not just cited — the two blocking findings that reverse this section's first
draft (this one and B4/B2 above) are both cases where the citation was named but not re-checked against
what it actually shows.

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
  record-fields.md`, `terrain-move-cost-table-in-dat.md`, `rivers-and-map-markers.md`, `design-audit.md`
  sections, `investigations/dat-file-layout.md`), several already cited by `game-design.md`/
  `design-audit.md` and re-cited here for the same claim, not re-derived independently.
- **An explicit `[designed]` tag** stating what was searched and came up empty, per `design-audit.md`
  §4.5, everywhere one appears above (desert tile colour, UI-chrome pixel size, the sfx-mapping gap, the
  candidate new sfx keys, the optional siege/fortification overlay, the capital's distinguishing mark as
  a fallback rather than a finding). Where a first-round `[designed]` tag stated a search result the
  corpus itself contradicted (the city-glyph claim in §3.3, corrected in rework round 1), the correction
  is recorded in place, from direct re-inspection, rather than quietly replaced.

No claim in this document rests on having opened the `.EXE`'s or `.DAT`'s own image/audio resources —
only on the DAT's *data* tables (nation names, terrain codes, marker arithmetic — all numeric/structural,
already covered by the project's standing decompilation work), the screenshots corpus, and existing
research reports.
