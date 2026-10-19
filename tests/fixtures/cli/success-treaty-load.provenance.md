# Native baseline treaty continuation fixture

This is an unedited native port JSON save, not an original-game SAV or evidence that the corrected campaign reaches this state. T192's reviewed contract permits only this fixed historical boundary and one current-engine feasibility trajectory. No seed, field, pending offer or event was injected.

## Generation

- Baseline commit: `a3dbf161b3b40bb02717845e933754ef08623854`, unchanged detached clone under the author's ignored `rendered/baseline-generator`.
- Original complete `tests/fixtures/cli/success.txt` SHA256: `b8527838f23f052d7f4495dcb53a5a696e08fee2af9fc1f896a125350ca12d66` (exact generator checkout bytes).
- Generator Engine DLL SHA256: `500cc99d80592cb4efe833d219038f754dafc8dd8daf319273063861df6b875a`.
- Generator CLI DLL SHA256: `7c62295deaa712ab389476812395f7aa5bf041f1498c5c5bbc8498f74e290ce7`.
- Raw Windows generator native file SHA256: `aed9d49b94e2c292e3ab391a73cf5e2cfcbf462423e3374f8e28a621a5ab822c`.
- `GameStateHash`: `a1fdb8d15ddcafeed384adc6712615e34e9a9054daf1eecf750a25068e2345c3`.

Build the baseline whole solution with .NET 10 (`dotnet build IC2.sln --nologo -v q -m:1 /nr:false --disable-build-servers`). Run its actual built CLI with `--scenario classical-mediterranean --seat macedonia --seed 3 --turn-order fixed --script <generation-prefix>`; the prefix is exactly nonblank/noncomment original commands 1–77 followed by ordinary `save <snapshot-path>`. Command 77 is `move mac-marines 142 42`; command 78 (`end`) has not run. Capture only at this normal player boundary, with no in-progress AI continuation or pending human treaty.

The baseline source and input are identified by the commit above; do not regenerate from current source, reconstruct from diagnostic summaries, or adjust fields to obtain another outcome. Build hashes identify the actual generator assemblies used, not a promise that another checkout path emits identical debug binaries.

## Saved context and validation

- Save format 7; scenario/world `classical-mediterranean`; ruleset `classical-faithful`.
- Calendar turn 15; active and sole human seat `macedonia`.
- Persisted root `14811735145207618862`, naturally reached from initial seed 3; it was checked, never assigned.
- Fixed saved order: `rome,carthage,seleucid,ptolemaic,macedonia,numidia,gaul,greece,celtiberia,illyria,dacia,bithynia,galatia,armenia,media,thracia`.
- No open tactical battle. Native ordinary saving succeeded; it would refuse an in-progress AI lap or pending human treaty.

An independent unchanged-baseline session replayed the same 77 commands, then ordinary save. Before/after full state equality and GameStateHash prove saving changed no state; the resulting native bytes exactly equal the actual CLI-generated fixture. Both baseline and current `SaveManager.LoadFile` validation passed. `SaveManager.Serialize`/`Load` preserve the entire save and state, exact serialized text and hash. The current proof used Engine SHA256 `e0201cdd310d714c42ccbe5c8ae920acd00231b84ffab4ebcdcafcdfa4f035f9`; both proof helpers' loaded Engine hashes matched their intended build.

## Continuation contract

`success-treaty-load.txt` is exactly original A commands 78–87, unchanged as a group. Original end78 is deliberately duplicated in the corrected new-game prefix and this case. Use the actual current built CLI:

```text
--load tests/fixtures/saves/success-treaty-before-end78.json --script tests/fixtures/cli/success-treaty-load.txt
```

The save supplies IDs, human seat, fixed order and persisted root. `--load` ignores new-game scenario/seat/seed overrides. The current AI lap may change the historical outcome. Require an actual resolver-generated Macedonia treaty offer, immediate same `peace-yes`, and every unchanged tail command accepted. A failed attempt stops fixture/golden work without another state, boundary, preparation or roll choice. No golden may be produced before all required fully accepted fresh CLI byte-pairs.

The single current-engine feasibility attempt at source checkpoint `5e699bc930609ae494dd753e6d6f62a6868daec7` succeeded (actual built CLI, native exit 0). The resolver announced Thracia's human treaty terms; `peace-yes` immediately accepted, and the unchanged fleet movement/embark/disembark/recruit/disband tail succeeded. This is not final source certification: fresh accepted CLI byte-pairs, full four-case tests/check, correctness regressions, mutations, independent review and CI remain required before completion. No golden was regenerated from this initial receipt.


## Repository line endings

Existing `.gitattributes` stores text with LF. The canonical committed snapshot SHA256 is `7494a66e8a8488fe3a95d0d8b572cb09b2a825d2d59c95de0bb8ed4c178ae77e`; it is exactly the original Windows native bytes with CRLF replaced by LF, with no other byte change. The raw generator receipt above remains retained in the author's ignored replay proof. Full loaded state/hash/context are identical. Tests allow only this structural CRLF-to-LF comparison against the stored file; native serializer-to-loader-to-serializer remains exact on each platform. No JSON fields, serializer policy or Git attributes were changed.

## Reproduce in a reviewer's own checkout

The following uses only the reviewer's repository and its own ignored `rendered` directory. It never reads an author's worktree or original-game files. Run from the review checkout root, with .NET 10; stop if any command fails or the source checkout is dirty. No alternative boundary or seed is permitted.

```powershell
$reviewRoot = (Get-Location).Path
$generatorRoot = Join-Path $reviewRoot 'rendered/t192-baseline-generator'
$generationInput = Join-Path $reviewRoot 'rendered/t192-save-prefix.txt'
$regeneratedSave = Join-Path $reviewRoot 'rendered/t192-regenerated-save.json'
git clone --shared --no-checkout . $generatorRoot
git -C $generatorRoot checkout --detach a3dbf161b3b40bb02717845e933754ef08623854
git -C $generatorRoot rev-parse HEAD
git -C $generatorRoot status --short
$originalInput = Join-Path $generatorRoot 'tests/fixtures/cli/success.txt'
Get-FileHash -Algorithm SHA256 -LiteralPath $originalInput
$commands = @(Get-Content -LiteralPath $originalInput | Where-Object { $_.Trim() -ne '' -and -not $_.TrimStart().StartsWith('#') })
if ($commands.Count -ne 87 -or $commands[76] -ne 'move mac-marines 142 42') { throw 'Wrong baseline input/boundary' }
[IO.File]::WriteAllLines($generationInput, @($commands[0..76]) + @('save ' + $regeneratedSave))
Push-Location $generatorRoot
try {
    $env:DOTNET_PROCESSOR_COUNT = '2'
    dotnet build IC2.sln --nologo -v q -m:1 /nr:false --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Baseline build failed' }
    Get-FileHash -Algorithm SHA256 src/IC2.Engine/bin/Debug/net10.0/IC2.Engine.dll,src/IC2.Cli/bin/Debug/net10.0/IC2.Cli.dll
    dotnet src/IC2.Cli/bin/Debug/net10.0/IC2.Cli.dll --scenario classical-mediterranean --seat macedonia --seed 3 --turn-order fixed --script $generationInput
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $regeneratedSave)) { throw 'Ordinary generation failed' }
    git status --short
} finally { Pop-Location }
Get-FileHash -Algorithm SHA256 -LiteralPath $regeneratedSave
$canonical = [IO.File]::ReadAllText($regeneratedSave).Replace("`r`n", "`n")
$canonicalHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical)))
if ($canonicalHash -ne '7494a66e8a8488fe3a95d0d8b572cb09b2a825d2d59c95de0bb8ed4c178ae77e') { throw 'Regenerated bytes differ beyond allowed CRLF transport' }
```

Windows ordinary generation must match raw SHA256 `aed9d49b94e2c292e3ab391a73cf5e2cfcbf462423e3374f8e28a621a5ab822c`; an LF-native platform matches the canonical SHA256 directly. Only CRLF-to-LF transport is permitted; no deserialize/edit/reserialize step may be used to obtain the expected file hash. Assembly hashes may differ with build checkout paths; record them and verify the pinned clean source rather than substituting an old binary.

In the current review checkout, build `IC2.sln` before running `The_loaded_treaty_case_has_pinned_native_context_and_exact_roundtrip`: it loads the file through native schema/semantic validation, checks the entire roundtrip/context, exact canonical file hash and state hash above, and requires native Serialize→Load→Serialize equality. The fixed canonical-byte comparison binds the regenerated file to that same validated state. Then execute the ten-command actual current CLI case at its specified `--load` arguments and inspect each own verb; a resolver-generated offer and immediate accepted answer are mandatory. Reviewer reproduction is verification of the successful fixed case, not permission to search another seed/state/window.

This tests native port JSON persistence and command coverage. It makes no original SAV import/export, original Windows-game compatibility, native-natural campaign equivalence or whole-fidelity claim.
