using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Export;

/// <summary>
/// Checks the committed <c>data/worlds/classical-mediterranean.json</c> against
/// <c>docs/task-catalogue.md</c> T29's Done-when lines 1, 2, 4, 7 and 9. These do not need the
/// original DAT: they check the file this repository already ships, the same way any other
/// committed fixture is checked. <see cref="ExportScriptReproducibilityTests"/> covers re-running
/// the export itself, which does need the DAT.
/// </summary>
public class ExportedWorldTests
{
    private static World Load() => GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);

    /// <summary>DoD 4: the committed file round-trips through the loader with no schema errors.</summary>
    [Fact]
    public void World_loads_with_no_schema_errors()
    {
        var world = Load();
        Assert.Equal("classical-mediterranean", world.Id);
    }

    /// <summary>
    /// DoD 1: exactly 334 cities, 16 nations and the confirmed 320x140 map.
    /// </summary>
    [Fact]
    public void World_has_the_confirmed_city_nation_and_map_counts()
    {
        var world = Load();

        Assert.Equal(320, world.Width);
        Assert.Equal(140, world.Height);
        Assert.Equal(334, world.Cities.Count);
        Assert.Equal(16, world.Nations.Count);
    }

    /// <summary>
    /// DoD 1: the 16 nation names, in order, match <c>IC2.Data.NationCatalog</c>'s ordered list --
    /// the cross-check the task entry requires, over the committed export's own values (not a
    /// re-parse of the DAT, which <see cref="ExportScriptReproducibilityTests"/> covers).
    /// </summary>
    [Fact]
    public void World_nation_names_match_NationCatalog_in_order()
    {
        var expected = new[]
        {
            "Rome", "Carthage", "Seleucid", "Ptolemaic", "Macedonia", "Numidia",
            "Gaul", "Greece", "Celtiberia", "Illyria", "Dacia", "Bithynia",
            "Galatia", "Armenia", "Media", "Thracia",
        };

        var world = Load();
        var actual = world.Nations.Select(n => n.Name).ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// DoD 2: no <c>leaderName</c> in the export may claim DAT provenance -- leader names are New
    /// Game state (<c>TPremierForm_NewGame</c>'s <c>FUN_00448aa4</c>), not world data.
    /// </summary>
    [Fact]
    public void No_leaderName_provenance_claims_DAT_provenance()
    {
        var world = Load();

        foreach (var nation in world.Nations)
        {
            var source = nation.Provenance?.SourceFor("leaderName");
            Assert.False(string.IsNullOrEmpty(source), $"Nation '{nation.Id}' has no leaderName provenance at all.");
            Assert.False(
                source!.Contains("confirmed", StringComparison.OrdinalIgnoreCase) && source.Contains("DAT", StringComparison.Ordinal),
                $"Nation '{nation.Id}' leaderName provenance claims DAT provenance: \"{source}\"");
        }
    }

    /// <summary>
    /// DoD 7: army 14 is the Thracian army at (160, 30) with 22,000 troops, 142 tons and morale
    /// 65 -- pinned from <c>docs/investigations/dat-file-layout.md</c>.
    /// </summary>
    [Fact]
    public void Army14_matches_the_pinned_Thracian_army()
    {
        var world = Load();
        var army = world.StartingArmies.Single(a => a.Id == "army-14");

        Assert.Equal("thracia", army.Nation);
        Assert.Equal(160, army.X);
        Assert.Equal(30, army.Y);
        Assert.Equal(65, army.Morale);
        Assert.Equal(142, army.SupplyTons);
        Assert.Equal(22000, army.Units.Sum(u => u.Troops));
    }

    /// <summary>
    /// DoD 7: fleet 0 is 90 ships with 80 tons at condition 85 -- pinned from
    /// <c>docs/investigations/dat-file-layout.md</c>.
    /// </summary>
    [Fact]
    public void Fleet0_matches_the_pinned_Carthaginian_fleet()
    {
        var world = Load();
        var fleet = world.StartingFleets.Single(f => f.Id == "fleet-0");

        Assert.Equal(90, fleet.Ships);
        Assert.Equal(80, fleet.SupplyTons);
        Assert.Equal(85, fleet.ConditionPercent);
    }

    /// <summary>DoD 7: exactly 15 starting armies and 2 starting fleets, no more, no fewer.</summary>
    [Fact]
    public void World_has_15_armies_and_2_fleets()
    {
        var world = Load();

        Assert.Equal(15, world.StartingArmies.Count);
        Assert.Equal(2, world.StartingFleets.Count);
    }

    /// <summary>
    /// DoD 9: Rome's starting tax base is the DAT word at +0x41b, exported as stored (2,528),
    /// which the export must NOT recompute from the world's own cities -- the quarterly rebuild
    /// gives a different figure (2,464) until the first quarter actually runs
    /// (<c>nation-tax-base-and-city-economy-fields.md</c>).
    /// </summary>
    [Fact]
    public void Rome_tax_base_is_stored_not_rebuilt()
    {
        var world = Load();
        var rome = world.Nations.Single(n => n.Id == "rome");

        Assert.Equal(2528, rome.TaxBase);

        // The quarterly rebuild's own shape (confirmed: nation-tax-base-and-city-economy-fields.md,
        // corpus id economy.taxBaseContributionMultiplier): taxBase = sum over owned cities of
        // (tribute * population / maxPopulation) << 2. Computed here purely to prove the stored
        // value is NOT what that formula gives -- this test does not assert the rebuilt figure is
        // "right", only that the export did not silently substitute it for the stored word.
        long rebuilt = 0;
        foreach (var city in world.Cities)
        {
            if (city.Owner != "rome" || city.MaxPopulationThousands == 0)
            {
                continue;
            }

            rebuilt += (city.Tribute * city.PopulationThousands / city.MaxPopulationThousands) * 4;
        }

        Assert.NotEqual(rebuilt, rome.TaxBase);
    }

    /// <summary>
    /// DoD 9's provenance must say the value is exported as stored, not recomputed -- the claim
    /// this test's sibling proves in numbers, restated as text so a future edit that starts
    /// recomputing the field cannot slip past a provenance no longer matching the code.
    /// </summary>
    [Fact]
    public void Rome_tax_base_provenance_says_stored_not_recomputed()
    {
        var world = Load();
        var rome = world.Nations.Single(n => n.Id == "rome");
        var source = rome.Provenance?.SourceFor("taxBase");

        Assert.False(string.IsNullOrEmpty(source));
        Assert.Contains("stored", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not recomputed", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sanity: the world's own <c>GameDataValidation</c> pass (schema plus semantic checks) is clean.</summary>
    [Fact]
    public void World_passes_GameDataValidation()
    {
        var world = Load();
        GameDataValidation.Validate(ExportedDataPaths.WorldFile, world);
    }
}
