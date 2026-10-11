using System.Linq;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// Bug #921 items 3 and 4: the watch-mode notice words the actual reason, and the human turn-start fall
/// check runs when a save is loaded.
/// </summary>
public sealed class SeatFallTextsTests : IDisposable
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ic2-921-" + Guid.NewGuid().ToString("N"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    public SeatFallTextsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static GameSession IndebtedCarthage()
    {
        var world = Classical.World with
        {
            Nations = ValueList.From(Classical.World.Nations.Select(n =>
                string.Equals(n.Id, "carthage", StringComparison.Ordinal)
                    ? n with { Treasury = -10_000_000 }
                    : n)),
        };
        return new GameSession(
            world, Classical.Ruleset, Classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
    }

    [Fact]
    public void The_watch_mode_notice_for_an_unpaid_army_does_not_say_deposed_for_every_reason()
    {
        var session = IndebtedCarthage();
        var lines = session.Submit("end").Lines.Concat(session.Submit("status").Lines).ToList();

        Assert.Contains(lines, l => l.Contains("Your army have deposed you", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("has been deposed and handed to the AI", StringComparison.Ordinal));
    }

    [Fact]
    public void The_watch_mode_notice_names_the_year_limit()
    {
        var classical = Classical;
        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        var calendar = session.State.Calendar with { YearBc = classical.Ruleset.Victory.HardEndYearBc };
        typeof(GameSession).GetProperty(nameof(GameSession.State))!
            .SetValue(session, session.State with { Calendar = calendar });

        var lines = session.Submit("end").Lines;

        Assert.Contains(lines, l => l.Contains("end of your allotted 20 years", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("has been deposed and handed to the AI", StringComparison.Ordinal));
    }

    [Fact]
    public void Loading_a_save_with_an_indebted_human_seat_deposes_it_at_once()
    {
        var classical = Classical;
        var original = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        var indebted = original.State with
        {
            Nations = ValueList.From(original.State.Nations.Select(n =>
                string.Equals(n.Id, "carthage", StringComparison.Ordinal)
                    ? n with { Treasury = -10_000_000 }
                    : n)),
        };
        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(original, indebted);
        var path = Path.Combine(_directory, "debt.json");
        original.Submit($"save {path}");

        var other = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, seedOverride: null, humanSeatNationId: "carthage");
        var output = other.Submit($"load {path}");

        Assert.Equal(SeatControl.Ai, other.State.NationById("carthage")!.Control);
        Assert.Contains(output.Lines, l => l.Contains("Your army have deposed you", StringComparison.Ordinal));
        Assert.Single(output.SeatFalls);
    }
}
