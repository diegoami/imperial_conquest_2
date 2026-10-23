using System.Buffers.Binary;

namespace IC2.Data;

/// <summary>
/// T184: decodes a strategic original SAV into an <see cref="OriginalSaveDocument"/> and encodes a
/// document into the canonical from-scratch byte form of the gate-2 writer policy.
/// </summary>
/// <remarks>
/// <para><strong>Canonical form</strong> (research <c>f878614</c>, <c>2026-10-22-sav-export-gate2-policy.md</c>):
/// text is NUL-terminated and then zero; unit slots are packed from slot 0 and every empty slot is zero;
/// a nation's city list is followed by −1 to its 334th entry; the INERT nation bytes are zero unless the
/// document carries them; the battle flag is 0 and no battle block follows. Every other byte is a typed
/// field of the document.</para>
/// <para><strong>Checked encoding.</strong> Every count, width, signedness and string is checked before the
/// output buffer is allocated: no narrowing wrap, no truncation, no lossy character replacement and no NUL
/// inside a string. A failure names the field and the value (<see cref="OriginalSaveEncodingException"/>).
/// Decoding is the inverse for the typed fields; residue the canonical form defines is not kept.</para>
/// </remarks>
public static class OriginalSaveCodec
{
    public const int CityRecordLength = 34, ArmyRecordLength = 656, UnitSlotLength = 32, FleetRecordLength = 26;
    public const int NationRecordLength = 1172, MercenaryRecordLength = 12, NewsRecordLength = 61, TrailerLength = 55;
    public const int CityNameBytes = 14, UnitNameBytes = 24, NationNameBytes = 11, LeaderBytes = 27;
    private const int MapBytes = OriginalSaveDocument.MapCells * 2;
    private const int CityTableOffset = MapBytes;
    private const int ArmyCountOffset = CityTableOffset + OriginalSaveDocument.CityCount * CityRecordLength;

    /// <summary>Decodes <paramref name="data"/>: a complete strategic SAV with no battle block.</summary>
    public static OriginalSaveDocument Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        OriginalSaveLayout.Validate(data);
        var r = new Reader(data);

        var map = new short[OriginalSaveDocument.MapCells];
        for (var i = 0; i < map.Length; i++) map[i] = r.I16(2 * i);

        var cities = new OriginalCity[OriginalSaveDocument.CityCount];
        for (var c = 0; c < cities.Length; c++)
        {
            var o = CityTableOffset + c * CityRecordLength;
            cities[c] = new OriginalCity(r.Text(o, CityNameBytes, $"city[{c}].name"),
                r.I16(o + 14), r.I16(o + 16), r.I16(o + 18), r.I16(o + 20), r.I16(o + 22), r.I16(o + 24),
                r.I16(o + 26), r.I16(o + 28), r.I16(o + 30), r.I16(o + 32));
        }

        var cursor = ArmyCountOffset;
        var armyCount = r.I16(cursor); cursor += 2;
        var armies = new OriginalArmy[armyCount];
        for (var a = 0; a < armyCount; a++, cursor += ArmyRecordLength)
        {
            var units = new List<OriginalUnitSlot>();
            var empty = false;
            for (var s = 0; s < OriginalSaveDocument.UnitSlotCount; s++)
            {
                var so = cursor + 16 + s * UnitSlotLength;
                var troops = r.I16(so + 4);
                if (troops <= 0) { empty = true; continue; }
                if (empty) throw new InvalidDataException($"army[{a}] slot {s} is live after an empty slot (unpacked).");
                units.Add(new OriginalUnitSlot(r.I16(so), r.I16(so + 2), troops, r.I16(so + 6),
                    r.Text(so + 8, UnitNameBytes, $"army[{a}].slot[{s}].name")));
            }

            armies[a] = new OriginalArmy(r.I16(cursor), r.I16(cursor + 2), r.I16(cursor + 4), r.I16(cursor + 6),
                r.I16(cursor + 8), r.I16(cursor + 10), r.I16(cursor + 12), r.I16(cursor + 14), units.AsReadOnly());
        }

        var fleetCount = r.I16(cursor); cursor += 2;
        var fleets = new OriginalFleet[fleetCount];
        for (var f = 0; f < fleetCount; f++, cursor += FleetRecordLength)
        {
            int W(int k) => r.I16(cursor + 2 * k);
            fleets[f] = new OriginalFleet(W(0), W(1), W(2), W(3), W(4), W(5), W(6), W(7), W(8), W(9), W(10), W(11), W(12));
        }

        var nations = new OriginalNation[OriginalSaveDocument.NationCount];
        for (var n = 0; n < nations.Length; n++, cursor += NationRecordLength)
        {
            var o = cursor;
            var relations = Words(r, o + 0x26, 16);
            var list = new List<int>();
            for (var e = 0; e < OriginalSaveDocument.CityCount; e++)
            {
                var v = r.I16(o + 0x48 + 2 * e);
                if (v == -1) break;
                list.Add(v);
            }

            var recruits = new OriginalRecruitEntry[OriginalSaveDocument.RecruitCount];
            for (var e = 0; e < recruits.Length; e++)
            {
                var ro = o + 0x2E4 + 8 * e;
                recruits[e] = new OriginalRecruitEntry(r.I16(ro), r.I16(ro + 2), r.I16(ro + 4), r.I16(ro + 6));
            }

            var colours = new[] { r.U32(o + 0x424), r.U32(o + 0x428), r.U32(o + 0x42C) };
            var ui = new OriginalNationUi(data[o + 0x46B], data[o + 0x46C], Words(r, o + 0x46E, 4), Words(r, o + 0x476, 4),
                Words(r, o + 0x47E, 4), r.I16(o + 0x486), r.I16(o + 0x488), r.I16(o + 0x48A), r.I16(o + 0x48C), r.I16(o + 0x48E));
            var inert = new OriginalInertBytes(data.AsSpan(o + 0x450, 27).ToArray(), data[o + 0x46D], data.AsSpan(o + 0x491, 3).ToArray());
            nations[n] = new OriginalNation(
                r.Text(o, NationNameBytes, $"nation[{n}].name"), r.Text(o + 0x0B, LeaderBytes, $"nation[{n}].leader"),
                relations, r.U16(o + 0x46), list.AsReadOnly(), recruits, colours,
                r.I32(o + 0x430), r.I32(o + 0x434), r.I32(o + 0x438), r.I32(o + 0x43C),
                r.I16(o + 0x440), r.I16(o + 0x442), r.I16(o + 0x444), r.I16(o + 0x446), r.I16(o + 0x448),
                r.I16(o + 0x44A), r.I16(o + 0x44C), r.I16(o + 0x44E), ui, data[o + 0x490], inert);
        }

        var mercenaries = new OriginalMercenary[OriginalSaveDocument.MercenaryCount];
        for (var m = 0; m < mercenaries.Length; m++, cursor += MercenaryRecordLength)
        {
            int W(int k) => r.I16(cursor + 2 * k);
            mercenaries[m] = new OriginalMercenary(W(0), W(1), W(2), W(3), W(4), W(5));
        }

        var newsIndex = r.I16(cursor); cursor += 2;
        var news = new string[newsIndex + 1];
        for (var i = 0; i < news.Length; i++, cursor += NewsRecordLength)
            news[i] = r.Text(cursor, NewsRecordLength, $"news[{i}]");

        var t = cursor;
        var trailer = new OriginalTrailer(Words(r, t, 16), r.I16(t + 32), r.I16(t + 34), r.I16(t + 36), r.I16(t + 38),
            r.I16(t + 40), r.I16(t + 42), r.I16(t + 44), r.I16(t + 46), r.I16(t + 48), r.I16(t + 50), r.I16(t + 52));
        return new OriginalSaveDocument(Array.AsReadOnly(map), cities, armies, fleets, nations, mercenaries, news, trailer);
    }

    /// <summary>Encodes <paramref name="document"/> in the canonical form. Throws
    /// <see cref="OriginalSaveEncodingException"/> naming the first field that cannot be represented.</summary>
    public static byte[] Encode(OriginalSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);

        var length = ArmyCountOffset + 2 + document.Armies.Count * ArmyRecordLength + 2
            + document.Fleets.Count * FleetRecordLength + OriginalSaveDocument.NationCount * NationRecordLength
            + OriginalSaveDocument.MercenaryCount * MercenaryRecordLength + 2 + document.News.Count * NewsRecordLength
            + TrailerLength;
        var w = new Writer(new byte[length]);

        for (var i = 0; i < OriginalSaveDocument.MapCells; i++) w.I16(2 * i, document.Map[i]);
        for (var c = 0; c < OriginalSaveDocument.CityCount; c++)
        {
            var city = document.Cities[c];
            var o = CityTableOffset + c * CityRecordLength;
            w.Text(o, city.Name);
            w.I16(o + 14, city.X); w.I16(o + 16, city.Y); w.I16(o + 18, city.Owner); w.I16(o + 20, city.Allegiance);
            w.I16(o + 22, city.Loyalty); w.I16(o + 24, city.Supplies); w.I16(o + 26, city.Fort);
            w.I16(o + 28, city.Population); w.I16(o + 30, city.MaxPopulation); w.I16(o + 32, city.Tribute);
        }

        var cursor = ArmyCountOffset;
        w.I16(cursor, document.Armies.Count); cursor += 2;
        foreach (var army in document.Armies)
        {
            w.I16(cursor, army.X); w.I16(cursor + 2, army.Y); w.I16(cursor + 4, army.Owner); w.I16(cursor + 6, army.Moves);
            w.I16(cursor + 8, army.Cell); w.I16(cursor + 10, army.Supplies); w.I16(cursor + 12, army.Money);
            w.I16(cursor + 14, army.Morale);
            for (var s = 0; s < army.Units.Count; s++)
            {
                var unit = army.Units[s];
                var so = cursor + 16 + s * UnitSlotLength;
                w.I16(so, unit.Label); w.I16(so + 2, unit.Type); w.I16(so + 4, unit.Troops); w.I16(so + 6, unit.Quality);
                w.Text(so + 8, unit.Name);
            }

            cursor += ArmyRecordLength;
        }

        w.I16(cursor, document.Fleets.Count); cursor += 2;
        foreach (var f in document.Fleets)
        {
            int[] words = [f.X, f.Y, f.RouteX, f.RouteY, f.Owner, f.Countdown, f.Moves, f.Supplies, f.Money, f.Ships, f.CityOrCondition, f.Army, f.Cell];
            for (var k = 0; k < words.Length; k++) w.I16(cursor + 2 * k, words[k]);
            cursor += FleetRecordLength;
        }

        foreach (var nation in document.Nations)
        {
            var o = cursor;
            w.Text(o, nation.Name); w.Text(o + 0x0B, nation.Leader);
            for (var k = 0; k < 16; k++) w.I16(o + 0x26 + 2 * k, nation.Relations[k]);
            w.U16(o + 0x46, nation.Neighbours);
            for (var e = 0; e < OriginalSaveDocument.CityCount; e++)
                w.I16(o + 0x48 + 2 * e, e < nation.CityList.Count ? nation.CityList[e] : -1);
            for (var e = 0; e < OriginalSaveDocument.RecruitCount; e++)
            {
                var entry = nation.Recruits[e];
                var ro = o + 0x2E4 + 8 * e;
                w.I16(ro, entry.State); w.I16(ro + 2, entry.Type); w.I16(ro + 4, entry.Troops); w.I16(ro + 6, entry.City);
            }

            for (var k = 0; k < 3; k++) w.U32(o + 0x424 + 4 * k, nation.Colours[k]);
            w.I32(o + 0x430, nation.Wealth); w.I32(o + 0x434, nation.Wealth0);
            w.I32(o + 0x438, nation.Treasury); w.I32(o + 0x43C, nation.Treasury0);
            w.I16(o + 0x440, nation.Unity); w.I16(o + 0x442, nation.Mobilization); w.I16(o + 0x444, nation.Capital);
            w.I16(o + 0x446, nation.Cities); w.I16(o + 0x448, nation.Cities0); w.I16(o + 0x44A, nation.Tax);
            w.I16(o + 0x44C, nation.TaxBase); w.I16(o + 0x44E, nation.Conqueror);
            var ui = nation.Ui;
            w.U8(o + 0x46B, ui.Hints); w.U8(o + 0x46C, ui.AreaMapToggle);
            for (var k = 0; k < 4; k++)
            {
                w.I16(o + 0x46E + 2 * k, ui.AreaWindow[k]);
                w.I16(o + 0x476 + 2 * k, ui.UnitWindow[k]);
                w.I16(o + 0x47E + 2 * k, ui.InfoWindow[k]);
            }

            w.I16(o + 0x486, ui.ViewY); w.I16(o + 0x488, ui.ViewX); w.I16(o + 0x48A, ui.MapFilter);
            w.I16(o + 0x48C, ui.MeleeDelay); w.I16(o + 0x48E, ui.ShootDelay);
            w.U8(o + 0x490, nation.Controller);
            if (nation.Inert is { } inert)
            {
                for (var k = 0; k < 27; k++) w.U8(o + 0x450 + k, inert.Block27[k]);
                w.U8(o + 0x46D, inert.Byte46D);
                for (var k = 0; k < 3; k++) w.U8(o + 0x491 + k, inert.Block491[k]);
            }

            cursor += NationRecordLength;
        }

        foreach (var m in document.Mercenaries)
        {
            int[] words = [m.X, m.Y, m.Label, m.Type, m.Troops, m.Quality];
            for (var k = 0; k < words.Length; k++) w.I16(cursor + 2 * k, words[k]);
            cursor += MercenaryRecordLength;
        }

        w.I16(cursor, document.News.Count - 1); cursor += 2;
        foreach (var line in document.News) { w.Text(cursor, line); cursor += NewsRecordLength; }

        var tr = document.Trailer;
        for (var k = 0; k < 16; k++) w.I16(cursor + 2 * k, tr.TurnOrder[k]);
        int[] tail = [tr.OfferFrom, tr.OfferType, tr.CurrentNation, tr.SeatIndex, tr.Week, tr.YearBc, tr.Season,
            tr.MainLeft, tr.MainTop, tr.MainClientHeight, tr.MainClientWidth];
        for (var k = 0; k < tail.Length; k++) w.I16(cursor + 32 + 2 * k, tail[k]);
        // +0x36: the battle flag, 0 (the buffer is zero).
        return w.Bytes;
    }

    /// <summary>Checks every count, width and string of <paramref name="d"/> without writing anything.</summary>
    public static void Validate(OriginalSaveDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        Count("map", d.Map, OriginalSaveDocument.MapCells);
        Count("cities", d.Cities, OriginalSaveDocument.CityCount);
        Count("nations", d.Nations, OriginalSaveDocument.NationCount);
        Count("mercenaries", d.Mercenaries, OriginalSaveDocument.MercenaryCount);
        if (d.Armies is null || d.Armies.Count > short.MaxValue) Fail("armies", d.Armies?.Count, "count outside 0..32767");
        if (d.Fleets is null || d.Fleets.Count > short.MaxValue) Fail("fleets", d.Fleets?.Count, "count outside 0..32767");
        if (d.News is null || d.News.Count > OriginalSaveDocument.NewsCapacity) Fail("news", d.News?.Count, "count outside 0..40");
        for (var c = 0; c < d.Cities.Count; c++)
        {
            var city = d.Cities[c] ?? throw Error($"city[{c}]", null, "missing");
            var p = $"city[{c}]";
            Text(p + ".name", city.Name, CityNameBytes, allowEmpty: false);
            I16(p + ".x", city.X); I16(p + ".y", city.Y); I16(p + ".owner", city.Owner); I16(p + ".allegiance", city.Allegiance);
            I16(p + ".loyalty", city.Loyalty); I16(p + ".supplies", city.Supplies); I16(p + ".fort", city.Fort);
            I16(p + ".population", city.Population); I16(p + ".maxPopulation", city.MaxPopulation); I16(p + ".tribute", city.Tribute);
        }

        for (var a = 0; a < d.Armies.Count; a++)
        {
            var army = d.Armies[a] ?? throw Error($"army[{a}]", null, "missing");
            var p = $"army[{a}]";
            I16(p + ".x", army.X); I16(p + ".y", army.Y); I16(p + ".owner", army.Owner); I16(p + ".moves", army.Moves);
            I16(p + ".cell", army.Cell); I16(p + ".supplies", army.Supplies); I16(p + ".money", army.Money); I16(p + ".morale", army.Morale);
            if (army.Units is null || army.Units.Count > OriginalSaveDocument.UnitSlotCount)
                Fail(p + ".units", army.Units?.Count, "count outside 0..20");
            for (var s = 0; s < army.Units!.Count; s++)
            {
                var unit = army.Units[s] ?? throw Error($"{p}.unit[{s}]", null, "missing");
                var q = $"{p}.unit[{s}]";
                I16(q + ".label", unit.Label); I16(q + ".type", unit.Type); I16(q + ".quality", unit.Quality);
                if (unit.Troops is < 1 or > short.MaxValue) Fail(q + ".troops", unit.Troops, "a listed unit needs 1..32767 troops");
                Text(q + ".name", unit.Name, UnitNameBytes, allowEmpty: false);
            }
        }

        for (var f = 0; f < d.Fleets.Count; f++)
        {
            var x = d.Fleets[f] ?? throw Error($"fleet[{f}]", null, "missing");
            var p = $"fleet[{f}]";
            (string, int)[] words = [("x", x.X), ("y", x.Y), ("routeX", x.RouteX), ("routeY", x.RouteY), ("owner", x.Owner),
                ("countdown", x.Countdown), ("moves", x.Moves), ("supplies", x.Supplies), ("money", x.Money), ("ships", x.Ships),
                ("cityOrCondition", x.CityOrCondition), ("army", x.Army), ("cell", x.Cell)];
            foreach (var (name, value) in words) I16($"{p}.{name}", value);
        }

        for (var n = 0; n < d.Nations.Count; n++)
        {
            var nation = d.Nations[n] ?? throw Error($"nation[{n}]", null, "missing");
            var p = $"nation[{n}]";
            Text(p + ".name", nation.Name, NationNameBytes, allowEmpty: false);
            Text(p + ".leader", nation.Leader, LeaderBytes, allowEmpty: true);
            Count(p + ".relations", nation.Relations, 16);
            for (var k = 0; k < 16; k++) I16($"{p}.relations[{k}]", nation.Relations[k]);
            if (nation.Neighbours is < 0 or > ushort.MaxValue) Fail(p + ".neighbours", nation.Neighbours, "outside 0..65535");
            if (nation.CityList is null || nation.CityList.Count > OriginalSaveDocument.CityCount)
                Fail(p + ".cityList", nation.CityList?.Count, "count outside 0..334");
            for (var e = 0; e < nation.CityList!.Count; e++)
                if (nation.CityList[e] is < 0 or >= OriginalSaveDocument.CityCount)
                    Fail($"{p}.cityList[{e}]", nation.CityList[e], "not a city index 0..333");
            Count(p + ".recruits", nation.Recruits, OriginalSaveDocument.RecruitCount);
            for (var e = 0; e < nation.Recruits.Count; e++)
            {
                var entry = nation.Recruits[e] ?? throw Error($"{p}.recruit[{e}]", null, "missing");
                var q = $"{p}.recruit[{e}]";
                I16(q + ".state", entry.State); I16(q + ".type", entry.Type); I16(q + ".troops", entry.Troops); I16(q + ".city", entry.City);
            }

            Count(p + ".colours", nation.Colours, 3);
            (string, int)[] words = [("unity", nation.Unity), ("mobilization", nation.Mobilization), ("capital", nation.Capital),
                ("cities", nation.Cities), ("cities0", nation.Cities0), ("tax", nation.Tax), ("taxBase", nation.TaxBase),
                ("conqueror", nation.Conqueror)];
            foreach (var (name, value) in words) I16($"{p}.{name}", value);
            var ui = nation.Ui ?? throw Error(p + ".ui", null, "missing");
            U8(p + ".ui.hints", ui.Hints); U8(p + ".ui.areaMapToggle", ui.AreaMapToggle);
            Count(p + ".ui.areaWindow", ui.AreaWindow, 4); Count(p + ".ui.unitWindow", ui.UnitWindow, 4); Count(p + ".ui.infoWindow", ui.InfoWindow, 4);
            for (var k = 0; k < 4; k++)
            {
                I16($"{p}.ui.areaWindow[{k}]", ui.AreaWindow[k]);
                I16($"{p}.ui.unitWindow[{k}]", ui.UnitWindow[k]);
                I16($"{p}.ui.infoWindow[{k}]", ui.InfoWindow[k]);
            }

            I16(p + ".ui.viewY", ui.ViewY); I16(p + ".ui.viewX", ui.ViewX); I16(p + ".ui.mapFilter", ui.MapFilter);
            I16(p + ".ui.meleeDelay", ui.MeleeDelay); I16(p + ".ui.shootDelay", ui.ShootDelay);
            // The original's human flag: 0 computer, 1 human. No other controller is encodable.
            if (nation.Controller is not (0 or 1)) Fail(p + ".controller", nation.Controller, "not 0 (computer) or 1 (human)");
            if (nation.Inert is { } inert)
            {
                Count(p + ".inert.block27", inert.Block27, 27);
                Count(p + ".inert.block491", inert.Block491, 3);
            }
        }

        for (var m = 0; m < d.Mercenaries.Count; m++)
        {
            var x = d.Mercenaries[m] ?? throw Error($"mercenary[{m}]", null, "missing");
            var p = $"mercenary[{m}]";
            I16(p + ".x", x.X); I16(p + ".y", x.Y); I16(p + ".label", x.Label); I16(p + ".type", x.Type);
            I16(p + ".troops", x.Troops); I16(p + ".quality", x.Quality);
        }

        for (var i = 0; i < d.News.Count; i++) Text($"news[{i}]", d.News[i], NewsRecordLength, allowEmpty: true);

        var t = d.Trailer ?? throw Error("trailer", null, "missing");
        Count("trailer.turnOrder", t.TurnOrder, 16);
        for (var k = 0; k < 16; k++) I16($"trailer.turnOrder[{k}]", t.TurnOrder[k]);
        (string, int)[] tail = [("offerFrom", t.OfferFrom), ("offerType", t.OfferType), ("currentNation", t.CurrentNation),
            ("seatIndex", t.SeatIndex), ("week", t.Week), ("yearBc", t.YearBc), ("season", t.Season), ("mainLeft", t.MainLeft),
            ("mainTop", t.MainTop), ("mainClientHeight", t.MainClientHeight), ("mainClientWidth", t.MainClientWidth)];
        foreach (var (name, value) in tail) I16($"trailer.{name}", value);
    }

    private static IReadOnlyList<int> Words(Reader r, int offset, int count)
    {
        var words = new int[count];
        for (var k = 0; k < count; k++) words[k] = r.I16(offset + 2 * k);
        return words;
    }

    private static void Count<T>(string field, IReadOnlyList<T>? list, int expected)
    {
        if (list is null || list.Count != expected) Fail(field, list?.Count, $"needs exactly {expected} entries");
    }

    private static void I16(string field, int value)
    {
        if (value is < short.MinValue or > short.MaxValue) Fail(field, value, "outside the signed 16-bit range");
    }

    private static void U8(string field, int value)
    {
        if (value is < 0 or > byte.MaxValue) Fail(field, value, "outside 0..255");
    }

    /// <summary>A NUL-terminated buffer of <paramref name="bytes"/>: at most bytes − 1 printable ASCII characters.</summary>
    private static void Text(string field, string? value, int bytes, bool allowEmpty)
    {
        if (value is null) Fail(field, null, "missing");
        if (!allowEmpty && value!.Length == 0) Fail(field, value, "empty");
        if (value!.Length > bytes - 1) Fail(field, value, $"longer than {bytes - 1} characters (no truncation)");
        foreach (var ch in value)
            if (ch is < ' ' or > '~') Fail(field, value, $"character U+{(int)ch:X4} is not printable ASCII (no replacement)");
    }

    private static void Fail(string field, object? value, string reason) => throw Error(field, value, reason);

    private static OriginalSaveEncodingException Error(string field, object? value, string reason) => new(field, value, reason);

    private readonly struct Reader(byte[] data)
    {
        public short I16(int o) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(o, 2));
        public int U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o, 2));
        public int I32(int o) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(o, 4));
        public uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o, 4));

        public string Text(int o, int length, string field)
        {
            var span = data.AsSpan(o, length);
            var end = span.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException($"{field}: no NUL terminator within {length} bytes.");
            var chars = new char[end];
            for (var i = 0; i < end; i++) chars[i] = (char)span[i];
            return new string(chars);
        }
    }

    private readonly struct Writer(byte[] bytes)
    {
        public byte[] Bytes => bytes;
        public void I16(int o, int v) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(o, 2), checked((short)v));
        public void U16(int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o, 2), checked((ushort)v));
        public void I32(int o, int v) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(o, 4), v);
        public void U32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(o, 4), v);
        public void U8(int o, int v) => bytes[o] = checked((byte)v);

        /// <summary>The characters, then zeros to the end of the buffer (already zero).</summary>
        public void Text(int o, string value)
        {
            for (var i = 0; i < value.Length; i++) bytes[o + i] = (byte)value[i];
        }
    }
}

/// <summary>A document value the original SAV cannot hold, named by its field path.</summary>
public sealed class OriginalSaveEncodingException(string field, object? value, string reason)
    : Exception($"{field}: {reason} (value {value ?? "null"}).")
{
    public string Field { get; } = field;
    public object? Value { get; } = value;
    public string Reason { get; } = reason;
}
