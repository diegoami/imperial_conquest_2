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

    /// <summary>
    /// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and
    /// deposition for debt", Done-when 6: the AI deposition check runs after this quarter's income and
    /// unity update, so it registers strictly after <c>economy.quarterly-nation-tick</c> (order 200).
    /// </summary>
    [Fact]
    public void AiDepositionHandler_RegistersAfterTheNationTick()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        var billing = registry.QuarterBoundaryHandlers.Single(h => h.Id == "economy.quarterly-billing");
        var nationTick = registry.QuarterBoundaryHandlers.Single(h => h.Id == "economy.quarterly-nation-tick");
        var deposition = registry.QuarterBoundaryHandlers.Single(h =>
            h.Id == "economy.ai-deposition"
            && h.ImplementationType == typeof(IC2.Engine.Economy.AiDepositionHandler));

        Assert.True(billing.Order < nationTick.Order);
        Assert.True(nationTick.Order < deposition.Order);
    }

    /// <summary>
    /// T87 rework round 1 (bug #380, review B5): the human deposition check used to also register as a
    /// <c>SeatStart</c> system (<c>HumanDepositionSystem</c>, since deleted) so it could be exercised
    /// through <see cref="IC2.Engine.Core.TurnCoordinator.RunTurn"/> directly. That system's only real
    /// caller turned out to be <c>GameSession.HandleEndSeated</c>'s own <c>RunTurn</c> call for the seat
    /// ending its own turn — which the review found deposes at the seat's own <c>end</c>, not the start
    /// of its <em>next</em> turn (#380's own symptom, reintroduced), and lets the same <c>RunTurn</c>'s own
    /// <c>Orders</c> phase immediately play the just-deposed seat as AI, in the same round as its own
    /// human turn. The check now runs only from <c>GameSession</c>'s own turn-rotation code
    /// (<c>DepositActiveHumanSeatIfItShouldFallAtTurnStart</c>), before any <c>RunTurn</c> call for that
    /// seat at all — no phase, no registration. This test is not replaced; there is nothing left to
    /// register into <see cref="TurnPhase.SeatStart"/> for.
    /// </summary>
    [Fact]
    public void SeatStart_NoLongerRegistersAHumanDepositionSystem()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var seatStart = registry.InPhase(TurnPhase.SeatStart);

        Assert.DoesNotContain(seatStart, s => s.Id == "economy.human-deposition");
    }
}
