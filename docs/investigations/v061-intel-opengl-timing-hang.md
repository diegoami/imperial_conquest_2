# v0.6.1 Intel OpenGL timing hang

Investigation dated 2026-10-19; [issue #1261](https://github.com/diegoami/imperial_conquest_2/issues/1261).
This records evidence and a temporary launch test, not a gameplay fix or a
verified battle-completion workaround.

## Observations and limits

**[confirmed: owner report]** The official v0.6.1 installation became
unresponsive near tactical battle completion. Computer General was active,
with both battle and movement pauses enabled. The owner confirmed
`classical-mediterranean-saveyy.json` as the starting save. Whether leaving
the game meant changing focus, minimizing it, or physically stepping away
remains unconfirmed.

**[confirmed: diagnostics]** Two minimal diagnostic snapshots several minutes
apart placed the continuously busy main thread at the same polling loop in
Intel's native OpenGL driver, version 32.0.101.7088. The managed stack did not
show tactical code executing. A separate audio-device warning was observed;
its relationship to the hang is unproved. Private diagnostics and save
contents are retained locally and are not included here.

**[derived]** Offline native unwind metadata and the Godot call site's
arguments strongly match a blocking GPU timestamp-result read. This is not
a debugger-symbolized identification or proof of the initiating fault.
The corresponding [Godot source at commit ed1daf0bf](https://github.com/godotengine/godot/blob/ed1daf0bf/drivers/gles3/storage/utilities.cpp#L321-L339)
reads timestamp queries using `glGetQueryObjectui64v` and `GL_QUERY_RESULT`.
Driver versus engine responsibility, the trigger, and the connection to the
battle transition remain **[open]**. Source inspection does not justify a
gameplay, tactical playback, or audio behavior change.

## Temporary ANGLE test

Use these per-launch engine arguments before any `--` user-argument separator:

```text
--rendering-method gl_compatibility --rendering-driver opengl3_angle
```

**[confirmed: code]** Godot's [Windows backend selection](https://github.com/godotengine/godot/blob/ed1daf0bf/platform/windows/display_server_windows.cpp#L7714-L7734)
selects GLES mode for ANGLE; the timestamp read above is gated on desktop GL
mode. This bypasses that particular read. No public timestamp-disable switch
was found in the inspected path. ANGLE initialization can
[fall back to native OpenGL](https://github.com/godotengine/godot/blob/ed1daf0bf/platform/windows/display_server_windows.cpp#L7660-L7673),
so successful startup alone does not establish which backend ran.

**[confirmed: startup only]** An owner-authorized temporary copy of unchanged
v0.6.1 completed 600 frames, capped at 60 FPS in a 1024 x 768 window, with exit
code zero, no errors and no fallback. Its log explicitly identified ANGLE,
Intel Iris Xe and Direct3D 11 with driver 32.0.101.7088. The executable hash
was unchanged. This did **not** test battle completion. No driver, project,
installation or shortcut was changed. Other GPU use, including a possible
WSL/Pygame workload, was not established as causal; its actual backend was
not verified and no other application was stopped.

## Next-computer comparison

Use the same v0.6.1 copy and a working copy of the confirmed starting save;
preserve the source save and avoid overwriting it. Run one game instance at
a time, recording GPU, driver, actual backend header and result.

| Run | Engine arguments | Battle conditions |
| --- | --- | --- |
| Native baseline | `--rendering-method gl_compatibility --rendering-driver opengl3` | Computer General active; battle and movement pauses enabled |
| ANGLE comparison | `--rendering-method gl_compatibility --rendering-driver opengl3_angle` | Same starting save and battle actions; same general and pause settings |

Require the ANGLE/Direct3D 11 header and absence of native fallback for the
second run. Record startup and actual battle-completion results separately.
Only add focus-loss or minimization variants if the owner confirms those
actions were part of the original sequence. If a test hangs, record the
elapsed time and end only the identified test instance; leave other
applications running. A successful startup is insufficient to close #1261.
