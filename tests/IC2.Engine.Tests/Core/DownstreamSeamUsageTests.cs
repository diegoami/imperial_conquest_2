using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

// Deliberately NOT IC2.Engine.Tests.Core: this file stands in for a later task's own test folder — the
// namespace T08's economy tests or T16's battle tests will actually sit in. T04 shipped an accessor that
// compiled perfectly inside its own namespace and failed with CS0234 from every sibling one, and the
// review could not see it from inside that PR. Twenty-five tasks consume the types below from namespaces
// like this one, so one test consumes them the way those tasks will.
namespace IC2.Engine.Tests.SeamConsumer;

/// <summary>
/// A later task's-eye view of the T03 seam: declare a system and a command handler by attribute, run the
/// pipeline, dispatch an order, fire the quarter boundary, and assert against the T04 fixtures corpus —
/// all from a namespace that is a sibling of, not inside, the one the seam's own tests live in.
/// </summary>
public class DownstreamSeamUsageTests
{
    private const string Group = "downstream-consumer";

    [Fact]
    public void A_later_task_can_declare_a_system_and_see_it_run()
    {
        var registry = CoreTestbed.RegistryFor(Group);
        var coordinator = new TurnCoordinator(
            registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

        // The system is declared in a round-scoped phase, so the global tick is what runs it — callable
        // on its own, with no calendar merged to decide when a round has completed.
        var result = coordinator.RunRoundTick(CoreTestbed.InitialState());

        Assert.Equal("downstream.tribute", Assert.Single(result.Trace).SystemId);
        Assert.NotEqual(
            GameStateHash.Compute(CoreTestbed.InitialState()),
            GameStateHash.Compute(result.State));
    }

    [Fact]
    public void A_later_task_can_reject_an_illegal_order_with_its_own_reason_code()
    {
        var dispatcher = new CommandDispatcher(
            CoreTestbed.RegistryFor(Group),
            CoreTestbed.Toy.Ruleset,
            CoreTestbed.Toy.World,
            NullEventSink.Instance);

        var state = CoreTestbed.InitialState();

        // The cap is the T04 corpus's, not a number written here: 100,000 troops per army.
        var cap = FixtureCorpus.Get("caps.maxTroopsPerArmy").AsInt();
        var result = dispatcher.Dispatch(state, new DownstreamLevyCommand(state.ActiveNationId, cap + 1));

        Assert.True(result.IsRejected);
        Assert.Equal(DownstreamRejections.OverTroopCap, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void A_later_task_can_fire_the_quarter_boundary_without_a_calendar()
    {
        var sink = new RecordingEventSink();
        var coordinator = new TurnCoordinator(
            CoreTestbed.RegistryFor(Group), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink);

        var before = CoreTestbed.InitialState();
        var after = coordinator.FireQuarterBoundary(before, CoreTestbed.Toy.Ruleset.Calendar.StartSeasonIndex);

        // Unity decays by 3 a quarter, from the corpus rather than from a literal or from memory.
        // T35, docs/task-catalogue.md DoD 10: renamed from economy.unityDecayPerQuarter -- the real
        // quarterly "-3" is mobilization's, not unity's (bug #68) -- but this demo handler's own shape
        // (decrement a nation field by a fixture-sourced amount) is unaffected by which field it is.
        var decay = FixtureCorpus.Get("economy.mobilizationDecayPerQuarter").AsInt();
        foreach (var nation in after.Nations)
        {
            Assert.Equal(before.NationById(nation.Id)!.Unity - decay, nation.Unity);
        }

        Assert.Single(sink.Events);
    }

    [Fact]
    public void A_later_task_can_enumerate_the_declared_event_kinds()
    {
        // The mechanism T10's news-log coverage test needs: every declared news-worthy event kind, found
        // by the same assembly scan that finds systems, with no shared enum for tasks to fight over.
        var kinds = DomainEventCatalog.Discover(new[] { typeof(DownstreamSeamUsageTests).Assembly });

        Assert.Contains(kinds, kind => kind.Kind == "downstream.tribute-collected" && kind.NewsWorthy);
        Assert.Equal(
            kinds.Select(kind => kind.Kind).OrderBy(kind => kind, StringComparer.Ordinal).ToArray(),
            kinds.Select(kind => kind.Kind).ToArray());
    }
}

/// <summary>Rejection codes a later task declares in its own file, exactly as T13 or T14 will.</summary>
public static class DownstreamRejections
{
    /// <summary>The order would take the army past the confirmed 100,000-troop cap.</summary>
    public static readonly RejectionCode OverTroopCap = new("downstream.over-troop-cap");
}

/// <summary>A later task's command.</summary>
public sealed record DownstreamLevyCommand(string IssuingNationId, int Troops) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "downstream.levy";
}

/// <summary>A later task's command handler, registered by attribute from its own namespace.</summary>
[TestFixtureGroup(DownstreamSeamFixtures.Group)]
[CommandHandler]
public sealed class DownstreamLevyHandler : ICommandHandler<DownstreamLevyCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(DownstreamLevyCommand command, CommandContext context)
    {
        var cap = context.Ruleset.ArmyManagement.MaxTroopsPerArmy;
        if (command.Troops > cap)
        {
            return CommandOutcome.Reject(
                DownstreamRejections.OverTroopCap,
                $"{command.Troops} troops exceeds the {cap}-troop army cap.");
        }

        return CommandOutcome.Accept(context.State);
    }
}

/// <summary>A later task's system, registered by attribute from its own namespace.</summary>
[TestFixtureGroup(DownstreamSeamFixtures.Group)]
[GameSystem(TurnPhase.CityTick, "downstream.tribute")]
public sealed class DownstreamTributeSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var cities = context.State.Cities.Select(city =>
            city with { Tribute = city.Tribute + context.Rng.NextInt(city.PopulationThousands + 1) });

        return context.State with { Cities = ValueList.From(cities) };
    }
}

/// <summary>A later task's quarter-boundary subscriber, registered by attribute from its own namespace.</summary>
[TestFixtureGroup(DownstreamSeamFixtures.Group)]
[QuarterBoundaryHandler("downstream.unity-decay")]
public sealed class DownstreamUnityDecayHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        var decay = context.Ruleset.Economy.MobilizationDecayPerQuarter;
        var nations = context.State.Nations.Select(nation => nation with { Unity = nation.Unity - decay });

        context.Events.Publish(new DownstreamTributeCollected(context.EndingSeasonIndex));
        return context.State with { Nations = ValueList.From(nations) };
    }
}

/// <summary>A later task's domain event.</summary>
[DomainEvent("downstream.tribute-collected", NewsWorthy = true)]
public sealed record DownstreamTributeCollected(int EndingSeasonIndex) : DomainEvent;

/// <summary>The fixture group name, so the three registrations above scope to this file's tests.</summary>
public static class DownstreamSeamFixtures
{
    /// <summary>The group.</summary>
    public const string Group = "downstream-consumer";
}
