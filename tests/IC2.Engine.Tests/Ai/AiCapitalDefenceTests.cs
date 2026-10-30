using IC2.Engine.Ai;
using IC2.Engine.Armies;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed partial class AiCapitalDefenceTests
{
    private static Ruleset Rules => AiScriptedStates.Ruleset with
    {
        Flags = AiScriptedStates.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original },
    };

    private static World World(bool sea = false) => AiScriptedStates.World with
    {
        Id = "classical-mediterranean", Width = 320, Height = 140,
        Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(sea ? 0 : 2, 320 * 140))),
    };

    private static ArmyState Army(string id, string nation, int x, int y, int moves = 4) =>
        Fx.Army(id, nation, x, y, 60, Fx.Unit("light_infantry", 30_000)) with { Moves = moves };

    private static CityState Capital => AiReachableFallbackTests.City("cap", "north", 100, 40);

    private static GameState State(IEnumerable<ArmyState> armies, bool war = true, CityState? capital = null)
    {
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            [Fx.Nation("north", treasury: 10_000, capitalCityId: "cap"), Fx.Nation("south", capitalCityId: "other")],
            [capital ?? Capital, AiReachableFallbackTests.City("other", "south", 200, 40)], armies), "north");
        return war ? state with { Relations = state.Relations.WithRelation("north", "south", Rules.Diplomacy.StateCodes.War) } : state;
    }

    private static AiView View(GameState state) => new(state, Rules, World(), "north");

    private static AiCapitalDefence.Outcome Run(GameState state, out List<string> log, World? world = null, Ruleset? rules = null)
    {
        log = new();
        var loadedWorld = world ?? World();
        var loadedRules = rules ?? Rules;
        return AiCapitalDefence.Run(state, loadedRules, loadedWorld, "north",
            new CommandDispatcher(SystemRegistry.FromEngineAssembly(), loadedRules, loadedWorld, NullEventSink.Instance),
            NullEventSink.Instance, log);
    }

    [Theory]
    [InlineData(true, 19, true)]
    [InlineData(true, 20, false)]
    [InlineData(false, 9, true)]
    [InlineData(false, 10, false)]
    public void Threat_radius_has_the_original_strict_outer_boundary(bool war, int radius, bool included)
    {
        var state = State([Army("enemy", "south", 100 + radius, 40)], war);
        Assert.Equal(included, AiCapitalDefence.Threats(View(state), Capital).Contains("enemy"));
    }

    [Fact]
    public void Only_foreign_armies_in_the_capitals_region_and_coast_side_are_listed()
    {
        var capital = Capital with { X = 60, Y = 55 };
        var state = State([Army("own", "north", 61, 55), Army("in", "south", 61, 55),
            Army("out", "south", 57, 55)], capital: capital);
        Assert.Equal(new[] { "in" }, AiCapitalDefence.Threats(View(state), capital));

        // Find the coast-side boundary locally without reproducing the coordinate tables in the fixture.
        var differentSide = (from x in Enumerable.Range(1, 318)
            from y in Enumerable.Range(1, 138)
            let here = ClassicalArmyNavigation.Classify(x, y)
            let next = ClassicalArmyNavigation.Classify(x + 1, y)
            where here.Region == next.Region && here.Side != next.Side
            select (x, y)).First();
        capital = Capital with { X = differentSide.x, Y = differentSide.y };
        state = State([Army("coast", "south", capital.X + 1, capital.Y)], capital: capital);
        Assert.Empty(AiCapitalDefence.Threats(View(state), capital));
    }

    [Fact]
    public void First_nine_records_win_admission_even_when_the_tenth_is_nearest()
    {
        var responder = Army("ours", "north", 100, 41);
        var enemies = Enumerable.Range(0, 9).Select(i => Army("far-" + i, "south", 110 + i, 40)).ToArray();
        var near = Army("near", "south", 101, 41);
        var state = State(new[] { responder }.Concat(enemies).Append(near));
        var threats = AiCapitalDefence.Threats(View(state), Capital);
        Assert.Equal(9, threats.Count);
        Assert.DoesNotContain("near", threats);
        Assert.NotEqual("near", AiCapitalDefence.NearestThreat(View(state), responder, Capital, threats)!.Id);
        state = state with { Armies = ValueList.From(new[] { responder, near }.Concat(enemies)) };
        threats = AiCapitalDefence.Threats(View(state), Capital);
        Assert.Equal("near", AiCapitalDefence.NearestThreat(View(state), responder, Capital, threats)!.Id);
    }

    [Fact]
    public void Equal_distance_uses_table_order_and_a_sweep_changes_the_winner()
    {
        var responder = Army("ours", "north", 100, 41);
        var a = Army("z-first", "south", 97, 41);
        var b = Army("a-last", "south", 103, 41);
        var state = State([responder, a, b]);
        string Pick(GameState s) => AiCapitalDefence.NearestThreat(View(s), responder, Capital,
            AiCapitalDefence.Threats(View(s), Capital))!.Id;
        Assert.Equal(a.Id, Pick(state));
        Assert.Equal(b.Id, Pick(state with { Armies = ValueList.Of(responder, b, a) }));
        state = state with { DeadArmySlots = ValueList.Of(0) };
        Assert.Equal(b.Id, Pick(ArmyTable.Sweep(state)));
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    public void Peaceful_threat_is_eligible_only_twenty_tiles_from_home(int distance, bool eligible)
    {
        var responder = Army("ours", "north", 100 - distance, 40);
        var state = State([responder, Army("neutral", "south", 99, 40)], war: false);
        var threat = AiCapitalDefence.NearestThreat(View(state), responder, Capital,
            AiCapitalDefence.Threats(View(state), Capital));
        Assert.Equal(eligible, threat is not null);
        if (threat is not null) Assert.Equal(new GridPoint(100, 40), AiCapitalDefence.Destination(View(state), threat, Capital));
    }

    [Theory]
    [InlineData(true, true, 100)]
    [InlineData(true, false, 105)]
    [InlineData(false, false, 100)]
    public void The_chosen_threats_covered_cell_and_relation_decide_home_or_interception(bool war, bool aboard, int x)
    {
        var threat = Army("threat", "south", 105, 40) with
            { CoveredTileCode = aboard ? null : 2, AboardFleetId = aboard ? "enemy-fleet" : null };
        var state = State([threat], war);
        Assert.Equal(new GridPoint(x, 40), AiCapitalDefence.Destination(View(state), threat, Capital));
    }

    [Theory]
    [InlineData(13, 4, 101, 3, 0, 0, true)]
    [InlineData(12, 4, 101, 3, 0, 0, false)]
    [InlineData(13, 4, 100, 3, 0, 0, false)]
    [InlineData(13, 4, 101, 4, 0, 0, false)]
    [InlineData(13, 4, 0, 0, 101, 3, true)]
    public void Offensive_skip_keeps_score_distance_and_capital_boundaries(int home, int moves,
        long armyScore, int armyDistance, long cityScore, int cityDistance, bool skipped) =>
        Assert.Equal(skipped, AiCapitalDefence.CommittedToOffence(home, moves, armyScore, armyDistance, cityScore, cityDistance));

    [Fact]
    public void No_threat_or_no_eligible_threat_does_not_dispatch()
    {
        var responder = Army("ours", "north", 100, 41);
        var state = State([responder]);
        Assert.Equal(new AiCapitalDefence.Outcome(state, 0, 0, 0), Run(state, out _));
        state = State([responder, Army("neutral", "south", 105, 40)], war: false);
        Assert.Equal(new AiCapitalDefence.Outcome(state, 0, 0, 0), Run(state, out _));
    }

    [Fact]
    public void A_real_qualifying_offensive_pick_far_from_home_is_not_preempted()
    {
        var responder = Army("ours", "north", 130, 40);
        var target = Army("offensive", "south", 129, 40) with { Units = ValueList.Of(Fx.Unit("light_infantry", 1_000)) };
        var state = State([responder, Army("homeland-threat", "south", 105, 40), target]);
        Assert.Equal(new[] { "homeland-threat" }, AiCapitalDefence.Threats(View(state), Capital));
        var attack = AiArmyTargetTree.ScoreArmyTarget(View(state), responder);
        Assert.Equal(target.Id, attack.Army!.Id);
        Assert.True(attack.Score > 100);
        Assert.True(attack.Distance < responder.Moves);
        Assert.Equal(new AiCapitalDefence.Outcome(state, 0, 0, 0), Run(state, out _));
    }

    [Fact]
    public void Responders_are_taken_in_table_order_and_dispatches_stop_at_threat_count_plus_two()
    {
        var state = State(Enumerable.Range(0, 5).Select(i => Army("own-" + i, "north", 100, 41 + i))
            .Append(Army("threat", "south", 110, 40)));
        var result = Run(state, out var log);
        Assert.Equal(3, result.Dispatches);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(3, result.Issued);
        Assert.Equal(new[] { "own-0", "own-1", "own-2" }, log.Select(l => l.Split(' ')[2]));
        Assert.Equal(state.ArmyById("own-3"), result.State.ArmyById("own-3"));
    }

    [Fact]
    public void No_op_dispatches_count_toward_the_limit_but_not_as_substantive_commands()
    {
        // Adjacent same-cell home fallback: no movement, but it is still a dispatch.
        var state = State(Enumerable.Range(0, 4).Select(i => Army("own-" + i, "north", 100, 40))
            .Append(Army("aboard-threat", "south", 105, 40) with { CoveredTileCode = null, AboardFleetId = "enemy" }));
        var result = Run(state, out _, World() with { Id = "synthetic-single-walk" });
        Assert.Equal(3, result.Dispatches);
        Assert.Equal(0, result.Issued);
        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public void An_aboard_responder_queues_a_landing_and_spends_two_without_sailing_in_the_prepass()
    {
        var responder = Army("ours", "north", 95, 40) with { CoveredTileCode = null, AboardFleetId = "carrier" };
        var fleet = new FleetState("carrier", "north", 95, 40, 3, 50, 100, 100, 100, null, null, responder.Id, 0);
        var state = State([responder, Army("threat", "south", 105, 40)]) with { Fleets = ValueList.Of(fleet) };
        var world = LandingWorld();
        var events = new RecordingEventSink();
        var routes = new OriginalFleetRoutes();
        var log = new List<string>();
        var result = AiCapitalDefence.Run(state, Rules, world, "north",
            new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Rules, world, events),
            routes.Capture(events), log);
        Assert.Equal(1, result.Dispatches);
        Assert.Equal(1, result.Issued);
        Assert.Equal(0, result.Rejected);
        Assert.Contains("toward (105, 40)", Assert.Single(log));
        var moved = result.State.FleetById(fleet.Id)!;
        Assert.Equal(fleet, moved);
        Assert.Equal(responder.Moves - 2, result.State.ArmyById(responder.Id)!.Moves);
        Assert.Equal(new GridPoint(100, 40), routes.For(fleet.Id));
        Assert.Equal((moved.X, moved.Y), (result.State.ArmyById(responder.Id)!.X, result.State.ArmyById(responder.Id)!.Y));
        Assert.Equal(responder.Id, moved.CarriedArmyId);
        Assert.Equal(fleet.Id, result.State.ArmyById(responder.Id)!.AboardFleetId);
    }

    [Fact]
    public void The_designed_policy_and_missing_capital_have_no_prepass()
    {
        var state = State([Army("ours", "north", 95, 40), Army("enemy", "south", 105, 40)]);
        Assert.Equal(new AiCapitalDefence.Outcome(state, 0, 0, 0), Run(state, out _, rules: AiScriptedStates.Ruleset));
        state = state with { Cities = ValueList<CityState>.Empty };
        Assert.Equal(new AiCapitalDefence.Outcome(state, 0, 0, 0), Run(state, out _));
    }

    [Fact]
    public void An_exhausted_responder_is_skipped_but_zero_carrier_moves_do_not_suppress_the_army_request()
    {
        var tired = Army("tired", "north", 95, 40, moves: 0);
        var aboard = Army("aboard", "north", 96, 40) with { AboardFleetId = "fleet", CoveredTileCode = null };
        var fleet = new FleetState("fleet", "north", 96, 40, 0, 50, 100, 100, 100, null, null, aboard.Id, 0);
        var state = State([tired, aboard, Army("enemy", "south", 105, 40)]) with { Fleets = ValueList.Of(fleet) };
        var result = Run(state, out _, LandingWorld());
        Assert.Equal(1, result.Dispatches);
        Assert.Equal(1, result.Issued);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(tired, result.State.ArmyById(tired.Id));
        Assert.Equal(fleet, result.State.FleetById(fleet.Id));
        Assert.Equal(aboard.Moves - 2, result.State.ArmyById(aboard.Id)!.Moves);
    }

    private static World LandingWorld()
    {
        var cells = new int[320 * 140];
        for (var y = 0; y < 140; y++)
            for (var x = 100; x < 320; x++) cells[y * 320 + x] = 2;
        return World(sea: true) with
        {
            Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
        };
    }

    [Fact]
    public void Real_turn_runs_defence_once_before_support_and_the_regenerated_target_loop()
    {
        var army = Army("ours", "north", 110, 40, moves: 3) with { SupplyTons = 0, Money = 0 };
        var state = State([army, Army("enemy", "south", 105, 40)]);
        var supplyCity = AiReachableFallbackTests.City("support", "north", 104, 41, supply: 5_000);
        state = state with { Cities = ValueList.From(state.Cities.Append(supplyCity)) };
        var rules = Rules with { Ai = Rules.Ai with { MaxActionsPerTurn = 2 } };
        var world = World();
        var log = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, log);
        var turn = AiTurn.Run(state, rules, world, dispatcher, SplitMix64Rng.ForStream(1, "capital-test"), log);
        Assert.Equal(0, turn.CommandsRejected);
        Assert.Single(turn.Log, l => l.StartsWith("capital defence:"));
        Assert.True(turn.State.ArmyById(army.Id)!.SupplyTons > 0);
        Assert.True(turn.State.CityById(supplyCity.Id)!.SupplyTons < supplyCity.SupplyTons);
        Assert.True(turn.Log.ToList().FindIndex(l => l.StartsWith("capital defence:"))
            < turn.Log.ToList().FindIndex(l => l.StartsWith("resupply:")));
        Assert.True(turn.CommandsIssued > 0);
    }
}
