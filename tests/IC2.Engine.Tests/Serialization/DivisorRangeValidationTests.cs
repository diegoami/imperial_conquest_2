using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.SerializationTests;

/// <summary>
/// N5 (T63 review round 1): six ruleset fields this task owns divide a live value at a real call
/// site -- <see cref="CombatRules.DeletionDivisorNational"/>/<see cref="CombatRules.DeletionDivisorMercenary"/>
/// (<c>BattleCasualties.DeleteBelowThreshold</c>), <see cref="SiegeRules.ErosionFloorDenominator"/>/
/// <see cref="SiegeRules.ErosionCeilingDenominator"/>/<see cref="SiegeRules.PopulationFloorDivisor"/>
/// (<c>InstantBattleResolver.ResolveSiege</c>'s erosion terms), and
/// <see cref="NavalRules.StormUnitLossDivisor"/> (<c>FleetAttritionRule</c>'s storm whole-unit loss).
/// A ruleset file with one of these at 0 (or negative) previously loaded cleanly and only crashed
/// with a <see cref="DivideByZeroException"/> the first time a siege, a battle-casualty pass, or a
/// heavy storm actually ran it -- this proves <see cref="GameDataValidation.Validate"/> now rejects
/// it at load time instead, naming the offending field.
/// </summary>
public class DivisorRangeValidationTests
{
    public static IEnumerable<object[]> ZeroDivisorMutations()
    {
        yield return new object[] { "combat.deletionDivisorNational", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { DeletionDivisorNational = v } }) };
        yield return new object[] { "combat.deletionDivisorMercenary", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Combat = r.Combat with { DeletionDivisorMercenary = v } }) };
        yield return new object[] { "siege.erosionFloorDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { ErosionFloorDenominator = v } }) };
        yield return new object[] { "siege.erosionCeilingDenominator", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { ErosionCeilingDenominator = v } }) };
        yield return new object[] { "siege.populationFloorDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Siege = r.Siege with { PopulationFloorDivisor = v } }) };
        yield return new object[] { "naval.stormUnitLossDivisor", (Func<Ruleset, int, Ruleset>)((r, v) => r with { Naval = r.Naval with { StormUnitLossDivisor = v } }) };
    }

    [Theory]
    [MemberData(nameof(ZeroDivisorMutations))]
    public void A_zero_divisor_is_rejected_at_validation_naming_the_field(string expectedFieldName, Func<Ruleset, int, Ruleset> withValue)
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var mutated = withValue(ruleset, 0);

        var ex = Assert.Throws<MalformedGameDataException>(
            () => GameDataValidation.Validate("mutated-toy-ruleset.json", mutated));

        Assert.Contains(expectedFieldName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("greater than 0", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ZeroDivisorMutations))]
    public void A_negative_divisor_is_also_rejected(string expectedFieldName, Func<Ruleset, int, Ruleset> withValue)
    {
        _ = expectedFieldName;
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var mutated = withValue(ruleset, -1);

        Assert.Throws<MalformedGameDataException>(
            () => GameDataValidation.Validate("mutated-toy-ruleset.json", mutated));
    }

    /// <summary>The shipped toy ruleset already passes this check (it must not throw at all).</summary>
    [Fact]
    public void Shipped_toy_ruleset_passes_the_divisor_check()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        GameDataValidation.Validate(TestPaths.ToyRulesetFile, ruleset); // must not throw
    }
}
