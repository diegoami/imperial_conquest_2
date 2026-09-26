using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T75 Done-when 2's load-time half: a matrix that is not symmetric; a non-zero diagonal; a nation id
/// not in the world; a matrix whose nation ids are not exactly the world's nations, each once (T75
/// rework B2: missing or duplicated); and a news index that does not address the last of its slots.
/// Each goes through <see cref="GameDataValidation.Validate"/> — the exact call
/// <see cref="GameDataLoader.Load{T}(string,string)"/> itself makes after deserializing — so these
/// tests prove <strong>loading</strong> rejects, not just that the checking method does (T75 rework B1).
/// </summary>
/// <remarks>
/// The ruleset-dependent half of Done-when 2 — a relation value outside the ruleset's codes/cooldown
/// range, and a news text over the ruleset's message length in bytes or holding a non-printable byte —
/// needs a <see cref="Ruleset"/>, which <see cref="GameDataValidation.ValidateWorld"/> does not have;
/// those checks run in <see cref="GameStateFactory"/> instead and are tested in
/// <see cref="GameStateFactoryStartingDataTests"/>.
/// </remarks>
public class WorldStartingDataValidationTests
{
    private static World ToyWorld() => GameDataLoader.LoadFile<World>(ModelTestPaths.ToyWorldFile);

    private static string[] ToyNationIds() => ToyWorld().Nations.Select(n => n.Id).ToArray();

    private static DiplomaticRelations UniformMatrix(string[] nationIds, int value) =>
        DiplomaticRelations.Uniform(ValueList.From(nationIds), value);

    private const string DocumentPath = "world-under-test.json (in-memory)";

    /// <summary>A world with no starting data at all validates as a no-op for both checks.</summary>
    [Fact]
    public void A_world_with_no_starting_data_validates_as_a_no_op()
    {
        var world = ToyWorld();
        Assert.Null(world.StartingRelations);
        Assert.Null(world.StartingNews);

        GameDataValidation.Validate(DocumentPath, world);
    }

    /// <summary>A well-formed, all-peace matrix over exactly the world's own nations, in order, passes.</summary>
    [Fact]
    public void A_well_formed_all_peace_matrix_passes()
    {
        var ids = ToyNationIds();
        var world = ToyWorld() with { StartingRelations = UniformMatrix(ids, 0) };

        GameDataValidation.Validate(DocumentPath, world);
    }

    [Fact]
    public void A_matrix_that_is_not_symmetric_is_rejected()
    {
        var ids = ToyNationIds();
        var uniform = UniformMatrix(ids, 0);
        // WithRelation always keeps [a][b] and [b][a] equal by construction (DiplomaticRelations' own
        // doc comment), so an intentionally lopsided matrix has to be built by hand instead.
        var rows = uniform.Matrix.Select(r => r.ToArray()).ToArray();
        rows[0][1] = 3; // War: [0][1] = War but [1][0] stays Peace
        var lopsided = new DiplomaticRelations(uniform.NationIds, ValueList.From(rows.Select(ValueList.From)));
        var world = ToyWorld() with { StartingRelations = lopsided };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("symmetric", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_non_zero_diagonal_is_rejected()
    {
        var ids = ToyNationIds();
        var uniform = UniformMatrix(ids, 0);
        var rows = uniform.Matrix.Select(r => r.ToArray()).ToArray();
        rows[0][0] = 3; // War: still "symmetric" (it's the diagonal), just non-zero
        var badDiagonal = new DiplomaticRelations(uniform.NationIds, ValueList.From(rows.Select(ValueList.From)));
        var world = ToyWorld() with { StartingRelations = badDiagonal };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("diagonal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>T75 rework B1: an unknown nation id is a typed reference failure, not a generic malformed one.</summary>
    [Fact]
    public void A_nation_id_not_in_the_world_is_rejected()
    {
        var badIds = new[] { "not-a-real-nation", "also-not-real" };
        var world = ToyWorld() with { StartingRelations = UniformMatrix(badIds, 0) };

        var ex = Assert.Throws<UnresolvedReferenceException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Equal("nation", ex.Kind);
        Assert.Equal("not-a-real-nation", ex.Id);
    }

    /// <summary>
    /// T75 rework B2: a matrix missing one of the world's nations used to pass and then crash the first
    /// diplomacy lookup for the missing nation; it is now rejected at load.
    /// </summary>
    [Fact]
    public void A_matrix_missing_a_world_nation_is_rejected()
    {
        var ids = ToyNationIds();
        var partial = new[] { ids[0] }; // "north" only -- "south" is missing
        var world = ToyWorld() with { StartingRelations = UniformMatrix(partial, 0) };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("exactly this world's own nations", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>T75 rework B2: a matrix that lists one nation twice (and so omits another) is rejected.</summary>
    [Fact]
    public void A_matrix_with_a_duplicated_nation_id_is_rejected()
    {
        var ids = ToyNationIds();
        var duplicated = new[] { ids[0], ids[0] }; // "north" twice -- "south" never appears
        var world = ToyWorld() with { StartingRelations = UniformMatrix(duplicated, 0) };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("exactly this world's own nations", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Follow-up #372 N3: <c>ValidateStartingRelationsShape</c>'s own remarks (World.cs:101-108) say a
    /// matrix agreeing with <c>Nations</c> on membership but not on order must still be rejected -- a
    /// plain set-equality check would wrongly accept it, and the order match exists specifically to keep
    /// <c>NationIds</c>-order iteration (<c>QuarterlyThawSystem</c>, <c>PeaceTreatySystem</c>,
    /// <c>RelationTransitions</c>, <c>PendingOfferSystem</c>) identical to the uniform-peace default's own
    /// order. Same two nations as <see cref="A_well_formed_all_peace_matrix_passes"/>, reversed.
    /// </summary>
    [Fact]
    public void A_matrix_with_the_worlds_own_nations_in_a_different_order_is_rejected()
    {
        var ids = ToyNationIds();
        var reversed = ids.Reverse().ToArray();
        Assert.NotEqual(ids, reversed); // the toy world's own nations are not order-symmetric already
        var world = ToyWorld() with { StartingRelations = UniformMatrix(reversed, 0) };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("exactly this world's own nations", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same order", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A well-formed news log (a few short slots, an index addressing the last one) passes.</summary>
    [Fact]
    public void A_well_formed_news_log_passes()
    {
        var slots = ValueList.Of(new NewsEntry("First."), new NewsEntry("Second."));
        var world = ToyWorld() with { StartingNews = new NewsLog(MostRecentSlot: 1, slots) };

        GameDataValidation.Validate(DocumentPath, world);
    }

    /// <summary>T75 rework N1: an index below -1 (an empty log's only valid value) is rejected.</summary>
    [Fact]
    public void A_news_index_of_minus_two_is_rejected()
    {
        var world = ToyWorld() with { StartingNews = new NewsLog(MostRecentSlot: -2, ValueList<NewsEntry>.Empty) };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("does not address", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>T75 rework N1: an index that names some slot other than the last one is rejected.</summary>
    [Fact]
    public void A_news_index_that_does_not_address_the_last_slot_is_rejected()
    {
        var slots = ValueList.Of(new NewsEntry("Only one slot."));
        var world = ToyWorld() with { StartingNews = new NewsLog(MostRecentSlot: 5, slots) };

        var ex = Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate(DocumentPath, world));
        Assert.Contains("does not address", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
