using IC2.Engine.Core;

namespace IC2.Engine.MapGen.Styles;

/// <summary>
/// T294c: the terrain side of the five shipped recipes — an all-sea canvas, warped-disc landmasses
/// and sea arms, crest ridges with forest-coded foothill flanks and deliberate plain passes
/// (docs/design/terrain-authoring.md), cardinal-step rivers carrying the shipped directional exit
/// codes, and the dry-interior/coastal-plain texture. Everything is a deterministic function of the
/// streams the caller passes; nothing here draws from any campaign RNG.
/// </summary>
public sealed class StyleCanvas
{
    public StyleCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Codes = new int[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public int[] Codes { get; }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Code(int x, int y) => Codes[(y * Width) + x];

    public void Set(int x, int y, int code)
    {
        if (InBounds(x, y)) Codes[(y * Width) + x] = code;
    }

    public bool IsLand(int x, int y) => InBounds(x, y) && CanonicalTiles.PassableByArmies(Code(x, y));

    public bool IsWater(int x, int y) => InBounds(x, y) && CanonicalTiles.PassableByFleets(Code(x, y));

    /// <summary>
    /// Radius wobble for one warped disc: three harmonics over the angle, drawn once from the
    /// stream, so a disc's outline is irregular but a redraw from the same stream is identical.
    /// </summary>
    public static double[] Wobble(SplitMix64Rng rng)
    {
        var harmonics = new double[6];
        for (var i = 0; i < harmonics.Length; i++)
            harmonics[i] = (rng.NextInt(1000) / 1000.0) * 2 * Math.PI;
        return harmonics;
    }

    private static double WobbledRadius(double[] wobble, double angle)
    {
        var f = 1.0;
        for (var i = 0; i < wobble.Length; i++)
            f += Math.Sin(((i + 2) * angle) + wobble[i]) * (0.28 / (i + 2));
        return Math.Max(0.35, f);
    }

    /// <summary>
    /// Paints one irregular disc: cells inside the wobbled radius hand their coordinates to
    /// <paramref name="tileFor"/>. Cells are visited row-major so texture draws stay ordered.
    /// </summary>
    public void PaintDisc(int cx, int cy, int radius, double[] wobble, Func<int, int, int> tileFor)
    {
        var reach = (int)Math.Ceiling(radius * 1.7);
        for (var y = cy - reach; y <= cy + reach; y++)
            for (var x = cx - reach; x <= cx + reach; x++)
            {
                if (!InBounds(x, y)) continue;
                var dx = x - cx;
                var dy = y - cy;
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                if (d > radius * WobbledRadius(wobble, Math.Atan2(dy, dx))) continue;
                Set(x, y, tileFor(x, y));
            }
    }

    /// <summary>
    /// One crest ridge from <paramref name="from"/> to <paramref name="to"/>: mountain tiles only on
    /// the crest line itself, forest-coded foothills on the flanking cells, and <paramref name="passes"/>
    /// deliberate plain corridors straight across the belt so the range stays usable
    /// (docs/design/terrain-authoring.md). Returns the crest cells.
    /// </summary>
    public List<(int X, int Y)> PaintRidge(SplitMix64Rng rng, (int X, int Y) from, (int X, int Y) to, int passes)
    {
        var crest = new List<(int X, int Y)>();
        var (x, y) = from;
        Set(x, y, CanonicalTiles.Mountains);
        crest.Add((x, y));
        while (x != to.X || y != to.Y)
        {
            if (Math.Abs(to.X - x) >= Math.Abs(to.Y - y)) x += Math.Sign(to.X - x);
            else y += Math.Sign(to.Y - y);
            if (rng.NextChance(1, 4)) // small measured jitter keeps the crest from looking ruled
            {
                if (rng.NextChance(1, 2)) x += rng.NextChance(1, 2) ? 1 : -1;
                else y += rng.NextChance(1, 2) ? 1 : -1;
            }

            x = Math.Clamp(x, 1, Width - 2);
            y = Math.Clamp(y, 1, Height - 2);
            Set(x, y, CanonicalTiles.Mountains);
            crest.Add((x, y));
        }

        foreach (var (cx, cy) in crest)
            foreach (var (nx, ny) in Neighbours4(cx, cy))
                if (IsLand(nx, ny) && !IsMountain(nx, ny))
                    Set(nx, ny, CanonicalTiles.Forest);

        // The passes: plain corridors run across the belt (along the long axis), past both flanks,
        // so a range crossing is a continuous plain route rather than a lone plain cell.
        for (var p = 1; p <= passes; p++)
        {
            var at = crest[(crest.Count * p) / (passes + 1)];
            var alongX = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
            var reach = 3;
            if (alongX)
            {
                var dir = Math.Sign(to.X - from.X);
                for (var d = -reach; d <= reach; d++)
                {
                    Set(at.X + (d * dir), at.Y, CanonicalTiles.Plain);
                    Set(at.X + (d * dir), at.Y + 1, CanonicalTiles.Plain);
                }
            }
            else
            {
                var dir = Math.Sign(to.Y - from.Y);
                for (var d = -reach; d <= reach; d++)
                {
                    Set(at.X, at.Y + (d * dir), CanonicalTiles.Plain);
                    Set(at.X + 1, at.Y + (d * dir), CanonicalTiles.Plain);
                }
            }
        }

        return crest;
    }

    /// <summary>
    /// Walks one river from a spring to the water in cardinal steps, steered downhill along the
    /// caller's noise field with jitter, and stops at any water or river cell. Returns the path
    /// including the spring and the last land cell; empty when the spring immediately has no move.
    /// </summary>
    public List<(int X, int Y)> WalkRiver(SplitMix64Rng rng, int springX, int springY, double[] downhill)
    {
        var path = new List<(int X, int Y)> { (springX, springY) };
        var seen = new HashSet<(int, int)> { (springX, springY) };
        var limit = 4 * (Width + Height);
        while (path.Count < limit)
        {
            var (x, y) = path[^1];
            (int X, int Y)[] options = [.. Neighbours4(x, y).Where(o =>
                InBounds(o.X, o.Y) && !seen.Contains(o) && !IsMountain(o.X, o.Y))];
            if (options.Length == 0) break;
            var best = -1.0;
            var pick = options[0];
            foreach (var o in options)
            {
                if (IsWater(o.X, o.Y) || IsRiver(o.X, o.Y)) return EndingAt(path, o);
                var score = 1.0 - downhill[(o.Y * Width) + o.X] + (rng.NextInt(100) / 100.0 * 0.8);
                if (score > best)
                {
                    best = score;
                    pick = o;
                }
            }

            path.Add(pick);
            seen.Add(pick);
        }

        return path;
    }

    private static List<(int X, int Y)> EndingAt(List<(int X, int Y)> path, (int X, int Y) mouth)
    {
        var full = new List<(int X, int Y)>(path) { mouth };
        return full;
    }

    /// <summary>
    /// Writes the shipped directional exit codes along a walked path (the italy build's scheme:
    /// each river cell names its two flow exits; the spring points its tail, the last cell points
    /// at its mouth or the river it joined). Confluences leave the receiving cell untouched.
    /// </summary>
    public void WriteRiverCodes(IReadOnlyList<(int X, int Y)> path)
    {
        if (path.Count == 0) return;
        for (var i = 0; i < path.Count - 1; i++)
        {
            var exits = new HashSet<char> { Direction(path[i], path[i + 1]) };
            if (i > 0) exits.Add(Direction(path[i], path[i - 1]));
            else exits.Add(Opposite(Direction(path[i], path[i + 1])));
            if (exits.Count != 2) continue;
            Set(path[i].X, path[i].Y, ExitCode(exits));
        }
    }

    public static char Direction((int X, int Y) from, (int X, int Y) to)
    {
        if (to.X > from.X) return 'E';
        if (to.X < from.X) return 'W';
        if (to.Y > from.Y) return 'S';
        return 'N';
    }

    public static char Opposite(char d) => d switch { 'N' => 'S', 'S' => 'N', 'E' => 'W', _ => 'E' };

    public static int ExitCode(IReadOnlySet<char> exits) => exits.SetEquals(['W', 'E']) ? 6
        : exits.SetEquals(['N', 'S']) ? 7
        : exits.SetEquals(['E', 'N']) ? 8
        : exits.SetEquals(['E', 'S']) ? 9
        : exits.SetEquals(['W', 'S']) ? 10
        : 11; // W+N

    public bool IsMountain(int x, int y) => InBounds(x, y) && Code(x, y) == CanonicalTiles.Mountains;

    public bool IsRiver(int x, int y) => InBounds(x, y) && Code(x, y) >= CanonicalTiles.River1;

    /// <summary>Smooth value noise in [0,1): a lattice per octave, bilinear with a smoothstep.</summary>
    public static double[] NoiseField(SplitMix64Rng rng, int width, int height, int scale, int octaves)
    {
        var field = new double[width * height];
        var amplitude = 1.0;
        var total = 0.0;
        for (var octave = 0; octave < octaves; octave++)
        {
            var latticeW = (width / scale) + 2;
            var latticeH = (height / scale) + 2;
            var lattice = new double[latticeW * latticeH];
            for (var i = 0; i < lattice.Length; i++) lattice[i] = rng.NextInt(1000) / 1000.0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var fx = (double)x / scale;
                    var fy = (double)y / scale;
                    var x0 = (int)fx;
                    var y0 = (int)fy;
                    var tx = Smooth(fx - x0);
                    var ty = Smooth(fy - y0);
                    var a = lattice[(y0 * latticeW) + x0];
                    var b = lattice[(y0 * latticeW) + x0 + 1];
                    var c = lattice[((y0 + 1) * latticeW) + x0];
                    var d = lattice[((y0 + 1) * latticeW) + x0 + 1];
                    field[(y * width) + x] += amplitude * (a + ((b - a) * tx) + (((c + ((d - c) * ty)) - (a + ((b - a) * tx))) * ty));
                }

            total += amplitude;
            amplitude *= 0.5;
            scale = Math.Max(2, scale / 2);
        }

        for (var i = 0; i < field.Length; i++) field[i] /= total;
        return field;
    }

    private static double Smooth(double t) => t * t * (3 - (2 * t));

    /// <summary>
    /// Rough land distance to the nearest water cell, capped at <paramref name="maxDist"/> (water
    /// itself is 0). Diagonal steps count as one, matching the engine's 8-neighbour walks.
    /// </summary>
    public int[] DistanceToWater(int maxDist)
    {
        var dist = new int[Codes.Length];
        Array.Fill(dist, maxDist + 1);
        var queue = new Queue<int>();
        for (var i = 0; i < Codes.Length; i++)
            if (CanonicalTiles.PassableByFleets(Codes[i]))
            {
                dist[i] = 0;
                queue.Enqueue(i);
            }

        while (queue.TryDequeue(out var at))
        {
            var x = at % Width;
            var y = at / Width;
            if (dist[at] >= maxDist) continue;
            foreach (var (nx, ny) in Neighbours8(x, y))
            {
                if (!InBounds(nx, ny)) continue;
                var ni = (ny * Width) + nx;
                if (dist[ni] > dist[at] + 1)
                {
                    dist[ni] = dist[at] + 1;
                    queue.Enqueue(ni);
                }
            }
        }

        return dist;
    }

    /// <summary>
    /// Forces a two-cell sea ring around the map edge. Generated land never relies on edge tiles,
    /// so the ring guarantees the fleets' edge water stays one connected sea — every port reaches
    /// every shore — and leaves water-edge tiles for barbarian arrivals (the T294a audit's edge rule).
    /// </summary>
    public void CarveSeaBorder(int thickness = 2)
    {
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (x < thickness || y < thickness || x >= Width - thickness || y >= Height - thickness)
                    Set(x, y, CanonicalTiles.SeaCalm);
            }
    }

    private IEnumerable<(int X, int Y)> Neighbours4(int x, int y)
    {
        if (y > 0) yield return (x, y - 1);
        if (y < Height - 1) yield return (x, y + 1);
        if (x > 0) yield return (x - 1, y);
        if (x < Width - 1) yield return (x + 1, y);
    }

    private IEnumerable<(int X, int Y)> Neighbours8(int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && InBounds(x + dx, y + dy))
                    yield return (x + dx, y + dy);
    }

    /// <summary>
    /// The largest 8-connected component of land (or water), as cell indices. Settlements belong to
    /// the mainland and fleets to the main sea: an island town or a lakeside berth would strand its
    /// record off the component the validator connects (T294c's island/lake guard).
    /// </summary>
    public HashSet<int> LargestComponent(bool land)
    {
        var seen = new bool[Codes.Length];
        HashSet<int>? best = null;
        for (var start = 0; start < Codes.Length; start++)
        {
            if (seen[start]) continue;
            var wanted = land
                ? CanonicalTiles.PassableByArmies(Codes[start])
                : CanonicalTiles.PassableByFleets(Codes[start]);
            if (!wanted) continue;
            var component = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.TryDequeue(out var at))
            {
                component.Add(at);
                var x = at % Width;
                var y = at / Width;
                foreach (var (nx, ny) in Neighbours8(x, y))
                {
                    var ni = (ny * Width) + nx;
                    if (seen[ni]) continue;
                    var ok = land
                        ? CanonicalTiles.PassableByArmies(Codes[ni])
                        : CanonicalTiles.PassableByFleets(Codes[ni]);
                    if (!ok) continue;
                    seen[ni] = true;
                    queue.Enqueue(ni);
                }
            }

            if (best is null || component.Count > best.Count) best = component;
        }

        return best ?? [];
    }
}
