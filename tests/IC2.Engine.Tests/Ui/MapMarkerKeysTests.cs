using IC2.Engine.Assets;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Economy;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// <c>docs/tasks/T94.md</c> Done-when 1 and folded follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/454">#454</see> item 2:
/// <see cref="MapMarkerKeys"/> resolves a marker's asset-pack icon from the <em>loaded</em>
/// <see cref="GameState"/>'s own numbers and the loaded ruleset's thresholds — never from a fixed
/// marker per owner, and never from the scenario-start capital. These tests drive the real
/// <c>data/</c> worlds/rulesets through <see cref="GameDataRepository"/> and
/// <see cref="GameStateFactory.CreateInitial"/>, the same path the Godot slice and screen use.
/// </summary>
public sealed class MapMarkerKeysTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>
    /// DoD 1, city half. The toy world's own numbers (portus 80,000 against a test-supplied 100,000
    /// boundary) decide the key; raising the same city's population changes the key, which a "fixed
    /// marker per owner" implementation could not do.
    /// </summary>
    /// <remarks>
    /// The boundary (100) is a <strong>test fixture</strong>, not the shipped value (the shipped
    /// [25, 50, 100] is pinned by the T163 theory below); it shows the key follows the number.
    /// </remarks>
    /// <summary>#1309: the size rank follows the same tiers as the icon, and a live capital ranks above them all.</summary>
    [Theory]
    [InlineData(5, 0)]
    [InlineData(24, 0)]
    [InlineData(25, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 2)]
    [InlineData(100, 3)]
    [InlineData(400, 3)]
    public void City_rank_follows_the_population_tiers(int population, int rank)
    {
        var resolved = Repository.Resolve("toy-3city");
        var state = GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);
        var city = state.CityById("portus")! with { PopulationThousands = population };
        var withCity = state with { Cities = ReplaceCity(state.Cities, city) };

        Assert.Equal(rank, MapMarkerKeys.CityRank(withCity, city, resolved.Ruleset.MapMarkers));
        Assert.Equal(MapMarkerKeys.CapitalRank, MapMarkerKeys.CityRank(withCity, withCity.CityById("meridia")!, resolved.Ruleset.MapMarkers));
    }

    /// <summary>#1309: each rank draws larger than the one below, so size reads at a glance.</summary>
    [Fact]
    public void City_marker_grows_with_rank()
    {
        for (var rank = 1; rank <= MapMarkerKeys.CapitalRank; rank++)
        {
            Assert.True(MapMarkerKeys.CityMarkerHalfSize(rank) > MapMarkerKeys.CityMarkerHalfSize(rank - 1), $"rank {rank} is not larger than {rank - 1}");
        }

        Assert.True(MapMarkerKeys.CityMarkerHalfSize(MapMarkerKeys.CapitalRank) <= 0.5f, "a capital fits inside its own tile");
    }

    /// <summary>#1309: the Italy campaign's thresholds spread its towns over all four tiers.</summary>
    [Fact]
    public void Italy_towns_use_every_tier()
    {
        var resolved = Repository.Resolve("italy-500bc");
        var state = GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);

        var ranks = state.Cities.Select(c => MapMarkerKeys.CityRank(state, c, resolved.Ruleset.MapMarkers)).ToHashSet();

        Assert.Equal(new[] { 0, 1, 2, 3, MapMarkerKeys.CapitalRank }, ranks.Order());
    }

    [Fact]
    public void City_marker_key_follows_population_from_the_loaded_state_not_the_owner()
    {
        var resolved = Repository.Resolve("toy-3city");
        var rules = resolved.Ruleset with
        {
            MapMarkers = resolved.Ruleset.MapMarkers with { CityPopulationTierThresholds = ValueList<int>.Of(100) },
        };
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);

        var low = state.CityById("portus")!;
        Assert.Equal("north", low.Owner);
        Assert.True(low.PopulationThousands < 100);
        Assert.Equal(AssetKeys.CityTier1Icon, MapMarkerKeys.CityIcon(state, low, rules.MapMarkers));

        // The same city, a population above the fixture boundary: the key follows the number, not the
        // city's identity or its owner.
        var raised = low with { PopulationThousands = 140 };
        var raisedState = state with { Cities = ReplaceCity(state.Cities, raised) };
        var raisedKey = MapMarkerKeys.CityIcon(raisedState, raisedState.CityById("portus")!, rules.MapMarkers);

        Assert.Equal(AssetKeys.CityTier2Icon, raisedKey);
        Assert.NotEqual(MapMarkerKeys.CityIcon(state, low, rules.MapMarkers), raisedKey);

        // Capital status is orthogonal to population (the one confirmed part of game-design.md's
        // "City markers"): meridia is south's live capital and still draws the capital icon at 140k.
        var capital = state.CityById("meridia")!;
        Assert.True(capital.PopulationThousands >= 100);
        Assert.Equal(AssetKeys.CityCapitalIcon, MapMarkerKeys.CityIcon(state, capital, rules.MapMarkers));
    }

    /// <summary>
    /// Rework round 1, B1, city half: the tier boundary itself, against the same test fixture's two
    /// thresholds (100 and 200). 99 is under the first, 100 and 199 are exactly between the two, 200
    /// reaches the third — the values a <c>&lt;=</c> comparison would misplace.
    /// </summary>
    /// <remarks>
    /// The thresholds are test fixtures (100, 200), not shipped values: the point is the comparison
    /// itself, strictly under a threshold.
    /// </remarks>
    [Theory]
    [InlineData(99, AssetKeys.CityTier1Icon)]
    [InlineData(100, AssetKeys.CityTier2Icon)]
    [InlineData(199, AssetKeys.CityTier2Icon)]
    [InlineData(200, AssetKeys.CityTier3Icon)]
    public void City_marker_key_switches_at_the_fixture_population_boundaries(int population, string expectedKey)
    {
        var resolved = Repository.Resolve("toy-3city");
        var rules = resolved.Ruleset with
        {
            MapMarkers = resolved.Ruleset.MapMarkers with
            {
                CityPopulationTierThresholds = ValueList<int>.Of(100, 200),
            },
        };
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);

        var portus = state.CityById("portus")!;
        var atBoundary = portus with { PopulationThousands = population };
        var atBoundaryState = state with { Cities = ReplaceCity(state.Cities, atBoundary) };

        Assert.Equal(
            expectedKey,
            MapMarkerKeys.CityIcon(atBoundaryState, atBoundaryState.CityById("portus")!, rules.MapMarkers));
    }

    /// <summary>
    /// T163 DoD 1: the SHIPPED thresholds ([25, 50, 100], research report
    /// 2026-10-07-city-marker-variants.md) decide the four town tiers, each boundary pinned from both
    /// sides (24/25, 49/50, 99/100), and a capital draws its own marker at any population.
    /// </summary>
    [Theory]
    [InlineData(24, AssetKeys.CityTier1Icon)]
    [InlineData(25, AssetKeys.CityTier2Icon)]
    [InlineData(49, AssetKeys.CityTier2Icon)]
    [InlineData(50, AssetKeys.CityTier3Icon)]
    [InlineData(99, AssetKeys.CityTier3Icon)]
    [InlineData(100, AssetKeys.CityTier4Icon)]
    [InlineData(500, AssetKeys.CityTier4Icon)]
    public void Shipped_ruleset_town_tiers_split_at_25_50_and_100(int population, string expectedKey)
    {
        var resolved = Repository.Resolve("toy-3city");
        var rules = resolved.Ruleset;
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);

        var portus = state.CityById("portus")!;
        var changed = portus with { PopulationThousands = population };
        var changedState = state with { Cities = ReplaceCity(state.Cities, changed) };
        Assert.Equal(
            expectedKey,
            MapMarkerKeys.CityIcon(changedState, changedState.CityById("portus")!, rules.MapMarkers));

        var meridia = state.CityById("meridia")!;
        var capital = meridia with { PopulationThousands = population };
        var capitalState = state with { Cities = ReplaceCity(state.Cities, capital) };
        Assert.Equal(
            AssetKeys.CityCapitalIcon,
            MapMarkerKeys.CityIcon(capitalState, capitalState.CityById("meridia")!, rules.MapMarkers));
    }

    /// <summary>
    /// T163 DoD 2: every shipped ruleset on the classical scale ships the report's [25, 50, 100]. #1309: the
    /// Italy and the Near East campaigns share the designed smaller-town scale,
    /// pinned in their campaign tests.
    /// </summary>
    [Fact]
    public void Every_shipped_ruleset_ships_the_confirmed_town_tier_thresholds()
    {
        Assert.NotEmpty(Repository.Rulesets);
        foreach (var ruleset in Repository.Rulesets)
        {
            var expected = ruleset.Id is "italy-500bc" or "near-east-1350bc" or "near-east-750bc"
                ? new[] { 6, 10, 18 } : new[] { 25, 50, 100 };
            Assert.True(
                ruleset.MapMarkers.CityPopulationTierThresholds.SequenceEqual(expected),
                $"{ruleset.Id} ships {string.Join(",", ruleset.MapMarkers.CityPopulationTierThresholds)}");
        }
    }

    /// <summary>
    /// DoD 1, army and fleet halves: <see cref="ArmyState.TotalTroops"/> and
    /// <see cref="FleetState.Ships"/> from the toy world's own loaded armies/fleets decide the three
    /// confirmed tiers, whose boundaries come from the loaded ruleset (25,000/50,000 and 25/50).
    /// </summary>
    [Fact]
    public void Army_and_fleet_marker_keys_follow_size_from_the_loaded_state()
    {
        var resolved = Repository.Resolve("toy-3city");
        var rules = resolved.Ruleset;
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);

        var army = state.Armies.Single(a => a.Id == "north-army-1");
        Assert.True(army.TotalTroops < 25_000);
        state = state with
        {
            Armies = ValueList.From(new[]
            {
                army,
                army with { Id = "mid-army", Units = OneUnitOf(30_000) },
                army with { Id = "large-army", Units = OneUnitOf(60_000) },
            }),
        };

        Assert.Equal(AssetKeys.ArmyTier1Icon, MapMarkerKeys.ArmyIcon(Army(state, "north-army-1"), rules.MapMarkers));
        Assert.Equal(AssetKeys.ArmyTier2Icon, MapMarkerKeys.ArmyIcon(Army(state, "mid-army"), rules.MapMarkers));
        Assert.Equal(AssetKeys.ArmyTier3Icon, MapMarkerKeys.ArmyIcon(Army(state, "large-army"), rules.MapMarkers));

        var fleet = state.Fleets.Single(f => f.Id == "north-fleet-1");
        Assert.True(fleet.Ships < 25);
        state = state with
        {
            Fleets = ValueList.From(new[]
            {
                fleet,
                fleet with { Id = "mid-fleet", Ships = 30 },
                fleet with { Id = "large-fleet", Ships = 60 },
            }),
        };

        Assert.Equal(AssetKeys.FleetTier1Icon, MapMarkerKeys.FleetIcon(Fleet(state, "north-fleet-1"), rules.MapMarkers));
        Assert.Equal(AssetKeys.FleetTier2Icon, MapMarkerKeys.FleetIcon(Fleet(state, "mid-fleet"), rules.MapMarkers));
        Assert.Equal(AssetKeys.FleetTier3Icon, MapMarkerKeys.FleetIcon(Fleet(state, "large-fleet"), rules.MapMarkers));
    }

    /// <summary>
    /// Rework round 1, B1, army half: the confirmed 25,000/50,000 split asserted on both sides of each
    /// boundary. 24,999 is still tier 1; 25,000 is already tier 2; 49,999 is still tier 2; 50,000 is
    /// tier 3. The edges are the loaded ruleset's own thresholds, read back here and asserted to be the
    /// confirmed values before they are used, so a re-tiered ruleset fails instead of re-pointing the
    /// test at different numbers.
    /// </summary>
    [Theory]
    [InlineData(24_999, AssetKeys.ArmyTier1Icon)]
    [InlineData(25_000, AssetKeys.ArmyTier2Icon)]
    [InlineData(49_999, AssetKeys.ArmyTier2Icon)]
    [InlineData(50_000, AssetKeys.ArmyTier3Icon)]
    public void Army_marker_key_switches_at_the_shipped_troop_boundaries(int troops, string expectedKey)
    {
        var resolved = Repository.Resolve("toy-3city");
        var rules = resolved.Ruleset;
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);
        var army = state.Armies.Single(a => a.Id == "north-army-1");

        Assert.Equal(25_000, rules.MapMarkers.ArmyTroopTierThresholds[0]);
        Assert.Equal(50_000, rules.MapMarkers.ArmyTroopTierThresholds[1]);

        var atBoundary = army with { Units = OneUnitOf(troops) };
        Assert.Equal(troops, atBoundary.TotalTroops);
        Assert.Equal(expectedKey, MapMarkerKeys.ArmyIcon(atBoundary, rules.MapMarkers));
    }

    /// <summary>
    /// Rework round 1, B1, fleet half: the confirmed 25/50 split asserted on both sides of each
    /// boundary — 24 ships is tier 1, 25 is tier 2, 49 is still tier 2, 50 is tier 3.
    /// </summary>
    [Theory]
    [InlineData(24, AssetKeys.FleetTier1Icon)]
    [InlineData(25, AssetKeys.FleetTier2Icon)]
    [InlineData(49, AssetKeys.FleetTier2Icon)]
    [InlineData(50, AssetKeys.FleetTier3Icon)]
    public void Fleet_marker_key_switches_at_the_shipped_ship_boundaries(int ships, string expectedKey)
    {
        var resolved = Repository.Resolve("toy-3city");
        var rules = resolved.Ruleset;
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);
        var fleet = state.Fleets.Single(f => f.Id == "north-fleet-1");

        Assert.Equal(25, rules.MapMarkers.FleetShipTierThresholds[0]);
        Assert.Equal(50, rules.MapMarkers.FleetShipTierThresholds[1]);

        var atBoundary = fleet with { Ships = ships };
        Assert.Equal(expectedKey, MapMarkerKeys.FleetIcon(atBoundary, rules.MapMarkers));
    }

    /// <summary>
    /// Folded #454 item 2: a capital that <em>moved during play</em>. <see cref="Rebirth.Run"/> is the
    /// engine path that relocates a nation's capital (<c>Rebirth.cs</c>, the new strongest city it
    /// reborns into), so this drives it on the real classical world and asserts the capital icon moves
    /// with the live <see cref="NationState.CapitalCityId"/> — the old capital falls back to the tier
    /// key, the new one draws the capital key, and nothing reads the scenario-start definition.
    /// </summary>
    [Fact]
    public void City_marker_follows_a_capital_that_rebirth_moved_during_play()
    {
        var resolved = Repository.Resolve("classical-mediterranean");
        var rules = resolved.Ruleset;
        var state = GameStateFactory.CreateInitial(resolved.World, rules, resolved.Scenario);

        const string rebornId = "seleucid";
        var reborn = state.NationById(rebornId)!;
        var oldCapital = state.CityById(reborn.CapitalCityId!)!;
        Assert.Equal(rebornId, oldCapital.Owner);

        // Eight rebellious cities: owned by another nation, allegiant to the (now dead) seleucid, low
        // loyalty — the exact shape of a rebirth's candidate set. The old capital is deliberately kept
        // loyal so it does not qualify and stays where it is.
        var defecting = state.Cities.Where(c => c.Owner == rebornId && c.Id != oldCapital.Id).Take(8).ToList();
        Assert.Equal(8, defecting.Count);

        var replacedIds = defecting.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var cities = state.Cities
            .Select(c => replacedIds.TryGetValue(c.Id, out var city)
                ? city with { Owner = "rome", Allegiance = rebornId, Loyalty = 0 }
                : c.Id == oldCapital.Id
                    ? c with { Allegiance = rebornId, Loyalty = rules.Economy.RebirthCandidateLoyaltyThreshold }
                    : c)
            .ToList();
        var before = state with
        {
            Cities = ValueList.From(cities),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == rebornId
                ? n with { Unity = 0, Eliminated = true, CapitalCityId = oldCapital.Id }
                : n)),
        };

        // The start state's own capital draws the capital key before the move, so the assertion below
        // is about the move, not about a marker that never followed the state at all.
        Assert.Equal(
            AssetKeys.CityCapitalIcon,
            MapMarkerKeys.CityIcon(before, before.CityById(oldCapital.Id)!, rules.MapMarkers));

        var after = Rebirth.Run(
            before,
            rules,
            before.NationById(rebornId)!,
            NullEventSink.Instance,
            new ScriptedRng(nextIntDraws: new[] { 0 }));

        var newCapitalId = after.NationById(rebornId)!.CapitalCityId;
        Assert.NotNull(newCapitalId);
        Assert.NotEqual(oldCapital.Id, newCapitalId);
        Assert.Contains(newCapitalId, defecting.Select(c => c.Id));
        Assert.Equal(AssetKeys.CityCapitalIcon, MapMarkerKeys.CityIcon(after, after.CityById(newCapitalId!)!, rules.MapMarkers));
        // The former capital is an ordinary town again: it draws its population tier (the shipped
        // [25, 50, 100]; its population is 100 thousand or more, so the fourth), not the capital marker.
        Assert.True(after.CityById(oldCapital.Id)!.PopulationThousands >= 100);
        Assert.Equal(AssetKeys.CityTier4Icon, MapMarkerKeys.CityIcon(after, after.CityById(oldCapital.Id)!, rules.MapMarkers));
    }

    private static ValueList<UnitSlot> OneUnitOf(int troops) =>
        ValueList<UnitSlot>.Of(new UnitSlot(0, "light_infantry", troops, 6, "test unit"));

    private static ArmyState Army(GameState state, string id) => state.Armies.Single(a => a.Id == id);

    private static FleetState Fleet(GameState state, string id) => state.Fleets.Single(f => f.Id == id);

    private static ValueList<CityState> ReplaceCity(ValueList<CityState> cities, CityState updated) =>
        ValueList.From(cities.Select(c => c.Id == updated.Id ? updated : c));
}
