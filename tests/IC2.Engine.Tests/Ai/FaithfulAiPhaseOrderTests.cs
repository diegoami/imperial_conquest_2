using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulAiPhaseOrderTests
{
    private sealed class ZeroRng : IRng
    {
        public ulong Seed => 1;
        public ulong State => 1;
        public ulong NextUInt64() => 0;
        public int NextInt(int exclusiveUpperBound) => 0;
        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => inclusiveLowerBound;
        public bool NextChance(int numerator, int denominator) => numerator > 0;
        public IRng ForStream(string streamName) => this;
    }

    private sealed class Trace(ICommandDispatch inner, bool retain) : ICommandDispatch, IAiBattleInterruption
    {
        public bool RetainPartialAiPass => retain;
        public bool ArmyPhaseInterrupted => false;
        public List<(ICommand Command, GameState Before, CommandResult Result)> Calls { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            var result = inner.Dispatch(state, command, events);
            Calls.Add((command, state, result)); return result;
        }
    }

    private static (GameState State, Ruleset Rules, World World) Fixture()
    {
        var rules = AiScriptedStates.Ruleset with { Flags = AiScriptedStates.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original },
            Ai = AiScriptedStates.Ruleset.Ai with { CityScoreThreshold = 1, MaxActionsPerTurn = 2 } };
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 320 * 140))) };
        var actor = F.Army("first", "north", 230, 80, 60, F.Unit("light_infantry", 40000)) with { Moves = 8, SupplyTons = 0, Money = 77 };
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            [F.Nation("north", treasury: 10000, wealth: 20000000, unity: 1000, capitalCityId: "home"),
             F.Nation("south", treasury: 10000, wealth: 1000000, unity: 500, capitalCityId: "other")],
            [F.City("home", "Home", 200, 50, "north", "north", 90, 100, 100, 100, 0),
             F.City("supplier", "Supplier", 230, 82, "north", "north", 90, 100, 100, 100, 0) with { SupplyTons = 10000 },
             F.City("target", "Target", 250, 80, "south", "south", 90, 100, 100, 100, 0),
             F.City("other", "Other", 300, 120, "south", "south", 90, 100, 100, 100, 0)],
            [actor, actor with { Id = "later", Y = 81, Moves = 0 }]), "north");
        state = state with { MercenaryPool = ValueList<MercenaryPoolSlot>.Empty,
            Neighbours = ValueList.Of(new NationNeighbours("north", ValueList.Of("south")), new NationNeighbours("south", ValueList.Of("north"))) };
        return (state, rules, world);
    }

    [Theory]
    [InlineData(false)] // Instant/headless/watch: no retained tactical gate.
    [InlineData(true)]  // Tactical-capable dispatch, with no battle opened in this fixture.
    public void Later_army_is_supported_only_after_the_first_armys_move(bool retain)
    {
        var f = Fixture();
        var state = BattleCommandTestbed.AtWar(f.State, "north", "south");
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, NullEventSink.Instance), retain);
        var outcome = AiTurn.Run(state, f.Rules, f.World, trace, new ZeroRng(), NullEventSink.Instance);
        var first = Assert.Single(trace.Calls, c => c.Command is MoveArmyCommand { ArmyId: "first" });
        Assert.True(first.Result.IsAccepted);
        Assert.Equal(577, first.Before.ArmyById("first")!.Money);
        Assert.Equal(77, first.Before.ArmyById("later")!.Money);
        Assert.Equal(577, outcome.State.ArmyById("later")!.Money); // Even a zero-move record gets E41C.
        Assert.Equal(0, outcome.CommandsRejected);
    }

    [Fact]
    public void Same_seat_war_precedes_the_economy_and_mobilizes_ready_recruits_immediately()
    {
        var f = Fixture();
        var army = f.State.Armies[0] with { X = 201, Y = 50, SupplyTons = 1000, Money = 1000,
            Units = ValueList.Of(F.Unit("light_infantry", 1000)) };
        var state = f.State with { Armies = ValueList.Of(army), Nations = ValueList.From(f.State.Nations.Select(n =>
            n.Id == "north" ? n with { RecruitmentSlots = ValueList.Of(new RecruitmentSlot("home", "light_infantry", 5000, 24)) }
                : n with { Control = SeatControl.Human })) };
        Assert.Equal("south", AiOwnDiplomacyRule.BestWarTarget(state, f.Rules, f.World, "north"));
        Assert.Equal(0, AiFaithfulEconomy.Threat(state, f.Rules, "north"));
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, NullEventSink.Instance), false);
        var outcome = AiTurn.Run(state, f.Rules, f.World, trace, new ZeroRng(), NullEventSink.Instance);
        var declaration = Assert.Single(trace.Calls, c => c.Command is DeclareWarCommand);
        var mobilize = Assert.Single(trace.Calls, c => c.Command is MobilizeRecruitSlotCommand);
        Assert.True(declaration.Result.IsAccepted && mobilize.Result.IsAccepted);
        Assert.True(trace.Calls.IndexOf(declaration) < trace.Calls.IndexOf(mobilize));
        Assert.Equal(f.Rules.Diplomacy.StateCodes.War, mobilize.Before.Relations.Get("north", "south"));
        Assert.Contains(mobilize.Result.State.ArmyById("first")!.Units, u => u.Troops == 5000);
        Assert.True(outcome.State.ArmyById("first")!.TotalTroops >= 6000);
        Assert.Equal(0, outcome.CommandsRejected);
    }
}
