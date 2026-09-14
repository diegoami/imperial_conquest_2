using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// Confirms this task's systems register into exactly the phases <c>docs/task-catalogue.md</c>
/// names: T06's declared attrition phase for the supply/morale rule (Done-when 10, 11), and the seasonal
/// weather-event phase (Done-when 8). Registered by attribute alone, discovered by scanning the real
/// shipped engine assembly -- the same mechanism <c>AttritionPhaseOrderingTests</c> (T06) proved was
/// usable before this task existed.
/// </summary>
public sealed class EconomySystemRegistrationTests
{
    [Fact]
    public void ArmySupplyAndMoraleSystem_RegistersIntoArmyTick()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var armyTick = registry.InPhase(TurnPhase.ArmyTick);

        Assert.Contains(armyTick, s =>
            s.Id == "economy.supply-consumption-and-morale"
            && s.ImplementationType == typeof(IC2.Engine.Economy.ArmySupplyAndMoraleSystem));
    }

    [Fact]
    public void WeatherEventSystem_RegistersIntoWeatherEvents()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var weatherEvents = registry.InPhase(TurnPhase.WeatherEvents);

        Assert.Contains(weatherEvents, s =>
            s.Id == "economy.weather-events"
            && s.ImplementationType == typeof(IC2.Engine.Economy.WeatherEventSystem));
    }

    [Fact]
    public void QuarterlyEconomySystem_RegistersAsAQuarterBoundaryHandler()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        Assert.Contains(registry.QuarterBoundaryHandlers, h =>
            h.Id == "economy.quarterly-billing"
            && h.ImplementationType == typeof(IC2.Engine.Economy.QuarterlyEconomySystem));
    }
}
