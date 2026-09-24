using System.IO;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T73 Done-when line 3 (bug #321): the saved turn order (trailer <c>+0</c>, 16 x int16) and its
/// index (trailer <c>+38</c>), on top of <see cref="SaveTurnState"/>'s existing calendar fields. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md,
/// the 2026-09-14 correction.
/// </summary>
public class SaveTurnOrderTests
{
    private static readonly ushort[] Identity =
        { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    [Fact]
    public void A_shuffled_turn_order_and_its_index_parse()
    {
        // A permutation of 0..15 with nation 9 (Illyria) at index 3, whose turn it currently is.
        ushort[] order = { 4, 2, 7, 9, 8, 15, 13, 11, 0, 5, 14, 1, 6, 10, 3, 12 };
        var data = SyntheticSaveBuilder.AppendTrailer(
            SyntheticSaveBuilder.MinimalSavWithFleets(0, 0),
            order, turnOrderIndex: 3, currentNation: 9, week: 1, yearBc: 270, season: 0);

        var turn = SaveTurnState.Parse(data);

        Assert.Equal(order, turn.TurnOrder);
        Assert.Equal((ushort)3, turn.TurnOrderIndex);
        Assert.Equal((ushort)9, turn.CurrentNationCode);
    }

    [Fact]
    public void On_the_configured_machine_thracia_is_at_index_5_of_16_in_the_named_save()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(FixtureResolver.ResolveOrThrow("1_thracia_271_spring_3.sav"));

        var turn = SaveTurnState.Parse(data);

        Assert.Equal((ushort)5, turn.TurnOrderIndex);
        Assert.Equal((ushort)15, turn.TurnOrder[5]);
        Assert.Equal("Thracia", NationCatalog.Name(turn.TurnOrder[5]));
        Assert.Equal(turn.CurrentNationCode, turn.TurnOrder[turn.TurnOrderIndex]);
    }

    [Fact]
    public void A_turn_order_that_is_not_a_permutation_is_rejected()
    {
        // Nation 0 appears twice (indices 0 and 1); nation 15 never appears.
        ushort[] order = { 0, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 1 };
        var data = SyntheticSaveBuilder.AppendTrailer(
            SyntheticSaveBuilder.MinimalSavWithFleets(0, 0),
            order, turnOrderIndex: 0, currentNation: 0, week: 1, yearBc: 270, season: 0);

        var ex = Assert.Throws<InvalidDataException>(() => SaveTurnState.Parse(data));
        Assert.Contains("permutation", ex.Message);
    }

    [Fact]
    public void A_turn_order_index_above_15_is_rejected()
    {
        var data = SyntheticSaveBuilder.AppendTrailer(
            SyntheticSaveBuilder.MinimalSavWithFleets(0, 0),
            Identity, turnOrderIndex: 16, currentNation: 0, week: 1, yearBc: 270, season: 0);

        var ex = Assert.Throws<InvalidDataException>(() => SaveTurnState.Parse(data));
        Assert.Contains("turn-order index", ex.Message);
    }

    [Fact]
    public void A_turn_order_index_pointing_at_the_wrong_nation_is_rejected()
    {
        // Identity order, index 3 (nation 3), but CurrentNationCode says nation 5.
        var data = SyntheticSaveBuilder.AppendTrailer(
            SyntheticSaveBuilder.MinimalSavWithFleets(0, 0),
            Identity, turnOrderIndex: 3, currentNation: 5, week: 1, yearBc: 270, season: 0);

        var ex = Assert.Throws<InvalidDataException>(() => SaveTurnState.Parse(data));
        Assert.Contains("current nation", ex.Message);
    }

    [Fact]
    public void The_dat_has_no_trailer_at_all()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(LocalAssets.Settings!.DatPath);

        Assert.Throws<DatDataNotPresentException>(() => SaveTurnState.Parse(data));
    }
}
