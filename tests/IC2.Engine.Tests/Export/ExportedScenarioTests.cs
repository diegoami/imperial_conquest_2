using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Checks the committed <c>data/scenarios/classical-mediterranean.json</c> against
/// <c>docs/task-catalogue.md</c> T29's Done-when lines 4 and 8.
/// </summary>
public class ExportedScenarioTests
{
    private static Scenario LoadScenario() => GameDataLoader.LoadFile<Scenario>(ExportedDataPaths.ScenarioFile);
    private static World LoadWorld() => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);
    private static Ruleset LoadRuleset() => GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.RulesetFile);

    /// <summary>DoD 4 and DoD 8: the scenario loads and references the exported world and ruleset.</summary>
    [Fact]
    public void Scenario_loads_and_references_the_exported_world_and_ruleset()
    {
        var scenario = LoadScenario();

        Assert.Equal("classical-mediterranean", scenario.WorldId);
        Assert.Equal("classical-faithful", scenario.RulesetId);
    }

    /// <summary>DoD 8: every nation in the world has a seat, control defaulting to AI.</summary>
    [Fact]
    public void Every_nation_has_a_seat_defaulting_to_AI()
    {
        var world = LoadWorld();
        var scenario = LoadScenario();

        Assert.Equal(world.Nations.Count, scenario.Seats.Count);
        foreach (var nation in world.Nations)
        {
            var seat = scenario.SeatFor(nation.Id);
            Assert.NotNull(seat);
            Assert.Equal(SeatControl.Ai, seat!.Control);
        }
    }

    /// <summary>
    /// DoD 8: the world, ruleset and scenario together build an initial <see cref="GameState"/> with
    /// no errors -- the seam every later loader-consuming task (T21, T23, T24, ...) depends on.
    /// </summary>
    [Fact]
    public void World_ruleset_and_scenario_build_an_initial_GameState()
    {
        var world = LoadWorld();
        var ruleset = LoadRuleset();
        var scenario = LoadScenario();

        var state = GameStateFactory.CreateInitial(world, ruleset, scenario);

        Assert.Equal(world.Nations.Count, state.Nations.Count);
        Assert.Equal(world.Cities.Count, state.Cities.Count);
        Assert.Equal(world.StartingArmies.Count, state.Armies.Count);
        Assert.Equal(world.StartingFleets.Count, state.Fleets.Count);
        GameDataValidation.Validate("classical-mediterranean.json (state)", state);
    }
}
