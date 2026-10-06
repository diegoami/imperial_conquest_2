using System.Text;
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
    /// T146 Done-when 1: every one of the 16 nations carries its 12-name leader pool from the DAT
    /// (<c>0x2089A</c>), <c>leaderName</c> is that pool's index-0 name, and both provenance notes cite
    /// the offset. Replaces T29's old <c>No_leaderName_provenance_claims_DAT_provenance</c>, which
    /// asserted the opposite (the pool is now world data).
    /// </summary>
    [Fact]
    public void Every_nation_carries_its_12_name_DAT_leader_pool()
    {
        var world = Load();
        Assert.Equal(16, world.Nations.Count);

        foreach (var nation in world.Nations)
        {
            var names = nation.LeaderNames;
            Assert.NotNull(names);
            Assert.Equal(12, names!.Count);
            Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name),
                $"Nation '{nation.Id}' has an empty leader name."));
            Assert.Equal(names[0], nation.LeaderName);

            var leaderSource = nation.Provenance?.SourceFor("leaderName");
            Assert.False(string.IsNullOrEmpty(leaderSource), $"Nation '{nation.Id}' has no leaderName provenance.");
            Assert.Contains("0x2089A", leaderSource, StringComparison.Ordinal);
            // Review R6: the index-0 pre-draw convention is not in any cited source, so the note is
            // tagged [designed] (the name's text stays DAT-confirmed at the offset above).
            Assert.StartsWith("designed:", leaderSource, StringComparison.Ordinal);

            var poolSource = nation.Provenance?.SourceFor("leaderNames");
            Assert.False(string.IsNullOrEmpty(poolSource), $"Nation '{nation.Id}' has no leaderNames provenance.");
            Assert.Contains("0x2089A", poolSource, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// T146 Done-when 1: the report's own observed names are in their nations' pools, and Carthage's and
    /// Ptolemaic's pinned indices hold. The observed names are the turn-order report's seed-12345 replay
    /// (<c>2026-10-03-new-game-turn-order-shuffle.md</c>): Rome's <em>Appius Claudius</em>,
    /// <em>Antiochus</em>, <em>Publius Scipio</em> and <em>Licinus Crassus</em>; Gaul's <em>Hengest</em>,
    /// <em>Arminius</em>, <em>Brennus</em> and <em>Horsa</em>; Carthage's index 6 <em>Agis</em> and the
    /// Ptolemaic index 5 <em>Thutmose</em>.
    /// </summary>
    [Fact]
    public void The_observed_names_are_in_their_nations_pools()
    {
        var world = Load();

        var rome = world.Nations.Single(n => n.Id == "rome").LeaderNames!;
        foreach (var expected in new[] { "Appius Claudius", "Antiochus", "Publius Scipio", "Licinus Crassus" })
            Assert.Contains(expected, rome);

        var gaul = world.Nations.Single(n => n.Id == "gaul").LeaderNames!;
        foreach (var expected in new[] { "Hengest", "Arminius", "Brennus", "Horsa" })
            Assert.Contains(expected, gaul);

        Assert.Equal("Agis", world.Nations.Single(n => n.Id == "carthage").LeaderNames![6]);
        Assert.Equal("Thutmose", world.Nations.Single(n => n.Id == "ptolemaic").LeaderNames![5]);
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

    /// <summary>
    /// T75 Done-when 3: the DAT's starting relation matrix -- Rome at war with Gaul, trading with
    /// Macedonia, and the confirmed 5 wars / 13 trades / 4 alliances / 0 cooldowns across all 120 pairs
    /// (decompiled-diplomacy-peace-terms-and-instant-battles.md's 2026-09-24 addition).
    /// </summary>
    [Fact]
    public void StartingRelations_has_the_DATs_confirmed_matrix()
    {
        var world = Load();
        var relations = world.StartingRelations;
        Assert.NotNull(relations);

        // The exported world's own diplomacy state codes (its ruleset ships with the same values,
        // 0/1/2/3 -- decompiled-diplomacy-peace-terms-and-instant-battles.md's relation-matrix table).
        var war = 3;
        var trade = 1;
        var alliance = 2;
        var peace = 0;

        Assert.Equal(war, relations!.Get("rome", "gaul"));
        Assert.Equal(trade, relations.Get("rome", "macedonia"));

        var wars = 0;
        var trades = 0;
        var alliances = 0;
        var cooldowns = 0;
        for (var i = 0; i < relations.NationIds.Count; i++)
        {
            for (var j = i + 1; j < relations.NationIds.Count; j++)
            {
                var value = relations.Matrix[i][j];
                if (value == war)
                {
                    wars++;
                }
                else if (value == trade)
                {
                    trades++;
                }
                else if (value == alliance)
                {
                    alliances++;
                }
                else if (value != peace)
                {
                    cooldowns++;
                }
            }
        }

        Assert.Equal(5, wars);
        Assert.Equal(13, trades);
        Assert.Equal(4, alliances);
        Assert.Equal(0, cooldowns);
    }

    /// <summary>T75 Done-when 2 (well-formedness half): the exported matrix is symmetric with a zero diagonal.</summary>
    [Fact]
    public void StartingRelations_is_symmetric_with_a_zero_diagonal()
    {
        var relations = Load().StartingRelations;
        Assert.NotNull(relations);
        Assert.True(relations!.IsWellFormed());

        for (var i = 0; i < relations.NationIds.Count; i++)
        {
            Assert.Equal(0, relations.Matrix[i][i]);
        }
    }

    /// <summary>
    /// T75 Done-when 3: slots 0 and 26 of the news seed, by their exact bytes -- byte-for-byte, not
    /// merely string-equal, per the task's own "Verbatim means byte for byte" hazard.
    /// </summary>
    [Fact]
    public void StartingNews_slots_0_and_26_match_the_DAT_byte_for_byte()
    {
        var news = Load().StartingNews;
        Assert.NotNull(news);
        Assert.Equal(27, news!.Slots.Count);
        Assert.Equal(26, news.MostRecentSlot);

        Assert.Equal(
            new byte[] { (byte)'2', (byte)'7', (byte)'2', (byte)' ', (byte)'B', (byte)'C' },
            Encoding.ASCII.GetBytes(news.Slots[0].Text));

        Assert.Equal(
            Encoding.ASCII.GetBytes("Week 1      Spring      270 BC"),
            Encoding.ASCII.GetBytes(news.Slots[26].Text));
    }

    /// <summary>
    /// T75 Hazards: "slot 12 and slot 25 are a single space" -- not trimmed away to an empty string.
    /// </summary>
    [Fact]
    public void StartingNews_slots_12_and_25_are_a_single_space_not_trimmed()
    {
        var news = Load().StartingNews;
        Assert.NotNull(news);
        Assert.Equal(" ", news!.Slots[12].Text);
        Assert.Equal(" ", news.Slots[25].Text);
        Assert.Equal(1, news.Slots[12].Text.Length);
        Assert.Equal(1, news.Slots[25].Text.Length);
    }

    /// <summary>T75 Done-when 3: both new fields carry a <c>_provenance</c> note citing the reports above.</summary>
    [Fact]
    public void StartingRelations_and_StartingNews_have_provenance()
    {
        var world = Load();
        var relationsSource = world.Provenance?.SourceFor("startingRelations");
        var newsSource = world.Provenance?.SourceFor("startingNews");

        Assert.False(string.IsNullOrEmpty(relationsSource));
        Assert.Contains("decompiled-diplomacy-peace-terms-and-instant-battles.md", relationsSource, StringComparison.Ordinal);

        Assert.False(string.IsNullOrEmpty(newsSource));
        Assert.Contains("news-log-format-and-messages.md", newsSource, StringComparison.Ordinal);
    }

    // ---- T85 Done-when 3: startingNeighbours, loaded from the DAT's own +0x2B mask ----

    /// <summary>
    /// The DAT's own 24 neighbour pairs, transcribed verbatim from <c>dat-neighbour-mask.md</c> §2's own
    /// table (nation-record <c>+0x2B</c>) -- the same list
    /// <c>NeighbourGeographyTests.DatConfirmedPairs</c> pins for the AI-facing query. Checked here
    /// directly against the exported <c>startingNeighbours</c> field itself, not through
    /// <see cref="IC2.Engine.Diplomacy.NeighbourGeography"/>.
    /// </summary>
    private static readonly (string A, string B)[] DatConfirmedNeighbourPairs =
    {
        ("rome", "carthage"), ("rome", "gaul"), ("rome", "illyria"),
        ("carthage", "ptolemaic"), ("carthage", "numidia"), ("carthage", "celtiberia"),
        ("seleucid", "ptolemaic"), ("seleucid", "bithynia"), ("seleucid", "galatia"),
        ("seleucid", "armenia"), ("seleucid", "media"),
        ("macedonia", "greece"), ("macedonia", "illyria"), ("macedonia", "dacia"), ("macedonia", "thracia"),
        ("gaul", "celtiberia"), ("gaul", "illyria"), ("gaul", "dacia"),
        ("greece", "illyria"), ("illyria", "dacia"), ("dacia", "thracia"),
        ("bithynia", "galatia"), ("bithynia", "armenia"), ("armenia", "media"),
    };

    /// <summary>
    /// The six pairs the T82 geometric derivation adds that the DAT's own mask does not have
    /// (<c>dat-neighbour-mask.md</c> §6) -- must not appear in the exported field.
    /// </summary>
    private static readonly (string A, string B)[] DerivedOnlyPairs =
    {
        ("rome", "greece"),
        ("thracia", "bithynia"), ("thracia", "seleucid"),
        ("seleucid", "macedonia"), ("seleucid", "greece"),
        ("ptolemaic", "greece"),
    };

    private static bool AreNeighbours(World world, string a, string b)
    {
        var neighbours = world.StartingNeighbours!;
        var forward = neighbours.FirstOrDefault(e => e.NationId == a);
        return forward is not null && forward.NeighbourIds.Contains(b);
    }

    [Fact]
    public void StartingNeighbours_is_present_and_has_one_entry_per_nation()
    {
        var world = Load();
        Assert.NotNull(world.StartingNeighbours);
        Assert.Equal(16, world.StartingNeighbours!.Count);
        Assert.Equal(
            world.Nations.Select(n => n.Id).ToArray(),
            world.StartingNeighbours.Select(e => e.NationId).ToArray());
    }

    [Fact]
    public void StartingNeighbours_has_exactly_the_dats_24_pairs()
    {
        var world = Load();

        foreach (var (a, b) in DatConfirmedNeighbourPairs)
        {
            Assert.True(AreNeighbours(world, a, b), $"'{a}' should border '{b}'.");
            Assert.True(AreNeighbours(world, b, a), "The relation must be symmetric.");
        }

        var totalPairs = world.StartingNeighbours!.Sum(e => e.NeighbourIds.Count) / 2;
        Assert.Equal(24, totalPairs);
    }

    /// <summary>None of the T82 derivation's six extra pairs is present -- Rome-Greece in particular.</summary>
    [Fact]
    public void StartingNeighbours_does_not_have_any_of_the_six_derived_only_pairs()
    {
        var world = Load();

        foreach (var (a, b) in DerivedOnlyPairs)
        {
            Assert.False(AreNeighbours(world, a, b), $"'{a}' should not border '{b}' -- that pair is derivation-only.");
            Assert.False(AreNeighbours(world, b, a), $"'{b}' should not border '{a}' -- that pair is derivation-only.");
        }
    }

    [Fact]
    public void StartingNeighbours_romes_row_is_carthage_gaul_illyria()
    {
        var world = Load();
        var rome = world.StartingNeighbours!.First(e => e.NationId == "rome");

        Assert.Equal(new[] { "carthage", "gaul", "illyria" }, rome.NeighbourIds);
    }

    [Fact]
    public void StartingNeighbours_has_provenance_citing_the_report()
    {
        var world = Load();
        var source = world.Provenance?.SourceFor("startingNeighbours");

        Assert.False(string.IsNullOrEmpty(source));
        Assert.Contains("dat-neighbour-mask.md", source, StringComparison.Ordinal);
    }
}
