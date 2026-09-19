using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Orders;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T18 City orders (fortification)", Done-when 3 (a siege attempt clears a
/// pending order, <c>fort %= 100</c>) and the <c>[open]</c> completion discrepancy this task resolves as
/// completion at 100, pinned by the catalogue's own 95+5 example. Run through the real
/// <see cref="TurnCoordinator"/>, scoped to just <see cref="CityOrderProgressSystem"/> -- the only way to
/// reach an <see cref="IGameSystem"/> at all, since <see cref="SystemContext"/>'s constructor is internal
/// (the same pattern as <c>tests/IC2.Engine.Tests/Economy/WeeklyCitySupplySystemTests.cs</c>).
/// </summary>
public sealed class CityOrderProgressSystemTests
{
    private const string CityId = "arx";

    /// <summary>
    /// Mutation proof, paired with <see cref="NotUnderSiege_TheSamePendingOrder_CompletesAtOneHundred"/>:
    /// the two fixtures are identical except <see cref="CityState.UnderSiege"/>, and land on different
    /// results (95 vs. 100). A handler that stopped reading the flag, or read it backwards, makes exactly
    /// one of the pair fail.
    /// </summary>
    [Fact]
    public void UnderSiege_ClearsThePendingOrderToTheFinishedRemainder()
    {
        var rule = OrdersTestbed.FortifyRule;
        var coordinator = OrdersTestbed.ProgressCoordinator();
        var initial = CoreTestbed.InitialState();

        // 95% finished, 5 points pending (code 60 + 5x100... using 95 here, not Arx's starting 60, to
        // share the exact fixture the completion pin below uses).
        var pendingCode = FortificationCode.WithOrder(95, 5, rule);
        Assert.Equal(595, pendingCode);
        var before = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with
        {
            FortificationCode = pendingCode,
            UnderSiege = true,
        });

        var after = coordinator.RunRoundTick(before).State;

        var city = after.CityById(CityId)!;
        Assert.Equal(95, city.FortificationCode); // fort %= 100: the pending order is wiped, not completed.
        Assert.False(FortificationCode.IsOrderInProgress(city.FortificationCode, rule));
    }

    /// <summary>
    /// The catalogue's own worked example and the <c>[open]</c> discrepancy this task resolves: read
    /// literally, "fort &gt; 100: if not threatened: fort += min(10, fort/100); fort -= min(1000,
    /// (fort/100)x100)" sends 95 + 5 pending to 600, then to 0 -- <em>not</em> 100. This task's chosen
    /// reading, completion at 100 (recorded in <see cref="CityOrderProgressSystem"/>'s own remarks), is
    /// what this test pins.
    /// </summary>
    [Fact]
    public void NotUnderSiege_TheSamePendingOrder_CompletesAtOneHundred()
    {
        var rule = OrdersTestbed.FortifyRule;
        var coordinator = OrdersTestbed.ProgressCoordinator();
        var initial = CoreTestbed.InitialState();

        var pendingCode = FortificationCode.WithOrder(95, 5, rule);
        var before = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with
        {
            FortificationCode = pendingCode,
            UnderSiege = false,
        });

        var after = coordinator.RunRoundTick(before).State;

        var city = after.CityById(CityId)!;
        Assert.Equal(100, city.FortificationCode); // completion at max, not the read-literal 0.
        Assert.NotEqual(0, city.FortificationCode);
        Assert.False(FortificationCode.IsOrderInProgress(city.FortificationCode, rule)); // finished, nothing pending.
    }
}
