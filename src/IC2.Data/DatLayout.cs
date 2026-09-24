using System;

namespace IC2.Data;

/// <summary>
/// The DAT's own fixed record layout — offsets and lengths decompiled from the New Game loader
/// <c>FUN_004481a0</c> (<c>0x004481A0</c>), cited in full in docs/investigations/dat-file-layout.md.
/// Every constant here is either a literal straight from that file's read-order table (or its
/// "Mapped onto the DAT record's own offsets" paragraph for the six labelled nation fields), or the
/// straightforward running sum of the ordered field lengths in that same table — never recovered by
/// searching a SAV for matching values (see docs/build-orchestration-plan.md's T30 hazard note: a
/// byte-search that lands on the same numbers is a [derived] result dressed as a [confirmed] one).
/// </summary>
internal static class DatLayout
{
    // ---- Army table: DAT offset 0x18A5C, 15 x 656-byte records, no count word ----
    // 0x18A5C == WorldPrefix.SharedPrefixLength (100,956 = 89,600 map + 11,356 city table): the army
    // table starts immediately after the shared map+city prefix, exactly as in a SAV, just without a
    // leading count word.
    internal const int ArmyTableStart = WorldPrefix.SharedPrefixLength;
    internal const int ArmyRecordCount = 15; // DAT_004a0324 = 0xf, assigned by the loader itself

    // ---- Fleet table: DAT offset 0x1B0CC, 2 x 26-byte records, no count word ----
    internal const int FleetTableStart = 0x1B0CC;
    internal const int FleetRecordCount = 2; // DAT_004a0326 = 2, assigned by the loader itself

    // ---- Nation table: DAT offset 0x1B100, 16 x 1,055-byte records ----
    internal const int NationTableStart = 0x1B100;
    internal const int NationRecordLength = 1055;

    // Field offsets within one 1,055-byte DAT nation record. Treasury/unity/mobilized/capital/
    // cities/tax are cited verbatim: "Mapped onto the DAT record's own offsets: treasury +0x40d,
    // unity +0x411, mobilized +0x413, capital +0x415, cities +0x417, tax +0x419." Name is the read
    // order table's first row (11 bytes at the record's start in both formats). The recruitment
    // queue's DAT-local offset is not separately spelled out in hex, but is the running sum of the
    // read order table's preceding row lengths, exactly as the 1,055-byte record total itself is
    // computed in that table: 11 (name) + 32 + 2 + 668 (three unlabelled reads) = 713 = 0x2C9,
    // immediately followed by the 320-byte recruitment queue, ending at 1,033 (0x409). That is
    // where the next unlabelled 4-byte field begins — NOT "one byte before" it, as an earlier
    // version of this comment said (an off-by-one in the prose, T34 #40 item 3; the constant below
    // was always correct): the recruitment queue's own end offset IS the next field's start offset,
    // with no gap between them. 1,033 + 4 = 1,037 = 0x40D, treasury's offset, which checks out
    // exactly. That 4-byte field is now identified: it is "wealth", confirmed at DAT +0x409 (the
    // same field as the SAV's in-memory +0x430) in
    // https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md.
    // Continuing the same running sum past tax (+0x419, 2 bytes, ending at 1,051 = 0x41B): the next
    // unlabelled 2-byte field is the signed tax base, confirmed directly (not just derived from the
    // running sum) at DAT +0x41b by the same report — matching the SAV's in-memory +0x44c.
    internal const int NationNameOffset = 0x000;
    internal const int NationNameLength = 11;
    internal const int NationRecruitmentOffset = 0x2C9; // 713 decimal
    internal const int NationWealthOffset = 0x409; // 1,033 decimal
    internal const int NationTreasuryOffset = 0x40D;
    internal const int NationUnityOffset = 0x411;
    internal const int NationMobilizedOffset = 0x413;
    internal const int NationCapitalOffset = 0x415;
    internal const int NationCitiesOffset = 0x417;
    internal const int NationTaxOffset = 0x419;
    internal const int NationTaxBaseOffset = 0x41B; // 1,051 decimal

    // ---- The nation's 16-entry relation row (T73, bug #321/#322) ----
    // "The row is at SAV nation-record +0x26, the same offset as at runtime... The DAT loader
    // FUN_004481A0 reads each nation's 11-byte name, then 32 bytes straight into runtime +0x26:
    // Read(rec, 0xb) then Read(rec + 0x26, 0x20)... The DAT record has no leader field, so on disk
    // the row is at DAT nation-record +0x0B" —
    // https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
    // §"The relation matrix", 2026-09-24 addition. This is also the read-order table's second row
    // (32 bytes, immediately after the 11-byte name), consistent with the running sum above.
    internal const int NationRelationOffset = 0x00B; // 11 decimal == NationNameOffset + NationNameLength

    // ---- The news log's DAT seed: the file's last 2,440 bytes (T73, bug #321) ----
    // "The DAT loader reads the last 2,440 bytes of the DAT (0x21C1A) straight into all 40 slots
    // (news_log_decomp.txt line 230: Read(&DAT_0049f994, 0x988))... FUN_00448AA4 then sets
    // newsIndex = 0x1A (line 30)." — the DAT itself stores only the 40 slots, never the index; see
    // https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
    // §Q1, "The DAT seeds the log, and a new game starts at index 26". The slot count itself is
    // SaveNewsLog.MaxSlotCount — the SAV's own bound (newsIndex -1..39) and the DAT's slot count are
    // the same "40", so there is one constant for it, not two (T73 review round 1, N6).
    internal const int NewsSeedLength = SaveNewsLog.MaxSlotCount * SaveNewsLog.SlotLength; // 2,440
}

/// <summary>Thrown when code asks a parser for something the DAT genuinely does not store — a whole
/// table (mercenary pool, calendar trailer) or a single nation-record field (leader name,
/// human-player flag) that is New Game state the original assigns only once play actually starts
/// (<c>TPremierForm_NewGame</c>'s second helper, <c>FUN_00448aa4</c> — see
/// docs/investigations/dat-file-layout.md). Distinguishes "absent by construction" from a genuinely
/// malformed record, which still throws <see cref="System.IO.InvalidDataException"/>.</summary>
public sealed class DatDataNotPresentException : InvalidOperationException
{
    public DatDataNotPresentException(string message) : base(message)
    {
    }
}
