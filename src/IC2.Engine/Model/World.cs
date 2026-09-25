using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// A map plus the nations, cities and starting forces placed on it — one of the four file kinds in
/// <c>docs/game-design.md</c> §"The core data model".
/// </summary>
/// <param name="StartingRelations">
/// T75: the DAT's own starting diplomatic relation matrix, or <see langword="null"/> when the world
/// does not carry one — <see cref="GameStateFactory"/> then opens the game at uniform peace, exactly
/// as it did before this field existed. Reuses <see cref="DiplomaticRelations"/> (defined alongside
/// <see cref="GameState"/>) rather than a new type, since the shape — nation ids plus an N×N matrix —
/// is identical; only where it lives (world data, not run state) differs. See
/// <see cref="ValidateStartingRelations"/> and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md's
/// 2026-09-24 addition, "the starting matrix".
/// </param>
/// <param name="StartingNews">
/// T75: the DAT's own 27-line news seed (slots 0-26, newest index 26), or <see langword="null"/> when
/// the world does not carry one — <see cref="GameStateFactory"/> then opens the game with an empty
/// log, exactly as it did before this field existed. Reuses <see cref="NewsLog"/> (defined alongside
/// <see cref="GameState"/>) rather than a new type, for the same reason as
/// <see cref="StartingRelations"/>. See <see cref="ValidateStartingNews"/> and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
/// §Q1, "the DAT seeds the log, and a new game starts at index 26".
/// </param>
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
    DiplomaticRelations? StartingRelations = null,
    NewsLog? StartingNews = null,
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

    /// <summary>
    /// Validates <see cref="StartingRelations"/> against this world's own nation list and
    /// <paramref name="ruleset"/>'s diplomacy rules. A no-op when <see cref="StartingRelations"/> is
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// T75 Done-when 2. Not currently wired into <see cref="IC2.Engine.Serialization.GameDataLoader"/> /
    /// <see cref="IC2.Engine.Serialization.GameDataValidation"/> — see this task's PR for why (a scope
    /// note, not a design decision): those files are outside this task's Owns list, so "loading a world
    /// rejects" is not yet true end-to-end. This method is the validation itself, ready to be called from
    /// <c>GameDataValidation.ValidateWorld</c> once that wiring is agreed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The matrix is not symmetric, sized to its own nation list; a diagonal entry is non-zero; a value
    /// lies outside the ruleset's relation codes and cooldown range; or a matrix nation id is not one of
    /// <see cref="Nations"/>.
    /// </exception>
    public void ValidateStartingRelations(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        if (StartingRelations is not { } relations)
        {
            return;
        }

        foreach (var nationId in relations.NationIds)
        {
            if (NationById(nationId) is null)
            {
                throw new InvalidOperationException(
                    $"startingRelations names nation '{nationId}', which this world does not define.");
            }
        }

        if (!relations.IsWellFormed())
        {
            throw new InvalidOperationException(
                "startingRelations must be square, sized to its own nation list, and symmetric.");
        }

        for (var i = 0; i < relations.NationIds.Count; i++)
        {
            if (relations.Matrix[i][i] != 0)
            {
                throw new InvalidOperationException(
                    $"startingRelations' diagonal entry for '{relations.NationIds[i]}' is "
                    + $"{relations.Matrix[i][i]}, not 0.");
            }
        }

        var codes = ruleset.Diplomacy.StateCodes;
        var maxCode = Math.Max(Math.Max(codes.Peace, codes.Trade), Math.Max(codes.Alliance, codes.War));
        var minCooldown = Math.Min(
            Math.Min(ruleset.Diplomacy.CooldownAfterBrokenTrade, ruleset.Diplomacy.CooldownAfterBrokenAlliance),
            Math.Min(
                ruleset.Diplomacy.CooldownAfterEndedWar,
                Math.Min(ruleset.Diplomacy.CooldownAfterPeaceTerms, ruleset.Diplomacy.CooldownAfterAllyPeace)));

        foreach (var row in relations.Matrix)
        {
            foreach (var value in row)
            {
                if (value < minCooldown || value > maxCode)
                {
                    throw new InvalidOperationException(
                        $"startingRelations has a value {value} outside the ruleset's relation-code/"
                        + $"cooldown range [{minCooldown}, {maxCode}].");
                }
            }
        }
    }

    /// <summary>
    /// Validates <see cref="StartingNews"/> against <paramref name="ruleset"/>'s news-log geometry. A
    /// no-op when <see cref="StartingNews"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// T75 Done-when 2. Same wiring note as <see cref="ValidateStartingRelations"/>: not currently called
    /// from the load path, for the same Owns-list scope reason.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="NewsLog.MostRecentSlot"/> is outside <c>-1 .. RingBufferSlots - 1</c>, does not address
    /// the last of <see cref="NewsLog.Slots"/>, or a slot's text exceeds the ruleset's message length.
    /// </exception>
    public void ValidateStartingNews(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        if (StartingNews is not { } news)
        {
            return;
        }

        if (news.MostRecentSlot < -1 || news.MostRecentSlot > ruleset.NewsLog.RingBufferSlots - 1)
        {
            throw new InvalidOperationException(
                $"startingNews.mostRecentSlot {news.MostRecentSlot} is outside "
                + $"-1..{ruleset.NewsLog.RingBufferSlots - 1}.");
        }

        if (!news.IsConsistent())
        {
            throw new InvalidOperationException(
                $"startingNews.mostRecentSlot {news.MostRecentSlot} does not address the last of its "
                + $"{news.Slots.Count} slots.");
        }

        var maxTextBytes = ruleset.NewsLog.MessageByteLength - 1;
        foreach (var slot in news.Slots)
        {
            if (slot.Text.Length > maxTextBytes)
            {
                throw new InvalidOperationException(
                    $"startingNews has a slot text of {slot.Text.Length} bytes, over the ruleset's "
                    + $"{maxTextBytes}-byte limit.");
            }
        }
    }
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
/// <param name="Encoding">Which of <see cref="Runs"/> / <see cref="Data"/> / <see cref="DataFile"/> carries the cells.</param>
/// <param name="Runs">Present for <see cref="TerrainEncoding.RunLength"/>.</param>
/// <param name="Data">
/// The base64 cells inline, for <see cref="TerrainEncoding.Base64"/>. Mutually exclusive with
/// <see cref="DataFile"/>: a document carries exactly one of the two for a base64 grid.
/// </param>
/// <param name="DataFile">
/// The base64 cells in a sidecar file (T62), for <see cref="TerrainEncoding.Base64"/> — named relative
/// to the directory the world document itself was loaded from, e.g. <c>classical-mediterranean.terrain.b64</c>
/// next to <c>classical-mediterranean.json</c>. A world large enough that its terrain blob would dominate
/// the JSON (the 320×140 original export; T62's Scope) ships this way instead of inline <see cref="Data"/>
/// so the JSON stays readable without it; a small fixture may still embed <see cref="Data"/> directly.
/// <see cref="GameDataLoader"/> resolves this into <see cref="Data"/> before the document reaches any
/// caller — a <see cref="TerrainGrid"/> obtained any other way (constructed directly, as every test not
/// exercising the sidecar path does) is expected to already carry <see cref="Data"/>, not this field.
/// </param>
public sealed record TerrainGrid(
    TerrainEncoding Encoding,
    ValueList<TerrainRun>? Runs = null,
    string? Data = null,
    string? DataFile = null)
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

                if (DataFile is not null)
                {
                    // Only a base64 grid may use a sidecar (DataFile's own doc comment, and DoD 4's
                    // stated toy-world exemption): a run-length grid's "runs" list is already the
                    // handful of entries a sidecar exists to avoid, so a stray "dataFile" here is
                    // rejected rather than silently carried through unresolved.
                    throw new InvalidOperationException(
                        "A run-length terrain grid must not carry \"dataFile\"; only a base64 grid may use a sidecar.");
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
                    // Distinguished from "no data at all" so a TerrainGrid built straight from JSON --
                    // bypassing GameDataLoader, which resolves DataFile into Data before anything else
                    // sees the document (see DataFile's doc comment) -- fails with a message that names
                    // the actual cause instead of claiming the document omitted "data" outright.
                    throw new InvalidOperationException(DataFile is not null
                        ? $"A base64 terrain grid's \"dataFile\" ('{DataFile}') has not been resolved into \"data\"; call it through GameDataLoader, not TerrainGrid.Decode directly."
                        : "A base64 terrain grid must carry \"data\" or \"dataFile\".");
                }

                if (DataFile is not null)
                {
                    throw new InvalidOperationException(
                        "A base64 terrain grid must not carry both \"data\" and \"dataFile\".");
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
