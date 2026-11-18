using IC2.Engine.Model;

namespace IC2.Engine.MapGen;

/// <summary>The map size a generation runs at (the T294 owner decision: one size for the first release).</summary>
/// <param name="Width">Map columns, positive.</param>
/// <param name="Height">Map rows, positive.</param>
public sealed record MapSize(int Width, int Height)
{
    public int Cells => Width * Height;
}

/// <summary>
/// T294b: one composed map candidate — the raw content a style hands the generator before it is
/// validated and converted into a <see cref="World"/>. The style owns every content decision here;
/// the core owns validation, retry and conversion.
/// </summary>
/// <param name="Relations">
/// The opening diplomacy, or null for uniform peace (GameStateFactory's documented fallback).
/// </param>
public sealed record GeneratedMap(
    int Width,
    int Height,
    int[] Codes,
    ValueList<NationDefinition> Nations,
    ValueList<CityDefinition> Cities,
    ValueList<StartingArmy> Armies,
    ValueList<StartingFleet> Fleets,
    ValueList<string> TurnOrder,
    DiplomaticRelations? Relations = null);

/// <summary>The generator's output: a validated world plus the provenance of the attempt that produced it.</summary>
public sealed record GeneratedWorld(
    World World,
    string Version,
    ulong Seed,
    int Attempt);
