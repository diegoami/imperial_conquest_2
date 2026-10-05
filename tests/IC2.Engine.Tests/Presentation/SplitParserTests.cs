using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T141.md</c> Done-when 1: the <c>split-army</c> parser takes a comma-separated unit list
/// plus optional <c>supply=</c>/<c>money=</c>, and <c>split-fleet</c> takes the same options, each at most
/// once and whole and non-negative; every malformed form prints the usage line and dispatches nothing, so
/// no new army or fleet is created.
/// </summary>
/// <remarks>
/// The parse is checked through a real <see cref="GameSession"/>, whose public <see cref="GameSession.State"/>
/// is then inspected: an accepted line proves the option reached the handler, not merely that the parser
/// did not reject it. The fixtures use 100,000-troop units and a 300-talent/300-ton fleet so the army
/// rebalance stays under both armies' <c>troops div 100</c> capacities and cannot obscure the values the
/// command actually carried.
/// </remarks>
public sealed class SplitParserTests
{
    private const string UsageArmy =
        "Usage: split-army <army> <new-army> <i,j,...> [supply=<tons>] [money=<talents>]";

    private const string UsageFleet =
        "Usage: split-fleet <fleet> <new-fleet> <ships> [supply=<tons>] [money=<talents>]";

    private static GameSession Session()
    {
        var toy = CoreTestbed.Toy;
        var initial = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);

        var parent = new ArmyState(
            "a", "north", 3, 3, Moves: 5, Morale: 60, Money: 200, SupplyTons: 60, CoveredTileCode: 2,
            AboardFleetId: null,
            Units: ValueList.Of(
                new UnitSlot(0, "light_infantry", 100_000, 6, "A0"),
                new UnitSlot(0, "light_infantry", 100_000, 6, "A1"),
                new UnitSlot(0, "light_infantry", 100_000, 6, "A2")));

        var fleet = new FleetState(
            "f", "north", 3, 5, Moves: 5, Ships: 30, ConditionPercent: 90, Money: 300, SupplyTons: 300,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

        var state = initial with { Armies = ValueList.Of(parent), Fleets = ValueList.Of(fleet) };
        var save = new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "141-parse-probe",
            Label: "T141 parser probe",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        return new GameSession(toy.World, toy.Ruleset, toy.Scenario, save);
    }

    [Fact]
    public void Split_army_with_a_unit_list_and_both_options_dispatches_exactly()
    {
        var session = Session();

        var output = session.Submit("split-army a n 0,2 supply=50 money=100");

        Assert.Contains("armies.split-army accepted.", output.Lines);
        Assert.DoesNotContain(UsageArmy, output.Lines);

        var parent = session.State.ArmyById("a")!;
        var created = session.State.ArmyById("n")!;
        Assert.Equal(2, created.Units.Count); // indexes 0 and 2 moved.
        Assert.Equal(100, created.Money);
        Assert.Equal(50, created.SupplyTons);
        Assert.Equal(200 - 100, parent.Money);
        Assert.Equal(60 - 50, parent.SupplyTons);
    }

    [Fact]
    public void Split_army_with_a_single_index_still_dispatches_zero_and_zero()
    {
        var session = Session();

        var output = session.Submit("split-army a n 1");

        Assert.Contains("armies.split-army accepted.", output.Lines);
        Assert.DoesNotContain(UsageArmy, output.Lines);

        var created = session.State.ArmyById("n")!;
        Assert.Single(created.Units); // index 1 only.
        Assert.Equal(0, created.Money);
        Assert.Equal(0, created.SupplyTons);
        Assert.Equal(60, session.State.ArmyById("a")!.SupplyTons);
    }

    [Theory]
    [InlineData("split-army a n 0 supply=50 supply=60", "a repeated option")]
    [InlineData("split-army a n 0 money=10 money=20", "a repeated option")]
    [InlineData("split-army a n 0 supply=x", "a non-numeric supply")]
    [InlineData("split-army a n 0 money=x", "a non-numeric money")]
    [InlineData("split-army a n 0 supply=-1", "a negative supply")]
    [InlineData("split-army a n 0 money=-1", "a negative money")]
    [InlineData("split-army a n 0 money=", "an empty value")]
    [InlineData("split-army a n 0,", "an empty list item")]
    [InlineData("split-army a n ,0", "an empty list item")]
    [InlineData("split-army a n 0,,2", "an empty list item")]
    [InlineData("split-army a n", "a missing index")]
    [InlineData("split-army a n 0 bogus=1", "an unknown key")]
    public void Malformed_split_army_prints_the_usage_line_and_dispatches_nothing(string line, string because)
    {
        var session = Session();

        var output = session.Submit(line);

        Assert.True(
            output.Lines.Contains(UsageArmy),
            $"'{line}' should print the usage line ({because}); it printed: {string.Join(" | ", output.Lines)}");
        Assert.Null(session.State.ArmyById("n")); // nothing dispatched.
    }

    [Fact]
    public void Split_fleet_with_both_options_dispatches_exactly()
    {
        var session = Session();

        var output = session.Submit("split-fleet f n 10 supply=40 money=30");

        Assert.Contains("naval.split-fleet accepted.", output.Lines);
        Assert.DoesNotContain(UsageFleet, output.Lines);

        var parent = session.State.FleetById("f")!;
        var created = session.State.FleetById("n")!;
        Assert.Equal(10, created.Ships);
        Assert.Equal(40, created.SupplyTons);
        Assert.Equal(30, created.Money);
        Assert.Equal(20, parent.Ships);
        Assert.Equal(260, parent.SupplyTons);
        Assert.Equal(270, parent.Money);
    }

    [Fact]
    public void Split_fleet_with_no_options_dispatches_zero_and_zero()
    {
        var session = Session();

        var output = session.Submit("split-fleet f n 10");

        Assert.Contains("naval.split-fleet accepted.", output.Lines);
        Assert.DoesNotContain(UsageFleet, output.Lines);

        var created = session.State.FleetById("n")!;
        Assert.Equal(10, created.Ships);
        Assert.Equal(0, created.SupplyTons);
        Assert.Equal(0, created.Money);
    }

    [Theory]
    [InlineData("split-fleet f n 10 supply=40 supply=40")]
    [InlineData("split-fleet f n 10 supply=-1")]
    [InlineData("split-fleet f n 10 money=x")]
    [InlineData("split-fleet f n 10 bogus=1")]
    public void Malformed_split_fleet_prints_the_usage_line_and_dispatches_nothing(string line)
    {
        var session = Session();

        var output = session.Submit(line);

        Assert.Contains(UsageFleet, output.Lines);
        Assert.Null(session.State.FleetById("n")); // nothing dispatched.
    }
}
