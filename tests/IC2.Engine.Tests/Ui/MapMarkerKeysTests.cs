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
    /// The boundary (100) is a <strong>test fixture</strong>, not a shipped value: every shipped
    /// ruleset leaves <see cref="MapMarkerRules.CityPopulationTierThresholds"/> empty on purpose (the
    /// field's own provenance records why), so there are no real boundaries to read until a ruleset
    /// supplies them — and <c>data/rulesets</c> is deliberately not touched by this task. The army and
    /// fleet boundaries this file also asserts are the shipped, confirmed ones.
    /// </remarks>
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
        Assert.Equal(AssetKeys.CityTier1Icon, MapMarkerKeys.CityIcon(after, after.CityById(oldCapital.Id)!, rules.MapMarkers));
    }

    private static ValueList<UnitSlot> OneUnitOf(int troops) =>
        ValueList<UnitSlot>.Of(new UnitSlot(0, "light_infantry", troops, 6, "test unit"));

    private static ArmyState Army(GameState state, string id) => state.Armies.Single(a => a.Id == id);

    private static FleetState Fleet(GameState state, string id) => state.Fleets.Single(f => f.Id == id);

    private static ValueList<CityState> ReplaceCity(ValueList<CityState> cities, CityState updated) =>
        ValueList.From(cities.Select(c => c.Id == updated.Id ? updated : c));
}
