using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Movement;

/// <summary>
/// The pieces every Movement test needs: the real shipped toy <see cref="Model.World"/> and
/// <see cref="Model.Ruleset"/> (reused from <see cref="CoreTestbed"/>, exactly as
/// <c>tests/IC2.Engine.Tests/Strength/StrengthTestbed.cs</c> does), plus a decoded-terrain tile-type
/// lookup so tests can drive <see cref="MovementWalker.Walk"/> against the committed toy grid without
/// each re-implementing the decode.
/// </summary>
public static class MovementTestbed
{
    /// <summary>The shipped toy world: an 8×6 grid, three cities, two nations.</summary>
    public static World World => CoreTestbed.Toy.World;

    /// <summary>The shipped toy ruleset. Every terrain cost a Movement test needs comes from here.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    private static readonly Lazy<int[]> LazyCells = new(() => World.Terrain.Decode(World.Width, World.Height));

    /// <summary>
    /// Resolves a cell to its tile type id in <see cref="World"/>, or <see langword="null"/> when the
    /// cell is off the grid or its code names no tile type — the two cases <see cref="MovementWalker"/>
    /// expects a caller's lookup to report as "unmapped."
    /// </summary>
    public static string? TileTypeIdAt(GridPoint point)
    {
        if (point.X < 0 || point.X >= World.Width || point.Y < 0 || point.Y >= World.Height)
        {
            return null;
        }

        var code = LazyCells.Value[(point.Y * World.Width) + point.X];
        return World.TileTypeByCode(code)?.Id;
    }

    /// <summary>Finds a shipped toy city's position by id. Throws if the id is unknown.</summary>
    public static GridPoint CityPosition(string cityId)
    {
        var city = World.CityById(cityId)
            ?? throw new InvalidOperationException($"The toy world has no city '{cityId}'.");
        return new GridPoint(city.X, city.Y);
    }
}
