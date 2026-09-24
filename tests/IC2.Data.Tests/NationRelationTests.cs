using System.IO;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T73 Done-when lines 1–2 (bug #321/#322): the 16-entry signed relation row at SAV nation-record
/// <c>+0x26</c> / DAT nation-record <c>+0x0B</c>, and the leader field narrowed to 27 bytes so it no
/// longer overlaps the row. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
/// §"The relation matrix" and its 2026-09-24 addition.
///
/// These are synthetic-only (no <see cref="LocalAssets"/> gate): they exercise
/// <see cref="SaveNationTable"/>'s own parsing and validation logic against bytes this suite
/// constructs, the same way <see cref="SyntheticSaveBuilder"/>'s existing consumers do.
/// </summary>
public class NationRelationTests
{
    [Fact]
    public void War_trade_alliance_and_cooldown_all_parse()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        // Rome (0) at war with Gaul (6), trade with Macedonia (4), allied with Illyria (9), and a
        // cooldown (-8, the trade-broken-off value FUN_00449B40 writes) toward Numidia (5).
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 6, 3);
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 4, 1);
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 9, 2);
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 5, -8);

        var nations = SaveNationTable.Parse(data).Nations;

        Assert.Equal((short)3, nations[0].Relations[6]);
        Assert.Equal((short)3, nations[6].Relations[0]);
        Assert.Equal((short)1, nations[0].Relations[4]);
        Assert.Equal((short)2, nations[0].Relations[9]);
        Assert.Equal((short)-8, nations[0].Relations[5]);
        // Every other entry (both directions) is still the default, untouched peace (0).
        Assert.Equal((short)0, nations[0].Relations[1]);
        Assert.Equal((short)0, nations[1].Relations[0]);
    }

    [Fact]
    public void A_deep_cooldown_at_the_documented_floor_parses()
    {
        // -24: the alliance-broken-off cooldown FUN_00449B40 writes — the most negative value any
        // documented writer produces, and SaveNationTable.MinRelationValue's own floor.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.SetSymmetricRelation(data, 2, 11, SaveNationTable.MinRelationValue);

        var nations = SaveNationTable.Parse(data).Nations;

        Assert.Equal(SaveNationTable.MinRelationValue, nations[2].Relations[11]);
        Assert.Equal(SaveNationTable.MinRelationValue, nations[11].Relations[2]);
    }

    [Fact]
    public void Nonzero_diagonal_is_rejected()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.WriteRelationEntry(data, 3, 3, 1);

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("diagonal", ex.Message);
    }

    [Fact]
    public void Asymmetric_entries_are_rejected()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        // Write only [a][b], not the reciprocal [b][a] — never possible through the real setter
        // (FUN_00449B40 always writes both), so this is a genuinely malformed file.
        SyntheticSaveBuilder.WriteRelationEntry(data, 1, 8, 3);

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("symmetric", ex.Message);
    }

    [Fact]
    public void A_value_above_war_is_rejected()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 1, (short)(SaveNationTable.MaxRelationValue + 1));

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("outside", ex.Message);
    }

    [Fact]
    public void A_cooldown_below_the_documented_floor_is_rejected()
    {
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 1, (short)(SaveNationTable.MinRelationValue - 1));

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("outside", ex.Message);
    }

    [Fact]
    public void Leader_narrowed_to_27_bytes_does_not_overlap_the_relation_row()
    {
        // A leader that fills all 27 bytes with NO NUL, immediately followed by a non-zero relation
        // row (Done-when line 2's own scenario): the leader must read back as exactly those 27
        // characters, and the relation row bytes must not have been consumed as leader text.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        var leaderBytes = System.Text.Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRSTUVWXYZ1"); // 27 chars
        SyntheticSaveBuilder.WriteNationLeaderBytes(data, 0, leaderBytes);
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 6, 3);

        var rome = SaveNationTable.Parse(data).Nations[0];

        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ1", rome.Leader);
        Assert.Equal((short)3, rome.Relations[6]);
    }
}
