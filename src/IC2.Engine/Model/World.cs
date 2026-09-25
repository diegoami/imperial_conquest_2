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
/// <see cref="ValidateStartingRelationsShape"/> and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md's
/// 2026-09-24 addition, "the starting matrix".
/// </param>
/// <param name="StartingNews">
/// T75: the DAT's own 27-line news seed (slots 0-26, newest index 26), or <see langword="null"/> when
/// the world does not carry one — <see cref="GameStateFactory"/> then opens the game with an empty
/// log, exactly as it did before this field existed. Reuses <see cref="NewsLog"/> (defined alongside
/// <see cref="GameState"/>) rather than a new type, for the same reason as
/// <see cref="StartingRelations"/>. See <see cref="ValidateStartingNewsShape"/> and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
/// §Q1, "the DAT seeds the log, and a new game starts at index 26".
/// </param>
/// <param name="StartingNeighbours">
/// T85 (correction for #385, folds in #392): the DAT's own neighbour mask (nation-record
/// <c>+0x2B</c>, <see cref="IC2.Data.DatLayout.NationNeighbourOffset"/>), as an adjacency list keyed
/// by nation id, or <see langword="null"/> when the world does not carry one -- then
/// <see cref="Diplomacy.NeighbourGeography"/> falls back to its own geometric derivation, exactly as
/// it did before this field existed. Unlike <see cref="StartingRelations"/>, this is NOT mirrored
/// into <see cref="GameState"/>: the original's own conquest routine (<c>FUN_0044C528</c>) rewrites
/// the mask during play by merging a defeated nation's neighbours into its conqueror's, which is out
/// of this task's scope (a later task moves the mask into the game state and applies that merge) --
/// this field is fixed for the run, deliberately, the same way the geometric derivation it replaces
/// for the classical world always was. See <see cref="ValidateStartingNeighboursShape"/> and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/dat-neighbour-mask.md.
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
    ValueList<NationNeighbours>? StartingNeighbours = null,
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
    /// Validates <see cref="StartingRelations"/>'s <em>shape</em> against this world's own nation list —
    /// the half of T75 Done-when 2 that needs no <see cref="Ruleset"/>, called from
    /// <see cref="IC2.Engine.Serialization.GameDataValidation.Validate"/> at load time. A no-op when
    /// <see cref="StartingRelations"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="IC2.Engine.Serialization.GameDataValidation.ValidateWorld"/> checks that every matrix
    /// nation id actually resolves (as <see cref="IC2.Engine.Serialization.UnresolvedReferenceException"/>)
    /// <em>before</em> calling this method — so by the time this runs, every id in
    /// <see cref="DiplomaticRelations.NationIds"/> is already known to be one of <see cref="Nations"/>,
    /// and the "exactly the world's nations, each once" check below is purely about missing or duplicated
    /// ids, not unknown ones. The ruleset-dependent half — a value outside the ruleset's relation codes
    /// and cooldown range — is <see cref="GameStateFactory"/>'s own check, made when a ruleset is actually
    /// available.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="DiplomaticRelations.NationIds"/> is not exactly <see cref="Nations"/>' own ids, each
    /// once, in the same order (T75 rework B2: a missing or duplicated nation used to pass and then crash
    /// the first diplomacy lookup for the missing one); the matrix is not square or not symmetric; or a
    /// diagonal entry is non-zero.
    /// </exception>
    public void ValidateStartingRelationsShape()
    {
        if (StartingRelations is not { } relations)
        {
            return;
        }

        // Exactly the world's own nations, each once, in the same order -- not just the same set. The
        // order match keeps NationIds-order iteration (QuarterlyThawSystem, PeaceTreatySystem,
        // RelationTransitions, PendingOfferSystem) identical to the uniform-peace default's own order,
        // and a plain set-equality check would still accept a matrix that agreed with Nations on
        // membership but disagreed on order.
        var worldIds = Nations.Select(n => n.Id).ToArray();
        if (!relations.NationIds.SequenceEqual(worldIds))
        {
            throw new InvalidOperationException(
                "startingRelations' nation list must be exactly this world's own nations, each once, in "
                + "the same order as \"nations\".");
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
    }

    /// <summary>
    /// Validates <see cref="StartingNews"/>'s <em>shape</em> — the half of T75 Done-when 2 that needs no
    /// <see cref="Ruleset"/>, called from <see cref="IC2.Engine.Serialization.GameDataValidation.Validate"/>
    /// at load time. A no-op when <see cref="StartingNews"/> is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The ruleset-dependent half — a slot text over the ruleset's message length in bytes, or holding a
    /// byte outside the printable range the news writer and the DAT parser accept — is
    /// <see cref="GameStateFactory"/>'s own check, made when a ruleset is actually available.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="NewsLog.MostRecentSlot"/> does not equal <c>Slots.Count - 1</c> (<c>-1</c> for an empty
    /// log) — the same rule <see cref="NewsLog.IsConsistent"/> enforces on a saved state.
    /// </exception>
    public void ValidateStartingNewsShape()
    {
        if (StartingNews is not { } news)
        {
            return;
        }

        if (!news.IsConsistent())
        {
            throw new InvalidOperationException(
                $"startingNews.mostRecentSlot {news.MostRecentSlot} does not address the last of its "
                + $"{news.Slots.Count} slots.");
        }
    }

    /// <summary>
    /// Validates <see cref="StartingNeighbours"/>'s <em>shape</em>, called from
    /// <see cref="IC2.Engine.Serialization.GameDataValidation.Validate"/> at load time, after every
    /// nation id it names has already been confirmed to resolve
    /// (<see cref="IC2.Engine.Serialization.UnresolvedReferenceException"/>) — the same order
    /// <see cref="ValidateStartingRelationsShape"/> uses. A no-op when <see cref="StartingNeighbours"/>
    /// is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// T85 Done-when 2's four rejected shapes, each its own check below: an asymmetric pair (one side
    /// lists the other, not both), a nation that neighbours itself, a duplicated entry (the same
    /// nation id given two rows), and a duplicated neighbour id within one row. An unknown nation id is
    /// <see cref="IC2.Engine.Serialization.GameDataValidation.ValidateWorld"/>'s own check, run before
    /// this method — see that method's <c>RequireNation</c> calls over
    /// <see cref="StartingNeighbours"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A nation id appears in more than one <see cref="NationNeighbours"/> entry; a row lists its own
    /// nation id, or the same neighbour id twice; or one side of a pair is not reciprocated by the
    /// other.
    /// </exception>
    public void ValidateStartingNeighboursShape()
    {
        if (StartingNeighbours is not { } neighbours)
        {
            return;
        }

        var seenNations = new HashSet<string>(StringComparer.Ordinal);
        var neighboursByNation = new Dictionary<string, ValueList<string>>(StringComparer.Ordinal);

        foreach (var entry in neighbours)
        {
            if (!seenNations.Add(entry.NationId))
            {
                throw new InvalidOperationException(
                    $"startingNeighbours lists '{entry.NationId}' in more than one entry.");
            }

            var seenNeighbours = new HashSet<string>(StringComparer.Ordinal);
            foreach (var neighbourId in entry.NeighbourIds)
            {
                if (string.Equals(neighbourId, entry.NationId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"startingNeighbours' entry for '{entry.NationId}' lists itself as a neighbour.");
                }

                if (!seenNeighbours.Add(neighbourId))
                {
                    throw new InvalidOperationException(
                        $"startingNeighbours' entry for '{entry.NationId}' lists '{neighbourId}' twice.");
                }
            }

            neighboursByNation[entry.NationId] = entry.NeighbourIds;
        }

        foreach (var entry in neighbours)
        {
            foreach (var neighbourId in entry.NeighbourIds)
            {
                var reciprocated = neighboursByNation.TryGetValue(neighbourId, out var reverseList)
                    && reverseList.Contains(entry.NationId);
                if (!reciprocated)
                {
                    throw new InvalidOperationException(
                        $"startingNeighbours is not symmetric: '{entry.NationId}' lists '{neighbourId}' "
                        + $"but '{neighbourId}' does not list '{entry.NationId}' back.");
                }
            }
        }
    }
}

/// <summary>
/// One nation's starting neighbours, as an adjacency-list entry of <see cref="World.StartingNeighbours"/>
/// — the DAT's own mask decoded into ids. Review round 1, N3 correction: an earlier revision of this
/// summary said "one entry per nation that has at least one neighbour", which is not what either side
/// requires — <see cref="World.ValidateStartingNeighboursShape"/> accepts a nation with an empty
/// <see cref="NeighbourIds"/> row just as it accepts one omitted entirely (both mean "no neighbours"),
/// and the export (<c>scripts/export-classical-world.cs</c>) in fact writes all 16 classical nations,
/// none with an empty row. <see cref="Diplomacy.NeighbourGeography"/> never falls back to its own
/// geometric derivation for a <see cref="World"/> that carries <see cref="World.StartingNeighbours"/> at
/// all — not even for a nation this list omits, and not even if a present entry's own
/// <see cref="NeighbourIds"/> is empty; either shape means that nation borders nobody, decided by the
/// field, never by falling through to geometry.
/// </summary>
/// <param name="NationId">The nation this entry is about.</param>
/// <param name="NeighbourIds">Every nation this one borders, in the DAT's own bit order (ascending nation code).</param>
public sealed record NationNeighbours(string NationId, ValueList<string> NeighbourIds);

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
