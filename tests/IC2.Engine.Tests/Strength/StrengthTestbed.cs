using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// The pieces every Strength test needs: the real shipped toy <see cref="Ruleset"/> (reused from
/// <see cref="CoreTestbed"/>, not re-loaded, so this task's tests agree with every other task's on the
/// same ruleset instance) and small helpers for building unit rosters without repeating
/// <see cref="UnitSlot"/>'s five-argument constructor everywhere.
/// </summary>
public static class StrengthTestbed
{
    /// <summary>The shipped toy ruleset. Every constant a Strength test needs comes from here.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>
    /// The unit type id the shipped rulesets use for archers — matching <c>type == 2</c> in the
    /// original (<c>decompiled-city-capture-resolution.md</c>: "type check <c>== 2</c>, i.e. archers").
    /// </summary>
    public const string ArcherUnitTypeId = "archers";

    /// <summary>
    /// The shipped ruleset's one city order (<c>docs/design-audit.md</c> §3 Q7), needed by
    /// <see cref="SiegeStrength.Defender"/> to decode a fortification word via
    /// <see cref="FortificationCode.FinishedPercent"/>.
    /// </summary>
    public static CityOrderRule FortifyOrder =>
        Ruleset.CityOrders.Orders.FindById(o => o.Id, "fortify")
            ?? throw new InvalidOperationException("The shipped toy ruleset has no 'fortify' city order.");

    /// <summary>Builds a regular (non-mercenary) unit slot with a throwaway name.</summary>
    public static UnitSlot Unit(string unitTypeId, int troops, int quality = 6) =>
        new(MercenaryLabel: 0, UnitTypeId: unitTypeId, Troops: troops, Quality: quality, Name: "Test Battalion");

    /// <summary>
    /// The published 13-unit Roman roster at (100, 42) in <c>11_supply.sav</c>
    /// (<c>army-records-and-roman-roster.md</c>): 48,173 troops total, matching the panel and the
    /// independently-confirmed 442-talent quarterly upkeep and 482-ton/100% supply figures already in
    /// the T04 corpus (<c>roman13.*</c>). Used here for <c>ArmyPower</c>'s Done-when 1 ("reproduces
    /// hand-computed values for the published 13-unit Roman roster").
    /// </summary>
    public static IReadOnlyList<UnitSlot> Roman13UnitRoster { get; } = new[]
    {
        Unit("light_infantry", 4210),
        Unit("heavy_infantry", 4900),
        Unit("heavy_infantry", 4920),
        Unit("heavy_infantry", 5747),
        Unit("heavy_cavalry", 774),
        Unit("heavy_infantry", 2583),
        Unit("heavy_cavalry", 1539),
        Unit("light_cavalry", 900),
        Unit("heavy_infantry", 4787),
        Unit("heavy_infantry", 3571),
        Unit("heavy_infantry", 5300),
        Unit("heavy_infantry", 3442),
        Unit("light_infantry", 5500),
    };
}
