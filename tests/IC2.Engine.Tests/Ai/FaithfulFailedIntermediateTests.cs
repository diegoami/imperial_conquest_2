using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulFailedIntermediateTests
{
    private static (GameState State, Ruleset Rules, World World) Fixture()
    {
        var rules = AiScriptedStates.Ruleset;
        rules = rules with { Flags = rules.Flags with { AiEconomy = AiEconomyModel.Original },
            Ai = rules.Ai with { CityScoreThreshold = 100000, ArmyScoreThreshold = 100000, MaxActionsPerTurn = 1 } };
        var actor = F.Army("actor", "north", 230, 80, 80, F.Unit("light_infantry", 5000))
            with { Moves = 10, SupplyTons = 1000, Money = 500 };
        var home = F.City("home", "Home", 230, 81, "north", "north", 80, 100, 5000, 100, 1);
        var offer = home with { Id = "offer", Name = "Offer", X = 236, Y = 80, SupplyTons = 10 };
        var goal = home with { Id = "goal", Owner = "south", Allegiance = "south", X = 240, Y = 85, SupplyTons = 1 };
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            [F.Nation("north", capitalCityId: "home") with { MobilizedPercent = rules.Recruitment.MobilizationCapPercent },
             F.Nation("south", capitalCityId: "goal")], [home, offer, goal], [actor]), "north");
        state = state with { MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, offer.X, offer.Y, 1, "light_infantry", 1000, 6)),
            Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War) };
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            cells[(offer.Y + dy) * 320 + offer.X + dx] = 0;
        }
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        return (state, rules, world);
    }

    [Fact]
    public void Failed_intermediate_search_is_an_accepted_noop_before_garrison()
    {
        var f = Fixture();
        var view = new AiView(f.State, f.Rules, f.World, "north");
        var actor = f.State.Armies[0];
        var target = new GridPoint(236, 80);
        Assert.False(ClassicalArmyNavigation.DirectLine(view, new(actor.X, actor.Y), target));
        Assert.Null(ClassicalArmyNavigation.Intermediate(view, new(actor.X, actor.Y), target).Point);
        Assert.NotNull(ClassicalArmyNavigation.LocalFallback(view, new(actor.X, actor.Y)));
        var command = AiArmyTargetTree.MarchCommand(view, actor, target);
        Assert.NotNull(command);
        var result = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, NullEventSink.Instance)
            .Dispatch(f.State, command);
        Assert.True(result.IsAccepted);
        // Every accepted command advances the dispatch seed, including a substantive no-op.
        Assert.Equal(f.State with { RandomSeed = result.State.RandomSeed }, result.State);
        Assert.Single(result.Events.OfType<OriginalArmyIntermediateSearchFailed>());
    }

    [Fact]
    public void Real_mercenary_run_and_its_immediate_direct_garrison_preserve_the_full_budget()
    {
        var f = Fixture();
        var view = new AiView(f.State, f.Rules, f.World, "north");
        var decision = AiArmyTargetTree.DecideUnlogged(view, f.State.Armies[0], true, out _);
        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);
        Assert.Equal(AiArmyTargetTree.Continuation.None, decision.After);
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, NullEventSink.Instance));
        var result = AiTurn.Run(f.State, f.Rules, f.World, trace, SplitMix64Rng.ForStream(1, "failed-search"), NullEventSink.Instance);
        Assert.Equal(2, trace.Moves.Count);
        Assert.Equal((236,80), (trace.Moves[0].TargetX, trace.Moves[0].TargetY));
        Assert.Equal((230,81), (trace.Moves[1].TargetX, trace.Moves[1].TargetY));
        var army = result.State.ArmyById("actor")!;
        Assert.Equal((230,80,10), (army.X, army.Y, army.Moves));
        Assert.Equal(0, result.CommandsRejected);
        Assert.False(trace.Moves[0].InheritFailedIntermediate);
        Assert.True(trace.Moves[1].InheritFailedIntermediate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fresh_commands_for_another_army_or_seat_do_not_inherit(bool otherSeat)
    {
        var f = Fixture();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, NullEventSink.Instance);
        var view = new AiView(f.State, f.Rules, f.World, "north");
        var failed = dispatch.Dispatch(f.State, AiArmyTargetTree.MarchCommand(view, f.State.Armies[0], new(236,80))!);
        var nation = otherSeat ? "south" : "north";
        var other = failed.State.Armies[0] with { Id = "other", Nation = nation };
        var state = AiScriptedStates.WithActiveSeat(failed.State with { Armies = ValueList.Of(other) }, nation);
        var fresh = dispatch.Dispatch(state, new MoveArmyCommand(nation, other.Id, 230,81,
            MoveWalkPolicy.AiContinuation, 230,81));
        Assert.True(fresh.IsAccepted);
        Assert.True(fresh.State.Armies[0].Moves < 10);
        Assert.NotEqual((230,80), (fresh.State.Armies[0].X, fresh.State.Armies[0].Y));
    }

    [Fact]
    public void A_fresh_AI_invocation_without_the_offer_does_not_keep_prior_suppression()
    {
        var f = Fixture();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, f.World, NullEventSink.Instance);
        var first = AiTurn.Run(f.State, f.Rules, f.World, dispatch, SplitMix64Rng.ForStream(1,"first"), NullEventSink.Instance);
        Assert.Equal(10, first.State.Armies[0].Moves);
        var trace = new Trace(dispatch);
        var second = AiTurn.Run(first.State with { MercenaryPool = ValueList<MercenaryPoolSlot>.Empty },
            f.Rules, f.World, trace, SplitMix64Rng.ForStream(2,"second"), NullEventSink.Instance);
        Assert.NotEmpty(trace.Moves);
        Assert.All(trace.Moves, m => Assert.False(m.InheritFailedIntermediate));
        Assert.True(second.State.Armies[0].Moves < 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Intermediate_path_and_Improved_commands_ignore_inherited_direct_path_suppression(bool faithful)
    {
        var f = Fixture();
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        // Put the original group-1 waypoint (221,94) within two moves. The old fixture's
        // distant waypoint exhausted all ten moves, masking an incorrect inherited break.
        cells[92 * 320 + 222] = 0;
        var world = f.World with { Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c,1)))) };
        var rules = faithful ? f.Rules : f.Rules with { Flags = f.Rules.Flags with { AiEconomy = AiEconomyModel.Designed } };
        var actor = f.State.Armies[0] with { X = 219, Y = 92 };
        var state = f.State with { Armies = ValueList.Of(actor) };
        var view = new AiView(state, rules, world, "north");
        var target = new GridPoint(224,92);
        Assert.False(ClassicalArmyNavigation.DirectLine(view, new(actor.X,actor.Y), target));
        Assert.NotNull(ClassicalArmyNavigation.Intermediate(view, new(actor.X,actor.Y), target).Point);
        var command = AiArmyTargetTree.MarchCommand(view, actor, target)!;
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance);
        var plain = dispatch.Dispatch(state, command);
        var inherited = dispatch.Dispatch(state, command with { InheritFailedIntermediate = true });
        Assert.True(plain.IsAccepted);
        Assert.Equal(plain.State, inherited.State);
        Assert.NotEqual(state.Armies, plain.State.Armies);
        Assert.True(plain.State.Armies[0].Moves > 0);
        if (faithful)
        {
            Assert.Equal((221,94), (command.X, command.Y));
            var firstLeg = dispatch.Dispatch(state, command with { Walk = MoveWalkPolicy.Single });
            Assert.True(firstLeg.IsAccepted);
            Assert.Equal((221,94,8), (firstLeg.State.Armies[0].X, firstLeg.State.Armies[0].Y, firstLeg.State.Armies[0].Moves));
            Assert.Equal((224,92,5), (plain.State.Armies[0].X, plain.State.Armies[0].Y, plain.State.Armies[0].Moves));
        }
    }

    [Fact]
    public void Cross_class_offer_without_a_ferry_suppresses_the_real_immediate_garrison_walk()
    {
        var f = Fixture();
        var actor = f.State.Armies[0] with { X = 193, Y = 96 };
        var state = f.State with { Armies = ValueList.Of(actor),
            Cities = ValueList.From(f.State.Cities.Select(c => c.Id switch {
                "home" => c with { X = 193, Y = 97 },
                "offer" => c with { X = 192, Y = 77 }, _ => c })),
            MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, 192, 77, 1, "light_infantry", 1000, 6)) };
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        cells[78 * 320 + 192] = 0;
        var world = f.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var view = new AiView(state, f.Rules, world, "north");
        Assert.NotEqual(ClassicalArmyNavigation.Classify(193, 96), ClassicalArmyNavigation.Classify(192, 77));
        Assert.False(ClassicalArmyNavigation.DirectLine(view, new(193, 96), new(192, 77)));
        Assert.Equal("offer", AiArmyTargetTree.MercenaryRunDestination(view, actor)!.Id);
        var sink = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, world, sink));
        var result = AiTurn.Run(state, f.Rules, world, trace, SplitMix64Rng.ForStream(1, "cross-class"), sink);
        Assert.Equal(2, trace.Moves.Count);
        Assert.Equal((192, 77), (trace.Moves[0].TargetX, trace.Moves[0].TargetY));
        Assert.Equal((193, 97), (trace.Moves[1].TargetX, trace.Moves[1].TargetY));
        Assert.True(trace.Moves[1].InheritFailedIntermediate);
        Assert.Single(sink.Events.OfType<OriginalArmyIntermediateSearchFailed>());
        var after = result.State.ArmyById(actor.Id)!;
        Assert.Equal((193, 96, 10), (after.X, after.Y, after.Moves));
        Assert.Equal(0, result.CommandsRejected);
        var fresh = trace.Dispatch(result.State, trace.Moves[1] with { InheritFailedIntermediate = false });
        Assert.True(fresh.IsAccepted);
        Assert.True(fresh.State.ArmyById(actor.Id)!.Moves < 10);
    }

    private sealed class Trace(ICommandDispatch inner) : ICommandDispatch
    {
        public List<MoveArmyCommand> Moves { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is MoveArmyCommand move) Moves.Add(move);
            return inner.Dispatch(state, command, events);
        }
    }
}
