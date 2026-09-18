using System.Linq;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Victory;

/// <summary>
/// <c>docs/task-catalogue.md</c> T43, DoD 4-6: <see cref="VictoryCheckSystem"/> is a registered
/// <c>RoundEnd</c> system (closing #105's wiring gap), runs after T42's news writer, publishes a domain
/// event on a win or an expiry (and nothing otherwise), and is idempotent within one run.
/// </summary>
public sealed class VictoryCheckSystemTests
{
    /// <summary>
    /// A registry over just <see cref="VictoryCheckSystem"/> in isolation -- deliberately excludes
    /// <see cref="EarlierWinnerAnnouncer"/> (a different fixture, only <c>DoesNotPublishASecondEvent_…</c>
    /// wants it present), so every other test here sees exactly this task's one production system.
    /// </summary>
    private static SystemRegistry SoloRegistry() =>
        SystemRegistry.FromAssemblies(
            new[] { typeof(VictoryCheckSystem).Assembly },
            type => type == typeof(VictoryCheckSystem));

    /// <summary>DoD 4: registers by attribute alone, from inside <c>Victory/**</c>, into <c>RoundEnd</c>.</summary>
    [Fact]
    public void RegisteredByAttribute_InRoundEnd_AcrossTheWholeEngineAssembly()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        var roundEnd = registry.InPhase(TurnPhase.RoundEnd);
        var registered = Assert.Single(roundEnd, s => s.Id == "victory.round-end-check");

        Assert.Equal(TurnPhase.RoundEnd, registered.Phase);
        Assert.IsType<VictoryCheckSystem>(registered.Instance);
    }

    /// <summary>
    /// DoD 5, pinned from the Victory side too (the News-side pin lives in
    /// <c>NewsLogWriterTests.NewsWriterRound_IsLast_InRoundEnd_AcrossTheWholeEngineAssembly</c>, which
    /// this task's PR also updates): <c>victory.round-end-check</c> runs after
    /// <c>news.writer.round</c> in the real, whole-engine-assembly registry, not merely in a narrow test
    /// fixture.
    /// </summary>
    [Fact]
    public void RunsAfterTheNewsWriter_AcrossTheWholeEngineAssembly()
    {
        var registry = SystemRegistry.FromEngineAssembly();
        var roundEnd = registry.InPhase(TurnPhase.RoundEnd);

        var writerIndex = IndexOfId(roundEnd, "news.writer.round");
        var victoryIndex = IndexOfId(roundEnd, "victory.round-end-check");

        Assert.True(writerIndex >= 0, "news.writer.round must be registered in RoundEnd.");
        Assert.True(victoryIndex >= 0, "victory.round-end-check must be registered in RoundEnd.");
        Assert.True(victoryIndex > writerIndex, "victory.round-end-check must run after news.writer.round.");
    }

    /// <summary>DoD 4: a win publishes exactly one <see cref="GameWon"/>, and it is not news-worthy.</summary>
    [Fact]
    public void PublishesGameWon_WhenTheRulesetsDefaultConditionIsMet()
    {
        var conquered = VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north");
        var coordinator = new TurnCoordinator(SoloRegistry(), VictoryTestbed.Ruleset, VictoryTestbed.World, NullEventSink.Instance);

        var result = coordinator.RunRoundTick(conquered);

        var won = Assert.Single(result.Events.OfType<GameWon>());
        Assert.Equal(VictoryConditionType.TotalConquest, won.ConditionType);
        Assert.Equal("north", won.WinningNationId);
        Assert.False(won.IsNewsWorthy, "A win must not be reported through the news log -- see GameWon's remarks.");
        Assert.Empty(result.Events.OfType<GameExpired>());
    }

    /// <summary>DoD 4: the hard end year with nobody having won publishes exactly one <see cref="GameExpired"/>.</summary>
    [Fact]
    public void PublishesGameExpired_WhenTheHardEndYearIsReached_WithNobodyHavingWon()
    {
        var atTheLimit = VictoryTestbed.WithYear(VictoryTestbed.InitialState(), VictoryTestbed.Ruleset.Victory.HardEndYearBc);
        var coordinator = new TurnCoordinator(SoloRegistry(), VictoryTestbed.Ruleset, VictoryTestbed.World, NullEventSink.Instance);

        var result = coordinator.RunRoundTick(atTheLimit);

        var expired = Assert.Single(result.Events.OfType<GameExpired>());
        Assert.Equal(VictoryConditionType.TotalConquest, expired.ConditionType);
        Assert.False(expired.IsNewsWorthy);
        Assert.Empty(result.Events.OfType<GameWon>());
    }

    /// <summary>DoD 4: an undecided state publishes nothing.</summary>
    [Fact]
    public void PublishesNothing_WhileTheGameIsUndecided()
    {
        var coordinator = new TurnCoordinator(SoloRegistry(), VictoryTestbed.Ruleset, VictoryTestbed.World, NullEventSink.Instance);

        var result = coordinator.RunRoundTick(VictoryTestbed.InitialState());

        Assert.Empty(result.Events.OfType<GameWon>());
        Assert.Empty(result.Events.OfType<GameExpired>());
    }

    /// <summary>
    /// DoD 6: if a <see cref="GameWon"/> was already published earlier in this same run -- simulated here
    /// by <see cref="EarlierWinnerAnnouncer"/>, registered at the default <c>Order</c> so it runs before
    /// <see cref="VictoryCheckSystem"/>'s <c>Order = int.MaxValue</c> within the same <c>RoundEnd</c>
    /// phase -- <see cref="VictoryCheckSystem"/> still evaluates the (also decided) state but does not
    /// publish a second, duplicate event.
    /// </summary>
    [Fact]
    public void DoesNotPublishASecondEvent_WhenOneWasAlreadyPublishedEarlierInTheSameRound()
    {
        var conquered = VictoryTestbed.WithAllCitiesOwnedBy(VictoryTestbed.InitialState(), "north");
        var registry = SystemRegistry.FromAssemblies(
            new[] { typeof(VictoryCheckSystem).Assembly, typeof(VictoryCheckSystemTests).Assembly },
            type => type == typeof(VictoryCheckSystem) || type == typeof(EarlierWinnerAnnouncer));
        var coordinator = new TurnCoordinator(registry, VictoryTestbed.Ruleset, VictoryTestbed.World, NullEventSink.Instance);

        var result = coordinator.RunRoundTick(conquered);

        // One event total across both systems: EarlierWinnerAnnouncer's own publish, and
        // VictoryCheckSystem's real evaluation correctly declining to add a second.
        Assert.Single(result.Events.OfType<GameWon>());
    }

    private const string IdempotenceProbeGroup = "victory.idempotence-probe";

    private static int IndexOfId(IReadOnlyList<RegisteredSystem> systems, string id)
    {
        for (var i = 0; i < systems.Count; i++)
        {
            if (string.Equals(systems[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A test-only <c>RoundEnd</c> system, registered at the default <c>Order</c> (so it runs before
    /// <see cref="VictoryCheckSystem"/>'s <c>Order = int.MaxValue</c>), that always announces the same
    /// win <see cref="VictoryCheckSystem"/> would independently reach for the "north owns every city"
    /// state — standing in for "some other event already decided this round" so DoD 6's idempotence can
    /// be exercised without a second real victory-publishing system.
    /// </summary>
    [TestFixtureGroup(IdempotenceProbeGroup)]
    [GameSystem(TurnPhase.RoundEnd, "test.victory.earlier-winner-announcer")]
    public sealed class EarlierWinnerAnnouncer : IGameSystem
    {
        /// <inheritdoc/>
        public GameState Execute(SystemContext context)
        {
            context.Events.Publish(new GameWon(VictoryConditionType.TotalConquest, "north"));
            return context.State;
        }
    }
}
