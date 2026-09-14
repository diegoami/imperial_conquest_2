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
}
