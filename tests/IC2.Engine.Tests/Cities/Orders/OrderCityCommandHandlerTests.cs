using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Orders;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T18 City orders (fortification)", Done-when 1 (cost), 2 (read-back via
/// the <c>&gt; 100</c> encoding), 4 (both refusals), and 5 (money conservation). Dispatched through the
/// real <see cref="CommandDispatcher"/> over the real engine assembly, exactly like every other command
/// handler's own tests
/// (<c>tests/IC2.Engine.Tests/Economy/Commands/BuySupplyCommandHandlerTests.cs</c> is the closest model).
/// </summary>
/// <remarks>
/// Every fixture here uses Arx from the shipped <c>toy-3city.json</c> world: owner <c>"north"</c> (the
/// toy scenario's active seat, so no seat-check gets in the way), population 220 thousand, starting
/// <c>fortificationCode</c> 60 (60% finished, nothing pending). Cost numbers below (220, 440) are that
/// population figure times the ruleset's own <c>costPerPointPerPopulationThousand</c> (1) and the points
/// ordered -- never a bare literal restating the rule.
/// </remarks>
public sealed class OrderCityCommandHandlerTests
{
    private const string CityId = "arx"; // owner "north", population 220k, fortificationCode 60.

    [Fact]
    public void Ordering_costs_population_thousands_times_points_and_debits_the_treasury_exactly()
    {
        var dispatcher = OrdersTestbed.Dispatcher();
        var before = CoreTestbed.InitialState();
        var city = before.CityById(CityId)!;
        var nation = before.NationById(before.ActiveNationId)!;
        Assert.Equal(60, city.FortificationCode); // sanity: finished, nothing pending yet.
        Assert.Equal(220, city.PopulationThousands);
        Assert.Equal(1, OrdersTestbed.FortifyRule.CostPerPointPerPopulationThousand);

        // N = 2 (not 1), so a mutation that drops the points factor entirely (charging population alone)
        // is caught: 220 != 440.
        var result = dispatcher.Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 2));

        Assert.True(result.IsAccepted);
        var updatedNation = result.State.NationById(nation.Id)!;
        Assert.Equal(440, nation.Treasury - updatedNation.Treasury); // 220 (population'000) x 2 (points) x 1.
    }

    [Fact]
    public void An_accepted_order_reads_back_as_in_progress_via_the_over_100_encoding_and_the_panel_value()
    {
        var dispatcher = OrdersTestbed.Dispatcher();
        var before = CoreTestbed.InitialState();
        var rule = OrdersTestbed.FortifyRule;

        var result = dispatcher.Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 2));

        Assert.True(result.IsAccepted);
        var updatedCity = result.State.CityById(CityId)!;
        Assert.Equal(260, updatedCity.FortificationCode); // 60 + 2 x 100 -- the dual encoding, > MaxPercent (100).
        Assert.True(FortificationCode.IsOrderInProgress(updatedCity.FortificationCode, rule));

        // What the status panel actually prints
        // (src/IC2.Engine/Presentation/GameSessionRendering.cs:65-68) is
        // FortificationCode.FinishedPercent, never the raw stored word -- so the panel still reads "60%"
        // while 2 points sit pending, not "260%" and not "0%".
        Assert.Equal(60, FortificationCode.FinishedPercent(updatedCity.FortificationCode, rule));
        Assert.Equal(2, FortificationCode.PendingPoints(updatedCity.FortificationCode, rule));
    }

    [Fact]
    public void Refused_at_max_percent_rejects_and_mutates_nothing()
    {
        var dispatcher = OrdersTestbed.Dispatcher();
        var initial = CoreTestbed.InitialState();
        var before = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with { FortificationCode = 100 });

        var result = dispatcher.Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 1));

        Assert.Equal(CityOrderRejections.FortifyRefusedAtMaxPercent, result.Code);
        Assert.Same(before, result.State); // no partial mutation -- the state that went in, unchanged.
    }

    [Fact]
    public void Refused_while_under_siege_rejects_and_mutates_nothing()
    {
        var dispatcher = OrdersTestbed.Dispatcher();
        var initial = CoreTestbed.InitialState();
        var before = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with { UnderSiege = true });

        var result = dispatcher.Dispatch(before, new OrderCityCommand(before.ActiveNationId, CityId, "fortify", 1));

        Assert.Equal(CityOrderRejections.FortifyRefusedWhileUnderSiege, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// Done-when 5 named explicitly: the accepted debit is exact, and each of the two refusals debits the
    /// treasury by exactly zero -- restated as an explicit treasury comparison, not only the
    /// <see cref="Assert.Same{T}(T, T)"/> reference check the two refusal tests above already make.
    /// </summary>
    [Fact]
    public void Money_conservation_exact_debit_on_accept_zero_on_either_refusal()
    {
        var dispatcher = OrdersTestbed.Dispatcher();
        var initial = CoreTestbed.InitialState();

        var acceptNationBefore = initial.NationById(initial.ActiveNationId)!;
        var acceptResult = dispatcher.Dispatch(initial, new OrderCityCommand(initial.ActiveNationId, CityId, "fortify", 1));
        Assert.True(acceptResult.IsAccepted);
        var acceptNationAfter = acceptResult.State.NationById(acceptNationBefore.Id)!;
        Assert.Equal(220, acceptNationBefore.Treasury - acceptNationAfter.Treasury); // 220 x 1 x 1.

        var atMax = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with { FortificationCode = 100 });
        var atMaxNationBefore = atMax.NationById(atMax.ActiveNationId)!;
        var atMaxResult = dispatcher.Dispatch(atMax, new OrderCityCommand(atMax.ActiveNationId, CityId, "fortify", 1));
        Assert.Equal(CityOrderRejections.FortifyRefusedAtMaxPercent, atMaxResult.Code);
        Assert.Equal(atMaxNationBefore.Treasury, atMaxResult.State.NationById(atMaxNationBefore.Id)!.Treasury);

        var besieged = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with { UnderSiege = true });
        var besiegedNationBefore = besieged.NationById(besieged.ActiveNationId)!;
        var besiegedResult = dispatcher.Dispatch(besieged, new OrderCityCommand(besieged.ActiveNationId, CityId, "fortify", 1));
        Assert.Equal(CityOrderRejections.FortifyRefusedWhileUnderSiege, besiegedResult.Code);
        Assert.Equal(besiegedNationBefore.Treasury, besiegedResult.State.NationById(besiegedNationBefore.Id)!.Treasury);
    }
}
