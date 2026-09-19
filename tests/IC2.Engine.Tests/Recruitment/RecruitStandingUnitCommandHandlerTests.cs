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

        // DoD 2's other direction: standing recruitment touches no army purse at all — not merely
        // "the same armies by count", but every army record-identical (money included) to the state
        // before the command ran.
        Assert.Equal(before.Armies, result.State.Armies);
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

    /// <summary>
    /// T55 Done-when 6: the recruitment table holds <see cref="IC2.Engine.Model.RecruitmentRules.MaxSlots"/>
    /// units and the next order is refused — the original's <em>"You have reached your limit of 40
    /// units."</em> The boundary either side: 39 slots accepts, 40 refuses.
    /// </summary>
    [Fact]
    public void An_order_is_refused_once_the_recruitment_table_holds_forty_units()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var maxSlots = RecruitmentTestbed.Ruleset.Recruitment.MaxSlots;
        Assert.Equal(40, maxSlots);

        var initial = RecruitmentTestbed.InitialState();
        var nation = initial.NationById("north")!;
        var slot = new IC2.Engine.Model.RecruitmentSlot("arx", "light_cavalry", 1400, 0);

        var oneShort = RecruitmentTestbed.WithNation(
            initial,
            nation with { RecruitmentSlots = IC2.Engine.Model.ValueList.From(Enumerable.Repeat(slot, maxSlots - 1)) });
        var accepted = dispatcher.Dispatch(
            oneShort, new RecruitStandingUnitCommand(nation.Id, "arx", "light_cavalry", 1400));
        Assert.True(accepted.IsAccepted);
        Assert.Equal(maxSlots, accepted.State.NationById(nation.Id)!.RecruitmentSlots.Count);

        // And the very next one -- against the state the accepted order produced, not a hand-built
        // full table -- is refused with nothing changed.
        var full = accepted.State;
        var refused = dispatcher.Dispatch(
            full, new RecruitStandingUnitCommand(nation.Id, "arx", "light_cavalry", 1400));
        Assert.Equal(RecruitStandingUnitRejections.RecruitmentTableFull, refused.Code);
        Assert.Same(full, refused.State);
    }

    /// <summary>
    /// T55 Done-when 7: placing an order raises the nation's mobilization rate by
    /// <c>1 + (troops × 1000) / wealth</c>, through the very function
    /// <see cref="MobilizationRate.AfterOrderPlaced"/> — asserted here against an independently
    /// computed figure, so the handler reading the rule and the rule itself cannot drift together.
    /// </summary>
    [Fact]
    public void An_order_raises_the_nations_mobilization_rate()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();

        // Rome's own figures from decompiled-mobilization-and-mercenary-restock.md §5: wealth 768,000
        // and a 15,000-troop light-infantry order, which the report works out as +20 points.
        var nation = initial.NationById("north")! with { Wealth = 768_000, MobilizedPercent = 30, Treasury = 1_000_000 };
        var before = RecruitmentTestbed.WithNation(initial, nation);

        var result = dispatcher.Dispatch(
            before, new RecruitStandingUnitCommand(nation.Id, "arx", "light_infantry", 15_000));

        Assert.True(result.IsAccepted);
        Assert.Equal(50, result.State.NationById(nation.Id)!.MobilizedPercent);
        Assert.Equal(30 + 1 + (15_000 * 1000 / 768_000), result.State.NationById(nation.Id)!.MobilizedPercent);
    }

    /// <summary>
    /// The cap is enforced where the raise happens: a nation already at 100 % stays at 100 % rather
    /// than climbing past it.
    /// </summary>
    [Fact]
    public void The_mobilization_rate_is_capped_where_it_is_raised()
    {
        var dispatcher = RecruitmentTestbed.Dispatcher();
        var initial = RecruitmentTestbed.InitialState();
        var cap = RecruitmentTestbed.Ruleset.Recruitment.MobilizationCapPercent;

        var nation = initial.NationById("north")! with { Wealth = 768_000, MobilizedPercent = cap, Treasury = 1_000_000 };
        var before = RecruitmentTestbed.WithNation(initial, nation);

        var result = dispatcher.Dispatch(
            before, new RecruitStandingUnitCommand(nation.Id, "arx", "light_infantry", 15_000));

        Assert.True(result.IsAccepted);
        Assert.Equal(cap, result.State.NationById(nation.Id)!.MobilizedPercent);
    }
}
