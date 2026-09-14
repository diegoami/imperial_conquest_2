using IC2.Engine.Movement;
using Xunit;

namespace IC2.Engine.Tests.Movement;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T09 Movement and terrain", Done When 2: "A Bresenham walk over a
/// committed test grid produces a cell sequence byte-equal to a committed expected-path fixture."
/// </summary>
public sealed class BresenhamPathTests
{
    /// <summary>
    /// The committed test grid is the shipped toy world (<c>data/worlds/toy-3city.json</c>, 8×6,
    /// decoded row-major):
    /// <code>
    /// y\x  0  1  2  3  4  5  6  7
    ///  0   0  0  2  2  2  4  5  0
    ///  1   0  2  2  3  2  4  5  0
    ///  2   0  2  6  2  2  2  4  0
    ///  3   0  2  2  2  5  2  2  0
    ///  4   0  0  2  2  2  2  0  0
    ///  5   1  0  0  0  0  0  0  1
    /// </code>
    /// The committed expected-path fixture below is the standard integer Bresenham trace from (1,1) to
    /// (6,3) (<c>dx=5, dy=2</c>), asserted byte-equal against <see cref="BresenhamPath.Trace"/>'s output.
    /// </summary>
    [Fact]
    public void Walk_ToyGridDiagonal_MatchesHandTracedPath()
    {
        var from = new GridPoint(1, 1);
        var to = new GridPoint(6, 3);

        var path = BresenhamPath.Trace(from, to);

        // Hand-traced (and independently cross-checked) sequence for dx=5, dy=2 over the toy grid.
        var expected = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(2, 1),
            new GridPoint(3, 2),
            new GridPoint(4, 2),
            new GridPoint(5, 3),
            new GridPoint(6, 3),
        };

        Assert.Equal(expected, path);

        // Every traced cell actually resolves on the committed grid, so the fixture is exercising real
        // terrain rather than coordinates that happen to be off the map.
        foreach (var cell in path)
        {
            Assert.NotNull(MovementTestbed.TileTypeIdAt(cell));
        }
    }

    /// <summary>A second, purely-horizontal trace over the same committed grid — the simplest case.</summary>
    [Fact]
    public void Walk_ToyGridHorizontal_MatchesHandTracedPath()
    {
        var from = new GridPoint(1, 2);
        var to = new GridPoint(6, 2);

        var path = BresenhamPath.Trace(from, to);

        var expected = new[]
        {
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(3, 2),
            new GridPoint(4, 2),
            new GridPoint(5, 2),
            new GridPoint(6, 2),
        };

        Assert.Equal(expected, path);
    }

    [Fact]
    public void Trace_SamePoint_ReturnsSingleCell()
    {
        var point = new GridPoint(4, 4);

        var path = BresenhamPath.Trace(point, point);

        Assert.Single(path);
        Assert.Equal(point, path[0]);
    }

    /// <summary>The standard Bresenham algorithm is symmetric: reversing the endpoints reverses the path.</summary>
    [Fact]
    public void Trace_ReversedEndpoints_ReversesThePath()
    {
        var from = new GridPoint(1, 1);
        var to = new GridPoint(6, 3);

        var forward = BresenhamPath.Trace(from, to);
        var backward = BresenhamPath.Trace(to, from);

        Assert.Equal(forward, backward.Reverse());
    }

    [Theory]
    [InlineData(0, 0, 4, 0)] // pure horizontal
    [InlineData(0, 0, 0, 4)] // pure vertical
    [InlineData(0, 0, 4, 4)] // pure diagonal
    [InlineData(0, 4, 4, 0)] // pure diagonal, other direction
    public void Trace_AxisAndDiagonalLines_StepExactlyOncePerCell(int x0, int y0, int x1, int y1)
    {
        var path = BresenhamPath.Trace(new GridPoint(x0, y0), new GridPoint(x1, y1));

        var expectedSteps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
        Assert.Equal(expectedSteps, path.Count);
        Assert.Equal(new GridPoint(x0, y0), path[0]);
        Assert.Equal(new GridPoint(x1, y1), path[^1]);
    }
}
