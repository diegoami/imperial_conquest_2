using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// T129 Done-when 5, the peace draw. The <c>Random(5)</c> after the promotions offers the treaty in <c>gate2_s2a</c>,
/// <c>hi-hi-one_s1</c> and <c>hi-hi-one_s3</c> and not in <c>gate2_a</c> or <c>hi-hi-one_s2</c>, as the reports observed; and
/// in each hooked series the port draws it exactly when the log has a record at the peace site (<c>0x45951C</c>), with
/// that record's value, and offers the treaty exactly when that value is below 2 (the gate the hook observed opening
/// the box in every case it logged, [Wine candidate: hook]). Whether the original's box opened in a given series is not
/// asserted here.
/// </summary>
public sealed class PeaceDrawTests
{
    /// <summary>The value below which the hook saw the box open ([Wine candidate: hook]).</summary>
    private const uint OfferedBelow = 2;

    [SkippableTheory]
    [InlineData("gate2_s2a", true)]
    [InlineData("hi-hi-one_s1_r1", true)]
    [InlineData("hi-hi-one_s3_r1", true)]
    [InlineData("gate2_a", false)]
    [InlineData("hi-hi-one_s2_r1", false)]
    public void The_treaty_is_offered_where_the_reports_observed_it(string name, bool offered)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var replay = GoldenReplay.Run(GoldenData.Named(name), compareBlocks: false);

        Assert.True(replay.Conclusion is not null, $"{name}: {replay.Structural}");
        KnownFailures.Check(nameof(The_treaty_is_offered_where_the_reports_observed_it) + ":" + name, () =>
        {
            KnownFailures.Require(offered == replay.Conclusion!.Result.PeaceOffered, $"{name}: peace offered: port {replay.Conclusion!.Result.PeaceOffered}, original {offered}");
        });
    }

    [SkippableTheory]
    [MemberData(nameof(Rows))]
    public void The_port_draws_the_peace_roll_when_the_original_did(string trial)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var t = GoldenData.Trial(trial);
        var log = HookLog.Load(t.HookLog);
        Assert.True(log.PairingError(t.Seed, t.Series.Files.Count) is null, $"FIXTURE ERROR in {trial}: its log does not pair with its series.");
        var original = log.PeaceDraw;

        var replay = GoldenReplay.Run(t.Series, compareBlocks: false);

        Assert.True(replay.Conclusion is not null, $"{trial}: {replay.Structural}");
        var conclusion = replay.Conclusion!.Result;
        KnownFailures.Check(nameof(The_port_draws_the_peace_roll_when_the_original_did) + ":" + trial, () =>
        {
            KnownFailures.Require(conclusion.PeaceDrawn == (original is not null),
                $"{trial}: the log {(original is null ? "has no" : "has a")} record at the peace site, the port {(conclusion.PeaceDrawn ? "drew" : "did not draw")} it");
            if (original is null)
            {
                KnownFailures.Require(!conclusion.PeaceOffered, $"{trial}: no draw, yet the treaty was offered");
                return;
            }

            var last = replay.Draws.Draws[^1];
            KnownFailures.Require(last.Bound == 5 && unchecked((uint)last.Value) == original.Result,
                $"{trial}: the port's peace draw is Random({last.Bound}) = {last.Value}, the log's is Random(5) = {original.Result}");
            KnownFailures.Require((original.Result < OfferedBelow) == conclusion.PeaceOffered, $"{trial}: peace offered: port {conclusion.PeaceOffered}, original {original.Result < OfferedBelow}");
        });

    }

    public static IEnumerable<object[]> Rows() => GoldenData.Hooked();
}
