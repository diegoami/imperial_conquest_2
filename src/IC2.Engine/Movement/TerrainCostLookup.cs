using IC2.Engine.Model;

namespace IC2.Engine.Movement;

/// <summary>
/// The move cost a ruleset charges for one tile type, and whether that came from the ruleset's own
/// table or from its fallback.
/// </summary>
/// <param name="MoveCost">The cost to charge.</param>
/// <param name="WasPricedByRuleset">
/// <see langword="false"/> when <paramref name="MoveCost"/> is <see cref="TerrainRules.DefaultMoveCost"/>
/// because the ruleset's table carries no entry for the tile type asked about — the "arbitrary custom
/// maps" guarantee (<c>docs/game-design.md</c> §Movement, <c>docs/design-audit.md</c> §2.3).
/// </param>
public readonly record struct TerrainCostLookupResult(int MoveCost, bool WasPricedByRuleset);

/// <summary>
/// Looks up a tile type's move cost in a <see cref="Ruleset"/>'s terrain table.
/// </summary>
/// <remarks>
/// <see cref="Ruleset.MoveCostFor"/> already exists and returns the same number, but only the number —
/// it cannot tell a caller whether the value came from the table or from the fallback. This task's Done
/// When 5 needs that distinction (a fallback fires a warning event), so this re-reads
/// <see cref="Ruleset.Terrain"/> directly rather than widening <see cref="Ruleset"/> itself, which is
/// T02's file and outside this task's Owns list.
/// </remarks>
public static class TerrainCostLookup
{
    /// <summary>Looks up the move cost for one tile type.</summary>
    /// <param name="terrain">The ruleset's terrain rules.</param>
    /// <param name="tileTypeId">The tile type id to price, as <see cref="TileType.Id"/> names it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="terrain"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tileTypeId"/> is null or whitespace.</exception>
    public static TerrainCostLookupResult MoveCostFor(TerrainRules terrain, string tileTypeId)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentException.ThrowIfNullOrWhiteSpace(tileTypeId);

        foreach (var entry in terrain.MoveCosts)
        {
            if (string.Equals(entry.TileTypeId, tileTypeId, StringComparison.Ordinal))
            {
                return new TerrainCostLookupResult(entry.MoveCost, WasPricedByRuleset: true);
            }
        }

        return new TerrainCostLookupResult(terrain.DefaultMoveCost, WasPricedByRuleset: false);
    }
}
