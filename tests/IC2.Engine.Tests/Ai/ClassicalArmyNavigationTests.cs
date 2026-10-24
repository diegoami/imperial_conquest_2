using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Ai;

/// <summary>Public, synthetic controls from the navigation187 matrix; no original files are loaded.</summary>
public sealed class ClassicalArmyNavigationTests
{
    private static AiView View(int defaultCode, params (int X, int Y, int Code)[] patches)
    {
        var original = RealGameData.World;
        var cells = Enumerable.Repeat(defaultCode, original.Width * original.Height).ToArray();
        foreach (var (x, y, code) in patches) cells[y * original.Width + x] = code;
        var runs = new List<TerrainRun>();
        for (var i = 0; i < cells.Length;)
        {
            var start = i;
            var code = cells[i];
            while (i < cells.Length && cells[i] == code) i++;
            runs.Add(new TerrainRun(code, i - start));
        }
        var world = original with
        {
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(runs)),
        };
        var state = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario) with
        {
            Cities = ValueList<CityState>.Empty,
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList<FleetState>.Empty,
        };
        return new AiView(state, RealGameData.Ruleset, world, state.ActiveNationId);
    }

    [Fact]
    public void Direct_line_ignores_the_final_medium_but_not_an_intermediate_sea_cell()
    {
        var origin = new GridPoint(230, 80);
        var target = new GridPoint(240, 90);
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2), origin, target));
        Assert.False(ClassicalArmyNavigation.DirectLine(View(2, (235, 85, 0)), origin, target));
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2, (235, 85, 203)), origin, target));
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2, (240, 90, 0)), origin, target));
        Assert.True(ClassicalArmyNavigation.DirectLine(View(2, (231, 81, 0)), origin,
            new GridPoint(231, 81)));
    }

    [Fact]
    public void A_single_medium_change_selects_the_original_intermediate_instead_of_the_direct_goal()
    {
        var origin = new GridPoint(230, 80);
        var target = new GridPoint(240, 90);
        var view = View(2, (235, 85, 0));

        Assert.True(ClassicalArmyNavigation.Classify(origin.X, origin.Y)
            == ClassicalArmyNavigation.Classify(target.X, target.Y));
        Assert.False(ClassicalArmyNavigation.DirectLine(view, origin, target));
        var choice = ClassicalArmyNavigation.Intermediate(view, origin, target);
        Assert.Equal(new GridPoint(221, 94), choice.Point);
        Assert.Equal(32, choice.NodeId);
    }

    [Fact]
    public void Route_rows_favor_primary_links_then_the_hub_and_keep_first_ties()
    {
        Assert.Equal(26, ClassicalArmyNavigation.RouteEntry(1, [26, 33], 31).NodeId);
        Assert.Equal(31, ClassicalArmyNavigation.RouteEntry(1, [31, 33], 32).NodeId);
        Assert.Equal(39, ClassicalArmyNavigation.RouteEntry(1, [26, 39], 36).NodeId);
        Assert.Throws<NotSupportedException>(() => ClassicalArmyNavigation.RouteEntry(3, [55], 56));
    }

    [Fact]
    public void A_sea_wall_without_nodes_or_local_land_ray_has_no_waypoint()
    {
        var patches = new List<(int X, int Y, int Code)>();
        for (var x = 229; x <= 231; x++)
            for (var y = 79; y <= 81; y++) patches.Add((x, y, 2));
        for (var x = 239; x <= 241; x++)
            for (var y = 89; y <= 91; y++) patches.Add((x, y, 2));
        var view = View(0, patches.ToArray());

        var choice = ClassicalArmyNavigation.Intermediate(view,
            new GridPoint(230, 80), new GridPoint(240, 90));
        Assert.Null(choice.Point);
    }

    [Fact]
    public void Any_own_fleet_opens_the_geographic_target_gate_but_only_a_usable_one_can_ferry()
    {
        var baseView = View(2);
        var initial = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario);
        var actor = initial.Armies.First(a => a.Nation == "seleucid") with { X = 100, Y = 40 };
        var target = new GridPoint(230, 90);
        var own = initial.Fleets[0] with
        {
            Id = "usable", Nation = actor.Nation, X = 105, Y = 40,
            ConstructionTicksRemaining = null, CarriedArmyId = null, Moves = 25,
        };
        AiView With(params FleetState[] fleets) => new(
            baseView.State with { Armies = ValueList.Of(actor), Fleets = ValueList.From(fleets) },
            baseView.Ruleset, baseView.World, actor.Nation);

        Assert.NotEqual(ClassicalArmyNavigation.Classify(actor.X, actor.Y),
            ClassicalArmyNavigation.Classify(target.X, target.Y));
        Assert.False(ClassicalArmyNavigation.TargetEligible(With(), actor, target.X, target.Y));
        var building = own with { Id = "building", ConstructionTicksRemaining = 5 };
        var zero = own with { Id = "zero", Moves = 0 };
        var loaded = own with { Id = "loaded", CarriedArmyId = actor.Id };
        Assert.True(ClassicalArmyNavigation.TargetEligible(With(building), actor, target.X, target.Y));
        Assert.Null(ClassicalArmyNavigation.NearestUsableFerry(With(building, zero, loaded), actor));
        var tied = own with { Id = "tied", X = 100, Y = 45 };
        Assert.Equal(own.Id, ClassicalArmyNavigation.NearestUsableFerry(
            With(building, own, tied), actor)?.Id);
    }

    [Fact]
    public void The_public_Seleucid_control_selects_the_blocked_line_target_and_its_waypoint()
    {
        var world = RealGameData.World;
        var ruleset = RealGameData.Ruleset;
        var state = GameStateFactory.CreateInitial(world, ruleset, RealGameData.Scenario);
        var actor = state.Armies.First(a => a.Nation == "seleucid") with
        {
            X = 219, Y = 93, Moves = 7,
        };
        var enemy = state.Armies.First(a => a.Nation == "ptolemaic") with
        {
            X = 191, Y = 95,
        };
        state = state with
        {
            Armies = ValueList.Of(actor, enemy),
            Cities = ValueList<CityState>.Empty,
            Fleets = ValueList<FleetState>.Empty,
            ActiveSeatIndex = Array.IndexOf(state.TurnOrder.ToArray(), actor.Nation),
            Relations = state.Relations.WithRelation(actor.Nation, enemy.Nation,
                ruleset.Diplomacy.StateCodes.War),
        };
        var view = new AiView(state, ruleset, world, actor.Nation);

        Assert.True(ClassicalArmyNavigation.Applies(view));
        Assert.Equal(ClassicalArmyNavigation.Classify(actor.X, actor.Y),
            ClassicalArmyNavigation.Classify(enemy.X, enemy.Y));
        Assert.True(ClassicalArmyNavigation.TargetEligible(view, actor, enemy.X, enemy.Y));
        Assert.False(ClassicalArmyNavigation.DirectLine(view,
            new GridPoint(actor.X, actor.Y), new GridPoint(enemy.X, enemy.Y)));
        Assert.Equal(enemy.Id, AiArmyTargetTree.ScoreArmyTarget(view, actor).Army?.Id);
        Assert.Equal(new GridPoint(189, 104), ClassicalArmyNavigation.Intermediate(view,
            new GridPoint(actor.X, actor.Y), new GridPoint(enemy.X, enemy.Y)).Point);

        var candidates = new List<AiCandidate>();
        AiMilitaryPhase.Propose(view, AiPersonalityProfile.For(state.NationById(actor.Nation)!, ruleset),
            SplitMix64Rng.ForStream(1, "ai.turn"), Array.Empty<string>(), candidates);
        var move = candidates.SelectMany(c => c.Commands).OfType<MoveArmyCommand>()
            .Single(command => command.ArmyId == actor.Id && command.X == 189 && command.Y == 104);
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), ruleset, world,
            NullEventSink.Instance);
        var result = dispatcher.Dispatch(state, move);
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal((212, 96, 0), (result.State.ArmyById(actor.Id)!.X,
            result.State.ArmyById(actor.Id)!.Y, result.State.ArmyById(actor.Id)!.Moves));
    }
}
