using System;
using System.Buffers.Binary;
using System.IO;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// Bug #675: an original save written during a tactical battle sets the 55-byte trailer's battle flag
/// (<c>+54</c>) to 1 and writes the 2,105-byte battle block <b>after</b> the trailer — so the trailer
/// starts at <c>length − 55 − 2,105</c>, not <c>length − 55</c>. <see cref="SaveTurnState"/>,
/// <see cref="SaveNewsLog"/> and <see cref="SavePendingOffer"/> must read such a file exactly as the
/// same save without the block. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-battle-probe.md
/// item 2 and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md.
/// </summary>
public class MidBattleSaveTests
{
    /// <summary>9 header bytes + 40 × 44 slot bytes + a 14 × 12 grid of 2-byte entries.</summary>
    private const int BattleBlockLength = 2105;

    /// <summary>The block's trailing icon grid: 14 × 12 little-endian 16-bit words, 336 bytes.</summary>
    private const int IconGridLength = 14 * 12 * 2;

    /// <summary>The value a real block writes in an empty icon-grid cell: the research layout says 50
    /// ("empty"), every non-empty entry at most 34 — so every word's <b>high</b> byte is 0 and the
    /// file's last byte is 0 on every real mid-battle save. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-decompiled-tactical-battle-rules.md
    /// §1 and its <c>scripts/battle-block-check.py</c>.</summary>
    private const ushort EmptyIconCell = 50;

    private static readonly ushort[] Order =
        { 4, 2, 7, 9, 8, 15, 13, 11, 0, 5, 14, 1, 6, 10, 3, 12 };

    /// <summary>A complete, structurally valid synthetic SAV (empty armies/fleets, a 3-slot news log
    /// and the calendar trailer) that never touches the original fixtures.</summary>
    private static byte[] SyntheticBaseSav()
    {
        var withFleets = SyntheticSaveBuilder.MinimalSavWithFleets(0, 0);
        var withNews = SyntheticSaveBuilder.AppendMercenaryTableAndNews(withFleets, 2, "One.", "Two.", "Three.");
        return SyntheticSaveBuilder.AppendTrailer(withNews, Order,
            turnOrderIndex: 3, currentNation: 9, week: 1, yearBc: 270, season: 0);
    }

    /// <summary>Returns <paramref name="baseSave"/> with the trailer's battle flag set to 1 and a
    /// <b>realistic</b> 2,105-byte battle block appended: its 14 × 12 icon grid is 168 little-endian
    /// words, all 50 (empty). Every real mid-battle save therefore ends with a grid word whose high
    /// byte is 0 — i.e. the file's last byte is 0, exactly like a no-block save's battle flag, so a
    /// locator that trusted that byte would mistake the block for a trailer (bug #675, review R1).</summary>
    private static byte[] WithBattleBlock(byte[] baseSave)
    {
        var midBattle = new byte[baseSave.Length + BattleBlockLength];
        Array.Copy(baseSave, midBattle, baseSave.Length);
        midBattle[baseSave.Length - 1] = 1; // trailer +54, the battle flag
        FillEmptyIconGrid(midBattle, blockStart: baseSave.Length);
        return midBattle;
    }

    /// <summary>Writes the block that starts at <paramref name="blockStart"/> as a real save writes
    /// it: the trailing 168-word icon grid holds the empty-cell value 50 in every cell.</summary>
    private static void FillEmptyIconGrid(byte[] data, int blockStart)
    {
        var gridStart = blockStart + BattleBlockLength - IconGridLength;
        for (var i = 0; i < IconGridLength / 2; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gridStart + i * 2, 2), EmptyIconCell);
    }

    /// <summary>Writes a complete, structurally valid 55-byte trailer at <paramref name="offset"/>
    /// (<see cref="Order"/>, nation 9 at index 3, week 1, year 270 BC, spring) with the given battle
    /// flag at <c>+54</c>.</summary>
    private static void WriteTrailerAt(byte[] data, int offset, byte battleFlag)
    {
        for (var i = 0; i < Order.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + i * 2, 2), Order[i]);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 36, 2), 9); // current nation
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 38, 2), 3); // turn-order index
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 40, 2), 1); // week
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 42, 2), 270); // year BC
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 44, 2), 0); // season
        data[offset + 54] = battleFlag;
    }

    [SkippableFact]
    public void On_the_configured_machine_a_real_mid_battle_save_reads_the_same_as_its_base()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var baseData = File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_rome_270_winter_11.sav"));
        var midBattle = WithBattleBlock(baseData);

        var expectedTurn = SaveTurnState.Parse(baseData);
        var expectedNews = SaveNewsLog.Parse(baseData);
        var expectedOffer = SavePendingOffer.Parse(baseData);

        var turn = SaveTurnState.Parse(midBattle);
        var news = SaveNewsLog.Parse(midBattle);
        var offer = SavePendingOffer.Parse(midBattle);

        Assert.Equal(expectedTurn.TurnOrder, turn.TurnOrder);
        Assert.Equal(expectedTurn.TurnOrderIndex, turn.TurnOrderIndex);
        Assert.Equal(expectedTurn.CurrentNationCode, turn.CurrentNationCode);
        Assert.Equal(expectedTurn.Week, turn.Week);
        Assert.Equal(expectedTurn.YearBc, turn.YearBc);
        Assert.Equal(expectedTurn.SeasonCode, turn.SeasonCode);

        Assert.Equal(expectedNews.NewestIndex, news.NewestIndex);
        Assert.Equal(expectedNews.Slots, news.Slots);

        Assert.Equal(expectedOffer.ProposingNationIndex, offer.ProposingNationIndex);
        Assert.Equal(expectedOffer.ProposedRelationState, offer.ProposedRelationState);
    }

    [Fact]
    public void A_synthetic_save_without_a_block_locates_the_trailer_at_length_minus_55()
    {
        var data = SyntheticBaseSav();

        var turn = SaveTurnState.Parse(data);
        var news = SaveNewsLog.Parse(data);
        var offer = SavePendingOffer.Parse(data);

        Assert.Equal(Order, turn.TurnOrder);
        Assert.Equal(2, news.NewestIndex);
        Assert.Equal(3, news.Slots.Count);
        // SyntheticSaveBuilder.AppendTrailer leaves the offer block zeroed, i.e. nation 0/state 0.
        Assert.Equal((ushort)0, offer.ProposingNationIndex);
        Assert.Equal((ushort)0, offer.ProposedRelationState);
    }

    [Fact]
    public void A_synthetic_mid_battle_save_locates_the_same_trailer_as_without_the_block()
    {
        var baseData = SyntheticBaseSav();
        var midBattle = WithBattleBlock(baseData);

        var expectedTurn = SaveTurnState.Parse(baseData);
        var turn = SaveTurnState.Parse(midBattle);
        var news = SaveNewsLog.Parse(midBattle);
        var offer = SavePendingOffer.Parse(midBattle);

        // The block's realistic empty grid leaves the last byte 0 — the same value a no-block
        // save's battle flag has, which is exactly what broke the old last-byte locator.
        Assert.Equal(BattleBlockLength, midBattle.Length - baseData.Length);
        Assert.Equal(0, midBattle[^1]);
        Assert.Equal(expectedTurn.TurnOrder, turn.TurnOrder);
        Assert.Equal(expectedTurn.TurnOrderIndex, turn.TurnOrderIndex);
        Assert.Equal(expectedTurn.CurrentNationCode, turn.CurrentNationCode);
        Assert.Equal(2, news.NewestIndex);
        Assert.Equal(SaveNewsLog.Parse(baseData).Slots, news.Slots);
        var expectedOffer = SavePendingOffer.Parse(baseData);
        Assert.Equal(expectedOffer.ProposingNationIndex, offer.ProposingNationIndex);
        Assert.Equal(expectedOffer.ProposedRelationState, offer.ProposedRelationState);
    }

    [Fact]
    public void A_block_free_save_whose_byte_at_length_minus_2106_is_one_still_locs_the_no_block_trailer()
    {
        // Review R2: a no-block save can by coincidence have the value 1 at the byte where a
        // mid-battle save's battle flag sits (length − 2,105 − 1). That single byte must not re-route
        // the file through the block shape: its structure at length − 2,105 − 2,105 is not a trailer,
        // so the real trailer is still the one at length − 55.
        var data = SyntheticBaseSav();
        data[data.Length - (BattleBlockLength + 1)] = 1;

        var turn = SaveTurnState.Parse(data);
        var news = SaveNewsLog.Parse(data);
        var offer = SavePendingOffer.Parse(data);

        Assert.Equal(Order, turn.TurnOrder);
        Assert.Equal((ushort)3, turn.TurnOrderIndex);
        Assert.Equal((ushort)9, turn.CurrentNationCode);
        Assert.Equal(2, news.NewestIndex);
        Assert.Equal(3, news.Slots.Count);
        Assert.Equal((ushort)0, offer.ProposingNationIndex);
        Assert.Equal((ushort)0, offer.ProposedRelationState);
    }

    [Fact]
    public void A_save_whose_last_byte_is_not_a_battle_flag_and_carries_no_block_is_rejected()
    {
        var data = SyntheticBaseSav();
        data[^1] = 7; // not the battle flag 0 of a no-block save, and no block follows

        var ex = Assert.Throws<InvalidDataException>(() => SaveTurnState.Parse(data));
        Assert.Contains("trailer", ex.Message);
    }

    // ---- direct synthetic tests of the shared locator (NO fixtures) ------------------------------

    [Fact]
    public void LocateTrailerStart_reads_the_last_55_bytes_when_the_battle_flag_is_zero()
    {
        var data = new byte[100];
        data[^1] = 0;

        Assert.Equal(45, SaveTurnState.LocateTrailerStart(data));
    }

    [Fact]
    public void LocateTrailerStart_skips_the_battle_block_when_the_battle_flag_is_one()
    {
        // A real mid-battle save: a valid trailer at length − 55 − 2,105 (flag 1) followed by the
        // block, whose realistic empty icon grid leaves the file's last byte 0 — the same value a
        // no-block save ends with. The flag byte alone cannot tell the two apart.
        var data = new byte[100 + BattleBlockLength];
        WriteTrailerAt(data, offset: 45, battleFlag: 1);
        FillEmptyIconGrid(data, blockStart: 45 + 55);

        Assert.Equal(45, SaveTurnState.LocateTrailerStart(data));
    }

    [Fact]
    public void LocateTrailerStart_rejects_a_file_whose_length_fits_neither_shape()
    {
        var data = new byte[100];
        data[^1] = 0x7F; // not the flag 0, and far too short to carry a 2,105-byte block

        var ex = Assert.Throws<InvalidDataException>(() => SaveTurnState.LocateTrailerStart(data));
        Assert.Contains("cannot be located", ex.Message);
    }
}
