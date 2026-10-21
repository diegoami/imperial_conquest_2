using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>The rename to "Open Imperial Conquest" moves user://; saves and settings follow, nothing is lost.</summary>
public sealed class UserDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-migration-" + Guid.NewGuid().ToString("N"));
    private string Old => Path.Combine(_root, UserDataMigration.FormerAppName);
    private string New => Path.Combine(_root, "Open Imperial Conquest");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Saves_and_settings_are_copied_and_the_old_folder_is_untouched()
    {
        Write(Path.Combine(Old, "saves", "a.json"), "save a");
        Write(Path.Combine(Old, "saves", "sub", "b.json"), "save b");
        Write(Path.Combine(Old, "settings.json"), "{}");
        Write(Path.Combine(Old, "logs", "diagnostics", "x.jsonl"), "log");

        var outcome = UserDataMigration.Run(Old, New);

        Assert.True(outcome.Ran);
        Assert.Equal(new[] { "settings.json", "saves/a.json", "saves/sub/b.json" }, outcome.CopiedFiles);
        Assert.Equal("save b", File.ReadAllText(Path.Combine(New, "saves", "sub", "b.json")));
        Assert.False(Directory.Exists(Path.Combine(New, "logs")));
        Assert.True(File.Exists(Path.Combine(Old, "saves", "a.json")));
        Assert.True(File.Exists(Path.Combine(New, UserDataMigration.MarkerFileName)));
    }

    [Fact]
    public void Nothing_already_in_the_new_folder_is_overwritten()
    {
        Write(Path.Combine(Old, "saves", "a.json"), "old");
        Write(Path.Combine(Old, "settings.json"), "old settings");
        Write(Path.Combine(New, "saves", "a.json"), "new");
        Write(Path.Combine(New, "settings.json"), "new settings");

        var outcome = UserDataMigration.Run(Old, New);

        Assert.Empty(outcome.CopiedFiles);
        Assert.Equal("new", File.ReadAllText(Path.Combine(New, "saves", "a.json")));
        Assert.Equal("new settings", File.ReadAllText(Path.Combine(New, "settings.json")));
    }

    [Fact]
    public void It_runs_once_so_a_save_deleted_later_is_not_copied_back()
    {
        Write(Path.Combine(Old, "saves", "a.json"), "save a");
        UserDataMigration.Run(Old, New);
        File.Delete(Path.Combine(New, "saves", "a.json"));

        var second = UserDataMigration.Run(Old, New);

        Assert.False(second.Ran);
        Assert.False(File.Exists(Path.Combine(New, "saves", "a.json")));
    }

    [Fact]
    public void A_first_install_with_no_old_folder_just_writes_the_marker()
    {
        var outcome = UserDataMigration.Run(Old, New);

        Assert.True(outcome.Ran);
        Assert.Empty(outcome.CopiedFiles);
        Assert.True(File.Exists(Path.Combine(New, UserDataMigration.MarkerFileName)));
    }

    [Fact]
    public void The_former_folder_is_the_new_ones_sibling() =>
        Assert.Equal(Old, UserDataMigration.FormerDirectoryBeside(New + Path.DirectorySeparatorChar));
}
