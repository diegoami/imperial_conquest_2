using System.Text.Json.Nodes;
using IC2.Engine.Core;
using IC2.Engine.Persistence;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <c>docs/tasks/T20.md</c> Done-when 2: "A committed older-version save file loads through a migration
/// and produces the expected state."
/// </summary>
/// <remarks>
/// <c>tests/fixtures/saves/toy-3city-turn-20.v1.json</c> is a real version-1 save: the toy scenario
/// played 20 seat-turns through every registered system, written by this task's own version-1
/// <see cref="SaveManager"/> (commit 646c1a2, before <see cref="SaveFormat.CurrentVersion"/> became 2).
/// It carries no <c>turnIndex</c> envelope field, because version 1 never wrote one.
/// <see cref="SaveMigrations"/>'s version-1-to-2 step is what has to supply it on load.
/// </remarks>
public sealed class SaveMigrationTests
{
    private static string FixturePath => Path.Combine(
        TestPaths.RepositoryRoot, "tests", "fixtures", "saves", "toy-3city-turn-20.v1.json");

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

        Assert.Equal("toy-3city-turn-20", loaded.Id);
        Assert.Equal(toy.World.Id, loaded.WorldId);
        Assert.Equal(toy.Ruleset.Id, loaded.RulesetId);
    }

    [Fact]
    public void The_migrated_envelope_carries_the_correct_turnIndex_not_a_default()
    {
        // A migration step that was deleted (or replaced with a no-op) would either fail SchemaValidator
        // for the missing field two lines further into the load, or -- if turnIndex were instead made
        // an optional field defaulting to 0 -- would silently produce the wrong value here. Asserting
        // the real, non-zero, non-default number is what makes this test fail either way.
        var text = File.ReadAllText(FixturePath);
        var toy = PersistenceTestbed.Toy;
        var loaded = SaveManager.LoadFile(FixturePath, toy.World, toy.Ruleset);

        Assert.NotEqual(0, loaded.State.Calendar.TurnIndex);

        // Re-serializing the migrated save and reading its own envelope's turnIndex back proves the
        // field actually reached the written form, not just an in-memory SaveGame the reader trusts.
        var resaved = SaveManager.Serialize(loaded);
        Assert.Contains($"\"turnIndex\": {loaded.State.Calendar.TurnIndex}", resaved, StringComparison.Ordinal);
        Assert.NotEqual(text, resaved); // the v1 file itself never carried this field
    }
}
