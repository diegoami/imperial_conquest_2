namespace IC2.Data;

/// <summary>
/// T184: the complete strategic original SAV as typed values — every byte of the file has a field here,
/// except the bytes the canonical encoding defines (text tails, empty unit slots, unused city-list entries,
/// the battle flag) and the 31 INERT nation bytes, which are zero unless carried from an authentic source.
/// </summary>
/// <remarks>
/// <para>Field names and offsets follow the SAV semantic atlas ledger (research `f83a886`, ledger v13 of
/// ic2-conquest's <c>field-map</c>) and the gate-2 writer policy (research <c>f878614</c>,
/// <c>2026-10-22-sav-export-gate2-policy.md</c>). Integer fields hold the value the file stores; the
/// encoder checks each against its width (no narrowing wrap) before anything is written.</para>
/// <para>This layer carries no domain meaning: the engine-side exporter (T185) supplies the values, and
/// <see cref="OriginalSaveExportPolicy"/> checks the cross-record invariants the original relies on.</para>
/// </remarks>
public sealed record OriginalSaveDocument(
    IReadOnlyList<short> Map,
    IReadOnlyList<OriginalCity> Cities,
    IReadOnlyList<OriginalArmy> Armies,
    IReadOnlyList<OriginalFleet> Fleets,
    IReadOnlyList<OriginalNation> Nations,
    IReadOnlyList<OriginalMercenary> Mercenaries,
    IReadOnlyList<string> News,
    OriginalTrailer Trailer)
{
    /// <summary>320 columns × 140 rows; cell (x, y) is at index <c>x × 140 + y</c>.</summary>
    public const int MapWidth = 320, MapHeight = 140, MapCells = MapWidth * MapHeight;
    public const int CityCount = 334, NationCount = 16, MercenaryCount = 50, NewsCapacity = 40;
    public const int UnitSlotCount = 20, RecruitCount = 40;

    /// <summary>The map cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public short CellAt(int x, int y) => Map[x * MapHeight + y];
}

/// <summary>A 34-byte city record. <paramref name="Name"/> holds at most 13 ASCII characters.</summary>
public sealed record OriginalCity(
    string Name, int X, int Y, int Owner, int Allegiance, int Loyalty, int Supplies, int Fort,
    int Population, int MaxPopulation, int Tribute);

/// <summary>A 656-byte army record: the 16-byte header, then <paramref name="Units"/> packed from slot 0
/// (each with troops &gt; 0); the encoder zeroes the remaining slots.</summary>
public sealed record OriginalArmy(
    int X, int Y, int Owner, int Moves, int Cell, int Supplies, int Money, int Morale,
    IReadOnlyList<OriginalUnitSlot> Units);

/// <summary>A 32-byte unit slot. <paramref name="Name"/> holds at most 23 ASCII characters.</summary>
public sealed record OriginalUnitSlot(int Label, int Type, int Troops, int Quality, string Name);

/// <summary>A 26-byte fleet record. <paramref name="CityOrCondition"/> is the build city while
/// <paramref name="Countdown"/> ≥ 0, the condition once launched (−1); <paramref name="Army"/> is the
/// carried army's index or −1.</summary>
public sealed record OriginalFleet(
    int X, int Y, int RouteX, int RouteY, int Owner, int Countdown, int Moves, int Supplies, int Money,
    int Ships, int CityOrCondition, int Army, int Cell);

/// <summary>One 8-byte recruitment entry of a nation (troops 0 = unused; its other words are kept).</summary>
public sealed record OriginalRecruitEntry(int State, int Type, int Troops, int City);

/// <summary>A 1,172-byte nation record.</summary>
/// <param name="Name">At most 10 ASCII characters.</param>
/// <param name="Leader">At most 26 ASCII characters.</param>
/// <param name="Relations">The 16 relation words (+0x026).</param>
/// <param name="Neighbours">The unsigned neighbour word (+0x046).</param>
/// <param name="CityList">The owned cities in city-name byte order (+0x048); the encoder writes −1 after
/// them, up to 334 entries.</param>
/// <param name="Recruits">Exactly 40 recruitment entries (+0x2E4).</param>
/// <param name="Colours">The three colour dwords (+0x424).</param>
/// <param name="Controller">The controller byte (+0x490): 1 human.</param>
/// <param name="Inert">The 31 INERT bytes carried from an authentic source, or null for zeros (the value a
/// fresh original New Game holds).</param>
public sealed record OriginalNation(
    string Name, string Leader, IReadOnlyList<int> Relations, int Neighbours, IReadOnlyList<int> CityList,
    IReadOnlyList<OriginalRecruitEntry> Recruits, IReadOnlyList<uint> Colours,
    int Wealth, int Wealth0, int Treasury, int Treasury0, int Unity, int Mobilization, int Capital,
    int Cities, int Cities0, int Tax, int TaxBase, int Conqueror,
    OriginalNationUi Ui, int Controller, OriginalInertBytes? Inert = null);

/// <summary>A nation's interface settings (+0x46B..0x48F, less the INERT +0x46D): presentation only.</summary>
public sealed record OriginalNationUi(
    int Hints, int AreaMapToggle, IReadOnlyList<int> AreaWindow, IReadOnlyList<int> UnitWindow,
    IReadOnlyList<int> InfoWindow, int ViewY, int ViewX, int MapFilter, int MeleeDelay, int ShootDelay);

/// <summary>The 31 INERT nation bytes: +0x450..0x46A (27), +0x46D (1), +0x491..0x493 (3).</summary>
public sealed record OriginalInertBytes(IReadOnlyList<byte> Block27, byte Byte46D, IReadOnlyList<byte> Block491);

/// <summary>A 12-byte mercenary slot (troops −1 marks an empty slot; its position is kept).</summary>
public sealed record OriginalMercenary(int X, int Y, int Label, int Type, int Troops, int Quality);

/// <summary>The 55-byte trailer, less the battle flag (always 0 in a strategic save).</summary>
public sealed record OriginalTrailer(
    IReadOnlyList<int> TurnOrder, int OfferFrom, int OfferType, int CurrentNation, int SeatIndex,
    int Week, int YearBc, int Season, int MainLeft, int MainTop, int MainClientHeight, int MainClientWidth);
