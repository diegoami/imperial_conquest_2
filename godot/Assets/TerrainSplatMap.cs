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
public sealed record RiverChain(IReadOnlyList<SplatPoint> Polyline)
{
    /// <summary>
    /// The number of leading points of <see cref="Polyline"/> that belong to a confluence joining
    /// segment (0 when the start does not join a receiving chain). The joining segment leaves the
    /// tangent-continuous curve at the start exit midpoint, so Done-when 1 exempts it from the
    /// per-segment turn bound and tests it separately; exposing the count is how the test knows which
    /// turns are the join's.
    /// </summary>
    public int JoinStartPoints { get; init; }

    /// <summary>
    /// The number of trailing points of <see cref="Polyline"/> that belong to a confluence joining
    /// segment (0 when the end does not join a receiving chain). See <see cref="JoinStartPoints"/>.
    /// </summary>
    public int JoinEndPoints { get; init; }
}

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
/// <strong>Blending: smooth, then threshold</strong> (the user's decision of 2026-10-06 at the
/// escalation of rework round 2). <see cref="WeightsAt(float,float)"/> is a smooth field over map
/// coordinates. For each class it first computes a <em>smoothed membership field</em>: the
/// Gaussian-weighted share (<see cref="SmoothingSigmaCells"/>, cells within
/// <see cref="SmoothingReachCells"/>) of the cells whose class it is — so a one-cell staircase blurs
/// into a smooth diagonal. A small deterministic noise (<see cref="FieldNoiseBoundCells"/> per class)
/// keeps smooth borders off a ruler. A <em>centre pin</em> (<see cref="CentrePinStrength"/>,
/// <see cref="CentrePinRadiusCells"/>) then adds, for the class of the cell containing the point, a
/// smooth bump that is largest at that cell's centre and zero beyond the radius, big enough that the
/// cell's own class always wins at its centre — a lone cell of one class reads as a small rounded patch
/// rather than vanishing. The weights are those fields sharpened by a softmax at
/// <see cref="SoftmaxSteepness"/>, whose 10 %-to-90 % transition is well under
/// <see cref="SoftmaxTransitionCells"/>, so borders stay crisp while following the smoothed shape.
/// </para>
/// <para>
/// <strong>Rivers.</strong> <see cref="BuildRiverChains"/> links orthogonally adjacent river cells whose
/// exits face each other across their shared edge, follows each chain cell to cell, and fits one
/// tangent-continuous curve through the chain — from the start's exit midpoint, THROUGH a circular
/// fillet of radius <see cref="RiverCornerRadiusCells"/> at every river cell's centre (so the curve cuts
/// the corner instead of passing through it at an angle), to the end's exit midpoint — plus a gentle
/// deterministic wobble of at most <see cref="RiverWobbleAmplitudeCells"/> cell, faded to zero near an
/// open chain's ends. The curve passes within <see cref="RiverMaxCentreDistanceCells"/> of every river
/// cell's centre and turns by well under 20° between consecutive 0.1-cell samples.
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
    /// The Gaussian smoothing length of the membership fields, in cells (T148 Scope "smooth, then
    /// threshold": σ = 0.6 cell, the user's decision of 2026-10-06 at the escalation of rework round 2).
    /// </summary>
    public const float SmoothingSigmaCells = 0.6f;

    /// <summary>The half-width, in cells, of the window each membership field sums over. A cell
    /// farther than this has a Gaussian weight below 2e-5 and is left out.</summary>
    public const int SmoothingReachCells = 2;

    /// <summary>
    /// The most the deterministic field noise adds to (or removes from) one class's field, in field
    /// units (T148 Scope: at most 0.06 per class). It keeps a smooth border off a ruler without moving
    /// it noticeably — the softened border's gradient is far steeper than this.
    /// </summary>
    public const float FieldNoiseBoundCells = 0.06f;

    /// <summary>The period, in cells, of the field noise: long, so a border wanders gently rather than
    /// being roughened.</summary>
    public const float FieldNoisePeriodCells = 7f;

    /// <summary>
    /// The height of the centre pin's bump at a cell's centre, added to that cell's class field (T148
    /// Scope "the cell's own terrain wins at its centre"). With σ = 0.6, a lone cell of one class loses
    /// its smoothed share to its surroundings (about 0.31 to 0.69) but wins again once this is added.
    /// </summary>
    public const float CentrePinStrength = 1f;

    /// <summary>The distance, in cells, beyond which the centre pin is exactly zero (T148 Scope). The
    /// four inner bake samples of a cell sit 0.177 cell from its centre, inside this bump.</summary>
    public const float CentrePinRadiusCells = 0.35f;

    /// <summary>
    /// The softmax steepness that sharpens the smoothed fields into weights (T148 Scope "sharpened by a
    /// softmax"). Chosen for a crisp border: the 10 %-to-90 % transition is about
    /// <see cref="SoftmaxTransitionCells"/> cell, not the blur the user rejected.
    /// </summary>
    public const float SoftmaxSteepness = 50f;

    /// <summary>The 10 %-to-90 % transition width, in cells, the softmax produces on a straight border:
    /// 2·ln(9) / (<see cref="SoftmaxSteepness"/> × the border's field gradient). Recorded so the
    /// constant's meaning is measurable.</summary>
    public const float SoftmaxTransitionCells = 0.073f;

    /// <summary>The arc-length spacing, in cells, between consecutive points of a river chain's
    /// polyline (T148 Done-when 1: sampled every 0.1 cell of arc length).</summary>
    public const float RiverPolylineStepCells = 0.1f;

    /// <summary>
    /// The radius, in cells, of the circular fillet the river curve cuts at each river cell's centre
    /// (T148 Scope: the curve cuts corners instead of passing exactly through every centre). It is
    /// capped by the available segment lengths; at 0.4 cell a fillet stays 0.166 cell from the centre
    /// and bounds the curvature to 0.1/0.4 cell per sample, 14.3° — under the 20° the Done-when allows.
    /// </summary>
    public const float RiverCornerRadiusCells = 0.4f;

    /// <summary>The largest distance, in cells, the fitted river curve keeps from a river cell's centre:
    /// the fillet's 0.166 plus the wobble's magnitude, under the 0.25 the Done-when requires.</summary>
    public const float RiverMaxCentreDistanceCells = 0.23f;

    /// <summary>The largest river-chain wobble per axis, in cells (T148 Scope: at most 0.1 cell), a
    /// deterministic value noise faded to zero at an open chain's ends so its endpoints stay exact.</summary>
    public const float RiverWobbleAmplitudeCells = 0.04f;

    /// <summary>The wobble's noise period, in cells — long enough that 0.1-cell chords of the curve stay
    /// far under the 20° the Done-when allows.</summary>
    public const float RiverWobblePeriodCells = 5f;

    /// <summary>Within this arc length of an open chain's start or end the wobble fades linearly to zero,
    /// so the polyline's endpoints are exactly the chain's exit midpoints.</summary>
    public const float RiverWobbleEndFadeCells = 0.5f;

    private const int NoiseSeedBase = 1013;
    private const int NoiseSeedStride = 101;
    private const int WobbleSeedX = 4421;
    private const int WobbleSeedY = 5531;
    private const float TwoSigmaSquared = 2f * SmoothingSigmaCells * SmoothingSigmaCells;

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
    /// delegates to, so the bake and the continuous function cannot drift. The smoothed membership
    /// fields, the field noise and the centre pin feed a softmax (see the class remarks).
    /// </summary>
    public static SurfaceWeights WeightsAt(int[] cells, int width, int height, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(cells);

        Span<float> fields = stackalloc float[6];
        var total = 0f;
        var cellX = (int)MathF.Floor(x);
        var cellY = (int)MathF.Floor(y);

        for (var cy = cellY - SmoothingReachCells; cy <= cellY + SmoothingReachCells; cy++)
        {
            if (cy < 0 || cy >= height)
            {
                continue;
            }

            for (var cx = cellX - SmoothingReachCells; cx <= cellX + SmoothingReachCells; cx++)
            {
                if (cx < 0 || cx >= width)
                {
                    continue;
                }

                var dx = x - (cx + 0.5f);
                var dy = y - (cy + 0.5f);
                var distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared > SmoothingReachCells * SmoothingReachCells)
                {
                    continue;
                }

                var weight = MathF.Exp(-distanceSquared / TwoSigmaSquared);
                total += weight;
                fields[(int)ClassOf(cells, width, height, cx, cy)] += weight;
            }
        }

        if (total <= 0f)
        {
            return default;
        }

        for (var c = 0; c < 6; c++)
        {
            fields[c] = (fields[c] / total) + ClassNoise((TerrainSurfaceClass)c, x, y);
        }

        if (cellX >= 0 && cellX < width && cellY >= 0 && cellY < height)
        {
            var ccx = (cellX + 0.5f) - x;
            var ccy = (cellY + 0.5f) - y;
            var centreDistance = MathF.Sqrt((ccx * ccx) + (ccy * ccy));
            fields[(int)ClassOf(cells, width, height, cellX, cellY)] += CentrePin(centreDistance);
        }

        return Softmax(fields);
    }

    /// <summary>
    /// The deterministic field noise one class adds at a map point, in field units, in
    /// [−<see cref="FieldNoiseBoundCells"/>, +<see cref="FieldNoiseBoundCells"/>]. A pure function of
    /// position and the class's constant seed; never a random draw.
    /// </summary>
    public static float ClassNoise(TerrainSurfaceClass cls, float x, float y)
    {
        var index = (int)cls;
        var nx = (x / FieldNoisePeriodCells) + (index * 13.7f);
        var ny = (y / FieldNoisePeriodCells) - (index * 7.3f);
        return ValueNoise(nx, ny, NoiseSeedBase + (index * NoiseSeedStride)) * FieldNoiseBoundCells;
    }

    /// <summary>
    /// The centre pin's bump at a distance <paramref name="distanceCells"/> from a cell's centre, in
    /// field units: <see cref="CentrePinStrength"/> at the centre, smoothly zero at
    /// <see cref="CentrePinRadiusCells"/> (a C1 smoothstep, so the field has no crease).
    /// </summary>
    public static float CentrePin(float distanceCells)
    {
        if (distanceCells >= CentrePinRadiusCells)
        {
            return 0f;
        }

        var t = 1f - (distanceCells / CentrePinRadiusCells);
        return CentrePinStrength * t * t * (3f - (2f * t));
    }

    private static SurfaceWeights Softmax(ReadOnlySpan<float> fields)
    {
        var max = float.NegativeInfinity;
        for (var c = 0; c < 6; c++)
        {
            if (fields[c] > max)
            {
                max = fields[c];
            }
        }

        Span<float> exponentials = stackalloc float[6];
        var total = 0f;
        for (var c = 0; c < 6; c++)
        {
            // The max is subtracted so a large steepness cannot overflow; the absent classes sit near
            // zero while the present ones sit near one, so the shift changes nothing but the scale.
            exponentials[c] = MathF.Exp(SoftmaxSteepness * (fields[c] - max));
            total += exponentials[c];
        }

        return new SurfaceWeights(
            exponentials[0] / total,
            exponentials[1] / total,
            exponentials[2] / total,
            exponentials[3] / total,
            exponentials[4] / total,
            exponentials[5] / total);
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
    /// The river chains of a grid: two orthogonally adjacent river cells are <em>linked</em> when each
    /// has an exit toward the other across their shared edge; a cell has at most two links, so the
    /// links form simple paths (and possibly closed loops, drawn as closed curves). Each chain becomes
    /// one <see cref="RiverChain"/>: a tangent-continuous curve through the chain's cell centres — cut by
    /// a circular fillet at each centre — open from its start exit's midpoint to its end exit's
    /// midpoint, wobbled by at most <see cref="RiverWobbleAmplitudeCells"/> cell and resampled every
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
            var joinStart = 0;
            var joinEnd = 0;

            if (chain.StartExit is { } startExit
                && TryReceiverCell(cells, width, height, chainOfCell, c, chain.Path[0], startExit, out var startReceiver))
            {
                var from = ExitMidpoint(chain.Path[0].X, chain.Path[0].Y, startExit);
                joinStart = PrependStraight(polyline, from, NearestPointOnPolyline(built[startReceiver].Polyline, from));
            }

            if (chain.EndExit is { } endExit
                && TryReceiverCell(cells, width, height, chainOfCell, c, chain.Path[^1], endExit, out var endReceiver))
            {
                var from = ExitMidpoint(chain.Path[^1].X, chain.Path[^1].Y, endExit);
                joinEnd = AppendStraight(polyline, from, NearestPointOnPolyline(built[endReceiver].Polyline, from));
            }

            chains.Add(new RiverChain(polyline) { JoinStartPoints = joinStart, JoinEndPoints = joinEnd });
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
    /// The polyline of one chain: the corner-cutting fillet curve through <paramref name="nodes"/>
    /// (see <see cref="RoundPolyline"/>), warped by the deterministic river wobble, resampled every
    /// <see cref="RiverPolylineStepCells"/> cell of arc length between the exact endpoints.
    /// </summary>
    private static List<SplatPoint> SampleChainCurve(List<SplatPoint> nodes, bool closed)
    {
        var dense = ApplyWobble(RoundPolyline(nodes, closed), closed);
        return ResampleByArcLength(dense, RiverPolylineStepCells, closed);
    }

    /// <summary>
    /// The corner-cutting curve through <paramref name="nodes"/>: at every interior vertex a circular
    /// fillet of radius up to <see cref="RiverCornerRadiusCells"/> replaces the sharp corner, and every
    /// straight run stays a straight run. The fillet is tangent to both neighbouring segments, so the
    /// curve is tangent-continuous; its radius is capped by the segment lengths so neighbouring fillets
    /// never overlap. An open chain keeps its two endpoints exactly; a closed loop closes on itself.
    /// </summary>
    private static List<SplatPoint> RoundPolyline(IReadOnlyList<SplatPoint> nodes, bool closed)
    {
        var count = nodes.Count;
        var result = new List<SplatPoint>();
        if (count < 3)
        {
            result.AddRange(nodes);
            if (closed && count > 1)
            {
                result.Add(nodes[0]);
            }

            return result;
        }

        if (!closed)
        {
            result.Add(nodes[0]);
        }

        var first = closed ? 0 : 1;
        var last = closed ? count - 1 : count - 2;
        for (var i = first; i <= last; i++)
        {
            var prev = nodes[closed ? ((i - 1 + count) % count) : i - 1];
            var v = nodes[i];
            var next = nodes[closed ? ((i + 1) % count) : i + 1];

            var inLength = (float)Distance(prev, v);
            var outLength = (float)Distance(v, next);
            if (inLength <= 1e-6 || outLength <= 1e-6)
            {
                result.Add(v);
                continue;
            }

            var ux = (v.X - prev.X) / inLength;
            var uy = (v.Y - prev.Y) / inLength;
            var wx = (next.X - v.X) / outLength;
            var wy = (next.Y - v.Y) / outLength;
            var dot = Math.Clamp((ux * wx) + (uy * wy), -1f, 1f);
            var phi = MathF.Acos(dot);
            if (phi < 1e-3f)
            {
                // Straight through: the centre is already on the curve.
                result.Add(v);
                continue;
            }

            var halfTan = MathF.Tan(phi / 2f);
            var prevFilleted = closed || i - 1 >= 1;
            var nextFilleted = closed || i + 1 <= count - 2;
            var capIn = (prevFilleted ? 0.45f : 0.9f) * inLength;
            var capOut = (nextFilleted ? 0.45f : 0.9f) * outLength;
            var tangentDistance = MathF.Min(RiverCornerRadiusCells * halfTan, MathF.Min(capIn, capOut));
            if (tangentDistance <= 1e-4f)
            {
                result.Add(v);
                continue;
            }

            var radius = tangentDistance / halfTan;
            var pIn = new SplatPoint(v.X - (ux * tangentDistance), v.Y - (uy * tangentDistance));
            var pOut = new SplatPoint(v.X + (wx * tangentDistance), v.Y + (wy * tangentDistance));

            // The fillet centre lies on the interior angle bisector, at radius / cos(phi / 2) from the
            // vertex.
            var bx = -ux + wx;
            var by = -uy + wy;
            var bisectorLength = MathF.Sqrt((bx * bx) + (by * by));
            if (bisectorLength <= 1e-6f)
            {
                result.Add(v);
                continue;
            }

            bx /= bisectorLength;
            by /= bisectorLength;
            var centreDistance = radius / MathF.Cos(phi / 2f);
            var ox = v.X + (bx * centreDistance);
            var oy = v.Y + (by * centreDistance);

            result.Add(pIn);
            AppendArc(result, ox, oy, radius, pIn, pOut);
            result.Add(pOut);
        }

        if (!closed)
        {
            result.Add(nodes[^1]);
        }
        else
        {
            result.Add(result[0]);
        }

        return result;
    }

    /// <summary>Appends the interior points of the circular arc centred on (ox, oy) from
    /// <paramref name="from"/> to <paramref name="to"/>; the caller adds the two endpoints.</summary>
    private static void AppendArc(List<SplatPoint> result, float ox, float oy, float radius, SplatPoint from, SplatPoint to)
    {
        var start = MathF.Atan2(from.Y - oy, from.X - ox);
        var end = MathF.Atan2(to.Y - oy, to.X - ox);
        var sweep = end - start;
        while (sweep > MathF.PI)
        {
            sweep -= 2f * MathF.PI;
        }

        while (sweep < -MathF.PI)
        {
            sweep += 2f * MathF.PI;
        }

        var steps = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(sweep) * radius / 0.02f));
        for (var k = 1; k < steps; k++)
        {
            var angle = start + (sweep * k / steps);
            result.Add(new SplatPoint(ox + (radius * MathF.Cos(angle)), oy + (radius * MathF.Sin(angle))));
        }
    }

    /// <summary>The deterministic wobble of the river curve, in cells, faded to zero at an open chain's
    /// ends so its first and last points stay exactly its exit midpoints.</summary>
    private static List<SplatPoint> ApplyWobble(List<SplatPoint> points, bool closed)
    {
        var total = ArcLength(points);
        var result = new List<SplatPoint>(points.Count);
        var travelled = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                travelled += Distance(points[i - 1], points[i]);
            }

            var fade = closed
                ? 1f
                : Math.Clamp((float)(Math.Min(travelled, total - travelled) / RiverWobbleEndFadeCells), 0f, 1f);
            var wobble = WobbleAt(points[i]);
            result.Add(new SplatPoint(
                points[i].X + (wobble.X * fade),
                points[i].Y + (wobble.Y * fade)));
        }

        return result;
    }

    private static SplatPoint WobbleAt(SplatPoint p) => new(
        ValueNoise(p.X / RiverWobblePeriodCells, p.Y / RiverWobblePeriodCells, WobbleSeedX) * RiverWobbleAmplitudeCells,
        ValueNoise((p.X / RiverWobblePeriodCells) + 19.7f, (p.Y / RiverWobblePeriodCells) - 7.3f, WobbleSeedY)
            * RiverWobbleAmplitudeCells);

    /// <summary>Points every <paramref name="step"/> of arc length along the polyline, starting and
    /// ending at its exact endpoints (the last gap may be shorter than the step). A closed polyline
    /// closes on its first point.</summary>
    private static List<SplatPoint> ResampleByArcLength(List<SplatPoint> points, float step, bool closed)
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

        var last = closed ? points[0] : points[^1];
        if (Distance(result[^1], last) > 1e-4)
        {
            result.Add(last);
        }

        return result;
    }

    /// <summary>Appends a straight run from the polyline's end (<paramref name="from"/>) to
    /// <paramref name="to"/>, every <see cref="RiverPolylineStepCells"/> or so, and returns how many
    /// points were added (the join's size for Done-when 1's exemption).</summary>
    private static int AppendStraight(List<SplatPoint> polyline, SplatPoint from, SplatPoint to)
    {
        var steps = Math.Max(1, (int)Math.Round(Distance(from, to) / RiverPolylineStepCells));
        for (var k = 1; k <= steps; k++)
        {
            polyline.Add(k == steps ? to : Lerp(from, to, k / (float)steps));
        }

        return steps;
    }

    /// <summary>Prepends a straight run from the polyline's start (<paramref name="from"/>) back to
    /// <paramref name="to"/>, every <see cref="RiverPolylineStepCells"/> or so, and returns how many
    /// points were added (the join's size for Done-when 1's exemption).</summary>
    private static int PrependStraight(List<SplatPoint> polyline, SplatPoint from, SplatPoint to)
    {
        var steps = Math.Max(1, (int)Math.Round(Distance(from, to) / RiverPolylineStepCells));
        var prefix = new List<SplatPoint>(steps);
        for (var k = steps; k >= 1; k--)
        {
            prefix.Add(k == steps ? to : Lerp(from, to, k / (float)steps));
        }

        polyline.InsertRange(0, prefix);
        return steps;
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
