using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class FleetRallyTests
{
    private static GameSession Build(bool construction = false, int launchMoves = 12)
    {
        var data = CoreTestbed.Toy;
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "north");
        var cells = Enumerable.Repeat(0, 40 * 40).ToArray();
        foreach (var city in initial.State.Cities) cells[city.Y * 40 + city.X] = 2;
        var world = data.World with { Width = 40, Height = 40,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var fleet = initial.State.Fleets.First() with { Id = "new-fleet", Nation = "north", X = 0, Y = 0,
            Moves = launchMoves, ConstructionTicksRemaining = construction ? 1 : null, BuildCityId = "arx", CarriedArmyId = null,
            SupplyTons = 500, ConditionPercent = 100 };
        var state = initial.State with { Armies = ValueList<ArmyState>.Empty, Fleets = ValueList.Of(fleet),
            Nations = ValueList.From(initial.State.Nations.Select(n => n with { Control = SeatControl.Human })) };
        return new(world, data.Ruleset, data.Scenario, new SaveGame(state.SchemaVersion, "rally", "rally",
            state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void Real_launch_binds_only_new_human_fleets_without_moving_or_drawing_rng(int launchMoves)
    {
        var session = Build(construction: true, launchMoves);
        var control = Build(construction: true, launchMoves);
        session.SetRallyPoint(new("north", 30, 30, IsFleet: true));
        session.Submit("end"); session.Submit("end");
        control.Submit("end"); control.Submit("end");
        Assert.Equal(control.State, session.State);
        var launched = session.State.FleetById("new-fleet")!;
        Assert.False(launched.IsUnderConstruction);
        Assert.Equal(launchMoves, launched.Moves);
        Assert.NotEqual((30, 30), (launched.X, launched.Y));
        Assert.Equal(new FleetRallyOrder("new-fleet", "north", 30, 30), Assert.Single(session.FleetRallyOrders));
        Assert.Equal(launchMoves == 0, FleetRallyPlanner.ForOrder(session.State, session.World,
            session.Ruleset, session.FleetRallyOrders[0]).WaitForMoves);
        Assert.Empty(control.FleetRallyOrders);
        var old = Build();
        old.SetRallyPoint(new("north", 30, 30, IsFleet: true));
        old.Submit("end"); old.Submit("end");
        Assert.Empty(old.FleetRallyOrders);
    }

    [Fact]
    public void Army_and_fleet_points_and_orders_survive_save_load_and_undo_without_schema_change()
    {
        var session = Build();
        session.StrategicUndoEnabled = true;
        var armyPoint = new NationRallyPoint("north", 2, 1, "arx");
        var fleetPoint = armyPoint with { IsFleet = true };
        session.SetRallyPoint(armyPoint); session.SetRallyPoint(fleetPoint);
        session.SetFleetRallyOrder(new("new-fleet", "north", 30, 30));
        Assert.Equal(2, session.NationRallyPoints.Count);
        Assert.Equal(armyPoint, session.RallyPointFor("north"));
        Assert.Equal(fleetPoint, session.RallyPointFor("north", true));
        var path = Path.Combine(Path.GetTempPath(), $"ic2-fleet-rally-{Guid.NewGuid():N}.json");
        try
        {
            session.Submit($"save {path}");
            var save = SaveManager.LoadFile(path, session.World, session.Ruleset);
            Assert.Equal(session.State.SchemaVersion, save.SchemaVersion);
            var resumed = new GameSession(session.World, session.Ruleset, session.Scenario, save);
            Assert.Equal(session.NationRallyPoints, resumed.NationRallyPoints);
            Assert.Equal(session.FleetRallyOrders, resumed.FleetRallyOrders);
            session.StrategicUndo();
            Assert.Empty(session.FleetRallyOrders);
            Assert.Equal(2, session.NationRallyPoints.Count);
            session.StrategicUndo();
            Assert.Null(session.RallyPointFor("north", true));
            Assert.Equal(armyPoint, session.RallyPointFor("north"));
            session.Submit($"load {path}");
            Assert.Equal(resumed.FleetRallyOrders, session.FleetRallyOrders);
            Assert.False(session.CanUndoStrategic);
            var legacy = new GameSession(session.World, session.Ruleset, session.Scenario,
                save with { FleetRallyOrders = null, NationRallyPoints = ValueList.Of(armyPoint) });
            Assert.Empty(legacy.FleetRallyOrders);
            Assert.Null(legacy.RallyPointFor("north", true));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Dialog_preview_and_atomic_commit_preserve_fleet_metadata_and_undo_resources()
    {
        var session = Build();
        session.SetRallyPoint(new("north", 30, 30, IsFleet: true));
        session.SetFleetRallyOrder(new("new-fleet", "north", 30, 30));
        session.StrategicUndoEnabled = true;
        var before = session.State;
        var orders = session.FleetRallyOrders;
        var draft = session.BeginDialogEdit(DialogEditKind.Diplomacy);
        var edit = draft.Stage("declare-war south");
        Assert.True(edit.Accepted, string.Join("\n", edit.Output.Lines));
        Assert.Equal(before, session.State);
        Assert.Equal(orders, session.FleetRallyOrders);
        Assert.True(draft.Commit().Accepted);
        Assert.Equal(orders, session.FleetRallyOrders);
        Assert.True(session.CanUndoStrategic);
        session.StrategicUndo();
        Assert.Equal(before, session.State);
        Assert.Equal(orders, session.FleetRallyOrders);
        Assert.True(session.RallyPointFor("north", true)!.IsFleet);
    }

    [Fact]
    public void Planner_obeys_hotseat_danger_and_owned_coastal_city_approaches()
    {
        var session = Build();
        var state = session.State;
        var order = new FleetRallyOrder("new-fleet", "north", 30, 30);
        var before = state;
        Assert.NotNull(FleetRallyPlanner.ForOrder(state, session.World, session.Ruleset, order).CommandLine);
        Assert.True(FleetRallyPlanner.ForOrder(state, session.World, session.Ruleset,
            order with { NationId = "south" }).Reason is not null);
        var otherSeat = state with { ActiveSeatIndex = 1 };
        Assert.True(FleetRallyPlanner.ForOrder(otherSeat, session.World, session.Ruleset, order).WaitForMoves);
        var enemy = state.Fleets[0] with { Id = "enemy", Nation = "south", X = 10, Y = 0 };
        var danger = state with { Fleets = ValueList.From(state.Fleets.Append(enemy)),
            Relations = state.Relations.WithRelation("north", "south", session.Ruleset.Diplomacy.StateCodes.War) };
        Assert.Contains("enemy fleet", FleetRallyPlanner.ForOrder(danger, session.World, session.Ruleset, order).Reason!);
        var city = state.CityById("arx")!;
        var cityOrder = order with { X = city.X, Y = city.Y, CityId = city.Id };
        var cityDecision = FleetRallyPlanner.ForOrder(state, session.World, session.Ruleset, cityOrder);
        Assert.NotNull(cityDecision.CommandLine);
        var coordinates = cityDecision.CommandLine!.Split(' ');
        Assert.NotEqual((city.X, city.Y), (int.Parse(coordinates[2]), int.Parse(coordinates[3])));
        var captured = state with { Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id ? c with { Owner = "south" } : c)) };
        Assert.Contains("changed owner", FleetRallyPlanner.ForOrder(captured, session.World, session.Ruleset, cityOrder).Reason!);
        Assert.Same(before, session.State);
    }
}
