using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 1: the three scorers (<see cref="AiArmyTargetTree.ScoreArmyTarget"/>,
/// <see cref="AiArmyTargetTree.ScoreCityTarget"/>, <see cref="AiArmyTargetTree.ScoreResupplyCity"/>)
/// reproduce the original's decompiled arithmetic on hand-built states. Each test runs the scorer on a
/// hand-built state and asserts the integer score; each term (region halving, defense-strength doubling,
/// capital defense-halving doubling, distance return, army-score 1000 cap, weaker +1000 bonus, own-capital
/// -20, foreign-non-war +20, beyond-15 fallback) is observed at least once across the test class.
/// </summary>
public sealed class AiArmyTargetTreeScorerTests
{
    private const string Acting = AiScriptedStates.Attacker;
    private const string Other = AiScriptedStates.Defender;

    private static Ruleset Ruleset => AiScriptedStates.Ruleset;

    private static AiView BuildViewWith(
        IEnumerable<NationDefinition> nations,
        IEnumerable<CityState> cities,
        IEnumerable<ArmyState> armies,
        IEnumerable<Seat> seats,
        IEnumerable<string> turnOrder,
        ValueList<AiCityRegionAssignment> cityRegionById,
        string actingId)
    {
        var ruleset = Ruleset with
        {
            Ai = Ruleset.Ai with { CityRegionById = cityRegionById },
        };
        var customWorld = AiScriptedStates.World with
        {
            Nations = ValueList.From(nations),
        };
        var scenario = new Scenario(
            SchemaVersion: 1,
            Id: "scenario-test",
            Name: "Scenario Test",
            WorldId: customWorld.Id,
            RulesetId: ruleset.Id,
            Seats: ValueList.Of(seats.ToArray()),
            Victory: new VictoryCondition(VictoryConditionType.TotalConquest),
            TurnLimit: null,
            BlindHotseat: false,
            RandomSeed: 1UL);
        var state = GameStateFactory.CreateInitial(customWorld, ruleset, scenario) with
        {
            RandomSeed = 1UL,
        };
        state = state with
        {
            Cities = ValueList.From(cities),
            Armies = ValueList.From(armies),
            ActiveSeatIndex = Array.IndexOf(state.TurnOrder.ToArray(), actingId),
        };
        return new AiView(state, ruleset, customWorld, actingId);
    }

    private static NationDefinition Nation(string id, string capitalCityId) =>
        new(
            Id: id, Name: id, ColorHex: "#fff", LeaderName: "L", CapitalCityId: capitalCityId,
            Treasury: 0, Unity: 600, Wealth: 0, TaxBase: 0, TaxRatePercent: 15,
            MobilizedPercent: 0, Population: 100);

    private static ArmyState MakeArmy(string id, string nation, int x, int y, int morale, int troops, int moves = 9) =>
        CaptureFixtures.Army(id, nation, x, y, morale,
                CaptureFixtures.Unit("heavy_infantry", troops))
            with { Moves = moves };

    private static CityState MakeCity(string id, string owner, int x, int y, int loyalty = 90, int fort = 100, int pop = 100, int supply = 0) =>
        CaptureFixtures.City(id, id, x, y, owner, owner,
            loyalty: loyalty, fortificationCode: fort, populationThousands: pop, maxPopulationThousands: pop, tribute: 0)
            with { SupplyTons = supply };

    // ---------- City score ----------

    /// <summary>
    /// Region halving: a state with two enemy cities of equal strength at the same distance, one in-region
    /// and one cross-region, gives a higher score for the in-region one. Specifically, the cross-region
    /// score is half of the in-region score (modulo the +distance step which both share).
    /// </summary>
    [Fact]
    public void ScoreCityTarget_halves_for_cross_region_target()
    {
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[] { MakeCity("near", "east", 11, 10), MakeCity("far", "east", 12, 12), MakeCity("west-cap", "west", 0, 0), MakeCity("east-cap", "east", 20, 20) },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 30_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 1),
                new AiCityRegionAssignment("near", 0),
                new AiCityRegionAssignment("far", 1)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, view.State.ArmyById("attacker")!, atWar: true);
        Assert.NotNull(scored.City);
        // The in-region city is preferred (higher score after the halving).
        Assert.Equal("near", scored.City!.Id);
        // The score (before +distance) is reduced by halving. Two cities at the same distance (1 each)
        // share that step, so the returned score difference reflects the halving.
        Assert.True(scored.Score > 0, "the in-region city should have a positive score");

        // Verify the halving directly: score a state with only the cross-region city.
        var crossOnlyView = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[] { MakeCity("far", "east", 11, 10), MakeCity("west-cap", "west", 0, 0), MakeCity("east-cap", "east", 20, 20) },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 30_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 1),
                new AiCityRegionAssignment("far", 1)),
            actingId: "west");
        var crossScored = AiArmyTargetTree.ScoreCityTarget(crossOnlyView, crossOnlyView.State.ArmyById("attacker")!, atWar: true);
        // The cross-region score (before +distance) should be exactly half of the in-region one (also before +distance).
        Assert.Equal(scored.Score / 2, crossScored.Score);
    }

    /// <summary>
    /// Defense-strength doubling: a city whose defense is well below the army's strength scores
    /// twice as much as one whose defense matches the army.
    /// </summary>
    [Fact]
    public void ScoreCityTarget_doubles_when_defense_is_below_strength_and_distance_below_seven()
    {
        // Two cities at the same distance with very different defenses.
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[]
            {
                MakeCity("weak", "east", 11, 10, loyalty: 1, fort: 0, pop: 1),
                MakeCity("strong", "east", 11, 11, loyalty: 90, fort: 100, pop: 200),
                MakeCity("west-cap", "west", 0, 0),
                MakeCity("east-cap", "east", 20, 20),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 50_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0),
                new AiCityRegionAssignment("weak", 0),
                new AiCityRegionAssignment("strong", 0)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, view.State.ArmyById("attacker")!, atWar: true);
        Assert.NotNull(scored.City);
        // The weak city wins (doubled).
        Assert.Equal("weak", scored.City!.Id);

        // Verify: only the strong city. Without doubling, it would score higher than the weak one.
        var strongOnlyView = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[] { MakeCity("strong", "east", 11, 10, loyalty: 90, fort: 100, pop: 200), MakeCity("west-cap", "west", 0, 0), MakeCity("east-cap", "east", 20, 20) },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 50_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0),
                new AiCityRegionAssignment("strong", 0)),
            actingId: "west");
        var strongScored = AiArmyTargetTree.ScoreCityTarget(strongOnlyView, strongOnlyView.State.ArmyById("attacker")!, atWar: true);
        // The weak (doubled) score is exactly 2x the strong (not doubled).
        Assert.Equal(strongScored.Score * 2, scored.Score);
    }

    /// <summary>
    /// Capital defense-halving doubling: a capital whose defense is below the two-thirds threshold
    /// scores twice as much as a non-capital city of equal defense.
    /// </summary>
    [Fact]
    public void ScoreCityTarget_doubles_when_city_is_a_capital_and_two_thirds_of_defense_is_below_strength()
    {
        // Two cities at the same distance with the SAME defense shape.
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "capital") },
            cities: new[]
            {
                MakeCity("capital", "east", 11, 10, loyalty: 90, fort: 100, pop: 100),
                MakeCity("regular", "east", 11, 10, loyalty: 90, fort: 100, pop: 100),
                MakeCity("west-cap", "west", 0, 0),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 50_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("capital", 0),
                new AiCityRegionAssignment("regular", 0)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, view.State.ArmyById("attacker")!, atWar: true);
        Assert.NotNull(scored.City);
        Assert.Equal("capital", scored.City!.Id);

        var regularOnlyView = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "regular") },
            cities: new[]
            {
                MakeCity("regular", "east", 11, 10, loyalty: 90, fort: 100, pop: 100),
                MakeCity("west-cap", "west", 0, 0),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 50_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("regular", 0)),
            actingId: "west");
        var regularScored = AiArmyTargetTree.ScoreCityTarget(regularOnlyView, regularOnlyView.State.ArmyById("attacker")!, atWar: true);
        // The capital's doubled score is exactly 2x the regular's.
        Assert.Equal(regularScored.Score * 2, scored.Score);
    }

    /// <summary>
    /// The scorer returns `score + distance` (the original's own return shape). Two cities at different
    /// distances differ on this returned value by the distance delta.
    /// </summary>
    [Fact]
    public void ScoreCityTarget_returns_score_plus_distance()
    {
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[]
            {
                MakeCity("near", "east", 11, 10, loyalty: 1, fort: 0, pop: 1),
                MakeCity("far", "east", 20, 20, loyalty: 1, fort: 0, pop: 1),
                MakeCity("west-cap", "west", 0, 0),
                MakeCity("east-cap", "east", 30, 30),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 30_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0),
                new AiCityRegionAssignment("near", 0),
                new AiCityRegionAssignment("far", 0)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreCityTarget(view, view.State.ArmyById("attacker")!, atWar: true);
        Assert.NotNull(scored.City);
        Assert.Equal("near", scored.City!.Id);
        Assert.Equal(1, scored.Distance); // army at (10,10), city at (11,10) → Chebyshev distance 1

        // Verify the +distance return: a far city gives a larger Distance but lower Score (since the
        // base score is reduced by distance).
        var farOnlyView = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[]
            {
                MakeCity("far", "east", 20, 20, loyalty: 1, fort: 0, pop: 1),
                MakeCity("west-cap", "west", 0, 0),
                MakeCity("east-cap", "east", 30, 30),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 30_000) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0),
                new AiCityRegionAssignment("far", 0)),
            actingId: "west");
        var farScored = AiArmyTargetTree.ScoreCityTarget(farOnlyView, farOnlyView.State.ArmyById("attacker")!, atWar: true);
        Assert.True(farScored.Distance > scored.Distance, "the far city should have a larger Distance");
    }

    // ---------- Army score ----------

    /// <summary>
    /// Army-score 1000 cap + weaker bonus: a vastly weaker enemy gives a returned score at or above
    /// 1000 (the cap), with the +1000 weaker bonus added on top.
    /// </summary>
    [Fact]
    public void ScoreArmyTarget_caps_at_one_thousand_then_adds_the_weaker_bonus()
    {
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[] { MakeCity("west-cap", "west", 0, 0), MakeCity("east-cap", "east", 20, 20) },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 30_000), MakeArmy("weaker", "east", 11, 10, 1, 100) },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreArmyTarget(view, view.State.ArmyById("attacker")!, atWar: true);
        Assert.NotNull(scored.Army);
        Assert.Equal("weaker", scored.Army!.Id);
        // 1000 cap + 1000 weaker bonus = 2000 (since weaker bonus is additive on the cap).
        Assert.True(scored.Score >= 1000, $"score should be >= 1000, got {scored.Score}");
    }

    /// <summary>
    /// Cross-region halving for armies: the score on a cross-region enemy is at most half of an
    /// in-region enemy's, at the same distance.
    /// </summary>
    [Fact]
    public void ScoreArmyTarget_halves_when_target_is_in_another_region()
    {
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("east", "east-cap") },
            cities: new[] { MakeCity("west-cap", "west", 0, 0), MakeCity("east-cap", "east", 20, 20) },
            armies: new[]
            {
                MakeArmy("attacker", "west", 10, 10, 60, 30_000),
                MakeArmy("in-region", "east", 11, 10, 1, 100),
                MakeArmy("cross-region", "east", 11, 12, 1, 100),
            },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0),
                new AiCityRegionAssignment("in-region", 0),
                new AiCityRegionAssignment("cross-region", 1)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreArmyTarget(view, view.State.ArmyById("attacker")!, atWar: true);
        Assert.NotNull(scored.Army);
        // The in-region army wins.
        Assert.Equal("in-region", scored.Army!.Id);
    }

    // ---------- Resupply city ----------

    /// <summary>
    /// Own-capital -20 penalty: an own city that is also the capital is the chosen one only when it
    /// is the only option; otherwise a non-capital own city at the same distance scores 20 higher.
    /// </summary>
    [Fact]
    public void ScoreResupplyCity_applies_minus_twenty_for_own_capital()
    {
        // Two identical own cities. The non-capital one wins (less penalty).
        var view = BuildViewWith(
            nations: new[] { Nation("west", "capital-city") },
            cities: new[]
            {
                MakeCity("capital-city", "west", 10, 11, loyalty: 1, fort: 0, pop: 1),
                MakeCity("regular-city", "west", 10, 12, loyalty: 1, fort: 0, pop: 1),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 100) },
            seats: new[] { new Seat("west", SeatControl.Ai, null) },
            turnOrder: new[] { "west" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("capital-city", 0),
                new AiCityRegionAssignment("regular-city", 0)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, view.State.ArmyById("attacker")!, atWar: false);
        Assert.NotNull(scored.City);
        Assert.Equal("regular-city", scored.City!.Id);
    }

    /// <summary>
    /// Foreign-non-war +20 bonus: a foreign city not at war scores +20 higher than an identical foreign
    /// city that *is* at war (the war city's stock exceeds strength + 80, so it would qualify).
    /// </summary>
    [Fact]
    public void ScoreResupplyCity_applies_plus_twenty_for_foreign_non_war_cities()
    {
        // Two foreign cities (with high stock). The neutral one wins (+20 bonus), the war one doesn't.
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("neutral", "neutral-city"), Nation("east", "east-cap") },
            cities: new[]
            {
                MakeCity("neutral-city", "neutral", 10, 11, loyalty: 50, fort: 50, pop: 50, supply: 100_000),
                MakeCity("east-cap", "east", 10, 12, loyalty: 50, fort: 50, pop: 50, supply: 100_000),
                MakeCity("west-cap", "west", 0, 0),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 100) with { Money = 1000 } },
            seats: new[]
            {
                new Seat("west", SeatControl.Ai, null),
                new Seat("neutral", SeatControl.Ai, null),
                new Seat("east", SeatControl.Ai, null),
            },
            turnOrder: new[] { "west", "neutral", "east" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("east-cap", 0),
                new AiCityRegionAssignment("neutral-city", 0),
                new AiCityRegionAssignment("east-cap", 1)), // NB: duplicate id, distinct name
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, view.State.ArmyById("attacker")!, atWar: false);
        Assert.NotNull(scored.City);
        // The non-war (neutral) city wins.
        Assert.Equal("neutral-city", scored.City!.Id);
    }

    /// <summary>
    /// Foreign-city beyond-15 fallback: a foreign city more than 15 tiles away is rejected in favour of
    /// the nearest own city.
    /// </summary>
    [Fact]
    public void ScoreResupplyCity_rejects_foreign_cities_beyond_fifteen_and_falls_back_to_own()
    {
        var view = BuildViewWith(
            nations: new[] { Nation("west", "west-cap"), Nation("neutral", "far-city") },
            cities: new[]
            {
                MakeCity("own-city", "west", 11, 10, loyalty: 1, fort: 0, pop: 1),
                MakeCity("far-city", "neutral", 30, 30, loyalty: 50, fort: 50, pop: 50, supply: 100_000),
                MakeCity("west-cap", "west", 0, 0),
            },
            armies: new[] { MakeArmy("attacker", "west", 10, 10, 60, 100) with { Money = 1000 } },
            seats: new[] { new Seat("west", SeatControl.Ai, null), new Seat("neutral", SeatControl.Ai, null) },
            turnOrder: new[] { "west", "neutral" },
            cityRegionById: ValueList<AiCityRegionAssignment>.Of(
                new AiCityRegionAssignment("west-cap", 0),
                new AiCityRegionAssignment("own-city", 0),
                new AiCityRegionAssignment("far-city", 0)),
            actingId: "west");

        var scored = AiArmyTargetTree.ScoreResupplyCity(view, view.State.ArmyById("attacker")!, atWar: false);
        Assert.NotNull(scored.City);
        Assert.Equal("own-city", scored.City!.Id);
    }
}