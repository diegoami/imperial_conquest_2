using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// A map plus the nations, cities and starting forces placed on it — one of the four file kinds in
/// <c>docs/game-design.md</c> §"The core data model".
/// </summary>
/// <remarks>
/// Deliberately free of any 320×140 / 16-nation / 334-city assumption: the original's data becomes
/// one shipped <see cref="World"/> (task T29) among possibly many, and the toy fixture under
/// <c>data/worlds/toy-3city.json</c> is another.
/// </remarks>
public sealed record World(
    int SchemaVersion,
    string Id,
    string Name,
    int Width,
    int Height,
    TerrainGrid Terrain,
    ValueList<TileType> TileTypes,
    ValueList<NationDefinition> Nations,
    ValueList<CityDefinition> Cities,
    ValueList<StartingArmy> StartingArmies,
    ValueList<StartingFleet> StartingFleets,
    ValueList<string> TurnOrder,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null) : IVersionedDocument
{
    /// <summary>Finds a tile type by its id, or <see langword="null"/>.</summary>
    public TileType? TileTypeById(string id) => TileTypes.FindById(t => t.Id, id);

    /// <summary>Finds a tile type by the numeric code stored in the terrain grid, or <see langword="null"/>.</summary>
    public TileType? TileTypeByCode(int code)
    {
        foreach (var tileType in TileTypes)
        {
            if (tileType.Code == code)
            {
                return tileType;
            }
        }

        return null;
    }

    /// <summary>Finds a city definition by id, or <see langword="null"/>.</summary>
    public CityDefinition? CityById(string id) => Cities.FindById(c => c.Id, id);

    /// <summary>Finds a nation definition by id, or <see langword="null"/>.</summary>
    public NationDefinition? NationById(string id) => Nations.FindById(n => n.Id, id);
}

/// <summary>How a <see cref="TerrainGrid"/>'s cell codes are encoded in JSON.</summary>
/// <remarks>
/// Two encodings ship so that both the toy fixture (a handful of runs, readable by eye) and a full
/// original-sized export (task T29, 44,800 cells) fit the same schema without widening it.
/// </remarks>
public enum TerrainEncoding
{
    /// <summary>Row-major runs of <c>(code, count)</c>.</summary>
    RunLength,

    /// <summary>Base64 of one little-endian unsigned 16-bit cell code per cell, row-major.</summary>
    Base64,
}

/// <summary>One run of identical cell codes in a <see cref="TerrainEncoding.RunLength"/> grid.</summary>
public sealed record TerrainRun(int Code, int Count);

/// <summary>The terrain cell grid, row-major, in one of the supported encodings.</summary>
/// <param name="Encoding">Which of <see cref="Runs"/> / <see cref="Data"/> carries the cells.</param>
/// <param name="Runs">Present for <see cref="TerrainEncoding.RunLength"/>.</param>
/// <param name="Data">Present for <see cref="TerrainEncoding.Base64"/>.</param>
public sealed record TerrainGrid(
    TerrainEncoding Encoding,
    ValueList<TerrainRun>? Runs = null,
    string? Data = null)
{
    /// <summary>
    /// Expands the grid into a row-major array of cell codes. The caller supplies the expected size so
    /// that a truncated or over-long encoding is a load error rather than a silently short map.
    /// </summary>
    /// <exception cref="InvalidOperationException">The encoding is inconsistent or the wrong length.</exception>
    public int[] Decode(int width, int height)
    {
        // Computed as a long and range-checked rather than with `checked`, so an absurd map size is an
        // InvalidOperationException the loader turns into a typed MalformedGameDataException, not an
        // OverflowException escaping untyped past GameDataValidation's catch.
        var cellCount = (long)width * height;
        if (width < 0 || height < 0 || cellCount > Array.MaxLength)
        {
            throw new InvalidOperationException(
                $"A {width}×{height} map is not a size this engine can hold.");
        }

        var expected = (int)cellCount;
        switch (Encoding)
        {
            case TerrainEncoding.RunLength:
            {
                if (Runs is null)
                {
                    throw new InvalidOperationException("A run-length terrain grid must carry \"runs\".");
                }

                if (Data is not null)
                {
                    throw new InvalidOperationException("A run-length terrain grid must not also carry \"data\".");
                }

                var cells = new int[expected];
                var written = 0;
                foreach (var run in Runs)
                {
                    if (run.Count < 0)
                    {
                        throw new InvalidOperationException("A terrain run count may not be negative.");
                    }

                    // Compared as a subtraction, never as `written + run.Count`: the sum overflows for a
                    // large count, wraps negative, and slips past the guard into an out-of-range write.
                    if (run.Count > expected - written)
                    {
                        throw new InvalidOperationException(
                            $"Terrain runs describe more than {expected} cells for a {width}×{height} map.");
                    }

                    for (var i = 0; i < run.Count; i++)
                    {
                        cells[written++] = run.Code;
                    }
                }

                if (written != expected)
                {
                    throw new InvalidOperationException(
                        $"Terrain runs describe {written} cells; a {width}×{height} map needs {expected}.");
                }

                return cells;
            }

            case TerrainEncoding.Base64:
            {
                if (Data is null)
                {
                    throw new InvalidOperationException("A base64 terrain grid must carry \"data\".");
                }

                if (Runs is not null)
                {
                    throw new InvalidOperationException("A base64 terrain grid must not also carry \"runs\".");
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(Data);
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException("The terrain grid's \"data\" is not valid base64.", ex);
                }

                if (bytes.Length != (long)expected * 2)
                {
                    throw new InvalidOperationException(
                        $"Terrain data holds {bytes.Length / 2} cells; a {width}×{height} map needs {expected}.");
                }

                var cells = new int[expected];
                for (var i = 0; i < expected; i++)
                {
                    cells[i] = bytes[i * 2] | (bytes[(i * 2) + 1] << 8);
                }

                return cells;
            }

            default:
                throw new InvalidOperationException($"Unsupported terrain encoding '{Encoding}'.");
        }
    }
}

/// <summary>
/// One terrain type. The list is <em>open</em>: a world may define any number of tile types, and a
/// ruleset prices whichever ones it recognises.
/// </summary>
/// <param name="Id">Stable key used by the ruleset's move-cost table and by asset packs.</param>
/// <param name="Code">The numeric code stored in the terrain grid.</param>
/// <param name="Name">Display name.</param>
/// <param name="PassableByArmies">Whether a land unit may enter this tile.</param>
/// <param name="PassableByFleets">Whether a fleet may enter this tile.</param>
public sealed record TileType(
    string Id,
    int Code,
    string Name,
    bool PassableByArmies,
    bool PassableByFleets,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>A nation as the world defines it at scenario start.</summary>
/// <param name="Wealth">Starting <see cref="Model.NationState.Wealth"/> — nation record <c>+0x430</c>.</param>
/// <param name="TaxBase">
/// Starting <see cref="Model.NationState.TaxBase"/> — nation record <c>+0x44C</c>, persisted state that
/// may legitimately disagree with a fresh quarterly rebuild from this world's own cities (the DAT's own
/// Rome starts at 2,528 stored against 2,464 freshly rebuilt;
/// <c>nation-tax-base-and-city-economy-fields.md</c>).
/// </param>
/// <param name="MobilizedPercent">Starting <see cref="Model.NationState.MobilizedPercent"/>, 0–100.</param>
public sealed record NationDefinition(
    string Id,
    string Name,
    string ColorHex,
    string LeaderName,
    string? CapitalCityId,
    int Treasury,
    int Unity,
    int Wealth,
    int TaxBase,
    int TaxRatePercent,
    int MobilizedPercent,
    int Population,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>A city as the world defines it at scenario start.</summary>
/// <param name="Owner">The nation that currently controls the city (city record <c>+6</c>).</param>
/// <param name="Allegiance">The nation the population identifies with (city record <c>+8</c>).</param>
/// <param name="FortificationCode">
/// The raw fortification word, carrying the original's dual encoding — a value of 100 or less is a
/// finished percentage, a value above 100 encodes a fortification order in progress. Decode it with
/// <see cref="FortificationCode"/> rather than reading it directly.
/// </param>
public sealed record CityDefinition(
    string Id,
    string Name,
    int X,
    int Y,
    string Owner,
    string Allegiance,
    int Loyalty,
    int SupplyTons,
    int FortificationCode,
    int PopulationThousands,
    int MaxPopulationThousands,
    int Tribute,
    ValueList<UnitSlot> Garrison,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>An army placed on the map at scenario start.</summary>
public sealed record StartingArmy(
    string Id,
    string Nation,
    int X,
    int Y,
    int Morale,
    int Money,
    int SupplyTons,
    int Moves,
    ValueList<UnitSlot> Units,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);

/// <summary>A fleet placed on the map at scenario start.</summary>
public sealed record StartingFleet(
    string Id,
    string Nation,
    int X,
    int Y,
    int Ships,
    int ConditionPercent,
    int Money,
    int SupplyTons,
    int Moves,
    [property: JsonPropertyName("_provenance")] ProvenanceMap? Provenance = null);
