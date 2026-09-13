using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// The scripted pipeline used by the determinism tests: four systems spread across the seat- and
/// round-scoped phases, each of which draws from its own random stream and writes the result into the
/// state, so that any loss of determinism anywhere in the seam shows up as a changed state hash.
/// </summary>
/// <remarks>
/// These are test doubles, not gameplay: the real economy is T08 and the real calendar is T06. Every
/// bound they use is nevertheless read from the loaded <see cref="Ruleset"/> rather than written as a
/// literal, so nothing here can be mistaken for an invented constant.
/// </remarks>
public static class PipelineFixtures
{
    /// <summary>The fixture group these systems belong to.</summary>
    public const string Group = "pipeline";
}

/// <summary>Announces the seat's turn. Publishes an event and changes nothing.</summary>
[TestFixtureGroup(PipelineFixtures.Group)]
[GameSystem(TurnPhase.SeatStart, "test.pipeline.seat-start")]
public sealed class PipelineSeatStartSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PipelineSeatStarted(context.ActiveNationId));
        return context.State;
    }
}

/// <summary>
/// Spends a random part of each of the active seat's armies' purses — a draw whose result lands in the
/// state, which is what makes the state hash sensitive to the random stream.
/// </summary>
[TestFixtureGroup(PipelineFixtures.Group)]
[GameSystem(TurnPhase.Orders, "test.pipeline.orders")]
public sealed class PipelineOrdersSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var cap = context.Ruleset.Economy.PurseCapPerUnit;
        var armies = context.State.Armies.Select(army =>
            string.Equals(army.Nation, context.ActiveNationId, StringComparison.Ordinal)
                ? army with { Money = context.Rng.NextInt(cap + 1) }
                : army);

        return context.State with { Armies = ValueList.From(armies) };
    }
}

/// <summary>
/// Stands in for T06's seat rotation: advances the seat and, when the round wraps back to the first
/// seat, signals the coordinator that the global tick is due.
/// </summary>
/// <remarks>
/// Deliberately a test fixture rather than engine code. Seat rotation follows the save's own turn-order
/// table and belongs to T06; what T03 owns is the signal, and this fixture exists to exercise it before
/// that task lands.
/// </remarks>
[TestFixtureGroup(PipelineFixtures.Group)]
[GameSystem(TurnPhase.SeatEnd, "test.pipeline.seat-end")]
public sealed class PipelineSeatEndSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var next = (context.State.ActiveSeatIndex + 1) % context.State.TurnOrder.Count;
        if (next == 0)
        {
            context.Signals.RequestRoundTick();
        }

        return context.State with { ActiveSeatIndex = next };
    }
}

/// <summary>
/// A round-scoped system: nudges every city's loyalty by a drawn amount, bounded by the ruleset's own
/// loyalty floors, and advances the calendar's turn counter so successive rounds are distinguishable.
/// </summary>
[TestFixtureGroup(PipelineFixtures.Group)]
[GameSystem(TurnPhase.CityTick, "test.pipeline.city-tick")]
public sealed class PipelineCityTickSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var loyalty = context.Ruleset.Loyalty;
        var cities = context.State.Cities.Select(city => city with
        {
            Loyalty = context.Rng.NextInt(loyalty.ForcedCaptureFloor, loyalty.AllegiantRecaptureTarget),
        });

        context.Events.Publish(new PipelineRoundTicked(context.State.Calendar.TurnIndex + 1));

        return context.State with
        {
            Cities = ValueList.From(cities),
            Calendar = context.State.Calendar with { TurnIndex = context.State.Calendar.TurnIndex + 1 },
        };
    }
}

/// <summary>
/// A command the scripted run issues once per turn, so the dispatcher's own advance of the root random
/// value is part of what the determinism check covers rather than being tested only in isolation.
/// </summary>
public sealed record PipelineStockUpCommand(string IssuingNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "test.stock-up";
}

/// <summary>Tops up the issuing nation's armies with a drawn quantity of supplies.</summary>
[TestFixtureGroup(PipelineFixtures.Group)]
[CommandHandler]
public sealed class PipelineStockUpHandler : ICommandHandler<PipelineStockUpCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(PipelineStockUpCommand command, CommandContext context)
    {
        var tonsPerTroops = context.Ruleset.Economy.ArmySupplyTonsPerTroops;
        var armies = context.State.Armies.Select(army =>
            string.Equals(army.Nation, command.IssuingNationId, StringComparison.Ordinal)
                ? army with { SupplyTons = context.Rng.NextInt((army.TotalTroops / tonsPerTroops) + 1) }
                : army);

        context.Events.Publish(new PipelineSeatStarted(command.IssuingNationId));
        return CommandOutcome.Accept(context.State with { Armies = ValueList.From(armies) });
    }
}

/// <summary>A seat's turn began.</summary>
[DomainEvent("test.seat-started")]
public sealed record PipelineSeatStarted(string NationId) : DomainEvent;

/// <summary>A full round of seats completed and the global tick ran.</summary>
[DomainEvent("test.round-ticked", NewsWorthy = true)]
public sealed record PipelineRoundTicked(int TurnIndex) : DomainEvent;
