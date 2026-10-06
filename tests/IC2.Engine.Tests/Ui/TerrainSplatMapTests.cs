using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T148.md</c> Done-when 1: <see cref="TerrainSplatMap"/> turns the world's terrain grid
/// into the six per-pixel surface weights the shader samples, bakes them at 4 × 4 samples per cell, and
/// gives each river cell a stroke whose ends meet its neighbours' on the shared edge. Godot-free, so it
/// is exercised here directly rather than through the headless map check.
/// </summary>
public sealed class TerrainSplatMapTests
{
    private static readonly GameDataRepository Repository =
        GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>The classical world is 320 × 140 = 44,800 cells, 1,280 × 560 bake samples.</summary>
    private const int ClassicalWidth = 320;
    private const int ClassicalHeight = 140;
    private const int ClassicalCells = ClassicalWidth * ClassicalHeight;

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
    /// quarter-cell. Recomputing <see cref="TerrainSplatMap.WeightsAt(float,float)"/> at the quarter-cell
    /// centres of the classical world's cell (200, 70) and comparing each named channel is the proof the
    /// bake's layout and positions are what the shader assumes.
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
            var plain = map.SplatA[offset];
            var desert = map.SplatA[offset + 1];
            var forest = map.SplatA[offset + 2];
            var mountain = map.SplatA[offset + 3];
            var shallow = map.SplatB[offset];
            var deep = map.SplatB[offset + 1];

            var sum = plain + desert + forest + mountain + shallow + deep;
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
    /// Done-when 1: two samples on either side of a plain–forest edge blend both classes, each side's own
    /// class winning. A three-cell scripted grid isolates the edge.
    /// </summary>
    [Fact]
    public void Two_samples_across_a_plain_forest_edge_blend_with_each_side_winning()
    {
        // plain (2), forest (4), plain (2): the plain–forest edge sits on x = 1.
        var map = TerrainSplatMap.Bake(3, 1, new[] { 2, 4, 2 });

        var left = map.WeightsAt(0.75f, 0.5f);
        Assert.True(left.Plain > 0f, "the plain side of the edge carries plain weight");
        Assert.True(left.Forest > 0f, "the plain side of the edge still blends forest across it");
        Assert.True(left.Plain > left.Forest, $"plain wins on the plain side ({left.Plain} vs {left.Forest})");

        var right = map.WeightsAt(1.25f, 0.5f);
        Assert.True(right.Plain > 0f, "the forest side of the edge still blends plain across it");
        Assert.True(right.Forest > 0f, "the forest side of the edge carries forest weight");
        Assert.True(right.Forest > right.Plain, $"forest wins on the forest side ({right.Forest} vs {right.Plain})");
    }

    /// <summary>
    /// Done-when 1: a sea cell within two cells of land is shallow, one more than two cells away is deep,
    /// and a river cell (6–11) counts as plain.
    /// </summary>
    [Fact]
    public void Sea_is_shallow_within_two_cells_of_land_and_rivers_are_plain()
    {
        // One row: deep sea at x=0, sea at x=1..5, land at x=3? Land must be the only land. Build a
        // 7-wide row with a single land cell at x=3 so the distance is easy to read.
        var row = new[] { 0, 1, 0, 2, 0, 1, 0 };
        const int width = 7;
        const int height = 1;

        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 2, 0));
        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 1, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(row, width, height, 0, 0));
        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 4, 0));
        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 5, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(row, width, height, 6, 0));

        // A code-1 sea cell is classified by proximity too, not by its code.
        Assert.Equal(TerrainSurfaceClass.Shallow, TerrainSplatMap.ClassOf(row, width, height, 1, 0));
        Assert.Equal(TerrainSurfaceClass.Deep, TerrainSplatMap.ClassOf(new[] { 1, 1 }, 2, 1, 0, 0));

        // Every river connectivity code is plain for the surface.
        for (var code = 6; code <= 11; code++)
        {
            Assert.Equal(TerrainSurfaceClass.Plain,
                TerrainSplatMap.ClassOf(new[] { code }, 1, 1, 0, 0));
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

    /// <summary>Done-when 1: the displacement never exceeds 0.3 cell on either axis or in magnitude.</summary>
    [Fact]
    public void The_displacement_never_exceeds_a_third_of_a_cell()
    {
        for (var y = -1f; y <= ClassicalHeight + 1f; y += 0.137f)
        {
            for (var x = -1f; x <= ClassicalWidth + 1f; x += 0.113f)
            {
                var displacement = TerrainSplatMap.NoiseDisplacementAt(x, y);
                Assert.True(MathF.Abs(displacement.X) <= 0.3f, $"x displacement {displacement.X} at ({x}, {y})");
                Assert.True(MathF.Abs(displacement.Y) <= 0.3f, $"y displacement {displacement.Y} at ({x}, {y})");
                var magnitude = MathF.Sqrt((displacement.X * displacement.X) + (displacement.Y * displacement.Y));
                Assert.True(magnitude <= 0.3f, $"displacement magnitude {magnitude} at ({x}, {y})");
            }
        }
    }

    /// <summary>
    /// Done-when 1, river strokes: a chain of cells with codes 8, 6 and 7 has strokes whose ends coincide
    /// at the shared edge midpoints, within 0.01 cell.
    /// </summary>
    [Fact]
    public void Neighbouring_river_strokes_meet_at_the_shared_edge_midpoints()
    {
        // 2x3 grid. (0,1)=8 (E,N), (1,1)=6 (W,E), (0,0)=7 (N,S).
        // 8's E midpoint (1, 1.5) is 6's W midpoint; 8's N midpoint (0.5, 1) is 7's S midpoint.
        var cells = new[]
        {
            /* y=0 */ 7, 2,
            /* y=1 */ 8, 6,
            /* y=2 */ 2, 2,
        };

        var strokes = TerrainSplatMap.RiverStrokes(cells, 2, 3);
        Assert.Equal(3, strokes.Count);

        // Code 8 at (0,1): E to N, start (1, 1.5), end (0.5, 1).
        // Code 6 at (1,1): W to E, start (1, 1.5), end (2, 1.5).
        // Code 7 at (0,0): N to S, start (0.5, 0), end (0.5, 1).
        var eight = strokes.Single(s => s.Control == new SplatPoint(0.5f, 1.5f));
        var six = strokes.Single(s => s.Control == new SplatPoint(1.5f, 1.5f));
        var seven = strokes.Single(s => s.Control == new SplatPoint(0.5f, 0.5f));

        AssertClose(new SplatPoint(1f, 1.5f), eight.Start, "8's east end");
        AssertClose(new SplatPoint(1f, 1.5f), six.Start, "6's west end meets 8's east end");
        AssertClose(new SplatPoint(0.5f, 1f), eight.End, "8's north end");
        AssertClose(new SplatPoint(0.5f, 1f), seven.End, "7's south end meets 8's north end");

        // Each stroke's control point is its own cell centre, so the river curves through the cell.
        Assert.Equal(new SplatPoint(0.5f, 1.5f), eight.Control);
        Assert.Equal(new SplatPoint(1.5f, 1.5f), six.Control);
        Assert.Equal(new SplatPoint(0.5f, 0.5f), seven.Control);
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

    private readonly record struct SampleBytes(
        byte Plain, byte Desert, byte Forest, byte Mountain, byte Shallow, byte Deep);
}
