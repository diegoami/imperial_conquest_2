using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Supply capacity and the panel's percentage readout — <c>docs/build-orchestration-plan.md</c>
/// "T08 Economy, supply, and purses", Done-when 4.
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> throughout:
/// <c>TAFSupply_ChangeBuyAmount</c> for the two capacity formulas, <c>TInformation_ShowArmyDetails</c> for
/// the percentage. The percentage formula reproduces every percentage reading on record exactly — the
/// published Roman 13-unit army (482/48,173 → 100%) and the three Galatia frames (204/998 → 20%,
/// 344/998 → 34%, 184/282 → 65%) — all in <c>tests/fixtures/corpus.json</c>.
/// </remarks>
public static class SupplyCapacity
{
    /// <summary>An army's supply capacity, in tons.</summary>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.ArmySupplyTonsPerTroops"/> — never a C# literal.</param>
    public static int ArmyCapacityTons(int troops, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return troops / ruleset.Economy.ArmySupplyTonsPerTroops;
    }

    /// <summary>A fleet's supply capacity, in tons.</summary>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.FleetSupplyTonsPerShip"/> — never a C# literal.</param>
    public static int FleetCapacityTons(int ships, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ships * ruleset.Economy.FleetSupplyTonsPerShip;
    }

    /// <summary>
    /// The supply panel's percentage readout: <c>supplyTons × 10000 / troops</c>, truncated. Shared by
    /// armies and fleets alike — the formula reads a troop count either way, and a fleet's own "troops"
    /// for this purpose is its ship count in the original's panel, so callers pass whichever count the
    /// panel they are reproducing uses.
    /// </summary>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.SupplyPercentNumerator"/> — never a C# literal.</param>
    /// <exception cref="DivideByZeroException"><paramref name="troops"/> is zero.</exception>
    public static int PercentFull(int supplyTons, int troops, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return supplyTons * ruleset.Economy.SupplyPercentNumerator / troops;
    }
}
