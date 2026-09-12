# Imperial Conquest 2 research and reimplementation

This repository contains original research code and documentation toward a modern reimplementation of Imperial Conquest 2. It does **not** contain the game's executables, data, help files, sounds, or saves. Supply files from your own installation when using the tools.

The first milestone is a C#/.NET parser for the shared world prefix in the original `.DAT` and `.sav` files. Godot will be used later for the map and interface; it is not required to build or run this parser.

## Requirements

- .NET 10 SDK to build and run the parser and command-line inspector. A future Godot C# project must target a compatible .NET version to reference the parser library.
- A copy of `Imperial Conquest 2.dat`; optionally, a `.sav` file for comparison.

From the repository root:

```text
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- "path/to/Imperial Conquest 2.dat" "path/to/a-save.sav"
```

The inspector validates the 320 × 140 grid and 334 city records, then reports changed cells and city records if a save is supplied. It prints findings only; it does not execute or modify the original game.

See [research notes](docs/research.md) for evidence, uncertain fields, archive hashes, and next steps. The original files are intentionally excluded by `.gitignore`.
