using System.IO;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T73 Done-when line 4 (bug #321): the news log between the mercenary table and the 55-byte
/// trailer on a SAV, and the DAT's 40-slot seed. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
/// §Q1.
/// </summary>
public class SaveNewsLogTests
{
    private static byte[] BuildSav(short newsIndex, params string[] slots)
    {
        var withNations = SyntheticSaveBuilder.MinimalSavWithFleets(0, 0);
        var withNews = SyntheticSaveBuilder.AppendMercenaryTableAndNews(withNations, newsIndex, slots);
        return SyntheticSaveBuilder.AppendTrailer(withNews,
            turnOrder: new ushort[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            turnOrderIndex: 0, currentNation: 0, week: 1, yearBc: 270, season: 0);
    }

    [Fact]
    public void An_empty_log_has_index_minus_one_and_no_slots()
    {
        var data = BuildSav(-1);

        var log = SaveNewsLog.Parse(data);

        Assert.Equal(-1, log.NewestIndex);
        Assert.Empty(log.Slots);
    }

    [Fact]
    public void A_partial_log_returns_its_slots_oldest_first()
    {
        var data = BuildSav(2, "Rome destroys army of Greece.", " ", "Week  3      Spring      270BC");

        var log = SaveNewsLog.Parse(data);

        Assert.Equal(2, log.NewestIndex);
        Assert.Equal(3, log.Slots.Count);
        Assert.Equal("Rome destroys army of Greece.", log.Slots[0]);
        Assert.Equal(" ", log.Slots[1]);
        Assert.Equal("Week  3      Spring      270BC", log.Slots[2]);
    }

    [Fact]
    public void A_full_40_slot_log_parses_every_slot()
    {
        var slots = new string[40];
        for (var i = 0; i < slots.Length; i++)
            slots[i] = $"Entry {i}.";
        var data = BuildSav(39, slots);

        var log = SaveNewsLog.Parse(data);

        Assert.Equal(39, log.NewestIndex);
        Assert.Equal(40, log.Slots.Count);
        Assert.Equal("Entry 0.", log.Slots[0]);
        Assert.Equal("Entry 39.", log.Slots[39]);
    }

    [Fact]
    public void A_single_space_entry_survives_untrimmed()
    {
        var data = BuildSav(0, " ");

        var log = SaveNewsLog.Parse(data);

        Assert.Equal(" ", log.Slots[0]);
    }

    [Fact]
    public void A_layout_that_does_not_account_for_the_file_length_is_rejected()
    {
        // Build a log for index 2 (3 slots), then drop the last byte so the slots run one byte
        // short of the 55-byte trailer's start — the file length no longer checks out exactly.
        var data = BuildSav(2, "One.", "Two.", "Three.");
        var truncated = new byte[data.Length - 1];
        System.Array.Copy(data, truncated, truncated.Length);
        // Re-append a valid-looking trailer tail is not needed: SaveTurnState is not under test
        // here, and SaveNewsLog.Parse computes the trailer boundary purely from data.Length.

        var ex = Assert.Throws<InvalidDataException>(() => SaveNewsLog.Parse(truncated));
        Assert.Contains("does not account for the file length", ex.Message);
    }

    [Fact]
    public void A_slot_with_no_nul_within_61_bytes_is_rejected()
    {
        // AppendMercenaryTableAndNews itself refuses a slot text long enough to leave no room for a
        // NUL terminator, so the malformed slot is built directly instead.
        var withNations = SyntheticSaveBuilder.MinimalSavWithFleets(0, 0);
        var withNews = SyntheticSaveBuilder.AppendMercenaryTableAndNews(withNations, 0, "placeholder");
        var slotStart = withNations.Length + SaveMercenaryTable.RecordCount * SaveMercenaryTable.RecordLength + 2;
        for (var i = 0; i < SaveNewsLog.SlotLength; i++)
            withNews[slotStart + i] = (byte)'x'; // fill the whole slot with non-NUL bytes
        var full = SyntheticSaveBuilder.AppendTrailer(withNews,
            turnOrder: new ushort[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            turnOrderIndex: 0, currentNation: 0, week: 1, yearBc: 270, season: 0);

        var ex = Assert.Throws<InvalidDataException>(() => SaveNewsLog.Parse(full));
        Assert.Contains("no NUL", ex.Message);
    }

    [Fact]
    public void A_byte_outside_the_printable_ascii_range_is_rejected()
    {
        var withNations = SyntheticSaveBuilder.MinimalSavWithFleets(0, 0);
        var withNews = SyntheticSaveBuilder.AppendMercenaryTableAndNews(withNations, 0, "placeholder");
        var slotStart = withNations.Length + SaveMercenaryTable.RecordCount * SaveMercenaryTable.RecordLength + 2;
        withNews[slotStart] = 0x01; // control byte, outside 0x20-0x7E, before the slot's own NUL
        withNews[slotStart + 1] = 0; // keep a NUL in range so the "no NUL" check isn't what fires
        var full = SyntheticSaveBuilder.AppendTrailer(withNews,
            turnOrder: new ushort[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            turnOrderIndex: 0, currentNation: 0, week: 1, yearBc: 270, season: 0);

        var ex = Assert.Throws<InvalidDataException>(() => SaveNewsLog.Parse(full));
        Assert.Contains("0x20-0x7E", ex.Message);
    }

    [Fact]
    public void On_the_configured_machine_the_dat_seed_matches_the_report_and_slots_27_to_39_are_empty()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        var log = SaveNewsLog.Parse(data);

        Assert.Equal(40, log.Slots.Count);
        Assert.Throws<DatDataNotPresentException>(() => log.NewestIndex);
        // The report's quoted slot 0 ("272 BC") and slot 26 ("Week 1      Spring      270 BC") — if
        // the bytes disagree with the report's spacing, the bytes win (task brief); this asserts
        // against the file's own bytes, not a re-typed guess.
        Assert.Equal("272 BC", log.Slots[0]);
        Assert.Equal("Week 1      Spring      270 BC", log.Slots[26]);
        for (var i = 27; i <= 39; i++)
            Assert.Equal("", log.Slots[i]);
    }
}
