using IC2.Engine.Assets;
using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>The six terrain classes the painted surface blends between.</summary>
public enum TerrainSurfaceClass
{
    Plain,
    Desert,
    Forest,
    Mountain,
    Shallow,
    Deep,
}

/// <summary>A point in map coordinates, in cells. Godot-free (see <see cref="TerrainSplatMap"/>).</summary>
public readonly record struct SplatPoint(float X, float Y);

/// <summary>The six surface weights at one sample, summing to 1.</summary>
public readonly record struct SurfaceWeights(
    float Plain,
    float Desert,
    float Forest,
    float Mountain,
    float Shallow,
    float Deep)
{
    /// <summary>The combined land weight (plain + desert + forest + mountain).</summary>
    public float Land => Plain + Desert + Forest + Mountain;

    /// <summary>The combined sea weight (shallow + deep).</summary>
    public float Sea => Shallow + Deep;
}

/// <summary>
/// T148: one river chain — the links between orthogonally adjacent river cells form a simple path (or
/// a closed loop) — fitted as ONE smooth curve and returned as a polyline sampled every
/// <see cref="TerrainSplatMap.RiverPolylineStepCells"/> cell of arc length. An open chain runs from its
/// start exit midpoint to its end exit midpoint (so the curve reaches the coast or the map's edge);
/// where the end exit faces another river cell (a confluence) the chain continues with a short straight
/// segment to the nearest point of that cell's chain polyline, so the tributary visibly joins the
/// river with no gap.
/// </summary>
/// <param name="Polyline">The curve's points, in order, from the chain's start to its end.</param>
public sealed record RiverChain(IReadOnlyList<SplatPoint> Polyline);

/// <summary>
/// T148 "The map's surface is painted, not tiled" — the one place that turns the world's terrain grid
/// into the six per-pixel weights the surface shader samples, plus the river chains drawn over them.
/// Godot-free by construction (the same seam <see cref="MapMarkerKeys"/> established): the main map
/// screen (<c>godot/UI/GameMapView.cs</c>) reads the baked lattice and the chains once and draws them,
/// and <c>tests/IC2.Engine.Tests/Ui/TerrainSplatMapTests.cs</c> exercises the continuous function and
/// the geometry directly, so the tested code is the drawn code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Classes.</strong> The world's own terrain list gives 12 codes in six names (sea ×2, plain 2,
/// desert 3, forest 4, mountains 5, river 6–11). Plain, desert, forest and mountains are their own
/// class; a river cell (6–11) counts as plain (its river is a stroke, not a surface class); a sea cell
/// (0–1) is <em>shallow</em> when a land cell lies within one cell of it (Chebyshev — a thin rim, not
/// the 2-cell band of the first attempt: the user's decision of 2026-10-06 at the visual review of the
/// splatting round), else <em>deep</em>, whatever its code — the shipped world has no code-1 cell, so
/// the code cannot carry the shallow/deep distinction (the first attempt's report, confirmed by the
/// Opus review of PR #806's R9 note).
/// </para>
/// <para>
/// <strong>The continuous function and the bake.</strong> <see cref="WeightsAt(float,float)"/> is a
/// smooth field over map coordinates: each cell contributes a bilinear tent of half-width
/// <see cref="TentHalfWidthCells"/> cell centred on its centre, so a class fades into its neighbour
/// over <see cref="BlendWidthPerSideCells"/> — a quarter of a cell — on each side of the shared edge
/// (crisp, not soft: the user's decision of 2026-10-06). The sample position is displaced by a fixed,
/// deterministic value noise of <see cref="OctaveCount"/> octaves
/// (<see cref="OctavePeriodCellsAt"/> and <see cref="OctaveAmplitudeCellsAt"/>), including a
/// low-frequency octave of period 3.5 cells so a diagonal coast becomes a wavy line rather than a cell
/// staircase, attenuated near every cell's centre by the smooth <see cref="CentreEnvelope"/>: within
/// <see cref="EnvelopeInnerRadiusCells"/> of a centre the displacement is zero, so the cell's own class
/// wins at its centre and in the mean of its four inner baked samples (0.18 cell from the centre,
/// 0.375 from every edge), which is what the linear-filtered shader reads there. <see cref="Bake"/>
/// evaluates the function on a lattice of 4 × 4 samples per cell at the quarter-cell centres.
/// </para>
/// <para>
/// <strong>Rivers.</strong> <see cref="BuildRiverChains"/> links orthogonally adjacent river cells
/// whose exits face each other across their shared edge, follows each chain cell to cell, and fits one
/// smooth chordal Catmull-Rom curve through the chain — from the start's exit midpoint, THROUGH the
/// river cells' centres (they are interpolation nodes, so the curve passes exactly through every cell's
/// centre), to the end's exit midpoint — with a deterministic wobble of at most
/// <see cref="WobbleAmplitudeCells"/> cell, faded to zero near an open chain's ends.
/// </para>
/// <para>
/// <strong>Deterministic, no random draw.</strong> The only "randomness" is the hash of integer lattice
/// coordinates in <see cref="ValueNoise"/>; every function here is a pure function of position and
/// constant seeds, so the surface and the rivers are byte-identical on every run and in every save. No
/// <c>IRng</c>, <c>System.Random</c> or <c>DateTime</c> appears anywhere in this file.
/// </para>
/// </remarks>
public sealed class TerrainSplatMap
{
    /// <summary>Bake samples per cell along each axis (4 × 4 samples per cell).</summary>
    public const int SamplesPerCell = 4;

    /// <summary>
    /// How far each side of a shared edge a terrain fades into its neighbour, in cells — a quarter of
    /// a cell (crisp, not soft: the user's decision of 2026-10-06 at the visual review of the
    /// splatting round).
    /// </summary>
    public const float BlendWidthPerSideCells = 0.25f;

    /// <summary>
    /// The half-width of one cell's contribution tent, in cells: its own half-cell plus the blend band
    /// (<see cref="BlendWidthPerSideCells"/>) on each side, so the fade zones of the two cells across
    /// an edge overlap over exactly the blend width.
    /// </summary>
    public const float TentHalfWidthCells = 0.5f + BlendWidthPerSideCells;

    /// <summary>
    /// The displacement ceiling of T148's splat noise, in cells. <see cref="Displacement"/> stays
    /// under it by construction: the octave amplitudes sum to
    /// <see cref="DisplacementAmplitudeSumCells"/> per axis, so the magnitude is bounded by √2 times
    /// that — 0.438 cell, under the ceiling — and the envelope never exceeds 1.
    /// </summary>
    public const float DisplacementCeilingCells = 0.45f;

    /// <summary>The number of displacement octaves.</summary>
    public const int OctaveCount = 3;

    /// <summary>The largest displacement per axis the octaves add up to, in cells: the sum of the
    /// octave amplitudes.</summary>
    public const float DisplacementAmplitudeSumCells =
        Octave0AmplitudeCells + Octave1AmplitudeCells + Octave2AmplitudeCells;

    /// <summary>Period, in cells, of the low-frequency octave — the 3-to-4-cell term that makes a
    /// diagonal coast a wavy line rather than a cell staircase (T148 Scope).</summary>
    public const float Octave0PeriodCells = 3.5f;

    /// <summary>Amplitude, in cells, of the low-frequency octave.</summary>
    public const float Octave0AmplitudeCells = 0.15f;

    /// <summary>Period, in cells, of the middle octave.</summary>
    public const float Octave1PeriodCells = 1.2f;

    /// <summary>Amplitude, in cells, of the middle octave.</summary>
    public const float Octave1AmplitudeCells = 0.1f;

    /// <summary>Period, in cells, of the fine octave.</summary>
    public const float Octave2PeriodCells = 0.5f;

    /// <summary>Amplitude, in cells, of the fine octave.</summary>
    public const float Octave2AmplitudeCells = 0.06f;

    /// <summary>Within this Euclidean distance, in cells, of the nearest cell centre the displacement
    /// envelope is zero — so a cell centre and its four inner bake samples (0.18 cell away) never
    /// move, which is the centre rule made geometric (Sol's review of PR 808, R5, asks at most 0.1
    /// cell within 0.2 cell of a centre; this gives exactly 0).</summary>
    public const float EnvelopeInnerRadiusCells = 0.2f;

    /// <summary>At least this far from the nearest cell centre the envelope is 1 and the full noise
    /// applies; between the two radii it rises smoothly (smoothstep).</summary>
    public const float EnvelopeOuterRadiusCells = 0.45f;

    /// <summary>The arc-length spacing, in cells, between consecutive points of a river chain's
    /// polyline (T148 Done-when 1: sampled every 0.1 cell of arc length).</summary>
    public const float RiverPolylineStepCells = 0.1f;

    /// <summary>The largest river-chain wobble, in cells (T148 Scope: at most 0.15 cell, so the curve
    /// still passes within 0.2 cell of every river cell's centre).</summary>
    public const float WobbleAmplitudeCells = 0.12f;

    /// <summary>The wobble's noise period, in cells — smooth enough that 0.1-cell chords of the curve
    /// never turn more than 30°.</summary>
    public const float WobblePeriodCells = 2.5f;

    /// <summary>Within this arc length of an open chain's start or end the wobble fades linearly to
    /// zero, so the polyline's endpoints are exactly the chain's exit midpoints.</summary>
    public const float WobbleEndFadeCells = 0.5f;

    private const int NoiseSeedX = 1013;
    private const int NoiseSeedY = 2027;
    private const int WobbleSeedX = 4421;
    private const int WobbleSeedY = 5531;

    private readonly int _width;
    private readonly int _height;
    private readonly int[] _cells;

    private TerrainSplatMap(int width, int height, int[] cells, byte[] splatA, byte[] splatB)
    {
        _width = width;
        _height = height;
        _cells = cells;
        SplatA = splatA;
        SplatB = splatB;
    }

    /// <summary>The world width in cells.</summary>
    public int Width => _width;

    /// <summary>The world height in cells.</summary>
    public int Height => _height;

    /// <summary>The bake's width in samples (4 × <see cref="Width"/>).</summary>
    public int LatticeWidth => _width * SamplesPerCell;

    /// <summary>The bake's height in samples (4 × <see cref="Height"/>).</summary>
    public int LatticeHeight => _height * SamplesPerCell;

    /// <summary>
    /// The first RGBA8 bake: R = plain, G = desert, B = forest, A = mountain, one byte per channel per
    /// sample, laid out row-major at the lattice's size.
    /// </summary>
    public byte[] SplatA { get; }

    /// <summary>
    /// The second RGBA8 bake: R = shallow sea, G = deep sea; B and A are unused (zero). Together with
    /// <see cref="SplatA"/> the six weights of a sample sum to 255.
    /// </summary>
    public byte[] SplatB { get; }

    /// <summary>The surface key for each class, in <see cref="TerrainSurfaceClass"/> order.</summary>
    public static IReadOnlyList<string> SurfaceKeysByClass { get; } = new[]
    {
        AssetKeys.TerrainPlainSurface,
        AssetKeys.TerrainDesertSurface,
        AssetKeys.TerrainForestSurface,
        AssetKeys.TerrainMountainSurface,
        AssetKeys.TerrainSeaShallowSurface,
        AssetKeys.TerrainSeaDeepSurface,
    };

    /// <summary>The period, in cells, of displacement octave <paramref name="index"/>.</summary>
    public static float OctavePeriodCellsAt(int index) => index switch
    {
        0 => Octave0PeriodCells,
        1 => Octave1PeriodCells,
        2 => Octave2PeriodCells,
        _ => throw new ArgumentOutOfRangeException(nameof(index), "there are three displacement octaves"),
    };

    /// <summary>The amplitude, in cells, of displacement octave <paramref name="index"/>.</summary>
    public static float OctaveAmplitudeCellsAt(int index) => index switch
    {
        0 => Octave0AmplitudeCells,
        1 => Octave1AmplitudeCells,
        2 => Octave2AmplitudeCells,
        _ => throw new ArgumentOutOfRangeException(nameof(index), "there are three displacement octaves"),
    };

    /// <summary>Bakes the map's terrain grid.</summary>
    public static TerrainSplatMap Bake(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var cells = world.Terrain.Decode(world.Width, world.Height);
        return Bake(world.Width, world.Height, cells);
    }

    /// <summary>Bakes a raw terrain grid (the test-friendly overload).</summary>
    public static TerrainSplatMap Bake(int width, int height, int[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "the grid must be non-empty");
        }

        if (cells.Length != width * height)
        {
            throw new ArgumentException("the grid length must equal width × height", nameof(cells));
        }

        var latticeWidth = width * SamplesPerCell;
        var latticeHeight = height * SamplesPerCell;
        var splatA = new byte[latticeWidth * latticeHeight * 4];
        var splatB = new byte[latticeWidth * latticeHeight * 4];

        for (var j = 0; j < latticeHeight; j++)
        {
            var cellY = j / SamplesPerCell;
            var sampleJ = j % SamplesPerCell;
            var y = cellY + ((sampleJ + 0.5f) / SamplesPerCell);

            for (var i = 0; i < latticeWidth; i++)
            {
                var cellX = i / SamplesPerCell;
                var sampleI = i % SamplesPerCell;
                var x = cellX + ((sampleI + 0.5f) / SamplesPerCell);

                var weights = WeightsAt(cells, width, height, x, y);
                var offset = ((j * latticeWidth) + i) * 4;
                WriteWeights(weights, splatA, offset, splatB, offset);
            }
        }

        return new TerrainSplatMap(width, height, cells, splatA, splatB);
    }

    /// <summary>
    /// The continuous six-weight function at a map point, in cells (a cell spans
    /// [x, x + 1) × [y, y + 1), so its centre is (x + 0.5, y + 0.5)).
    /// </summary>
    public SurfaceWeights WeightsAt(float x, float y) => WeightsAt(_cells, _width, _height, x, y);

    /// <summary>
    /// The six weights at a map point for a raw grid — the static half <see cref="WeightsAt(float,float)"/>
    /// delegates to, so the bake and the continuous function cannot drift.
    /// </summary>
    public static SurfaceWeights WeightsAt(int[] cells, int width, int height, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var displacement = Displacement(x, y);
        var sampleX = x + displacement.X;
        var sampleY = y + displacement.Y;

        Span<float> weights = stackalloc float[6];
        var total = 0f;

        var centreX = (int)MathF.Floor(sampleX);
        var centreY = (int)MathF.Floor(sampleY);
        for (var cy = centreY - 1; cy <= centreY + 1; cy++)
        {
            var yFactor = 1f - (MathF.Abs(sampleY - (cy + 0.5f)) / TentHalfWidthCells);
            if (yFactor <= 0f)
            {
                continue;
            }

            for (var cx = centreX - 1; cx <= centreX + 1; cx++)
            {
                var xFactor = 1f - (MathF.Abs(sampleX - (cx + 0.5f)) / TentHalfWidthCells);
                if (xFactor <= 0f)
                {
                    continue;
                }

                if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                {
                    continue;
                }

                var weight = xFactor * yFactor;
                weights[(int)ClassOf(cells, width, height, cx, cy)] += weight;
                total += weight;
            }
        }

        if (total <= 0f)
        {
            return default;
        }

        return new SurfaceWeights(
            weights[0] / total,
            weights[1] / total,
            weights[2] / total,
            weights[3] / total,
            weights[4] / total,
            weights[5] / total);
    }

    /// <summary>
    /// The class of one grid cell: plain/desert/forest/mountain by code, river 6–11 as plain, and sea
    /// (0–1) as shallow when a land cell is within one cell (Chebyshev), else deep.
    /// </summary>
    public static TerrainSurfaceClass ClassOf(int[] cells, int width, int height, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var code = cells[(y * width) + x];
        switch (code)
        {
            case 3:
                return TerrainSurfaceClass.Desert;
            case 4:
                return TerrainSurfaceClass.Forest;
            case 5:
                return TerrainSurfaceClass.Mountain;
            case 0:
            case 1:
                return HasLandWithinOne(cells, width, height, x, y)
                    ? TerrainSurfaceClass.Shallow
                    : TerrainSurfaceClass.Deep;
            default:
                // Plain (2) and every river connectivity code (6–11) are plain for the surface; an
                // unknown code is treated as plain rather than inventing a class.
                return TerrainSurfaceClass.Plain;
        }
    }

    /// <summary>Whether a passable-by-armies land cell lies within one cell (Chebyshev) of (x, y) —
    /// the thin shallow rim the user decided on 2026-10-06, not the 2-cell band of the first attempt.</summary>
    public static bool HasLandWithinOne(int[] cells, int width, int height, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= height)
            {
                continue;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                if (nx < 0 || nx >= width)
                {
                    continue;
                }

                if (IsLandCode(cells[(ny * width) + nx]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a terrain code is land for the surface (plain/desert/forest/mountain/river).</summary>
    public static bool IsLandCode(int code) => code is >= 2 and <= 11;

    /// <summary>A river connectivity code (6–11), whose cell draws a stroke, not a surface class.</summary>
    public static bool IsRiverCode(int code) => code is >= 6 and <= 11;

    /// <summary>
    /// Displacement octave <paramref name="index"/> at a map point, in cells: that octave's amplitude
    /// times its own value-noise field (−1..1) evaluated at the point over the octave's period, on two
    /// independent fields for the two axes — the envelope-free contribution
    /// <see cref="Displacement"/> multiplies by the <see cref="CentreEnvelope"/>. Pure function of
    /// position and constant seeds.
    /// </summary>
    public static SplatPoint OctaveDisplacement(int index, float x, float y)
    {
        var period = OctavePeriodCellsAt(index);
        var amplitude = OctaveAmplitudeCellsAt(index);
        var seedOffset = index * 101;
        var nx = ValueNoise(x / period, y / period, NoiseSeedX + seedOffset);
        var ny = ValueNoise((x / period) + 37.3f, (y / period) - 11.7f, NoiseSeedY + seedOffset);
        return new SplatPoint(nx * amplitude, ny * amplitude);
    }

    /// <summary>
    /// The total displacement of the sampling position at a map point, in cells: the
    /// <see cref="CentreEnvelope"/> times the sum of the <see cref="OctaveCount"/> octave
    /// contributions — exactly what <see cref="Displacement"/> computes, which Done-when 1 asserts at
    /// fixed sample points so the published octaves are the ones in use. Its magnitude never exceeds
    /// <see cref="DisplacementCeilingCells"/>. Pure function of position and constant seeds; never a
    /// random draw.
    /// </summary>
    public static SplatPoint Displacement(float x, float y)
    {
        var envelope = CentreEnvelope(x, y);
        var sumX = 0f;
        var sumY = 0f;
        for (var index = 0; index < OctaveCount; index++)
        {
            var octave = OctaveDisplacement(index, x, y);
            sumX += octave.X;
            sumY += octave.Y;
        }

        return new SplatPoint(envelope * sumX, envelope * sumY);
    }

    /// <summary>
    /// The smooth attenuation of the displacement near cell centres, in [0, 1]: zero within
    /// <see cref="EnvelopeInnerRadiusCells"/> of the nearest cell centre (so a centre sample and the
    /// cell's four inner bake samples never move), rising with smoothstep to 1 at
    /// <see cref="EnvelopeOuterRadiusCells"/>.
    /// </summary>
    public static float CentreEnvelope(float x, float y)
    {
        // The centre of the cell containing the point is at most 0.5 cell away on each axis, so it is
        // the nearest cell centre in Euclidean distance too.
        var dx = MathF.Abs(x - (MathF.Floor(x) + 0.5f));
        var dy = MathF.Abs(y - (MathF.Floor(y) + 0.5f));
        var distance = MathF.Sqrt((dx * dx) + (dy * dy));
        var t = Math.Clamp(
            (distance - EnvelopeInnerRadiusCells) / (EnvelopeOuterRadiusCells - EnvelopeInnerRadiusCells),
            0f,
            1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    /// The river chains of a grid: two orthogonally adjacent river cells are <em>linked</em> when each
    /// has an exit toward the other across their shared edge; a cell has at most two links, so the
    /// links form simple paths (and possibly closed loops, drawn as closed curves). Each chain becomes
    /// one <see cref="RiverChain"/>: a chordal Catmull-Rom curve through the chain's cell centres — an
    /// open chain from its start exit's midpoint to its end exit's midpoint — wobbled by at most
    /// <see cref="WobbleAmplitudeCells"/> cell and resampled every
    /// <see cref="RiverPolylineStepCells"/> cell of arc length. An unlinked exit facing another river
    /// cell (a confluence the codes cannot encode) ends the chain at that exit's midpoint and continues
    /// it with a short straight segment to the nearest point of that cell's chain polyline.
    /// Deterministic: the same grid gives identical chains on every call.
    /// </summary>
    public static IReadOnlyList<RiverChain> BuildRiverChains(int[] cells, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var visited = new bool[cells.Length];
        var built = new List<BuiltChain>();

        // Pass 1: open paths, started at an unlinked exit of an unvisited river cell, row-major, the
        // cell's exits in ExitsForCode order. Every path has at least one unlinked exit; walking from
        // each consumes the whole path, so each path is found once.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var code = cells[(y * width) + x];
                if (!IsRiverCode(code) || visited[(y * width) + x])
                {
                    continue;
                }

                var (first, second) = ExitsForCode(code);
                foreach (var startExit in new[] { first, second })
                {
                    if (visited[(y * width) + x] || IsLinkedExit(cells, width, height, x, y, startExit))
                    {
                        continue;
                    }

                    var walk = WalkPath(cells, width, height, x, y, startExit, visited);
                    built.Add(BuildChain(walk, cells, width, height));
                }
            }
        }

        // Pass 2: closed loops — every remaining unvisited river cell lies in a cycle.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                if (!IsRiverCode(cells[index]) || visited[index])
                {
                    continue;
                }

                var loop = WalkLoop(cells, width, height, x, y, visited);
                built.Add(BuildChain(loop, cells, width, height));
            }
        }

        // Which chain each river cell belongs to (the confluence join looks a facing cell up by it).
        var chainOfCell = new int[cells.Length];
        Array.Fill(chainOfCell, -1);
        for (var c = 0; c < built.Count; c++)
        {
            foreach (var cell in built[c].Path)
            {
                chainOfCell[(cell.Y * width) + cell.X] = c;
            }
        }

        // Pass 3: confluence joins, reading the base polylines only, so joins cannot feed back into
        // each other and the result is order-independent.
        var chains = new List<RiverChain>(built.Count);
        for (var c = 0; c < built.Count; c++)
        {
            var chain = built[c];
            var polyline = new List<SplatPoint>(chain.Polyline);

            if (chain.StartExit is { } startExit
                && TryReceiverCell(cells, width, height, chainOfCell, c, chain.Path[0], startExit, out var startReceiver))
            {
                var from = ExitMidpoint(chain.Path[0].X, chain.Path[0].Y, startExit);
                PrependStraight(polyline, from, NearestPointOnPolyline(built[startReceiver].Polyline, from));
            }

            if (chain.EndExit is { } endExit
                && TryReceiverCell(cells, width, height, chainOfCell, c, chain.Path[^1], endExit, out var endReceiver))
            {
                var from = ExitMidpoint(chain.Path[^1].X, chain.Path[^1].Y, endExit);
                AppendStraight(polyline, from, NearestPointOnPolyline(built[endReceiver].Polyline, from));
            }

            chains.Add(new RiverChain(polyline));
        }

        return chains;
    }

    /// <summary>The two cardinal exits of a river connectivity code, in the order MapViewer.DrawRiver
    /// reads them (6 east–west, 7 north–south, 8 east+north, 9 east+south, 10 west+south, 11 west+north).</summary>
    public static (char First, char Second) ExitsForCode(int code) => code switch
    {
        6 => ('W', 'E'),
        7 => ('N', 'S'),
        8 => ('E', 'N'),
        9 => ('E', 'S'),
        10 => ('W', 'S'),
        11 => ('W', 'N'),
        _ => throw new ArgumentOutOfRangeException(nameof(code), "not a river connectivity code"),
    };

    /// <summary>The midpoint of a cell's cardinal edge.</summary>
    public static SplatPoint ExitMidpoint(int x, int y, char exit) => exit switch
    {
        'N' => new SplatPoint(x + 0.5f, y),
        'S' => new SplatPoint(x + 0.5f, y + 1f),
        'W' => new SplatPoint(x, y + 0.5f),
        'E' => new SplatPoint(x + 1f, y + 0.5f),
        _ => throw new ArgumentOutOfRangeException(nameof(exit)),
    };

    /// <summary>A chain being walked: its cells in order and its open ends (null for a closed loop).</summary>
    private sealed record ChainWalk(List<(int X, int Y)> Path, char? StartExit, char? EndExit);

    /// <summary>A walked chain after orientation and polyline fitting.</summary>
    private sealed record BuiltChain(
        List<(int X, int Y)> Path,
        char? StartExit,
        char? EndExit,
        List<SplatPoint> Polyline);

    private static bool IsLinkedExit(int[] cells, int width, int height, int x, int y, char exit)
    {
        var (nx, ny) = StepTo(x, y, exit);
        if (nx < 0 || nx >= width || ny < 0 || ny >= height || !IsRiverCode(cells[(ny * width) + nx]))
        {
            return false;
        }

        // Linked only when the neighbour also exits back across the shared edge.
        var (first, second) = ExitsForCode(cells[(ny * width) + nx]);
        var back = Opposite(exit);
        return first == back || second == back;
    }

    private static ChainWalk WalkPath(
        int[] cells, int width, int height, int x, int y, char startExit, bool[] visited)
    {
        var path = new List<(int X, int Y)>();
        (int X, int Y) current = (x, y);
        var enteredFrom = startExit;

        while (true)
        {
            path.Add(current);
            visited[(current.Y * width) + current.X] = true;

            var (first, second) = ExitsForCode(cells[(current.Y * width) + current.X]);
            var other = enteredFrom == first ? second : first;

            if (!IsLinkedExit(cells, width, height, current.X, current.Y, other))
            {
                return new ChainWalk(path, startExit, other);
            }

            enteredFrom = Opposite(other);
            var (nx, ny) = StepTo(current.X, current.Y, other);
            current = (nx, ny);
        }
    }

    private static ChainWalk WalkLoop(
        int[] cells, int width, int height, int x, int y, bool[] visited)
    {
        var path = new List<(int X, int Y)>();
        (int X, int Y) current = (x, y);
        char? enteredFrom = null;

        while (!visited[(current.Y * width) + current.X])
        {
            path.Add(current);
            visited[(current.Y * width) + current.X] = true;

            var (first, second) = ExitsForCode(cells[(current.Y * width) + current.X]);
            var onward = enteredFrom == first ? second : first;
            enteredFrom = Opposite(onward);
            var (nx, ny) = StepTo(current.X, current.Y, onward);
            current = (nx, ny);
        }

        return new ChainWalk(path, null, null);
    }

    /// <summary>
    /// Fit one chain: orient it so a confluence end (an unlinked exit facing another river cell) is the
    /// END — so the join segment trails the polyline, as Done-when 1 reads it — and sample the curve.
    /// </summary>
    private static BuiltChain BuildChain(ChainWalk walk, int[] cells, int width, int height)
    {
        var path = walk.Path;
        var startExit = walk.StartExit;
        var endExit = walk.EndExit;

        if (startExit is { } se && FacesRiverCell(cells, width, height, path[0], se)
            && !(endExit is { } ee0 && FacesRiverCell(cells, width, height, path[^1], ee0)))
        {
            path.Reverse();
            (startExit, endExit) = (endExit, startExit);
        }

        var closed = startExit is null;
        var nodes = new List<SplatPoint>();
        if (!closed)
        {
            nodes.Add(ExitMidpoint(path[0].X, path[0].Y, startExit!.Value));
        }

        foreach (var cell in path)
        {
            nodes.Add(new SplatPoint(cell.X + 0.5f, cell.Y + 0.5f));
        }

        if (!closed)
        {
            nodes.Add(ExitMidpoint(path[^1].X, path[^1].Y, endExit!.Value));
        }

        return new BuiltChain(path, startExit, endExit, SampleChainCurve(nodes, closed));
    }

    private static bool FacesRiverCell(int[] cells, int width, int height, (int X, int Y) cell, char exit)
    {
        var (nx, ny) = StepTo(cell.X, cell.Y, exit);
        return nx >= 0 && nx < width && ny >= 0 && ny < height && IsRiverCode(cells[(ny * width) + nx]);
    }

    private static bool TryReceiverCell(
        int[] cells,
        int width,
        int height,
        int[] chainOfCell,
        int ownChain,
        (int X, int Y) cell,
        char exit,
        out int receiver)
    {
        receiver = -1;
        var (nx, ny) = StepTo(cell.X, cell.Y, exit);
        if (nx < 0 || nx >= width || ny < 0 || ny >= height || !IsRiverCode(cells[(ny * width) + nx]))
        {
            return false;
        }

        receiver = chainOfCell[(ny * width) + nx];

        // A chain never joins itself: its own cell is already on the other side of the shared edge,
        // and the polyline reaching the exit midpoint already meets it.
        return receiver >= 0 && receiver != ownChain;
    }

    /// <summary>
    /// The polyline of one chain: a chordal Catmull-Rom curve through <paramref name="nodes"/>, warped
    /// by the deterministic river wobble (zero at an open chain's endpoints, full from
    /// <see cref="WobbleEndFadeCells"/> of arc length inwards), resampled every
    /// <see cref="RiverPolylineStepCells"/> cell of arc length between the exact endpoints.
    /// </summary>
    private static List<SplatPoint> SampleChainCurve(List<SplatPoint> nodes, bool closed)
    {
        var dense = SampleCatmullRom(nodes, closed);
        var total = ArcLength(dense);
        var warped = new List<SplatPoint>(dense.Count);
        var travelled = 0d;
        for (var i = 0; i < dense.Count; i++)
        {
            if (i > 0)
            {
                travelled += Distance(dense[i - 1], dense[i]);
            }

            var fade = closed
                ? 1f
                : Math.Clamp((float)(Math.Min(travelled, total - travelled) / WobbleEndFadeCells), 0f, 1f);
            var wobble = WobbleAt(dense[i]);
            warped.Add(new SplatPoint(
                dense[i].X + (wobble.X * fade),
                dense[i].Y + (wobble.Y * fade)));
        }

        return ResampleByArcLength(warped, RiverPolylineStepCells);
    }

    private static SplatPoint WobbleAt(SplatPoint p) => new(
        ValueNoise(p.X / WobblePeriodCells, p.Y / WobblePeriodCells, WobbleSeedX) * WobbleAmplitudeCells,
        ValueNoise((p.X / WobblePeriodCells) + 19.7f, (p.Y / WobblePeriodCells) - 7.3f, WobbleSeedY)
            * WobbleAmplitudeCells);

    /// <summary>
    /// Dense samples of the chordal Catmull-Rom curve through <paramref name="nodes"/>: the curve
    /// interpolates every node (so it passes exactly through each river cell's centre), tangents scaled
    /// by the node chord lengths; a closed curve wraps its nodes and ends where it starts.
    /// </summary>
    private static List<SplatPoint> SampleCatmullRom(List<SplatPoint> nodes, bool closed)
    {
        var count = nodes.Count;
        var samples = new List<SplatPoint>();

        SplatPoint NodeAt(int i) => closed
            ? nodes[((i % count) + count) % count]
            : nodes[Math.Clamp(i, 0, count - 1)];

        var chord = new double[count];
        var segments = closed ? count : count - 1;
        for (var i = 0; i < segments; i++)
        {
            chord[i] = Distance(nodes[i], NodeAt(i + 1));
        }

        for (var i = 0; i < segments; i++)
        {
            var p0 = NodeAt(i);
            var p1 = NodeAt(i + 1);
            var h = chord[i];
            var previousChord = chord[((i - 1) + segments) % segments];

            SplatPoint tangent;
            SplatPoint tangentNext;
            if (closed)
            {
                tangent = ChordalTangent(NodeAt(i - 1), NodeAt(i + 1), previousChord + h);
                tangentNext = ChordalTangent(NodeAt(i), NodeAt(i + 2), h + chord[(i + 1) % segments]);
            }
            else if (i == 0)
            {
                // One-sided at an open chain's first endpoint: along the chain.
                tangent = ChordalTangent(p0, p1, h);
                tangentNext = segments == 1
                    ? ChordalTangent(p0, p1, h)
                    : ChordalTangent(p0, NodeAt(2), h + chord[1]);
            }
            else if (i == segments - 1)
            {
                tangent = ChordalTangent(NodeAt(i - 2), p1, previousChord + h);
                tangentNext = ChordalTangent(p0, p1, h);
            }
            else
            {
                tangent = ChordalTangent(NodeAt(i - 1), NodeAt(i + 1), previousChord + h);
                tangentNext = ChordalTangent(p0, NodeAt(i + 2), h + chord[i + 1]);
            }

            var steps = Math.Max(2, (int)Math.Ceiling(h / 0.02));
            for (var k = 0; k < steps; k++)
            {
                var u = k / (double)steps;
                samples.Add(Hermite(p0, tangent, p1, tangentNext, u, h));
            }
        }

        samples.Add(closed ? nodes[0] : nodes[^1]);
        return samples;
    }

    private static SplatPoint ChordalTangent(SplatPoint before, SplatPoint after, double span) => span <= 0.0
        ? new SplatPoint(0f, 0f)
        : new SplatPoint(
            (float)((after.X - before.X) / span),
            (float)((after.Y - before.Y) / span));

    private static SplatPoint Hermite(
        SplatPoint p0, SplatPoint m0, SplatPoint p1, SplatPoint m1, double u, double h)
    {
        var uu = u * u;
        var uuu = uu * u;
        var h00 = (2d * uuu) - (3d * uu) + 1d;
        var h10 = uuu - (2d * uu) + u;
        var h01 = (-2d * uuu) + (3d * uu);
        var h11 = uuu - uu;
        return new SplatPoint(
            (float)((p0.X * h00) + ((m0.X * h * h10) + (p1.X * h01)) + (m1.X * h * h11)),
            (float)((p0.Y * h00) + ((m0.Y * h * h10) + (p1.Y * h01)) + (m1.Y * h * h11)));
    }

    /// <summary>Points every <paramref name="step"/> of arc length along the polyline, starting and
    /// ending at its exact endpoints (the last gap may be shorter than the step).</summary>
    private static List<SplatPoint> ResampleByArcLength(List<SplatPoint> points, float step)
    {
        var result = new List<SplatPoint> { points[0] };
        if (points.Count < 2)
        {
            return result;
        }

        var travelled = 0d;
        var nextTarget = (double)step;
        for (var i = 1; i < points.Count; i++)
        {
            var segment = Distance(points[i - 1], points[i]);
            if (segment <= 1e-9)
            {
                continue;
            }

            while (nextTarget <= travelled + segment)
            {
                var t = (nextTarget - travelled) / segment;
                result.Add(Lerp(points[i - 1], points[i], (float)t));
                nextTarget += step;
            }

            travelled += segment;
        }

        var last = points[^1];
        if (Distance(result[^1], last) > 1e-4)
        {
            result.Add(last);
        }

        return result;
    }

    private static void AppendStraight(List<SplatPoint> polyline, SplatPoint from, SplatPoint to)
    {
        var steps = Math.Max(1, (int)Math.Round(Distance(from, to) / RiverPolylineStepCells));
        for (var k = 1; k <= steps; k++)
        {
            polyline.Add(k == steps ? to : Lerp(from, to, k / (float)steps));
        }
    }

    private static void PrependStraight(List<SplatPoint> polyline, SplatPoint from, SplatPoint to)
    {
        var steps = Math.Max(1, (int)Math.Round(Distance(from, to) / RiverPolylineStepCells));
        var prefix = new List<SplatPoint>(steps);
        for (var k = steps; k >= 1; k--)
        {
            prefix.Add(k == steps ? to : Lerp(from, to, k / (float)steps));
        }

        polyline.InsertRange(0, prefix);
    }

    /// <summary>The nearest point of a polyline to <paramref name="from"/>: the projection onto one of
    /// its segments (or a vertex), so the join's final point lies ON the receiving polyline.</summary>
    private static SplatPoint NearestPointOnPolyline(IReadOnlyList<SplatPoint> polyline, SplatPoint from)
    {
        var best = polyline[0];
        var bestDistance = double.MaxValue;
        for (var i = 1; i < polyline.Count; i++)
        {
            var a = polyline[i - 1];
            var b = polyline[i];
            var abx = (double)b.X - a.X;
            var aby = (double)b.Y - a.Y;
            var lengthSquared = (abx * abx) + (aby * aby);
            var t = lengthSquared <= 0.0
                ? 0d
                : Math.Clamp((((from.X - a.X) * abx) + ((from.Y - a.Y) * aby)) / lengthSquared, 0d, 1d);
            var candidate = Lerp(a, b, (float)t);
            var distance = Distance(from, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static double ArcLength(List<SplatPoint> points)
    {
        var total = 0d;
        for (var i = 1; i < points.Count; i++)
        {
            total += Distance(points[i - 1], points[i]);
        }

        return total;
    }

    private static (int X, int Y) StepTo(int x, int y, char exit) => exit switch
    {
        'N' => (x, y - 1),
        'S' => (x, y + 1),
        'W' => (x - 1, y),
        'E' => (x + 1, y),
        _ => throw new ArgumentOutOfRangeException(nameof(exit)),
    };

    private static char Opposite(char exit) => exit switch
    {
        'N' => 'S',
        'S' => 'N',
        'W' => 'E',
        'E' => 'W',
        _ => throw new ArgumentOutOfRangeException(nameof(exit)),
    };

    private static double Distance(SplatPoint a, SplatPoint b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static SplatPoint Lerp(SplatPoint a, SplatPoint b, float t) => new(
        a.X + ((b.X - a.X) * t),
        a.Y + ((b.Y - a.Y) * t));

    private static void WriteWeights(SurfaceWeights weights, byte[] splatA, int offset, byte[] splatB, int offsetB)
    {
        Span<int> channels = stackalloc int[6];
        channels[0] = ToByte(weights.Plain);
        channels[1] = ToByte(weights.Desert);
        channels[2] = ToByte(weights.Forest);
        channels[3] = ToByte(weights.Mountain);
        channels[4] = ToByte(weights.Shallow);
        channels[5] = ToByte(weights.Deep);

        // The six weights of a sample must sum to 255. Rounding each channel independently leaves at
        // most ±3, so the remainder is applied to the largest channel (always large enough to absorb it),
        // and the total is exact rather than merely within the ±2 the specification allows.
        var sum = 0;
        var largest = 0;
        for (var i = 0; i < channels.Length; i++)
        {
            sum += channels[i];
            if (channels[i] > channels[largest])
            {
                largest = i;
            }
        }

        channels[largest] = Math.Clamp(channels[largest] + (255 - sum), 0, 255);

        splatA[offset] = (byte)channels[0];
        splatA[offset + 1] = (byte)channels[1];
        splatA[offset + 2] = (byte)channels[2];
        splatA[offset + 3] = (byte)channels[3];
        splatB[offsetB] = (byte)channels[4];
        splatB[offsetB + 1] = (byte)channels[5];
        splatB[offsetB + 2] = 0;
        splatB[offsetB + 3] = 0;
    }

    private static int ToByte(float weight) =>
        Math.Clamp((int)MathF.Round(Math.Clamp(weight, 0f, 1f) * 255f), 0, 255);

    private static float ValueNoise(float x, float y, int seed)
    {
        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        var xf = x - xi;
        var yf = y - yi;
        var u = xf * xf * (3f - (2f * xf));
        var v = yf * yf * (3f - (2f * yf));

        var a = Hash(xi, yi, seed);
        var b = Hash(xi + 1, yi, seed);
        var c = Hash(xi, yi + 1, seed);
        var d = Hash(xi + 1, yi + 1, seed);

        var top = a + ((b - a) * u);
        var bottom = c + ((d - c) * u);
        return ((top + ((bottom - top) * v)) * 2f) - 1f;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)((x * 374761393) ^ (y * 668265263) ^ (seed * 1442695041));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / (float)0xFFFFFF;
        }
    }
}
