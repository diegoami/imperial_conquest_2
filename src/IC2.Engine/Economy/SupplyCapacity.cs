using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Supply capacity and the panel's percentage readout — <c>docs/task-catalogue.md</c>
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

    /// <summary>
    /// An army's supply-<em>dialog</em> capacity, in tons — <see cref="ArmyCapacityTons"/> plus the
    /// dialog's <c>+1</c> allowance (review round 3, R1's resolution; <c>supply-capacity-rounding.md</c>
    /// [confirmed]). <c>TAFSupply_ChangeSupply</c> (own-city, free) and <c>TAFSupply_ChangeBuyAmount</c>
    /// (foreign, paid) both cap a dialog transfer here, identically on both paths — an <c>IDIV</c> by
    /// <see cref="EconomyRules.ArmySupplyTonsPerTroops"/> followed by an unconditional <c>INC</c>, not a
    /// rounding of any kind. Every other writer (automatic resupply, army-to-army rebalancing, battle
    /// absorption) uses <see cref="ArmyCapacityTons"/> alone, with no bonus — <see cref="SupplyPurchase"/>
    /// is the only caller of this method.
    /// </summary>
    /// <param name="ruleset">
    /// Supplies <see cref="EconomyRules.ArmySupplyTonsPerTroops"/> and
    /// <see cref="EconomyRules.SupplyDialogArmyCapacityBonus"/> — never a C# literal.
    /// </param>
    public static int ArmyDialogCapacityTons(int troops, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ArmyCapacityTons(troops, ruleset) + ruleset.Economy.SupplyDialogArmyCapacityBonus;
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
