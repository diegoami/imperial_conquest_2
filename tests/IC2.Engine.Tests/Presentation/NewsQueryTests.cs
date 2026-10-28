using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class NewsQueryTests
{
    [Fact]
    public void Reading_news_does_not_consume_a_pending_seat_fall_or_publish_events()
    {
        var data = CoreTestbed.Toy;
        var ruleset = data.Ruleset with { Calendar = data.Ruleset.Calendar with {
            StartYearBc = data.Ruleset.Victory.HardEndYearBc } };
        var session = new GameSession(data.World, ruleset, data.Scenario);
        var before = session.State;
        var events = 0;
        session.EventsPublished += _ => events++;

        Assert.NotEmpty(session.ReadNewsLines());
        Assert.NotEmpty(session.ReadNewsLines());
        Assert.Same(before, session.State);
        Assert.Equal(0, events);
        var output = session.Submit("status");
        Assert.Equal(SeatFallReason.HardEndYear, Assert.Single(output.SeatFalls).Reason);
    }

    [Fact]
    public void Reading_news_preserves_the_first_end_prelude_and_owed_news()
    {
        var data = CoreTestbed.Toy;
        var control = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var viewed = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var before = viewed.State;
        viewed.ReadNewsLines();
        viewed.ReadNewsLines();
        Assert.Same(before, viewed.State);
        Assert.Equal(control.Submit("end").Lines, viewed.Submit("end").Lines);
        Assert.Equal(control.State, viewed.State);
    }
}
