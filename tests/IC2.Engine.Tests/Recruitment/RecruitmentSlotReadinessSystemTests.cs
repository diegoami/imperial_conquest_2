using IC2.Engine.Calendar;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// Scope: "advancing each slot's state code through T06's <see cref="CityUnitStateCode"/>." Exercises the
/// real <see cref="RecruitmentSlotReadinessSystem"/>, registered into <c>TurnPhase.CityTick</c>, run
/// through the real <see cref="Core.TurnCoordinator"/> (the only way to reach an <see cref="Core.IGameSystem"/>
/// at all — <c>SystemContext</c>'s constructor is internal), through the same
/// <see cref="CityUnitStateCode.Advance"/> T06 already ships and this task does not re-implement.
/// </summary>
public sealed class RecruitmentSlotReadinessSystemTests
{
    [Fact]
    public void A_slot_below_the_cap_steps_by_the_ruleset_increment()
    {
        var coordinator = RecruitmentTestbed.CoordinatorOnly(null, typeof(RecruitmentSlotReadinessSystem));
        var calendar = RecruitmentTestbed.Ruleset.Calendar;
        var initial = RecruitmentTestbed.InitialState();
        var nation = initial.NationById("north")!;
        var slot = new RecruitmentSlot(TargetCityId: "arx", UnitTypeId: "light_cavalry", Troops: 1400, StateCode: 10);
        var before = RecruitmentTestbed.WithNation(initial, nation with { RecruitmentSlots = ValueList.Of(slot) });

        var after = coordinator.RunRoundTick(before).State;

        var updatedSlot = Assert.Single(after.NationById("north")!.RecruitmentSlots);
        Assert.Equal(CityUnitStateCode.Advance(10, calendar), updatedSlot.StateCode);
        Assert.Equal(10 + calendar.CityUnitStateCodeStep, updatedSlot.StateCode);
    }

    [Fact]
    public void A_slot_at_the_cap_holds_there()
    {
        var coordinator = RecruitmentTestbed.CoordinatorOnly(null, typeof(RecruitmentSlotReadinessSystem));
        var calendar = RecruitmentTestbed.Ruleset.Calendar;
        var initial = RecruitmentTestbed.InitialState();
        var nation = initial.NationById("north")!;
        var slot = new RecruitmentSlot(TargetCityId: "arx", UnitTypeId: "light_cavalry", Troops: 1400, StateCode: calendar.CityUnitStateCodeCap);
        var before = RecruitmentTestbed.WithNation(initial, nation with { RecruitmentSlots = ValueList.Of(slot) });

        var after = coordinator.RunRoundTick(before).State;

        var updatedSlot = Assert.Single(after.NationById("north")!.RecruitmentSlots);
        Assert.Equal(calendar.CityUnitStateCodeCap, updatedSlot.StateCode);
    }

    [Fact]
    public void Every_nations_slots_advance_independently_and_other_state_is_untouched()
    {
        var coordinator = RecruitmentTestbed.CoordinatorOnly(null, typeof(RecruitmentSlotReadinessSystem));
        var calendar = RecruitmentTestbed.Ruleset.Calendar;
        var initial = RecruitmentTestbed.InitialState();
        var north = initial.NationById("north")!;
        var south = initial.NationById("south")!;
        var northSlot = new RecruitmentSlot("arx", "light_infantry", 15000, StateCode: 0);
        var southSlot = new RecruitmentSlot("meridia", "heavy_infantry", 6000, StateCode: 20);

        var before = RecruitmentTestbed.WithNation(
            RecruitmentTestbed.WithNation(initial, north with { RecruitmentSlots = ValueList.Of(northSlot) }),
            south with { RecruitmentSlots = ValueList.Of(southSlot) });

        var after = coordinator.RunRoundTick(before).State;

        Assert.Equal(CityUnitStateCode.Advance(0, calendar), Assert.Single(after.NationById("north")!.RecruitmentSlots).StateCode);
        Assert.Equal(CityUnitStateCode.Advance(20, calendar), Assert.Single(after.NationById("south")!.RecruitmentSlots).StateCode);

        // Nothing else in the state moved -- this run is narrowed to RecruitmentSlotReadinessSystem alone.
        Assert.Equal(before.Cities, after.Cities);
        Assert.Equal(before.Armies, after.Armies);
        Assert.Equal(before.Fleets, after.Fleets);
        Assert.Equal(before.MercenaryPool, after.MercenaryPool);
    }

    [Fact]
    public void A_nation_with_no_slots_is_left_unchanged()
    {
        var coordinator = RecruitmentTestbed.CoordinatorOnly(null, typeof(RecruitmentSlotReadinessSystem));
        var before = RecruitmentTestbed.InitialState();

        var after = coordinator.RunRoundTick(before).State;

        Assert.Equal(before.Nations, after.Nations);
    }
}
