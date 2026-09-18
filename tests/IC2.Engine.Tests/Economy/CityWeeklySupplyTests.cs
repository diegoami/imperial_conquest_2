using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 1, 2, 3 and
/// 5's pure-function level: <see cref="CityWeeklySupply.Apply"/> in isolation, with a stub RNG so the
/// famine-unrest roll can be pinned without hunting for a real seed. Done-when 4's "asserted end to end"
/// requirement and the famine roll's city-list-order requirement are <see cref="WeeklyCitySupplySystemTests"/>'s,
/// not this file's — this file only proves the formula shape, never the threat predicate wired through
/// the system. Every constant is transcribed from <c>city-population-growth.md</c>, not re-derived.
/// </summary>
public sealed class CityWeeklySupplyTests
{
    private static readonly Ruleset Ruleset = EconomyTestbed.Ruleset;

    private const int Spring = 0;
    private const int Summer = 1;
    private const int Autumn = 2;
    private const int Winter = 3;

    private static CityWeeklySupply.Result Apply(
        int currentSupplyTons, int populationThousands, int seasonIndex, int mobilizedPercent, bool threatened = false,
        bool? famineRoll = null) =>
        CityWeeklySupply.Apply(
            currentSupplyTons, populationThousands, seasonIndex, mobilizedPercent, threatened,
            Ruleset, new ScriptedRng(nextChanceDraws: famineRoll is { } roll ? new[] { roll } : null));

    // ---- Done when 1: at mobilization 0, a pop-50 city's stock changes by exactly
    // +50 / +200 / +200 / -100 per turn in Spring / Summer / Autumn / Winter. ----

    [Theory]
    [InlineData(Spring, 50)]
    [InlineData(Summer, 200)]
    [InlineData(Autumn, 200)]
    [InlineData(Winter, -100)]
    public void AtMobilizationZero_APop50City_ChangesByExactlyTheSeasonsFigure(int seasonIndex, int expectedDelta)
    {
        const int start = 200; // clear of both the 0 floor and the pop*10=500 cap for every case above.
        var result = Apply(start, populationThousands: 50, seasonIndex, mobilizedPercent: 0);

        Assert.Equal(start + expectedDelta, result.SupplyTons);
    }

    // ---- Done when 2: at mobilization 100 every figure halves; at mobilization 30 the Winter
    // change is -85. ----

    [Theory]
    [InlineData(Spring, 25)]
    [InlineData(Summer, 100)]
    [InlineData(Autumn, 100)]
    [InlineData(Winter, -50)]
    public void AtMobilizationOneHundred_EveryFigureHalves(int seasonIndex, int expectedDelta)
    {
        const int start = 200;
        var result = Apply(start, populationThousands: 50, seasonIndex, mobilizedPercent: 100);

        Assert.Equal(start + expectedDelta, result.SupplyTons);
    }

    [Fact]
    public void AtMobilizationThirty_TheWinterChangeIsMinusEightyFive()
    {
        const int start = 200;
        var result = Apply(start, populationThousands: 50, Winter, mobilizedPercent: 30);

        Assert.Equal(start - 85, result.SupplyTons);
    }

    // ---- Done when 3: the stock is capped at pop x 10 (a pop-181 city at 1,700 in Summer ends at
    // 1,810), and floored at 0. ----

    [Fact]
    public void ThePop181CityAt1700InSummer_EndsAtTheCeiling1810()
    {
        var result = Apply(currentSupplyTons: 1700, populationThousands: 181, Summer, mobilizedPercent: 0);

        Assert.Equal(1810, result.SupplyTons);
    }

    [Fact]
    public void TheStockNeverGoesBelowZero()
    {
        // pop 50, Winter, mob 0: inc = -100. Starting at 30, the naive result would be -70. This
        // floored result qualifies for the famine roll (0, Winter) -- scripted false, since the roll's
        // own behaviour is Done-when 5's, not this test's.
        var result = Apply(currentSupplyTons: 30, populationThousands: 50, Winter, mobilizedPercent: 0, famineRoll: false);

        Assert.Equal(0, result.SupplyTons);
    }

    [Fact]
    public void APop50CityAlreadyAtItsCeiling_CannotExceedItInSummer()
    {
        var result = Apply(currentSupplyTons: 500, populationThousands: 50, Summer, mobilizedPercent: 0);

        Assert.Equal(500, result.SupplyTons); // pop * 10 = 500, the ceiling.
    }

    // ---- Done when 5, the formula-level half: with a stub RNG whose Random(3) returns 0 (NextChance
    // true), a Winter city whose stock ends the turn at 0 loses exactly 1 loyalty; returning 1 or 2
    // (NextChance false) keeps it; a city with stock left, or outside Winter, never draws at all
    // (ScriptedRng throws if a draw is attempted with none scripted). ----

    [Fact]
    public void WinterCityEndingAtZero_WithARandomHit_LosesExactlyOneLoyalty()
    {
        var rng = new ScriptedRng(nextChanceDraws: new[] { true });

        // pop 10, mob 0, Winter: inc = -20; starting at 20 lands exactly on 0.
        var result = CityWeeklySupply.Apply(
            currentSupplyTons: 20, populationThousands: 10, Winter, ownerMobilizedPercent: 0,
            threatened: false, Ruleset, rng);

        Assert.Equal(0, result.SupplyTons);
        Assert.Equal(-1, result.LoyaltyDelta);
    }

    [Fact]
    public void WinterCityEndingAtZero_WithARandomMiss_KeepsItsLoyalty()
    {
        var rng = new ScriptedRng(nextChanceDraws: new[] { false });

        var result = CityWeeklySupply.Apply(
            currentSupplyTons: 20, populationThousands: 10, Winter, ownerMobilizedPercent: 0,
            threatened: false, Ruleset, rng);

        Assert.Equal(0, result.SupplyTons);
        Assert.Equal(0, result.LoyaltyDelta);
    }

    [Fact]
    public void AWinterCityWithStockLeft_NeverDrawsAndNeverLosesLoyalty()
    {
        // pop 50, mob 0, Winter: inc = -100; starting at 200 leaves 100 tons, never reaching zero.
        var rng = new ScriptedRng(); // no draws scripted -- a draw attempt throws.
        var result = CityWeeklySupply.Apply(
            currentSupplyTons: 200, populationThousands: 50, Winter, ownerMobilizedPercent: 0,
            threatened: false, Ruleset, rng);

        Assert.Equal(100, result.SupplyTons);
        Assert.Equal(0, result.LoyaltyDelta);
    }

    [Fact]
    public void ACityEndingAtZeroOutsideWinter_NeverDrawsAndNeverLosesLoyalty()
    {
        // pop 50, mob 0, Spring: inc = +50, never zero from a positive start -- use a pop that ends
        // exactly at zero instead: pop 0 keeps supply at 0 (0 * anything = 0) regardless of season.
        var rng = new ScriptedRng(); // no draws scripted -- a draw attempt throws.
        var result = CityWeeklySupply.Apply(
            currentSupplyTons: 0, populationThousands: 0, Spring, ownerMobilizedPercent: 0,
            threatened: false, Ruleset, rng);

        Assert.Equal(0, result.SupplyTons);
        Assert.Equal(0, result.LoyaltyDelta);
    }

    // ---- Done when 4, the pure-formula half only (see WeeklyCitySupplySystemTests for the "asserted
    // end to end" requirement): a threatened city gains nothing in Summer, but still loses in Winter. ----

    [Fact]
    public void AThreatenedCity_GainsNothingInSummer_ButStillLosesInWinter()
    {
        var summer = Apply(currentSupplyTons: 200, populationThousands: 50, Summer, mobilizedPercent: 0, threatened: true);
        Assert.Equal(200, summer.SupplyTons); // unchanged: inc would be +200, clamped to <= 0.

        var winter = Apply(currentSupplyTons: 200, populationThousands: 50, Winter, mobilizedPercent: 0, threatened: true);
        Assert.Equal(100, winter.SupplyTons); // -100 still applies: min(inc, 0) does not touch a loss.
    }
}
