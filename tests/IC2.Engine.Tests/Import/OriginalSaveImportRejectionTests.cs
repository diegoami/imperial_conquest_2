using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Import;

/// <summary>
/// Done-when 3: "importing onto a different ruleset or world id is rejected with the specified message"
/// — game-design.md §"Original-save compatibility". None of these tests needs a real <c>.sav</c> byte or
/// <c>assets.local.ini</c>: <see cref="OriginalSaveImporter.Import"/> checks the World/Ruleset id before
/// it ever touches the save bytes, so an empty byte array is enough to prove the rejection fires first.
/// </summary>
public class OriginalSaveImportRejectionTests
{
    private static readonly World RealWorld = RealGameData.World;
    private static readonly Ruleset RealRuleset = RealGameData.Ruleset;
    private static readonly Scenario RealScenario = RealGameData.Scenario;

    [Fact]
    public void A_world_id_other_than_classical_mediterranean_is_rejected_with_a_clear_message()
    {
        var wrongWorld = RealWorld with { Id = "some-other-world" };

        var ex = Assert.Throws<SaveContextMismatchException>(() => OriginalSaveImporter.Import(
            Array.Empty<byte>(), "test.sav", wrongWorld, RealRuleset, RealScenario, "save-1", "Save 1"));

        Assert.Equal("world", ex.Kind);
        Assert.Equal("some-other-world", ex.ExpectedId);
        Assert.Equal(OriginalSaveImporter.RequiredWorldId, ex.FoundId);
        Assert.Contains("some-other-world", ex.Message);
        Assert.Contains(OriginalSaveImporter.RequiredWorldId, ex.Message);
    }

    [Fact]
    public void A_ruleset_id_other_than_classical_faithful_is_rejected_with_a_clear_message()
    {
        var wrongRuleset = RealRuleset with { Id = "some-other-ruleset" };

        var ex = Assert.Throws<SaveContextMismatchException>(() => OriginalSaveImporter.Import(
            Array.Empty<byte>(), "test.sav", RealWorld, wrongRuleset, RealScenario, "save-1", "Save 1"));

        Assert.Equal("ruleset", ex.Kind);
        Assert.Equal("some-other-ruleset", ex.ExpectedId);
        Assert.Equal(OriginalSaveImporter.RequiredRulesetId, ex.FoundId);
        Assert.Contains("some-other-ruleset", ex.Message);
        Assert.Contains(OriginalSaveImporter.RequiredRulesetId, ex.Message);
    }

    [Fact]
    public void The_world_check_runs_before_any_save_byte_is_read()
    {
        // Garbage bytes would fail IC2.Data's own SAV-shape probe with a completely different exception
        // type (UnrecognizedSaveFormatException) -- proving the rejection is still SaveContextMismatchException
        // here shows the id check really does run first, exactly as the DoD says ("rejected... not
        // silently reinterpreted").
        var wrongWorld = RealWorld with { Id = "some-other-world" };
        var garbage = new byte[] { 1, 2, 3 };

        Assert.Throws<SaveContextMismatchException>(() => OriginalSaveImporter.Import(
            garbage, "test.sav", wrongWorld, RealRuleset, RealScenario, "save-1", "Save 1"));
    }

    [Fact]
    public void A_world_with_the_wrong_nation_count_under_the_right_id_is_rejected_naming_the_count()
    {
        // Positional city/nation mapping (OriginalSaveImporter's own remarks) only holds for a world
        // shaped exactly like the shipped one; this is the defensive guard for a corrupted or
        // hand-edited copy under the right id, not a scenario game-design.md itself describes.
        var truncatedWorld = RealWorld with { Nations = ValueList<NationDefinition>.Empty };

        var ex = Assert.Throws<InvalidDataException>(() => OriginalSaveImporter.Import(
            Array.Empty<byte>(), "test.sav", truncatedWorld, RealRuleset, RealScenario, "save-1", "Save 1"));

        Assert.Contains("0 nations", ex.Message);
        Assert.Contains("16", ex.Message);
    }

    [Fact]
    public void A_world_with_the_wrong_city_count_under_the_right_id_is_rejected_naming_the_count()
    {
        var truncatedWorld = RealWorld with { Cities = ValueList<CityDefinition>.Empty };

        var ex = Assert.Throws<InvalidDataException>(() => OriginalSaveImporter.Import(
            Array.Empty<byte>(), "test.sav", truncatedWorld, RealRuleset, RealScenario, "save-1", "Save 1"));

        Assert.Contains("0 cities", ex.Message);
        Assert.Contains("334", ex.Message);
    }
}
