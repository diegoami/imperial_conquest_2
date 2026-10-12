using System.Buffers.Binary;
using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
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
