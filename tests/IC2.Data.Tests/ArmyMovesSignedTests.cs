using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T44 (issue #126): <c>ArmyRecord.Moves</c> (word +6) is a **signed** 16-bit field, not a <c>ushort</c>
/// — see the field's own doc comment and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-moves-field-signed-and-the-ffff-underflow.md.
/// Decoding it unsigned turns the one real save's underflowed <c>-1</c> into <c>65535</c>. This is not
/// a sentinel: no code anywhere compares this field against <c>-1</c>/<c>0xFFFF</c>, so the fix is
/// simply to read the bit pattern as signed, exactly as the original does.
/// </summary>
public class ArmyMovesSignedTests
{
    // ---- Real save: the one corpus record that actually underflowed (Done-when line 4) ----
    // Ptolemaic army 9 at (192, 96), Summer 270 week 7. Confirmed present, byte-identical in this
    // respect, in all four save files that record this same save state.

    [SkippableTheory]
    [InlineData("11.sav")]
    [InlineData("11_ptol.sav")]
    [InlineData("11_supply.sav")]
    [InlineData("1_rome_270_summer_7.sav")]
    public void Ptolemaic_army_9_reads_negative_one_not_65535(string fixtureName)
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(FixtureResolver.ResolveOrThrow(fixtureName));

        var table = SaveArmyTable.Parse(data);
        var army = table.Armies.Single(a => a.Index == 9);

        Assert.Equal((ushort)3, army.OwnerCode); // Ptolemaic
        Assert.Equal((ushort)192, army.X);
        Assert.Equal((ushort)96, army.Y);
        Assert.Equal((short)-1, army.Moves);
        Assert.True(army.IsFrozen);
        // coveredCell is 2 (live on-map army), not the aboard-fleet sentinel — the report's own point
        // that this is a live army frozen by the underflow, not one legitimately parked off-map.
        Assert.Equal((ushort)2, army.CoveredCell);
        Assert.False(army.IsAboardFleet);
    }

    // ---- Same saves, an ordinary (positive-moves) army: a fix that flattens both is caught ----
    // Rome's army at (100, 42), 48,173 troops. The report's own worked example: formula
    // 10 - min(5, 48173/20000) = 10 - 2 = 8, exactly what is stored.

    [SkippableTheory]
    [InlineData("11.sav")]
    [InlineData("11_ptol.sav")]
    [InlineData("11_supply.sav")]
    [InlineData("1_rome_270_summer_7.sav")]
    public void Romes_army_in_the_same_save_keeps_its_ordinary_positive_moves(string fixtureName)
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var data = File.ReadAllBytes(FixtureResolver.ResolveOrThrow(fixtureName));

        var table = SaveArmyTable.Parse(data);
        var army = table.Armies.Single(a => a.Index == 0);

        Assert.Equal((ushort)0, army.OwnerCode); // Rome
        Assert.Equal((ushort)100, army.X);
        Assert.Equal((ushort)42, army.Y);
        Assert.Equal((short)8, army.Moves);
        Assert.False(army.IsFrozen);
    }

    // ---- Synthetic: the decode itself, independent of any real save (no assets needed) ----

    [Fact]
    public void Synthetic_0xFFFF_bit_pattern_decodes_as_negative_one_not_65535()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 1, y: 1, owner: 2, moves: -1)));

        var army = SaveArmyTable.Parse(data).Armies.Single();

        Assert.Equal((short)-1, army.Moves);
        Assert.True(army.IsFrozen);
    }

    [Fact]
    public void Synthetic_ordinary_positive_moves_is_not_flagged_frozen()
    {
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 1, y: 1, owner: 2, moves: 8)));

        var army = SaveArmyTable.Parse(data).Armies.Single();

        Assert.Equal((short)8, army.Moves);
        Assert.False(army.IsFrozen);
    }

    [Fact]
    public void Synthetic_zero_moves_is_not_flagged_frozen()
    {
        // Zero is the ordinary "spent all moves this week" state, reached by many legitimate write
        // sites (report's write-site table) — must not be conflated with the negative/frozen state.
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 1, y: 1, owner: 2, moves: 0)));

        var army = SaveArmyTable.Parse(data).Armies.Single();

        Assert.Equal((short)0, army.Moves);
        Assert.False(army.IsFrozen);
    }

    [Fact]
    public void A_negative_moves_record_is_not_treated_as_a_parse_failure()
    {
        // Hazard: "Do not treat a negative moves as a parse failure." A record otherwise valid but
        // with moves == -1 must parse cleanly, exactly like any other legitimate army.
        var data = SyntheticSaveBuilder.MinimalSav(1,
            (0, (d, off) => SyntheticSaveBuilder.WriteArmyHeader(d, off, x: 5, y: 5, owner: 1, moves: -1)));

        var table = SaveArmyTable.Parse(data);

        Assert.Single(table.Armies);
        Assert.Empty(table.SkippedRecords);
    }
}
