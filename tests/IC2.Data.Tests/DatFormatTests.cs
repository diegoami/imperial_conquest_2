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
        var data = File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_thracia_271_spring_1.sav"));

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
        // T34 #40 item 5: pinned to exactly Assert.Null, not the looser IsNullOrEmpty — the DAT's
        // contract is "absent" (null), not merely "falsy", so a future change that started returning
        // "" instead of null must fail this test.
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var table = SaveNationTable.Parse(data);

        Assert.All(table.Nations, nation => Assert.Null(nation.Leader));
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
        var data = File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_thracia_271_spring_1.sav"));

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
        // The DAT length match is a FALLBACK, tried only once the SAV structural walk fails (see
        // SaveFormat.Detect's remarks) — so this garbage content is filled with 0xFF words rather
        // than left zeroed: an all-zero 140,706-byte array would read as a (degenerate but
        // structurally valid) empty SAV, armies=0, fleets=0, which is a genuinely different, correct
        // outcome, not a bug. 0xFFFF as the word at the SAV army-count offset drives the SAV walk's
        // computed fleet-count offset past the end of the file, so it reliably fails and falls
        // through to the DAT length match.
        var garbageDatShaped = new byte[SaveFormat.DatFileLength];
        Array.Fill(garbageDatShaped, (byte)0xFF);
        Assert.Equal(SaveFileFormat.Dat, SaveFormat.Detect(garbageDatShaped));
    }

    [Fact]
    public void An_all_zero_file_the_exact_dat_length_is_a_degenerate_but_valid_empty_sav()
    {
        // The flip side of the test above, spelled out explicitly: zero bytes everywhere means
        // armyCount = 0 and fleetCount = 0, which is a structurally valid (if empty) SAV shape, and
        // the SAV structural check is authoritative whenever it succeeds — even at exactly the DAT's
        // own length. Neither this test nor the one above is "the" right answer in isolation; between
        // them they pin that Detect() genuinely prefers the structural check over a bare length match.
        var allZero = new byte[SaveFormat.DatFileLength];
        Assert.Equal(SaveFileFormat.Sav, SaveFormat.Detect(allZero));
    }

    [Fact]
    public void A_real_sized_sav_padded_to_the_dats_exact_length_is_still_detected_and_parsed_as_sav()
    {
        // Regression for the length-first misclassification found in review: a real, structurally
        // valid SAV that happens to be exactly SaveFormat.DatFileLength bytes (here: padded, which
        // does not disturb the count-word walk — see SyntheticSaveBuilder.PadTo) must still be
        // classified and parsed as a SAV, not silently misread at DAT offsets.
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 5, y: 9, owner: 2)));
        var padded = SyntheticSaveBuilder.PadTo(data, SaveFormat.DatFileLength);
        Assert.Equal(SaveFormat.DatFileLength, padded.Length);

        Assert.Equal(SaveFileFormat.Sav, SaveFormat.Detect(padded));

        var table = SaveArmyTable.Parse(padded);
        var army = Assert.Single(table.Armies);
        Assert.Equal((ushort)5, army.X);
        Assert.Equal((ushort)9, army.Y);
        Assert.Equal((ushort)2, army.OwnerCode);

        var fleets = SaveFleetTable.Parse(padded);
        Assert.Empty(fleets.Fleets);
    }

    // ---- T34 #40 item 2: once SaveArmyTable.cs's and SaveFleetTable.cs's now-unreachable SAV-branch
    // guards are removed, a malformed SAV must still be rejected — via SaveFormat.Detect, which every
    // parser calls first — with the SAME typed error, not some other exception each guard used to
    // throw locally. ----

    public static IEnumerable<object[]> EveryFormatDetectingParser()
    {
        yield return new object[] { "SaveArmyTable", (Action<byte[]>)(d => SaveArmyTable.Parse(d)) };
        yield return new object[] { "SaveFleetTable", (Action<byte[]>)(d => SaveFleetTable.Parse(d)) };
        yield return new object[] { "SaveNationTable", (Action<byte[]>)(d => SaveNationTable.Parse(d)) };
        yield return new object[] { "SaveRecruitmentTable", (Action<byte[]>)(d => SaveRecruitmentTable.Parse(d)) };
        yield return new object[] { "SaveMercenaryTable", (Action<byte[]>)(d => SaveMercenaryTable.Parse(d)) };
        yield return new object[] { "SaveTurnState", (Action<byte[]>)(d => SaveTurnState.Parse(d)) };
    }

    [Theory]
    [MemberData(nameof(EveryFormatDetectingParser))]
    public void A_malformed_sav_is_rejected_by_every_parser_with_the_same_typed_error(
        string parserName, Action<byte[]> parse)
    {
        var garbage = new byte[12345];
        var ex = Assert.Throws<UnrecognizedSaveFormatException>(() => parse(garbage));
        Assert.True(ex.Message.Contains("140706"),
            $"{parserName}: expected the 140,706-byte DAT length in the message, got '{ex.Message}'.");
    }
}
