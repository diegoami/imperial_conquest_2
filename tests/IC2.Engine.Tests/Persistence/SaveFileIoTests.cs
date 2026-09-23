using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// <see cref="SaveManager.WriteFile"/>/<see cref="SaveManager.LoadFile"/>/<see cref="SaveManager.PeekSummaryFile"/>
/// against a real file on disk — review round 1 (N3) found none of the three was actually exercised
/// through the file system; every other test goes through <see cref="SaveManager.Serialize"/> and
/// <see cref="SaveManager.Load"/> directly on an in-memory string.
/// </summary>
public sealed class SaveFileIoTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ic2-persistence-tests", Path.GetRandomFileName());

    public SaveFileIoTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leaked temp directory is a nuisance, not a test failure.
        }
    }

    private static SaveGame BuildToySave(string id, string label)
    {
        var state = PersistenceTestbed.PlayTurns(6);
        return new SaveGame(
            SchemaVersion: state.SchemaVersion,
            Id: id,
            Label: label,
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);
    }

    [Fact]
    public void WriteFile_then_LoadFile_round_trips_through_the_real_file_system()
    {
        var toy = PersistenceTestbed.Toy;
        var path = Path.Combine(_directory, "toy-3city.ic2save.json");

        // A label with a quote and a non-ASCII character, so the round trip also proves JSON escaping
        // survives an actual file write/read, not just an in-memory JsonSerializer call.
        var save = BuildToySave("toy-3city-io", "Diego's game — été campaign");

        SaveManager.WriteFile(path, save);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp")); // no leftover temp file on a successful write

        var loaded = SaveManager.LoadFile(path, toy.World, toy.Ruleset);

        Assert.Equal(save.Label, loaded.Label);
        Assert.Equal(GameStateHash.Compute(save.State), GameStateHash.Compute(loaded.State));
        Assert.Equal(save.State, loaded.State);
    }

    [Fact]
    public void WriteFile_then_PeekSummaryFile_reads_the_same_file_without_a_world_or_ruleset()
    {
        var path = Path.Combine(_directory, "toy-3city-summary.ic2save.json");
        var save = BuildToySave("toy-3city-io-summary", "Summary probe");

        SaveManager.WriteFile(path, save);
        var summary = SaveManager.PeekSummaryFile(path);

        Assert.Equal(save.Id, summary.Id);
        Assert.Equal(save.Label, summary.Label);
        Assert.Equal(save.State.Calendar.TurnIndex, summary.TurnIndex);
    }

    [Fact]
    public void WriteFile_overwrites_a_previous_save_at_the_same_path_and_leaves_no_temp_file_behind()
    {
        var toy = PersistenceTestbed.Toy;
        var path = Path.Combine(_directory, "toy-3city-overwrite.ic2save.json");

        SaveManager.WriteFile(path, BuildToySave("toy-3city-first", "First save"));
        SaveManager.WriteFile(path, BuildToySave("toy-3city-second", "Second save"));

        var loaded = SaveManager.LoadFile(path, toy.World, toy.Ruleset);
        Assert.Equal("toy-3city-second", loaded.Id);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteFile_to_a_directory_that_does_not_exist_throws_a_typed_error_not_a_raw_IO_exception()
    {
        var path = Path.Combine(_directory, "no-such-subdirectory", "toy-3city.ic2save.json");
        var save = BuildToySave("toy-3city-io-missing-dir", "Missing directory probe");

        var ex = Assert.Throws<SaveWriteException>(() => SaveManager.WriteFile(path, save));
        Assert.Contains("could not be written", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteFile_when_the_temp_path_is_blocked_leaves_the_previous_save_untouched()
    {
        // Review round 2 (R1): the temp-then-rename behaviour and its "the previous save is never
        // opened for writing at all" claim had no test that could tell it apart from a direct
        // File.WriteAllText(path, ...) implementation -- every existing test's write either fully
        // succeeds or fails before any file exists at path at all. This puts a real obstacle at
        // exactly the temp path the real implementation writes to first: a directory can't be opened
        // for writing text, so the real code fails there, before path is ever touched. A reverted,
        // direct-write implementation ignores the obstacle entirely (it never looks at path + ".tmp")
        // and would silently overwrite the previous save with no error at all -- caught here by both
        // the typed-exception assertion and the unchanged-bytes assertion.
        var toy = PersistenceTestbed.Toy;
        var path = Path.Combine(_directory, "toy-3city-blocked-temp.ic2save.json");

        SaveManager.WriteFile(path, BuildToySave("toy-3city-first", "First save"));
        var previousBytes = File.ReadAllBytes(path);

        var tempPath = path + ".tmp";
        Directory.CreateDirectory(tempPath);
        try
        {
            var secondSave = BuildToySave("toy-3city-second", "Second save");
            Assert.Throws<SaveWriteException>(() => SaveManager.WriteFile(path, secondSave));

            Assert.Equal(previousBytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void WriteFile_when_the_rename_fails_leaves_no_temp_file_behind()
    {
        // Review round 2 (R1): the cleanup that deletes a leftover .tmp file after a failed rename had
        // no test either -- every existing test's write either succeeds (renaming the temp file away)
        // or fails before a temp file could exist (a missing directory). This locks the target
        // exclusively so the temp file is written successfully and only the rename fails, which is the
        // one point a leaked .tmp file is actually possible; deleting the cleanup call would leave one
        // behind here.
        var path = Path.Combine(_directory, "toy-3city-locked-target.ic2save.json");
        SaveManager.WriteFile(path, BuildToySave("toy-3city-first", "First save"));

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var secondSave = BuildToySave("toy-3city-second", "Second save");
            Assert.Throws<SaveWriteException>(() => SaveManager.WriteFile(path, secondSave));
            Assert.False(File.Exists(path + ".tmp"));
        }
    }

    [Fact]
    public void LoadFile_of_a_path_that_does_not_exist_throws_a_typed_error_not_a_raw_IO_exception()
    {
        var toy = PersistenceTestbed.Toy;
        var path = Path.Combine(_directory, "does-not-exist.ic2save.json");

        Assert.Throws<MalformedGameDataException>(() => SaveManager.LoadFile(path, toy.World, toy.Ruleset));
    }

    [Fact]
    public void PeekSummaryFile_of_a_path_that_does_not_exist_throws_a_typed_error_not_a_raw_IO_exception()
    {
        var path = Path.Combine(_directory, "does-not-exist.ic2save.json");

        Assert.Throws<MalformedGameDataException>(() => SaveManager.PeekSummaryFile(path));
    }
}
