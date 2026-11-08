# v0.11 continuous observer showcase

Owner direction, 2026-11-08: immediate Classical observer launch through a **command-line flag** (confirmed in chat), optional continuous random-civilization observation, and a GitHub Pages version offering only that continuous observation. This supersedes the playable-browser scope in [#1797](https://github.com/diegoami/imperial_conquest_2/issues/1797).

## Desktop contract

- `-- --observe` starts Classical Faithful immediately, all civilizations computer controlled, with playback running. Ordinary startup and existing paused New Game/loaded observer entry remain unchanged.
- `-- --observe-loop` does the same with continuous observation. Choose one surviving playable civilization randomly, watch it until elimination, then choose another survivor at a presentation boundary. Never take human control. Exclude the barbarian role. Observer selection uses randomness separate from campaign RNG.
- Continue the current campaign to its existing end condition; elimination of the watched civilization alone does not restart it. After its last visible activity, show the ending briefly and start a fresh Classical campaign with a fresh seed. Preserve existing victory/calendar rules. Do not restart on an exception or an unexplained stalled turn: stop with a diagnostic.
- Default continuous playback is deliberately slow (0.5x, approximately 0.24 seconds per strategic cell). Follow and locally zoom to the watched moving army/fleet, keeping its surroundings visible. Pace empty nation turns too. These are presentation defaults, not changes to movement, combat or AI.
- Pause/exit remain available. Loop presentation choices are transient and do not overwrite remembered watch/speed/follow preferences or modify save formats. A desktop observer may stop looping and use normal observer controls/takeover.

## Browser contract

The public page automatically runs the continuous observer showcase with authored assets and prominent, persistent links to the [repository](https://github.com/diegoami/imperial_conquest_2) and [desktop releases](https://github.com/diegoami/imperial_conquest_2/releases). No human seats, takeover, gameplay orders, playable New Game, save import/export or local-original-asset picker. Pause, mute, speed and camera inspection are spectator controls only. Reload starts a fresh observation; no browser campaign persistence is promised.

Use the production rules engine; do not implement a second simulation in JavaScript or silently substitute prerecorded footage. First prove a maintained single-threaded .NET/WebAssembly route with the engine, static repository-subpath hosting and a real browser. Godot's [current export documentation](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html), rechecked 2026-11-08, still excludes Godot 4 C# Web export. A small browser canvas frontend is an implementation option, contingent on that proof. No server process, custom isolation headers, credentials or private/original media in the payload.

Acceptance: live simulation, random selection/reselection, full-game restart, slow movement and camera framing; Chromium and Firefox checks at desktop/mobile sizes; Safari support recorded explicitly; deployed Pages HTTPS/subpath/asset verification; startup/download and responsiveness measurements. Verify the absence of player command routes. Compare bounded engine state/RNG traces across native/browser using identical scenario and seed. Keep a visible error state on runtime failures.

The browser restriction narrows #1798: optional original-asset loading belongs to the desktop client; the Pages showcase ships authored media only. Historical/random campaigns, Improved general and UI work remain in v0.11 under their existing contracts.

## Evidence and ownership

The private original manual chapters 1, 2, 3 and 10 describe New Game, map controls, turns and endings. It has no continuous observer or browser showcase. These are explicit owner-designed conveniences, building on T278/T283, not original-behavior claims. Preserve normal human controls, compatibility and existing observation policies.

Implementation starts with [T285](../tasks/T285.md): desktop startup, transient observation policy and meaningful lifecycle/control checks. The web runtime proof and Pages delivery stay under #1797, following this contract. Full milestone completion still requires the owner-started independent review before tagging; autonomous development stops at that freeze as required by `docs/milestone-review.md`.
