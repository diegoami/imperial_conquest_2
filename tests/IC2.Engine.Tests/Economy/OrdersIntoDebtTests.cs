using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Cities.Orders;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// Bug #549, T115: <c>unaffordableRecruitAndFortify</c> decides whether a recruitment or a fortification
/// order the treasury cannot cover is accepted into debt (<c>allowDebt</c>, <c>classical-faithful</c>) or
/// refused (<c>refuse</c>, <c>improved</c> and the small test ruleset). Run here over the small test
/// ruleset <c>with</c> the flag set, since the flag is the only thing the two handlers read.
/// </summary>
public sealed class OrdersIntoDebtTests
{
    private const string CityId = "arx";
    private const string UnitType = "light_cavalry";
    private const int Troops = 1400;

    private static CommandDispatcher Dispatcher(UnaffordableOrderPolicy policy) => new(
        SystemRegistry.FromEngineAssembly(),
        CoreTestbed.Toy.Ruleset with
        {
            Flags = CoreTestbed.Toy.Ruleset.Flags with { UnaffordableRecruitAndFortify = policy },
        },
        CoreTestbed.Toy.World,
        NullEventSink.Instance);

    private static GameState WithTreasury(GameState state, int treasury) =>
        OrdersTestbed.WithNation(state, state.NationById(state.ActiveNationId)! with { Treasury = treasury });

    [Fact]
    public void The_toy_ruleset_refuses()
    {
        Assert.Equal(UnaffordableOrderPolicy.Refuse, CoreTestbed.Toy.Ruleset.Flags.UnaffordableRecruitAndFortify);
    }

    [Fact]
    public void A_recruitment_the_treasury_cannot_cover_is_taken_into_debt_under_allowDebt()
    {
        var before = WithTreasury(CoreTestbed.InitialState(), 10);
        var cost = StandingRecruitmentCost.InitialCost(Troops, UnitType, CoreTestbed.Toy.Ruleset);
        Assert.True(before.NationById(before.ActiveNationId)!.Treasury < cost);
        var nationBefore = before.NationById(before.ActiveNationId)!;

        var result = Dispatcher(UnaffordableOrderPolicy.AllowDebt)
            .Dispatch(before, new RecruitStandingUnitCommand(before.ActiveNationId, CityId, UnitType, Troops));

        Assert.True(result.IsAccepted);
        var after = result.State.NationById(nationBefore.Id)!;
        Assert.Equal(nationBefore.Treasury - cost, after.Treasury);
        Assert.True(after.Treasury < 0);
        var slot = Assert.Single(after.RecruitmentSlots);
        Assert.Equal(Troops, slot.Troops);
        Assert.Equal(
            MobilizationRate.AfterOrderPlaced(nationBefore.MobilizedPercent, Troops, nationBefore.Wealth, CoreTestbed.Toy.Ruleset.Recruitment),
            after.MobilizedPercent);
        Assert.True(after.MobilizedPercent > nationBefore.MobilizedPercent);
    }

    [Fact]
    public void A_fortification_the_treasury_cannot_cover_is_taken_into_debt_under_allowDebt()
    {
        var before = WithTreasury(CoreTestbed.InitialState(), 10);
        var city = before.CityById(CityId)!;
        const int points = 2;
        var rule = OrdersTestbed.FortifyRule;
        var cost = rule.CostPerPointPerPopulationThousand * points * city.PopulationThousands;
        var nationBefore = before.NationById(before.ActiveNationId)!;
        Assert.True(nationBefore.Treasury < cost);

        var result = Dispatcher(UnaffordableOrderPolicy.AllowDebt)
            .Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", points));

        Assert.True(result.IsAccepted);
        var after = result.State.NationById(nationBefore.Id)!;
        Assert.Equal(nationBefore.Treasury - cost, after.Treasury);
        Assert.True(after.Treasury < 0);
        Assert.Equal(
            FortificationCode.WithOrder(city.FortificationCode, points, rule),
            result.State.CityById(CityId)!.FortificationCode);
        Assert.Equal(points, FortificationCode.PendingPoints(result.State.CityById(CityId)!.FortificationCode, rule));
    }

    [Fact]
    public void Both_orders_are_refused_with_their_existing_rejections_under_refuse()
    {
        var before = WithTreasury(CoreTestbed.InitialState(), 10);
        var dispatcher = Dispatcher(UnaffordableOrderPolicy.Refuse);

        var recruit = dispatcher.Dispatch(before, new RecruitStandingUnitCommand(before.ActiveNationId, CityId, UnitType, Troops));
        Assert.Equal(RecruitStandingUnitRejections.InsufficientTreasury, recruit.Code);
        Assert.Same(before, recruit.State);

        var fortify = dispatcher.Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 2));
        Assert.Equal(CityOrderRejections.InsufficientTreasury, fortify.Code);
        Assert.Same(before, fortify.State);
    }

    [Theory]
    [InlineData(UnaffordableOrderPolicy.AllowDebt)]
    [InlineData(UnaffordableOrderPolicy.Refuse)]
    public void An_order_city_from_an_unknown_nation_is_still_refused(UnaffordableOrderPolicy policy)
    {
        var before = CoreTestbed.InitialState();

        var result = Dispatcher(policy).Dispatch(before, new OrderCityCommand("no-such-nation", CityId, "fortify", 1));

        Assert.False(result.IsAccepted);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void An_affordable_order_is_unchanged_under_both_values()
    {
        var before = CoreTestbed.InitialState();
        var allow = Dispatcher(UnaffordableOrderPolicy.AllowDebt)
            .Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 1));
        var refuse = Dispatcher(UnaffordableOrderPolicy.Refuse)
            .Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 1));

        Assert.True(allow.IsAccepted);
        Assert.Equal(allow.State, refuse.State);
    }
}
