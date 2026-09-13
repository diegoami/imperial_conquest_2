using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// DoD 2: "The toy scenario loads from <c>data/scenarios/toy-3city.json</c> and resolves its world and
/// ruleset by id."
/// </summary>
public class ScenarioLoadingTests
{
    [Fact]
    public void Toy_scenario_loads_from_its_shipped_file()
    {
        var scenario = GameDataLoader.LoadFile<Scenario>(TestPaths.ToyScenarioFile);

        Assert.Equal("toy-3city", scenario.Id);
        Assert.Equal("toy-3city", scenario.WorldId);
        Assert.Equal("toy-ruleset", scenario.RulesetId);
        Assert.Equal(2, scenario.Seats.Count);
        Assert.Equal(SeatControl.Human, scenario.SeatFor("north")!.Control);
        Assert.Equal(SeatControl.Ai, scenario.SeatFor("south")!.Control);
    }

    [Fact]
    public void Toy_scenario_resolves_its_world_and_ruleset_by_id()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);

        var resolved = repository.Resolve("toy-3city");

        Assert.Equal("toy-3city", resolved.Scenario.Id);
        Assert.Equal(resolved.Scenario.WorldId, resolved.World.Id);
        Assert.Equal(resolved.Scenario.RulesetId, resolved.Ruleset.Id);
        Assert.Equal(3, resolved.World.Cities.Count);
        Assert.Equal(2, resolved.World.Nations.Count);
    }

    [Fact]
    public void Resolving_a_scenario_produces_a_startable_state()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);

        var state = repository.CreateInitialState("toy-3city");

        Assert.Equal("toy-3city", state.ScenarioId);
        Assert.Equal("toy-ruleset", state.RulesetId);
        Assert.Equal(3, state.Cities.Count);
        Assert.Equal("north", state.ActiveNationId);
        Assert.Equal(SeatControl.Human, state.NationById("north")!.Control);
        Assert.Equal(SeatControl.Ai, state.NationById("south")!.Control);
        Assert.NotNull(state.NationById("south")!.Personality);
    }

    [Fact]
    public void An_unknown_scenario_id_is_an_unresolved_reference_not_a_null()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);

        var error = Assert.Throws<UnresolvedReferenceException>(() => repository.Resolve("no-such-scenario"));

        Assert.Equal("scenario", error.Kind);
        Assert.Equal("no-such-scenario", error.Id);
    }

    [Fact]
    public void A_scenario_naming_a_missing_world_is_an_unresolved_reference()
    {
        // A fixed directory name, not a generated one: nothing in this repository reaches for a random
        // identifier, so the tests do not either (docs/build-orchestration-plan.md §6.2 gate 3).
        var root = Path.Combine(Path.GetTempPath(), "ic2-t02-unresolved-world");
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(Path.Combine(root, GameDataRepository.ScenariosDirectoryName));
            var scenario = GameDataLoader.LoadFile<Scenario>(TestPaths.ToyScenarioFile) with
            {
                WorldId = "absent-world",
            };
            File.WriteAllText(
                Path.Combine(root, GameDataRepository.ScenariosDirectoryName, "toy.json"),
                GameJson.Serialize(scenario));

            var repository = GameDataRepository.Load(root);

            var error = Assert.Throws<UnresolvedReferenceException>(() => repository.Resolve("toy-3city"));
            Assert.Equal("world", error.Kind);
            Assert.Equal("absent-world", error.Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
