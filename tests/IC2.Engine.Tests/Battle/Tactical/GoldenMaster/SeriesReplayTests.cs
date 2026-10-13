using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// T129 Done-when 2: every listed series replays exactly. For each series the port, seeded with the original's
/// generator, plays copy-in, the computer general and the half-rounds, and its block equals the file's at each point
/// where the lab wrote a save (all 9 header bytes, 40 slots and 168 cells); at the end the post-battle fields equal the
/// post save's. A failing file is reported with the first differing field. Local-only: skips without the fixtures.
/// </summary>
public sealed class SeriesReplayTests
{
    [SkippableTheory]
    [MemberData(nameof(FixedRows))]
    public void The_recorded_series_replays_exactly(string name)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = GoldenData.Named(name);
        Assert.Equal(GoldenData.FixedFileCounts[name], spec.Files.Count);

        var outcome = GoldenReplay.Run(spec);

        Assert.True(outcome.Passed, outcome.Describe(spec));
    }

    [SkippableTheory]
    [MemberData(nameof(HookedRows))]
    public void The_hooked_series_replays_exactly(string trial)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = GoldenData.Trial(trial).Series;

        var outcome = GoldenReplay.Run(spec);

        Assert.True(outcome.Passed, outcome.Describe(spec));
    }

    public static IEnumerable<object[]> FixedRows() => GoldenData.Fixed();

    public static IEnumerable<object[]> HookedRows() => GoldenData.Hooked();
}
