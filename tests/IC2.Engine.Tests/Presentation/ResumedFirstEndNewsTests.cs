using System.Text.Json;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// T137 (#707; #487 item 1): a resumed session's first <c>end</c> shows the same News as an uninterrupted
/// one's, because the save carries <see cref="SaveGame.FirstEndNewsOwed"/> while the first <c>end</c> is pending.
/// </summary>
public sealed class ResumedFirstEndNewsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ic2-resumed-first-end-tests", Path.GetRandomFileName());

    public ResumedFirstEndNewsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(TestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static readonly Lazy<ResolvedScenario> LazyToy = new(
        () => GameDataRepository.Load(TestPaths.DataRoot).Resolve("toy-3city"));

    public static IEnumerable<object[]> ClassicalSeats() =>
        LazyClassical.Value.Scenario.Seats.Select(s => new object[] { s.Nation });


    private const ulong Seed = 20260928;

    private static GameSession Build(ResolvedScenario r, string seat) =>
        new(r.World, r.Ruleset, r.Scenario, seedOverride: Seed, humanSeatNationId: seat);

    // The first Submit of an uninterrupted session also flushes the construction-time prelude before the
    // echo line; the comparison is of what follows the "> end" echo, as the task's Done-when 1 says.
    private static IReadOnlyList<string> AfterEcho(IReadOnlyList<string> lines)
    {
        var echo = lines.ToList().LastIndexOf("> end");
        Assert.True(echo >= 0, "no echo line");
        return lines.Skip(echo + 1).ToList();
    }

    private string Save(GameSession session, string name)
    {
        var path = Path.Combine(_directory, name);
        var output = session.Submit($"save {path}");
        Assert.Contains(output.Lines, l => l.StartsWith("Saved to", StringComparison.Ordinal));
        return path;
    }

    private static GameSession Resume(ResolvedScenario r, string path)
    {
        var save = SaveManager.LoadFile(path, r.World, r.Ruleset);
        return new GameSession(r.World, r.Ruleset, r.Scenario, save);
    }

    [Fact]
    public void The_classical_scenario_seats_sixteen_nations()
    {
        Assert.Equal(16, LazyClassical.Value.Scenario.Seats.Count);
    }

    [Theory]
    [MemberData(nameof(ClassicalSeats))]
    public void A_resumed_first_end_equals_the_uninterrupted_first_end_for_every_classical_seat(string seat)
    {
        var classical = LazyClassical.Value;

        var uninterrupted = AfterEcho(Build(classical, seat).Submit("end").Lines);

        var saver = Build(classical, seat);
        var path = Save(saver, $"{seat}.json");
        var resumed = AfterEcho(Resume(classical, path).Submit("end").Lines);

        Assert.Equal(uninterrupted, resumed);
    }

    [Fact]
    public void With_an_order_first_the_resumed_first_end_prints_the_declaration_exactly_when_the_uninterrupted_one_does()
    {
        var toy = LazyToy.Value;

        var a = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        a.Submit("declare-war south");
        var uninterrupted = AfterEcho(a.Submit("end").Lines);

        var b = new GameSession(toy.World, toy.Ruleset, toy.Scenario);
        b.Submit("declare-war south");
        var path = Save(b, "toy-order.json");
        var resumed = AfterEcho(Resume(toy, path).Submit("end").Lines);

        Assert.Equal(
            uninterrupted.Any(l => l.Contains("declare-war", StringComparison.OrdinalIgnoreCase)),
            resumed.Any(l => l.Contains("declare-war", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(uninterrupted, resumed);
    }

    [Fact]
    public void A_save_made_after_the_first_end_has_no_owed_key_and_the_next_end_matches()
    {
        var classical = LazyClassical.Value;
        var seat = classical.Scenario.Seats[0].Nation;

        var a = Build(classical, seat);
        a.Submit("end");
        var uninterrupted = AfterEcho(a.Submit("end").Lines);

        var b = Build(classical, seat);
        b.Submit("end");
        var path = Save(b, "after-first-end.json");

        using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Assert.False(doc.RootElement.GetProperty(SaveFormat.PayloadField).TryGetProperty("firstEndNewsOwed", out _));
        }

        Assert.Equal(uninterrupted, AfterEcho(Resume(classical, path).Submit("end").Lines));
    }

    [Fact]
    public void A_save_made_before_the_first_end_writes_the_owed_count()
    {
        var classical = LazyClassical.Value;
        var seat = classical.Scenario.Seats.Select(s => s.Nation).First(n => n.Contains("ptolem", StringComparison.OrdinalIgnoreCase));
        var path = Save(Build(classical, seat), "owed.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty(SaveFormat.PayloadField).TryGetProperty("firstEndNewsOwed", out var owed));
        Assert.True(owed.GetInt32() > 0);
    }
}
