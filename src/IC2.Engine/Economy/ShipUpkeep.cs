using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly fleet upkeep — <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses",
/// Done-when 2.
/// </summary>
/// <remarks>
/// <c>upkeep = ships × 3</c> per quarter, charged against the national treasury for every deployed fleet
/// <strong>[confirmed: fleet-order-at-caere.md, decompiled-fleet-tax-and-mercenary-formulas.md,
/// decompiled-quarterly-billing-and-economy.md]</strong> — the last of those three confirms this is the
/// ongoing charge, not just a build-order dialog preview.
/// </remarks>
public static class ShipUpkeep
{
    /// <summary>Computes one quarter's upkeep for a fleet of <paramref name="ships"/> ships.</summary>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.ShipUpkeepPerQuarter"/> — never a C# literal.</param>
    public static int Compute(int ships, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ships * ruleset.Economy.ShipUpkeepPerQuarter;
    }

    /// <summary>
    /// Sums <see cref="Compute"/> over every launched fleet a nation owns — the exact term
    /// <see cref="QuarterlyEconomySystem"/> charges that nation's treasury, extracted here so the
    /// balance-sheet projection (<c>docs/tasks/T104.md</c>) shares the aggregation rather than restating
    /// which fleets are billed.
    /// </summary>
    /// <param name="state">Supplies the fleets (a fleet still under construction is never billed).</param>
    /// <param name="nationId">The owning nation to sum for.</param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.ShipUpkeepPerQuarter"/> — never a C# literal.</param>
    public static int ComputeForNation(GameState state, string nationId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nationId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var total = 0;
        foreach (var fleet in state.Fleets)
        {
            if (fleet.IsUnderConstruction || !string.Equals(fleet.Nation, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            total += Compute(fleet.Ships, ruleset);
        }

        return total;
    }
}
