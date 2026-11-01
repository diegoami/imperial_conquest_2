using System.Text.Json;
using System.Text.Json.Nodes;
using IC2.Engine.Barbarians;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Naval.Commands;
using IC2.Slice.UI;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using IC2.Engine.Victory;
using Xunit;

namespace IC2.Engine.Tests.Barbarians;

public sealed class BarbarianFactionTests
{
    private static (World World, Ruleset Rules, Scenario Scenario, GameState State) Fixture()
    {
        var toy = ToyFixtures.Toy;
        var faction = toy.World.Nations[0] with
        {
            Id = "raiders", Name = "Raiders", CapitalCityId = null,
            RecruitmentSlots = null, LeaderNames = null,
        };
        var world = toy.World with
        {
            Nations = new(toy.World.Nations.Append(faction)),
            TurnOrder = new(toy.World.TurnOrder.Append(faction.Id)),
        };
        var config = new BarbarianScenario("raiders", ValueList.Of(new UnitSlot(0,
            "light_infantry", 100, toy.Ruleset.Combat.QualityFloor, "Raiders")),
            Math.Max(1, (100 + toy.Ruleset.Naval.TransportTroopsPerShip - 1) / toy.Ruleset.Naval.TransportTroopsPerShip));
        var scenario = toy.Scenario with
        {
            Seats = new(toy.Scenario.Seats.Append(new Seat("raiders", SeatControl.Ai))), Barbarians = config,
        };
        return (world, toy.Ruleset, scenario, GameStateFactory.CreateInitial(world, toy.Ruleset, scenario));
    }

    [Fact]
    public void Cityless_faction_is_active_capitalless_AI_and_always_hostile_both_ways()
    {
        var f = Fixture();
        var nation = f.State.NationById("raiders")!;
        Assert.True(nation.IsBarbarian);
        Assert.False(nation.Eliminated);
        Assert.Null(nation.CapitalCityId);
        Assert.Contains("raiders", f.State.TurnOrder);
        Assert.DoesNotContain(GameSessionFactory.SeatOptionsFor(new(f.Scenario, f.World, f.Rules)), s => s.NationId == "raiders");
        foreach (var other in f.State.Nations.Where(n => n.Id != nation.Id))
        {
            Assert.Equal(f.Rules.Diplomacy.StateCodes.War, f.State.Relations.Get(nation.Id, other.Id));
            Assert.Equal(f.Rules.Diplomacy.StateCodes.War, f.State.Relations.Get(other.Id, nation.Id));
        }
        Assert.Same(f.State, BarbarianFaction.EnforceHostility(f.State, f.Rules));
        Assert.Throws<ArgumentException>(() => new GameSession(f.World, f.Rules, f.Scenario, humanSeatNationId: "raiders"));
    }

    [Theory]
    [InlineData("human")]
    [InlineData("capital")]
    [InlineData("unknown")]
    [InlineData("overcapacity")]
    [InlineData("quality")]
    [InlineData("chance")]
    public void Invalid_scenario_role_is_rejected_at_resolve_and_factory(string fault)
    {
        var f = Fixture();
        var scenario = f.Scenario;
        var world = f.World;
        if (fault == "human") scenario = scenario with { Seats = new(scenario.Seats.Select(s => s.Nation == "raiders" ? s with { Control = SeatControl.Human } : s)) };
        if (fault == "capital") world = world with { Nations = new(world.Nations.Select(n => n.Id == "raiders" ? n with { CapitalCityId = "arx" } : n)) };
        if (fault == "unknown") scenario = scenario with { Barbarians = scenario.Barbarians! with { NationId = "absent" } };
        if (fault == "overcapacity") scenario = scenario with { Barbarians = scenario.Barbarians! with { ArrivalUnits = ValueList.Of(scenario.Barbarians.ArrivalUnits[0] with { Troops = int.MaxValue }) } };
        if (fault == "quality") scenario = scenario with { Barbarians = scenario.Barbarians! with { ArrivalUnits = ValueList.Of(scenario.Barbarians.ArrivalUnits[0] with { Quality = f.Rules.Combat.QualityCap + 1 }) } };
        if (fault == "chance") scenario = scenario with { Barbarians = scenario.Barbarians! with { ChancePercent = 101 } };
        Assert.Throws<ArgumentException>(() => GameStateFactory.CreateInitial(world, f.Rules, scenario));
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.ValidateResolved(scenario, world, f.Rules));
    }

    [Fact]
    public void Diplomacy_is_refused_before_any_effects_in_either_direction()
    {
        var f = Fixture();
        var sink = new RecordingEventSink();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, sink);
        var commands = new ICommand[]
        {
            new MakePeaceCommand("north", "raiders"), new ProposeTradeCommand("north", "raiders"),
            new ProposeAllianceCommand("north", "raiders"), new AiFormTradeCommand("north", "raiders"),
            new AiFormAllianceCommand("north", "raiders"), new MakePeaceCommand("raiders", "north"),
        };
        foreach (var command in commands)
        {
            var state = f.State with { ActiveSeatIndex = f.State.TurnOrder.ToList().IndexOf(command.IssuingNationId) };
            var result = dispatch.Dispatch(state, command);
            Assert.False(result.IsAccepted);
            Assert.Equal(BarbarianFaction.NoDiplomacy, result.Rejection!.Code);
            Assert.Same(state, result.State);
        }
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void Quarterly_thaw_skips_faction_pairs_and_hostility_is_restored_after_reset()
    {
        var f = Fixture();
        var state = f.State with { Relations = f.State.Relations.WithRelation("north", "raiders", -3) };
        var thawed = QuarterlyThawSystem.Apply(state, f.Rules, SplitMix64Rng.ForStream(42, "thaw"));
        Assert.Equal(-3, thawed.Relations.Get("north", "raiders"));
        var restored = BarbarianFaction.EnforceHostility(thawed, f.Rules);
        Assert.Equal(f.Rules.Diplomacy.StateCodes.War, restored.Relations.Get("north", "raiders"));
    }

    [Fact]
    public void Barbarian_wars_do_not_block_regular_alliance_but_remain_military_wars()
    {
        var f = Fixture();
        var state = f.State with
        {
            ActiveSeatIndex = f.State.TurnOrder.ToList().IndexOf("north"),
            Nations = new(f.State.Nations.Select(n => n.Id == "north" ? n with { Control = SeatControl.Human, MobilizedPercent = 0 } : n)),
        };
        Assert.True(RelationTransitions.IsAtWarWithAnyone(state, f.Rules, "north"));
        Assert.False(RelationTransitions.IsAtWarWithAnyone(state, f.Rules, "north", includeBarbarians: false));
        Assert.False(AiOwnDiplomacyRule.IsBusy(state, f.Rules, "north"));
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, new RecordingEventSink());
        var alliance = dispatch.Dispatch(state, new ProposeAllianceCommand("north", "south"));
        Assert.True(alliance.IsAccepted, alliance.Rejection?.Message);
        Assert.Equal(f.Rules.Diplomacy.StateCodes.Alliance, alliance.State.Relations.Get("north", "south"));
        Assert.False(RelationTransitions.HasAnAllyAtWarWithAnyone(alliance.State, f.Rules, "north"));
        Assert.Equal(f.Rules.Diplomacy.StateCodes.War, alliance.State.Relations.Get("north", "raiders"));
        var offered = state with { PendingOffer = new("south", f.Rules.Diplomacy.StateCodes.Alliance) };
        var accepted = dispatch.Dispatch(offered, new AcceptPendingOfferCommand("north"));
        Assert.True(accepted.IsAccepted, accepted.Rejection?.Message);
        Assert.Equal(f.Rules.Diplomacy.StateCodes.Alliance, accepted.State.Relations.Get("north", "south"));

        var atWar = state with { Relations = state.Relations.WithRelation("north", "south", f.Rules.Diplomacy.StateCodes.War) };
        Assert.True(RelationTransitions.IsAtWarWithAnyone(atWar, f.Rules, "north", includeBarbarians: false));
        Assert.True(AiOwnDiplomacyRule.IsBusy(atWar, f.Rules, "north"));
        Assert.False(dispatch.Dispatch(atWar, new ProposeAllianceCommand("north", "south")).IsAccepted);
    }

    [Fact]
    public void Normal_seat_rotation_gives_cityless_faction_a_turn_then_continues()
    {
        var f = Fixture();
        var registry = SystemRegistry.FromEngineAssembly();
        var coordinator = new TurnCoordinator(registry, f.Rules, f.World, NullEventSink.Instance, scenario: f.Scenario);
        var state = f.State with
        {
            ActiveSeatIndex = f.State.TurnOrder.Count - 2,
            Nations = new(f.State.Nations.Select(n => n.IsBarbarian ? n : n with { Control = SeatControl.Human })),
        };
        var arrivalTurn = coordinator.RunTurn(state).State;
        Assert.Equal("raiders", arrivalTurn.ActiveNationId);
        var next = coordinator.RunTurn(arrivalTurn).State;
        Assert.Equal(f.State.TurnOrder[0], next.ActiveNationId);
        Assert.False(next.NationById("raiders")!.Eliminated);
    }

    [Fact]
    public void Last_city_loss_does_not_eliminate_or_remove_roaming_forces()
    {
        var f = Fixture();
        var army = f.State.Armies[0] with { Nation = "raiders" };
        var state = f.State with { Armies = ValueList.Of(army) };
        var result = NationElimination.ApplyIfLastCityLost(state, state.NationById("raiders")!, f.Rules, "north");
        Assert.False(result.JustEliminated);
        Assert.False(result.Nation.Eliminated);
        var city = state.Cities[0] with { Owner = "raiders" };
        state = state with
        {
            Cities = ValueList.Of(city),
            Armies = ValueList.Of(army, f.State.Armies[0] with { Id = "liberator", Nation = "north" }),
        };
        var captured = CityCaptureResolver.Capture(state, "liberator", city.Id, f.Rules, "archers", "fortify", new RecordingEventSink());
        Assert.Equal("north", captured.CityById(city.Id)!.Owner);
        Assert.False(captured.NationById("raiders")!.Eliminated);
        Assert.Equal(army, captured.ArmyById(army.Id));
    }

    [Fact]
    public void Captured_city_cannot_build_fleets_and_direct_treaty_does_nothing()
    {
        var f = Fixture();
        var state = f.State with { ActiveSeatIndex = f.State.TurnOrder.ToList().IndexOf("raiders") };
        var sink = new RecordingEventSink();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, sink);
        var refused = dispatch.Dispatch(state, new OrderFleetCommand("raiders", "portus", 10, "built"));
        Assert.False(refused.IsAccepted);
        Assert.Equal("barbarians.no-production", refused.Rejection!.Code.Value);
        Assert.Same(state, refused.State);
        var after = PeaceTreatySystem.Apply(state, f.Rules, f.World, new PeaceTreatyTriggered("north", "raiders", 1000, 10),
            SplitMix64Rng.ForStream(42, "peace"));
        Assert.Same(state, after);
    }

    [Theory]
    [InlineData("human")]
    [InlineData("eliminated")]
    [InlineData("capital")]
    [InlineData("future")]
    public void Malformed_native_role_is_refused(string fault)
    {
        var f = Fixture();
        var state = f.State with { Nations = new(f.State.Nations.Select(n => !n.IsBarbarian ? n : fault switch
        {
            "human" => n with { Control = SeatControl.Human },
            "eliminated" => n with { Eliminated = true },
            "capital" => n with { CapitalCityId = "arx" },
            _ => n with { LastBarbarianArrivalTurn = f.State.Calendar.TurnIndex + 1 },
        })) };
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("test", state));
    }

    [Fact]
    public void No_treasury_upkeep_credit_or_deposition_but_regular_nations_are_billed()
    {
        var f = Fixture();
        var faction = f.State.NationById("raiders")! with { Treasury = int.MinValue / 2, Unity = 1 };
        var state = f.State with
        {
            Nations = new(f.State.Nations.Select(n => n.Id == faction.Id ? faction : n)),
            Armies = new(f.State.Armies.Append(f.State.Armies[0] with { Id = "raider-army", Nation = faction.Id })),
        };
        var sink = new RecordingEventSink();
        var rng = SplitMix64Rng.ForStream(42, "quarter");
        var billed = new QuarterlyEconomySystem().OnQuarterBoundary(new(state, f.Rules, f.World, 0, rng, sink));
        Assert.Equal(faction, billed.NationById(faction.Id));
        Assert.True(billed.NationById("north")!.Treasury < state.NationById("north")!.Treasury);
        var credited = new QuarterlyNationEconomySystem().OnQuarterBoundary(new(billed, f.Rules, f.World, 0, rng, sink));
        Assert.Equal(faction, credited.NationById(faction.Id));
        var deposed = new AiDepositionHandler().OnQuarterBoundary(new(credited, f.Rules, f.World, 0, rng, sink));
        Assert.Equal(faction, deposed.NationById(faction.Id));
    }

    [Fact]
    public void Cannot_win_and_held_cities_block_regular_total_conquest()
    {
        var f = Fixture();
        var allRaider = f.State with { Cities = new(f.State.Cities.Select(c => c with { Owner = "raiders" })) };
        Assert.Equal(VictoryStatus.Undecided, VictoryEvaluator.EvaluateTotalConquest(allRaider, f.Rules.Victory).Status);
        var held = f.State with { Cities = new(f.State.Cities.Select((c, i) => c with { Owner = i == 0 ? "raiders" : "north" })) };
        Assert.Equal(VictoryStatus.Undecided, VictoryEvaluator.EvaluateTotalConquest(held, f.Rules.Victory).Status);
        var liberated = held with { Cities = new(held.Cities.Select(c => c with { Owner = "north" })) };
        Assert.Equal("north", VictoryEvaluator.EvaluateTotalConquest(liberated, f.Rules.Victory).WinningNationId);
    }

    [Fact]
    public void Native_role_roundtrips_and_legacy_defaults_add_no_JSON_or_hash_bytes()
    {
        var f = Fixture();
        var state = f.State with { Nations = new(f.State.Nations.Select(n => n.IsBarbarian ? n with { LastBarbarianArrivalTurn = 0 } : n)) };
        var json = GameJson.Serialize(state);
        SchemaValidator.Validate<GameState>("test", JsonNode.Parse(json));
        var restored = JsonSerializer.Deserialize<GameState>(json, GameJson.Options)!;
        GameDataValidation.Validate("test", restored);
        Assert.Equal(state, restored);
        var toy = ToyFixtures.Toy;
        var legacy = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
        var bytes = GameStateHash.CanonicalBytes(legacy);
        Assert.DoesNotContain("isBarbarian", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("lastBarbarianArrivalTurn", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("barbarians", GameJson.Serialize(toy.Scenario));
        Assert.Same(legacy, BarbarianFaction.EnforceHostility(legacy, toy.Ruleset));
    }
}
