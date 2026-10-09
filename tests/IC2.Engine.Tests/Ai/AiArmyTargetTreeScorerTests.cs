using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 1: the three scorers of the original's army target tree
/// (<c>FUN_0044ece4</c>, <c>FUN_0044ee60</c>, <c>FUN_0044e670</c>) on hand-built states. Every expected
/// value is the report's formula written out in the test from the same public strength and defence
/// functions the scorer uses (<c>2026-10-07-strategic-ai-turn.md</c> §3.3 and §5), so a removed or
/// altered term moves a score the test pins to the integer.
/// </summary>
public sealed class AiArmyTargetTreeScorerTests
{
    private const string Us = "north";
    private const string Them = "south";
    private const string Third = "east";

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    /// <summary>A 64 x 64 world of plain, so that the reachability rule never interferes with the scorer arithmetic.</summary>
    private static readonly World OpenPlain = AiScriptedStates.World with
    {
        Width = 64,
        Height = 64,
        Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 64 * 64))),
    };

    private static string Archer => BattleCommandRuleset.ArcherUnitTypeIdIn(Ruleset)!;

    /// <summary>
    /// A view for <see cref="Us"/> over the given cities and armies. <see cref="Us"/> and <see cref="Them"/>
    /// are at war; <see cref="Third"/> is at peace with both. The world is the toy world: the scorers
    /// read distances only, so coordinates outside its grid are fine.
    /// </summary>
    private static AiView ViewOf(
        IEnumerable<CityState> cities,
        IEnumerable<ArmyState> armies,
        params (string CityId, int Region)[] regions) =>
        ViewIn(OpenPlain, cities, armies, regions);

    private static AiView ViewIn(
        World world,
        IEnumerable<CityState> cities,
        IEnumerable<ArmyState> armies,
        params (string CityId, int Region)[] regions)
    {
        var nations = new[]
        {
            CaptureFixtures.Nation(Us, capitalCityId: "us-capital"),
            CaptureFixtures.Nation(Them, capitalCityId: "them-capital"),
            CaptureFixtures.Nation(Third, capitalCityId: "third-capital"),
        };
        var state = BattleCommandTestbed.StateWith(nations, cities, armies);
        state = state with
        {
            Relations = state.Relations.WithRelation(Us, Them, Ruleset.Diplomacy.StateCodes.War),
        };
        var ruleset = Ruleset with
        {
            Ai = Ruleset.Ai with
            {
                CityRegionById = ValueList.From(regions.Select(r => new AiCityRegionAssignment(r.CityId, r.Region))),
            },
        };
        return new AiView(state, ruleset, world, Us);
    }

    private static CityState City(
        string id, string owner, int x, int y, int loyalty = 90, int fort = 100, int pop = 200, int supply = 0) =>
        CaptureFixtures.City(id, id, x, y, owner, owner,
            loyalty: loyalty, fortificationCode: fort, populationThousands: pop, maxPopulationThousands: pop, tribute: 0)
        with { SupplyTons = supply };

    private static ArmyState Army(string id, string nation, int x, int y, int troops, int morale = 60) =>
        CaptureFixtures.Army(id, nation, x, y, morale, CaptureFixtures.Unit("light_infantry", troops))
        with { Moves = 9 };

    private static int Strength(ArmyState army, AiView view) =>
        AiArmyTargetTree.AssaultStrength(army, view.Ruleset, Archer);

    private static int Defense(CityState city, AiView view) =>
        AiArmyTargetTree.CityDefense(city, view.Ruleset, view.State);

    private static int StrengthOf(int troops) =>
        AiArmyTargetTree.AssaultStrength(Army("probe", Us, 0, 0, troops), Ruleset, Archer);

    /// <summary>The first troop count (in steps of 50) whose strength satisfies <paramref name="fits"/>.</summary>
    private static int TroopsWhere(Func<int, bool> fits)
    {
        for (var troops = 50; troops <= 400_000; troops += 50)
        {
            if (fits(troops))
            {
                return troops;
            }
        }

        throw new InvalidOperationException("no troop count fits the wanted strength band");
    }

    // ---------- the best enemy city (FUN_0044ece4) ----------

    /// <summary>A strong city, same region, distance 4: no modifier applies, so the score is <c>110 s / d - dist</c> and the return adds the distance back.</summary>
    [Fact]
    public void City_score_is_the_ratio_minus_distance_and_the_return_adds_the_distance_back()
    {
        var city = City("c", Them, 14, 10);
        var d = Defense(city, ViewOf([City("us-capital", Us, 8, 10), city], []));
        var army = Army("a", Us, 10, 10, TroopsWhere(t => StrengthOf(t) >= d * 9 / 10));
        var view = ViewOf([City("us-capital", Us, 8, 10), city, City("them-capital", Them, 30, 30)], [army]);
        var s = Strength(army, view);
        Assert.True(d >= s, "fixture: the city must out-defend the army so no doubling applies");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, army);

        Assert.Equal("c", scored.City!.Id);
        Assert.Equal(4, scored.Distance);
        var raw = (110 * s / d) - 4;
        Assert.Equal(raw + 4, scored.Score);
    }

    /// <summary>A city in another region is halved: <c>score -= score / 2</c>, then the distance is added back.</summary>
    [Fact]
    public void City_score_is_halved_when_the_city_is_in_another_region()
    {
        var city = City("c", Them, 14, 10);
        var d = Defense(city, ViewOf([City("us-capital", Us, 8, 10), city], []));
        var army = Army("a", Us, 10, 10, TroopsWhere(t => StrengthOf(t) >= d * 9 / 10));
        var view = ViewOf(
            [City("us-capital", Us, 8, 10), city, City("them-capital", Them, 30, 30)],
            [army],
            ("us-capital", 0), ("c", 1));
        var s = Strength(army, view);
        Assert.True(d >= s);

        var scored = AiArmyTargetTree.ScoreCityTarget(view, army);

        var raw = (110 * s / d) - 4;
        Assert.True(raw > 1, "fixture: a score of at least 2 so that halving changes it");
        Assert.Equal(raw - (raw / 2) + 4, scored.Score);
    }

    /// <summary>Defence below strength and distance below 7 doubles the score; at 7 it does not.</summary>
    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void City_score_doubles_when_defence_is_below_strength_and_the_distance_is_below_seven(int distance, bool doubled)
    {
        var army = Army("a", Us, 10, 10, 20_000);
        var city = City("c", Them, 10 + distance, 10, loyalty: 10, fort: 10, pop: 10);
        var view = ViewOf([City("us-capital", Us, 8, 10), city, City("them-capital", Them, 30, 30)], [army]);
        var s = Strength(army, view);
        var d = Defense(city, view);
        Assert.True(d < s, "fixture: the army must out-strengthen the city");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, army);

        var raw = (110 * s / d) - distance;
        Assert.Equal((doubled ? raw * 2 : raw) + distance, scored.Score);
    }

    /// <summary>A capital whose defence times two thirds is below the strength doubles, even beyond 7; the same city when it is no capital does not.</summary>
    [Fact]
    public void City_score_doubles_for_a_capital_whose_two_thirds_defence_is_below_the_strength()
    {
        var capital = City("them-capital", Them, 18, 10, loyalty: 10, fort: 10, pop: 10);
        var d = Defense(capital, ViewOf([City("us-capital", Us, 8, 10), capital], []));
        var army = Army("a", Us, 10, 10, TroopsWhere(t => StrengthOf(t) > d));
        var view = ViewOf([City("us-capital", Us, 8, 10), capital], [army]);
        var s = Strength(army, view);
        Assert.True(d * 2 / 3 < s && d < s);

        var asCapital = AiArmyTargetTree.ScoreCityTarget(view, army);

        Assert.Equal(8, asCapital.Distance);
        var raw = (110 * s / d) - 8;
        Assert.Equal((raw * 2) + 8, asCapital.Score);

        // The same city when it is not a capital (the owner's capital is elsewhere): no doubling at distance 8.
        var plain = capital with { Id = "plain" };
        var plainView = ViewOf([City("us-capital", Us, 8, 10), plain, City("them-capital", Them, 30, 30)], [army]);
        var plainScore = AiArmyTargetTree.ScoreCityTarget(plainView, army);
        var plainDefense = Defense(plain, plainView);
        Assert.Equal(((110 * s / plainDefense) - 8) + 8, plainScore.Score);
    }

    /// <summary>The capital doubling is keyed to two thirds of the defence: between two thirds and the full defence it applies, at or below two thirds it does not.</summary>
    [Fact]
    public void City_score_capital_doubling_is_keyed_to_two_thirds_of_the_defence()
    {
        var capital = City("them-capital", Them, 18, 10);
        var d = Defense(capital, ViewOf([City("us-capital", Us, 8, 10), capital], []));
        var between = Army("a", Us, 10, 10, TroopsWhere(t => StrengthOf(t) > d * 2 / 3));
        var view = ViewOf([City("us-capital", Us, 8, 10), capital], [between]);
        var s = Strength(between, view);
        Assert.True(s > d * 2 / 3 && s < d, "fixture: strength between two thirds of the defence and the defence");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, between);

        // Defence is not below the strength at distance 8 either way; only the capital doubling can apply.
        Assert.Equal((((110 * s / d) - 8) * 2) + 8, scored.Score);

        var weaker = Army("a", Us, 10, 10, TroopsWhere(t => StrengthOf(t) > d / 2));
        var weakerView = ViewOf([City("us-capital", Us, 8, 10), capital], [weaker]);
        var w = Strength(weaker, weakerView);
        Assert.True(w <= d * 2 / 3);
        var none = AiArmyTargetTree.ScoreCityTarget(weakerView, weaker);
        Assert.Equal(((110 * w / d) - 8) + 8, none.Score);
    }

    /// <summary>Cities of nations not at war are not targets.</summary>
    [Fact]
    public void City_score_ignores_cities_of_nations_not_at_war()
    {
        var army = Army("a", Us, 10, 10, 1_000);
        var view = ViewOf([City("us-capital", Us, 8, 10), City("t", Third, 12, 10), City("them-capital", Them, 30, 30)], [army]);

        var scored = AiArmyTargetTree.ScoreCityTarget(view, army);

        Assert.Equal("them-capital", scored.City!.Id);
    }

    // ---------- the best enemy field army (FUN_0044ee60) ----------

    /// <summary>A far weaker enemy within 7: the ratio is capped at 1000, then +1000 for weaker and near; at 7 only the cap.</summary>
    [Theory]
    [InlineData(6, 2000)]
    [InlineData(7, 1000)]
    public void Army_score_caps_the_ratio_at_one_thousand_then_adds_a_thousand_for_a_weaker_near_enemy(int distance, long expected)
    {
        var army = Army("a", Us, 10, 10, 20_000);
        var enemy = Army("e", Them, 10 + distance, 10, 100);
        var view = ViewOf([City("us-capital", Us, 8, 10), City("them-capital", Them, 30, 30)], [army, enemy]);
        var s = Strength(army, view);
        var t = Strength(enemy, view);
        Assert.True((110 * s / t) - distance > 1000 && t < s);

        var scored = AiArmyTargetTree.ScoreArmyTarget(view, army);

        Assert.Equal("e", scored.Army!.Id);
        Assert.Equal(distance, scored.Distance);
        Assert.Equal(expected, scored.Score);
    }

    /// <summary>A moderately weaker enemy: no cap reached, so the score is the ratio minus the distance plus the +1000.</summary>
    [Fact]
    public void Army_score_without_the_cap_is_the_ratio_minus_distance_plus_the_weaker_bonus()
    {
        var enemy = Army("e", Them, 13, 10, 5_000);
        var t = Strength(enemy, ViewOf([City("us-capital", Us, 8, 10)], [enemy]));
        var army = Army("a", Us, 10, 10, TroopsWhere(x => StrengthOf(x) > t * 2));
        var view = ViewOf([City("us-capital", Us, 8, 10), City("them-capital", Them, 30, 30)], [army, enemy]);
        var s = Strength(army, view);
        var raw = (110 * s / t) - 3;
        Assert.True(raw < 1000 && t < s);

        var scored = AiArmyTargetTree.ScoreArmyTarget(view, army);

        Assert.Equal(raw + 1000, scored.Score);
    }

    /// <summary>An equal or stronger enemy gets no bonus: the score is the ratio minus the distance.</summary>
    [Fact]
    public void Army_score_of_a_stronger_enemy_has_no_weaker_bonus()
    {
        var army = Army("a", Us, 10, 10, 1_000);
        var enemy = Army("e", Them, 13, 10, 3_000);
        var view = ViewOf([City("us-capital", Us, 8, 10), City("them-capital", Them, 30, 30)], [army, enemy]);
        var s = Strength(army, view);
        var t = Strength(enemy, view);
        Assert.True(t >= s);

        var scored = AiArmyTargetTree.ScoreArmyTarget(view, army);

        Assert.Equal((110 * s / t) - 3, scored.Score);
    }

    /// <summary>An enemy in another region is halved before the cap.</summary>
    [Fact]
    public void Army_score_is_halved_when_the_enemy_is_in_another_region()
    {
        var army = Army("a", Us, 10, 10, 1_000);
        var enemy = Army("e", Them, 13, 10, 3_000);
        var view = ViewOf(
            [City("us-capital", Us, 8, 10), City("them-capital", Them, 14, 10)],
            [army, enemy],
            ("us-capital", 0), ("them-capital", 1));
        var s = Strength(army, view);
        var t = Strength(enemy, view);
        Assert.True(t >= s);
        var raw = (110 * s / t) - 3;
        Assert.True(raw > 1);

        var scored = AiArmyTargetTree.ScoreArmyTarget(view, army);

        Assert.Equal(raw - (raw / 2), scored.Score);
    }

    // ---------- the reachability rule ([designed]: the user's decision of 2026-10-10 on #925) ----------

    /// <summary>The 64 x 64 plain world with sea (code 0) on the given tiles.</summary>
    private static World WorldWithSea(params (int X, int Y)[] sea)
    {
        var cells = Enumerable.Repeat(2, 64 * 64).ToArray();
        foreach (var (x, y) in sea)
        {
            cells[(y * 64) + x] = 0;
        }

        var runs = new List<TerrainRun>();
        foreach (var cell in cells)
        {
            if (runs.Count > 0 && runs[^1].Code == cell)
            {
                runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
            }
            else
            {
                runs.Add(new TerrainRun(cell, 1));
            }
        }

        return OpenPlain with { Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(runs)) };
    }

    /// <summary>A city across a sea tile on the straight line is skipped, and the next-best city is chosen.</summary>
    [Fact]
    public void Reachability_skips_a_city_whose_straight_line_crosses_the_sea_and_takes_the_next_best()
    {
        var army = Army("a", Us, 10, 10, 20_000);
        var blocked = City("blocked", Them, 14, 10, loyalty: 10, fort: 10, pop: 10);
        var open = City("open", Them, 10, 20);
        var cities = new[] { City("us-capital", Us, 8, 10), blocked, open, City("them-capital", Them, 40, 40) };

        var control = AiArmyTargetTree.ScoreCityTarget(ViewOf(cities, [army]), army);
        var sea = AiArmyTargetTree.ScoreCityTarget(ViewIn(WorldWithSea((12, 10)), cities, [army]), army);

        Assert.Equal("blocked", control.City!.Id);
        Assert.Equal("open", sea.City!.Id);
        Assert.Equal(1, sea.Skipped);
    }

    /// <summary>A city on the line, an army on the line and a fleet on the line each stop the walk; only the target's own tile is exempt.</summary>
    [Fact]
    public void Reachability_skips_a_target_whose_first_step_is_blocked_and_takes_the_next_best()
    {
        var army = Army("a", Us, 10, 10, 20_000);
        var blockedTarget = Army("blocked", Them, 14, 10, 100);
        var openTarget = Army("open", Them, 10, 16, 1_000);
        var cities = new[] { City("us-capital", Us, 11, 10), City("them-capital", Them, 40, 40) };

        var viewBlocked = ViewOf(cities, [army, blockedTarget, openTarget]);
        var picked = AiArmyTargetTree.ScoreArmyTarget(viewBlocked, army);

        Assert.Equal("open", picked.Army!.Id);
        Assert.Equal(1, picked.Skipped);

        // The same army target with the city moved off the line is reachable and wins.
        var clear = ViewOf([City("us-capital", Us, 11, 12), City("them-capital", Them, 40, 40)], [army, blockedTarget, openTarget]);
        Assert.Equal("blocked", AiArmyTargetTree.ScoreArmyTarget(clear, army).Army!.Id);

        // An adjacent target is reachable even though its own tile is occupied.
        var adjacent = Army("adjacent", Them, 11, 10, 100);
        var adjacentView = ViewOf([City("us-capital", Us, 11, 12), City("them-capital", Them, 40, 40)], [army, adjacent]);
        Assert.Equal("adjacent", AiArmyTargetTree.ScoreArmyTarget(adjacentView, army).Army!.Id);
    }

    /// <summary>An army standing on an intervening tile stops the walk; the target's own tile being the only obstacle does not.</summary>
    [Fact]
    public void Reachability_skips_a_target_behind_an_intervening_army_but_not_one_whose_own_tile_is_the_only_obstacle()
    {
        var army = Army("a", Us, 10, 10, 20_000);
        var target = City("target", Them, 14, 10, loyalty: 10, fort: 10, pop: 10);
        var fallback = City("fallback", Them, 10, 25);
        var cities = new[] { City("us-capital", Us, 8, 10), target, fallback, City("them-capital", Them, 40, 40) };
        var bystander = Army("bystander", Third, 12, 10, 100);

        var behind = AiArmyTargetTree.ScoreCityTarget(ViewOf(cities, [army, bystander]), army);
        var clear = AiArmyTargetTree.ScoreCityTarget(ViewOf(cities, [army]), army);

        Assert.Equal("fallback", behind.City!.Id);
        Assert.Equal(1, behind.Skipped);
        Assert.Equal("target", clear.City!.Id);
        Assert.Equal(0, clear.Skipped);
    }

    /// <summary>A city next to the army, and a city whose walk stops on the tile beside it, are reachable: the target's own tile is the only obstacle.</summary>
    [Fact]
    public void Reachability_keeps_a_city_beside_the_army_and_one_reached_through_the_tile_beside_it()
    {
        var army = Army("a", Us, 10, 10, 20_000);
        var beside = AiArmyTargetTree.ScoreCityTarget(
            ViewOf([City("us-capital", Us, 8, 10), City("t", Them, 11, 10), City("them-capital", Them, 40, 40)], [army]), army);
        var twoAway = AiArmyTargetTree.ScoreCityTarget(
            ViewOf([City("us-capital", Us, 8, 10), City("t", Them, 12, 10), City("them-capital", Them, 40, 40)], [army]), army);

        Assert.Equal("t", beside.City!.Id);
        Assert.Equal(0, beside.Skipped);
        Assert.Equal("t", twoAway.City!.Id);
        Assert.Equal(0, twoAway.Skipped);
    }

    /// <summary>When every target of a kind is skipped there is no target, and the decision takes the next branch.</summary>
    [Fact]
    public void Reachability_leaves_no_target_and_the_decision_takes_the_next_branch()
    {
        var army = Army("a", Us, 10, 10, 20_000) with { SupplyTons = 0 };
        var cities = new[] { City("us-capital", Us, 20, 20), City("them-capital", Them, 14, 10, loyalty: 10, fort: 10, pop: 10) };
        var view = ViewIn(WorldWithSea((12, 10)), cities, [army]);

        var decision = AiArmyTargetTree.Decide(view, army, atWar: true);

        Assert.Null(decision.TargetCity);
        Assert.Equal(AiArmyTargetTree.Kind.MoveToResupplyCity, decision.Selected);
    }

    // ---------- the resupply or defence city (FUN_0044e670) ----------

    private static ArmyState Supplicant() =>
        Army("a", Us, 10, 10, 10_000) with { Money = 1_000 };

    /// <summary>An own capital scores -20: the nearer capital loses to a farther ordinary city that needs stock too.</summary>
    [Fact]
    public void Resupply_scores_an_own_capital_twenty_lower()
    {
        var army = Supplicant();
        var view = ViewOf(
            [City("us-capital", Us, 13, 10), City("regular", Us, 20, 10), City("them-capital", Them, 30, 30)],
            [army]);

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: false);

        Assert.Equal("regular", scored.City!.Id);
        Assert.Equal(-10, scored.Score);
    }

    /// <summary>A qualifying foreign city of a nation not at war scores +20, so a foreign city at 15 beats an own one at 5, while at war only.</summary>
    [Fact]
    public void Resupply_scores_a_foreign_non_war_city_twenty_higher_while_at_war()
    {
        var army = Supplicant();
        var view = ViewOf(
            [City("us-capital", Us, 15, 10), City("foreign", Third, 25, 10, supply: 1_000), City("them-capital", Them, 30, 30)],
            [army]);

        var atWar = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true);
        var atPeace = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: false);

        Assert.Equal("foreign", atWar.City!.Id);
        Assert.Equal(5, atWar.Score);
        Assert.Equal("us-capital", atPeace.City!.Id);
    }

    /// <summary>A foreign city needs stock above <c>troops/100 + 80</c> and the army money above <c>troops/100/5</c>; a city of a nation at war is never a source.</summary>
    [Fact]
    public void Resupply_foreign_city_must_hold_stock_and_the_army_money_and_not_be_at_war()
    {
        var army = Supplicant();
        var strength = army.TotalTroops / 100;
        var thin = ViewOf(
            [City("us-capital", Us, 25, 10), City("thin", Third, 12, 10, supply: strength + 80), City("them-capital", Them, 30, 30)],
            [army]);
        var pocketChange = army with { Money = strength / 5 };
        var poor = ViewOf(
            [City("us-capital", Us, 25, 10), City("rich", Third, 12, 10, supply: strength + 81), City("them-capital", Them, 30, 30)],
            [pocketChange]);
        var hostile = ViewOf(
            [City("us-capital", Us, 25, 10), City("them-capital", Them, 12, 10, supply: 5_000)],
            [army]);
        var enough = ViewOf(
            [City("us-capital", Us, 25, 10), City("rich", Third, 12, 10, supply: strength + 81), City("them-capital", Them, 30, 30)],
            [army]);

        Assert.Equal("us-capital", AiArmyTargetTree.ScoreResupplyCity(thin, army, atWar: true).City!.Id);
        Assert.Equal("us-capital", AiArmyTargetTree.ScoreResupplyCity(poor, pocketChange, atWar: true).City!.Id);
        Assert.Equal("us-capital", AiArmyTargetTree.ScoreResupplyCity(hostile, army, atWar: true).City!.Id);
        Assert.Equal("rich", AiArmyTargetTree.ScoreResupplyCity(enough, army, atWar: true).City!.Id);
    }

    /// <summary>The best foreign city beyond 15 is dropped for the nearest own city, even one that needs no stock.</summary>
    [Fact]
    public void Resupply_falls_back_to_the_nearest_own_city_when_the_foreign_pick_is_beyond_fifteen()
    {
        var army = Supplicant();
        var view = ViewOf(
            [
                City("us-capital", Us, 40, 10),
                City("own-near", Us, 30, 10, supply: 5_000),
                City("foreign", Third, 26, 10, supply: 1_000),
                City("them-capital", Them, 50, 50),
            ],
            [army]);

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true);

        Assert.Equal("own-near", scored.City!.Id);
        Assert.Equal(20, scored.Distance);
    }

    /// <summary>A foreign city at exactly 15 is still taken.</summary>
    [Fact]
    public void Resupply_keeps_a_foreign_pick_at_exactly_fifteen()
    {
        var army = Supplicant();
        var view = ViewOf(
            [City("us-capital", Us, 40, 10), City("foreign", Third, 25, 10, supply: 1_000), City("them-capital", Them, 50, 50)],
            [army]);

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: true);

        Assert.Equal("foreign", scored.City!.Id);
    }

    /// <summary>A city the army stands beside is where the original's army already is, so it is never the destination.</summary>
    [Fact]
    public void Resupply_never_picks_the_city_the_army_is_beside()
    {
        var army = Supplicant();
        var view = ViewOf(
            [City("beside", Us, 11, 10), City("far", Us, 30, 10), City("them-capital", Them, 50, 50)],
            [army]);

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: false);

        Assert.Equal("far", scored.City!.Id);
    }
}
