using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulDestinationPolicyTests
{
    private static ArmyState Actor(int moves = 8) => AiReachableFallbackTests.Army(230, 80, moves) with
        { Id = "actor", SupplyTons = 0 };
    private static CityState City(string id, int x, int y, string owner = "north") =>
        AiReachableFallbackTests.City(id, owner, x, y, supply: 5000);

    private static AiView View(bool faithful, ArmyState actor, IEnumerable<CityState> cities,
        IEnumerable<ArmyState>? others = null, IEnumerable<MercenaryPoolSlot>? offers = null,
        Action<int[]>? edit = null, bool hasCapital = true)
    {
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        edit?.Invoke(cells);
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var rules = AiScriptedStates.Ruleset with { Flags = AiScriptedStates.Ruleset.Flags with
            { AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed } };
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            [Fx.Nation("north", treasury: 10_000, capitalCityId: hasCapital ? "cap" : null), Fx.Nation("south")],
            cities, new[] { actor }.Concat(others ?? [])), "north");
        state = state with { MercenaryPool = ValueList.From(offers ?? []),
            Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.Alliance) };
        return new(state, rules, world, "north");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Faithful_resupply_keeps_the_raw_adjacent_or_blocked_pick(bool faithful, bool blocked)
    {
        var actor = Actor();
        var cap = City("cap", blocked ? 235 : 231, 80);
        var far = City("far", 235, 90);
        var view = View(faithful, actor, [cap, far], edit: cells =>
        {
            if (blocked) for (var y = 79; y <= 81; y++) cells[y * 320 + 233] = 0;
        });
        var raw = AiArmyTargetTree.ScoreResupplyCity(view, actor, atWar: false);
        var result = AiArmyTargetTree.ResolveResupplyCity(view, actor, atWar: false);
        if (faithful) Assert.Equal(raw, result);
        else Assert.NotEqual(cap.Id, result.City?.Id);
        if (faithful) Assert.Equal(cap.Id, result.City?.Id);
    }

    [Theory]
    [InlineData(true, 9, 232, 80)]
    [InlineData(true, 10, 230, 60)]
    [InlineData(true, 11, 230, 60)]
    [InlineData(false, 9, 232, 80)]
    [InlineData(false, 10, 232, 80)]
    [InlineData(false, 11, 230, 60)]
    public void Garrison_near_capital_boundary_is_strict_only_in_Faithful(bool faithful, int distance, int x, int y)
    {
        var actor = Actor();
        var guard = actor with { Id = "guard", X = 230 + distance, Y = 60, Moves = 0 };
        var view = View(faithful, actor, [City("cap", 230, 60), City("near", 232, 80, "south")], [guard]);
        Assert.Equal((x, y), AiArmyTargetTree.GarrisonFallback(view, actor, view.State));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Garrison_ties_follow_city_table_in_Faithful_and_ID_in_Improved(bool faithful, bool reverse)
    {
        var actor = Actor();
        var z = City("z-first", 225, 80);
        var a = City("a-last", 235, 80, "south");
        var cities = reverse ? new[] { a, z, City("cap", 230, 72) } : new[] { z, a, City("cap", 230, 72) };
        var view = View(faithful, actor, cities);
        var expected = faithful ? cities[0] : a;
        Assert.Equal((expected.X, expected.Y), AiArmyTargetTree.GarrisonFallback(view, actor, view.State));
    }

    [Theory]
    [InlineData(true, 19, true)]
    [InlineData(true, 20, false)]
    [InlineData(true, 21, false)]
    [InlineData(false, 19, true)]
    [InlineData(false, 20, true)]
    [InlineData(false, 21, false)]
    public void Offer_range_is_strict_only_in_Faithful(bool faithful, int distance, bool selected)
    {
        var actor = Actor();
        var offerCity = City("offer", 230 + distance, 80);
        var view = View(faithful, actor, [City("cap", 230, 60), offerCity],
            offers: [new(0, offerCity.X, offerCity.Y, 1, "light_infantry", 1000, 5)]);
        Assert.Equal(selected ? offerCity : null, AiArmyTargetTree.MercenaryRunDestination(view, actor));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Offer_ties_use_pool_slot_independently_of_city_records(bool reverseCities, bool reverseAssignments)
    {
        var actor = Actor();
        var z = City("z-city", 235, 80);
        var a = City("a-city", 230, 85);
        var first = reverseAssignments ? a : z;
        var second = reverseAssignments ? z : a;
        var cities = reverseCities ? new[] { a, z } : new[] { z, a };
        // Native SlotIndex identifies the original fixed pool slot, even in a reordered JSON list.
        var offers = new[] { new MercenaryPoolSlot(1, second.X, second.Y, 1, "light_infantry", 1000, 5),
            new MercenaryPoolSlot(0, first.X, first.Y, 1, "light_infantry", 1000, 5) };
        Assert.Equal(first, AiArmyTargetTree.MercenaryRunDestination(View(true, actor, cities, offers: offers), actor));
        Assert.Equal(a, AiArmyTargetTree.MercenaryRunDestination(View(false, actor, cities, offers: offers), actor));
    }

    [Fact]
    public void Missing_capital_and_offer_city_keep_native_safety_guards()
    {
        var actor = Actor();
        var view = View(true, actor, [], offers: [new(0, 240, 80, 1, "light_infantry", 1000, 5)], hasCapital: false);
        Assert.Null(AiArmyTargetTree.GarrisonFallback(view, actor, view.State));
        Assert.Null(AiArmyTargetTree.MercenaryRunDestination(view, actor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_faithful_proposal_uses_original_swing_or_stayput_instead_of_emergency(bool stayPut)
    {
        var actor = Actor(8);
        var blockers = new[] { 79, 80, 81 }.Select(y => actor with { Id = "block" + y, X = 231, Y = y, Moves = 0 });
        var view = View(true, actor, [City("cap", 235, 80)], blockers,
            edit: cells => { if (stayPut) cells[82 * 320 + 232] = 0; });
        Assert.False(AiArmyTargetTree.IsReachable(view, actor, 235, 80));
        var candidates = new List<AiCandidate>();
        AiMilitaryPhase.Propose(view, AiPersonalityProfile.For(view.Nation, view.Ruleset),
            SplitMix64Rng.ForStream(1, "policy"), [], candidates);
        var march = Assert.Single(candidates.SelectMany(c => c.Commands).OfType<MoveArmyCommand>(), c => c.ArmyId == actor.Id);
        Assert.Equal((235, 80, MoveWalkPolicy.AiContinuation), (march.TargetX ?? march.X, march.TargetY ?? march.Y, march.Walk));
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, NullEventSink.Instance);
        var result = dispatch.Dispatch(view.State, march);
        Assert.True(result.IsAccepted);
        var expected = dispatch.Dispatch(view.State, new MoveArmyCommand("north", actor.Id, 235, 80,
            MoveWalkPolicy.AiContinuation, 235, 80));
        Assert.Equal(expected.State, result.State);
        Assert.Equal(0, result.State.ArmyById(actor.Id)!.Moves);
        if (stayPut) Assert.Equal((230, 80), (result.State.ArmyById(actor.Id)!.X, result.State.ArmyById(actor.Id)!.Y));
        Assert.Null(AiArmyTargetTree.EmergencyMoveDestination(view, actor, view.State));
    }

    private sealed class Trace(ICommandDispatch inner) : ICommandDispatch
    {
        public List<MoveArmyCommand> Moves { get; } = new();
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is MoveArmyCommand march) Moves.Add(march);
            return inner.Dispatch(state, command, events);
        }
    }

    [Fact]
    public void A_blocked_raw_resupply_order_prepares_the_original_first_waypoint()
    {
        var actor = Actor();
        var view = View(true, actor, [City("cap", 235, 80), City("other", 230, 90)],
            edit: cells => { for (var y = 79; y <= 81; y++) cells[y * 320 + 233] = 0; });
        var origin = new GridPoint(actor.X, actor.Y);
        var goal = new GridPoint(235, 80);
        Assert.False(ClassicalArmyNavigation.DirectLine(view, origin, goal));
        var first = ClassicalArmyNavigation.Intermediate(view, origin, goal).Point;
        Assert.NotNull(first);
        var candidates = new List<AiCandidate>();
        AiMilitaryPhase.Propose(view, AiPersonalityProfile.For(view.Nation, view.Ruleset),
            SplitMix64Rng.ForStream(1, "blocked-policy"), [], candidates);
        var move = Assert.Single(candidates.SelectMany(c => c.Commands).OfType<MoveArmyCommand>(), c => c.ArmyId == actor.Id);
        Assert.Equal(goal, new GridPoint(move.TargetX!.Value, move.TargetY!.Value));
        Assert.Equal(first.Value, new GridPoint(move.X, move.Y));
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, NullEventSink.Instance);
        var expected = dispatch.Dispatch(view.State, new MoveArmyCommand("north", actor.Id, first.Value.X, first.Value.Y,
            MoveWalkPolicy.AiContinuation, goal.X, goal.Y));
        Assert.Equal(expected.State, dispatch.Dispatch(view.State, move).State);
    }

    [Fact]
    public void Post_execution_garrison_fallback_also_uses_original_continuation()
    {
        var actor = Actor(20);
        var view = View(true, actor, [City("cap", 229, 79)]);
        var events = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, events));
        var turn = AiTurn.Run(view.State, view.Ruleset, view.World, trace, SplitMix64Rng.ForStream(1, "policy-turn"), events);
        var moves = trace.Moves.Where(c => c.ArmyId == actor.Id).ToArray();
        Assert.Equal(2, moves.Length);
        Assert.All(moves, c => Assert.Equal((229, 79, MoveWalkPolicy.AiContinuation), (c.TargetX ?? c.X, c.TargetY ?? c.Y, c.Walk)));
        var end = turn.State.ArmyById(actor.Id)!;
        Assert.Equal((230, 80, 6), (end.X, end.Y, end.Moves));
        Assert.Equal(0, turn.CommandsRejected);
        Assert.DoesNotContain(turn.Log, s => s.Contains("emergency move", StringComparison.Ordinal));
    }
}
