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
    private const string CityId = OrdersTestbed.ArxId;

    /// <summary>
    /// Mutation proof, paired with <see cref="NoHostileArmyAdjacent_TheSamePendingOrder_CompletesAtOneHundred"/>:
    /// the two fixtures are identical except that a hostile army was moved beside the city by a real
    /// <c>MoveArmyCommand</c> (T67, #304: "under siege" is the hostile-army-adjacent predicate, not a flag),
    /// and land on different results (95 vs. 100). A tick that stopped evaluating the predicate, or read it
    /// backwards, makes exactly one of the pair fail.
    /// </summary>
    [Fact]
    public void HostileArmyMovedBeside_ClearsThePendingOrderToTheFinishedRemainder()
    {
        var rule = OrdersTestbed.FortifyRule;
        var coordinator = OrdersTestbed.ProgressCoordinator();
        var initial = CoreTestbed.InitialState();

        // 95% finished, 5 points pending.
        var pendingCode = FortificationCode.WithOrder(95, 5, rule);
        Assert.Equal(595, pendingCode);
        var ordered = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with { FortificationCode = pendingCode });
        var before = OrdersTestbed.WithSouthArmyMovedBesideArx(ordered);

        var after = coordinator.RunRoundTick(before).State;

        var city = after.CityById(CityId)!;
        Assert.Equal(95, city.FortificationCode); // fort %= 100: the pending order is dropped, not completed.
        Assert.False(FortificationCode.IsOrderInProgress(city.FortificationCode, rule));
    }

    /// <summary>
    /// The same army beside the city, but the two nations are at peace: the predicate does not hold, so the
    /// order progresses. Together with the test above this is the predicate's relation gate on the weekly path.
    /// </summary>
    [Fact]
    public void AnArmyBesideTheCity_ButNotAtWar_DoesNotDropTheOrder()
    {
        var rule = OrdersTestbed.FortifyRule;
        var initial = CoreTestbed.InitialState();
        var ordered = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with
        {
            FortificationCode = FortificationCode.WithOrder(95, 5, rule),
        });
        var before = OrdersTestbed.WithSouthArmyMovedBesideArx(ordered, atWar: false);

        var after = OrdersTestbed.ProgressCoordinator().RunRoundTick(before).State;

        Assert.Equal(100, after.CityById(CityId)!.FortificationCode);
    }

    /// <summary>
    /// The shipped completion at 100 (bug #548 tracks that the original ends this order at 0): 95 with 5
    /// ordered is stored as 595, advances 5 and ends at 100. The 595 is the stored word, not 599.
    /// </summary>
    [Fact]
    public void NoHostileArmyAdjacent_TheSamePendingOrder_CompletesAtOneHundred()
    {
        var rule = OrdersTestbed.FortifyRule;
        var coordinator = OrdersTestbed.ProgressCoordinator();
        var initial = CoreTestbed.InitialState();

        var pendingCode = FortificationCode.WithOrder(95, 5, rule);
        Assert.Equal(595, pendingCode);
        var before = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with { FortificationCode = pendingCode });

        var after = coordinator.RunRoundTick(before).State;

        var city = after.CityById(CityId)!;
        Assert.Equal(100, city.FortificationCode); // completion at max, not the original's 0 (bug #548).
        Assert.NotEqual(0, city.FortificationCode);
        Assert.False(FortificationCode.IsOrderInProgress(city.FortificationCode, rule)); // finished, nothing pending.
    }

    /// <summary>
    /// T67 (#304, bug #344): the weekly step's cap is <c>cityOrders.fortifyWeeklyPointCap</c>, not a literal.
    /// With the cap set to 5 in a test ruleset, a 20-point order at 40% advances exactly 5 a week
    /// (45% with 15 pending), where the shipped cap (10) advances 10.
    /// </summary>
    [Fact]
    public void TheWeeklyCap_IsRulesetData()
    {
        var rule = OrdersTestbed.FortifyRule;
        var initial = CoreTestbed.InitialState();
        var before = OrdersTestbed.WithCity(initial, initial.CityById(CityId)! with
        {
            FortificationCode = FortificationCode.WithOrder(40, 20, rule),
        });

        var shipped = OrdersTestbed.ProgressCoordinator().RunRoundTick(before).State.CityById(CityId)!;
        Assert.Equal(10, OrdersTestbed.Ruleset.CityOrders.FortifyWeeklyPointCap);
        Assert.Equal(50, FortificationCode.FinishedPercent(shipped.FortificationCode, rule));
        Assert.Equal(10, FortificationCode.PendingPoints(shipped.FortificationCode, rule));

        var capFive = OrdersTestbed.Ruleset with
        {
            CityOrders = OrdersTestbed.Ruleset.CityOrders with { FortifyWeeklyPointCap = 5 },
        };
        var slowed = OrdersTestbed.ProgressCoordinator(ruleset: capFive).RunRoundTick(before).State.CityById(CityId)!;
        Assert.Equal(45, FortificationCode.FinishedPercent(slowed.FortificationCode, rule));
        Assert.Equal(15, FortificationCode.PendingPoints(slowed.FortificationCode, rule));
    }
}
