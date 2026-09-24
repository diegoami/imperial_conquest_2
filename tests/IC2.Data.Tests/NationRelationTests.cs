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
        // -24, written as a literal (T73 review round 1, N2) so this pins the actual cited value —
        // the alliance-broken-off cooldown FUN_00449B40 writes, the most negative value any
        // documented writer produces — rather than whatever SaveNationTable.MinRelationValue happens
        // to hold if that constant is ever mutated.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.SetSymmetricRelation(data, 2, 11, -24);

        var nations = SaveNationTable.Parse(data).Nations;

        Assert.Equal((short)-24, nations[2].Relations[11]);
        Assert.Equal((short)-24, nations[11].Relations[2]);
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
        // 4, a literal one past war (3) — T73 review round 1, N2: written as a literal, not
        // MaxRelationValue + 1, so this pins the cited upper bound instead of tracking a mutated
        // constant.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 1, 4);

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("outside", ex.Message);
    }

    [Fact]
    public void A_cooldown_below_the_documented_floor_is_rejected()
    {
        // -25, a literal one past the documented floor (-24) — same reasoning as the test above.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        SyntheticSaveBuilder.SetSymmetricRelation(data, 0, 1, -25);

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("outside", ex.Message);
    }

    [Fact]
    public void Leader_narrowed_to_27_bytes_does_not_overlap_the_relation_row()
    {
        // A leader that fills all 27 bytes with NO NUL, immediately followed by a non-zero relation
        // row (Done-when line 2's own scenario): the leader must read back as exactly those 27
        // characters, and the relation row bytes must not have been consumed as leader text.
        //
        // T73 review round 1, B1: this must NOT use nation 0. Nation 0's own diagonal entry
        // Relations[0] is always 0 (every nation's diagonal is 0), so a read that runs past +0x26
        // still finds a NUL right there and returns the same 27 characters — a 34-byte mutation
        // stays green. Nation 1's relation TOWARD nation 0 (Relations[0], the row's first entry, at
        // +0x26) is set to 3 (war) instead, so the byte immediately after the 27-byte leader is
        // non-zero: a read past 27 bytes pulls that byte (0x03, outside 0x20-0x7E) into the leader
        // and throws, which is what actually distinguishes 27 from 34.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        var leaderBytes = System.Text.Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRSTUVWXYZ1"); // 27 chars
        SyntheticSaveBuilder.WriteNationLeaderBytes(data, 1, leaderBytes);
        SyntheticSaveBuilder.SetSymmetricRelation(data, 1, 0, 3);

        var nation = SaveNationTable.Parse(data).Nations[1];

        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ1", nation.Leader);
        Assert.Equal((short)3, nation.Relations[0]);
    }

    [Fact]
    public void An_empty_leader_is_rejected()
    {
        // T73 review round 2, B3: ReadLeader's own doc comment (and MinimalSavWithNations') claims
        // an empty leader "is still rejected, exactly as ReadName rejects an empty name", but nothing
        // visited that edge — deleting the rejection at SaveNationTable.cs left every existing test
        // green. Nation record leader offset is +0x0B (SaveNationTable.cs, the ParseSav leader-read
        // comment); zeroing that byte makes the very first leader byte a NUL, i.e. an empty leader.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        data[SyntheticSaveBuilder.NationRecordOffset(data, 0) + 0x0B] = 0;

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("Missing nation text", ex.Message);
    }

    [Fact]
    public void An_empty_name_is_rejected()
    {
        // T73 review round 2's separate report: ReadName's own empty-name rejection predates this
        // PR but lives in this task's Owns file (SaveNationTable.cs) and was likewise never visited —
        // deleting it alongside the leader check above left every existing test green too. Nation
        // record name offset is +0x00; zeroing that byte makes the very first name byte a NUL.
        var data = SyntheticSaveBuilder.MinimalSavWithNations();
        data[SyntheticSaveBuilder.NationRecordOffset(data, 0)] = 0;

        var ex = Assert.Throws<InvalidDataException>(() => SaveNationTable.Parse(data));
        Assert.Contains("Missing nation text", ex.Message);
    }
}
