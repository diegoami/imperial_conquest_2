using System.Text.Json;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// T161: a new game on <c>classical-mediterranean</c> starts with every nation's recruitment queue as the
/// original's DAT gives it, slot by slot. The expected values come from
/// <c>tests/fixtures/new-game-recruitment-queues.json</c>, copied from the research report
/// 2026-09-29-new-game-recruitment-queues-come-from-the-dat.md, never from the world file under test.
/// </summary>
public class StartingRecruitmentQueuesTests
{
    private const string DocumentPath = "world-under-test.json (in-memory)";
    private const string RomeId = "rome";

    private static ResolvedScenario Classical() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");

    private static ResolvedScenario Toy() =>
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("toy-3city");

    private sealed record FixtureNation(string Nation, int SlotCount, int TroopTotal, string CityId, RecruitmentSlot[] Slots);

    private static FixtureNation[] Expected()
    {
        var path = Path.Combine(ModelTestPaths.RepositoryRoot, "tests", "fixtures", "new-game-recruitment-queues.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("nations").EnumerateArray().Select(n => new FixtureNation(
            n.GetProperty("nation").GetString()!,
            n.GetProperty("slotCount").GetInt32(),
            n.GetProperty("troopTotal").GetInt32(),
            n.GetProperty("cityId").GetString()!,
            n.GetProperty("slots").EnumerateArray().Select(s => new RecruitmentSlot(
                s.GetProperty("targetCityId").GetString()!,
                s.GetProperty("unitTypeId").GetString()!,
                s.GetProperty("troops").GetInt32(),
                s.GetProperty("stateCode").GetInt32())).ToArray())).ToArray();
    }

    /// <summary>Done-when 1: Rome's queue, before any turn runs, is the report's four units at city 85.</summary>
    [Fact]
    public void Rome_starts_with_the_reports_four_units_in_training_at_city_85()
    {
        var classical = Classical();
        var state = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);

        // The DAT's city 85 is Rome itself: the exporter's index-to-id mapping, pinned by id here.
        Assert.Equal("rome", classical.World.Cities[85].Id);

        var slots = state.NationById(RomeId)!.RecruitmentSlots;
        Assert.Equal(
            new[]
            {
                new RecruitmentSlot("rome", "light_infantry", 5500, 9),
                new RecruitmentSlot("rome", "heavy_infantry", 3500, 13),
                new RecruitmentSlot("rome", "heavy_infantry", 4000, 17),
                new RecruitmentSlot("rome", "heavy_cavalry", 1000, 17),
            },
            slots.ToArray());
        Assert.Equal(14000, slots.Sum(s => s.Troops));
    }

    /// <summary>Done-when 2: all 16 nations' slot counts, troop totals and slots match the report.</summary>
    [Fact]
    public void Every_nation_starts_with_the_reports_queue()
    {
        var classical = Classical();
        var state = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);
        var expected = Expected();

        Assert.Equal(16, expected.Length);
        Assert.Equal(16, state.Nations.Count);
        foreach (var want in expected)
        {
            var slots = state.NationById(want.Nation)!.RecruitmentSlots;
            Assert.Equal(want.SlotCount, slots.Count);
            Assert.Equal(want.TroopTotal, slots.Sum(s => s.Troops));
            Assert.Equal(want.Slots, slots.ToArray());
            Assert.All(slots, s => Assert.Equal(want.CityId, s.TargetCityId));
        }

        Assert.Equal(2, state.NationById("carthage")!.RecruitmentSlots.Count);
        Assert.Equal(4200, state.NationById("carthage")!.RecruitmentSlots.Sum(s => s.Troops));
        Assert.Equal(8, state.NationById("seleucid")!.RecruitmentSlots.Count);
        Assert.Equal(35200, state.NationById("seleucid")!.RecruitmentSlots.Sum(s => s.Troops));
    }

    /// <summary>Done-when 4: a world with no recruitmentSlots opens with empty queues, as before.</summary>
    [Fact]
    public void A_world_without_recruitment_slots_starts_with_empty_queues()
    {
        var toy = Toy();
        Assert.All(toy.World.Nations, n => Assert.Null(n.RecruitmentSlots));

        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
        Assert.All(state.Nations, n => Assert.Empty(n.RecruitmentSlots));
    }

    /// <summary>Done-when 4: a state holding starting queues survives a save round trip.</summary>
    [Fact]
    public void A_state_with_starting_queues_round_trips()
    {
        var classical = Classical();
        var state = GameStateFactory.CreateInitial(classical.World, classical.Ruleset, classical.Scenario);

        var first = GameJson.Serialize(state);
        var reloaded = GameDataLoader.Load<GameState>("state", first);

        Assert.Equal(first, GameJson.Serialize(reloaded));
        Assert.Equal(
            state.NationById(RomeId)!.RecruitmentSlots.ToArray(),
            reloaded.NationById(RomeId)!.RecruitmentSlots.ToArray());
    }

    /// <summary>The world file round-trips with its new field too.</summary>
    [Fact]
    public void The_classical_world_round_trips()
    {
        var world = Classical().World;
        var first = GameJson.Serialize(world);
        Assert.Equal(first, GameJson.Serialize(GameDataLoader.Load<World>("world", first)));
    }

    private static World ToyWithNorthSlots(params RecruitmentSlot[] slots)
    {
        var toy = Toy().World;
        return toy with
        {
            Nations = ValueList.From(toy.Nations.Select(n =>
                n.Id == "north" ? n with { RecruitmentSlots = ValueList.Of(slots) } : n)),
        };
    }

    /// <summary>Done-when 4: a slot whose city does not exist is refused, naming the key.</summary>
    [Fact]
    public void A_slot_with_an_unknown_city_is_refused_with_its_key()
    {
        var world = ToyWithNorthSlots(new RecruitmentSlot("nowhere", "light_infantry", 100, 0));
        var ex = Assert.Throws<UnresolvedReferenceException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("recruitmentSlots[0].targetCityId", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nowhere", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Done-when 4: a slot whose unit type does not exist is refused, naming the key.</summary>
    [Fact]
    public void A_slot_with_an_unknown_unit_type_is_refused_with_its_key()
    {
        var toy = Toy();
        var world = ToyWithNorthSlots(new RecruitmentSlot("arx", "no_such_unit", 100, 0));
        GameDataValidation.Validate(DocumentPath, world);

        var ex = Assert.Throws<ArgumentException>(() => GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario));
        Assert.Contains("recruitmentSlots[0]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unitTypeId", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no_such_unit", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Done-when 4: a state code outside 0 to the readiness cap is refused, naming the key.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public void A_slot_with_a_state_outside_the_readiness_range_is_refused_with_its_key(int stateCode)
    {
        var toy = Toy();
        var world = ToyWithNorthSlots(new RecruitmentSlot("arx", "light_infantry", 100, stateCode));

        Exception ex;
        if (stateCode < 0)
        {
            ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        }
        else
        {
            GameDataValidation.Validate(DocumentPath, world);
            ex = Assert.Throws<ArgumentException>(() => GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario));
        }

        Assert.Contains("recruitmentSlots[0]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("stateCode", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The boundary state codes 0 and the ruleset's cap are accepted.</summary>
    [Fact]
    public void The_state_range_bounds_are_accepted()
    {
        var toy = Toy();
        var cap = toy.Ruleset.Calendar.CityUnitStateCodeCap;
        var world = ToyWithNorthSlots(
            new RecruitmentSlot("arx", "light_infantry", 100, 0),
            new RecruitmentSlot("arx", "light_infantry", 100, cap));
        GameDataValidation.Validate(DocumentPath, world);
        var state = GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario);
        Assert.Equal(2, state.NationById("north")!.RecruitmentSlots.Count);
    }

    /// <summary>
    /// Done-when 5: the game screen's training view (the model behind the Recruit unit dialog and the
    /// nation overview) lists Rome's four units at game start in a new game as Rome.
    /// </summary>
    [Fact]
    public void The_training_view_shows_romes_four_units_at_game_start()
    {
        var classical = Classical();
        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: 1UL, humanSeatNationId: RomeId);

        var atRome = RecruitmentPanelViewModel.TrainingAtCity(session.State, session.Ruleset, RomeId, "rome");
        Assert.Equal(4, atRome.Count);
        Assert.Equal(
            new[] { ("light_infantry", 5500, 9), ("heavy_infantry", 3500, 13), ("heavy_infantry", 4000, 17), ("heavy_cavalry", 1000, 17) },
            atRome.Select(r => (r.UnitTypeId, r.Troops, r.StateCode)).ToArray());
        Assert.Equal(4, RecruitmentPanelViewModel.TrainingForNation(session.State, session.Ruleset, RomeId).Count);
    }
}
