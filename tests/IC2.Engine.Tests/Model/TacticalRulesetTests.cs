using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T122 Done-when 2: every value of the tactical battle's Scope table is asserted for all three
/// shipped rulesets against literals copied from
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c>, with the report section named in a comment,
/// and <c>improved</c>'s <c>combat.tactical</c> equals <c>classical-faithful</c>'s. This test does not
/// compile on <c>main</c> (there is no <c>CombatRules.Tactical</c> member) and passes after T122.
/// </summary>
public class TacticalRulesetTests
{
    private static Ruleset Toy => GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
    private static Ruleset Improved => GameDataLoader.LoadFile<Ruleset>(
        Path.Combine(TestPaths.DataRoot, "rulesets", "improved.json"));
    private static Ruleset Classical => GameDataLoader.LoadFile<Ruleset>(
        Path.Combine(TestPaths.DataRoot, "rulesets", "classical-faithful.json"));

    public static IEnumerable<object[]> ShippedRulesets() => new[]
    {
        new object[] { "toy-ruleset" },
        new object[] { "improved" },
        new object[] { "classical-faithful" },
    };

    private static Ruleset ByName(string id) => id switch
    {
        "toy-ruleset" => Toy,
        "improved" => Improved,
        "classical-faithful" => Classical,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown shipped ruleset."),
    };

    /// <summary>The Scope table's per-type shooting vulnerability, in unitTypes order (report §4).</summary>
    [Theory]
    [MemberData(nameof(ShippedRulesets))]
    public void Shot_vulnerability_is_the_reports_per_type_weight(string id)
    {
        var ruleset = ByName(id);
        var expected = new[] { 18, 2, 18, 15, 4 }; // LI, HI, Ar, LC, HC -- report §4, stat +0x20.
        Assert.Equal(expected.Length, ruleset.UnitTypes.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], ruleset.UnitTypes[i].ShotVulnerability);
        }
    }

    /// <summary>Every scalar and table of the tactical block, for all three shipped rulesets.</summary>
    [Theory]
    [MemberData(nameof(ShippedRulesets))]
    public void Every_tactical_value_matches_the_report(string id)
    {
        var t = ByName(id).Combat.Tactical;
        Assert.NotNull(t);

        // Board -- report §1, §2.
        Assert.Equal(14, t.BoardWidth);
        Assert.Equal(12, t.BoardHeight);
        Assert.Equal(3, t.HomeRows);        // attacker y < 3, defender y > 8.
        Assert.Equal(13, t.CopyInColumns);  // (s mod 13, s div 13).

        // Copy-in -- report §2.
        Assert.Equal(3, t.ComputerArmyMoraleBonus);
        Assert.Equal(4, t.MoraleQualityFactor);
        Assert.Equal(60, t.MoraleMin);
        Assert.Equal(90, t.MoraleMax);

        // Slow advance -- report §3.
        Assert.Equal(1, t.SlowAdvanceMoves);
        Assert.Equal(2, t.SlowAdvanceMinDistance);
        Assert.Equal(10, t.SlowAdvanceHalfRounds);
        Assert.Equal("heavy_infantry", t.SlowAdvanceExemptType);

        // Shooting -- report §4, §6.
        Assert.Equal(5, t.ShotTroopsFactor);
        Assert.Equal(150000, t.ShotDivisorBase);
        Assert.Equal(3, t.ShotShooterTroopsDivisor);
        Assert.Equal(2, t.ShotTargetTroopsDivisor);
        Assert.Equal(35, t.ShotMoraleNumerator);
        Assert.Equal(3, t.ShotMoraleCap);

        // Melee -- report §5, §6.
        Assert.Equal(4, t.MeleeFocusCap);
        Assert.Equal(12, t.MeleeAttackerExchangeDivisor);
        Assert.Equal(10, t.MeleeDefenderExchangeDivisor);
        Assert.Equal(2, t.MoraleWinnerDelta);
        Assert.Equal(-3, t.MoraleLoserDelta);
        Assert.Equal(99, t.MoraleCap);

        // Rout -- report §6.
        Assert.Equal(25, t.RoutTroopsDivisor);
        Assert.Equal(19, t.RoutMoraleAutomatic);
        Assert.Equal(39, t.RoutMoraleSafe);
        Assert.Equal(29, t.RoutDrawThreshold);
        Assert.Equal(6, t.RoutFriendPenalty);
        Assert.Equal(30, t.RoutCascadeBelow);
        Assert.Equal(5, t.RoutEnemyBonus);

        // Computer general -- report §2 (Form), §7 (order, preferences, scoring), §3 (danger term).
        var expectedFormations = new[]
        {
            new[] { "heavy_cavalry", "heavy_infantry", "light_infantry", "light_cavalry" },
            new[] { "light_infantry", "heavy_infantry", "heavy_cavalry", "light_cavalry" },
            new[] { "light_cavalry", "heavy_cavalry", "light_infantry", "heavy_infantry" },
            new[] { "light_cavalry", "light_infantry", "heavy_infantry", "heavy_cavalry" },
            new[] { "heavy_cavalry", "light_cavalry", "heavy_infantry", "light_infantry" },
        };
        Assert.Equal(expectedFormations.Length, t.PlacementFormations.Count);
        for (var r = 0; r < expectedFormations.Length; r++)
        {
            Assert.Equal(expectedFormations[r], t.PlacementFormations[r]);
        }

        Assert.Equal(
            new[] { "heavy_infantry", "heavy_cavalry", "light_cavalry", "light_infantry", "archers" },
            t.TypeOrder);

        var preferences = t.TargetPreferences;
        Assert.Equal(3, preferences.Count);
        Assert.Equal("light_infantry", preferences[0].Type);
        Assert.Equal(new[] { "archers", "light_infantry" }, preferences[0].Prefers);
        Assert.Equal("heavy_infantry", preferences[1].Type);
        Assert.Equal(new[] { "heavy_infantry" }, preferences[1].Prefers);
        Assert.Equal("heavy_cavalry", preferences[2].Type);
        Assert.Equal(new[] { "heavy_infantry" }, preferences[2].Prefers);

        Assert.Equal(10000, t.ScoreDivisor);
        Assert.Equal(4, t.ClaimLimit);
        Assert.Equal(3, t.ArcherEngageDistance);
        Assert.Equal(new[] { 1, 1, 2, 1, 1 }, t.ApproachBoxRadius); // unitTypes order; archers 2.
        Assert.Equal(3, t.FlankFlipChanceDenominator);
        Assert.Equal(100, t.DangerQualityDivisor);
    }

    /// <summary>
    /// T122 carries no <c>improved</c> differences: the two presets' <c>combat.tactical</c> values
    /// (provenance excluded) are identical. <c>improved</c>'s switches are T130's, added later.
    /// </summary>
    [Fact]
    public void Improved_tactical_values_equal_classical_faithful()
    {
        var improved = Improved.Combat.Tactical;
        var classical = Classical.Combat.Tactical;
        Assert.NotNull(improved);
        Assert.NotNull(classical);

        Assert.Equal(classical with { Provenance = null }, improved with { Provenance = null });
    }
}
