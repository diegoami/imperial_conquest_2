using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T75 Done-when 2: <see cref="World.ValidateStartingRelations"/> and
/// <see cref="World.ValidateStartingNews"/> each reject the six malformed shapes the task entry names,
/// with a typed error (<see cref="InvalidOperationException"/> — the same type
/// <see cref="TerrainGrid.Decode"/> and <see cref="Ruleset.ValidateSeasonNames"/> throw for the same
/// reason: <c>GameDataValidation</c> is the layer that turns a model-level <see cref="InvalidOperationException"/>
/// into a <see cref="MalformedGameDataException"/> naming the document).
/// </summary>
/// <remarks>
/// These methods are not yet called from <see cref="GameDataLoader.Load{T}(string,string)"/> — see this
/// task's PR for why (a scope note: wiring them in touches <c>GameDataValidation.ValidateWorld</c>, which
/// is outside this task's Owns list). Tested here by calling them directly, against the shipped toy
/// world's own three nations, so a future wiring change has full coverage of the checks themselves
/// already in place.
/// </remarks>
public class WorldStartingDataValidationTests
{
    private static World ToyWorld() => GameDataLoader.LoadFile<World>(ModelTestPaths.ToyWorldFile);

    private static Ruleset ToyRuleset() => GameDataLoader.LoadFile<Ruleset>(ModelTestPaths.ToyRulesetFile);

    private static string[] ToyNationIds() => ToyWorld().Nations.Select(n => n.Id).ToArray();

    private static DiplomaticRelations UniformMatrix(string[] nationIds, int value) =>
        DiplomaticRelations.Uniform(ValueList.From(nationIds), value);

    /// <summary>A world with no starting data at all validates as a no-op for both checks.</summary>
    [Fact]
    public void A_world_with_no_starting_data_validates_as_a_no_op()
    {
        var world = ToyWorld();
        Assert.Null(world.StartingRelations);
        Assert.Null(world.StartingNews);

        world.ValidateStartingRelations(ToyRuleset());
        world.ValidateStartingNews(ToyRuleset());
    }

    /// <summary>A well-formed, all-peace matrix over the world's own nations passes.</summary>
    [Fact]
    public void A_well_formed_all_peace_matrix_passes()
    {
        var ids = ToyNationIds();
        var ruleset = ToyRuleset();
        var world = ToyWorld() with { StartingRelations = UniformMatrix(ids, ruleset.Diplomacy.StateCodes.Peace) };

        world.ValidateStartingRelations(ruleset);
    }

    [Fact]
    public void A_matrix_that_is_not_symmetric_is_rejected()
    {
        var ids = ToyNationIds();
        var ruleset = ToyRuleset();
        var uniform = UniformMatrix(ids, ruleset.Diplomacy.StateCodes.Peace);
        // WithRelation always keeps [a][b] and [b][a] equal by construction (DiplomaticRelations'
        // own doc comment), so an intentionally lopsided matrix has to be built by hand instead.
        var rows = uniform.Matrix.Select(r => r.ToArray()).ToArray();
        rows[0][1] = ruleset.Diplomacy.StateCodes.War; // [0][1] = War but [1][0] stays Peace
        var lopsided = new DiplomaticRelations(uniform.NationIds, ValueList.From(rows.Select(ValueList.From)));

        var world = ToyWorld() with { StartingRelations = lopsided };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingRelations(ruleset));
        Assert.Contains("symmetric", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_non_zero_diagonal_is_rejected()
    {
        var ids = ToyNationIds();
        var ruleset = ToyRuleset();
        var uniform = UniformMatrix(ids, ruleset.Diplomacy.StateCodes.Peace);
        var rows = uniform.Matrix.Select(r => r.ToArray()).ToArray();
        rows[0][0] = ruleset.Diplomacy.StateCodes.War; // still "symmetric" (diagonal), just non-zero
        var badDiagonal = new DiplomaticRelations(uniform.NationIds, ValueList.From(rows.Select(ValueList.From)));

        var world = ToyWorld() with { StartingRelations = badDiagonal };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingRelations(ruleset));
        Assert.Contains("diagonal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_value_outside_the_relation_code_and_cooldown_range_is_rejected()
    {
        var ids = ToyNationIds();
        var ruleset = ToyRuleset();
        var minCooldown = new[]
        {
            ruleset.Diplomacy.CooldownAfterBrokenTrade,
            ruleset.Diplomacy.CooldownAfterBrokenAlliance,
            ruleset.Diplomacy.CooldownAfterEndedWar,
            ruleset.Diplomacy.CooldownAfterPeaceTerms,
            ruleset.Diplomacy.CooldownAfterAllyPeace,
        }.Min();

        // Only the off-diagonal cells go out of range -- the diagonal stays 0, so this test isolates
        // the value-range check from the (separately tested) non-zero-diagonal check.
        var uniform = UniformMatrix(ids, ruleset.Diplomacy.StateCodes.Peace);
        var rows = uniform.Matrix.Select(r => r.ToArray()).ToArray();
        rows[0][1] = minCooldown - 1;
        rows[1][0] = minCooldown - 1; // keep it symmetric, so this is the only rule tripped
        var outOfRange = new DiplomaticRelations(uniform.NationIds, ValueList.From(rows.Select(ValueList.From)));

        var world = ToyWorld() with { StartingRelations = outOfRange };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingRelations(ruleset));
        Assert.Contains("range", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_nation_id_not_in_the_world_is_rejected()
    {
        var ruleset = ToyRuleset();
        var badIds = new[] { "not-a-real-nation", "also-not-real" };
        var world = ToyWorld() with { StartingRelations = UniformMatrix(badIds, ruleset.Diplomacy.StateCodes.Peace) };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingRelations(ruleset));
        Assert.Contains("not-a-real-nation", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A well-formed news log (a few short slots, a consistent index) passes.</summary>
    [Fact]
    public void A_well_formed_news_log_passes()
    {
        var ruleset = ToyRuleset();
        var slots = ValueList.Of(new NewsEntry("First."), new NewsEntry("Second."));
        var world = ToyWorld() with { StartingNews = new NewsLog(MostRecentSlot: 1, slots) };

        world.ValidateStartingNews(ruleset);
    }

    [Fact]
    public void A_news_text_over_the_ruleset_message_length_is_rejected()
    {
        var ruleset = ToyRuleset();
        var tooLong = new string('x', ruleset.NewsLog.MessageByteLength); // == limit, so 1 over the -1 budget
        var slots = ValueList.Of(new NewsEntry(tooLong));
        var world = ToyWorld() with { StartingNews = new NewsLog(MostRecentSlot: 0, slots) };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingNews(ruleset));
        Assert.Contains("byte", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_news_index_outside_minus_one_to_slots_minus_one_is_rejected()
    {
        var ruleset = ToyRuleset();
        var tooManySlots = Enumerable.Range(0, ruleset.NewsLog.RingBufferSlots + 1)
            .Select(i => new NewsEntry($"slot {i}"))
            .ToArray();
        var world = ToyWorld() with
        {
            StartingNews = new NewsLog(MostRecentSlot: ruleset.NewsLog.RingBufferSlots, ValueList.Of(tooManySlots)),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingNews(ruleset));
        Assert.Contains("mostRecentSlot", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inconsistent_index_that_does_not_address_the_last_slot_is_rejected()
    {
        var ruleset = ToyRuleset();
        var slots = ValueList.Of(new NewsEntry("Only one slot."));
        var world = ToyWorld() with { StartingNews = new NewsLog(MostRecentSlot: 5, slots) };

        var ex = Assert.Throws<InvalidOperationException>(() => world.ValidateStartingNews(ruleset));
        Assert.Contains("does not address", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
