using System.Text.Json.Nodes;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T20.md</c> Done-when 2: "A committed older-version save file loads through a migration
/// and produces the expected state."
/// </summary>
/// <remarks>
/// <c>tests/fixtures/saves/toy-3city-turn-10.v1.json</c> is a real version-1 save: the toy scenario
/// played 20 <em>seat</em>-turns through every registered system — <see cref="TurnCoordinator.RunTurn"/>
/// runs one seat's turn, and on this two-seat toy scenario a full calendar turn is two of those, so 20
/// seat-turns lands at <c>calendar.turnIndex == 10</c>, which is what the file name says — written by
/// this task's own version-1 <see cref="SaveManager"/> (commit 646c1a2, before
/// <see cref="SaveFormat.CurrentVersion"/> became 2). It carries no <c>turnIndex</c> envelope field,
/// because version 1 never wrote one. <see cref="SaveMigrations"/>'s version-1-to-2 step is what has to
/// supply it on load — and review round 1 found that nothing actually checked the value it supplies, only
/// that some value was present; see the two tests below that were added to close that gap (B2).
/// </remarks>
public sealed class SaveMigrationTests
{
    private static string FixturePath => Path.Combine(
        TestPaths.RepositoryRoot, "tests", "fixtures", "saves", "toy-3city-turn-10.v1.json");

    [Fact]
    public void A_version_1_fixture_carries_no_turnIndex_envelope_field()
    {
        // Documents the premise the migration test below depends on: if this ever stopped being true
        // (the fixture got regenerated with newer code, say), the migration test would no longer be
        // exercising a migration at all, silently. Checked on the parsed envelope's own top-level keys,
        // not a raw string search -- the nested state carries its own, unrelated calendar.turnIndex.
        var envelope = (JsonObject)JsonNode.Parse(File.ReadAllText(FixturePath))!;
        Assert.Equal(1, envelope["saveFormatVersion"]!.GetValue<int>());
        Assert.False(envelope.ContainsKey("turnIndex"));
    }

    [Fact]
    public void Loading_the_version_1_fixture_migrates_it_and_produces_the_expected_state()
    {
        var toy = PersistenceTestbed.Toy;

        var loaded = SaveManager.LoadFile(FixturePath, toy.World, toy.Ruleset);

        // The expected state: replaying the same 20 seat-turns fresh, with today's code, from the same
        // scenario. If the migration silently dropped or corrupted anything beyond the envelope's own
        // turnIndex field, this equality is what would catch it -- not just "it didn't throw".
        var expected = PersistenceTestbed.PlayTurns(20);
        Assert.Equal(GameStateHash.Compute(expected), GameStateHash.Compute(loaded.State));
        Assert.Equal(expected, loaded.State);

        // "toy-3city-turn-20" is the save's own id, exactly as the version-1 fixture's content already
        // carries it (renaming the file in review round 1 did not touch the committed bytes -- the
        // fixture's own id predates the file being renamed to match its actual turn index).
        Assert.Equal("toy-3city-turn-20", loaded.Id);
        Assert.Equal(toy.World.Id, loaded.WorldId);
        Assert.Equal(toy.Ruleset.Id, loaded.RulesetId);
    }

    [Fact]
    public void PeekSummaryFile_on_the_version_1_fixture_reports_the_turnIndex_the_migration_supplied()
    {
        // Review round 1 (B2, mutation M4): making MigrateV1ToV2 write turnIndex = 0 instead of the real
        // value left every existing test green, because none of them read the migrated envelope's own
        // turnIndex through the one caller that trusts it without SaveManager.Load's cross-check --
        // PeekSummary/PeekSummaryFile, the save-picker path the field exists for in the first place. This
        // is that check: the v1 fixture's nested state.calendar.turnIndex is 10 (20 seat-turns on the
        // two-seat toy scenario), so a migration that dropped, zeroed or otherwise miscomputed the value
        // fails this directly, with no cross-check involved.
        var summary = SaveManager.PeekSummaryFile(FixturePath);

        Assert.Equal(10, summary.TurnIndex);
        Assert.Equal("toy-3city-turn-20", summary.Id);
        Assert.Equal("toy-3city", summary.WorldId);
        Assert.Equal("toy-ruleset", summary.RulesetId);
    }

    [Fact]
    public void Loading_a_current_format_envelope_whose_turnIndex_disagrees_with_its_state_is_rejected()
    {
        // Review round 1 (B2, mutation M3): deleting SaveManager.Load's envelope/state turnIndex
        // cross-check left every existing test green, because nothing ever wrote a mismatched envelope to
        // begin with. This constructs one directly -- a well-formed, current-format save whose envelope
        // turnIndex has been tampered with after writing -- so the cross-check is exercised on its own,
        // independent of the migration path.
        var toy = PersistenceTestbed.Toy;
        var state = PersistenceTestbed.PlayTurns(5);
        var save = new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: "toy-3city-turnindex-mismatch",
            Label: "Toy turnIndex-mismatch fixture",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        var envelope = (JsonObject)JsonNode.Parse(SaveManager.Serialize(save))!;
        var realTurnIndex = envelope[SaveFormat.TurnIndexField]!.GetValue<int>();
        envelope[SaveFormat.TurnIndexField] = realTurnIndex + 1;

        var ex = Assert.Throws<MalformedGameDataException>(
            () => SaveManager.Load("mismatched-turnindex.json", envelope.ToJsonString(), toy.World, toy.Ruleset));

        Assert.Contains("disagrees", ex.Message, StringComparison.Ordinal);
        Assert.Contains((realTurnIndex + 1).ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains(realTurnIndex.ToString(), ex.Message, StringComparison.Ordinal);
    }
}
