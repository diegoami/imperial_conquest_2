# Native treaty continuation fixture

`tests/fixtures/saves/success-treaty-before-attack.json` is an unedited native port JSON save. It is not an original-game SAV and not evidence that the original campaign reaches this state. No seed, field, pending offer or event was injected.

## Why it was rebuilt (#1012, owner decision 2026-10-22)

T192 pinned a save taken by baseline `a3dbf161b3b40bb02717845e933754ef08623854` before original command 78. Its contract allowed one current-engine attempt and no other boundary. #1012 (the original's lateral step, with markers stepped around) changed where Thracia's `army-14` marches. Two things followed:

- The sacrificial `mac-sac-1` attack in `success.txt` could no longer reach `army-14`, so it was dropped.
- Without that attack, Thracia recruits past Macedonia's army power by original78. Its win there then fails the offer gate (the winner's army power must be below the loser's). The baseline save no longer reached an offer.

The owner chose to rebuild the save on the current engine and search the earlier player turns for a boundary where a lost battle is offered peace. Before original70, Thracia's power is below Macedonia's and the gate passes; the chance roll then decides. The search moved `mac-detached` beside `army-14` and attacked on each earlier player turn. The turn after `split-fleet mac-fleet-1 mac-fleet-keep 13` gives an offer, and it is the first such turn on which the marine fleet already exists.

## Generation

Build `IC2.sln` with .NET 10. Take the current `tests/fixtures/cli/success.txt` and remove its `move mac-detached 156 24` and `attack-army mac-detached army-14` lines. Keep the result through the `end` that follows `split-fleet mac-fleet-1 mac-fleet-keep 13` (67 commands). Append `save tests/fixtures/saves/success-treaty-before-attack.json` and run the built CLI:

```text
dotnet src/IC2.Cli/bin/Debug/net10.0/IC2.Cli.dll --scenario classical-mediterranean --seat macedonia --seed 3 --turn-order fixed --script <that prefix>
```

## Saved context

- Save SHA256 (LF): `4803fc30a60dc67c56c6206942ac93bd08043494c70db5f433e57aeb340da357`.
- `GameStateHash`: `4b17202ae07488627ab40a1bdd4b035bed4b32a4ede64dcbe37574c86d45cf91`.
- Scenario/world `classical-mediterranean`; ruleset `classical-faithful`.
- Calendar turn 13; the active and only human seat is `macedonia`.
- Persisted root `17524961124136824322`, reached naturally from seed 3.
- Fixed order: `rome,carthage,seleucid,ptolemaic,macedonia,numidia,gaul,greece,celtiberia,illyria,dacia,bithynia,galatia,armenia,media,thracia`.
- No open tactical battle.

`The_loaded_treaty_case_has_pinned_native_context_and_exact_roundtrip` checks these values and the native Serialize→Load→Serialize equality. Any engine change that alters the prefix makes that test fail; the save must then be regenerated as above.

## Continuation

`success-treaty-load.txt` runs:

1. `mac-detached` steps beside `army-14` and attacks. The battle is lost and the resolver offers an honourable peace.
2. `peace-yes` accepts the offer.
3. The marine group follows: `move mac-marines 142 42`, `end`, then original commands 80–87. The fleet target is `(149, 41)`, because `(148, 41)` is no longer reached in one turn.

Use the built CLI:

```text
--load tests/fixtures/saves/success-treaty-before-attack.json --script tests/fixtures/cli/success-treaty-load.txt
```

`SuccessScriptTests` requires the real offer and an accepted outcome for every own command. `CliProcessTests` compares the actual CLI output to the golden.

This fixture tests native port JSON persistence and command coverage. It makes no claim about original SAV import/export, compatibility with the original Windows game, or campaign fidelity.
