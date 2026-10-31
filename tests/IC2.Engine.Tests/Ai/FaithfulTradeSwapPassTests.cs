using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Diplomacy;
using Xunit;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulTradeSwapPassTests
{
    private static (GameState State, Ruleset Rules, World World) Fixture(bool faithful, bool gainedLater = false)
    {
        var toy = AiScriptedStates.Ruleset;
        var rules = toy with { Flags = toy.Flags with { AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed },
            Diplomacy = toy.Diplomacy with { MaxTradePartners = 2 } };
        // Names deliberately disagree with table order. No cities/armies: no war, alliance or economy action.
        var entries = gainedLater
            ? new[] { ("me", 0), ("poor", 100), ("z-mid", 500), ("a-rich", 1000), ("filler", 2000) }
            : new[] { ("z-mid", 500), ("me", 0), ("poor", 100), ("second", 200), ("a-rich", 1000) };
        var state = DiplomacyTestbed.StateOf(entries.Select(e => DiplomacyTestbed.Nation(e.Item1, e.Item1, taxBase: e.Item2)).ToArray());
        state = AiScriptedStates.WithActiveSeat(state, "me");
        state = state with { Relations = state.Relations.WithRelation("me", "poor", rules.Diplomacy.StateCodes.Trade)
            .WithRelation("me", gainedLater ? "filler" : "second", rules.Diplomacy.StateCodes.Trade) };
        return (state, rules, AiScriptedStates.World with { Id = "classical-mediterranean" });
    }

    private sealed class Trace(CommandDispatcher inner, bool reject = false) : ICommandDispatch
    {
        public List<(string Poor, string Rich)> Swaps { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, new RecordingEventSink());
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is AiSwapTradePartnerCommand swap)
            {
                Swaps.Add((swap.PoorerPartnerId, swap.RicherCandidateId));
                if (reject) return UnavailableCommandDispatch.Instance.Dispatch(state, command, events);
            }
            return inner.Dispatch(state, command, events);
        }
    }

    private static (AiTurnOutcome Outcome, Trace Trace) Run(GameState state, Ruleset rules, World world, bool reject = false)
    {
        var sink = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, sink), reject);
        return (AiTurn.Run(state, rules, world, trace, SplitMix64Rng.ForStream(1, "ai.turn"), sink), trace);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Lower_index_gained_partner_is_not_revisited_only_in_Faithful(bool faithful)
    {
        var (state, rules, world) = Fixture(faithful);
        var (outcome, trace) = Run(state, rules, world);
        Assert.Equal(new[] { ("poor", "z-mid"), faithful ? ("second", "a-rich") : ("z-mid", "a-rich") }, trace.Swaps);
        Assert.Equal(0, outcome.CommandsRejected);
        Assert.Equal(0, outcome.ProjectionMismatches);
        Assert.Equal(rules.Diplomacy.StateCodes.Trade, outcome.State.Relations.Get("me", faithful ? "z-mid" : "second"));
        Assert.Equal(rules.Diplomacy.CooldownAfterBrokenTrade, outcome.State.Relations.Get("me", faithful ? "second" : "z-mid"));
        Assert.Equal(2, AiOwnDiplomacyRule.TradeCount(outcome.State, rules, "me"));
        // Replaying the same seat starts a fresh transient pass, with identical commands and result.
        var replay = Run(state, rules, world);
        Assert.Equal(trace.Swaps, replay.Trace.Swaps);
        Assert.Equal(outcome.State, replay.Outcome.State);
        // A fresh invocation on the result may revisit the earlier gained partner.
        if (faithful) Assert.Equal(new[] { ("z-mid", "second") }, Run(outcome.State with {
            Nations = ValueList.From(outcome.State.Nations.Select(n => n.Id == "second" ? n with { TaxBase = 2000 } : n)),
            Relations = outcome.State.Relations.WithRelation("me", "second", rules.Diplomacy.StateCodes.Peace)
        }, rules, world).Trace.Swaps);
    }

    [Fact]
    public void Higher_index_gained_partner_is_visited_later_in_the_same_pass()
    {
        var (state, rules, world) = Fixture(true, gainedLater: true);
        var (outcome, trace) = Run(state, rules, world);
        Assert.Equal(new[] { ("poor", "z-mid"), ("z-mid", "a-rich") }, trace.Swaps);
        Assert.Equal(0, outcome.CommandsRejected);
        Assert.Equal(rules.Diplomacy.CooldownAfterBrokenTrade, outcome.State.Relations.Get("me", "z-mid"));
        Assert.Equal(rules.Diplomacy.StateCodes.Trade, outcome.State.Relations.Get("me", "a-rich"));
    }

    [Fact]
    public void Nation_record_order_is_independent_of_relation_matrix_order_and_candidate_caps_are_live()
    {
        var (state, rules, world) = Fixture(true);
        var codes = rules.Diplomacy.StateCodes;
        var relations = DiplomaticRelations.Uniform(ValueList.From(state.Nations.Reverse().Select(n => n.Id)), codes.Peace);
        foreach (var a in state.Nations)
            foreach (var b in state.Nations)
                if (a.Id != b.Id) relations = relations.WithRelation(a.Id, b.Id, state.Relations.Get(a.Id, b.Id));
        state = state with { Relations = relations.WithRelation("a-rich", "poor", codes.Trade)
            .WithRelation("a-rich", "second", codes.Trade) };
        var (outcome, trace) = Run(state, rules, world);
        Assert.Equal(new[] { ("poor", "z-mid") }, trace.Swaps);
        Assert.Equal(0, outcome.CommandsRejected);
        Assert.Equal(codes.Trade, outcome.State.Relations.Get("me", "second"));
        Assert.Equal(2, AiOwnDiplomacyRule.TradeCount(outcome.State, rules, "a-rich"));
    }

    [Fact]
    public void A_rejected_swap_is_attempted_once_and_does_not_restart_the_pass()
    {
        var (state, rules, world) = Fixture(true);
        var (outcome, trace) = Run(state, rules, world, reject: true);
        Assert.Single(trace.Swaps);
        Assert.Equal(1, outcome.CommandsRejected);
        Assert.Equal(state.Relations, outcome.State.Relations);
        Assert.False(outcome.HitActionCap);
    }
}
