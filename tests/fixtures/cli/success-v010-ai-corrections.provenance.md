# T282 authored CLI fixture provenance

This is a remake regression-fixture adjustment, not original-game evidence. The owner required the five November 5 AI audit defects fixed in v0.10. The pre-correction main pin is `03884eb4`.

Correction commits: `54ca0f70` (X1/#1771 diplomacy before economy; X2/#1772 per-army support independent of Tactical presentation), `c29b9ac3` (B03/#1773), `1b93a6f8` (D05/#1774), `40e5b0a3` (C23/#1775). See [T282](../../../docs/tasks/T282.md) and integration [#1781](https://github.com/diegoami/imperial_conquest_2/issues/1781).

The fixtures retain Classical Mediterranean/Faithful, Macedonia, seed 3 and fixed turn order. No scenario/ruleset, original fixture, loaded treaty JSON/tail or random seed was edited. `success.txt` is unchanged. Its human commands remain accepted; AI order counts and subsequent native campaign draws/news change after the corrected phases (the first transcript difference is Rome's first returned turn, four orders becoming three).

For `success-trade-offer.txt`, the corrected same-seed play has no pending offer at the old turn-8 answer. Ordinary execution of all the existing player commands produces Numidia's trade at turn 9, immediately after the existing `split-army mac-marines mac-merc 2 supply=45 money=80` and `end`. Move only the existing `accept-offer` there. All 70 commands and their relative order otherwise remain; no offer/state is injected, no seed search is used, and every command including the later mercenary/fleet suffix is accepted. The natural-offer test pins turn 9, Numidia, trade type, acceptance and the resulting relation.

Only `success.golden.txt` and `success-trade-offer.golden.txt` are regenerated. Capture the real built CLI's stdout as UTF-8 with LF line endings; use the same arguments as `CliProcessTests`:

```text
dotnet build IC2.sln --nologo -v q
dotnet src/IC2.Cli/bin/Debug/net10.0/IC2.Cli.dll --scenario classical-mediterranean --seat macedonia --seed 3 --turn-order fixed --script tests/fixtures/cli/success.txt
dotnet src/IC2.Cli/bin/Debug/net10.0/IC2.Cli.dll --scenario classical-mediterranean --seat macedonia --seed 3 --turn-order fixed --script tests/fixtures/cli/success-trade-offer.txt
```

Full in-process and real-process equality, repeated-run determinism, the explicit retained-command lineage, nonempty own-verb acceptance and reflection-based command coverage remain enforced. No skip, comparison normalization, weaker predicate or new coverage exception is introduced. The focused integration set (`SuccessScriptTests`, `CliProcessTests`, `CommandCoverageTests`, `FleetBuildCityTests`) passes 34 cases after this adjustment; the real Godot `OrderCoverageCheck` also exits successfully.
