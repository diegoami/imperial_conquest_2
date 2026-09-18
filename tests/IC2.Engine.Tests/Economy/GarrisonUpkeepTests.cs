using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition
/// for debt", Done-when 5 (T08 follow-up #76 N3): every recruitment slot with troops is charged at the
/// regular rate, "not ready" ones included.
/// </summary>
public sealed class GarrisonUpkeepTests
{
    [Fact]
    public void Compute_ChargesEverySlotWithTroops_AtTheRegularRate()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var slots = new[]
        {
            new RecruitmentSlot("arx", "light_infantry", 15_000, StateCode: 0), // barely started; not ready.
            new RecruitmentSlot("arx", "heavy_infantry", 6_000, StateCode: 24), // fully ready.
        };

        // Same worked values as the army-side regular formula: 15,000 LI -> 75, 6,000 HI -> 60.
        Assert.Equal(75 + 60, GarrisonUpkeep.Compute(slots, ruleset));
    }

    [Fact]
    public void Compute_ChargesANotReadySlot_ExactlyLikeAReadyOne()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var notReady = new[] { new RecruitmentSlot("arx", "light_infantry", 15_000, StateCode: 0) };
        var ready = new[] { new RecruitmentSlot("arx", "light_infantry", 15_000, StateCode: 24) };

        Assert.Equal(GarrisonUpkeep.Compute(ready, ruleset), GarrisonUpkeep.Compute(notReady, ruleset));
    }

    [Fact]
    public void Compute_SkipsAnEmptySlot()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var slots = new[] { new RecruitmentSlot("arx", "light_infantry", 0, StateCode: 0) };

        Assert.Equal(0, GarrisonUpkeep.Compute(slots, ruleset));
    }

    [Fact]
    public void Compute_EmptyRoster_IsZero() =>
        Assert.Equal(0, GarrisonUpkeep.Compute(Array.Empty<RecruitmentSlot>(), EconomyTestbed.Ruleset));

    [Fact]
    public void Compute_UnknownUnitType_Throws()
    {
        var slots = new[] { new RecruitmentSlot("arx", "not_a_real_type", 100, StateCode: 0) };
        Assert.Throws<ArgumentException>(() => GarrisonUpkeep.Compute(slots, EconomyTestbed.Ruleset));
    }
}
