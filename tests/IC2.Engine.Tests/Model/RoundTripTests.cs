using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// DoD 1: "Round-trip equality tests pass for each of <c>World</c>, <c>Ruleset</c>, <c>Scenario</c>,
/// <c>SaveGame</c> (serialize → deserialize → serialize produces identical JSON)."
/// </summary>
public class RoundTripTests
{
    [Fact]
    public void World_round_trips_to_identical_json()
    {
        AssertRoundTrips(GameDataLoader.LoadFile<World>(TestPaths.ToyWorldFile), "world");
    }

    [Fact]
    public void Ruleset_round_trips_to_identical_json()
    {
        AssertRoundTrips(GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile), "ruleset");
    }

    [Fact]
    public void Scenario_round_trips_to_identical_json()
    {
        AssertRoundTrips(GameDataLoader.LoadFile<Scenario>(TestPaths.ToyScenarioFile), "scenario");
    }

    [Fact]
    public void SaveGame_round_trips_to_identical_json()
    {
        AssertRoundTrips(ToyFixtures.NonTrivialSave(), "save");
    }

    [Fact]
    public void World_provenance_entries_survive_the_round_trip_in_order()
    {
        var world = GameDataLoader.LoadFile<World>(TestPaths.ToyWorldFile);
        var reloaded = GameDataLoader.Load<World>("world", GameJson.Serialize(world));

        Assert.NotNull(world.Provenance);
        Assert.Equal(world.Provenance, reloaded.Provenance);
        Assert.NotNull(world.TileTypes[0].Provenance);
        Assert.Equal(world.TileTypes[0].Provenance, reloaded.TileTypes[0].Provenance);
    }

    private static void AssertRoundTrips<T>(T document, string documentName)
        where T : IVersionedDocument
    {
        var first = GameJson.Serialize(document);
        var reloaded = GameDataLoader.Load<T>(documentName, first);
        var second = GameJson.Serialize(reloaded);

        Assert.Equal(first, second);
        Assert.Equal(document, reloaded);
    }
}
