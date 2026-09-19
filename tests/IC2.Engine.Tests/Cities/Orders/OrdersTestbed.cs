using System.Linq;
using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Cities.Orders;

/// <summary>
/// The pieces a Cities/Orders test needs: the shipped toy <see cref="Ruleset"/> (reused from
/// <see cref="CoreTestbed"/>, exactly like every other task's tests), a real
/// <see cref="CommandDispatcher"/> for <see cref="OrderCityCommand"/>, a <see cref="TurnCoordinator"/>
/// scoped to just <see cref="CityOrderProgressSystem"/> so a round tick exercises nothing else (the same
/// pattern as <c>tests/IC2.Engine.Tests/Economy/WeeklyCitySupplySystemTests.cs</c>'s
/// <c>EconomyTestbed.CoordinatorOnly</c>, since <see cref="SystemContext"/>'s constructor is internal and
/// no test builds one directly), and small state-replacement helpers.
/// </summary>
/// <remarks>
/// Every gameplay number the tests in this folder assert comes from <see cref="Ruleset"/> (loaded from
/// the shipped <c>toy-ruleset.json</c>, including its <c>cityOrders</c> block) or from the shipped
/// <c>toy-3city.json</c> world fixture -- never pasted in as a bare literal.
/// </remarks>
public static class OrdersTestbed
{
    /// <summary>The shipped toy ruleset, including its <c>cityOrders</c> block.</summary>
    public static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>The shipped ruleset's one city order.</summary>
    public static CityOrderRule FortifyRule =>
        Ruleset.CityOrders.Orders.First(o => string.Equals(o.Id, "fortify", System.StringComparison.Ordinal));

    /// <summary>A dispatcher over the real engine assembly, exactly like every other command handler's own tests.</summary>
    public static CommandDispatcher Dispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), Ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    /// <summary>A coordinator scoped to only <see cref="CityOrderProgressSystem"/>.</summary>
    public static TurnCoordinator ProgressCoordinator(IEventSink? sink = null) =>
        new(
            SystemRegistry.FromAssemblies(
                new[] { typeof(CityOrderProgressSystem).Assembly },
                type => type == typeof(CityOrderProgressSystem)),
            Ruleset,
            CoreTestbed.Toy.World,
            sink ?? NullEventSink.Instance);

    /// <summary>Replaces one city in <paramref name="state"/> by id.</summary>
    public static GameState WithCity(GameState state, CityState updated) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, updated.Id, System.StringComparison.Ordinal) ? updated : c)),
        };

    /// <summary>Replaces one nation in <paramref name="state"/> by id.</summary>
    public static GameState WithNation(GameState state, NationState updated) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, updated.Id, System.StringComparison.Ordinal) ? updated : n)),
        };
}
