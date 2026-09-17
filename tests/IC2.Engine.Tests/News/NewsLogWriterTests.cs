using IC2.Engine.Core;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// DoD 4: "A news-worthy event published to the sink during a turn appears as a rendered message in
/// <c>GameState.NewsLog</c> at the end of that turn, asserted on the state itself rather than on the sink;
/// a non-news-worthy event does not. The 40-slot eviction is asserted end-to-end through the writer, not
/// only against the buffer in isolation."
/// </summary>
/// <remarks>
/// Every test here runs the real, registry-instantiated <see cref="NewsLogWriterSeatEnd"/> or
/// <see cref="NewsLogWriterRoundEnd"/> through a real <see cref="TurnCoordinator"/>, and asserts on
/// <c>result.State.NewsLog</c> — never on a copy of the rendering logic, and never by calling
/// <see cref="NewsLogWriter.Append"/> directly (that is <c>NewsLogWriterAppendTests</c>'s job). The DoD-4
/// cases (built via <see cref="CoordinatorFor"/>) use a <see cref="CompositeEventSink"/> of a recorder plus
/// <see cref="RecordingUiStyleSink"/> — a realistic caller composition, not a bare sink — to make explicit
/// that the writer's correctness does not depend on what else is chained after it (see
/// <see cref="NewsLogWriterFixtures.RegistryFor"/> and <see cref="RecordingUiStyleSink"/>'s remarks; this
/// is exactly where the first attempt's reflection-based design broke, R7). The two ordering-proof tests
/// below build their own coordinator directly over <see cref="NullEventSink"/> instead: they are proving
/// execution order, not sink robustness, and gain nothing from a composed sink.
/// </remarks>
public class NewsLogWriterTests
{
    private static TurnCoordinator CoordinatorFor(string group, out RecordingUiStyleSink uiSink)
    {
        uiSink = new RecordingUiStyleSink();
        return NewsLogWriterFixtures.CoordinatorFor(group, new CompositeEventSink(new RecordingEventSink(), uiSink));
    }

    /// <summary>DoD 4(a): a seat-scoped news-worthy event appears in the log at the end of that seat's turn.</summary>
    [Fact]
    public void SeatScopedEvent_AppearsInLog_AtSeatEnd()
    {
        var coordinator = CoordinatorFor(NewsLogWriterFixtures.SeatScopedGroup, out var uiSink);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        Assert.Single(result.State.NewsLog.Slots);
        Assert.Equal("City0   (Old)  falls to New.", result.State.NewsLog.Slots[0].Text);
        // The composed "UI" sink still receives the raw event -- the writer's own rendering path never
        // replaces or interferes with the caller's own consumer.
        Assert.Contains(uiSink.Received, e => e is CityFallsToFixtureEvent);
    }

    /// <summary>
    /// DoD 4(b): a non-news-worthy event published in the same turn as a news-worthy one is never
    /// rendered, while the news-worthy one is (N19) — so this cannot pass merely because the writer never
    /// ran at all.
    /// </summary>
    [Fact]
    public void NonNewsworthyEvent_NeverAppears_WhileACompanionNewsworthyEventDoes()
    {
        var coordinator = CoordinatorFor(NewsLogWriterFixtures.NonNewsworthyGroup, out _);
        var state = CoreTestbed.InitialState();

        var result = coordinator.RunTurn(state);

        var texts = result.State.NewsLog.Slots.Select(s => s.Text).ToList();
        Assert.Single(texts);
        Assert.Contains(texts, t => t.Contains("StillRenders", StringComparison.Ordinal));
    }

    /// <summary>
    /// DoD 4(c): a round-scoped event appears at the round boundary in the very same
    /// <see cref="TurnCoordinator.RunRoundTick"/> call that published it -- not deferred to a later seat's
    /// turn (the failure mode the first attempt's reflection-based design had under a realistic sink,
    /// per PR #77's ultra-review addendum). T42 (DoD 3): the round tick's own mandatory blank-line/
    /// week-header pair follows it -- this fixture group's narrow registry (production writers plus its
    /// own fixtures only) never includes <c>CalendarSystem</c>, so the calendar stays at the toy
    /// scenario's own start (week 1, Spring, 270 BC); <c>RoundHeader_ReflectsTheCalendarAfterItsOwnAdvance</c>
    /// below covers the header against a calendar that actually advances.
    /// </summary>
    [Fact]
    public void RoundScopedEvent_AppearsAtRoundBoundary_NotDeferred()
    {
        var coordinator = CoordinatorFor(NewsLogWriterFixtures.RoundScopedGroup, out _);

        var result = coordinator.RunRoundTick(CoreTestbed.InitialState());

        var texts = result.State.NewsLog.Slots.Select(s => s.Text).ToList();
        Assert.Equal(
            new[]
            {
                "A fleet belonging to NavalNation is lost at sea.",
                " ",
                "Week  1      Spring      270BC",
            },
            texts);
    }

    /// <summary>
    /// T42 DoD 3: a full round through the real <see cref="TurnCoordinator"/> -- including the real
    /// <see cref="global::IC2.Engine.Calendar.CalendarSystem"/>, not just the news writers -- ends with the blank entry then
    /// the week header, using the calendar values <em>after</em> that round's own advance (week 1 -> 3,
    /// still Spring, 270 BC unchanged), spacing included.
    /// </summary>
    [Fact]
    public void RoundHeader_ReflectsTheCalendarAfterItsOwnAdvance()
    {
        var registry = SystemRegistry.FromAssemblies(
            new[] { typeof(NewsLogWriterRoundEnd).Assembly },
            type => type == typeof(global::IC2.Engine.Calendar.CalendarSystem) || type == typeof(NewsLogWriterRoundEnd));
        var coordinator = new TurnCoordinator(
            registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

        var result = coordinator.RunRoundTick(CoreTestbed.InitialState());

        var texts = result.State.NewsLog.Slots.Select(s => s.Text).ToList();
        Assert.Equal(new[] { " ", "Week  3      Spring      270BC" }, texts);
    }

    /// <summary>
    /// DoD 4(d): 41 seat-scoped events through 41 real turns leave exactly the ring buffer's own capacity
    /// (<c>caps.maxNewsSlots</c>), oldest evicted -- proven end-to-end through the writer, not only against
    /// <c>NewsLog.Append</c> in isolation (<c>NewsRingBufferTests</c> covers that separately, for DoD 1).
    /// </summary>
    [Fact]
    public void FortyOneTurns_LeaveExactlyCapacity_OldestEvicted()
    {
        var capacity = FixtureCorpus.Get("caps.maxNewsSlots").AsInt();
        var coordinator = CoordinatorFor(NewsLogWriterFixtures.SeatScopedGroup, out _);
        var state = CoreTestbed.InitialState();

        for (var turn = 0; turn < capacity + 1; turn++)
        {
            state = coordinator.RunTurn(state).State;
        }

        var texts = state.NewsLog.Slots.Select(s => s.Text).ToList();
        var expected = Enumerable.Range(1, capacity)
            .Select(i => $"City{i}   (Old)  falls to New.")
            .ToList();
        Assert.Equal(expected, texts);
    }

    /// <summary>
    /// A seat's turn that always requests the round tick renders its seat-scoped event through
    /// <see cref="NewsLogWriterSeatEnd"/> and its round-scoped event through
    /// <see cref="NewsLogWriterRoundEnd"/> -- each exactly once, even though both writers see both events
    /// in <see cref="SystemContext.PublishedEvents"/> by the time <c>RoundEnd</c> runs. Guards specifically
    /// against the scope filter regressing into rendering the same seat-scoped event twice. T42: the round
    /// tick's own mandatory blank-line/week-header pair follows both (this group's registry, like
    /// <see cref="RoundScopedEvent_AppearsAtRoundBoundary_NotDeferred"/>'s, never includes
    /// <c>CalendarSystem</c>, so the header names the toy scenario's own unadvanced start).
    /// </summary>
    [Fact]
    public void FollowOnRoundTick_RendersEachScopedEvent_ExactlyOnce()
    {
        var coordinator = CoordinatorFor(NewsLogWriterFixtures.FollowOnGroup, out _);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        Assert.True(result.RoundTickRan);
        var texts = result.State.NewsLog.Slots.Select(s => s.Text).ToList();
        Assert.Equal(4, texts.Count);
        Assert.Single(texts, t => t.Contains("FollowOnCity", StringComparison.Ordinal));
        Assert.Single(texts, t => t.Contains("FollowOnFleetNation", StringComparison.Ordinal));
        Assert.Equal(new[] { " ", "Week  1      Spring      270BC" }, texts.TakeLast(2));
    }

    /// <summary>
    /// Proves <c>news.writer</c>'s <c>Order = int.MaxValue</c> is load-bearing: a fixture registered in the
    /// same phase (<c>SeatEnd</c>) whose id sorts <em>after</em> "news.writer" alphabetically still has its
    /// event rendered, which is only possible if the writer runs after it despite the id ordering that
    /// would otherwise apply at equal <c>Order</c>.
    /// </summary>
    [Fact]
    public void NewsWriter_SeatEnd_RunsAfterAnEarlierSameNamedSystem()
    {
        var registry = NewsLogWriterFixtures.RegistryFor(NewsLogWriterFixtures.SeatOrderGroup);
        var writer = registry.Systems.Single(s => s.Id == "news.writer");
        Assert.Equal(int.MaxValue, writer.Order);

        var coordinator = new TurnCoordinator(registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        Assert.Contains(result.State.NewsLog.Slots, s => s.Text.Contains("OrderProofCity", StringComparison.Ordinal));
    }

    /// <summary>The same proof as above, for <c>news.writer.round</c> within <c>RoundEnd</c>.</summary>
    [Fact]
    public void NewsWriter_RoundEnd_RunsAfterAnEarlierSameNamedSystem()
    {
        var registry = NewsLogWriterFixtures.RegistryFor(NewsLogWriterFixtures.RoundOrderGroup);
        var writer = registry.Systems.Single(s => s.Id == "news.writer.round");
        Assert.Equal(int.MaxValue, writer.Order);

        var coordinator = new TurnCoordinator(registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

        var result = coordinator.RunRoundTick(CoreTestbed.InitialState());

        Assert.Contains(result.State.NewsLog.Slots, s => s.Text.Contains("RoundOrderProofNation", StringComparison.Ordinal));
    }

    /// <summary>Both writer systems are discoverable by attribute alone, in their declared phases.</summary>
    [Fact]
    public void BothWriterSystems_AreDiscoverableInRegistry()
    {
        var registry = NewsLogWriterFixtures.RegistryFor(NewsLogWriterFixtures.SeatScopedGroup);

        var seatEnd = registry.Systems.Single(s => s.Id == "news.writer");
        var roundEnd = registry.Systems.Single(s => s.Id == "news.writer.round");

        Assert.Equal(TurnPhase.SeatEnd, seatEnd.Phase);
        Assert.Equal(TurnPhase.RoundEnd, roundEnd.Phase);
        Assert.IsType<NewsLogWriterSeatEnd>(seatEnd.Instance);
        Assert.IsType<NewsLogWriterRoundEnd>(roundEnd.Instance);
    }

    /// <summary>
    /// #91 N18: "news.writer"/"news.writer.round" runs last pinned against the <em>whole engine
    /// assembly</em> -- not a narrow test fixture -- so a future <c>SeatEnd</c>/<c>RoundEnd</c> system
    /// also declared at <c>Order = int.MaxValue</c>, with an id that sorts after the writer's ordinally
    /// (<see cref="NewsLogWriterSeatEnd"/>'s remarks on the exact tie-break), fails this test the moment
    /// it is introduced, rather than silently losing its own news that tick.
    /// </summary>
    [Fact]
    public void NewsWriter_IsLast_InSeatEnd_AcrossTheWholeEngineAssembly()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        var seatEnd = registry.InPhase(TurnPhase.SeatEnd);

        Assert.NotEmpty(seatEnd);
        Assert.Equal("news.writer", seatEnd[^1].Id);
    }

    /// <summary>The same pin, for <c>news.writer.round</c> within <c>RoundEnd</c>.</summary>
    [Fact]
    public void NewsWriterRound_IsLast_InRoundEnd_AcrossTheWholeEngineAssembly()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        var roundEnd = registry.InPhase(TurnPhase.RoundEnd);

        Assert.NotEmpty(roundEnd);
        Assert.Equal("news.writer.round", roundEnd[^1].Id);
    }
}
