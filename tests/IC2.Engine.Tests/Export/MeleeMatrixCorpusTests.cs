using IC2.Engine.Serialization;
using IC2.Engine.Tests.Fixtures;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// T120 Done-when 3: pins the 25 melee-matrix cells the original's code reads from DAT <c>0x1F7A6</c>
/// (2026-10-04-decompiled-tactical-battle-rules.md §5), and checks that the fixtures corpus and all
/// three shipped rulesets agree with them cell for cell, in the report's orientation. This is the
/// guard bug #647 asked for: the corpus previously carried two AI tables read from DAT
/// <c>0x1F3B8</c>, so every cell differs from this literal and the test fails on <c>main</c>'s values.
/// </summary>
public class MeleeMatrixCorpusTests
{
    /// <summary>
    /// The report's §5 table copied cell for cell: <c>value[attacker][defender]</c>, rows and columns
    /// in <c>combat.detailedResolver.typeEffectivenessOrder</c> (LI, HI, Ar, LC, HC):
    /// <code>
    ///         LI   HI   Ar   LC   HC
    /// LI      15    4   20    5    3
    /// HI      60    5   65   15    8
    /// Ar      10    3   18    5    3
    /// LC      25    8   28   15    8
    /// HC      18   12   20   12    8
    /// </code>
    /// </summary>
    private static readonly int[][] ReportMatrix =
    {
        new[] { 15, 4, 20, 5, 3 },
        new[] { 60, 5, 65, 15, 8 },
        new[] { 10, 3, 18, 5, 3 },
        new[] { 25, 8, 28, 15, 8 },
        new[] { 18, 12, 20, 12, 8 },
    };

    private static readonly string[] Order =
    {
        "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry",
    };

    /// <summary>The corpus's own id segment for each index, in the same order as <see cref="Order"/>.</summary>
    private static readonly string[] CorpusIdSegment =
    {
        "lightInfantry", "heavyInfantry", "archers", "lightCavalry", "heavyCavalry",
    };

    private const string ReportSource = "2026-10-04-decompiled-tactical-battle-rules.md";

    private static readonly Lazy<GameDataRepository> LazyRepository = new(() => GameDataRepository.Load(TestPaths.DataRoot));

    private static GameDataRepository Repository => LazyRepository.Value;

    [Fact]
    public void The_corpus_carries_the_reports_25_values_with_its_source_and_tag()
    {
        for (var i = 0; i < Order.Length; i++)
        {
            for (var j = 0; j < Order.Length; j++)
            {
                var entry = FixtureCorpus.Get($"matrix.{CorpusIdSegment[i]}.vs.{CorpusIdSegment[j]}");
                Assert.Equal(ReportMatrix[i][j], entry.AsInt());
                Assert.Equal(ReportSource, entry.Source);
                Assert.Equal("confirmed", entry.Tag);
            }
        }
    }

    [Theory]
    [InlineData("toy-ruleset")]
    [InlineData("improved")]
    [InlineData("classical-faithful")]
    public void Every_shipped_ruleset_carries_the_reports_matrix(string rulesetId)
    {
        var detailed = Repository.RulesetById(rulesetId)!.Combat.DetailedResolver;

        Assert.Equal(Order, detailed.TypeEffectivenessOrder.ToArray());
        for (var i = 0; i < Order.Length; i++)
        {
            for (var j = 0; j < Order.Length; j++)
            {
                var corpusValue = FixtureCorpus.Get($"matrix.{CorpusIdSegment[i]}.vs.{CorpusIdSegment[j]}").AsInt();
                Assert.Equal(ReportMatrix[i][j], corpusValue);
                Assert.Equal(corpusValue, detailed.TypeEffectiveness[i][j]);
            }
        }
    }

    [Theory]
    [InlineData("toy-ruleset")]
    [InlineData("improved")]
    [InlineData("classical-faithful")]
    public void Each_type_effectiveness_note_cites_the_report_and_the_dat_offset(string rulesetId)
    {
        var note = Repository.RulesetById(rulesetId)!.Combat.DetailedResolver.Provenance?.SourceFor("typeEffectiveness") ?? "";
        Assert.Contains(ReportSource, note, StringComparison.Ordinal);
        Assert.Contains("0x1F7A6", note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deployment_sort_keys_are_consistent_with_the_matrix()
    {
        // Report §2: the AI's deployment sort key is troops × M[type][0] div M[0][type], giving
        // LI 1, HI 15, archers 0.5, LC 5 and HC 6. Integer arithmetic cross-checks HI, LC and HC at
        // their factors (60/4, 25/5, 18/3); the archers' 0.5 appears inverted as M[0][2] / M[2][0].
        Assert.Equal(15, ReportMatrix[1][0] / ReportMatrix[0][1]);
        Assert.Equal(5, ReportMatrix[3][0] / ReportMatrix[0][3]);
        Assert.Equal(6, ReportMatrix[4][0] / ReportMatrix[0][4]);
        Assert.Equal(2, ReportMatrix[0][2] / ReportMatrix[2][0]);
    }
}
