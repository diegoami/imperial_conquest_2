using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T148.md</c> (as amended by plan PRs #807, #808, #811 and #813) Done-when 1:
/// <see cref="TerrainSplatMap"/> turns the world's terrain grid into the six per-pixel surface weights
/// the shader samples — a 4 × 4-per-cell bake whose channels are named, the cell's own class winning at
/// its centre both in the continuous function and in the Catmull-Rom bicubic the shader actually reads,
/// a smoothed-then-sharpened border (0.15 ± 0.02 cell wide) that turns a one-cell staircase into a
/// straight line, shallow sea within one cell of land, bounded deterministic field noise — and gives
/// each river chain one smooth, corner-cut polyline sampled uniformly every 0.1 cell of arc length,
/// confluences visibly joining. Godot-free, so it is exercised here directly rather than through the
/// headless map check.
/// </summary>
public sealed class TerrainSplatMapTests
{
    private static readonly GameDataRepository Repository =
        GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>The classical world is 320 × 140 = 44,800 cells, 1,280 × 560 bake samples.</summary>
    private const int ClassicalWidth = 320;
    private const int ClassicalHeight = 140;

    [Fact]
    public void The_classical_world_bakes_a_four_by_four_lattice()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var map = TerrainSplatMap.Bake(world);

        Assert.Equal(ClassicalWidth, map.Width);
        Assert.Equal(ClassicalHeight, map.Height);
        Assert.Equal(ClassicalWidth * 4, map.LatticeWidth);
        Assert.Equal(ClassicalHeight * 4, map.LatticeHeight);
        Assert.Equal(map.LatticeWidth * map.LatticeHeight * 4, map.SplatA.Length);
        Assert.Equal(map.LatticeWidth * map.LatticeHeight * 4, map.SplatB.Length);
    }

    /// <summary>
    /// Done-when 1: the bake really is the continuous function sampled at the centre of each
    /// quarter-cell. Recomputing <see cref="TerrainSplatMap.WeightsAt(int[],int,int,float,float)"/> at
    /// the quarter-cell centres of the classical world's cell (200, 70) and comparing each named
    /// channel is the proof the bake's layout and positions are what the shader assumes.
    /// </summary>
    [Fact]
    public void Each_bake_sample_is_the_continuous_function_at_its_quarter_cell_centre()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var map = TerrainSplatMap.Bake(world);

        const int cellX = 200;
        const int cellY = 70;
        for (var j = 0; j < TerrainSplatMap.SamplesPerCell; j++)
        {
            for (var i = 0; i < TerrainSplatMap.SamplesPerCell; i++)
            {
                var x = cellX + ((i + 0.5f) / TerrainSplatMap.SamplesPerCell);
                var y = cellY + ((j + 0.5f) / TerrainSplatMap.SamplesPerCell);
                var expected = TerrainSplatMap.WeightsAt(cells, world.Width, world.Height, x, y);
                ReadSample(map, (cellX * 4) + i, (cellY * 4) + j, out var actual);

                // The bake rounds each channel to a byte and moves the tiny remainder onto the largest
                // channel so the six sum to exactly 255, so each channel is within 3 of the continuous
                // function's value at this position.
                AssertWithinThree(expected.Plain, actual.Plain, $"plain at ({x}, {y})");
                AssertWithinThree(expected.Desert, actual.Desert, $"desert at ({x}, {y})");
                AssertWithinThree(expected.Forest, actual.Forest, $"forest at ({x}, {y})");
                AssertWithinThree(expected.Mountain, actual.Mountain, $"mountain at ({x}, {y})");
                AssertWithinThree(expected.Shallow, actual.Shallow, $"shallow at ({x}, {y})");
                // Done-when 1 (the Opus review of round 3, N4): all six channels, deep sea included —
                // a desert/forest channel swap was already caught, a deep/shallow one was not.
                AssertWithinThree(expected.Deep, actual.Deep, $"deep at ({x}, {y})");
            }
        }
    }

    /// <summary>
    /// Done-when 1: every sample's six weights are in the named channels and sum to 255 (±2) — splat_a's
    /// R/G/B/A carry plain/desert/forest/mountain, splat_b's R/G carry shallow/deep.
    /// </summary>
    [Fact]
    public void Every_sample_holds_the_six_weights_in_the_named_channels_summing_to_255()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var map = TerrainSplatMap.Bake(world);

        for (var sample = 0; sample < map.LatticeWidth * map.LatticeHeight; sample++)
        {
            var offset = sample * 4;
            var sum = map.SplatA[offset] + map.SplatA[offset + 1]
                + map.SplatA[offset + 2] + map.SplatA[offset + 3]
                + map.SplatB[offset] + map.SplatB[offset + 1];
            Assert.InRange(sum, 253, 257);
        }
    }

    /// <summary>
    /// Done-when 1, the centre rule: over all 44,800 classical-world cells the cell's own class has the
    /// highest weight in <see cref="TerrainSplatMap.WeightsAt(float,float)"/> at the cell centre, and in
    /// <see cref="TerrainSplatMap.SampleBicubic(float,float)"/> at that centre — the kernel the shader
    /// actually reads there, its real output and overshoot included. A port therefore sits on its own
    /// terrain and a coastline never crosses a centre.
    /// </summary>
    [Fact]
    public void The_cells_own_class_wins_at_its_centre_in_the_function_and_in_the_bicubic_sample()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var map = TerrainSplatMap.Bake(world);

        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                var own = TerrainSplatMap.ClassOf(cells, world.Width, world.Height, x, y);

                var atCentre = map.WeightsAt(x + 0.5f, y + 0.5f);
                AssertOwnClassWins(own, atCentre, $"the function at the centre of cell ({x}, {y})");

                // What the shader reads at the centre, through the Catmull-Rom kernel over the bake.
                var sampled = map.SampleBicubic(x + 0.5f, y + 0.5f);
                AssertOwnClassWins(own, sampled, $"SampleBicubic at the centre of cell ({x}, {y})");
            }
        }
    }

    /// <summary>
    /// Done-when 1: two samples on either side of a plain–forest edge blend both classes, each side's
    /// own class winning. The border is no longer a fixed-width blend band but a smoothed-then-sharpened
    /// field, so the test scans the flank of a long scripted edge for the first sample that lands on its
    /// own side with the neighbour still present — deterministic, since the field is a pure function.
    /// </summary>
    [Fact]
    public void Two_samples_across_a_plain_forest_edge_blend_with_each_side_winning()
    {
        // 64 × 8: plain left of x = 32, forest right of it; the shared edge runs along x = 32.
        var width = 64;
        var height = 8;
        var cells = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[(y * width) + x] = x < 32 ? 2 : 4;
            }
        }

        var plainSide = FindBlendSample(cells, width, height, own: TerrainSurfaceClass.Plain, neighbour: TerrainSurfaceClass.Forest);
        var forestSide = FindBlendSample(cells, width, height, own: TerrainSurfaceClass.Forest, neighbour: TerrainSurfaceClass.Plain);

        Assert.NotNull(plainSide);
        Assert.NotNull(forestSide);
    }

    /// <summary>
    /// Done-when 1: a sea cell next to land (within one cell, Chebyshev) is shallow and one two or more
    /// cells from land is deep — the thin rim the user decided on 2026-10-06, whatever the sea code; and
    /// every river connectivity code counts as plain.
    /// </summary>
    [Fact]
    public void Sea_is_shallow_within_one_cell_of_land_deep_at_two_and_rivers_are_plain()
    {
        // One row of sea with the only land cell at x = 3, so the Chebyshev distance reads directly.
        var row = new[] { 0, 1, 0, 2, 0, 1, 0 };
        const int width = 7;
        const int height = 1;

        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 2, 0));
        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 4, 0));

        // Two cells away is already deep: the 2-cell band of the first attempt is gone.
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(row, width, height, 1, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(row, width, height, 5, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(row, width, height, 0, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(row, width, height, 6, 0));

        // The sea code itself never carries the distinction: a code-1 cell next to land is shallow, a
        // code-0 cell far from land is deep (the shipped world has no code-1 cell at all).
        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(new[] { 1, 2 }, 2, 1, 0, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(new[] { 0, 0 }, 2, 1, 0, 0));

        // Every river connectivity code is plain for the surface.
        for (var code = 6; code <= 11; code++)
        {
            Assert.Equal(TerrainSurfaceClass.Plain, TerrainSplatMap.ClassOf(new[] { code }, 1, 1, 0, 0));
        }
    }

    /// <summary>Done-when 1: the same grid bakes byte-identical weights on two calls — no random draw.</summary>
    [Fact]
    public void Two_bakes_of_the_same_grid_are_byte_identical()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);

        var first = TerrainSplatMap.Bake(world.Width, world.Height, cells);
        var second = TerrainSplatMap.Bake(world.Width, world.Height, cells);

        Assert.Equal(first.SplatA, second.SplatA);
        Assert.Equal(first.SplatB, second.SplatB);
    }

    /// <summary>
    /// Done-when 1, the field noise: at 1,000 fixed sample points every class's noise is within
    /// ±0.06 (the literal bound the Done-when names, not the constant: the Opus review of round 3, N1),
    /// and two calls return the identical value — a pure function of position and a constant seed,
    /// never a random draw.
    /// </summary>
    [Fact]
    public void The_field_noise_is_bounded_and_identical_on_two_calls()
    {
        foreach (var (x, y) in FixedSamplePoints(1_000))
        {
            foreach (var cls in Enum.GetValues<TerrainSurfaceClass>())
            {
                var noise = TerrainSplatMap.ClassNoise(cls, x, y);
                Assert.InRange(noise, -0.06f, 0.06f);
                Assert.Equal(noise, TerrainSplatMap.ClassNoise(cls, x, y));
            }
        }
    }

    /// <summary>
    /// Done-when 1, no stair-steps: on an 80 × 80 grid whose cell (x, y) is plain when x + y &lt; 40 and
    /// sea otherwise (a diagonal one-cell staircase), the plain weight crosses 0.5 exactly once on each
    /// of **300** lines, the k-th through (5 + 0.1k, 35 − 0.1k) in the direction (1, 1)/√2 — the
    /// stair-step's every phase at 0.1-cell spacing, not only its corners (the Opus review of round 3,
    /// R1: the 30 integer-corner lines all sampled the same phase and could not see the smoothing) —
    /// and the 300 crossings lie within 0.08 cell of their least-squares straight line. With the
    /// sampling ±1.5 cell and every sample at least 3.9 cells inside the grid, the smoothing's 2-cell
    /// reach never reads the world's edge.
    /// </summary>
    [Fact]
    public void A_diagonal_staircase_coast_blurs_into_a_straight_line()
    {
        const int width = 80;
        const int height = 80;
        var cells = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[(y * width) + x] = x + y < 40 ? 2 : 0;
            }
        }

        var direction = 1f / MathF.Sqrt(2f);
        var crossings = new List<(float X, float Y)>();
        for (var k = 0; k < 300; k++)
        {
            var px = 5f + (0.1f * k);
            var py = 35f - (0.1f * k);
            var t = FindPlainCrossing(cells, width, height, px, py, direction, direction, $"the diagonal line {k}");
            crossings.Add((px + (t * direction), py + (t * direction)));
        }

        AssertLineDeviation(crossings, 0.08f, "the diagonal staircase's 0.5 crossings");
    }

    /// <summary>
    /// Done-when 1, no stair-steps: the same test on an 80 × 80 grid with plain for y &lt; 40 and sea for
    /// y ≥ 40, with 300 vertical lines through (25 + 0.1k, 40), gives crossings within 0.08 cell of
    /// their least-squares line (the straight coast is already straight without smoothing, but the test
    /// still catches a doubled noise bound: the Opus review of round 3, N1).
    /// </summary>
    [Fact]
    public void A_straight_staircase_coast_blurs_into_a_straight_line()
    {
        const int width = 80;
        const int height = 80;
        var cells = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[(y * width) + x] = y < 40 ? 2 : 0;
            }
        }

        var crossings = new List<(float X, float Y)>();
        for (var k = 0; k < 300; k++)
        {
            var px = 25f + (0.1f * k);
            var t = FindPlainCrossing(cells, width, height, px, 40f, 0f, 1f, $"the vertical line {k}");
            crossings.Add((px, 40f + t));
        }

        AssertLineDeviation(crossings, 0.08f, "the straight staircase's 0.5 crossings");
    }

    /// <summary>
    /// Done-when 1, the border width (the Opus review of round 3, R2): across the same straight coast,
    /// the distance between the points where the plain weight is 0.9 and 0.1 is 0.15 ± 0.02 cell. The
    /// softmax steepness is the one constant that sets it; before the round-4 fix it measured 0.062.
    /// </summary>
    [Fact]
    public void The_border_across_the_straight_coast_is_fifteen_hundredths_of_a_cell_wide()
    {
        const int width = 80;
        const int height = 80;
        var cells = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                cells[(y * width) + x] = y < 40 ? 2 : 0;
            }
        }

        var width09To01 = BorderWidthAt(cells, width, height, 40.5f);
        Assert.True(
            MathF.Abs(width09To01 - 0.15f) <= 0.02f,
            $"the plain weight 0.9-to-0.1 border width is {width09To01:F4} cell, expected 0.15 ± 0.02");
    }

    /// <summary>
    /// Done-when 1 (Sol's review of PR 813, R2): the surf constant is at most 0.15, and the shader takes
    /// the rim tint, its mix factor and the surf from uniforms — bound by GameMapView from
    /// TerrainSplatMap's public constants — not from literals. The splat sampling is the named bicubic
    /// function, so the lattice's zig-zag is smoothed (the Opus review of round 4's R2).
    /// </summary>
    [Fact]
    public void The_shore_constants_are_bounded_and_the_shader_reads_them_as_uniforms()
    {
        Assert.True(
            TerrainSplatMap.SurfStrength <= 0.15f,
            $"the surf strength {TerrainSplatMap.SurfStrength} must be at most 0.15");
        Assert.InRange(TerrainSplatMap.ShallowToneDown, 0f, 1f);
        Assert.InRange(TerrainSplatMap.ShallowTintR, 0f, 1f);
        Assert.InRange(TerrainSplatMap.ShallowTintG, 0f, 1f);
        Assert.InRange(TerrainSplatMap.ShallowTintB, 0f, 1f);

        var shaderPath = Path.Combine(ModelTestPaths.RepositoryRoot, "godot", "UI", "TerrainSurface.gdshader");
        var code = File.ReadAllText(shaderPath);
        Assert.Contains("uniform vec3 shallow_tint", code);
        Assert.Contains("uniform float shallow_tone_down", code);
        Assert.Contains("uniform float surf_strength", code);

        // The fragment must USE the uniforms, so changing the C# constant changes the draw.
        Assert.Contains("mix(texture(tex_shallow, surface_uv).rgb, shallow_tint, shallow_tone_down)", code);
        Assert.Contains("surf_strength)", code);

        // The two splat textures are read through the named Catmull-Rom bicubic function, not the
        // bilinear texture() call the round-3 review measured the zig-zag from.
        Assert.Contains("sample_splat_bicubic(splat_a, UV)", code);
        Assert.Contains("sample_splat_bicubic(splat_b, UV)", code);
    }

    /// <summary>
    /// Done-when 1: the shader's bicubic kernel is the separable Catmull-Rom the entry names — at a
    /// sample gap's midpoint the four per-axis weights are −1/16, 9/16, 9/16, −1/16 — and the C# mirror
    /// reproduces the shader's arithmetic.
    /// </summary>
    [Fact]
    public void The_bicubic_kernel_is_the_catmull_rom_the_entry_names()
    {
        var shaderPath = Path.Combine(ModelTestPaths.RepositoryRoot, "godot", "UI", "TerrainSurface.gdshader");
        var code = File.ReadAllText(shaderPath);
        Assert.Contains("vec4 cubic_weights(float t)", code);
        Assert.Contains("-0.5 * t3 + t2 - 0.5 * t", code);
        Assert.Contains("1.5 * t3 - 2.5 * t2 + 1.0", code);
        Assert.Contains("-1.5 * t3 + 2.0 * t2 + 0.5 * t", code);
        Assert.Contains("0.5 * t3 - 0.5 * t2", code);
    }

    /// <summary>
    /// Done-when 1: a lone plain cell surrounded by sea keeps plain as its highest weight at its centre —
    /// the centre pin, so a one-cell island or a narrow strait never loses its own terrain.
    /// </summary>
    [Fact]
    public void A_lone_plain_cell_keeps_plain_as_its_highest_weight_at_its_centre()
    {
        const int width = 9;
        const int height = 9;
        var cells = new int[width * height];
        Array.Fill(cells, 0);
        cells[(4 * width) + 4] = 2;

        var weights = TerrainSplatMap.WeightsAt(cells, width, height, 4.5f, 4.5f);
        AssertOwnClassWins(TerrainSurfaceClass.Plain, weights, "a lone plain cell's centre");
    }

    /// <summary>
    /// Done-when 1, rivers: the scripted chain (0,0)=6, (1,0)=6, (2,0)=10, (2,1)=7, (2,2)=8, (3,2)=6,
    /// (4,2)=6 is exactly one chain; its polyline passes within 0.35 cell of each of the seven centres,
    /// its 0.1-cell samples turn less than 20°, its endpoints are (0,0)'s west and (4,2)'s east exit
    /// midpoints within 0.01 (unlinked exits end the chain), and two calls give identical polylines.
    /// </summary>
    [Fact]
    public void The_scripted_river_is_one_smooth_chain_end_to_end()
    {
        var cells = new int[6 * 4];
        Array.Fill(cells, 2);
        cells[(0 * 6) + 0] = 6;
        cells[(0 * 6) + 1] = 6;
        cells[(0 * 6) + 2] = 10;
        cells[(1 * 6) + 2] = 7;
        cells[(2 * 6) + 2] = 8;
        cells[(2 * 6) + 3] = 6;
        cells[(2 * 6) + 4] = 6;

        var chains = TerrainSplatMap.BuildRiverChains(cells, 6, 4);
        Assert.Single(chains);
        Assert.Equal(0, chains[0].JoinStartPoints);
        Assert.Equal(0, chains[0].JoinEndPoints);
        var line = chains[0].Polyline;

        (float X, float Y)[] centres =
        [
            (0.5f, 0.5f), (1.5f, 0.5f), (2.5f, 0.5f), (2.5f, 1.5f), (2.5f, 2.5f), (3.5f, 2.5f), (4.5f, 2.5f),
        ];
        foreach (var centre in centres)
        {
            var distance = MinDistanceToPolyline(line, new SplatPoint(centre.X, centre.Y));
            Assert.True(distance <= 0.35f, $"the polyline stays within 0.35 of the centre ({centre.X}, {centre.Y}) (got {distance:F3})");
        }

        Assert.True(line.Count > 10, $"the polyline of a seven-cell river has {line.Count} points");
        for (var k = 0; k < line.Count - 1; k++)
        {
            var gap = Distance(line[k], line[k + 1]);
            Assert.True(gap <= 0.11f, $"consecutive points {k} are {gap:F4} apart, over 0.11");
            // Done-when 1: the spacing is uniform, so the LAST gap is inside 0.1 ± 0.01 too (the Opus
            // review of round 3, R3: the old walk appended a short last gap).
            Assert.True(gap >= TerrainSplatMap.RiverPolylineStepCells - 0.01f,
                $"consecutive points {k} are {gap:F4} apart, under the 0.1 ± 0.01 sample spacing");
        }

        AssertTurnsUnder(line, 20.0, 0, 0, "the scripted river");

        AssertClose(new SplatPoint(0f, 0.5f), line[0], "the chain starts at (0,0)'s west exit midpoint");
        AssertClose(new SplatPoint(5f, 2.5f), line[^1], "the chain ends at (4,2)'s east exit midpoint");

        var again = TerrainSplatMap.BuildRiverChains(cells, 6, 4);
        Assert.Equal(
            chains[0].Polyline.Select(p => (p.X, p.Y)),
            again[0].Polyline.Select(p => (p.X, p.Y)));
    }

    /// <summary>
    /// Done-when 1 (the Opus review of round 2, R1): a chain ending on a bend, (0,0)=6, (1,0)=10,
    /// (1,1)=7, turns by less than 20° per segment through its last centre and reaches exactly its two
    /// exit midpoints — the round-2 off-by-one kinked the last centre of every such chain.
    /// </summary>
    [Fact]
    public void A_chain_ending_on_a_bend_keeps_a_continuous_tangent_through_its_last_centre()
    {
        var cells = new int[4 * 3];
        Array.Fill(cells, 2);
        cells[0] = 6;
        cells[1] = 10;
        cells[(1 * 4) + 1] = 7;

        var chains = TerrainSplatMap.BuildRiverChains(cells, 4, 3);
        Assert.Single(chains);
        var line = chains[0].Polyline;

        (float X, float Y)[] centres = [(0.5f, 0.5f), (1.5f, 0.5f), (1.5f, 1.5f)];
        foreach (var centre in centres)
        {
            var distance = MinDistanceToPolyline(line, new SplatPoint(centre.X, centre.Y));
            Assert.True(distance <= 0.35f, $"the bend-ending polyline stays within 0.35 of ({centre.X}, {centre.Y}) (got {distance:F3})");
        }

        AssertTurnsUnder(line, 20.0, 0, 0, "the bend-ending chain");
        AssertClose(new SplatPoint(0f, 0.5f), line[0], "the bend-ending chain starts at its west exit midpoint");
        AssertClose(new SplatPoint(1.5f, 2f), line[^1], "the bend-ending chain ends at its south exit midpoint");
    }

    /// <summary>
    /// Done-when 1, confluence (Sol's review of PR 808, round 2, R1): a third river cell whose exit
    /// faces a river cell that does not exit back ends that cell's chain at its exit midpoint and adds
    /// the joining segment — the tributary polyline's last point lies within 0.05 cell of the receiving
    /// chain's polyline, so the two visibly join with no gap.
    /// </summary>
    [Fact]
    public void A_confluence_ends_at_the_exit_and_joins_the_receiving_chain()
    {
        // The main river runs east–west along y = 0 (four code-6 cells); a code-7 tributary at (1,1)
        // exits north toward (1,0), which does not exit back — a confluence the codes cannot encode.
        var cells = new int[6 * 3];
        Array.Fill(cells, 2);
        cells[0] = 6;
        cells[1] = 6;
        cells[2] = 6;
        cells[3] = 6;
        cells[(1 * 6) + 1] = 7;

        var chains = TerrainSplatMap.BuildRiverChains(cells, 6, 3);
        Assert.Equal(2, chains.Count);

        var main = chains.Single(chain => Distance(chain.Polyline[0], new SplatPoint(0f, 0.5f)) < 0.01f);
        var tributary = chains.Single(chain => chain != main);

        // The tributary's chain ends at its north exit midpoint (1.5, 1) — the confluence exit — and
        // the joining segment trails from there.
        Assert.Contains(
            tributary.Polyline,
            point => Distance(point, new SplatPoint(1.5f, 1f)) <= 0.01f);
        Assert.True(tributary.JoinEndPoints > 0, "the tributary carries its confluence joining segment");

        var last = tributary.Polyline[^1];
        var distance = MinDistanceToPolyline(main.Polyline, last);
        Assert.True(distance <= 0.05f, $"the tributary's last point is {distance:F4} from the receiving chain, expected ≤ 0.05");

        // Its start reaches the coast end as always: (1,1)'s south exit midpoint (1.5, 2).
        AssertClose(new SplatPoint(1.5f, 2f), tributary.Polyline[0], "the tributary starts at its south exit midpoint");
    }

    /// <summary>
    /// Done-when 1: on the classical world every river cell lies within 0.35 cell of some chain's
    /// polyline — no river cell is left undrawn.
    /// </summary>
    [Fact]
    public void Every_classical_river_cell_lies_near_a_chain()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var chains = TerrainSplatMap.BuildRiverChains(cells, world.Width, world.Height);
        Assert.NotEmpty(chains);

        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                var code = cells[(y * world.Width) + x];
                if (!TerrainSplatMap.IsRiverCode(code))
                {
                    continue;
                }

                var centre = new SplatPoint(x + 0.5f, y + 0.5f);
                var best = double.MaxValue;
                foreach (var chain in chains)
                {
                    best = Math.Min(best, MinDistanceToPolyline(chain.Polyline, centre));
                    if (best <= 0.35)
                    {
                        break;
                    }
                }

                Assert.True(best <= 0.35, $"river cell ({x}, {y}) code {code} is {best:F3} from every chain");
            }
        }
    }

    /// <summary>
    /// Done-when 1: the river wobble is the 0.1–0.15-cell meander the user asked for, not the old
    /// invisible 0.04 — the amplitude constant is inside that range and the fitted classical chains
    /// really reach 0.1 cell of displacement somewhere (the Opus review of round 3's visual note: the
    /// old 0.04-cell wobble was about 1.3 px at 32 px a cell and invisible).
    /// </summary>
    [Fact]
    public void The_classical_rivers_wobble_visibly_within_the_designed_band()
    {
        Assert.InRange(TerrainSplatMap.RiverWobbleAmplitudeCells, 0.1f, 0.15f);
        Assert.InRange(TerrainSplatMap.RiverWobblePeriodCells, 2f, 4f);

        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var chains = TerrainSplatMap.BuildRiverChains(cells, world.Width, world.Height);
        Assert.NotEmpty(chains);

        var best = 0.0;
        foreach (var chain in chains)
        {
            foreach (var point in chain.Polyline)
            {
                var offset = TerrainSplatMap.WobbleAt(point);
                best = Math.Max(best, Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y)));
            }
        }

        Assert.True(best >= 0.1, $"the largest wobble offset on the classical chains is {best:F4} cell, under 0.1");
    }

    /// <summary>
    /// Done-when 1 (the Opus review of round 2, R2): on the classical world every chain turns by less
    /// than 20° between consecutive 0.1-cell segments, a confluence's joining segment excepted. The
    /// joining segment is a straight run that leaves the smoothed curve at the exit midpoint; its
    /// terminal point is tested on its own (within 0.05 cell of the receiving chain's polyline), and the
    /// single turn at the join's junction is not part of the bound.
    /// </summary>
    [Fact]
    public void Every_classical_chain_turns_under_twenty_degrees_a_segment()
    {
        var world = Repository.Resolve("classical-mediterranean").World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var chains = TerrainSplatMap.BuildRiverChains(cells, world.Width, world.Height);
        Assert.NotEmpty(chains);

        foreach (var chain in chains)
        {
            AssertTurnsUnder(chain.Polyline, 20.0, chain.JoinStartPoints, chain.JoinEndPoints, "a classical chain");

            // The joining segment is tested on its own: its terminal point lies on the receiving chain.
            if (chain.JoinEndPoints > 0)
            {
                AssertJoinTouchesAChain(chains, chain, chain.Polyline[^1]);
            }

            if (chain.JoinStartPoints > 0)
            {
                AssertJoinTouchesAChain(chains, chain, chain.Polyline[0]);
            }
        }
    }

    private static void AssertJoinTouchesAChain(
        IReadOnlyList<RiverChain> chains, RiverChain own, SplatPoint joinPoint)
    {
        var best = double.MaxValue;
        foreach (var other in chains)
        {
            if (ReferenceEquals(other, own))
            {
                continue;
            }

            best = Math.Min(best, MinDistanceToPolyline(other.Polyline, joinPoint));
        }

        Assert.True(best <= 0.05, $"a joining segment's terminal point is {best:F4} from every other chain, expected ≤ 0.05");
    }

    /// <summary>
    /// The 1,000 fixed sample points Done-when 1 names, spread deterministically over the classical
    /// world by two irrational strides (no RNG; the same set every run).
    /// </summary>
    private static IEnumerable<(float X, float Y)> FixedSamplePoints(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var fx = (i * 0.6180339887498949d) % 1.0d;
            var fy = (i * 0.7548776662457773d) % 1.0d;
            yield return ((float)(fx * ClassicalWidth), (float)(fy * ClassicalHeight));
        }
    }

    /// <summary>
    /// The 0.9-to-0.1 width, in cells, of the plain weight across the straight coast at column
    /// <paramref name="x"/>: the distance between the 0.9 and 0.1 level crossings found by linear
    /// interpolation on a 0.005-cell vertical walk.
    /// </summary>
    private static float BorderWidthAt(int[] cells, int width, int height, float x)
    {
        const float high = 0.9f;
        const float low = 0.1f;
        float? yHigh = null;
        float? yLow = null;
        float? previousY = null;
        float? previousWeight = null;
        for (var step = 0; step <= 800; step++)
        {
            var y = 38f + (step * 0.005f);
            var weight = TerrainSplatMap.WeightsAt(cells, width, height, x, y).Plain;
            if (previousWeight is { } pw && previousY is { } py)
            {
                CaptureCrossing(pw, weight, py, y, high, ref yHigh);
                CaptureCrossing(pw, weight, py, y, low, ref yLow);
            }

            previousY = y;
            previousWeight = weight;
        }

        Assert.True(yHigh.HasValue && yLow.HasValue, "both the 0.9 and the 0.1 plain-weight crossings exist");
        return MathF.Abs(yLow!.Value - yHigh!.Value);
    }

    private static void CaptureCrossing(
        float previousWeight, float weight, float previousY, float y, float level, ref float? captured)
    {
        if (captured is null && (previousWeight - level) * (weight - level) < 0f)
        {
            var fraction = (level - previousWeight) / (weight - previousWeight);
            captured = previousY + (fraction * (y - previousY));
        }
    }

    /// <summary>
    /// Walks a sample line and returns the parameter t (in cells from the line's named point, along
    /// (dx, dy)) at which the plain weight crosses 0.5, by linear interpolation between the two samples
    /// that bracket it. Asserts the crossing happens exactly once.
    /// </summary>
    private static float FindPlainCrossing(
        int[] cells, int width, int height, float px, float py, float dx, float dy, string where)
    {
        float? previousT = null;
        float? previousWeight = null;
        float? crossing = null;
        var crossings = 0;
        const int steps = 600;
        for (var step = 0; step <= steps; step++)
        {
            var t = -1.5f + (step * 0.005f);
            var weight = TerrainSplatMap.WeightsAt(cells, width, height, px + (t * dx), py + (t * dy)).Plain;
            if (previousWeight is { } pw && previousT is { } pt)
            {
                if ((pw - 0.5f) * (weight - 0.5f) < 0f)
                {
                    crossings++;
                    var fraction = (0.5f - pw) / (weight - pw);
                    crossing = pt + (fraction * (t - pt));
                }
            }

            previousT = t;
            previousWeight = weight;
        }

        Assert.True(crossings == 1, $"{where}: the plain weight crosses 0.5 {crossings} times, expected exactly once");
        return crossing!.Value;
    }

    /// <summary>Fits a least-squares straight line to the points and asserts every point's perpendicular
    /// distance from it is at most <paramref name="tolerance"/>.</summary>
    private static void AssertLineDeviation(List<(float X, float Y)> points, float tolerance, string where)
    {
        Assert.True(points.Count >= 2, $"{where}: too few points to fit a line");
        var n = points.Count;
        var meanX = points.Average(p => (double)p.X);
        var meanY = points.Average(p => (double)p.Y);
        double sxx = 0;
        double sxy = 0;
        double syy = 0;
        foreach (var (x, y) in points)
        {
            sxx += (x - meanX) * (x - meanX);
            sxy += (x - meanX) * (y - meanY);
            syy += (y - meanY) * (y - meanY);
        }

        // Principal axis of the point cloud: the least-squares straight line through it.
        var angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        var nx = -Math.Sin(angle);
        var ny = Math.Cos(angle);
        var worst = 0.0;
        foreach (var (x, y) in points)
        {
            var deviation = Math.Abs((nx * (x - meanX)) + (ny * (y - meanY)));
            worst = Math.Max(worst, deviation);
        }

        Assert.True(worst <= tolerance, $"{where}: the worst perpendicular deviation is {worst:F4} cell, over {tolerance}");
    }

    /// <summary>Asserts every consecutive-segment turn of the polyline is under <paramref name="limit"/>
    /// degrees, skipping the two join junctions (a start join's is at index JoinStartPoints, an end
    /// join's at Count − 1 − JoinEndPoints) that the Done-when exempts.</summary>
    private static void AssertTurnsUnder(
        IReadOnlyList<SplatPoint> line, double limit, int joinStartPoints, int joinEndPoints, string where)
    {
        var endJunction = line.Count - 1 - joinEndPoints;
        for (var k = 1; k < line.Count - 1; k++)
        {
            if (k == joinStartPoints || (joinEndPoints > 0 && k == endJunction))
            {
                continue;
            }

            var turn = TurnAngleDegrees(line[k - 1], line[k], line[k + 1]);
            Assert.True(turn < limit,
                $"{where}: the direction turns {turn:F1}° between segments at point {k}, expected under {limit}°");
        }
    }

    /// <summary>
    /// Scans the flank of the scripted plain–forest edge for one sample at which the named own class
    /// wins and the neighbour class still blends in — the Done-when's two samples on either side.
    /// Returns the first found, or null.
    /// </summary>
    private static SplatPoint? FindBlendSample(
        int[] cells, int width, int height, TerrainSurfaceClass own, TerrainSurfaceClass neighbour)
    {
        var edge = 32f;
        var left = own == TerrainSurfaceClass.Plain;
        for (var y = 0.3f; y <= height - 0.3f; y += 0.025f)
        {
            for (var d = 0.05f; d <= 0.2f; d += 0.01f)
            {
                var x = left ? edge - d : edge + d;
                var weights = TerrainSplatMap.WeightsAt(cells, width, height, x, y);
                var ownWeight = WeightFor(own, weights);
                var neighbourWeight = WeightFor(neighbour, weights);
                if (ownWeight > neighbourWeight && neighbourWeight > 0f)
                {
                    return new SplatPoint(x, y);
                }
            }
        }

        return null;
    }

    private static void AssertWithinThree(float expectedWeight, byte actual, string because)
    {
        var expected = Math.Clamp((int)Math.Round(expectedWeight * 255f), 0, 255);
        Assert.InRange(actual, expected - 3, expected + 3);
        _ = because;
    }

    private static void AssertClose(SplatPoint expected, SplatPoint actual, string because) =>
        Assert.True(
            MathF.Abs(expected.X - actual.X) <= 0.01f && MathF.Abs(expected.Y - actual.Y) <= 0.01f,
            $"{because}: expected {expected}, got {actual}");

    private static void AssertOwnClassWins(TerrainSurfaceClass own, SurfaceWeights weights, string where)
    {
        var ownWeight = WeightFor(own, weights);
        for (var other = 0; other < 6; other++)
        {
            if ((TerrainSurfaceClass)other == own)
            {
                continue;
            }

            var otherWeight = WeightFor((TerrainSurfaceClass)other, weights);
            Assert.True(ownWeight > otherWeight,
                $"{where}: own class {own} weight {ownWeight} must beat {((TerrainSurfaceClass)other)} weight {otherWeight}");
        }
    }

    private static float WeightFor(TerrainSurfaceClass cls, SurfaceWeights weights) => cls switch
    {
        TerrainSurfaceClass.Plain => weights.Plain,
        TerrainSurfaceClass.Desert => weights.Desert,
        TerrainSurfaceClass.Forest => weights.Forest,
        TerrainSurfaceClass.Mountain => weights.Mountain,
        TerrainSurfaceClass.Shallow => weights.Shallow,
        _ => weights.Deep,
    };

    private static void ReadSample(TerrainSplatMap map, int i, int j, out SampleBytes sample)
    {
        var offset = ((j * map.LatticeWidth) + i) * 4;
        sample = new SampleBytes(
            map.SplatA[offset],
            map.SplatA[offset + 1],
            map.SplatA[offset + 2],
            map.SplatA[offset + 3],
            map.SplatB[offset],
            map.SplatB[offset + 1]);
    }

    private static double Distance(SplatPoint a, SplatPoint b)
    {
        var dx = (double)a.X - b.X;
        var dy = (double)a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>The distance from a point to the nearest point of a polyline (segment projections).</summary>
    private static double MinDistanceToPolyline(IReadOnlyList<SplatPoint> polyline, SplatPoint point)
    {
        if (polyline.Count == 1)
        {
            return Distance(polyline[0], point);
        }

        var best = double.MaxValue;
        for (var i = 1; i < polyline.Count; i++)
        {
            var a = polyline[i - 1];
            var b = polyline[i];
            var abx = (double)b.X - a.X;
            var aby = (double)b.Y - a.Y;
            var lengthSquared = (abx * abx) + (aby * aby);
            var t = lengthSquared <= 0.0
                ? 0d
                : Math.Clamp((((point.X - a.X) * abx) + ((point.Y - a.Y) * aby)) / lengthSquared, 0d, 1d);
            var px = a.X + (abx * t);
            var py = a.Y + (aby * t);
            var ddx = (double)point.X - px;
            var ddy = (double)point.Y - py;
            best = Math.Min(best, Math.Sqrt((ddx * ddx) + (ddy * ddy)));
            if (best <= 0.0)
            {
                break;
            }
        }

        return best;
    }

    /// <summary>The angle, in degrees, between the segments a→b and b→c.</summary>
    private static double TurnAngleDegrees(SplatPoint a, SplatPoint b, SplatPoint c)
    {
        var v1x = (double)b.X - a.X;
        var v1y = (double)b.Y - a.Y;
        var v2x = (double)c.X - b.X;
        var v2y = (double)c.Y - b.Y;
        var v1 = Math.Sqrt((v1x * v1x) + (v1y * v1y));
        var v2 = Math.Sqrt((v2x * v2x) + (v2y * v2y));
        if (v1 <= 0.0 || v2 <= 0.0)
        {
            return 0.0;
        }

        var cosine = Math.Clamp(((v1x * v2x) + (v1y * v2y)) / (v1 * v2), -1.0, 1.0);
        return Math.Acos(cosine) * (180.0 / Math.PI);
    }

    private readonly record struct SampleBytes(
        byte Plain, byte Desert, byte Forest, byte Mountain, byte Shallow, byte Deep);
}
