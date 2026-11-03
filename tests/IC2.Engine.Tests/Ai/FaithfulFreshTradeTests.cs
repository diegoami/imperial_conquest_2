using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Diplomacy;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulFreshTradeTests
{
    // Deliberately not alphabetical. z then y strictly improve the war ratio (16 then 40).
    private static readonly string[] Ids = ["me", "z-leader", "y-leader", "x-trade", "b-trade", "a-trade"];

    private static (GameState State, Ruleset Rules, World World) Fixture(bool faithful = true, int cap = 3)
    {
        var toy = AiScriptedStates.Ruleset;
        var rules = toy with { Flags = toy.Flags with { AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed },
            Diplomacy = toy.Diplomacy with { MaxTradePartners = cap } };
        var state = DiplomacyTestbed.StateOf(Ids.Select(id => DiplomacyTestbed.Nation(id, id,
            unity: 1000, wealth: id == "z-leader" ? 100000 : id == "y-leader" ? 40000 : 200000, taxBase: 100)).ToArray());
        state = AiScriptedStates.WithActiveSeat(state, "me");
        var world = AiScriptedStates.World with { StartingNeighbours = ValueList.From(Ids.Select(id =>
            new NationNeighbours(id, id == "me" ? ValueList.Of("z-leader", "y-leader")
                : id is "z-leader" or "y-leader" ? ValueList.Of("me") : ValueList<string>.Empty))) };
        Assert.Equal("y-leader", AiOwnDiplomacyRule.BestWarTarget(state, rules, world, "me"));
        return (state, rules, world);
    }

    private sealed class Trace(CommandDispatcher inner) : ICommandDispatch
    {
        public List<string> Trades { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is AiFormTradeCommand trade) Trades.Add(trade.PartnerNationId);
            return inner.Dispatch(state, command, events);
        }
    }

    private static (AiTurnOutcome Outcome, Trace Trace) Run(GameState state, Ruleset rules, World world, ulong seed = 1)
    {
        // Isolate trade order from the independently covered chance of declaring war. Keep the real
        // named-stream RNG and verify the miss rather than changing diplomacy parameters or dispatch.
        Assert.False(SplitMix64Rng.ForStream(seed, "ai.turn").ForStream($"ai.ownDiplomacy.warRoll:me:{state.Calendar.TurnIndex}")
            .NextChance(1, rules.Diplomacy.AiOwnDiplomacy.WarDeclareRollDenominator));
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance));
        var result = AiTurn.Run(state, rules, world, trace, SplitMix64Rng.ForStream(seed, "ai.turn"), NullEventSink.Instance);
        Assert.Equal(0, result.CommandsRejected); Assert.Equal(0, result.ProjectionMismatches);
        return (result, trace);
    }

    [Theory]
    [InlineData(true, 1UL)]
    [InlineData(true, 7UL)]
    [InlineData(false, 1UL)]
    public void Binding_cap_excludes_every_running_leader_only_in_Faithful(bool faithful, ulong seed)
    {
        var (state, rules, world) = Fixture(faithful);
        var result = Run(state, rules, world, seed);
        var expected = faithful ? new[] { "x-trade", "b-trade", "a-trade" } : ["z-leader", "y-leader", "x-trade"];
        Assert.Equal(expected, result.Trace.Trades);
        Assert.Equal(3, AiOwnDiplomacyRule.TradeCount(result.Outcome.State, rules, "me"));
        foreach (var id in Ids.Skip(1)) Assert.Equal(expected.Contains(id) ? rules.Diplomacy.StateCodes.Trade
            : rules.Diplomacy.StateCodes.Peace, result.Outcome.State.Relations.Get("me", id));
        var replay = Run(state, rules, world, seed);
        Assert.Equal(result.Trace.Trades, replay.Trace.Trades); Assert.Equal(result.Outcome.State, replay.Outcome.State);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Later_sweep_revisits_skipped_leaders_in_table_order_if_capacity_remains(int cap)
    {
        var (state, rules, world) = Fixture(cap: cap);
        var result = Run(state, rules, world);
        Assert.Equal(new[] { "x-trade", "b-trade", "a-trade", "z-leader", "y-leader" }.Take(cap), result.Trace.Trades);
        Assert.Equal(cap, AiOwnDiplomacyRule.TradeCount(result.Outcome.State, rules, "me"));
    }

    [Fact]
    public void Live_partner_cap_skips_an_otherwise_eligible_first_sweep_nation()
    {
        var (state, rules, world) = Fixture();
        foreach (var partner in new[] { "z-leader", "y-leader", "b-trade" })
            state = state with { Relations = state.Relations.WithRelation("x-trade", partner, rules.Diplomacy.StateCodes.Trade) };
        var result = Run(state, rules, world);
        Assert.Equal(new[] { "b-trade", "a-trade", "z-leader" }, result.Trace.Trades);
        Assert.Equal(3, AiOwnDiplomacyRule.TradeCount(result.Outcome.State, rules, "x-trade"));
    }

    [Theory]
    [InlineData("busy", "z-leader,y-leader,x-trade,b-trade,a-trade")]
    [InlineData("equal", "y-leader,x-trade,b-trade,a-trade,z-leader")]
    [InlineData("non-neighbour", "z-leader,x-trade,b-trade,a-trade,y-leader")]
    [InlineData("protected", "z-leader,x-trade,b-trade,a-trade,y-leader")]
    [InlineData("human", "y-leader,x-trade,b-trade,a-trade")]
    [InlineData("cooldown", "x-trade,b-trade,a-trade,y-leader")]
    public void Running_war_predicates_and_human_candidates_use_the_same_rules(string mode, string expected)
    {
        var (state, rules, world) = Fixture();
        switch (mode)
        {
            case "busy": state = state with { Calendar = state.Calendar with { SeasonIndex = rules.Calendar.SeasonsPerYear - 1 } }; break;
            case "equal": state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "y-leader" ? n with { Wealth = 100000 } : n)) }; break;
            case "non-neighbour": world = world with { StartingNeighbours = ValueList.From(world.StartingNeighbours!.Select(n =>
                n.NationId == "me" ? n with { NeighbourIds = ValueList.Of("y-leader") } : n.NationId == "z-leader" ? n with { NeighbourIds = ValueList<string>.Empty } : n)) }; break;
            case "protected": state = state with { Relations = state.Relations.WithRelation("z-leader", "a-trade", rules.Diplomacy.StateCodes.Alliance),
                Cities = ValueList.Of(F.City("ally", "Ally", 1, 1, "a-trade", "a-trade", 80, 0, 10, 10, 0)) }; break;
            case "human": state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "z-leader" ? n with { Control = SeatControl.Human, Wealth = 20000 } : n)) }; break;
            case "cooldown": state = state with { Relations = state.Relations.WithRelation("me", "z-leader", -1) }; break;
        }
        var before = state;
        // Pure live query: repeatability and record identity, without introducing another RNG stream.
        Assert.Equal(expected.Split(','), AiOwnDiplomacyRule.OriginalTradePartners(state, rules, world, "me"));
        Assert.Equal(expected.Split(','), AiOwnDiplomacyRule.OriginalTradePartners(state, rules, world, "me"));
        Assert.Same(before, state);
    }
}
