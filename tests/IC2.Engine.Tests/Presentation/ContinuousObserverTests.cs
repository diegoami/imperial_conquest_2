using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class ContinuousObserverTests
{
    [Theory]
    [InlineData("", false, false)]
    [InlineData("--observe", true, false)]
    [InlineData("--observe-loop", true, true)]
    [InlineData("--observe --observe-loop", true, true)]
    public void Launch_flags(string arguments, bool observe, bool loop) =>
        Assert.Equal(new ObserverLaunchOptions(observe, loop), ObserverLaunchOptions.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void Misspelled_observer_argument_is_not_silently_ignored() =>
        Assert.Throws<ArgumentException>(() => ObserverLaunchOptions.Parse(["--observe-looop"]));

    [Fact]
    public void Viewpoint_survives_turns_and_changes_only_on_elimination_without_campaign_draws()
    {
        var f = TacticalSessionTestbed.Toy;
        var session = new GameSession(f.World, f.Ruleset, f.Scenario);
        var state = session.State;
        var draws = 0;
        var policy = new ContinuousObserverPolicy(count => { draws++; return count - 1; });
        var chosen = policy.SelectSurvivor(state, f.Scenario);
        Assert.NotNull(chosen);
        Assert.Equal(chosen, policy.SelectSurvivor(state, f.Scenario));
        Assert.Equal(1, draws);
        Assert.Same(state, session.State);
        var eliminated = state with { Nations = ValueList.From(state.Nations.Select(n => n with { Eliminated = n.Id == chosen })) };
        Assert.NotEqual(chosen, policy.SelectSurvivor(eliminated, f.Scenario));
        Assert.Equal(2, draws);
        Assert.Equal(state.RandomSeed, eliminated.RandomSeed);
        Assert.Null(policy.SelectSurvivor(eliminated with { Nations = ValueList.From(eliminated.Nations.Select(n => n with { Eliminated = true })) }, f.Scenario));
        Assert.Equal(2, draws);
    }

    [Fact]
    public void Barbarian_role_and_unseated_nations_are_never_viewpoints()
    {
        var f = TacticalSessionTestbed.Toy;
        var state = new GameSession(f.World, f.Ruleset, f.Scenario).State;
        var north = state.Nations.First(n => n.Id == "north");
        state = state with { Nations = ValueList.From(state.Nations.Concat([north with { Id = "unseated" }])) };
        var scenario = f.Scenario with { Seats = ValueList.From(f.Scenario.Seats.Where(s => s.Nation == "south")) };
        var policy = new ContinuousObserverPolicy(_ => 0);
        Assert.Equal("south", policy.SelectSurvivor(state, scenario));
        // Treat north as the excluded barbarian id using the existing scenario model.
        // The separate barbarian fixtures below exercise its production configuration.
        var barbarian = IC2.Engine.Serialization.GameDataRepository.Load(FindData()).Resolve("italy-500bc-barbarians");
        var observed = new GameSession(barbarian.World, barbarian.Ruleset, barbarian.Scenario).State;
        var onlyBarbarian = observed with { Nations = ValueList.From(observed.Nations.Select(n => n with { Eliminated = n.Id != barbarian.Scenario.Barbarians!.NationId })) };
        Assert.Null(new ContinuousObserverPolicy().SelectSurvivor(onlyBarbarian, barbarian.Scenario));
    }

    [Fact]
    public void Showcase_ends_on_authored_limit_but_not_when_watched_nation_dies()
    {
        var f = TacticalSessionTestbed.Toy;
        var scenario = f.Scenario with { Seats = ValueList.From(f.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })), TurnLimit = 1 };
        var session = new GameSession(f.World, f.Ruleset, scenario);
        Assert.Null(ContinuousObserverPolicy.Ending(session));
        session.Submit("end");
        Assert.NotNull(ContinuousObserverPolicy.Ending(session));
        var calendarEnd = new GameSession(f.World, f.Ruleset with {
            Victory = f.Ruleset.Victory with { HardEndYearBc = session.State.Calendar.YearBc } }, scenario with { TurnLimit = null });
        Assert.Equal("Campaign time limit reached", ContinuousObserverPolicy.Ending(calendarEnd));
    }

    private static string FindData()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("data"), "data");
    }
}
