using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T20.md</c> Done-when 3: "An unknown future version is rejected with a typed error, not
/// best-effort parsed."
/// </summary>
public sealed class SaveFormatVersionTests
{
    [Fact]
    public void A_save_format_version_newer_than_this_build_supports_is_rejected_with_a_typed_error()
    {
        var toy = PersistenceTestbed.Toy;
        var state = PersistenceTestbed.PlayTurns(5);

        var save = new IC2.Engine.Model.SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "toy-3city-future",
            Label: "Toy future-format fixture",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        var currentText = SaveManager.Serialize(save);

        // A future build's envelope, simulated by writing one version past what this build supports —
        // never by hand-crafting an unrelated shape, so the test proves the version gate itself, not
        // some other parse failure.
        var futureText = currentText.Replace(
            $"\"{SaveFormat.VersionField}\": {SaveFormat.CurrentVersion}",
            $"\"{SaveFormat.VersionField}\": {SaveFormat.CurrentVersion + 1}",
            StringComparison.Ordinal);
        Assert.NotEqual(currentText, futureText); // the replace actually matched something

        var ex = Assert.Throws<UnsupportedSaveFormatException>(
            () => SaveManager.Load("future-save.json", futureText, toy.World, toy.Ruleset));

        Assert.Equal(SaveFormat.CurrentVersion + 1, ex.Found);
        Assert.Equal(SaveFormat.CurrentVersion, ex.Supported);
    }

    [Fact]
    public void A_save_format_version_this_build_does_not_recognise_is_never_best_effort_parsed()
    {
        var toy = PersistenceTestbed.Toy;

        // A future envelope may carry fields this build has never seen; the rejection must happen from
        // the version check alone, before any attempt to read the rest of the document.
        var futureEnvelope =
            $$"""
            {
              "{{SaveFormat.VersionField}}": {{SaveFormat.CurrentVersion + 5}},
              "aFieldThisBuildHasNeverSeen": { "nested": true },
              "{{SaveFormat.PayloadField}}": "not even an object"
            }
            """;

        Assert.Throws<UnsupportedSaveFormatException>(
            () => SaveManager.Load("far-future-save.json", futureEnvelope, toy.World, toy.Ruleset));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_save_format_version_below_the_earliest_this_build_ever_shipped_gets_an_accurate_message(
        int inventedVersion)
    {
        // Review round 1 (N2): SaveMigrations' catch-all reused UnsupportedSaveFormatException's "was
        // written by a newer build" message for this direction too, which is simply false for a version
        // below SaveFormat.MinimumSupportedVersion -- such a file was never written by any build, newer
        // or otherwise. The exception TYPE was already right (this is not a new bug in behaviour, only in
        // wording), so this test is about the message, not about whether it throws.
        var toy = PersistenceTestbed.Toy;
        var envelope =
            $$"""
            {
              "{{SaveFormat.VersionField}}": {{inventedVersion}},
              "{{SaveFormat.PayloadField}}": {}
            }
            """;

        var ex = Assert.Throws<UnsupportedSaveFormatException>(
            () => SaveManager.Load("invented-old-save.json", envelope, toy.World, toy.Ruleset));

        Assert.Equal(inventedVersion, ex.Found);
        Assert.DoesNotContain("newer build", ex.Message, StringComparison.Ordinal);
        Assert.Contains("never", ex.Message, StringComparison.Ordinal);

        // Review round 2 (R3): Contains(MinimumSupportedVersion.ToString()) checked for the substring
        // "1", which every version of this message already contains for unrelated reasons (it names
        // inventedVersion itself when inventedVersion is 1-and-something, and "-1" contains "1" too).
        // Checking the exact clause that names the earliest version is what actually ties the message
        // to SaveFormat.MinimumSupportedVersion.
        Assert.Contains(
            $"the earliest is version {SaveFormat.MinimumSupportedVersion}", ex.Message, StringComparison.Ordinal);
    }
}
