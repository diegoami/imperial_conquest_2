using System.Text.RegularExpressions;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// The two properties every Done-when line above rests on: the resolver is reproducible under a fixed
/// seed, and it draws from <see cref="IRng"/> alone — plus the hazard guard that keeps the reserve
/// tactical research out of this diff.
/// </summary>
public class BattleDeterminismTests
{
    private const string Attacker = "north-attacker";
    private const string Defender = "south-defender";

    /// <summary>The same seed resolves the same battle identically, result and state alike.</summary>
    [Fact]
    public void TheSameSeedProducesTheSameBattleTwice()
    {
        var first = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            new SplitMix64Rng(BattleTestbed.Seed), NullEventSink.Instance);

        var second = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            new SplitMix64Rng(BattleTestbed.Seed), NullEventSink.Instance);

        Assert.Equal(first.Result, second.Result);
        Assert.Equal(GameJson.Serialize(first.State), GameJson.Serialize(second.State));
    }

    /// <summary>
    /// The companion: a different seed moves the seeded outcomes, so the reproducibility above is the
    /// generator doing its job rather than the resolver having no randomness in it at all.
    /// </summary>
    [Fact]
    public void ADifferentSeedMovesTheSeededOutcomes()
    {
        var promotionPatterns = new List<string>();
        var distances = new List<int>();

        for (ulong seed = 1; seed <= 24; seed++)
        {
            var (_, result) = InstantBattleResolver.ResolveField(
                FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
                new SplitMix64Rng(seed), NullEventSink.Instance);

            promotionPatterns.Add(string.Concat(result.Promotions.Select(p => p.PromotedByRoll ? "1" : "0")));
            if (result.Scatter is { } scatter)
            {
                distances.Add(scatter.RequestedDistance);
            }
        }

        Assert.True(promotionPatterns.Distinct().Count() > 1, "the promotion roll must depend on the seed");
        Assert.True(distances.Distinct().Count() > 1, "the scatter distance must depend on the seed");
    }

    /// <summary>
    /// Both rulesets consume the same draws for everything the original itself does, which is what makes
    /// "unaffected by <c>combat.onDefeat</c>" true of the winner's side of the battle rather than
    /// coincidentally true of one fixture.
    /// </summary>
    [Fact]
    public void TheTwoRulesetsAgreeOnEverythingTheOriginalDoes()
    {
        var (_, destroyed) = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Destroyed, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        var (_, scattered) = InstantBattleResolver.ResolveField(
            FieldBattleTests.Fixture(), Attacker, Defender, BattleTestbed.Scatter, BattleTestbed.World,
            BattleTestbed.BattleRng(), NullEventSink.Instance);

        Assert.Equal(destroyed.AttackerPower, scattered.AttackerPower);
        Assert.Equal(destroyed.DefenderPower, scattered.DefenderPower);
        Assert.Equal(destroyed.Winner, scattered.Winner);
        Assert.Equal(destroyed.WinnerCasualties, scattered.WinnerCasualties);
        Assert.Equal(destroyed.UnitCasualties, scattered.UnitCasualties);
        Assert.Equal(destroyed.Promotions, scattered.Promotions);
        Assert.Equal(destroyed.AbsorbedMoney, scattered.AbsorbedMoney);
        Assert.Equal(destroyed.AbsorbedSupplyTons, scattered.AbsorbedSupplyTons);
        Assert.Equal(destroyed.WinnerUnityDelta, scattered.WinnerUnityDelta);
        Assert.Equal(destroyed.LoserUnityDelta, scattered.LoserUnityDelta);
        Assert.Equal(destroyed.PeaceTreatyFired, scattered.PeaceTreatyFired);

        // Only the loser's fate differs.
        Assert.NotEqual(destroyed.LoserFate, scattered.LoserFate);
    }

    /// <summary>
    /// The hazard guard (<c>docs/task-catalogue.md</c> T16 Hazards): none of the reserve tactical
    /// research appears in this namespace's <em>code</em>. Comments are stripped first, because the files
    /// deliberately name these things in prose to record that they are absent — which is the opposite of
    /// using them, and must not trip the check.
    /// </summary>
    [Fact]
    public void NoReserveTacticalResearchAppearsInTheBattleNamespacesCode()
    {
        string[] reserved =
        {
            // The reserve's whole ruleset surface, plus the rout mechanic's own threshold field.
            "DetailedResolver",
            "TypeEffectiveness",
            "MeleeLossCap",
            "MeleeBasePowerFloor",
            "MeleePowerDivisor",
            "InRangeShotMultiplier",
            "StandardBattalionSize",
        };

        var battleSources = Directory.GetFiles(
            Path.Combine(TestPaths.RepositoryRoot, "src", "IC2.Engine", "Battle"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(battleSources);

        var offenders = new List<string>();
        foreach (var file in battleSources)
        {
            var code = StripComments(File.ReadAllText(file));
            foreach (var name in reserved)
            {
                if (code.Contains(name, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The shipped instant resolver must not consume the reserve tactical research: "
            + string.Join(", ", offenders));
    }

    /// <summary>The guard's own regression case: it does see a reserve name when one is really in code.</summary>
    [Fact]
    public void TheReserveGuardDetectsARealUse()
    {
        const string Sample = """
            /// <summary>TypeEffectiveness is deliberately absent.</summary>
            public static int Broken(Ruleset r) => r.Combat.DetailedResolver.MeleeLossCapPercent;
            """;

        var code = StripComments(Sample);
        Assert.DoesNotContain("TypeEffectiveness", code, StringComparison.Ordinal);
        Assert.Contains("DetailedResolver", code, StringComparison.Ordinal);
    }

    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//.*?$", string.Empty, RegexOptions.Multiline);
    }
}
