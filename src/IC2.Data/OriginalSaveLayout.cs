using System.Buffers.Binary;

namespace IC2.Data;

/// <summary>Complete strategic SAV framing, independently of the reader's partial format probe.
/// Evidence: research commit d5f0bc797ec23c181e854f743db1b06343e2876d,
/// decompiled-sav-file-layout.md (004484D0/004487C4, September correction),
/// news-log-format-and-messages.md, and 2026-10-04-decompiled-tactical-battle-rules.md Â§1.
/// The older report's claim that battle saves cannot exist is superseded.</summary>
public sealed class OriginalSaveLayout
{
    public const string ResearchCommit = "d5f0bc797ec23c181e854f743db1b06343e2876d";
    public int ArmyCount { get; }
    public int FleetCount { get; }
    public int NewsIndex { get; }
    public int Length { get; }
    public int NationTableOffset { get; }
    public int TrailerOffset { get; }
    public const string RegionPolicyResearchCommit = "3bda5a0244b98e837e55269efe4eb552edfc4a65";
    public const string TaxResearchCommit = "51c98f1f8903822af1aca3bf2d876047bf1b7a8b";
    public IReadOnlyList<OriginalSaveRegion> Regions { get; }

    private OriginalSaveLayout(int armies, int fleets, int news, int length, int nations, int trailer, List<OriginalSaveRegion> regions)
    {
        ArmyCount = armies; FleetCount = fleets; NewsIndex = news; Length = length;
        Regions = regions.AsReadOnly();
        NationTableOffset = nations; TrailerOffset = trailer;
    }

    /// <summary>Checks the entire byte sequence. No battle block, trailing byte or DAT fallback.</summary>
    public static OriginalSaveLayout Validate(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var regions = new List<OriginalSaveRegion>();
        var cursor = 0;
        void Add(string name, int length)
        {
            if (length < 0 || length > data.Length - cursor)
                throw new InvalidDataException($"SAV ends within {name} at 0x{cursor:X}, requiring {length} bytes.");
            if (length > 0) regions.Add(new(name, cursor, length));
            cursor = checked(cursor + length);
        }
        int Word(string name, bool signed = false)
        {
            var start = cursor;
            Add(name, 2);
            return signed ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(start, 2))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(start, 2));
        }
        Add("map", 89600);
        for (var i = 0; i < 334; i++) Add($"city[{i}]", 34);
        var armies = Word("army count");
        for (var i = 0; i < armies; i++) Add($"army[{i}]", 656);
        var fleets = Word("fleet count");
        for (var i = 0; i < fleets; i++) Add($"fleet[{i}]", 26);
        var nations = cursor;
        // Exhaustive nation partition. Names identify structure, not a permission to preserve
        // live bytes after state changes. dat-file-layout.md explicitly leaves runtime gaps.
        (string Name, int Length)[] nationParts =
        [ ("name",11), ("leader",27), ("relations",32), ("neighbours",2),
          ("live owned-city list +048..2E3",668), ("recruitment",320), ("colours",12),
          ("wealth",4), ("historical population +434",4), ("treasury",4), ("historical treasury +43C",4),
          ("unity",2), ("mobilized",2), ("capital",2), ("city count",2),
          ("historical city count +448",2), ("tax",2), ("tax base",2), ("conqueror history +44E",2),
          ("UNKNOWN +450..46A",27), ("hints",1), ("area map",1), ("UNKNOWN +46D",1),
          ("geometry +46E..485",24), ("viewport",4), ("filter",2), ("delays",4),
          ("controller BYTE",1), ("UNKNOWN +491..493",3) ];
        for (var i = 0; i < 16; i++)
            foreach (var part in nationParts) Add($"nation[{i}].{part.Name}", part.Length);
        for (var i = 0; i < 50; i++) Add($"mercenary[{i}]", 12);
        var news = Word("news index", signed: true);
        if (news is < -1 or > 39) throw new InvalidDataException($"News index {news} outside -1..39.");
        for (var i = 0; i <= news; i++) Add($"news[{i}]", 61);
        var trailer = cursor;
        Add("turn order", 32); Add("pending offer", 4); Add("current nation", 2);
        Add("turn index", 2); Add("week", 2); Add("year BC", 2); Add("season", 2);
        Add("window geometry", 8); Add("battle flag", 1);
        if (data[cursor - 1] != 0) throw new InvalidDataException("Strategic template requires battle flag 0.");
        if (cursor != data.Length) throw new InvalidDataException("SAV has a battle block or unaccounted trailing bytes.");
        if (SaveTurnState.LocateTrailerStart(data) != trailer)
            throw new InvalidDataException("Ambiguous or incompatible SAV trailer.");
        // Validate actual bytes using production decoders as an additional check, never as the
        // complete-byte coverage authority. Opaque bytes have no general changed-state defaults. The bounded tax profile also
        // preserves them exactly. The nation reader WORD +490/+491 remains an additional
        // conservative restriction (bug 1212); never zero opaque +491 to pass it.
        WorldPrefix.Parse(data); SaveArmyTable.Parse(data); SaveFleetTable.Parse(data);
        SaveNationTable.Parse(data); SaveRecruitmentTable.Parse(data); SaveMercenaryTable.Parse(data);
        SaveNewsLog.Parse(data); SaveTurnState.Parse(data); SavePendingOffer.Parse(data);
        return new(armies, fleets, news, cursor, nations, trailer, regions);
    }
}

/// <summary>One non-overlapping logical region; offsets are derived anew for each template.</summary>
public sealed record OriginalSaveRegion(string Name, int Offset, int Length);
