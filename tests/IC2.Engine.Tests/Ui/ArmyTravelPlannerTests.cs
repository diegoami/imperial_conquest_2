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

    [Theory]
    [InlineData(false, 1, true, true)]
    [InlineData(true, 1, true, true)]
    [InlineData(false, 2, true, false)]
    [InlineData(true, 2, true, false)]
    [InlineData(false, 1, false, false)]
    [InlineData(true, 1, false, false)]
    public void Travel_stops_only_for_nearby_hostile_army_or_city(bool cityContact, int distance,
        bool atWar, bool shouldStop)
    {
        var (session, original) = Setup();
        var army = original with { X = 10, Y = 10, Moves = 8, CoveredTileCode = 2, AboardFleetId = null };
        var world = session.World with { Width = 32, Height = 32,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 32 * 32))) };
        var enemy = army with { Id = "contact", Nation = "seleucid", X = 10 + distance };
        var city = session.State.Cities[0] with { Owner = "seleucid", X = 10 + distance, Y = 10 };
        var state = session.State with
        {
            Armies = cityContact ? ValueList.Of(army) : ValueList.Of(army, enemy),
            Cities = cityContact ? ValueList.Of(city) : ValueList<CityState>.Empty,
            Relations = session.State.Relations.WithRelation("rome", "seleucid", atWar
                ? session.Ruleset.Diplomacy.StateCodes.War : session.Ruleset.Diplomacy.StateCodes.Peace),
        };
        var target = new ArmyTravelTarget(army.Id, army.Nation, 20, 20);
        var decision = ArmyTravelPlanner.ForTarget(state, world, session.Ruleset, target);

        Assert.Equal(shouldStop ? "hostile contact" : null, decision.Reason);
        Assert.Equal(shouldStop ? null : $"move {army.Id} 20 20", decision.CommandLine);
        Assert.False(decision.WaitForMoves);
        Assert.False(decision.Arrived);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Travel_waits_for_moves_without_clearing_the_campaign_target(int moves, bool waiting)
    {
        var (session, original) = Setup();
        var army = original with { X = 10, Y = 10, Moves = moves, CoveredTileCode = 2, AboardFleetId = null };
        var world = session.World with { Width = 32, Height = 32,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 32 * 32))) };
        var state = session.State with { Armies = ValueList.Of(army), Cities = ValueList<CityState>.Empty };
        var target = new ArmyTravelTarget(army.Id, army.Nation, 20, 20);
        session.SetArmyTravelTarget(target);
        var before = session.State;

        var decision = ArmyTravelPlanner.ForTarget(state, world, session.Ruleset, target);

        Assert.Equal(waiting, decision.WaitForMoves);
        Assert.Equal(waiting ? null : $"move {army.Id} 20 20", decision.CommandLine);
        Assert.Null(decision.Reason);
        Assert.False(decision.Arrived);
        Assert.Equal(target, session.TravelTargetFor(army.Id));
        Assert.Same(before, session.State);
    }

    [Fact]
    public void Rally_city_ownership_loss_stops_only_the_rally_created_target()
    {
        var (session, original) = Setup();
        var city = session.World.Cities.First();
        var army = original with { X = city.X + 5, Y = city.Y, Moves = 8 };
        var state = session.State with { Armies = ValueList.Of(army),
            Cities = ValueList.Of(session.State.CityById(city.Id)! with { Owner = "seleucid" }) };
        var rally = new ArmyTravelTarget(army.Id, army.Nation, city.X, city.Y, city.Id, FromRally: true);

        var stopped = ArmyTravelPlanner.ForTarget(state, session.World, session.Ruleset, rally);
        Assert.Equal("rally city changed owner", stopped.Reason);
        Assert.Null(stopped.CommandLine);
        Assert.False(stopped.WaitForMoves);
        var manual = ArmyTravelPlanner.ForTarget(state, session.World, session.Ruleset, rally with { FromRally = false });
        Assert.NotNull(manual.CommandLine);
    }

    [Fact]
    public void Rally_points_and_origin_survive_constructor_and_live_load_with_legacy_defaults()
    {
        var (session, army) = Setup();
        NationRallyPoint Point(string nation) => session.State.CityById(session.State.NationById(nation)!.CapitalCityId!)
            is { } city ? new(nation, city.X, city.Y, city.Id) : throw new InvalidOperationException();
        var rome = Point("rome");
        var gaul = Point("gaul");
        session.SetRallyPoint(rome);
        session.SetRallyPoint(gaul);
        var target = new ArmyTravelTarget(army.Id, army.Nation, rome.X, rome.Y, rome.CityId, FromRally: true);
        session.SetArmyTravelTarget(target);
        var path = Path.Combine(Path.GetTempPath(), $"ic2-rally-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Contains(session.Submit($"save {path}").Lines, line => line.StartsWith("Saved to", StringComparison.Ordinal));
            var save = SaveManager.LoadFile(path, session.World, session.Ruleset);
            var resumed = new GameSession(session.World, session.Ruleset, session.Scenario, save);
            Assert.Equal(rome, resumed.RallyPointFor("rome"));
            Assert.Equal(gaul, resumed.RallyPointFor("gaul"));
            Assert.Equal(target, resumed.TravelTargetFor(army.Id));
            session.ClearRallyPoint("rome");
            session.ClearRallyPoint("gaul");
            Assert.Contains(session.Submit($"load {path}").Lines, line => line.StartsWith("Loaded", StringComparison.Ordinal));
            Assert.Equal(rome, session.RallyPointFor("rome"));
            Assert.Equal(gaul, session.RallyPointFor("gaul"));
            resumed.SetRallyPoint(rome with { CityId = null, X = army.X, Y = army.Y });
            Assert.Equal(2, resumed.NationRallyPoints.Count);
            Assert.Equal(gaul, resumed.RallyPointFor("gaul"));
            Assert.True(resumed.ClearRallyPoint("rome"));
            Assert.Equal(gaul, Assert.Single(resumed.NationRallyPoints));
            Assert.Empty(new GameSession(session.World, session.Ruleset, session.Scenario,
                save with { NationRallyPoints = null }).NationRallyPoints);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
