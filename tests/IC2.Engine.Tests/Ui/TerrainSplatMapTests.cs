using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T148.md</c> (as amended by plan PRs #807, #808 and #811) Done-when 1:
/// <see cref="TerrainSplatMap"/> turns the world's terrain grid into the six per-pixel surface weights
/// the shader samples — a 4 × 4-per-cell bake whose channels are named, the cell's own class winning at
/// its centre and in the mean of its four inner samples, a smoothed-then-sharpened border that turns a
/// one-cell staircase into a straight line, shallow sea within one cell of land, bounded deterministic
/// field noise — and gives each river chain one smooth, corner-cut polyline sampled every 0.1 cell of
/// arc length, confluences visibly joining. Godot-free, so it is exercised here directly rather than
/// through the headless map check.
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
    /// the mean of the cell's four inner baked samples (which is what the linear-filtered shader reads at
    /// the centre). A port therefore sits on its own terrain and a coastline never crosses a centre.
    /// </summary>
    [Fact]
    public void The_cells_own_class_wins_at_its_centre_in_the_function_and_in_the_inner_samples()
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
                AssertOwnClassWins(own, atCentre, $"function at the centre of cell ({x}, {y})");

                // The four inner samples are quarter indices 1 and 2 in each axis: at cell centres the
                // linear-filtered shader reads exactly their mean.
                var mean = new float[6];
                for (var j = 1; j <= 2; j++)
                {
                    for (var i = 1; i <= 2; i++)
                    {
                        var sample = WeightsAtCellQuarter(map, x, y, i, j);
                        mean[0] += sample.Plain;
                        mean[1] += sample.Desert;
                        mean[2] += sample.Forest;
                        mean[3] += sample.Mountain;
                        mean[4] += sample.Shallow;
                        mean[5] += sample.Deep;
                    }
                }

                var meanWeights = new SurfaceWeights(
                    mean[0] / 4f, mean[1] / 4f, mean[2] / 4f, mean[3] / 4f, mean[4] / 4f, mean[5] / 4f);
                AssertOwnClassWins(own, meanWeights, $"mean of the four inner samples of cell ({x}, {y})");
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
    /// ±<see cref="TerrainSplatMap.FieldNoiseBoundCells"/>, and two calls return the identical value —
    /// a pure function of position and a constant seed, never a random draw.
    /// </summary>
    [Fact]
    public void The_field_noise_is_bounded_and_identical_on_two_calls()
    {
        foreach (var (x, y) in FixedSamplePoints(1_000))
        {
            foreach (var cls in Enum.GetValues<TerrainSurfaceClass>())
            {
                var noise = TerrainSplatMap.ClassNoise(cls, x, y);
                Assert.InRange(noise, -TerrainSplatMap.FieldNoiseBoundCells, TerrainSplatMap.FieldNoiseBoundCells);
                Assert.Equal(noise, TerrainSplatMap.ClassNoise(cls, x, y));
            }
        }
    }

    /// <summary>
    /// Done-when 1, no stair-steps: on an 80 × 80 grid whose cell (x, y) is plain when x + y &lt; 40 and
    /// sea otherwise (a diagonal one-cell staircase), the plain weight crosses 0.5 exactly once on each
    /// of the 30 lines through (5 + k, 35 − k) in the direction (1, 1)/√2, and the 30 crossings lie
    /// within 0.08 cell of their least-squares straight line — an unsmoothed staircase deviates by about
    /// 0.35.
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
        for (var k = 0; k < 30; k++)
        {
            var px = 5f + k;
            var py = 35f - k;
            var t = FindPlainCrossing(cells, width, height, px, py, direction, direction, $"the diagonal line {k}");
            crossings.Add((px + (t * direction), py + (t * direction)));
        }

        AssertLineDeviation(crossings, 0.08f, "the diagonal staircase's 0.5 crossings");
    }

    /// <summary>
    /// Done-when 1, no stair-steps: the same test on an 80 × 80 grid with plain for y &lt; 40 and sea for
    /// y ≥ 40, with vertical lines through (25 + k, 40), gives crossings within 0.08 cell of their
    /// least-squares line.
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
        for (var k = 0; k < 30; k++)
        {
            var px = 25f + k;
            var t = FindPlainCrossing(cells, width, height, px, 40f, 0f, 1f, $"the vertical line {k}");
            crossings.Add((px, 40f + t));
        }

        AssertLineDeviation(crossings, 0.08f, "the straight staircase's 0.5 crossings");
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
    /// (4,2)=6 is exactly one chain; its polyline passes within 0.25 cell of each of the seven centres,
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
            Assert.True(distance <= 0.25f, $"the polyline stays within 0.25 of the centre ({centre.X}, {centre.Y}) (got {distance:F3})");
        }

        Assert.True(line.Count > 10, $"the polyline of a seven-cell river has {line.Count} points");
        for (var k = 0; k < line.Count - 1; k++)
        {
            var gap = Distance(line[k], line[k + 1]);
            Assert.True(gap <= 0.11f, $"consecutive points {k} are {gap:F4} apart, over 0.11");
            if (k < line.Count - 2)
            {
                Assert.True(gap >= TerrainSplatMap.RiverPolylineStepCells - 0.01f,
                    $"consecutive points {k} are {gap:F4} apart, under the 0.1 ± 0.01 sample spacing");
            }
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
            Assert.True(distance <= 0.25f, $"the bend-ending polyline stays within 0.25 of ({centre.X}, {centre.Y}) (got {distance:F3})");
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
    /// Done-when 1: on the classical world every river cell lies within 0.25 cell of some chain's
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
                    if (best <= 0.25)
                    {
                        break;
                    }
                }

                Assert.True(best <= 0.25, $"river cell ({x}, {y}) code {code} is {best:F3} from every chain");
            }
        }
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

    private static SurfaceWeights WeightsAtCellQuarter(TerrainSplatMap map, int x, int y, int i, int j)
    {
        ReadSample(map, (x * 4) + i, (y * 4) + j, out var sample);
        return new SurfaceWeights(
            sample.Plain / 255f,
            sample.Desert / 255f,
            sample.Forest / 255f,
            sample.Mountain / 255f,
            sample.Shallow / 255f,
            sample.Deep / 255f);
    }

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
