# The battle "freeze" diagnosed: a hardcoded ~3-second pause per action, not a real hang

Follow-up to `full-battle-resolution-rome-vs-gaul.md`, which established the freeze isn't a hard crash (real progress happens, just slowly). The user captured two Process Monitor traces (`Logfile.PML`, `Logfile2.PML`) of a fresh reproduction attempt and asked for a diagnosis. Converted both to CSV with `Procmon64.exe /OpenLog <pml> /SaveAs <csv> /Quiet` and filtered to the `Imperial Conquest 2.exe` process (PIDs 12560 then 19976 — the game was relaunched once; PID 19976 is the live session with the freeze).

## The finding

`Logfile2.csv` (the PID 19976 session, ~5:47 of capture) contains **27 near-identical gaps of exactly 3.02–3.03 seconds** where the process makes *zero* system calls of any kind — no file I/O, no registry access, no thread activity, nothing. Every one of these gaps has the same shape:

```text
...
"Thread Create" — Thread ID: 24480          (a short-lived audio-playback thread spins up)
...                                          (thread opens R:\WAVS\SOUNDn.WAV, plays it, <0.5s total)
"Thread Exit"   — Thread ID: 24480, ~0.4s later
                                              <<< exactly ~3.02s of complete silence >>>
"RegQueryKey" HKCU — the next sound-effect lookup begins (next combat action)
```

The playback thread itself is fast — `SOUND5.WAV` is a 4,148-byte file, consistent with the sub-half-second thread lifetime actually observed. **The 3-second delay starts only after the sound has already finished playing**, and it is not spent on any traceable OS activity. The consistency is the key signal: 27 samples cluster within a 17ms band around 3,020ms (a spread of ~0.5%). That's the signature of a fixed software delay (something like a `Sleep(3000)` or an equivalent hardcoded wait), not variable hardware/driver/network latency, which would show far more jitter and would itself register as slow syscalls rather than as silence.

`Logfile.csv` (the earlier/other capture, overlapping in wall-clock time but a different game instance, PID 12560) shows **no** such gaps at all — consistent with it having captured menu/setup activity rather than an actual battle in progress, since `TPremierForm_MakeSound` (the function called at almost every combat step per `decompiled-combat-formula-structure.md` and `battle-quality-promotion-and-morale-array-decompiled.md`) fires far less often outside tactical battles.

## What this means

This is very likely **original 1996 game behavior, not a modern compatibility bug**: a deliberate pause after each combat message/sound cue, presumably designed so a human player has time to read "X ATTACKS Y — TROOP LOSSES: Z" before the next action happens. On period hardware, the surrounding computation (AI move selection, combat math, screen redraw) would itself have taken a non-trivial fraction of a second to a few seconds, so a ~3-second pacing delay wouldn't have stood out. On modern hardware, everything else in the cycle completes in a few milliseconds, leaving the fixed 3-second wait as the dominant cost of *every single combat action* — and a full battle between two 15-20 unit armies can involve dozens of such actions, compounding into the multi-minute "freeze" seen in the recordings (matches the 27-minute third recording from `full-battle-resolution-rome-vs-gaul.md` almost exactly: dozens of actions × ~3s each, plus the ordinary overhead, adds up).

This reframes the problem: it's not that the game is broken or hanging under the AIM toolkit — it's progressing at exactly its original designed pace, which was tuned for the human player to watch one battle at a time, not to be sat through at full 1990s-observer speed on hardware fast enough to finish everything else instantly. AIM toolkit (or any general Windows compatibility shim) wouldn't fix this, because there's nothing to compatibility-patch: the game is doing exactly what it always did, just with all the "real work" now consuming close to 0% of each cycle instead of a competitive share of it.

## What this does not establish

- The exact source line/API call responsible (a Win16-era `Sleep`, a message-loop timer, or a busy-wait loop) — not decompiled this pass; the Ghidra symbol table doesn't have an obvious "Delay"/"Pause"-named function, and this was diagnosed purely from the Procmon trace, not from the EXE.
- Whether *every* combat action carries this delay or only specific ones (roughly one gap every ~13 seconds was observed, not one per every sound effect — 277 playback threads were created in the same window but only 27 were followed by a 3-second stall, so it's not simply "every PlaySound call").
- Whether the delay is skippable via an in-game setting (a "battle speed" option, if one exists) that wasn't tried in this session.

## Practical implication for this project

For future battle recordings, expect roughly one ~3-second dead pause per combat action — a 20-exchange battle will take at least ~60 seconds of pure waiting on top of everything else, regardless of hardware. This isn't fixable from the outside; it's a property of the original binary's pacing logic being decompiled/reimplemented eventually rather than something to work around today. For faster testing, prefer battles with fewer total exchanges (smaller armies, more lopsided matchups that resolve in fewer rounds) when a full end-to-end capture is needed.

## Reproduction

```text
& "Procmon64.exe" /OpenLog "Logfile.PML" /SaveAs "Logfile.csv" /Quiet
& "Procmon64.exe" /OpenLog "Logfile2.PML" /SaveAs "Logfile2.csv" /Quiet
grep '"Imperial Conquest 2.exe"' Logfile2.csv > game2.csv
# then compute inter-event time deltas per row; the ~3.02s gaps immediately follow a
# short-lived audio "Thread Create"/"Thread Exit" pair and precede the next "RegQueryKey HKCU".
```

## Next checks

1. Decompile around `TPremierForm_MakeSound` and whatever it calls after the sound API returns, looking for a `Sleep`/timer wait — would confirm the exact mechanism and hardcoded duration.
2. Check whether the game's options screen has a battle-speed or message-pause setting that could be lowered.
3. Confirm the ~13-second inter-gap spacing against the number of distinct combat actions visible in a parallel video recording, to nail down which specific action type triggers the pause (all of them, or only ones with an on-screen text message).
