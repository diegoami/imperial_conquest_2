using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// T129 Done-when 7: for every hooked trial (the B5 grid, the size matrix and <c>mix-rg</c>) the port's draws through
/// its seam equal the log's <c>Random</c> records one for one, in order, range and result, from the first copy-in draw to
/// the log's last record (the peace draw, or the last promotion draw), with none extra and none missing. The replay
/// that feeds this test compares no block, so a series' draws are read before its blocks are. A log whose pairing
/// check fails is a fixture error, reported as such with the trial, never a port failure.
/// </summary>
public sealed class DrawLogTests
{
    [SkippableTheory]
    [MemberData(nameof(Rows))]
    public void The_log_belongs_to_the_series(string trial)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var t = GoldenData.Trial(trial);
        var log = HookLog.Load(t.HookLog);

        var error = log.PairingError(t.Seed, t.Series.Files.Count);

        Assert.True(error is null, $"FIXTURE ERROR in {trial}: {error}");
    }

    [SkippableTheory]
    [MemberData(nameof(Rows))]
    public void The_port_draws_what_the_original_drew(string trial)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var t = GoldenData.Trial(trial);
        var log = HookLog.Load(t.HookLog);
        Assert.True(log.PairingError(t.Seed, t.Series.Files.Count) is null, $"FIXTURE ERROR in {trial}: its log does not pair with its series.");

        var replay = GoldenReplay.Run(t.Series, compareBlocks: false);

        Assert.True(replay.Structural is null, $"{trial}: {replay.Structural}");
        var difference = DrawComparison.First(replay.Draws.Draws, log);
        Assert.True(difference is null, $"{trial}: {difference}");
    }

    public static IEnumerable<object[]> Rows() => GoldenData.Hooked();
}
