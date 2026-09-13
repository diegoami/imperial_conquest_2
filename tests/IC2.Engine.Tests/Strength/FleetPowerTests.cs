using IC2.Engine.Core;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T07 Strength functions", Done-when 2, 3, and 5 (the third
/// of the three required truncation cases).
/// </summary>
public sealed class FleetPowerTests
{
    /// <summary>
    /// Done-when 2 (no carried army): <c>base = ships * condition / 10</c>, reproduced exactly with the
    /// random draw fixed at 0 so the bonus term (<c>draw * (base/10)</c>) contributes nothing and the
    /// base formula is isolated. <c>50 ships * 80% condition / 10 = 400</c>.
    /// </summary>
    [Fact]
    public void Compute_WithoutCarriedArmy_MatchesHandComputedBase()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var rng = new FixedDrawRng(fixedDraw: 0);

        var power = FleetPower.Compute(ships: 50, conditionPercent: 80, rng, ruleset);

        Assert.Equal(400, power);
    }

    /// <summary>
    /// Done-when 2 (with carried army): the carried-army term uses <see cref="SiegeStrength.Attacker"/>
    /// (the original's own choice, <c>FUN_0044A930</c> -- see <see cref="FleetPower"/>'s remarks),
    /// divided by <c>NavalCombatRules.CarriedArmyPowerDivisor</c> (50).
    /// <code>
    /// base                = 50 * 80 / 10 = 400
    /// carried army: 1 archers unit, 1,000 troops, morale 60
    ///   siege strength    = (1000 * 3) / 80 * 60 = (3000/80)*60 = 37*60 = 2,220
    ///   carried term      = 2220 / 50 = 44
    /// base (with carried) = 400 + 44 = 444
    /// </code>
    /// </summary>
    [Fact]
    public void Compute_WithCarriedArmy_MatchesHandComputedBase()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var rng = new FixedDrawRng(fixedDraw: 0);
        var carriedArmy = new FleetPower.CarriedArmyStrength(
            Units: new[] { StrengthTestbed.Unit(StrengthTestbed.ArcherUnitTypeId, 1000) },
            Morale: 60,
            ArcherUnitTypeId: StrengthTestbed.ArcherUnitTypeId);

        var power = FleetPower.Compute(ships: 50, conditionPercent: 80, rng, ruleset, carriedArmy);

        Assert.Equal(444, power);
    }

    /// <summary>
    /// Done-when 3: the random bonus is applied through <see cref="IRng"/> and is exactly reproducible
    /// under a fixed seed -- two independent generators created from the same seed must produce the
    /// same <see cref="FleetPower"/> result, asserted twice (as two separate calls) in this one test.
    /// A second, different seed is also checked to confirm the draw is genuinely seed-driven rather
    /// than a constant that happens to satisfy the first assertion vacuously.
    /// </summary>
    [Fact]
    public void Compute_RandomBonus_IsReproducibleUnderTheSameSeed()
    {
        var ruleset = StrengthTestbed.Ruleset;

        var first = FleetPower.Compute(ships: 90, conditionPercent: 85, new SplitMix64Rng(12345UL), ruleset);
        var second = FleetPower.Compute(ships: 90, conditionPercent: 85, new SplitMix64Rng(12345UL), ruleset);
        Assert.Equal(first, second);

        var differentSeed = FleetPower.Compute(ships: 90, conditionPercent: 85, new SplitMix64Rng(999UL), ruleset);
        Assert.NotEqual(first, differentSeed);
    }

    /// <summary>
    /// Done-when 5 (case 3 of 3): the random bonus is <c>base + draw * (base / 10)</c>, with
    /// <c>base / 10</c> truncated before multiplying by the integer draw -- not
    /// <c>base * (1 + draw / 10)</c> evaluated as one real-valued expression (see
    /// <see cref="FleetPower"/>'s remarks for why the original's own decompiled code rules out the
    /// second reading). <c>ships=17, condition=10</c> gives <c>base = 170/10 = 17</c> exactly, isolating
    /// the bonus term:
    /// <code>
    /// correct:    base + draw * (base / 10) = 17 + 3 * (17 / 10) = 17 + 3 * 1 = 20
    /// naive real: base * (1 + draw / 10.0)  = 17 * 1.3            = 22.1 -> 22
    /// </code>
    /// </summary>
    [Fact]
    public void Compute_RandomBonusTruncationOrder_DiffersFromNaiveRealDivision()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var rng = new FixedDrawRng(fixedDraw: 3);

        var power = FleetPower.Compute(ships: 17, conditionPercent: 10, rng, ruleset);

        Assert.Equal(20, power);
        Assert.NotEqual((int)Math.Round(17 * (1 + 3 / 10.0)), power);
    }
}
