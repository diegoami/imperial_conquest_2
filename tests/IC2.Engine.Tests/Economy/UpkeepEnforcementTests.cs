using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses", Done-when 7: "An army whose
/// upkeep cannot be paid loses troops (a real consequence, not a debt counter)."
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompiled-quarterly-billing-and-economy.md]</strong>: "army units lose troops,
/// reduced by troops/100, when available upkeep funds drop below 1"
/// (<c>tests/fixtures/corpus.json</c> <c>economy.unpaidUpkeepConsequence</c>).
/// </remarks>
public sealed class UpkeepEnforcementTests
{
    [Fact]
    public void NationCanPayUpkeep_FundsDropBelowOne_IsFalse()
    {
        Assert.False(UpkeepEnforcement.NationCanPayUpkeep(treasuryBeforeUpkeep: 100, totalUpkeep: 100)); // -> 0, below 1.
        Assert.False(UpkeepEnforcement.NationCanPayUpkeep(treasuryBeforeUpkeep: 50, totalUpkeep: 100)); // -> negative.
    }

    [Fact]
    public void NationCanPayUpkeep_FundsAtLeastOne_IsTrue()
    {
        Assert.True(UpkeepEnforcement.NationCanPayUpkeep(treasuryBeforeUpkeep: 101, totalUpkeep: 100)); // -> 1, the threshold itself.
        Assert.True(UpkeepEnforcement.NationCanPayUpkeep(treasuryBeforeUpkeep: 500, totalUpkeep: 100));
    }

    /// <summary>
    /// The real consequence itself: troops actually decrease, by exactly <c>troops / 100</c> — not a debt
    /// counter that leaves the roster untouched.
    /// </summary>
    [Fact]
    public void ApplyMutiny_ReducesTroopsByTroopsOverOneHundred()
    {
        var unit = new UnitSlot(0, "light_infantry", 10_000, 6, "Mutinous Battalion");

        var mutinied = UpkeepEnforcement.ApplyMutiny(unit, EconomyTestbed.Ruleset);

        Assert.Equal(9_900, mutinied.Troops); // 10,000 - 10,000/100 = 9,900.
        Assert.True(mutinied.Troops < unit.Troops); // the actual assertion Done-when 7 cares about.
    }

    [Fact]
    public void ApplyMutiny_NeverGoesNegative()
    {
        var unit = new UnitSlot(0, "light_infantry", 50, 6, "Tiny Battalion");
        var mutinied = UpkeepEnforcement.ApplyMutiny(unit, EconomyTestbed.Ruleset);
        Assert.Equal(50, mutinied.Troops); // 50/100 = 0 loss (integer division), stays 50, never negative.
    }

    [Fact]
    public void ApplyMutinyToArmy_AppliesToEveryUnit()
    {
        var army = new ArmyState(
            "a1", "north", 0, 0, 9, 60, 0, 0, null, null,
            ValueList.Of(
                new UnitSlot(0, "light_infantry", 10_000, 6, "First"),
                new UnitSlot(0, "heavy_infantry", 5_000, 6, "Second")));

        var mutinied = UpkeepEnforcement.ApplyMutinyToArmy(army, EconomyTestbed.Ruleset);

        Assert.Equal(9_900, mutinied.Units[0].Troops);
        Assert.Equal(4_950, mutinied.Units[1].Troops);
    }

    [Fact]
    public void ApplyMutiny_UsesTheRulesetsDivisorNotALiteral()
    {
        var unit = new UnitSlot(0, "light_infantry", 10_000, 6, "Battalion");
        var coarser = EconomyTestbed.Ruleset with
        {
            Economy = EconomyTestbed.Ruleset.Economy with { UnpaidUpkeepTroopLossDivisor = 10 },
        };

        var mutinied = UpkeepEnforcement.ApplyMutiny(unit, coarser);

        Assert.Equal(9_000, mutinied.Troops); // 10,000 - 10,000/10 = 9,000, not the shipped 9,900.
    }
}
