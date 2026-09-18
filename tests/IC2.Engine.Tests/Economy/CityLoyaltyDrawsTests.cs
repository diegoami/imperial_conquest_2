using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 11: the quarterly loyalty draws and rebellion-risk check, pure-function
/// coverage with a scripted <see cref="IRng"/>. The city-index-order determinism bullet is covered at the
/// wired-system level, in <c>QuarterlyCityEconomySystemTests</c>.
/// </summary>
public sealed class CityLoyaltyDrawsTests
{
    private static readonly EconomyRules Economy = EconomyTestbed.Ruleset.Economy;

    private static CityState City(int loyalty) => new(
        "c1", "City", 0, 0, "north", "north", loyalty, 0, 0, 10, 10, 0, false, ValueList<UnitSlot>.Empty);

    [Fact]
    public void ALowTaxCity_RisesByExactlyTheDraw()
    {
        var city = City(loyalty: 50);
        var rng = new ScriptedRng(nextIntDraws: new[] { 2 }, nextChanceDraws: new[] { false });

        var result = CityLoyaltyDraws.Apply(city, ownerTaxRatePercent: 5, isCapital: false, Economy, rng);

        Assert.Equal(52, result.Loyalty);
    }

    /// <summary>
    /// A city at 80 never rises. No <see cref="IRng.NextInt(int)"/> draw is scripted at all: if the
    /// implementation drew one anyway, the test would throw rather than silently pass.
    /// </summary>
    [Fact]
    public void ACityAt80_NeverRises()
    {
        var city = City(loyalty: 80);
        var rng = new ScriptedRng(nextIntDraws: Array.Empty<int>(), nextChanceDraws: new[] { false });

        var result = CityLoyaltyDraws.Apply(city, ownerTaxRatePercent: 5, isCapital: false, Economy, rng);

        Assert.Equal(80, result.Loyalty);
    }

    /// <summary>The loss is <c>Random(taxRate) / 8</c>, drawn only when the 1-in-3 roll hits.</summary>
    [Fact]
    public void TheLossIsRandomTaxRateDividedByEight()
    {
        var city = City(loyalty: 60);
        // taxRatePercent 40 >= 11, so the rise branch never draws; the fall branch's Random(3)==0 hits,
        // and Random(40) draws 17: loss = 17 / 8 = 2.
        var rng = new ScriptedRng(nextIntDraws: new[] { 17 }, nextChanceDraws: new[] { true });

        var result = CityLoyaltyDraws.Apply(city, ownerTaxRatePercent: 40, isCapital: false, Economy, rng);

        Assert.Equal(58, result.Loyalty);
    }

    /// <summary>
    /// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 10
    /// (bug <c>#132</c>): <see cref="LoyaltyRiseRollBound"/> and <see cref="LoyaltyFallProbabilityDenominator"/>
    /// are pinned, not just exercised. This is what makes the difference: <see cref="ScriptedRng"/>
    /// checks each draw's actual bound/odds against the exact values <see cref="FixtureCorpus"/>
    /// transcribes for these two constants (independent of <c>toy-ruleset.json</c>) — so substituting 8
    /// for <see cref="EconomyRules.LoyaltyRiseRollBound"/> or 5 for
    /// <see cref="EconomyRules.LoyaltyFallProbabilityDenominator"/> in the ruleset now makes
    /// <see cref="Economy.CityLoyaltyDraws"/> call <c>NextInt</c>/<c>NextChance</c> with a bound/odds
    /// that no longer match the corpus's fixed 4/3, and this test fails on the mismatch rather than
    /// silently accepting whatever the caller happened to pass.
    /// </summary>
    [Fact]
    public void TheRollBoundAndFallDenominator_ArePinnedAgainstTheCorpus_NotJustExercised()
    {
        var city = City(loyalty: 50);
        var expectedRollBound = FixtureCorpus.Get("economy.loyaltyRiseRollBound").AsInt();
        var expectedFallDenominator = FixtureCorpus.Get("economy.loyaltyFallProbabilityDenominator").AsInt();

        var rng = new ScriptedRng(
            nextIntDraws: new[] { 2 },
            nextChanceDraws: new[] { false },
            expectedNextIntBounds: new[] { expectedRollBound },
            expectedNextChanceOdds: new[] { (1, expectedFallDenominator) });

        var result = CityLoyaltyDraws.Apply(city, ownerTaxRatePercent: 5, isCapital: false, Economy, rng);

        Assert.Equal(52, result.Loyalty);
    }

    [Theory]
    [InlineData(29, false, true)]
    [InlineData(30, false, false)]
    [InlineData(29, true, false)]
    public void RebellionRisk_FiresForANonCapitalCityUnderTheThreshold(int loyalty, bool isCapital, bool expectedRisk)
    {
        var city = City(loyalty);
        // taxRatePercent 40 >= 11 skips the rise draw; the fall roll misses, so loyalty is unchanged.
        var rng = new ScriptedRng(nextIntDraws: Array.Empty<int>(), nextChanceDraws: new[] { false });

        var result = CityLoyaltyDraws.Apply(city, ownerTaxRatePercent: 40, isCapital, Economy, rng);

        Assert.Equal(loyalty, result.Loyalty);
        Assert.Equal(expectedRisk, result.RebellionRisk);
    }
}
