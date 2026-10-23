using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class ArmyTravelPlannerTests
{
    private static (GameSession Session, ArmyState Army) Setup()
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        return (session, session.State.Armies.First(a => a.Nation == "rome" && !a.IsEmbarked));
    }

    [Fact]
    public void Planner_arrives_at_fixed_cell_and_adjacent_city_without_mutating_state()
    {
        var (session, army) = Setup();
        var state = session.State;
        var here = new ArmyTravelTarget(army.Id, army.Nation, army.X, army.Y);
        var atCell = ArmyTravelPlanner.ForTarget(state, session.World, session.Ruleset, here);
        Assert.True(atCell.Arrived);
        Assert.Null(atCell.CommandLine);

        var city = session.World.Cities.First();
        var besideCity = state with { Armies = ValueList.From(state.Armies.Select(a => a.Id == army.Id
            ? a with { X = city.X + 1, Y = city.Y } : a)) };
        var cityTarget = new ArmyTravelTarget(army.Id, army.Nation, city.X, city.Y, city.Id);
        Assert.True(ArmyTravelPlanner.ForTarget(besideCity, session.World, session.Ruleset, cityTarget).Arrived);
        Assert.Equal(state, session.State);
        Assert.Equal(state.RandomSeed, session.State.RandomSeed);
    }

    [Fact]
    public void Invalid_and_foreign_targets_stop_without_a_command()
    {
        var (session, army) = Setup();
        var target = new ArmyTravelTarget(army.Id, army.Nation, army.X, army.Y);
        string Reason(GameState state, ArmyTravelTarget plan) =>
            ArmyTravelPlanner.ForTarget(state, session.World, session.Ruleset, plan).Reason ?? string.Empty;
        Assert.Contains("missing", Reason(session.State, target with { ArmyId = "missing" }));
        Assert.Contains("changed nation", Reason(session.State, target with { NationId = "gaul" }));
        Assert.Contains("outside", Reason(session.State, target with { X = -1 }));
        Assert.Contains("missing", Reason(session.State, target with { CityId = "missing" }));
        Assert.Contains("embarked", Reason(session.State with { Armies = ValueList.From(session.State.Armies.Select(a =>
            a.Id == army.Id ? a with { AboardFleetId = "fleet" } : a)) }, target));
    }

    [Fact]
    public void Campaign_target_survives_native_save_and_legacy_save_has_none()
    {
        var (session, army) = Setup();
        Assert.Empty(session.ArmyTravelTargets);
        var target = new ArmyTravelTarget(army.Id, army.Nation, army.X + 2, army.Y);
        session.SetArmyTravelTarget(target);
        var path = Path.Combine(Path.GetTempPath(), $"ic2-travel-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Contains(session.Submit($"save {path}").Lines, line => line.StartsWith("Saved to", StringComparison.Ordinal));
            var save = SaveManager.LoadFile(path, session.World, session.Ruleset);
            var resumed = new GameSession(session.World, session.Ruleset, session.Scenario, save);
            Assert.Equal(target, resumed.TravelTargetFor(army.Id));
            session.ClearArmyTravelTarget(army.Id);
            Assert.Contains(session.Submit($"load {path}").Lines, line => line.StartsWith("Loaded", StringComparison.Ordinal));
            Assert.Equal(target, session.TravelTargetFor(army.Id));
            Assert.Empty(new GameSession(session.World, session.Ruleset, session.Scenario,
                save with { ArmyTravelTargets = null }).ArmyTravelTargets);
            resumed.ClearArmyTravelTarget(army.Id);
            Assert.Empty(resumed.ArmyTravelTargets);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
