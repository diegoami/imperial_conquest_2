using IC2.Engine.Ai;
using IC2.Engine.Barbarians;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Barbarians;

public sealed class BarbarianMilitaryTests
{
    internal static (World World, Ruleset Rules, Scenario Scenario, GameState State) Coast()
    {
        var f = BarbarianArrivalSystemTests.Fixture();
        var town = ToyFixtures.Toy.World.Cities[0] with
        {
            Id = "target", Name = "Target", X = 5, Y = 3, Owner = "north", Allegiance = "north",
            PopulationThousands = 1, MaxPopulationThousands = 1, FortificationCode = 0,
            Loyalty = 0, SupplyTons = 1000, Garrison = ValueList<UnitSlot>.Empty,
        };
        var world = f.World with
        {
            Width = 7, Height = 5, Cities = ValueList.Of(town),
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(0, 14), new TerrainRun(1, 21))),
        };
        var state = GameStateFactory.CreateInitial(world, f.Rules, f.Scenario);
        var army = new ArmyState("landing-army", "raiders", 0, 0, 12, 70, 0, 30, null, "transport",
            ValueList.Of(new UnitSlot(0, "light_infantry", 3000, 6, "Raiders")));
        var fleet = new FleetState("transport", "raiders", 0, 0, 12, 10, 100, 0, 100, null, null, army.Id, 0);
        state = state with
        {
            Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet),
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("raiders"),
        };
        return (world, f.Rules, f.Scenario, state);
    }

    [Fact]
    public void Transport_sails_and_lands_through_existing_commands_without_overlap_or_troop_loss()
    {
        var f = Coast();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, new RecordingEventSink());
        var candidate = BarbarianTransport.Propose(new(f.State, f.Rules, f.World, "raiders"), Array.Empty<string>());
        Assert.NotNull(candidate);
        var sail = Assert.IsType<MoveFleetCommand>(Assert.Single(candidate.Commands));
        var sailed = dispatch.Dispatch(f.State, sail);
        Assert.True(sailed.IsAccepted, sailed.Rejection?.Message);
        Assert.NotEqual((0, 0), (sailed.State.Fleets[0].X, sailed.State.Fleets[0].Y));
        var landing = BarbarianTransport.Propose(new(sailed.State, f.Rules, f.World, "raiders"), new[] { "transport" });
        Assert.NotNull(landing);
        var disembark = Assert.IsType<DisembarkArmyCommand>(Assert.Single(landing.Commands));
        var landed = dispatch.Dispatch(sailed.State, disembark);
        Assert.True(landed.IsAccepted, landed.Rejection?.Message);
        var army = Assert.Single(landed.State.Armies);
        Assert.False(army.IsEmbarked);
        Assert.Null(landed.State.Fleets[0].CarriedArmyId);
        Assert.DoesNotContain(landed.State.Cities, c => c.X == army.X && c.Y == army.Y);
        Assert.Equal(3000, army.TotalTroops);
        Assert.Equal(0, army.Moves);
        Assert.Equal(0, landed.State.Fleets[0].Moves);
    }

    [Fact]
    public void Restricted_driver_lands_then_uses_existing_siege_rules_without_recruitment_or_diplomacy()
    {
        var f = Coast();
        var state = f.State;
        var sink = new RecordingEventSink();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, sink);
        var diagnostics = new List<string>();
        for (var turn = 0; turn < 5 && state.CityById("target")!.Owner != "raiders"; turn++)
        {
            var outcome = AiTurn.Run(state, f.Rules, f.World, dispatch, SplitMix64Rng.ForStream((ulong)turn, "military"), sink);
            Assert.Equal(0, outcome.CommandsRejected);
            Assert.Equal(0, outcome.ProjectionMismatches);
            state = outcome.State;
            diagnostics.AddRange(outcome.Log);
            diagnostics.AddRange(state.Armies.Select(a => $"army ({a.X},{a.Y}) aboard={a.AboardFleetId} moves={a.Moves}"));
            // The production weekly tick normally refreshes these; isolate the order driver here.
            state = state with
            {
                Armies = new(state.Armies.Select(a => a with { Moves = SupplyMoraleRule.BaseMoves(a.TotalTroops, f.Rules), SupplyTons = SupplyCapacity.ArmyCapacityTons(a.TotalTroops, f.Rules) })),
                Fleets = new(state.Fleets.Select(fleet => fleet with { Moves = 12 })),
            };
        }
        Assert.True(state.CityById("target")!.Owner == "raiders", string.Join(Environment.NewLine, diagnostics));
        Assert.Empty(state.NationById("raiders")!.RecruitmentSlots);
        Assert.Equal(1, state.Armies.Count(a => a.Nation == "raiders"));
        Assert.Single(state.Fleets);
        Assert.DoesNotContain(sink.Events, e => e.Kind.StartsWith("trade.", StringComparison.Ordinal)
            || e.Kind.StartsWith("alliance.", StringComparison.Ordinal) || e.Kind == "war.declared");
        Assert.DoesNotContain(sink.Events, e => e.Kind.Contains("hired", StringComparison.Ordinal) || e.Kind.Contains("recruit", StringComparison.Ordinal));
    }

    [Fact]
    public void Barbarian_field_battle_Undo_and_OK_resume_the_same_AI_pass_as_immediate_commit()
    {
        GameSession Build(bool undo)
        {
            var f = Coast();
            var home = f.World.Cities[0] with { Id = "raider-home", Name = "Home", X = 4, Y = 4,
                Owner = "raiders", Allegiance = "raiders", SupplyTons = 1000 };
            var world = f.World with { Cities = new(f.World.Cities.Append(home)) };
            var state = GameStateFactory.CreateInitial(world, f.Rules, f.Scenario);
            var units = f.State.Armies[0].Units;
            state = state with
            {
                ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("north"),
                Armies = ValueList.Of(
                    new ArmyState("defender", "north", 2, 3, 12, 70, 200, 5, 1, null,
                        ValueList.Of(units[0] with { Troops = 500 })),
                    new ArmyState("raider", "raiders", 3, 3, 12, 70, 0, 30, 1, null, units)),
                Fleets = ValueList<FleetState>.Empty,
            };
            var save = new SaveGame(GameDataSchema.CurrentVersion, "test", "test", state.ScenarioId, state.WorldId, state.RulesetId, state);
            var session = new GameSession(world, f.Rules, f.Scenario, save)
                { HumanBattles = HumanBattleMode.Tactical, TacticalUndoEnabled = undo };
            var output = session.Submit("end");
            Assert.True(session.OpenTacticalBattle is not null, string.Join(Environment.NewLine, output.Lines));
            Assert.Equal("raiders", session.State.ActiveNationId);
            return session;
        }
        var session = Build(true);
        var baseline = Build(false);
        var opened = session.State;
        session.BattleSurrender(true);
        Assert.Same(opened, session.State);
        Assert.True(session.BattleResultAwaitingAcknowledgement);
        session.BattleUndo();
        Assert.Same(opened, session.State);
        Assert.False(session.OpenTacticalBattle!.IsOver);
        session.BattleSurrender(true);
        baseline.BattleSurrender(true);
        session.AcknowledgeBattleResult();
        Assert.Equal(baseline.State, session.State);
    }
}
