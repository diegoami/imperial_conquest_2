using IC2.Engine.Core;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// The command layer around <see cref="StandingRecruitmentCost.InitialCost"/> — Scope: "Standing
/// recruitment from the treasury, into the per-nation recruitment slots T35 adds to the model". The
/// formula itself is Done-when 1's concern (<see cref="StandingRecruitmentCostTests"/>); these tests
/// cover only the command wrapped around it: the treasury debit and the created
/// <see cref="Model.RecruitmentSlot"/>.
/// </summary>
public sealed class RecruitStandingUnitCommandHandlerTests
{
    [Fact]
    public void An_order_debits_the_treasury_by_InitialCost_and_creates_a_slot_at_StateCode_zero()
    {
        var sink = new RecordingEventSink();
        var dispatcher = RecruitmentTestbed.Dispatcher(sink);
        var before = RecruitmentTestbed.InitialState();
        var nation = before.NationById("north")!;
        var city = before.CityById("arx")!;
        Assert.Equal(nation.Id, city.Owner);

        const int troops = 1400; // menu-and-toolbar-inventory.md's own light-cavalry example.
        var expectedCost = StandingRecruitmentCost.InitialCost(troops, "light_cavalry", RecruitmentTestbed.Ruleset);
        Assert.Equal(105, expectedCost);

        var result = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(nation.Id, city.Id, "light_cavalry", troops));

        Assert.True(result.IsAccepted);
        var ordered = Assert.IsType<RecruitmentOrdered>(Assert.Single(sink.Events));
        Assert.Equal(expectedCost, ordered.TalentsPaid);

        var updatedNation = result.State.NationById(nation.Id)!;
        Assert.Equal(nation.Treasury - expectedCost, updatedNation.Treasury);

        var slot = Assert.Single(updatedNation.RecruitmentSlots);
        Assert.Equal(city.Id, slot.TargetCityId);
        Assert.Equal("light_cavalry", slot.UnitTypeId);
        Assert.Equal(troops, slot.Troops);
        Assert.Equal(0, slot.StateCode);
    }

    [Fact]
    public void Unknown_city_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(before.ActiveNationId, "no-such-city", "light_cavalry", 1400));

        Assert.Equal(RecruitStandingUnitRejections.UnknownCity, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_foreign_citys_order_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.InitialState();
        Assert.Equal("north", before.ActiveNationId);

        var result = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(before.ActiveNationId, "meridia", "light_cavalry", 1400));

        Assert.Equal(RecruitStandingUnitRejections.NotYourCity, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Unknown_unit_type_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(before.ActiveNationId, "arx", "no-such-type", 1400));

        Assert.Equal(RecruitStandingUnitRejections.UnknownUnitType, result.Code);
        Assert.Same(before, result.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_troop_count_is_rejected_and_changes_nothing(int troops)
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var before = RecruitmentTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(before.ActiveNationId, "arx", "light_cavalry", troops));

        Assert.Equal(RecruitStandingUnitRejections.InvalidTroops, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_order_the_treasury_cannot_afford_is_rejected_and_changes_nothing()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var nation = initial.NationById("north")!;
        var poorNation = nation with { Treasury = 10 }; // far less than the 105-talent order below.
        var before = RecruitmentTestbed.WithNation(initial, poorNation);

        var result = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(nation.Id, "arx", "light_cavalry", 1400));

        Assert.Equal(RecruitStandingUnitRejections.InsufficientTreasury, result.Code);
        Assert.Same(before, result.State);
    }
}
