using System;
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

    /// <summary>Returns <paramref name="baseSave"/> with the trailer's battle flag set to 1 and the
    /// 2,105-byte battle block appended. The block's last byte is deliberately non-zero (a populated
    /// 14 × 12 icon grid) so the last byte is not mistaken for a no-block battle flag of 0.</summary>
    private static byte[] WithBattleBlock(byte[] baseSave)
    {
        var midBattle = new byte[baseSave.Length + BattleBlockLength];
        Array.Copy(baseSave, midBattle, baseSave.Length);
        midBattle[baseSave.Length - 1] = 1; // trailer +54, the battle flag
        midBattle[^1] = 0x2A;
        return midBattle;
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

        Assert.Equal(BattleBlockLength, midBattle.Length - baseData.Length);
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
    public void A_save_whose_last_byte_is_not_a_battle_flag_and_carries_no_block_is_rejected()
    {
        var data = SyntheticBaseSav();
        data[^1] = 7; // not the battle flag 0 of a no-block save, and no block follows

        var ex = Assert.Throws<InvalidDataException>(() => SaveTurnState.Parse(data));
        Assert.Contains("trailer", ex.Message);
    }
}
