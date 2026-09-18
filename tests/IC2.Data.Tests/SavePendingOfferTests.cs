using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T34 Done-when line 6: the pending diplomatic-offer block at <c>fileLength − 22</c>, confirmed in
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/pending-offer-block-army-split-and-naupactus.md.
/// </summary>
public class SavePendingOfferTests
{
    [SkippableFact]
    public void No_offer_pending_reads_the_none_sentinel()
    {
        // `1_rome_270_winter_9.sav` | `FF FF 01 00` | -1 · 1
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves-processed/1_rome_270_winter_9.sav"));

        var offer = SavePendingOffer.Parse(data);

        Assert.False(offer.HasOffer);
        Assert.Equal(SavePendingOffer.NoOfferSentinel, offer.ProposingNationIndex);
    }

    [SkippableFact]
    public void Greece_offers_trade_to_rome_in_winter_9_b()
    {
        // `1_rome_270_winter_9_b.sav` | `07 00 01 00` | 7 · 1 | "Greece wants to trade with Rome"
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves-processed/1_rome_270_winter_9_b.sav"));

        var offer = SavePendingOffer.Parse(data);

        Assert.True(offer.HasOffer);
        Assert.Equal((ushort)7, offer.ProposingNationIndex);
        Assert.Equal("Greece", NationCatalog.Name(offer.ProposingNationIndex));
        Assert.Equal((ushort)1, offer.ProposedRelationState);
    }

    [SkippableFact]
    public void Bithynia_offers_trade_to_rome_in_winter_11()
    {
        // `1_rome_270_winter_11.sav` | `0B 00 01 00` | 11 · 1 | "Bythinia wants to trade with Rome"
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var data = File.ReadAllBytes(settings.ResolveSavePath("saves/1_rome_270_winter_11.sav"));

        var offer = SavePendingOffer.Parse(data);

        Assert.True(offer.HasOffer);
        Assert.Equal((ushort)11, offer.ProposingNationIndex);
        Assert.Equal("Bithynia", NationCatalog.Name(offer.ProposingNationIndex));
        Assert.Equal((ushort)1, offer.ProposedRelationState);
    }

    [SkippableFact]
    public void Dat_has_no_pending_offer_block()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        Assert.Throws<DatDataNotPresentException>(() => SavePendingOffer.Parse(data));
    }
}
