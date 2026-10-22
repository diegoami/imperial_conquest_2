namespace IC2.Data;

/// <summary>One broken gate-2 rule: the rule's name (as the upstream checker names it) and where.</summary>
public sealed record OriginalSavePolicyViolation(string Rule, string Where);

/// <summary>
/// T184/T185: the gate-2 writer policy's refusals and the SAV atlas's cross-record invariants, checked on a
/// document before it is encoded.
/// </summary>
/// <remarks>
/// <para>Ported rule by rule from ic2-conquest's <c>scripts/sav_export_policy_check.py</c> (checker
/// <c>eaee1ef0…</c>, with the <c>city_allegiance_in_range</c> rule) and <c>scripts/sav_atlas_invariants.py</c>,
/// as promoted in research <c>f878614</c> (<c>2026-10-22-sav-export-gate2-policy.md</c>) and the atlas
/// increments 7–10 (research <c>f83a886</c>). The two drifts the original itself writes are allowed, as
/// upstream allows them: an army marker whose troop band lags, and an eliminated nation's stale city count.
/// The text-tail and empty-slot rules need no check here: <see cref="OriginalSaveCodec.Encode"/> writes the
/// canonical form by construction.</para>
/// <para>The upstream checker stays an independent oracle on encoded bytes; this port is what the exporter
/// runs before it writes.</para>
/// </remarks>
public static class OriginalSaveExportPolicy
{
    private const int Width = OriginalSaveDocument.MapWidth, Height = OriginalSaveDocument.MapHeight;

    /// <summary>Every violation in <paramref name="d"/>, in a stable order; empty when the document may be
    /// written. <paramref name="fromScratch"/> requires the INERT bytes to be zero (a native-state export);
    /// with a source they may be carried.</summary>
    public static IReadOnlyList<OriginalSavePolicyViolation> Check(OriginalSaveDocument d, bool fromScratch = true)
    {
        ArgumentNullException.ThrowIfNull(d);
        var v = new List<OriginalSavePolicyViolation>();
        void Add(string rule, string where) => v.Add(new(rule, where));
        bool OnMap(int x, int y) => x is >= 0 and < Width && y is >= 0 and < Height;

        for (var c = 0; c < d.Cities.Count; c++)
        {
            var city = d.Cities[c];
            if (city.Owner is < 0 or > 15) Add("city_owner_in_range", $"city[{c}]");
            if (city.Allegiance is < 0 or > 15) Add("city_allegiance_in_range", $"city[{c}]");
            var m = OnMap(city.X, city.Y) ? d.CellAt(city.X, city.Y) : -1;
            if (m is < 20 or > 99 || (m - 20) % 16 != city.Owner) Add("city_marker", $"city[{c}]");
            if (city.Fort < 0 || (city.Fort > 100 && city.Fort / 100 + city.Fort % 100 > 100)) Add("fort_encoding", $"city[{c}]");
        }

        var cityAt = new Dictionary<(int, int), int>();
        foreach (var city in d.Cities) cityAt.TryAdd((city.X, city.Y), city.Owner);

        for (var a = 0; a < d.Armies.Count; a++)
        {
            var army = d.Armies[a];
            if (army.Owner is < 0 or > 15) { Add("no_army_tombstones", $"army[{a}]"); continue; }
            if (army.Cell != -1)
            {
                if (cityAt.TryGetValue((army.X, army.Y), out var cityOwner))
                {
                    if (army.Cell is < 20 or > 99 || (army.Cell - 20) % 16 != cityOwner) Add("army_cell_terrain_or_city", $"army[{a}]");
                }
                else if (army.Cell is < 0 or > 11) Add("army_cell_terrain_or_city", $"army[{a}]");

                var m = OnMap(army.X, army.Y) ? d.CellAt(army.X, army.Y) : -1;
                if (m is < 200 or > 247 || (m - 200) % 16 != army.Owner) Add("army_marker", $"army[{a}]");
            }
            else
            {
                var carriers = 0;
                foreach (var f in d.Fleets)
                    if (f.Owner >= 0 && f.Army == a) carriers++;
                if (carriers != 1) Add("aboard_link_army_side", $"army[{a}]");
            }
        }

        for (var j = 0; j < d.Fleets.Count; j++)
        {
            var f = d.Fleets[j];
            if (f.Owner is < 0 or > 15) { Add("no_fleet_tombstones", $"fleet[{j}]"); continue; }
            if (f.Countdown != -1 && (f.Countdown is < 0 or > 24 || f.Countdown % 2 != 0)) Add("fleet_countdown", $"fleet[{j}]");
            if (f.Countdown >= 0)
            {
                if (f.CityOrCondition is < 0 or >= OriginalSaveDocument.CityCount) Add("fleet_building_city", $"fleet[{j}]");
                if (fromScratch && (f.Moves != 0 || f.RouteX != -1)) Add("building_fleet_moves_0_route_x_minus1", $"fleet[{j}]");
            }
            else
            {
                if (f.CityOrCondition is < 0 or > 100) Add("fleet_condition", $"fleet[{j}]");
                var band = f.Ships < 25 ? 300 : f.Ships < 50 ? 316 : 332;
                if (!OnMap(f.X, f.Y) || d.CellAt(f.X, f.Y) != f.Owner + band) Add("fleet_marker", $"fleet[{j}]");
            }

            if (f.Army >= 0)
            {
                if (f.Army >= d.Armies.Count || d.Armies[f.Army].Owner != f.Owner || d.Armies[f.Army].Cell != -1)
                    Add("aboard_link_fleet_side", $"fleet[{j}]");
                else if (d.Armies[f.Army].X != f.X || d.Armies[f.Army].Y != f.Y)
                    Add("carried_army_on_fleet_tile", $"fleet[{j}]");
            }
        }

        var owned = new List<int>[OriginalSaveDocument.NationCount];
        for (var n = 0; n < owned.Length; n++) owned[n] = [];
        for (var c = 0; c < d.Cities.Count; c++)
            if (d.Cities[c].Owner is >= 0 and <= 15) owned[d.Cities[c].Owner].Add(c);

        for (var n = 0; n < d.Nations.Count; n++)
        {
            var nation = d.Nations[n];
            var list = nation.CityList;
            if (!new HashSet<int>(list).SetEquals(owned[n]) || list.Count != owned[n].Count) Add("city_list_equals_owned", $"nation[{n}]");
            for (var e = 1; e < list.Count; e++)
                if (string.CompareOrdinal(d.Cities[list[e - 1]].Name, d.Cities[list[e]].Name) > 0)
                {
                    Add("city_list_sorted", $"nation[{n}]");
                    break;
                }

            // An eliminated nation (capital −1) may keep a stale count: the original writes one.
            if (nation.Capital >= 0)
            {
                if (nation.Cities != list.Count) Add("city_count", $"nation[{n}]");
                if (nation.Capital >= OriginalSaveDocument.CityCount || d.Cities[nation.Capital].Owner != n)
                    Add("capital_owned", $"nation[{n}]");
            }

            for (var e = 0; e < nation.Recruits.Count; e++)
                if (nation.Recruits[e].Troops > 0 && nation.Recruits[e].City is < 0 or >= OriginalSaveDocument.CityCount)
                    Add("recruit_city_index", $"nation[{n}].recruit[{e}]");

            if (fromScratch && nation.Inert is { } inert
                && (inert.Block27.Any(b => b != 0) || inert.Byte46D != 0 || inert.Block491.Any(b => b != 0)))
                Add("inert_bytes_carried_or_zero", $"nation[{n}]");
        }

        var cityTiles = new HashSet<(int, int)>(d.Cities.Select(c => (c.X, c.Y)));
        for (var s = 0; s < d.Mercenaries.Count; s++)
        {
            var m = d.Mercenaries[s];
            if (m.Troops >= 0 && !cityTiles.Contains((m.X, m.Y))) Add("merc_live_on_city", $"mercenary[{s}]");
        }

        return v.AsReadOnly();
    }
}
