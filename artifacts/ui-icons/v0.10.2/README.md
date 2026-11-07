# T284 action icon artwork

These are newly generated project illustrations, not original Imperial Conquest media. Provider: OpenRouter; requested and returned model: `google/gemini-2.5-flash-image`. No original-game images were supplied. Prompts, accepted raw/output hashes and source paths are in [provenance.json](provenance.json). The raw PNGs and per-request metadata are retained in `generated-sources/ui-actions/`; three retired candidates and their outputs are in `revisions/` there. Every master is under 5 MB.

[Family sheet](icon-family-preview.png): each cell shows 24px, 32px and enlarged versions on Parchment and Dark. Inspect the [native theme/state sheet](theme-states.png) separately: it uses images decoded by Godot and the actual theme/icon-state colours, not a screenshot of the owner's desktop. Four theme blocks are Parchment, Dark, High contrast, Slate; each contains normal, hover, pressed, focus, disabled rows. Columns follow the table below. These are readability/contact sheets; no original-game or owner-save capture is included.

| Icon | Existing control / meaning |
| --- | --- |
| play | Strategic observer Play and tactical Resume |
| pause | Strategic/tactical observer Pause |
| skip | Skip current activity / recorded battle playback |
| battle_reports | Latest battles (owner called this “all battles”); history policy unchanged |
| next_round | Advance one campaign date, then pause |
| watch | Watch civilizations; the compact count remains next to the icon |
| take_control | Paused nation takeover, with existing timing/guards |
| undo | Strategic and tactical toolbar Undo; existing own-turn boundaries |
| split | Army Split in Two and the recovery split suggestion |
| fleet_split | Fleet Split in Two; anchor with two branching arrows |
| recover | Retreat and supply |
| rally | Army rally flag |
| fleet_rally | Fleet rally flag/anchor |
| rally_clear | Clear army rally point |
| fleet_rally_clear | Clear fleet rally point |

The 36 existing colored `ui.command.*` icons and eight colored battle-toolbar glyphs are preserved. The old travel icon has no active Set target action after ordinary-click travel was introduced; its legacy fallback is retained. Menus/dialog choices and speed/nation values keep their useful text. Numeric watch counts remain visible. This is a presentation refresh, not new battle history, AI, movement or save behavior.

## Reproduce without a model call

Use Python with Pillow. From the checkout root:

```powershell
python scripts/generate-authored-assets.py --ui-icons-gemini --self-check
python scripts/generate-authored-assets.py --ui-icons-gemini --reconform --raw artifacts/ui-icons/v0.10.2/generated-sources --key ui.action.pause
```

Repeat the explicit key for other accepted icons. This verifies each raw/provenance hash and processes it offline; it reads neither credentials nor the model catalogue. Ordinary or `--all` generation never selects this new family; UI mode rejects `--all`. Billable generation requires an explicit UI key and the configured Gemini model, refuses existing paid outputs, and never falls back to another model. The generator stores paid source bytes before conformance. CI/self-checks use synthetic data and stubbed network.

The finished format is 32px RGBA PNG with transparent padding. The runtime uses the shared asset resolver, samples to 24px, preserves illustration RGB, and fades disabled icons. Legacy SVG/text fallback is retained for unavailable artwork, while the native asset check requires every illustrated icon to be present in a valid shipped pack.

The generated-request count and provider-reported cost are recorded in provenance.json.

## Implementation rationale

The new renderer-owned `ui.action.*` family lives in the authored pack manifest, parallel to the existing Godot-owned battle keys. The engine's 36 `ui.command.*` BMP contract is unchanged. New art is RGBA PNG: the first BI_RGB BMP contact-sheet reader lost alpha, so PNG was chosen for explicit transparency across the authoring tools and runtime. That preview was not evidence of a Godot BMP defect. Native Godot loading now checks alpha, color diversity and the 24px sampled texture.

`QolCommandIcons` resolves authored images through the existing pack loader, caches them, samples a copy to 24px and marks the resulting texture with its logical key. It keeps illustration tint white and only fades disabled alpha. `Appearance` reapplies that neutral tint after palette remapping, because the High contrast palette's white text role would otherwise recolor illustrated buttons during a theme switch. Repeated Play/Pause refreshes skip unchanged icon/caption assignments to avoid rebuilding theme overrides each frame.

The native state sheet composites loaded textures with the actual theme/button tint colors; it is not a windowed screenshot. The family sheet uses the stored 32px output and an enlarged view for visual inspection. New generation is not required to resume the work: all paid masters and targeted revisions are retained, and normalization is offline.

The resumed visual pass, copied-package checks and owner-added campaign-turn regression/mutation evidence are recorded in [verification.md](verification.md). Independent milestone review remains separate.
