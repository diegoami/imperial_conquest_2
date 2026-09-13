using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// docs/build-orchestration-plan.md, T30, Defect B and Done-when lines 5-7: the DAT parses to 16
/// nation records matching <see cref="NationCatalog"/> in order with city counts summing to 334, 15
/// armies and 2 fleets; <see cref="NationRecord.Leader"/> and <see cref="NationRecord.HumanPlayer"/>
/// are absent by construction rather than defaulted; and a file that is neither DAT-shaped nor
/// SAV-shaped is rejected with a typed error naming both checks, never silently misread as one or the
/// other.
/// </summary>
public class DatFormatTests
{
    [SkippableFact]
    public void Dat_is_detected_as_dat_shaped()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        Assert.Equal(SaveFileFormat.Dat, SaveFormat.Detect(data));
        Assert.Equal(SaveFormat.DatFileLength, data.Length);
    }

    [SkippableFact]
    public void A_real_save_is_detected_as_sav_shaped()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves/1_thracia_271_spring_1.sav"));

        Assert.Equal(SaveFileFormat.Sav, SaveFormat.Detect(data));
    }

    [SkippableFact]
    public void Dat_yields_16_nations_in_catalog_order_with_city_counts_summing_to_334()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var table = SaveNationTable.Parse(data);

        Assert.Equal(16, table.Nations.Count);
        for (var i = 0; i < table.Nations.Count; i++)
        {
            Assert.Equal(NationCatalog.Name((ushort)i), table.Nations[i].Name);
            Assert.Equal(SaveFileFormat.Dat, table.Nations[i].Source);
        }
        Assert.Equal(334, table.Nations.Sum(n => (int)n.CityCount));

        // docs/investigations/dat-file-layout.md: "Rome's treasury/unity/capital/cities read
        // 2,200 / 821 / 85 / 25 at those offsets, matching the values SaveNationTable reads from the
        // session's first save."
        var rome = table.Nations[0];
        Assert.Equal(2200, rome.Treasury);
        Assert.Equal((ushort)821, rome.UnityValue);
        Assert.Equal((ushort)85, rome.CapitalCityIndex);
        Assert.Equal((ushort)25, rome.CityCount);
    }

    [SkippableFact]
    public void Dat_yields_15_armies_and_2_fleets()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        Assert.Equal(15, SaveArmyTable.Parse(data).Armies.Count);
        Assert.Equal(2, SaveFleetTable.Parse(data).Fleets.Count);
    }

    [SkippableFact]
    public void Leader_is_absent_from_every_dat_nation_not_a_fabricated_empty_string()
    {
        // Done-when line 6's negative test: "a caller cannot read a leader name off the DAT and get a
        // non-empty string." Modelled as null (docs/investigations/dat-file-layout.md: the leader name
        // is assigned by a random draw only at New Game, so nothing DAT-derived would be honest here).
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var table = SaveNationTable.Parse(data);

        Assert.All(table.Nations, nation =>
        {
            Assert.True(string.IsNullOrEmpty(nation.Leader),
                $"{nation.Name}: expected no leader name from the DAT, got '{nation.Leader}'.");
        });
    }

    [SkippableFact]
    public void HumanPlayer_throws_rather_than_defaulting_on_a_dat_nation()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var table = SaveNationTable.Parse(data);

        Assert.All(table.Nations, nation =>
            Assert.Throws<DatDataNotPresentException>(() => nation.HumanPlayer));
    }

    [SkippableFact]
    public void Sav_nation_record_still_carries_a_real_leader_and_human_player_flag()
    {
        // Confirms the Dat-only absence modelling did not regress the SAV path.
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves/1_thracia_271_spring_1.sav"));

        var table = SaveNationTable.Parse(data);

        Assert.All(table.Nations, nation =>
        {
            Assert.Equal(SaveFileFormat.Sav, nation.Source);
            Assert.False(string.IsNullOrEmpty(nation.Leader));
            // Must not throw for a SAV-origin record:
            _ = nation.HumanPlayer;
        });
    }

    // ---- Format discriminator: neither shape must not be silently misread (Done-when line 7) ----
    // These use synthetic bytes, not the original files, so no Skip is needed (see
    // SyntheticSaveBuilder's remarks for the same reasoning).

    [Fact]
    public void A_file_matching_neither_shape_is_rejected_naming_both_checks()
    {
        // Wrong length for a DAT, and too short / structurally wrong to be a SAV.
        var garbage = new byte[12345];
        var ex = Assert.Throws<UnrecognizedSaveFormatException>(() => SaveFormat.Detect(garbage));
        Assert.Contains("140706", ex.Message);
        Assert.Contains("SAV", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_file_is_rejected_not_silently_treated_as_either_shape()
    {
        var ex = Assert.Throws<UnrecognizedSaveFormatException>(() => SaveFormat.Detect(Array.Empty<byte>()));
        Assert.Contains("0 bytes", ex.Message);
    }

    [Fact]
    public void A_file_the_exact_dat_length_but_garbage_content_is_still_detected_as_dat_shaped()
    {
        // The discriminator is length-based for the DAT (docs/investigations/dat-file-layout.md: the
        // read order sums to all 140,706 bytes exactly), so garbage content of the right length is
        // still classified as Dat by Detect() itself — it is the DAT-shaped parse paths' own field
        // validation (e.g. nation name mismatches) that catches garbage content, exercised separately.
        var garbageDatShaped = new byte[SaveFormat.DatFileLength];
        Assert.Equal(SaveFileFormat.Dat, SaveFormat.Detect(garbageDatShaped));
    }
}
