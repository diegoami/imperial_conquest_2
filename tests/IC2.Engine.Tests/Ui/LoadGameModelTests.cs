using System.Buffers.Binary;
using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>T165 (#1021) Done-when 1, 3 and 4: <see cref="LoadGameModel"/> offers and opens .json and .sav files.</summary>
public class LoadGameModelTests : IDisposable
{
    private const string RomeSave = "1_rome_270_winter_7.sav";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ic2-t165-" + Guid.NewGuid().ToString("N"));

    public LoadGameModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static GameDataRepository Repo() =>
        GameDataRepository.Load(Path.Combine(LocalOriginalAssets.RepositoryRoot, "data"));

    private string Write(string name, byte[] bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void Done_when_1_browse_offers_json_and_sav_only()
    {
        Write("a.json", new byte[] { 1 });
        Write("b.SAV", new byte[] { 1 });
        Write("c.txt", new byte[] { 1 });
        Write("d.dat", new byte[] { 1 });
        Write("e.sav.bak", new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(_dir, "sub.sav"));

        var names = LoadGameModel.ListOpenable(_dir).Select(Path.GetFileName).ToList();

        Assert.Equal(new[] { "a.json", "b.SAV" }, names);
        Assert.Empty(LoadGameModel.ListOpenable(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void Native_rows_show_actual_filenames_and_sort_newest_first_with_filename_ties()
    {
        var data = Repo().Resolve("classical-mediterranean");
        var source = new GameSession(data.World, data.Ruleset, data.Scenario);
        var old = Path.Combine(_dir, "a-old.json");
        source.Submit($"save {old}");
        // Stored metadata is deliberately unrelated to the on-disk filenames.
        Assert.DoesNotContain("a-old.json", SaveManager.PeekSummaryFile(old).Label);
        var newer = Path.Combine(_dir, "z-new.json");
        var tied = Path.Combine(_dir, "b-tie.json");
        File.Copy(old, newer);
        File.Copy(old, tied);
        var time = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(old, time.AddDays(-1));
        File.SetLastWriteTimeUtc(newer, time);
        File.SetLastWriteTimeUtc(tied, time);
        var original = Write("original.SAV", new byte[] { 1, 2, 3 });
        File.SetLastWriteTimeUtc(original, time.AddHours(-12));

        var rows = LoadGameModel.ListNativeSaves(_dir);

        Assert.Equal(new[] { tied, newer, original, old }, rows.Select(row => row.Path));
        foreach (var row in rows) Assert.StartsWith(Path.GetFileName(row.Path) + " — ", row.Text);
        Assert.Equal("original.SAV — original save", rows[2].Text);
        Assert.True(LoadGameModel.Open(rows[1].Path, Repo()).Succeeded);
    }

    [Fact]
    public void Native_row_primary_text_uses_filename_instead_of_stored_label()
    {
        var data = Repo().Resolve("classical-mediterranean");
        var session = new GameSession(data.World, data.Ruleset, data.Scenario);
        var path = Path.Combine(_dir, "my-actual-save.JSON");
        session.Submit($"save {path}");
        var row = Assert.Single(LoadGameModel.ListNativeSaves(_dir));
        Assert.StartsWith("my-actual-save.JSON — ", row.Text);
        Assert.Equal(path, row.Path);
    }

    [Fact]
    public void Native_rows_keep_malformed_files_identifiable_and_disappearing_files_fail_safely()
    {
        var path = Write("broken.json", new byte[] { 1, 2, 3 });
        var original = Write("original.sav", new byte[] { 1 });
        var rows = LoadGameModel.ListNativeSaves(_dir);
        Assert.Equal(2, rows.Count);
        var row = Assert.Single(rows, row => row.Path == path);
        Assert.Equal(path, row.Path);
        Assert.StartsWith("broken.json — could not be read", row.Text);
        Assert.Equal("original.sav — original save", Assert.Single(rows, row => row.Path == original).Text);
        File.Delete(path);
        AssertRefused(LoadGameModel.Open(row.Path, Repo()), "broken.json");
        AssertRefused(LoadGameModel.Open(original, Repo()), "original.sav");
        Assert.Empty(LoadGameModel.ListNativeSaves(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void Last_browsed_folder_is_remembered_and_listed_without_scanning_subfolders()
    {
        var saves = Path.Combine(_dir, "saves");
        var recent = Path.Combine(_dir, "recent");
        Directory.CreateDirectory(saves);
        Directory.CreateDirectory(recent);
        var native = Path.Combine(saves, "native.json");
        var original = Path.Combine(recent, "battle.SAV");
        File.WriteAllBytes(native, new byte[] { 1 });
        File.WriteAllBytes(original, new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(recent, "nested"));
        File.WriteAllBytes(Path.Combine(recent, "nested", "hidden.sav"), new byte[] { 1 });
        var preference = Path.Combine(_dir, "last-folder.txt");

        LoadGameModel.RememberRecentFolder(preference, recent);
        Assert.Equal(recent, LoadGameModel.ReadRecentFolder(preference));
        var rows = LoadGameModel.ListNativeSaves(saves, LoadGameModel.ReadRecentFolder(preference));
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Path == native);
        Assert.Contains(rows, row => row.Path == original && row.Text.Contains("original save · recent"));
        Assert.Single(LoadGameModel.ListNativeSaves(recent, recent));

        Directory.Delete(recent, recursive: true);
        Assert.Null(LoadGameModel.ReadRecentFolder(preference));
        Assert.Single(LoadGameModel.ListNativeSaves(saves, LoadGameModel.ReadRecentFolder(preference)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HumanBattleMode.Instant)]
    [InlineData(HumanBattleMode.Tactical)]
    public void Native_save_opens_with_requested_or_default_mode(HumanBattleMode? mode)
    {
        var repo = Repo();
        var data = repo.Resolve("classical-mediterranean");
        var source = new GameSession(data.World, data.Ruleset, data.Scenario);
        var path = Path.Combine(_dir, "native.json");
        source.Submit($"save {path}");
        Assert.True(File.Exists(path));

        var outcome = mode is { } requested
            ? LoadGameModel.Open(path, repo, requested)
            : LoadGameModel.Open(path, repo);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(mode ?? HumanBattleMode.Instant, outcome.Session!.HumanBattles);
        Assert.Equal(source.State, outcome.Session.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HumanBattleMode.Instant)]
    [InlineData(HumanBattleMode.Tactical)]
    public void Native_mid_battle_save_reopens_the_battle_with_requested_or_default_mode(HumanBattleMode? mode)
    {
        var source = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var opened = source.Submit("attack-army north-army-1 south-army-1").OpenBattleView;
        Assert.NotNull(opened);
        var path = Path.Combine(_dir, "mid-battle.json");
        source.Submit($"save {path}");
        Assert.True(File.Exists(path));

        var outcome = mode is { } requested
            ? LoadGameModel.Open(path, Repo(), requested)
            : LoadGameModel.Open(path, Repo());

        Assert.True(outcome.Succeeded, outcome.Message);
        var session = outcome.Session!;
        Assert.Equal(mode ?? HumanBattleMode.Instant, session.HumanBattles);
        var repo = Repo();
        var data = repo.Resolve(source.Scenario.Id);
        var saved = SaveManager.LoadFile(path, data.World, data.Ruleset);
        Assert.NotNull(saved.Battle);
        // Compare against the engine's resume semantics: classical reload may advance placement.
        var expected = new GameSession(data.World, data.Ruleset, data.Scenario, saved);
        var before = expected.Submit("battle show").OpenBattleView!;
        var reopened = session.Submit("battle show").OpenBattleView;
        Assert.NotNull(reopened);
        Assert.Equal(before.AttackerArmyId, reopened.AttackerArmyId);
        Assert.Equal(before.DefenderArmyId, reopened.DefenderArmyId);
        Assert.Equal(before.Counter, reopened.Counter);
        Assert.Equal(before.SideToMove, reopened.SideToMove);
        Assert.Equal(before.Slots, reopened.Slots);
        Assert.Equal(before.Grid, reopened.Grid);
    }

    [SkippableTheory]
    [InlineData(null)]
    [InlineData(HumanBattleMode.Instant)]
    [InlineData(HumanBattleMode.Tactical)]
    public void Original_save_opens_with_requested_or_default_mode(HumanBattleMode? mode)
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var path = OriginalFixture.ResolveOrThrow(RomeSave);
        var repo = Repo();

        var outcome = mode is { } requested
            ? LoadGameModel.Open(path, repo, requested)
            : LoadGameModel.Open(path, repo);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(mode ?? HumanBattleMode.Instant, outcome.Session!.HumanBattles);
        var expected = OriginalSaveImporter.Import(
            File.ReadAllBytes(path), RomeSave, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "x", "x").Save.State;
        Assert.Equal(expected, outcome.Session.State);
    }

    [Theory]
    [InlineData("missing.json", false)]
    [InlineData("missing.sav", false)]
    [InlineData("malformed.json", true)]
    [InlineData("malformed.sav", true)]
    public void Requested_mode_preserves_missing_and_malformed_file_feedback(string name, bool exists)
    {
        var path = exists ? Write(name, new byte[] { 1, 2, 3 }) : Path.Combine(_dir, name);

        AssertRefused(LoadGameModel.Open(path, Repo(), HumanBattleMode.Tactical), name);
    }

    [SkippableFact]
    public void Done_when_3_original_save_opens_and_a_turn_ends()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var path = OriginalFixture.ResolveOrThrow(RomeSave);
        var repo = Repo();

        var outcome = LoadGameModel.Open(path, repo);

        Assert.True(outcome.Succeeded, outcome.Message);
        var session = outcome.Session!;
        var expected = OriginalSaveImporter.Import(
            File.ReadAllBytes(path), RomeSave, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario, "x", "x").Save.State;
        var active = session.State.NationById(session.State.ActiveNationId)!;
        Assert.Equal(SeatControl.Human, active.Control);
        Assert.Equal(expected.ActiveNationId, session.State.ActiveNationId);
        Assert.Equal(expected.Calendar.TurnIndex, session.State.Calendar.TurnIndex);

        var beforeTurn = session.State.Calendar.TurnIndex;
        var beforeSeat = session.State.ActiveNationId;
        session.Submit("end");
        Assert.True(
            session.State.Calendar.TurnIndex != beforeTurn || session.State.ActiveNationId != beforeSeat,
            "end of turn did not advance the imported session");
    }

    [SkippableFact]
    public void Done_when_4_truncated_sav_gives_a_message()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var bytes = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(RomeSave));
        var path = Write("truncated.sav", bytes.AsSpan(0, bytes.Length / 2).ToArray());

        AssertRefused(LoadGameModel.Open(path, Repo()), "truncated.sav");
    }

    [SkippableFact]
    public void Done_when_4_sav_with_no_human_seat_gives_a_message()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var bytes = File.ReadAllBytes(OriginalFixture.ResolveOrThrow(RomeSave));

        // Mirrors SaveNationLayout.Locate (internal to IC2.Data): prefix 100,956 bytes, army count + 656-byte
        // army records, fleet count + 26-byte fleet records, then 16 nation records of 1,172 bytes; the
        // human flag is the word at +0x490 of each record (SaveNationTable.ParseSav).
        const int prefix = 100956;
        var armies = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(prefix, 2));
        var fleetCountAt = prefix + 2 + armies * 656;
        var fleets = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(fleetCountAt, 2));
        var start = fleetCountAt + 2 + fleets * 26;
        Assert.Contains(SaveNationTable.Parse(bytes).Nations, n => n.HumanPlayer);
        for (var i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start + i * 1172 + 0x490, 2), 0);
        }

        Assert.DoesNotContain(SaveNationTable.Parse(bytes).Nations, n => n.HumanPlayer);
        var path = Write("nohuman.sav", bytes);

        AssertRefused(LoadGameModel.Open(path, Repo()), "nohuman.sav");
    }

    [SkippableFact]
    public void Done_when_4_json_naming_an_unknown_world_gives_a_message()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var repo = Repo();
        var scenario = repo.ScenarioById("classical-mediterranean")!;
        var state = OriginalSaveImporter.Import(
            File.ReadAllBytes(OriginalFixture.ResolveOrThrow(RomeSave)), RomeSave,
            RealGameData.World, RealGameData.Ruleset, scenario, "s", "s").Save;
        var good = Path.Combine(_dir, "good.json");
        SaveManager.WriteFile(good, state);
        var path = Path.Combine(_dir, "unknown-world.json");
        File.WriteAllText(path, File.ReadAllText(good).Replace("\"classical-mediterranean\"", "\"atlantis\""));

        AssertRefused(LoadGameModel.Open(path, repo), "unknown-world.json");
        Assert.True(LoadGameModel.Open(good, repo).Succeeded);
    }

    private static void AssertRefused(LoadGameModel.Outcome outcome, string fileName)
    {
        Assert.Null(outcome.Session);
        Assert.False(outcome.Succeeded);
        Assert.Contains(fileName, outcome.Message);
    }
}
