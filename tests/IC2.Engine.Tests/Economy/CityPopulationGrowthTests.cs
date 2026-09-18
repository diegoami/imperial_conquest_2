using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 9: quarterly city population growth. Every value below is transcribed
/// from <c>city-population-growth.md</c>, not re-derived.
/// </summary>
public sealed class CityPopulationGrowthTests
{
    private static readonly IC2.Engine.Model.EconomyRules Economy = EconomyTestbed.Ruleset.Economy;

    /// <summary>Sala (Carthage), <c>1_thracia_271_spring_11 → summer_1</c>: 60 of 70, tax 5, mob 15 → 63.</summary>
    [Fact]
    public void BelowMaximum_GrowsByAtLeastOne_Sala()
    {
        var grown = CityPopulationGrowth.Grow(
            populationThousands: 60, maxPopulationThousands: 70,
            ownerTaxRatePercent: 5, ownerMobilizedPercent: 15, threatened: false, Economy);

        Assert.Equal(63, grown);
        Assert.True(grown > 60);
    }

    [Fact]
    public void AtMaximum_DoesNotChange()
    {
        var grown = CityPopulationGrowth.Grow(
            populationThousands: 70, maxPopulationThousands: 70,
            ownerTaxRatePercent: 5, ownerMobilizedPercent: 15, threatened: false, Economy);

        Assert.Equal(70, grown);
    }

    /// <summary>pop 44 of 72 (d = 7), mob 0: tax 17 → +8, tax 18 → +7 (the tax divisor lies in 120..126).</summary>
    [Theory]
    [InlineData(17, 52)]
    [InlineData(18, 51)]
    public void HigherTaxRate_GrowsItLess_InSteps(int taxRatePercent, int expected)
    {
        var grown = CityPopulationGrowth.Grow(
            populationThousands: 44, maxPopulationThousands: 72,
            ownerTaxRatePercent: taxRatePercent, ownerMobilizedPercent: 0, threatened: false, Economy);

        Assert.Equal(expected, grown);
    }

    /// <summary>pop 44 of 72 (d = 7), tax 0: mob 42 → +8, mob 43 → +7.</summary>
    [Theory]
    [InlineData(42, 52)]
    [InlineData(43, 51)]
    public void HigherMobilization_GrowsItLess_InSteps(int mobilizedPercent, int expected)
    {
        var grown = CityPopulationGrowth.Grow(
            populationThousands: 44, maxPopulationThousands: 72,
            ownerTaxRatePercent: 0, ownerMobilizedPercent: mobilizedPercent, threatened: false, Economy);

        Assert.Equal(expected, grown);
    }

    /// <summary>Laranda (Galatia), <c>1_rome_270_autumn_11 → winter_1</c>: 56 of 72, tax 40, mob 100 → 59 —
    /// the only sampled city where both reductions bite.</summary>
    [Fact]
    public void BothReductionsTogether_Laranda()
    {
        var grown = CityPopulationGrowth.Grow(
            populationThousands: 56, maxPopulationThousands: 72,
            ownerTaxRatePercent: 40, ownerMobilizedPercent: 100, threatened: false, Economy);

        Assert.Equal(59, grown);
    }

    [Fact]
    public void AThreatenedCity_DoesNotGrow_TheSameCityOneCellFurtherAwayDoes()
    {
        var threatened = CityPopulationGrowth.Grow(
            populationThousands: 60, maxPopulationThousands: 70,
            ownerTaxRatePercent: 5, ownerMobilizedPercent: 15, threatened: true, Economy);
        Assert.Equal(60, threatened);

        var unthreatened = CityPopulationGrowth.Grow(
            populationThousands: 60, maxPopulationThousands: 70,
            ownerTaxRatePercent: 5, ownerMobilizedPercent: 15, threatened: false, Economy);
        Assert.Equal(63, unthreatened);
    }

    /// <summary>
    /// Every combination of gap 1..40, tax 0..100 and mobilization 0..100 the task entry's property
    /// test asks for — a small hand-rolled combinatorial sweep rather than a property-testing library
    /// this repo does not otherwise depend on.
    /// </summary>
    public static IEnumerable<object[]> GapsTaxesAndMobilizations()
    {
        int[] rates = { 0, 25, 50, 75, 100 };
        for (var gap = 1; gap <= 40; gap++)
        {
            foreach (var tax in rates)
            {
                foreach (var mobilization in rates)
                {
                    yield return new object[] { gap, tax, mobilization };
                }
            }
        }
    }

    /// <summary>Growth never exceeds the maximum, over the full confirmed domain.</summary>
    [Theory]
    [MemberData(nameof(GapsTaxesAndMobilizations))]
    public void GrowthNeverExceedsTheMaximum(int gap, int taxRatePercent, int mobilizedPercent)
    {
        const int maxPopulationThousands = 100;
        var population = maxPopulationThousands - gap;

        var grown = CityPopulationGrowth.Grow(
            population, maxPopulationThousands, taxRatePercent, mobilizedPercent, threatened: false, Economy);

        Assert.InRange(grown, population, maxPopulationThousands);
    }
}
