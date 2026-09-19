using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T34 Done-when line 7: the nation table exposes wealth (SAV +0x430, DAT +0x409) and the signed
/// 16-bit tax base (SAV +0x44c, DAT +0x41b), confirmed in
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md.
/// </summary>
public class SaveNationTableEconomyTests
{
    // Nation indices, per NationCatalog: Rome 0, Greece 7, Illyria 9.

    [SkippableFact]
    public void Romes_tax_base_is_2444_in_summer_7()
    {
        // "Rome's value is 2,444, not 2,440 (... 1_rome_270_summer_7.sav, ...)."
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_rome_270_summer_7.sav"));

        var rome = SaveNationTable.Parse(data).Nations[0];

        Assert.Equal((short)2444, rome.TaxBase);
    }

    [SkippableFact]
    public void Romes_tax_base_is_2528_in_the_dat()
    {
        // "The DAT's starting values are not the rebuild's. Rome 2528 stored vs 2464 recomputed; ..."
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var rome = SaveNationTable.Parse(data).Nations[0];

        Assert.Equal((short)2528, rome.TaxBase);
    }

    [SkippableFact]
    public void Tax_base_moves_by_the_captured_citys_contribution_times_four_across_the_naupactus_capture()
    {
        // The Naupactus capture table: Illyria (new owner) tax base 396 -> 444 (+48); Greece (old
        // owner) 2296 -> 2248 (-48), both "± 12 << 2" for Naupactus's contribution of 12.
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var before = SaveNationTable.Parse(
            File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_rome_270_winter_7_b.sav")));
        var after = SaveNationTable.Parse(
            File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_rome_270_winter_9_b.sav")));

        Assert.Equal((short)396, before.Nations[9].TaxBase);
        Assert.Equal((short)444, after.Nations[9].TaxBase);
        Assert.Equal((short)2296, before.Nations[7].TaxBase);
        Assert.Equal((short)2248, after.Nations[7].TaxBase);
    }

    [SkippableFact]
    public void Wealth_moves_by_population_times_3000_across_the_naupactus_capture()
    {
        // The same table's wealth column: Illyria 768,000 -> 843,000; Greece 2,490,000 -> 2,415,000,
        // both "± 25 × 3000" for Naupactus's population of 25.
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var before = SaveNationTable.Parse(
            File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_rome_270_winter_7_b.sav")));
        var after = SaveNationTable.Parse(
            File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_rome_270_winter_9_b.sav")));

        Assert.Equal(768000, before.Nations[9].Wealth);
        Assert.Equal(843000, after.Nations[9].Wealth);
        Assert.Equal(2490000, before.Nations[7].Wealth);
        Assert.Equal(2415000, after.Nations[7].Wealth);
    }
}
