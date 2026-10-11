using IC2.Engine.Ai;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T158 (#958): the faithful economy's last two sub-phases, on hand-built states: ordering a new fleet
/// (FUN_00450768) and consolidating army units (FUN_00450858). Every gate has a test that fails when it
/// moves by one.
/// </summary>
public sealed class AiFaithfulFleetAndConsolidationTests
{
    private const string Me = "a";
    private const string Foe = "b";

    private static Ruleset Rules(Func<NavalRules, NavalRules>? naval = null, Func<AiWeightsRules, AiWeightsRules>? ai = null)
    {
        var toy = BattleCommandTestbed.ToyRuleset;
        var rules = toy with { Flags = toy.Flags with { AiEconomy = AiEconomyModel.Original } };
        if (naval is not null)
        {
            rules = rules with { Naval = naval(rules.Naval) };
        }

        return ai is null ? rules : rules with { Ai = ai(rules.Ai) };
    }

    // Row 0 is sea (code 0); every other row is plain (code 2). A city at y = 1 is coastal; y >= 3 is not.
    private static World SeaStripWorld() => BattleCommandTestbed.ToyWorld with
    {
        Width = 40,
        Height = 20,
        Terrain = new TerrainGrid(
            TerrainEncoding.RunLength,
            ValueList.From(new[] { new TerrainRun(0, 40), new TerrainRun(2, 760) })),
    };

    private static CityState City(string id, int x, int y, string owner = Me) =>
        CaptureFixtures.City(id, id, x, y, owner, owner, 80, 0, 10, 100, 10);

    /// <summary>The nation's cities: an inland capital at (20, 4) plus <paramref name="extra"/> inland fillers and the given ports.</summary>
    private static List<CityState> Cities(int total, params CityState[] ports)
    {
        var cities = new List<CityState> { City("a-cap", 20, 4) };
        cities.AddRange(ports);
        for (var n = 0; cities.Count < total; n++)
        {
            cities.Add(City($"f{n:D3}", n % 40, 8 + (n / 40)));
        }

        return cities;
    }

    private static FleetState Fleet(string id, string nation, bool underConstruction, string? buildCity = null) =>
        new(id, nation, 0, 0, 0, 20, 100, 0, 0, underConstruction ? 12 : null, buildCity, null, null);

    private static GameState Build(
        IEnumerable<CityState> cities,
        int treasury = 5000,
        IEnumerable<FleetState>? fleets = null,
        IEnumerable<ArmyState>? armies = null)
    {
        var mine = CaptureFixtures.Nation(Me, treasury: treasury, unity: 600, wealth: 1_000_000, capitalCityId: "a-cap");
        var foe = CaptureFixtures.Nation(Foe, capitalCityId: "b-cap");
        var all = cities.Concat(new[] { City("b-cap", 39, 19, Foe) });
        return BattleCommandTestbed.StateWith(new[] { mine, foe }, all, armies, fleets) with
        {
            TurnOrder = ValueList.From(new[] { Me, Foe }),
            ActiveSeatIndex = 0,
        };
    }

    private static CityState P(string id, int x, int y = 1) => City(id, x, y);

    private static (GameState State, int Issued, int Rejected) OrderFleet(GameState state, Ruleset? rules = null)
    {
        rules ??= Rules();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), rules, SeaStripWorld(), NullEventSink.Instance);
        return AiFaithfulEconomy.OrderFleet(
            state, rules, SeaStripWorld(), dispatcher, NullEventSink.Instance, Me, new List<string>());
    }

    private static (GameState State, int Issued, int Rejected) Consolidate(GameState state)
    {
        var rules = Rules();
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), rules, SeaStripWorld(), NullEventSink.Instance);
        return AiFaithfulEconomy.ConsolidateUnits(
            state, rules, dispatcher, NullEventSink.Instance, Me, new List<string>());
    }

    private static List<FleetState> OwnNew(GameState before, GameState after) =>
        after.Fleets.Where(f => before.FleetById(f.Id) is null).ToList();

    // ---------------------------------------------------------------- Done-when 1: the gates

    [Fact]
    public void A_rich_nation_with_no_fleets_orders_exactly_one_fleet()
    {
        var state = Build(Cities(3, P("p1", 24)));
        var (after, issued, rejected) = OrderFleet(state);
        Assert.Equal(1, issued);
        Assert.Equal(0, rejected);
        Assert.Single(OwnNew(state, after));
    }

    [Fact]
    public void The_game_wide_fleet_count_gates_at_99_counting_other_nations_fleets()
    {
        // Own fleets 0 so the score is 0; the other 98 fleets are the foe's.
        FleetState[] Others(int n) => Enumerable.Range(0, n).Select(i => Fleet($"o{i}", Foe, false)).ToArray();

        var ninetyEight = OrderFleet(Build(Cities(3, P("p1", 24)), fleets: Others(98)));
        Assert.Equal(1, ninetyEight.Issued);

        var ninetyNine = OrderFleet(Build(Cities(3, P("p1", 24)), fleets: Others(99)));
        Assert.Equal(0, ninetyNine.Issued);
    }

    [Fact]
    public void The_treasury_gates_above_3000()
    {
        Assert.Equal(1, OrderFleet(Build(Cities(3, P("p1", 24)), treasury: 3001)).Issued);
        Assert.Equal(0, OrderFleet(Build(Cities(3, P("p1", 24)), treasury: 3000)).Issued);
    }

    [Fact]
    public void The_scores_weights_are_one_per_launched_fleet_and_four_per_fleet_under_construction()
    {
        var rules = Rules();
        var launched = Build(Cities(3), fleets: new[] { Fleet("l1", Me, false) });
        var building = Build(Cities(3), fleets: new[] { Fleet("c1", Me, true, "elsewhere") });
        var foreign = Build(Cities(3), fleets: new[] { Fleet("o1", Foe, false), Fleet("o2", Foe, true, "x") });

        Assert.Equal(1, AiFaithfulEconomy.FleetScore(launched, rules, Me));
        Assert.Equal(4, AiFaithfulEconomy.FleetScore(building, rules, Me));
        Assert.Equal(0, AiFaithfulEconomy.FleetScore(foreign, rules, Me));
    }

    [Fact]
    public void Score_1_orders_only_with_more_than_50_cities()
    {
        var launched = new[] { Fleet("l1", Me, false) };
        Assert.Equal(1, OrderFleet(Build(Cities(51, P("p1", 24)), fleets: launched)).Issued);
        Assert.Equal(0, OrderFleet(Build(Cities(50, P("p1", 24)), fleets: launched)).Issued);
    }

    [Fact]
    public void Score_2_orders_only_with_more_than_100_cities()
    {
        var two = new[] { Fleet("l1", Me, false), Fleet("l2", Me, false) };
        Assert.Equal(1, OrderFleet(Build(Cities(101, P("p1", 24)), fleets: two)).Issued);
        Assert.Equal(0, OrderFleet(Build(Cities(100, P("p1", 24)), fleets: two)).Issued);
    }

    [Fact]
    public void Score_0_orders_with_a_single_city_and_three_launched_fleets_never_order()
    {
        Assert.Equal(1, OrderFleet(Build(Cities(2, P("p1", 24)))).Issued);
        var three = Enumerable.Range(0, 3).Select(i => Fleet($"l{i}", Me, false)).ToArray();
        Assert.Equal(0, OrderFleet(Build(Cities(200, P("p1", 24)), fleets: three)).Issued);
    }

    [Fact]
    public void One_own_fleet_under_construction_blocks_a_new_order()
    {
        var building = new[] { Fleet("c1", Me, true, "elsewhere") };
        Assert.Equal(0, OrderFleet(Build(Cities(200, P("p1", 24)), fleets: building)).Issued);
    }

    [Fact]
    public void At_most_one_order_a_turn()
    {
        var state = Build(Cities(3, P("p1", 24), P("p2", 26)));
        var (after, issued, _) = OrderFleet(state);
        Assert.Equal(1, issued);
        Assert.Single(OwnNew(state, after));
    }

    // ---------------------------------------------------------------- Done-when 2: size and port

    [Theory]
    [InlineData(2, 52)]
    [InlineData(49, 99)]
    [InlineData(50, 100)]
    [InlineData(60, 100)]
    public void The_ships_are_the_smaller_of_100_and_cities_plus_50(int cities, int ships)
    {
        // The score is zeroed so the city count does not gate this test.
        var state = Build(Cities(cities, P("p1", 24)));
        var fleet = Assert.Single(OwnNew(state, OrderFleet(state).State));
        Assert.Equal(ships, fleet.Ships);
    }

    [Fact]
    public void The_port_is_the_coastal_city_nearest_the_capital_and_skips_inland_ones()
    {
        // Capital (20, 4). Inland (21, 5) is nearer than any port; coastal (24, 1) is 4 away, (27, 1) is 7.
        var state = Build(Cities(4, P("near", 24), P("far", 27)).Append(City("inland", 21, 5)));
        var fleet = Assert.Single(OwnNew(state, OrderFleet(state).State));
        Assert.Equal("near", fleet.BuildCityId);
    }

    [Fact]
    public void A_city_already_hosting_a_construction_order_is_skipped()
    {
        var state = Build(
            Cities(3, P("near", 24), P("far", 27)),
            fleets: new[] { Fleet("o1", Foe, true, "near") });
        var fleet = Assert.Single(OwnNew(state, OrderFleet(state).State));
        Assert.Equal("far", fleet.BuildCityId);
    }

    [Fact]
    public void Equal_distances_resolve_to_the_lower_city_id_in_either_collection_order()
    {
        // (16, 1) and (24, 1) are both 4 from the capital (20, 4).
        var forward = Build(new[] { City("a-cap", 20, 4), P("zeta", 16), P("alpha", 24) });
        var backward = Build(new[] { City("a-cap", 20, 4), P("alpha", 24), P("zeta", 16) });
        Assert.Equal("alpha", Assert.Single(OwnNew(forward, OrderFleet(forward).State)).BuildCityId);
        Assert.Equal("alpha", Assert.Single(OwnNew(backward, OrderFleet(backward).State)).BuildCityId);
    }

    // ---------------------------------------------------------------- Done-when 4: the human's commands

    [Fact]
    public void An_order_the_command_would_refuse_is_never_proposed()
    {
        // Ships would be 52 but the ruleset's range tops out at 40: no order, nothing refused.
        var rules = Rules(naval: n => n with { OrderMaxShips = 40 });
        var result = OrderFleet(Build(Cities(2, P("p1", 24))), rules);
        Assert.Equal((0, 0), (result.Issued, result.Rejected));

        // No coastal city at all: nothing proposed, nothing refused.
        var inland = OrderFleet(Build(Cities(3)));
        Assert.Equal((0, 0), (inland.Issued, inland.Rejected));
    }

    [Fact]
    public void The_whole_phase_through_Run_is_accepted()
    {
        var state = Build(Cities(3, P("p1", 24)));
        var rules = Rules();
        var world = SeaStripWorld();
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance);
        var result = AiFaithfulEconomy.Run(
            state, rules, world, dispatcher, NullEventSink.Instance, new SplitMix64Rng(3), Me, new List<string>());
        Assert.Equal(0, result.Rejected);
        Assert.Single(result.State.Fleets);
    }

    // ---------------------------------------------------------------- Done-when 3: consolidation

    private static UnitSlot U(string type, int troops, int quality = 6, int label = 0) =>
        new(label, type, troops, quality, "u");

    private static GameState WithArmy(params UnitSlot[] units) =>
        Build(Cities(1), armies: new[] { CaptureFixtures.Army("m", Me, 5, 5, 60, units) });

    private static IReadOnlyList<UnitSlot> Units(GameState s) => s.ArmyById("m")!.Units;

    // light_infantry's standard size is 15000: 2/3 = 10000, 1/3 = 5000, 1/6 = 2500.
    private const string Li = "light_infantry";

    [Fact]
    public void A_regular_below_two_thirds_merges_with_a_later_slot_below_one_third()
    {
        var (after, issued, rejected) = Consolidate(WithArmy(U(Li, 9999, 6), U(Li, 4999, 3)));
        Assert.Equal((1, 0), (issued, rejected));
        var unit = Assert.Single(Units(after));
        Assert.Equal(14998, unit.Troops);
        Assert.Equal(4, unit.Quality); // (6 + 3) / 2
    }

    [Fact]
    public void A_regular_at_exactly_two_thirds_does_not_merge()
    {
        var (after, issued, _) = Consolidate(WithArmy(U(Li, 10000), U(Li, 4999)));
        Assert.Equal(0, issued);
        Assert.Equal(2, Units(after).Count);
    }

    [Fact]
    public void A_partner_at_exactly_one_third_is_not_taken()
    {
        var (after, issued, _) = Consolidate(WithArmy(U(Li, 9999), U(Li, 5000)));
        Assert.Equal(0, issued);
        Assert.Equal(2, Units(after).Count);
    }

    [Fact]
    public void Only_a_later_slot_is_a_partner_and_the_first_qualifying_one_is_taken()
    {
        // The small unit comes first: slot 0 (4000) has no partner after it below 1/3 but slot 1 is big.
        var (before, _, _) = (WithArmy(U(Li, 4000), U(Li, 9000)), 0, 0);
        var (after, issued, _) = Consolidate(before);
        Assert.Equal(0, issued);
        Assert.Equal(2, Units(after).Count);

        // Two candidates after slot 0: the first one (slot 1, 3000) is taken, not slot 2 (1000).
        var (merged, _, _) = Consolidate(WithArmy(U(Li, 9000), U(Li, 3000), U(Li, 1000)));
        Assert.Equal(new[] { 12000, 1000 }, Units(merged).Select(u => u.Troops).ToArray());
    }

    [Fact]
    public void Only_the_same_type_and_regular_units_merge()
    {
        var (a, issuedA, _) = Consolidate(WithArmy(U(Li, 9000), U("archers", 100)));
        Assert.Equal(0, issuedA);
        Assert.Equal(2, Units(a).Count);

        var (b, issuedB, _) = Consolidate(WithArmy(U(Li, 9000), U(Li, 4000, label: 7)));
        Assert.Equal(0, issuedB); // 4000 is above the mercenary 1/6 and a mercenary never joins
        Assert.Equal(2, Units(b).Count);
    }

    [Fact]
    public void A_mercenary_below_one_sixth_is_disbanded_and_at_one_sixth_is_kept()
    {
        var (gone, issued, rejected) = Consolidate(WithArmy(U(Li, 2499, label: 7), U(Li, 15000)));
        Assert.Equal((1, 0), (issued, rejected));
        Assert.Single(Units(gone));

        var (kept, issuedKept, _) = Consolidate(WithArmy(U(Li, 2500, label: 7), U(Li, 15000)));
        Assert.Equal(0, issuedKept);
        Assert.Equal(2, Units(kept).Count);
    }

    [Fact]
    public void An_army_with_one_undersized_mercenary_keeps_it_and_no_order_is_issued()
    {
        var (after, issued, rejected) = Consolidate(WithArmy(U(Li, 100, label: 7)));
        Assert.Equal((0, 0), (issued, rejected));
        Assert.Single(Units(after));
    }

    [Fact]
    public void Disbands_that_would_leave_none_keep_the_last_unit_without_a_refused_order()
    {
        var (after, issued, rejected) = Consolidate(WithArmy(U(Li, 100, label: 7), U(Li, 200, label: 8)));
        Assert.Equal((1, 0), (issued, rejected));
        var last = Assert.Single(Units(after));
        Assert.Equal(200, last.Troops);
    }

    [Fact]
    public void The_slots_are_walked_in_order_across_a_disband_and_a_join()
    {
        // slot 0 merc 100 disbanded; the regulars then shift down and 9000 + 1000 join.
        var (after, issued, rejected) = Consolidate(
            WithArmy(U(Li, 100, label: 7), U(Li, 9000), U(Li, 1000), U("archers", 3500)));
        Assert.Equal((2, 0), (issued, rejected));
        Assert.Equal(new[] { 10000, 3500 }, Units(after).Select(u => u.Troops).ToArray());
    }

    // ---------------------------------------------------------------- Done-when 5: improved is unchanged

    [Fact]
    public void Under_the_designed_economy_the_turn_orders_no_fleet_and_consolidates_nothing()
    {
        var rules = BattleCommandTestbed.ToyRuleset;
        Assert.NotEqual(AiEconomyModel.Original, rules.Flags.AiEconomy);
        var state = Build(
            Cities(3, P("p1", 24)),
            armies: new[] { CaptureFixtures.Army("m", Me, 5, 5, 60, U(Li, 100, label: 7), U(Li, 9000), U(Li, 1000)) });
        var world = SeaStripWorld();
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance);
        var outcome = AiTurn.Run(state, rules, world, dispatcher, new SplitMix64Rng(5), NullEventSink.Instance);
        Assert.Empty(outcome.State.Fleets);
        Assert.Equal(3, outcome.State.ArmyById("m")!.Units.Count);
    }
}
