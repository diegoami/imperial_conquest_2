using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed partial class AiCapitalDefenceTests
{
    private static ArmyState Defender(string id, int x, int y, int troops = 30_000, int moves = 5) =>
        Army(id, "north", x, y, moves) with { Money = 0, SupplyTons = 0,
            Units = ValueList.Of(Fx.Unit("light_infantry", troops)) };
    private static ArmyState Invader(int troops = 1000, int x = 104) =>
        Army("threat", "south", x, 41) with { Money = 0, SupplyTons = 0,
            Units = ValueList.Of(Fx.Unit("light_infantry", troops)) };

    private sealed class TraceDefenceDispatch(ICommandDispatch inner) : ICommandDispatch
    {
        public List<(ICommand Command, GameState State)> Calls { get; } = new();
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            Calls.Add((command, state));
            return inner.Dispatch(state, command, events);
        }
    }

    private static (AiCapitalDefence.Outcome Result, RecordingEventSink Events, TraceDefenceDispatch Trace)
        ContactRun(GameState state, World? world = null, Ruleset? rules = null)
    {
        var loadedWorld = world ?? World();
        var loadedRules = rules ?? Rules;
        var events = new RecordingEventSink();
        var trace = new TraceDefenceDispatch(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), loadedRules, loadedWorld, events));
        var result = AiCapitalDefence.Run(state, loadedRules, loadedWorld, "north", trace, events, new());
        return (result, events, trace);
    }

    [Fact]
    public void Arrival_attack_kills_the_threat_before_later_responders_select_it()
    {
        var later = Defender("later", 100, 42);
        var state = State([Defender("first", 100, 41), later, Invader()]);
        var rules = Rules with { Flags = Rules.Flags with { UnitTableCompaction = UnitTableCompactionPolicy.SwapRemoveSweep } };
        var f = ContactRun(state, rules: rules);
        Assert.Null(f.Result.State.ArmyById("threat"));
        Assert.Equal(later, f.Result.State.ArmyById(later.Id));
        Assert.Equal((1, 2, 0), (f.Result.Dispatches, f.Result.Issued, f.Result.Rejected));
        Assert.Collection(f.Trace.Calls, c => Assert.IsType<MoveArmyCommand>(c.Command),
            c => Assert.IsType<AttackArmyCommand>(c.Command));
        var atAttack = f.Trace.Calls[1].State;
        Assert.Equal((103, 41, 2), (atAttack.ArmyById("first")!.X, atAttack.ArmyById("first")!.Y,
            atAttack.ArmyById("first")!.Moves));
        Assert.Equal(2, Assert.Single(f.Result.State.DeadArmySlots!));
        Assert.Single(f.Events.Events.OfType<BattleResolved>());
    }

    [Fact]
    public void A_surviving_threat_is_reconsidered_against_the_next_live_responder()
    {
        var state = State([Defender("first", 100, 41, troops: 1000),
            Defender("later", 100, 42, troops: 60_000), Invader(troops: 30_000)]);
        var f = ContactRun(state);
        Assert.Equal(new[] { "first", "later" }, f.Events.Events.OfType<BattleResolved>().Select(e => e.Result.AttackerId));
        Assert.Null(f.Result.State.ArmyById("first"));
        Assert.Null(f.Result.State.ArmyById("threat"));
        Assert.NotNull(f.Result.State.ArmyById("later"));
        Assert.Equal((2, 4, 0), (f.Result.Dispatches, f.Result.Issued, f.Result.Rejected));
    }

    [Theory]
    [InlineData(0, 104, false)]
    [InlineData(3, 104, false)]
    [InlineData(4, 104, true)]
    [InlineData(1, 101, true)]
    [InlineData(5, 101, true)]
    [InlineData(3, 109, false)]
    public void Arrival_requires_exact_adjacency_and_positive_remaining_moves(int moves, int targetX, bool fights)
    {
        var f = ContactRun(State([Defender("first", 100, 41, moves: moves), Invader(x: targetX)]));
        Assert.Equal(fights, f.Events.Events.OfType<BattleResolved>().Any());
        Assert.Equal(fights, f.Result.State.ArmyById("threat") is null);
        Assert.Equal(0, f.Result.Rejected);
    }

    [Theory]
    [InlineData("peace")]
    [InlineData("embarked-threat")]
    [InlineData("custom")]
    [InlineData("designed")]
    public void Ineligible_contacts_and_other_policies_do_not_gain_an_attack(string mode)
    {
        var threat = Invader();
        if (mode == "embarked-threat") threat = threat with { AboardFleetId = "enemy-ship", CoveredTileCode = null };
        var state = State([Defender("first", 100, 41), threat], war: mode != "peace");
        var f = ContactRun(state, mode == "custom" ? World() with { Id = "custom" } : null,
            mode == "designed" ? AiScriptedStates.Ruleset : null);
        Assert.DoesNotContain(f.Trace.Calls, c => c.Command is AttackArmyCommand);
        Assert.Empty(f.Events.Events.OfType<BattleResolved>());
        Assert.Equal(threat, f.Result.State.ArmyById(threat.Id));
        Assert.Equal(0, f.Result.Rejected);
    }

    [Fact]
    public void Real_turn_fights_during_defence_before_support_and_the_ordinary_phase()
    {
        var state = State([Defender("first", 100, 41), Defender("later", 100, 42), Invader()]);
        state = state with { Cities = ValueList.From(state.Cities.Select(c => c.Id == "cap" ? c with { SupplyTons = 5000 } : c)),
            MercenaryPool = ValueList<MercenaryPoolSlot>.Empty };
        var world = World();
        var rules = Rules with { Ai = Rules.Ai with { MaxActionsPerTurn = 1 } };
        var events = new RecordingEventSink();
        var trace = new TraceDefenceDispatch(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, events));
        var turn = AiTurn.Run(state, rules, world, trace, SplitMix64Rng.ForStream(1, "contact-turn"), events);
        var attack = Assert.Single(trace.Calls, c => c.Command is AttackArmyCommand);
        Assert.Equal(0, attack.State.ArmyById("first")!.Money);
        Assert.True(turn.State.ArmyById("first")!.Money > 0);
        Assert.Null(turn.State.ArmyById("threat"));
        Assert.Equal(0, turn.CommandsRejected);
        Assert.True(turn.CommandsIssued >= 2);
        Assert.Single(events.Events.OfType<BattleResolved>());
    }
}
