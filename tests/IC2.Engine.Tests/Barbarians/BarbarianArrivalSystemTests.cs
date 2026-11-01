using System.Text.Json;
using IC2.Engine.Barbarians;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Barbarians;

public sealed class BarbarianArrivalSystemTests
{
    internal static (World World, Ruleset Rules, Scenario Scenario, GameState State) Fixture(bool sea = false)
    {
        var toy = ToyFixtures.Toy;
        var nations = toy.World.Nations.Select(n => n with { CapitalCityId = null, RecruitmentSlots = null }).ToList();
        nations.Add(nations[0] with { Id = "raiders", Name = "Barbarians", LeaderNames = null });
        var rows = sea ? new[] { "sss", "sls", "sss" } : new[] { "lll", "lll", "lll" };
        var world = toy.World with
        {
            Width = 3, Height = 3, Nations = new(nations),
            TurnOrder = new(toy.World.TurnOrder.Append("raiders")),
            Cities = ValueList<CityDefinition>.Empty, StartingArmies = ValueList<StartingArmy>.Empty,
            StartingFleets = ValueList<StartingFleet>.Empty,
            Terrain = new(TerrainEncoding.RunLength, new(rows.SelectMany(r => r.Select(c => new TerrainRun(c == 's' ? 0 : 1, 1))))),
            TileTypes = ValueList.Of(new TileType("sea", 0, "Sea", false, true), new TileType("land", 1, "Land", true, false)),
        };
        var scenario = toy.Scenario with
        {
            Seats = new(toy.Scenario.Seats.Append(new Seat("raiders", SeatControl.Ai))),
            Barbarians = new("raiders", ValueList.Of(new UnitSlot(0, "light_infantry", 1000, 6, "Raiders")),
                FleetShips: 10, ChancePercent: 100),
        };
        return (world, toy.Ruleset, scenario, GameStateFactory.CreateInitial(world, toy.Ruleset, scenario));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Arrivals_are_deterministic_legal_fully_supplied_and_atomic(bool sea)
    {
        var f = Fixture(sea);
        var sink = new RecordingEventSink();
        var state = BarbarianArrivalSystem.Apply(f.State, f.World, f.Rules, f.Scenario, SplitMix64Rng.ForStream(42, "arrival"), sink);
        var repeat = BarbarianArrivalSystem.Apply(f.State, f.World, f.Rules, f.Scenario, SplitMix64Rng.ForStream(42, "arrival"), new RecordingEventSink());
        Assert.Equal(state, repeat);
        var army = Assert.Single(state.Armies);
        Assert.True(army.X is 0 or 2 || army.Y is 0 or 2);
        Assert.Equal(0, army.Moves);
        Assert.Equal(SupplyCapacity.ArmyCapacityTons(army.TotalTroops, f.Rules), army.SupplyTons);
        Assert.Equal(0, state.NationById("raiders")!.LastBarbarianArrivalTurn);
        var entered = Assert.Single(sink.Events.OfType<BarbarianArrivalEntered>());
        Assert.Equal(sea ? "by sea" : "by land", entered.Approach);
        var news = NewsLogWriter.Append(state, sink.Events, f.Rules.NewsLog).NewsLog.Slots.Last().Text;
        Assert.Equal($"Barbarian forces arrive {entered.Approach} at ({entered.X}, {entered.Y}).", news);
        if (sea)
        {
            var fleet = Assert.Single(state.Fleets);
            Assert.Equal(fleet.Id, army.AboardFleetId);
            Assert.Equal(army.Id, fleet.CarriedArmyId);
            Assert.Equal((fleet.X, fleet.Y), (army.X, army.Y));
            Assert.Null(army.CoveredTileCode);
            Assert.Equal(SupplyCapacity.FleetCapacityTons(fleet.Ships, f.Rules), fleet.SupplyTons);
            Assert.False(IC2.Engine.Naval.ArmyTransportCapacity.Exceeds(army.TotalTroops, fleet.Ships, f.Rules, SeatControl.Ai));
        }
        else
        {
            Assert.Empty(state.Fleets);
            Assert.False(army.IsEmbarked);
            Assert.Equal(1, army.CoveredTileCode);
        }
        GameDataValidation.Validate("test", state);
    }

    [Fact]
    public void Attempt_marker_survives_native_reload_and_duplicate_callback_draws_nothing()
    {
        var f = Fixture();
        var state = BarbarianArrivalSystem.Apply(f.State, f.World, f.Rules, f.Scenario, SplitMix64Rng.ForStream(1, "arrival"), new RecordingEventSink());
        state = JsonSerializer.Deserialize<GameState>(GameJson.Serialize(state), GameJson.Options)!;
        var rng = SplitMix64Rng.ForStream(2, "arrival");
        var before = rng.State;
        Assert.Same(state, BarbarianArrivalSystem.Apply(state, f.World, f.Rules, f.Scenario, rng, new RecordingEventSink()));
        Assert.Equal(before, rng.State);
        var later = state with { Calendar = state.Calendar with { TurnIndex = 6 } };
        var next = BarbarianArrivalSystem.Apply(later, f.World, f.Rules, f.Scenario, rng, new RecordingEventSink());
        Assert.Equal(2, next.Armies.Count);
        Assert.Equal(6, next.NationById("raiders")!.LastBarbarianArrivalTurn);
        Assert.Equal(2, next.Armies.Select(a => a.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("chance")]
    [InlineData("cap")]
    [InlineData("blocked")]
    public void Failed_attempt_is_recorded_without_forced_spawn_or_repeat_roll(string reason)
    {
        var f = Fixture();
        var scenario = reason == "chance" ? f.Scenario with { Barbarians = f.Scenario.Barbarians! with { ChancePercent = 0 } } : f.Scenario;
        var state = f.State;
        if (reason == "cap") state = state with { Armies = new(Enumerable.Range(0, 4).Select(i => new ArmyState($"a{i}", "raiders", i % 3, i / 3, 0, 70, 0, 10, 1, null, scenario.Barbarians!.ArrivalUnits))) };
        if (reason == "blocked")
            f.World = f.World with { TileTypes = ValueList.Of(new TileType("rock", 1, "Rock", false, false)) };
        var rng = SplitMix64Rng.ForStream(1, "arrival");
        var seed = rng.State;
        var sink = new RecordingEventSink();
        var after = BarbarianArrivalSystem.Apply(state, f.World, f.Rules, scenario, rng, sink);
        Assert.Equal(state.Armies, after.Armies);
        Assert.Equal(state.Fleets, after.Fleets);
        Assert.Equal(0, after.NationById("raiders")!.LastBarbarianArrivalTurn);
        Assert.Equal(seed, rng.State);
        Assert.Empty(sink.Events);
        Assert.Same(after, BarbarianArrivalSystem.Apply(after, f.World, f.Rules, scenario, rng, sink));
    }

    [Fact]
    public void Disabled_campaign_is_reference_identical_and_consumes_no_draws()
    {
        var f = ToyFixtures.Toy;
        var state = GameStateFactory.CreateInitial(f.World, f.Ruleset, f.Scenario);
        var rng = SplitMix64Rng.ForStream(1, "arrival");
        var seed = rng.State;
        Assert.Same(state, BarbarianArrivalSystem.Apply(state, f.World, f.Ruleset, f.Scenario, rng, new RecordingEventSink()));
        Assert.Equal(seed, rng.State);
    }
}
