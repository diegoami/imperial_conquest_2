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

/// <summary>One river cell's stroke: a quadratic Bézier from <see cref="Start"/> to <see cref="End"/>.</summary>
public readonly record struct RiverStroke(SplatPoint Start, SplatPoint Control, SplatPoint End);

/// <summary>
/// T148 "The map's surface is painted, not tiled" — the one place that turns the world's terrain grid
/// into the six per-pixel weights the surface shader samples, plus the river strokes drawn over them.
/// Godot-free by construction (the same seam <see cref="MapMarkerKeys"/> established): the main map
/// screen (<c>godot/UI/GameMapView.cs</c>) reads the baked lattice and
/// <c>tests/IC2.Engine.Tests/Ui/TerrainSplatMapTests.cs</c> exercises the continuous function directly,
/// so the tested code is the drawn code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Classes — <c>[confirmed]</c> code table, <c>[designed]</c> aggregation.</strong> The world's
/// own terrain list gives 12 codes in six names (sea ×2, plain 2, desert 3, forest 4, mountains 5,
/// river 6–11). Plain, desert, forest and mountains are their own class; a river cell (6–11) counts as
/// plain (its river is a stroke, not a surface class); a sea cell (0–1) is <em>shallow</em> when a land
/// cell lies within two cells of it, else <em>deep</em>, whatever its code — the shipped world has no
/// code-1 cell, so the code cannot carry the shallow/deep distinction (the first attempt's report,
/// confirmed by the Opus review of PR #806's R9 note).
/// </para>
/// <para>
/// <strong>The continuous function and the bake.</strong> <see cref="WeightsAt"/> is a smooth field over
/// map coordinates: each cell contributes a bilinear tent of half-width one cell centred on its centre,
/// so a class fades into its neighbour over about half a cell on each side of the shared edge, and the
/// cell's own class wins at its centre. The sample is displaced by a fixed, deterministic value noise
/// (amplitude 0.2 cell per axis, three octaves) so borders and coasts are irregular. <see cref="Bake"/>
/// evaluates it on a lattice of 4 × 4 samples per cell at the quarter-cell centres; the shader samples
/// that bake with linear filtering, so at a cell's centre it reads exactly the mean of the cell's four
/// inner samples — which the weights are built so the cell's own class wins.
/// </para>
/// <para>
/// <strong>Deterministic, no random draw.</strong> The only "randomness" is the hash of integer lattice
/// coordinates in <see cref="NoiseDisplacementAt"/>; it is a pure function of position and a constant
/// seed, so the surface is byte-identical on every run and in every save. No <c>IRng</c>,
/// <c>System.Random</c> or <c>DateTime</c> appears anywhere in this file.
/// </para>
/// </remarks>
public sealed class TerrainSplatMap
{
    /// <summary>Bake samples per cell along each axis (4 × 4 samples per cell).</summary>
    public const int SamplesPerCell = 4;

    /// <summary>The largest displacement per axis, in cells — keeps the cell-centre rule with margin.</summary>
    public const float MaxNoiseAmplitudeCells = 0.2f;

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
    /// delegates to, so the bake and the continuous function cannot drift.
    /// </summary>
    public static SurfaceWeights WeightsAt(int[] cells, int width, int height, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(cells);

        var displacement = NoiseDisplacementAt(x, y);
        var sampleX = x + displacement.X;
        var sampleY = y + displacement.Y;

        Span<float> weights = stackalloc float[6];
        var total = 0f;

        var centreX = (int)MathF.Floor(sampleX);
        var centreY = (int)MathF.Floor(sampleY);
        for (var cy = centreY - 1; cy <= centreY + 1; cy++)
        {
            var yFactor = 1f - MathF.Abs(sampleY - (cy + 0.5f));
            if (yFactor <= 0f)
            {
                continue;
            }

            for (var cx = centreX - 1; cx <= centreX + 1; cx++)
            {
                var xFactor = 1f - MathF.Abs(sampleX - (cx + 0.5f));
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
    /// (0–1) as shallow when a land cell is within two cells, else deep.
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
                return HasLandWithinTwo(cells, width, height, x, y)
                    ? TerrainSurfaceClass.Shallow
                    : TerrainSurfaceClass.Deep;
            default:
                // Plain (2) and every river connectivity code (6–11) are plain for the surface; an
                // unknown code is treated as plain rather than inventing a class.
                return TerrainSurfaceClass.Plain;
        }
    }

    /// <summary>Whether a passable-by-armies land cell lies within two cells (Chebyshev) of (x, y).</summary>
    public static bool HasLandWithinTwo(int[] cells, int width, int height, int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= height)
            {
                continue;
            }

            for (var dx = -2; dx <= 2; dx++)
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
    /// The deterministic displacement of the sampling position at a map point, in cells — a fixed value
    /// noise of three octaves, at most <see cref="MaxNoiseAmplitudeCells"/> per axis. Pure function of
    /// position and a constant seed; never a random draw.
    /// </summary>
    public static SplatPoint NoiseDisplacementAt(float x, float y)
    {
        var nx = FractalNoise(x, y, 1013);
        var ny = FractalNoise(x + 37.3f, y - 11.7f, 2027);
        return new SplatPoint(nx * MaxNoiseAmplitudeCells, ny * MaxNoiseAmplitudeCells);
    }

    /// <summary>The river strokes of a grid: one quadratic Bézier per river cell, exit midpoint to exit
    /// midpoint through the cell centre, so strokes of neighbouring cells meet on the shared edge.</summary>
    public static IReadOnlyList<RiverStroke> RiverStrokes(int[] cells, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var strokes = new List<RiverStroke>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var code = cells[(y * width) + x];
                if (!IsRiverCode(code))
                {
                    continue;
                }

                var (firstExit, secondExit) = ExitsForCode(code);
                var start = ExitMidpoint(x, y, firstExit);
                var end = ExitMidpoint(x, y, secondExit);
                strokes.Add(new RiverStroke(start, new SplatPoint(x + 0.5f, y + 0.5f), end));
            }
        }

        return strokes;
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

    private static float FractalNoise(float x, float y, int seed)
    {
        var value = 0f;
        var amplitude = 1f;
        var frequency = 1f;
        var normalizer = 0f;
        for (var octave = 0; octave < 3; octave++)
        {
            value += amplitude * ValueNoise(x * frequency, y * frequency, seed + (octave * 101));
            normalizer += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return value / normalizer; // -1..1
    }

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
